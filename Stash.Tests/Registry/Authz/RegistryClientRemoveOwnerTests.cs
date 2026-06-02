using System;
using System.Linq;
using System.Threading.Tasks;
using Stash.Cli.PackageManager;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// End-to-end tests for the CLI <see cref="RegistryClient.RemoveOwner"/> path
/// (<c>stash pkg owner remove</c>) against the real in-process registry.
/// </summary>
/// <remarks>
/// These drive the production <see cref="RegistryClient"/> — its real JSON
/// serialization and URL construction — through the registry's actual
/// <c>DELETE /admin/packages/{scope}/{name}/roles</c> endpoint and
/// <c>[FromBody] RevokeRoleRequest</c> binding. That makes them the wire-contract
/// guard for the fix tracked in the "Package role revocation not exposed over HTTP"
/// bug: a mismatched JSON key (<c>principalId</c> vs the server's required
/// <c>principal_id</c>) would surface as a 400 here, not a silently-green mock.
///
/// The HTTP endpoint, last-owner invariant, and status mapping themselves are
/// covered server-side by <see cref="RegistryAuthzRoleRevokeTests"/>; these tests
/// cover the CLI client's half of the round-trip.
/// </remarks>
public sealed class RegistryClientRemoveOwnerTests : RegistryAuthzTestBase
{
    // The registry's admin endpoints live under /api/v1/admin; RegistryClient builds
    // its URLs as "{baseUrl}/admin/packages/...", so the base is the /api/v1 root.
    private const string ApiBase = "http://localhost/api/v1";

    [Fact]
    public async Task RemoveOwner_SecondOwnerPresent_Returns204AndRevokesRole()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        // First registered user gets the admin role; owner add/remove use the admin route.
        string adminToken = await RegisterAndGetAdminTokenAsync(serverClient, "adminro");
        await RegisterAndGetTokenAsync(serverClient, "bobro");

        await SeedScopeAsync(factory, "adminro", "adminro");
        await SeedPackageAsync(factory, "@adminro/widgets", "adminro");          // adminro is owner
        await SeedPackageRoleAsync(factory, "@adminro/widgets", "user", "bobro", "owner"); // bobro is 2nd owner

        var cli = new RegistryClient(ApiBase, serverClient, token: adminToken);

        bool result = cli.RemoveOwner("@adminro/widgets", "bobro");

        Assert.True(result);
        var roles = await GetPackageRolesAsync(factory, "@adminro/widgets");
        Assert.DoesNotContain(roles, r => r.PrincipalId == "bobro");
        Assert.Contains(roles, r => r.PrincipalId == "adminro" && r.Role == "owner");
    }

    [Fact]
    public async Task RemoveOwner_LastOwner_ThrowsWithServerMessage()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(serverClient, "adminro2");
        await SeedScopeAsync(factory, "adminro2", "adminro2");
        await SeedPackageAsync(factory, "@adminro2/widgets", "adminro2"); // sole owner

        var cli = new RegistryClient(ApiBase, serverClient, token: adminToken);

        // Revoking the only owner is refused (409); RemoveOwner surfaces the server's
        // message rather than collapsing it to an opaque bool.
        var ex = Assert.Throws<InvalidOperationException>(
            () => cli.RemoveOwner("@adminro2/widgets", "adminro2"));
        Assert.Contains("cannot remove the last owner of a package", ex.Message);

        // The owner row survives the refused revoke.
        var roles = await GetPackageRolesAsync(factory, "@adminro2/widgets");
        Assert.Contains(roles, r => r.PrincipalId == "adminro2" && r.Role == "owner");
    }

    [Fact]
    public async Task RemoveOwner_PrincipalHoldsNoRole_ThrowsNotFound()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(serverClient, "adminro3");
        await SeedScopeAsync(factory, "adminro3", "adminro3");
        await SeedPackageAsync(factory, "@adminro3/widgets", "adminro3");

        var cli = new RegistryClient(ApiBase, serverClient, token: adminToken);

        // "nobody" holds no role on the package → 404 → surfaced as an exception.
        var ex = Assert.Throws<InvalidOperationException>(
            () => cli.RemoveOwner("@adminro3/widgets", "nobody"));
        Assert.Contains("NotFound", ex.Message);
    }
}
