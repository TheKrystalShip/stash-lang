using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Stash.Registry.Contracts;
using Stash.Tests.Registry.Authz;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Integration tests for the <c>GET /api/v1/.well-known/registry</c> discovery endpoint.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint is public (no authentication required) and hosted as a minimal-API
/// <c>MapGet</c> — it does NOT carry <c>[RegistryAuthorize]</c> and is not subject to
/// <c>PackagesControllerRegistryAuthorizeRequiredTests</c>.
/// </para>
/// <para>
/// Key assertions:
/// <list type="bullet">
///   <item>Anonymous GET → 200 with a <c>DiscoveryResponse</c> JSON body.</item>
///   <item>Revoked-token Bearer → 200 (JTI gate is bypassed for <c>/.well-known</c> paths).</item>
///   <item><c>features.*</c> flags reflect the expected Bucket-A/Bucket-B values.</item>
///   <item><c>limits.maxPageSize</c> equals <see cref="PagingLimits.MaxPageSize"/>.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class DiscoveryEndpointTests : RegistryAuthzTestBase
{
    // ── Anonymous access ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetDiscovery_Anonymous_Returns200()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        var response = await client.GetAsync("/api/v1/.well-known/registry");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetDiscovery_Anonymous_ReturnsDiscoveryResponseBody()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        var response = await client.GetAsync("/api/v1/.well-known/registry");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Required top-level fields.
        Assert.Equal("Stash Package Registry", root.GetProperty("name").GetString());
        Assert.Equal("v1", root.GetProperty("apiVersion").GetString());
        Assert.Equal("/api/v1", root.GetProperty("basePath").GetString());

        // limits block.
        var limits = root.GetProperty("limits");
        Assert.True(limits.GetProperty("maxPackageSize").GetInt64() > 0,
            "limits.maxPackageSize must be a positive byte count.");

        // links block.
        var links = root.GetProperty("links");
        Assert.Equal("/api/v1/search", links.GetProperty("search").GetString());
        Assert.Equal("/api/v1/packages", links.GetProperty("packages").GetString());
        Assert.Equal("/openapi/v1.json", links.GetProperty("openapi").GetString());
        Assert.Equal("/api/v1/.well-known/registry", links.GetProperty("wellKnown").GetString());

        // features block must be present.
        Assert.True(root.TryGetProperty("features", out _), "features block must be present.");
    }

    // ── Bucket-B feature flags are pinned false ──────────────────────────────

    /// <summary>
    /// Pins the remaining unimplemented (Bucket-B) feature flags to false.
    /// <c>metrics</c> was removed from this set when it was promoted to Bucket-A
    /// in the <c>registry-download-metrics</c> feature (M6). The five remaining
    /// flags (<c>advisories</c>, <c>provenance</c>, <c>signatures</c>,
    /// <c>trustedPublishing</c>, <c>verifiedPublishers</c>) stay false until their
    /// respective features ship.
    /// </summary>
    [Fact]
    public async Task GetDiscovery_BucketBFlags_ArePinnedFalse()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        var response = await client.GetAsync("/api/v1/.well-known/registry");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var features = doc.RootElement.GetProperty("features");

        Assert.False(features.GetProperty("advisories").GetBoolean(),     "advisories must be false (Bucket-B).");
        Assert.False(features.GetProperty("provenance").GetBoolean(),     "provenance must be false (Bucket-B).");
        Assert.False(features.GetProperty("signatures").GetBoolean(),     "signatures must be false (Bucket-B).");
        Assert.False(features.GetProperty("trustedPublishing").GetBoolean(), "trustedPublishing must be false (Bucket-B).");
        Assert.False(features.GetProperty("verifiedPublishers").GetBoolean(), "verifiedPublishers must be false (Bucket-B).");
    }

    // ── Metrics feature flag is true (Bucket-A) ──────────────────────────────

    /// <summary>
    /// Pins <c>features.metrics = true</c> now that the <c>registry-download-metrics</c>
    /// feature is complete (M6 acceptance criterion 7).
    /// </summary>
    [Fact]
    public async Task GetDiscovery_MetricsFlag_IsTrue()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        var response = await client.GetAsync("/api/v1/.well-known/registry");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var features = doc.RootElement.GetProperty("features");

        Assert.True(features.GetProperty("metrics").GetBoolean(),
            "features.metrics must be true — the download-metrics feature is complete (M6).");
    }

    // ── Bucket-A feature flags are true ─────────────────────────────────────

    [Fact]
    public async Task GetDiscovery_BucketAFlags_AreTrue()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        var response = await client.GetAsync("/api/v1/.well-known/registry");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var features = doc.RootElement.GetProperty("features");

        Assert.True(features.GetProperty("organizations").GetBoolean(),   "organizations must be true (Bucket-A, exists today).");
        Assert.True(features.GetProperty("privatePackages").GetBoolean(), "privatePackages must be true (Bucket-A, exists today).");
    }

    // ── limits.maxPageSize equals PagingLimits.MaxPageSize const ────────────

    [Fact]
    public async Task GetDiscovery_LimitsMaxPageSize_EqualsConstant()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        var response = await client.GetAsync("/api/v1/.well-known/registry");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var limits = doc.RootElement.GetProperty("limits");

        int advertised = limits.GetProperty("maxPageSize").GetInt32();
        Assert.True(PagingLimits.MaxPageSize == advertised,
            $"The advertised limits.maxPageSize ({advertised}) must equal " +
            $"PagingLimits.MaxPageSize ({PagingLimits.MaxPageSize}). " +
            $"If the constant is changed, re-check all [Range] attributes on SearchQuery.pageSize " +
            $"and VersionsQuery.pageSize to maintain drift-impossibility.");
    }

    // ── Revoked Bearer token does NOT block the discovery endpoint ───────────

    [Fact]
    public async Task GetDiscovery_WithRevokedJwt_Returns200()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        // Register a user, issue a separate API token, then revoke it.
        string loginToken = await RegisterAndGetTokenAsync(client, "discovery-revoke-test");
        SetBearer(client, loginToken);

        var issueResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "read", expiresIn = "1d" }));
        Assert.Equal(HttpStatusCode.Created, issueResp.StatusCode);

        var issueBody = await issueResp.Content.ReadAsStringAsync();
        using var issueDoc = JsonDocument.Parse(issueBody);
        string apiToken = issueDoc.RootElement.GetProperty("token").GetString()!;
        string tokenId = issueDoc.RootElement.GetProperty("tokenId").GetString()!;

        // Revoke the token.
        var revokeResp = await client.PostAsync($"/api/v1/auth/tokens/{tokenId}/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revokeResp.StatusCode);

        // Present the revoked token to the discovery endpoint — must still return 200.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        var discoveryResp = await client.GetAsync("/api/v1/.well-known/registry");

        Assert.True(discoveryResp.StatusCode == HttpStatusCode.OK,
            "The discovery endpoint must return 200 even when a revoked Bearer token is presented. " +
            "The JTI revocation gate must be bypassed for /.well-known paths.");
    }

    // ── Garbage Bearer token does NOT block the discovery endpoint ───────────

    [Fact]
    public async Task GetDiscovery_WithGarbageBearerToken_Returns200()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        // Present a completely invalid JWT — the endpoint must still return 200.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "invalid.garbage.jwt");

        var response = await client.GetAsync("/api/v1/.well-known/registry");

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            "The discovery endpoint must return 200 even when a garbage Bearer token is presented.");
    }

    // ── cors feature flag reflects CorsConfig.Enabled ───────────────────────

    [Fact]
    public async Task GetDiscovery_CorsFeatureFlag_ReflectsConfig_Disabled()
    {
        // Default factory has Cors.Enabled = false.
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        var response = await client.GetAsync("/api/v1/.well-known/registry");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var features = doc.RootElement.GetProperty("features");

        Assert.False(features.GetProperty("cors").GetBoolean(),
            "features.cors must be false when CorsConfig.Enabled is false (the default).");
    }
}
