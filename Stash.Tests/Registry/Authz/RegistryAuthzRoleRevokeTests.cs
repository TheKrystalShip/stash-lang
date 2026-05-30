using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Tests for role revocation endpoints (P3 / D18).
/// </summary>
public sealed class RegistryAuthzRoleRevokeTests : RegistryAuthzTestBase
{
    private static HttpRequestMessage DeleteWithBody(string url, object body)
    {
        var msg = new HttpRequestMessage(HttpMethod.Delete, url);
        msg.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return msg;
    }

    [Fact]
    public async Task RoleRevoke_OwnerRevokesPublisherRole_Returns204()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-rr");
        await RegisterAndGetTokenAsync(client, "bob-rr");
        await SeedScopeAsync(factory, "alice-rr", "alice-rr");
        await SeedPackageAsync(factory, "@alice-rr/widgets", "alice-rr");
        await SeedPackageRoleAsync(factory, "@alice-rr/widgets", "user", "bob-rr", "publisher");

        SetBearer(client, aliceToken);
        var resp = await client.SendAsync(DeleteWithBody(
            "/api/v1/packages/alice-rr/widgets/roles",
            new { principal_type = "user", principal_id = "bob-rr" }));

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var roles = await GetPackageRolesAsync(factory, "@alice-rr/widgets");
        Assert.DoesNotContain(roles, r => r.PrincipalId == "bob-rr");
    }

    [Fact]
    public async Task RoleRevoke_NonOwnerAttempt_Returns403PackageRoleInsufficient()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "alice-rr2");
        string malloryToken = await RegisterAndGetTokenAsync(client, "mallory-rr2");
        await SeedScopeAsync(factory, "alice-rr2", "alice-rr2");
        await SeedPackageAsync(factory, "@alice-rr2/widgets", "alice-rr2");

        SetBearer(client, malloryToken);
        var resp = await client.SendAsync(DeleteWithBody(
            "/api/v1/packages/alice-rr2/widgets/roles",
            new { principal_type = "user", principal_id = "alice-rr2" }));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("PackageRoleInsufficient", body);
    }

    [Fact]
    public async Task RoleRevoke_MissingPackage_Returns404()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-rr3");
        SetBearer(client, token);

        var resp = await client.SendAsync(DeleteWithBody(
            "/api/v1/packages/alice-rr3/nonexistent/roles",
            new { principal_type = "user", principal_id = "bob" }));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task RoleRevoke_PrincipalHoldsNoRole_Returns404()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-rr4");
        await SeedScopeAsync(factory, "alice-rr4", "alice-rr4");
        await SeedPackageAsync(factory, "@alice-rr4/pkg", "alice-rr4");

        SetBearer(client, aliceToken);
        var resp = await client.SendAsync(DeleteWithBody(
            "/api/v1/packages/alice-rr4/pkg/roles",
            new { principal_type = "user", principal_id = "nobody" }));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task RoleRevoke_LastOwner_Returns409WithDocumentedMessage()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-rr5");
        await SeedScopeAsync(factory, "alice-rr5", "alice-rr5");
        await SeedPackageAsync(factory, "@alice-rr5/pkg", "alice-rr5");

        SetBearer(client, aliceToken);
        var resp = await client.SendAsync(DeleteWithBody(
            "/api/v1/packages/alice-rr5/pkg/roles",
            new { principal_type = "user", principal_id = "alice-rr5" }));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("cannot remove the last owner of a package", body);

        var roles = await GetPackageRolesAsync(factory, "@alice-rr5/pkg");
        Assert.Contains(roles, r => r.PrincipalId == "alice-rr5" && r.Role == "owner");
    }

    [Fact]
    public async Task RoleRevoke_SecondOwnerPresent_FirstOwnerRevokeSucceeds()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-rr6");
        await RegisterAndGetTokenAsync(client, "bob-rr6");
        await SeedScopeAsync(factory, "alice-rr6", "alice-rr6");
        await SeedPackageAsync(factory, "@alice-rr6/pkg", "alice-rr6");
        await SeedPackageRoleAsync(factory, "@alice-rr6/pkg", "user", "bob-rr6", "owner");

        SetBearer(client, aliceToken);
        var resp = await client.SendAsync(DeleteWithBody(
            "/api/v1/packages/alice-rr6/pkg/roles",
            new { principal_type = "user", principal_id = "alice-rr6" }));

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var roles = await GetPackageRolesAsync(factory, "@alice-rr6/pkg");
        Assert.DoesNotContain(roles, r => r.PrincipalId == "alice-rr6" && r.Role == "owner");
        Assert.Contains(roles, r => r.PrincipalId == "bob-rr6" && r.Role == "owner");
    }

    [Fact]
    public async Task RoleRevoke_AdminOverride_Returns204WithNoDirectRole()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        // First registered = admin; we need an admin-scoped token for the admin endpoint
        string adminToken = await RegisterAndGetAdminTokenAsync(client, "root-rr");
        await RegisterAndGetTokenAsync(client, "alice-rr7");
        await RegisterAndGetTokenAsync(client, "bob-rr7");

        await SeedScopeAsync(factory, "alice-rr7", "alice-rr7");
        await SeedPackageAsync(factory, "@alice-rr7/pkg", "alice-rr7");
        await SeedPackageRoleAsync(factory, "@alice-rr7/pkg", "user", "bob-rr7", "publisher");
        // alice is the lone owner

        SetBearer(client, adminToken);
        var resp = await client.SendAsync(DeleteWithBody(
            "/api/v1/admin/packages/alice-rr7/pkg/roles",
            new { principal_type = "user", principal_id = "bob-rr7" }));

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var roles = await GetPackageRolesAsync(factory, "@alice-rr7/pkg");
        Assert.DoesNotContain(roles, r => r.PrincipalId == "bob-rr7");
    }

    [Fact]
    public async Task RoleRevoke_AdminOverride_LastOwner_Returns409()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "root-rr2");
        await RegisterAndGetTokenAsync(client, "alice-rr8");

        await SeedScopeAsync(factory, "alice-rr8", "alice-rr8");
        await SeedPackageAsync(factory, "@alice-rr8/pkg", "alice-rr8");

        SetBearer(client, adminToken);
        var resp = await client.SendAsync(DeleteWithBody(
            "/api/v1/admin/packages/alice-rr8/pkg/roles",
            new { principal_type = "user", principal_id = "alice-rr8" }));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("cannot remove the last owner of a package", body);
    }

    [Fact]
    public async Task RoleRevoke_NonAdminOnAdminEndpoint_Returns403()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "root-rr3"); // admin
        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-rr9");
        await RegisterAndGetTokenAsync(client, "bob-rr9");

        await SeedScopeAsync(factory, "alice-rr9", "alice-rr9");
        await SeedPackageAsync(factory, "@alice-rr9/pkg", "alice-rr9");
        await SeedPackageRoleAsync(factory, "@alice-rr9/pkg", "user", "bob-rr9", "publisher");

        // alice is package owner but NOT registry admin
        SetBearer(client, aliceToken);
        var resp = await client.SendAsync(DeleteWithBody(
            "/api/v1/admin/packages/alice-rr9/pkg/roles",
            new { principal_type = "user", principal_id = "bob-rr9" }));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
