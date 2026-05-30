using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Verifies the P5 requirement that <c>POST /api/v1/auth/tokens</c> enforces
/// <c>Security.MaxTokenLifetime</c> (default 90 days).
/// </summary>
public sealed class TokenLifetimeCapTests : RegistryAuthzTestBase
{
    // ── Happy path: expires_in within cap succeeds ────────────────────────────

    [Fact]
    public async Task CreateToken_ExpiresIn30d_WithinCap_Returns201()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-tlc1");
        SetBearer(client, token);

        var resp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "30d" }));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        // Decode JWT and confirm exp ≈ now + 30d.
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        string jwt = doc.RootElement.GetProperty("token").GetString()!;
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(jwt);
        var expectedExpiry = DateTime.UtcNow.AddDays(30);
        Assert.True(
            Math.Abs((jwtToken.ValidTo - expectedExpiry).TotalMinutes) < 5,
            $"Expected exp ≈ now+30d, got {jwtToken.ValidTo:O}");
    }

    // ── Fail path: expires_in exceeds MaxTokenLifetime returns 400 ───────────

    [Fact]
    public async Task CreateToken_ExpiresIn365d_ExceedsCap_Returns400TokenLifetimeExceeded()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-tlc2");
        SetBearer(client, token);

        var resp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "365d" }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("TokenLifetimeExceeded", body);
        // The configured MaxTokenLifetime (90d) must be echoed in the response.
        Assert.Contains("90d", body);
    }

    // ── Fail path: expires_in absent returns 400 ─────────────────────────────

    [Fact]
    public async Task CreateToken_ExpiresInAbsent_Returns400()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-tlc3");
        SetBearer(client, token);

        // No expiresIn field.
        var resp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish" }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("expires_in", body);
    }

    // ── Fail path: expires_in empty string returns 400 ───────────────────────

    [Fact]
    public async Task CreateToken_ExpiresInEmpty_Returns400()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-tlc4");
        SetBearer(client, token);

        var resp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "" }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Fail path: expires_in invalid format returns 400 ─────────────────────

    [Fact]
    public async Task CreateToken_ExpiresInInvalidFormat_Returns400()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-tlc5");
        SetBearer(client, token);

        var resp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "not-a-duration" }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Edge: exactly at the cap boundary succeeds ───────────────────────────

    [Fact]
    public async Task CreateToken_ExpiresIn90d_ExactlyAtCap_Returns201()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-tlc6");
        SetBearer(client, token);

        // 90d is exactly the default MaxTokenLifetime — must succeed.
        var resp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "read", expiresIn = "90d" }));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }
}
