using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Tests.Registry.Authz;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Integration tests for <c>GET /api/v1/packages/{scope}/{name}/versions</c>.
/// </summary>
/// <remarks>
/// <para>
/// Covers the three load-bearing visibility rows:
/// <list type="bullet">
///   <item>Anonymous caller on private package → 404.</item>
///   <item>Authenticated caller without a role on private package → 404.</item>
///   <item>Authenticated caller with reader-or-above role on private package → 200 with versions list.</item>
/// </list>
/// Also covers ETag / Last-Modified / Cache-Control header emission, and conditional
/// requests returning 304 via <c>If-None-Match</c> and <c>If-Modified-Since</c>.
/// </para>
/// <para>
/// URL note: the route template is <c>{scope}/{name}</c> where <c>scope</c> does NOT
/// include the leading <c>@</c> — the controller's <see cref="Stash.Registry.Auth.Authorization.PackageRoute.From"/>
/// constructs the full name as <c>@{scope}/{name}</c>.  All test URLs use bare scope names
/// (e.g. <c>vis-owner-a/priv-pkg/versions</c>, not <c>@vis-owner-a/priv-pkg/versions</c>).
/// </para>
/// </remarks>
public sealed class VersionsEndpointTests : RegistryAuthzTestBase
{
    // ── Helper: seed a version record directly into the DB ──────────────────────

    private static async Task SeedVersionAsync(
        WebApplicationFactory<Stash.Registry.Program> factory,
        string packageName,
        string version = "1.0.0")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        bool exists = await db.VersionExistsAsync(packageName, version);
        if (!exists)
        {
            await db.AddVersionAsync(packageName, new VersionRecord
            {
                PackageName = packageName,
                Version = version,
                Integrity = "sha256-test",
                PublishedAt = DateTime.UtcNow,
                PublishedBy = "seeder"
            });
        }
    }

    // ── Visibility tests ─────────────────────────────────────────────────────────

    /// <summary>
    /// An anonymous caller requesting versions for a private package must receive 404.
    /// The package must not be revealed (not 200, not 403).
    /// </summary>
    [Fact]
    public async Task GetVersions_AnonymousCaller_PrivatePackage_Returns404()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        // Register first user (becomes admin), then a second user as the package owner.
        string adminToken = await RegisterAndGetTokenAsync(client, "vis-admin-a");
        string ownerToken = await RegisterAndGetTokenAsync(client, "vis-owner-a");

        await SeedScopeAsync(ctx.Factory, "vis-owner-a", "vis-owner-a");
        // Package stored as "@vis-owner-a/priv-pkg"; URL scope segment is "vis-owner-a" (no @).
        await SeedPackageAsync(ctx.Factory, "@vis-owner-a/priv-pkg", "vis-owner-a", visibility: "private");
        await SeedVersionAsync(ctx.Factory, "@vis-owner-a/priv-pkg");

        // Anonymous request (no Authorization header).
        var response = await client.GetAsync("/api/v1/packages/vis-owner-a/priv-pkg/versions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// An authenticated caller who holds no role on a private package must receive 404.
    /// </summary>
    [Fact]
    public async Task GetVersions_AuthenticatedNoRole_PrivatePackage_Returns404()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        // First user becomes admin.
        string adminToken = await RegisterAndGetTokenAsync(client, "vis-admin-b");
        // Second user owns the package.
        string ownerToken = await RegisterAndGetTokenAsync(client, "vis-owner-b");
        // Third user has no role on the package.
        string noneToken = await RegisterAndGetTokenAsync(client, "vis-none-b");

        await SeedScopeAsync(ctx.Factory, "vis-owner-b", "vis-owner-b");
        await SeedPackageAsync(ctx.Factory, "@vis-owner-b/priv-pkg", "vis-owner-b", visibility: "private");
        await SeedVersionAsync(ctx.Factory, "@vis-owner-b/priv-pkg");

        // Authenticated as vis-none-b — has no role on the package.
        SetBearer(client, noneToken);
        var response = await client.GetAsync("/api/v1/packages/vis-owner-b/priv-pkg/versions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// An authenticated caller with a reader (or above) role on a private package must receive 200
    /// with the versions list.
    /// </summary>
    [Fact]
    public async Task GetVersions_AuthenticatedWithReaderRole_PrivatePackage_Returns200()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        // First user becomes admin.
        string adminToken = await RegisterAndGetTokenAsync(client, "vis-admin-c");
        string ownerToken = await RegisterAndGetTokenAsync(client, "vis-owner-c");
        string readerToken = await RegisterAndGetTokenAsync(client, "vis-reader-c");

        await SeedScopeAsync(ctx.Factory, "vis-owner-c", "vis-owner-c");
        await SeedPackageAsync(ctx.Factory, "@vis-owner-c/priv-pkg", "vis-owner-c", visibility: "private");
        await SeedVersionAsync(ctx.Factory, "@vis-owner-c/priv-pkg", "1.0.0");

        // Grant reader role to vis-reader-c.
        await SeedPackageRoleAsync(ctx.Factory, "@vis-owner-c/priv-pkg", "user", "vis-reader-c", "reader");

        SetBearer(client, readerToken);
        var response = await client.GetAsync("/api/v1/packages/vis-owner-c/priv-pkg/versions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        // Verify envelope shape.
        Assert.True(doc.RootElement.TryGetProperty("items", out var items));
        Assert.True(doc.RootElement.TryGetProperty("totalCount", out var totalCount));
        Assert.True(doc.RootElement.TryGetProperty("page", out _));
        Assert.True(doc.RootElement.TryGetProperty("pageSize", out _));
        Assert.True(doc.RootElement.TryGetProperty("totalPages", out _));

        // The seeded version must appear.
        Assert.Equal(1, totalCount.GetInt32());
        Assert.Equal(1, items.GetArrayLength());
    }

    // ── Public package baseline ─────────────────────────────────────────────────

    /// <summary>
    /// An anonymous caller on a public package gets 200 with the versions list.
    /// </summary>
    [Fact]
    public async Task GetVersions_AnonymousCaller_PublicPackage_Returns200()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "vis-pub-d");
        await SeedScopeAsync(ctx.Factory, "vis-pub-d", "vis-pub-d");
        await SeedPackageAsync(ctx.Factory, "@vis-pub-d/pub-pkg", "vis-pub-d", visibility: "public");
        await SeedVersionAsync(ctx.Factory, "@vis-pub-d/pub-pkg", "1.0.0");
        await SeedVersionAsync(ctx.Factory, "@vis-pub-d/pub-pkg", "2.0.0");

        var response = await client.GetAsync("/api/v1/packages/vis-pub-d/pub-pkg/versions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(2, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    // ── Caching headers ─────────────────────────────────────────────────────────

    /// <summary>
    /// A 200 response emits ETag, Last-Modified, and Cache-Control headers.
    /// </summary>
    [Fact]
    public async Task GetVersions_Returns200_IncludesCachingHeaders()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "cache-e");
        await SeedScopeAsync(ctx.Factory, "cache-e", "cache-e");
        await SeedPackageAsync(ctx.Factory, "@cache-e/pub-pkg", "cache-e", visibility: "public");
        await SeedVersionAsync(ctx.Factory, "@cache-e/pub-pkg");

        var response = await client.GetAsync("/api/v1/packages/cache-e/pub-pkg/versions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.ETag != null, "ETag header must be present on 200 response");
        Assert.True(response.Content.Headers.LastModified.HasValue, "Last-Modified header must be present on 200 response");

        // Cache-Control may be in response headers or content headers.
        bool hasCacheControl =
            response.Headers.TryGetValues("Cache-Control", out _) ||
            response.Content.Headers.TryGetValues("Cache-Control", out _);
        Assert.True(hasCacheControl, "Cache-Control header must be present on 200 response");
    }

    // ── Conditional requests ─────────────────────────────────────────────────────

    /// <summary>
    /// A request with a matching If-None-Match header returns 304 with no body.
    /// </summary>
    [Fact]
    public async Task GetVersions_IfNoneMatch_MatchingETag_Returns304()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "cond-f");
        await SeedScopeAsync(ctx.Factory, "cond-f", "cond-f");
        await SeedPackageAsync(ctx.Factory, "@cond-f/pub-pkg", "cond-f", visibility: "public");
        await SeedVersionAsync(ctx.Factory, "@cond-f/pub-pkg");

        // First request — obtain ETag.
        var firstResponse = await client.GetAsync("/api/v1/packages/cond-f/pub-pkg/versions");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(firstResponse.Headers.ETag);

        string etag = firstResponse.Headers.ETag!.ToString();

        // Second request with matching If-None-Match.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/packages/cond-f/pub-pkg/versions");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var conditionalResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, conditionalResponse.StatusCode);
    }

    /// <summary>
    /// A request with If-Modified-Since equal to (or after) Last-Modified returns 304.
    /// </summary>
    [Fact]
    public async Task GetVersions_IfModifiedSince_NotModified_Returns304()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "cond-g");
        await SeedScopeAsync(ctx.Factory, "cond-g", "cond-g");
        await SeedPackageAsync(ctx.Factory, "@cond-g/pub-pkg", "cond-g", visibility: "public");
        await SeedVersionAsync(ctx.Factory, "@cond-g/pub-pkg");

        // First request — obtain Last-Modified.
        var firstResponse = await client.GetAsync("/api/v1/packages/cond-g/pub-pkg/versions");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.True(firstResponse.Content.Headers.LastModified.HasValue);

        DateTimeOffset lastModified = firstResponse.Content.Headers.LastModified!.Value;

        // Second request with If-Modified-Since equal to Last-Modified (same second = not modified).
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/packages/cond-g/pub-pkg/versions");
        request.Headers.IfModifiedSince = lastModified;
        var conditionalResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, conditionalResponse.StatusCode);
    }

    /// <summary>
    /// A request with If-None-Match that does NOT match returns 200 with a body.
    /// </summary>
    [Fact]
    public async Task GetVersions_IfNoneMatch_StaleETag_Returns200()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "cond-h");
        await SeedScopeAsync(ctx.Factory, "cond-h", "cond-h");
        await SeedPackageAsync(ctx.Factory, "@cond-h/pub-pkg", "cond-h", visibility: "public");
        await SeedVersionAsync(ctx.Factory, "@cond-h/pub-pkg");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/packages/cond-h/pub-pkg/versions");
        request.Headers.TryAddWithoutValidation("If-None-Match", "W/\"0-0\"");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Pagination ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Pagination parameters are honored: page and pageSize control the result window.
    /// </summary>
    [Fact]
    public async Task GetVersions_Pagination_ReturnsCorrectSlice()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "pag-i");
        await SeedScopeAsync(ctx.Factory, "pag-i", "pag-i");
        await SeedPackageAsync(ctx.Factory, "@pag-i/pub-pkg", "pag-i", visibility: "public");

        // Seed 3 versions.
        await SeedVersionAsync(ctx.Factory, "@pag-i/pub-pkg", "1.0.0");
        await SeedVersionAsync(ctx.Factory, "@pag-i/pub-pkg", "2.0.0");
        await SeedVersionAsync(ctx.Factory, "@pag-i/pub-pkg", "3.0.0");

        // Fetch page 1 with pageSize=2.
        var response = await client.GetAsync("/api/v1/packages/pag-i/pub-pkg/versions?page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(3, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(2, doc.RootElement.GetProperty("totalPages").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    // ── ETag is weak ───────────────────────────────────────────────────────────

    /// <summary>
    /// The ETag emitted on a 200 response is a weak ETag (prefixed with <c>W/</c>).
    /// </summary>
    [Fact]
    public async Task GetVersions_Returns200_WeakETag()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "etag-j");
        await SeedScopeAsync(ctx.Factory, "etag-j", "etag-j");
        await SeedPackageAsync(ctx.Factory, "@etag-j/pub-pkg", "etag-j", visibility: "public");
        await SeedVersionAsync(ctx.Factory, "@etag-j/pub-pkg");

        var response = await client.GetAsync("/api/v1/packages/etag-j/pub-pkg/versions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string etagHeader = response.Headers.ETag!.ToString();
        Assert.StartsWith("W/", etagHeader);
    }

    // ── ETag invalidation on version deprecate/undeprecate ─────────────────────

    /// <summary>
    /// After PATCH …/{version}/deprecate the /versions ETag must change so that
    /// a client sending the pre-deprecation ETag via If-None-Match receives 200
    /// (with the version now showing deprecated = true), not a stale 304.
    /// </summary>
    [Fact]
    public async Task DeprecateVersion_ThenIfNoneMatchPriorETag_Returns200WithDeprecatedState()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        // The owner also gets a publish-ceiling token (RegisterAndGetTokenAsync always does).
        string ownerToken = await RegisterAndGetTokenAsync(client, "etag-dep-k");
        await SeedScopeAsync(ctx.Factory, "etag-dep-k", "etag-dep-k");
        await SeedPackageAsync(ctx.Factory, "@etag-dep-k/pkg", "etag-dep-k", visibility: "public");
        await SeedVersionAsync(ctx.Factory, "@etag-dep-k/pkg", "1.0.0");

        // Step 1: fetch /versions and capture the pre-deprecation ETag.
        var firstResponse = await client.GetAsync("/api/v1/packages/etag-dep-k/pkg/versions");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(firstResponse.Headers.ETag);
        string oldEtag = firstResponse.Headers.ETag!.ToString();

        // Step 2: deprecate version 1.0.0 (owner has the publish-ceiling token).
        SetBearer(client, ownerToken);
        var patchResp = await client.PatchAsync(
            "/api/v1/packages/etag-dep-k/pkg/1.0.0/deprecate",
            Json(new { message = "Deprecated for testing." }));
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);

        // Step 3: fetch /versions with the pre-deprecation ETag — must return 200
        // (not 304) because the ETag has been invalidated by the deprecation.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/packages/etag-dep-k/pkg/versions");
        request.Headers.TryAddWithoutValidation("If-None-Match", oldEtag);
        var conditionalResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, conditionalResponse.StatusCode);

        // The response must carry a new (different) ETag.
        Assert.NotNull(conditionalResponse.Headers.ETag);
        string newEtag = conditionalResponse.Headers.ETag!.ToString();
        Assert.NotEqual(oldEtag, newEtag);

        // The body must show the version as deprecated.
        var body = await conditionalResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        var v = items[0];
        Assert.True(v.GetProperty("deprecated").GetBoolean(), "Version must be deprecated in the response body.");
    }

    /// <summary>
    /// After DELETE …/{version}/deprecate (undeprecate) the /versions ETag must change
    /// so that a client sending the pre-undeprecation ETag via If-None-Match receives 200
    /// (with the version now showing deprecated = false), not a stale 304.
    /// </summary>
    [Fact]
    public async Task UndeprecateVersion_ThenIfNoneMatchPriorETag_Returns200WithUndeprecatedState()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "etag-undep-l");
        await SeedScopeAsync(ctx.Factory, "etag-undep-l", "etag-undep-l");
        await SeedPackageAsync(ctx.Factory, "@etag-undep-l/pkg", "etag-undep-l", visibility: "public");
        await SeedVersionAsync(ctx.Factory, "@etag-undep-l/pkg", "1.0.0");

        // Pre-condition: deprecate the version first.
        SetBearer(client, ownerToken);
        var deprecateResp = await client.PatchAsync(
            "/api/v1/packages/etag-undep-l/pkg/1.0.0/deprecate",
            Json(new { message = "Initially deprecated." }));
        Assert.Equal(HttpStatusCode.OK, deprecateResp.StatusCode);

        // Step 1: fetch /versions in the deprecated state and capture the ETag.
        var firstResponse = await client.GetAsync("/api/v1/packages/etag-undep-l/pkg/versions");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(firstResponse.Headers.ETag);
        string oldEtag = firstResponse.Headers.ETag!.ToString();

        // Verify the version shows deprecated = true before the undeprecation.
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        using var firstDoc = JsonDocument.Parse(firstBody);
        Assert.True(firstDoc.RootElement.GetProperty("items")[0].GetProperty("deprecated").GetBoolean());

        // Step 2: undeprecate version 1.0.0.
        var undeprecateResp = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/api/v1/packages/etag-undep-l/pkg/1.0.0/deprecate"));
        Assert.Equal(HttpStatusCode.OK, undeprecateResp.StatusCode);

        // Step 3: fetch /versions with the pre-undeprecation ETag — must return 200.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/packages/etag-undep-l/pkg/versions");
        request.Headers.TryAddWithoutValidation("If-None-Match", oldEtag);
        var conditionalResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, conditionalResponse.StatusCode);

        // The response must carry a new (different) ETag.
        Assert.NotNull(conditionalResponse.Headers.ETag);
        string newEtag = conditionalResponse.Headers.ETag!.ToString();
        Assert.NotEqual(oldEtag, newEtag);

        // The body must show the version as no longer deprecated.
        var body = await conditionalResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        var v = items[0];
        Assert.False(v.GetProperty("deprecated").GetBoolean(), "Version must not be deprecated in the response body.");
    }
}
