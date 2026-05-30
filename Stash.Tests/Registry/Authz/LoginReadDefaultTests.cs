using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Verifies the P5 requirement that <c>POST /api/v1/auth/login</c> issues a
/// <c>read</c>-ceiling token by default, and that a publish-ceiling token is needed
/// for write operations.
/// </summary>
public sealed class LoginReadDefaultTests : RegistryAuthzTestBase
{
    // ── Happy path: login token carries token_scope = "read" ──────────────────

    [Fact]
    public async Task Login_AccessToken_HasReadScope()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "alice-lrd", password = "Password123!" }));
        var loginResp = await client.PostAsync("/api/v1/auth/login",
            Json(new { username = "alice-lrd", password = "Password123!" }));

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

        var body = await loginResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        string jwt = doc.RootElement.GetProperty("accessToken").GetString()!;

        // Decode the JWT (without validating signature) and assert token_scope = "read".
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);
        string? scope = token.Claims.FirstOrDefault(c => c.Type == "token_scope")?.Value;
        Assert.Equal("read", scope);
    }

    // ── Happy path: read token can access a public endpoint ───────────────────

    [Fact]
    public async Task Login_ReadToken_CanGetPublicPackage()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        // Register and get raw login (read-ceiling) token.
        await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "alice-lrd2", password = "Password123!" }));
        var loginResp = await client.PostAsync("/api/v1/auth/login",
            Json(new { username = "alice-lrd2", password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        var loginBody = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginBody);
        string readToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        // Seed a public package directly in the DB.
        await SeedScopeAsync(factory, "alice-lrd2", "alice-lrd2");
        await SeedPackageAsync(factory, "@alice-lrd2/widget", "alice-lrd2", "public");

        // GET with read-ceiling token must return 200.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", readToken);
        var getResp = await client.GetAsync("/api/v1/packages/alice-lrd2/widget");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
    }

    // ── Fail path: read token cannot PUT a package (403 TokenScopeInsufficient) ─

    [Fact]
    public async Task Login_ReadToken_PutPackageReturns403TokenScopeInsufficient()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        // Register and get the raw login (read-ceiling) token — do NOT use the helper
        // which auto-upgrades to publish.
        await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "alice-lrd3", password = "Password123!" }));
        var loginResp = await client.PostAsync("/api/v1/auth/login",
            Json(new { username = "alice-lrd3", password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        var loginBody = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginBody);
        string readToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        await SeedScopeAsync(factory, "alice-lrd3", "alice-lrd3");

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", readToken);
        byte[] tarball = CreateTarball("@alice-lrd3/lib", "1.0.0");
        var putResp = await client.PutAsync("/api/v1/packages/alice-lrd3/lib",
            TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Forbidden, putResp.StatusCode);
        string body = await putResp.Content.ReadAsStringAsync();
        Assert.Contains("TokenScopeInsufficient", body);
    }

    // ── Full round-trip: read token blocked, then publish-ceiling token succeeds ─

    [Fact]
    public async Task Login_AfterIssuingPublishToken_PutSucceeds()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        // Register and obtain read-ceiling login token.
        await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "alice-lrd4", password = "Password123!" }));
        var loginResp = await client.PostAsync("/api/v1/auth/login",
            Json(new { username = "alice-lrd4", password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        var loginBody = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginBody);
        string readToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        await SeedScopeAsync(factory, "alice-lrd4", "alice-lrd4");

        // Confirm the read token is insufficient for PUT.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", readToken);
        byte[] tarball = CreateTarball("@alice-lrd4/lib", "1.0.0");
        var failResp = await client.PutAsync("/api/v1/packages/alice-lrd4/lib",
            TarballContent(tarball));
        Assert.Equal(HttpStatusCode.Forbidden, failResp.StatusCode);

        // Issue a publish-ceiling token (POST /auth/tokens — only needs authentication).
        var tokenResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "1d" }));
        Assert.Equal(HttpStatusCode.Created, tokenResp.StatusCode);
        var tokenBody = await tokenResp.Content.ReadAsStringAsync();
        using var tokenDoc = JsonDocument.Parse(tokenBody);
        string publishToken = tokenDoc.RootElement.GetProperty("token").GetString()!;

        // Confirm publish-ceiling token allows PUT.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", publishToken);
        var okResp = await client.PutAsync("/api/v1/packages/alice-lrd4/lib",
            TarballContent(tarball));
        Assert.Equal(HttpStatusCode.Created, okResp.StatusCode);
    }

    // ── POST /auth/tokens must reject a "capabilities" field ─────────────────

    [Fact]
    public async Task CreateToken_WithCapabilitiesField_Returns400()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-lrd5");
        SetBearer(client, token);

        // Supply a capabilities field — must be rejected 400.
        var resp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "1d", capabilities = new[] { "read", "write" } }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("capabilities", body);
    }

    // ── POST /auth/tokens with ceiling field issues token ────────────────────

    [Fact]
    public async Task CreateToken_WithCeilingField_IssuesToken()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        // Get a raw read-ceiling login token for the authenticated call (D6 default).
        await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "alice-lrd6", password = "Password123!" }));
        var loginResp = await client.PostAsync("/api/v1/auth/login",
            Json(new { username = "alice-lrd6", password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        var loginBody = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginBody);
        string readToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;
        SetBearer(client, readToken);

        var resp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "7d" }));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var respBody = await resp.Content.ReadAsStringAsync();
        using var respDoc = JsonDocument.Parse(respBody);
        string publishJwt = respDoc.RootElement.GetProperty("token").GetString()!;

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(publishJwt);
        string? scope = jwtToken.Claims.FirstOrDefault(c => c.Type == "token_scope")?.Value;
        Assert.Equal("publish", scope);
    }

    // ── POST /auth/tokens with scope field (no ceiling) is rejected 400 ──────
    // Regression for D11: the old backwards-compat alias has been removed.

    [Fact]
    public async Task CreateToken_WithScopeFieldOnly_Returns400CeilingRequired()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-lrd7");
        SetBearer(client, token);

        // Send 'scope' only (no 'ceiling') — must be rejected 400.
        var resp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { scope = "publish", expiresIn = "30d" }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("ceiling", body);
    }
}
