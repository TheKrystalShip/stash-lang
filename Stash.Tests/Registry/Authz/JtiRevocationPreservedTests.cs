using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Verifies the P5 requirement that JTI-based token revocation is enforced uniformly at the
/// authentication layer (before the PDP), including on <c>[PublicEndpoint]</c> routes (D22).
///
/// The revocation mechanism is hard-delete: after <c>POST /auth/tokens/{id}/revoke</c>
/// removes the token row, <c>OnTokenValidated</c> fails validation because the row is missing,
/// and a uniform middleware gate returns 401 before the request reaches any endpoint or PDP.
/// </summary>
public sealed class JtiRevocationPreservedTests : RegistryAuthzTestBase
{
    // ── Happy path: revoke own token, subsequent request with that token is 401 ─

    [Fact]
    public async Task RevokeOwnToken_SubsequentRequestWithRevokedToken_Returns401()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        // Issue a publish-ceiling token (separate from the login token).
        string loginToken = await RegisterAndGetTokenAsync(client, "alice-jti1");
        SetBearer(client, loginToken);
        var issueResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "1d" }));
        Assert.Equal(HttpStatusCode.Created, issueResp.StatusCode);
        var issueBody = await issueResp.Content.ReadAsStringAsync();
        using var issueDoc = JsonDocument.Parse(issueBody);
        string publishToken = issueDoc.RootElement.GetProperty("token").GetString()!;
        string tokenId = issueDoc.RootElement.GetProperty("tokenId").GetString()!;

        // Revoke the token.
        var revokeResp = await client.PostAsync($"/api/v1/auth/tokens/{tokenId}/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revokeResp.StatusCode);

        // Use the revoked JWT — must receive 401 even though exp is still in the future.
        SetBearer(client, publishToken);
        var afterResp = await client.GetAsync("/api/v1/auth/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, afterResp.StatusCode);
    }

    // ── Revoked token is rejected 401 even on a [PublicEndpoint] ──────────────

    [Fact]
    public async Task RevokeToken_PresentedToPublicEndpoint_Returns401()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string loginToken = await RegisterAndGetTokenAsync(client, "alice-jti2");
        await SeedScopeAsync(factory, "alice-jti2", "alice-jti2");
        await SeedPackageAsync(factory, "@alice-jti2/widget", "alice-jti2", "public");

        SetBearer(client, loginToken);

        // Issue a separate API token so we can revoke it.
        var issueResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "read", expiresIn = "1d" }));
        Assert.Equal(HttpStatusCode.Created, issueResp.StatusCode);
        var issueBody = await issueResp.Content.ReadAsStringAsync();
        using var issueDoc = JsonDocument.Parse(issueBody);
        string apiToken = issueDoc.RootElement.GetProperty("token").GetString()!;
        string tokenId = issueDoc.RootElement.GetProperty("tokenId").GetString()!;

        // Revoke the API token.
        var revokeResp = await client.PostAsync($"/api/v1/auth/tokens/{tokenId}/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revokeResp.StatusCode);

        // A [PublicEndpoint] (GET /packages/{scope}/{name}) WITH the revoked JWT must be 401.
        // The auth-layer gate must run before the endpoint classification.
        SetBearer(client, apiToken);
        var getResp = await client.GetAsync("/api/v1/packages/alice-jti2/widget");
        Assert.Equal(HttpStatusCode.Unauthorized, getResp.StatusCode);
        string body = await getResp.Content.ReadAsStringAsync();
        Assert.Contains("TokenRevoked", body);
    }

    // ── Revoked token response body contains TokenRevoked reason ──────────────

    [Fact]
    public async Task RevokeToken_Response_BodyContainsTokenRevoked()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string loginToken = await RegisterAndGetTokenAsync(client, "alice-jti3");
        SetBearer(client, loginToken);

        var issueResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "read", expiresIn = "1d" }));
        Assert.Equal(HttpStatusCode.Created, issueResp.StatusCode);
        var issueBody = await issueResp.Content.ReadAsStringAsync();
        using var issueDoc = JsonDocument.Parse(issueBody);
        string apiToken = issueDoc.RootElement.GetProperty("token").GetString()!;
        string tokenId = issueDoc.RootElement.GetProperty("tokenId").GetString()!;

        await client.PostAsync($"/api/v1/auth/tokens/{tokenId}/revoke", null);

        SetBearer(client, apiToken);
        var resp = await client.GetAsync("/api/v1/auth/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("TokenRevoked", body);
    }

    // ── Revocation precedes PDP: AuditService records TokenRevoked at the auth layer ─

    [Fact]
    public async Task RevokeToken_AuditRecordsTokenRevokedBeforePdp()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string loginToken = await RegisterAndGetTokenAsync(client, "alice-jti4");
        SetBearer(client, loginToken);

        var issueResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "1d" }));
        Assert.Equal(HttpStatusCode.Created, issueResp.StatusCode);
        var issueBody = await issueResp.Content.ReadAsStringAsync();
        using var issueDoc = JsonDocument.Parse(issueBody);
        string apiToken = issueDoc.RootElement.GetProperty("token").GetString()!;
        string tokenId = issueDoc.RootElement.GetProperty("tokenId").GetString()!;

        // Revoke.
        var revokeResp = await client.PostAsync($"/api/v1/auth/tokens/{tokenId}/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revokeResp.StatusCode);

        // Use the revoked token to attempt a request.
        SetBearer(client, apiToken);
        var resp = await client.GetAsync("/api/v1/auth/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

        // Verify the audit log contains a TokenRevoked entry (not a PDP reason).
        var auditEntries = await GetAuditEntriesAsync(factory);
        bool hasRevokedEntry = auditEntries.Exists(e =>
            e.DenyReason != null &&
            e.DenyReason.Contains("TokenRevoked") &&
            e.Decision == "deny");
        Assert.True(hasRevokedEntry,
            "Expected an audit entry with DenyReason 'TokenRevoked' but found none. " +
            $"Audit entries: {string.Join(", ", auditEntries.ConvertAll(e => $"{e.Action}/{e.Decision}/{e.DenyReason}"))}");
    }

    // ── Non-owner cannot revoke another user's token ──────────────────────────

    [Fact]
    public async Task RevokeToken_NonOwner_Returns403()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-jti5");
        SetBearer(client, aliceToken);

        // Alice issues a token.
        var issueResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "read", expiresIn = "1d" }));
        Assert.Equal(HttpStatusCode.Created, issueResp.StatusCode);
        var issueBody = await issueResp.Content.ReadAsStringAsync();
        using var issueDoc = JsonDocument.Parse(issueBody);
        string tokenId = issueDoc.RootElement.GetProperty("tokenId").GetString()!;

        // Bob attempts to revoke Alice's token.
        string bobToken = await RegisterAndGetTokenAsync(client, "bob-jti5");
        SetBearer(client, bobToken);
        var revokeResp = await client.PostAsync($"/api/v1/auth/tokens/{tokenId}/revoke", null);
        Assert.Equal(HttpStatusCode.Forbidden, revokeResp.StatusCode);
    }

    // ── Revoking a non-existent token returns 404 ─────────────────────────────

    [Fact]
    public async Task RevokeToken_NonExistentId_Returns404()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-jti6");
        SetBearer(client, token);

        var resp = await client.PostAsync("/api/v1/auth/tokens/nonexistent-id/revoke", null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── Unrevoked token with future exp is NOT rejected ───────────────────────

    [Fact]
    public async Task ValidToken_WithFutureExp_IsAccepted()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-jti7");
        SetBearer(client, token);

        var resp = await client.GetAsync("/api/v1/auth/whoami");
        // Token is valid and not revoked — must succeed (200).
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
