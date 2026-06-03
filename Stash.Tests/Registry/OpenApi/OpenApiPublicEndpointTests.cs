using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Xunit;
using Stash.Tests.Registry.Authz;

namespace Stash.Tests.Registry.OpenApi;

/// <summary>
/// Integration tests verifying the P1 requirement that <c>GET /openapi/v1.json</c> is
/// publicly accessible in both Development and Production environments, and that a caller
/// presenting a revoked JWT still receives the document.
/// </summary>
public sealed class OpenApiPublicEndpointTests : RegistryAuthzTestBase
{
    // ── Development environment ───────────────────────────────────────────────

    [Fact]
    public async Task GetOpenApiDoc_DevelopmentEnvironment_Returns200()
    {
        await using var ctx = RegistryAuthzFactory.Create(); // already uses Development
        using var client = ctx.Factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Production environment ────────────────────────────────────────────────

    [Fact]
    public async Task GetOpenApiDoc_ProductionEnvironment_Returns200()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        // Override to Production — must return 200 (the IsDevelopment() gate is dropped).
        using var prodClient = ctx.Factory
            .WithWebHostBuilder(b => b.UseEnvironment("Production"))
            .CreateClient();

        var response = await prodClient.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Revoked JWT bypass ────────────────────────────────────────────────────

    [Fact]
    public async Task GetOpenApiDoc_WithRevokedJwt_Returns200()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        // Register a user and issue a separate API token, then revoke it.
        string loginToken = await RegisterAndGetTokenAsync(client, "openapi-revoke-test");
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

        // Present the revoked token to /openapi/v1.json — must still return 200 (bypass).
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        var openApiResp = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, openApiResp.StatusCode);
    }

    // ── Revoked JWT is still rejected on other endpoints ─────────────────────

    /// <summary>
    /// Regression guard: the JTI bypass ONLY covers /openapi/* — revoked tokens
    /// must still be rejected 401 on all other paths.
    /// </summary>
    [Fact]
    public async Task RevokedJwt_OnNonOpenApiEndpoint_Returns401()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string loginToken = await RegisterAndGetTokenAsync(client, "openapi-bypass-guard-test");
        SetBearer(client, loginToken);

        var issueResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "read", expiresIn = "1d" }));
        Assert.Equal(HttpStatusCode.Created, issueResp.StatusCode);

        var issueBody = await issueResp.Content.ReadAsStringAsync();
        using var issueDoc = JsonDocument.Parse(issueBody);
        string apiToken = issueDoc.RootElement.GetProperty("token").GetString()!;
        string tokenId = issueDoc.RootElement.GetProperty("tokenId").GetString()!;

        var revokeResp = await client.PostAsync($"/api/v1/auth/tokens/{tokenId}/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revokeResp.StatusCode);

        // Revoked token on a non-OpenAPI endpoint must still receive 401.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        var nonOpenApiResp = await client.GetAsync("/api/v1/auth/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, nonOpenApiResp.StatusCode);
    }

    // ── Document content — servers is request-derived ─────────────────────────

    [Fact]
    public async Task GetOpenApiDoc_ServersUrl_ReflectsRequestHost()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // The servers array must be present and servers[0].url must include the test host.
        Assert.True(root.TryGetProperty("servers", out var servers), "Document must have a 'servers' array");
        Assert.True(servers.GetArrayLength() > 0, "servers array must be non-empty");

        string serverUrl = servers[0].GetProperty("url").GetString()!;
        // The request host is the test client's localhost (may include port), so the URL
        // must start with "http://" and not be a static placeholder.
        Assert.StartsWith("http://", serverUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetOpenApiDoc_WithXForwardedHostHeader_ServersUrlUsesForwardedHost()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "registry.example.com");

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("servers", out var servers));
        Assert.True(servers.GetArrayLength() > 0);

        string serverUrl = servers[0].GetProperty("url").GetString()!;
        Assert.Contains("registry.example.com", serverUrl, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://", serverUrl, StringComparison.OrdinalIgnoreCase);
    }

    // ── Document info metadata ────────────────────────────────────────────────

    [Fact]
    public async Task GetOpenApiDoc_InfoMetadata_IsPopulated()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var info = doc.RootElement.GetProperty("info");

        Assert.Equal("Stash Package Registry", info.GetProperty("title").GetString());
        Assert.False(string.IsNullOrEmpty(info.GetProperty("description").GetString()), "description must be non-empty");
        Assert.Equal("1.0.0", info.GetProperty("version").GetString());

        var license = info.GetProperty("license");
        Assert.Equal("MIT", license.GetProperty("name").GetString());

        var contact = info.GetProperty("contact");
        Assert.Equal("Stash project", contact.GetProperty("name").GetString());
    }
}
