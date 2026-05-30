using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Verifies the P5 ceiling-first / admin-narrow intersection:
/// <list type="bullet">
///   <item>Admin with a <c>read</c>-ceiling token is denied writes (ceiling check fires first).</item>
///   <item>Admin with a <c>publish</c>-ceiling token can write to any package (admin short-circuits resource-side).</item>
/// </list>
/// </summary>
public sealed class AdminNarrowCeilingTests : RegistryAuthzTestBase
{
    // ── Happy path: admin with publish-ceiling token can write to package she has no direct role on ──

    [Fact]
    public async Task Admin_PublishCeilingToken_CanWriteToAnyPackage_Returns201()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        // First user gets admin role.
        string adminPublishToken = await RegisterAndGetTokenAsync(client, "admin-anc1");

        // alice is a regular user who owns a package.
        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-anc1");
        await SeedScopeAsync(factory, "alice-anc1", "alice-anc1");
        await SeedPackageAsync(factory, "@alice-anc1/foo", "alice-anc1");

        // admin has no direct role on @alice-anc1/foo.
        // But admin with a publish-ceiling token should resolve to effective owner.
        SetBearer(client, adminPublishToken);
        byte[] tarball = CreateTarball("@alice-anc1/foo", "2.0.0");
        var resp = await client.PutAsync("/api/v1/packages/alice-anc1/foo",
            TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    // ── Fail path: admin with read-ceiling token is denied writes ─────────────

    [Fact]
    public async Task Admin_ReadCeilingToken_PutPackageReturns403TokenScopeInsufficient()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        // First user gets admin role.
        await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "admin-anc2", password = "Password123!" }));
        var loginResp = await client.PostAsync("/api/v1/auth/login",
            Json(new { username = "admin-anc2", password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        var loginBody = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginBody);
        // The login token is read-ceiling (P5 default).
        string readToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        // alice owns the package.
        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-anc2");
        await SeedScopeAsync(factory, "alice-anc2", "alice-anc2");
        await SeedPackageAsync(factory, "@alice-anc2/bar", "alice-anc2");

        // admin presents a read-ceiling token → ceiling check denies before resource check.
        SetBearer(client, readToken);
        byte[] tarball = CreateTarball("@alice-anc2/bar", "2.0.0");
        var resp = await client.PutAsync("/api/v1/packages/alice-anc2/bar",
            TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("TokenScopeInsufficient", body);
    }

    // ── Verify admin can issue admin-scoped token and use it for writes ────────

    [Fact]
    public async Task Admin_AdminScopedToken_CanWriteToPackageWithNoDirectRole()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        // First user → admin role.
        string adminPublishToken = await RegisterAndGetTokenAsync(client, "admin-anc3");

        // Issue an admin-ceiling token (requires admin role).
        SetBearer(client, adminPublishToken);
        var adminTokenResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "admin", expiresIn = "1d" }));
        Assert.Equal(HttpStatusCode.Created, adminTokenResp.StatusCode);
        var adminTokenBody = await adminTokenResp.Content.ReadAsStringAsync();
        using var adminTokenDoc = JsonDocument.Parse(adminTokenBody);
        string adminToken = adminTokenDoc.RootElement.GetProperty("token").GetString()!;

        // alice owns a package.
        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-anc3");
        await SeedScopeAsync(factory, "alice-anc3", "alice-anc3");
        await SeedPackageAsync(factory, "@alice-anc3/baz", "alice-anc3");

        // admin-ceiling token — ceiling ≥ publish AND admin resolves resource-side.
        SetBearer(client, adminToken);
        byte[] tarball = CreateTarball("@alice-anc3/baz", "2.0.0");
        var resp = await client.PutAsync("/api/v1/packages/alice-anc3/baz",
            TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }
}
