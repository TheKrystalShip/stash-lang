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
/// Integration tests for <c>GET /api/v1/packages/{scope}/{name}/readme</c>.
/// </summary>
/// <remarks>
/// <para>
/// Covers the three load-bearing visibility rows:
/// <list type="bullet">
///   <item>Anonymous caller on private package → 404.</item>
///   <item>Authenticated caller without a role on private package → 404.</item>
///   <item>Authenticated caller with reader-or-above role on private package → 200 with readme content.</item>
/// </list>
/// Also covers the no-README case (→ 404), README content returned verbatim, ETag /
/// Last-Modified / Cache-Control header emission, and conditional requests returning 304
/// via <c>If-None-Match</c> and <c>If-Modified-Since</c>.
/// </para>
/// <para>
/// URL note: the route template is <c>{scope}/{name}</c> where <c>scope</c> does NOT
/// include the leading <c>@</c> — the controller's
/// <see cref="Stash.Registry.Auth.Authorization.PackageRoute.From"/> constructs the full
/// name as <c>@{scope}/{name}</c>. All test URLs use bare scope names
/// (e.g. <c>rm-owner-a/priv-pkg/readme</c>, not <c>@rm-owner-a/priv-pkg/readme</c>).
/// </para>
/// </remarks>
public sealed class ReadmeEndpointTests : RegistryAuthzTestBase
{
    // ── Helper: set README directly on an existing package record ────────────

    private static async Task SeedReadmeAsync(
        WebApplicationFactory<Stash.Registry.Program> factory,
        string packageName,
        string readme = "# Test README\n\nSome content.")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        await db.UpdatePackageReadmeAsync(packageName, readme);
    }

    // ── Visibility tests ──────────────────────────────────────────────────────

    /// <summary>
    /// An anonymous caller requesting the readme for a private package must receive 404.
    /// The package must not be revealed (not 200, not 403).
    /// </summary>
    [Fact]
    public async Task GetReadme_AnonymousCaller_PrivatePackage_Returns404()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string adminToken = await RegisterAndGetTokenAsync(client, "rm-admin-a");
        string ownerToken = await RegisterAndGetTokenAsync(client, "rm-owner-a");

        await SeedScopeAsync(ctx.Factory, "rm-owner-a", "rm-owner-a");
        await SeedPackageAsync(ctx.Factory, "@rm-owner-a/priv-pkg", "rm-owner-a", visibility: "private");
        await SeedReadmeAsync(ctx.Factory, "@rm-owner-a/priv-pkg");

        // Anonymous request (no Authorization header).
        var response = await client.GetAsync("/api/v1/packages/rm-owner-a/priv-pkg/readme");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// An authenticated caller who holds no role on a private package must receive 404.
    /// </summary>
    [Fact]
    public async Task GetReadme_AuthenticatedNoRole_PrivatePackage_Returns404()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string adminToken = await RegisterAndGetTokenAsync(client, "rm-admin-b");
        string ownerToken = await RegisterAndGetTokenAsync(client, "rm-owner-b");
        string noneToken = await RegisterAndGetTokenAsync(client, "rm-none-b");

        await SeedScopeAsync(ctx.Factory, "rm-owner-b", "rm-owner-b");
        await SeedPackageAsync(ctx.Factory, "@rm-owner-b/priv-pkg", "rm-owner-b", visibility: "private");
        await SeedReadmeAsync(ctx.Factory, "@rm-owner-b/priv-pkg");

        // Authenticated as rm-none-b — has no role on the package.
        SetBearer(client, noneToken);
        var response = await client.GetAsync("/api/v1/packages/rm-owner-b/priv-pkg/readme");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// An authenticated caller with a reader (or above) role on a private package must receive 200
    /// with the readme content.
    /// </summary>
    [Fact]
    public async Task GetReadme_AuthenticatedWithReaderRole_PrivatePackage_Returns200()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string adminToken = await RegisterAndGetTokenAsync(client, "rm-admin-c");
        string ownerToken = await RegisterAndGetTokenAsync(client, "rm-owner-c");
        string readerToken = await RegisterAndGetTokenAsync(client, "rm-reader-c");

        await SeedScopeAsync(ctx.Factory, "rm-owner-c", "rm-owner-c");
        await SeedPackageAsync(ctx.Factory, "@rm-owner-c/priv-pkg", "rm-owner-c", visibility: "private");
        await SeedReadmeAsync(ctx.Factory, "@rm-owner-c/priv-pkg", "# Private Readme");

        // Grant reader role to rm-reader-c.
        await SeedPackageRoleAsync(ctx.Factory, "@rm-owner-c/priv-pkg", "user", "rm-reader-c", "reader");

        SetBearer(client, readerToken);
        var response = await client.GetAsync("/api/v1/packages/rm-owner-c/priv-pkg/readme");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        // Verify envelope shape.
        Assert.True(doc.RootElement.TryGetProperty("content", out _));
        Assert.True(doc.RootElement.TryGetProperty("contentType", out var contentType));
        Assert.True(doc.RootElement.TryGetProperty("byteSize", out _));

        Assert.Equal("text/markdown", contentType.GetString());
    }

    // ── No-README case ────────────────────────────────────────────────────────

    /// <summary>
    /// A package with no README (null Readme field) returns 404 — the readme resource does
    /// not exist.
    /// </summary>
    [Fact]
    public async Task GetReadme_NoReadme_Returns404()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "rm-owner-d");
        await SeedScopeAsync(ctx.Factory, "rm-owner-d", "rm-owner-d");
        // SeedPackageAsync does NOT set a README — package.Readme is null.
        await SeedPackageAsync(ctx.Factory, "@rm-owner-d/pub-pkg", "rm-owner-d", visibility: "public");

        var response = await client.GetAsync("/api/v1/packages/rm-owner-d/pub-pkg/readme");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Public package baseline ───────────────────────────────────────────────

    /// <summary>
    /// An anonymous caller on a public package with a README gets 200 with the readme body.
    /// </summary>
    [Fact]
    public async Task GetReadme_AnonymousCaller_PublicPackage_Returns200()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "rm-pub-e");
        await SeedScopeAsync(ctx.Factory, "rm-pub-e", "rm-pub-e");
        await SeedPackageAsync(ctx.Factory, "@rm-pub-e/pub-pkg", "rm-pub-e", visibility: "public");
        await SeedReadmeAsync(ctx.Factory, "@rm-pub-e/pub-pkg", "# Hello World");

        var response = await client.GetAsync("/api/v1/packages/rm-pub-e/pub-pkg/readme");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("# Hello World", doc.RootElement.GetProperty("content").GetString());
    }

    // ── Content returned verbatim ─────────────────────────────────────────────

    /// <summary>
    /// The <c>content</c> field must carry the raw markdown verbatim — no sanitization or
    /// rendering applied by the registry.
    /// </summary>
    [Fact]
    public async Task GetReadme_ReturnsContentVerbatim()
    {
        const string rawMarkdown = "# My Package\n\n<script>alert(1)</script>\n\n> Note: raw content.";

        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "rm-owner-f");
        await SeedScopeAsync(ctx.Factory, "rm-owner-f", "rm-owner-f");
        await SeedPackageAsync(ctx.Factory, "@rm-owner-f/pub-pkg", "rm-owner-f", visibility: "public");
        await SeedReadmeAsync(ctx.Factory, "@rm-owner-f/pub-pkg", rawMarkdown);

        var response = await client.GetAsync("/api/v1/packages/rm-owner-f/pub-pkg/readme");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(rawMarkdown, doc.RootElement.GetProperty("content").GetString());
    }

    // ── byteSize field ────────────────────────────────────────────────────────

    /// <summary>
    /// The <c>byteSize</c> field must equal the UTF-8 byte length of the content.
    /// </summary>
    [Fact]
    public async Task GetReadme_ByteSize_EqualsUtf8Length()
    {
        const string content = "# Readme\n\nHello.";
        int expectedByteSize = System.Text.Encoding.UTF8.GetByteCount(content);

        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "rm-owner-g");
        await SeedScopeAsync(ctx.Factory, "rm-owner-g", "rm-owner-g");
        await SeedPackageAsync(ctx.Factory, "@rm-owner-g/pub-pkg", "rm-owner-g", visibility: "public");
        await SeedReadmeAsync(ctx.Factory, "@rm-owner-g/pub-pkg", content);

        var response = await client.GetAsync("/api/v1/packages/rm-owner-g/pub-pkg/readme");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(expectedByteSize, doc.RootElement.GetProperty("byteSize").GetInt32());
    }

    // ── extractedFromVersion field ────────────────────────────────────────────

    /// <summary>
    /// The <c>extractedFromVersion</c> field must equal the package's <c>latest</c> version.
    /// </summary>
    [Fact]
    public async Task GetReadme_ExtractedFromVersion_EqualsLatest()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "rm-owner-h");
        await SeedScopeAsync(ctx.Factory, "rm-owner-h", "rm-owner-h");
        // SeedPackageAsync sets Latest = "1.0.0" (default).
        await SeedPackageAsync(ctx.Factory, "@rm-owner-h/pub-pkg", "rm-owner-h", visibility: "public");
        await SeedReadmeAsync(ctx.Factory, "@rm-owner-h/pub-pkg");

        var response = await client.GetAsync("/api/v1/packages/rm-owner-h/pub-pkg/readme");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("1.0.0", doc.RootElement.GetProperty("extractedFromVersion").GetString());
    }

    // ── Caching headers ───────────────────────────────────────────────────────

    /// <summary>
    /// A 200 response emits ETag, Last-Modified, and Cache-Control headers.
    /// </summary>
    [Fact]
    public async Task GetReadme_Returns200_IncludesCachingHeaders()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "rm-cache-i");
        await SeedScopeAsync(ctx.Factory, "rm-cache-i", "rm-cache-i");
        await SeedPackageAsync(ctx.Factory, "@rm-cache-i/pub-pkg", "rm-cache-i", visibility: "public");
        await SeedReadmeAsync(ctx.Factory, "@rm-cache-i/pub-pkg");

        var response = await client.GetAsync("/api/v1/packages/rm-cache-i/pub-pkg/readme");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.ETag != null, "ETag header must be present on 200 response");
        Assert.True(response.Content.Headers.LastModified.HasValue, "Last-Modified header must be present on 200 response");

        bool hasCacheControl =
            response.Headers.TryGetValues("Cache-Control", out _) ||
            response.Content.Headers.TryGetValues("Cache-Control", out _);
        Assert.True(hasCacheControl, "Cache-Control header must be present on 200 response");
    }

    // ── Conditional requests ──────────────────────────────────────────────────

    /// <summary>
    /// A request with a matching If-None-Match header returns 304 with no body.
    /// </summary>
    [Fact]
    public async Task GetReadme_IfNoneMatch_MatchingETag_Returns304()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "rm-cond-j");
        await SeedScopeAsync(ctx.Factory, "rm-cond-j", "rm-cond-j");
        await SeedPackageAsync(ctx.Factory, "@rm-cond-j/pub-pkg", "rm-cond-j", visibility: "public");
        await SeedReadmeAsync(ctx.Factory, "@rm-cond-j/pub-pkg");

        // First request — obtain ETag.
        var firstResponse = await client.GetAsync("/api/v1/packages/rm-cond-j/pub-pkg/readme");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(firstResponse.Headers.ETag);

        string etag = firstResponse.Headers.ETag!.ToString();

        // Second request with matching If-None-Match.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/packages/rm-cond-j/pub-pkg/readme");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var conditionalResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, conditionalResponse.StatusCode);
    }

    /// <summary>
    /// A request with If-Modified-Since equal to (or after) Last-Modified returns 304.
    /// </summary>
    [Fact]
    public async Task GetReadme_IfModifiedSince_NotModified_Returns304()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "rm-cond-k");
        await SeedScopeAsync(ctx.Factory, "rm-cond-k", "rm-cond-k");
        await SeedPackageAsync(ctx.Factory, "@rm-cond-k/pub-pkg", "rm-cond-k", visibility: "public");
        await SeedReadmeAsync(ctx.Factory, "@rm-cond-k/pub-pkg");

        // First request — obtain Last-Modified.
        var firstResponse = await client.GetAsync("/api/v1/packages/rm-cond-k/pub-pkg/readme");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.True(firstResponse.Content.Headers.LastModified.HasValue);

        DateTimeOffset lastModified = firstResponse.Content.Headers.LastModified!.Value;

        // Second request with If-Modified-Since equal to Last-Modified (same second = not modified).
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/packages/rm-cond-k/pub-pkg/readme");
        request.Headers.IfModifiedSince = lastModified;
        var conditionalResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, conditionalResponse.StatusCode);
    }

    /// <summary>
    /// A request with If-None-Match that does NOT match returns 200 with a body.
    /// </summary>
    [Fact]
    public async Task GetReadme_IfNoneMatch_StaleETag_Returns200()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "rm-cond-l");
        await SeedScopeAsync(ctx.Factory, "rm-cond-l", "rm-cond-l");
        await SeedPackageAsync(ctx.Factory, "@rm-cond-l/pub-pkg", "rm-cond-l", visibility: "public");
        await SeedReadmeAsync(ctx.Factory, "@rm-cond-l/pub-pkg");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/packages/rm-cond-l/pub-pkg/readme");
        request.Headers.TryAddWithoutValidation("If-None-Match", "W/\"0-0\"");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── ETag is weak ──────────────────────────────────────────────────────────

    /// <summary>
    /// The ETag emitted on a 200 response is a weak ETag (prefixed with <c>W/</c>).
    /// </summary>
    [Fact]
    public async Task GetReadme_Returns200_WeakETag()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(client, "rm-etag-m");
        await SeedScopeAsync(ctx.Factory, "rm-etag-m", "rm-etag-m");
        await SeedPackageAsync(ctx.Factory, "@rm-etag-m/pub-pkg", "rm-etag-m", visibility: "public");
        await SeedReadmeAsync(ctx.Factory, "@rm-etag-m/pub-pkg");

        var response = await client.GetAsync("/api/v1/packages/rm-etag-m/pub-pkg/readme");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string etagHeader = response.Headers.ETag!.ToString();
        Assert.StartsWith("W/", etagHeader);
    }
}
