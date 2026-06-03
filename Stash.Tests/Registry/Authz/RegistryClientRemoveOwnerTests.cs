using System;
using System.Linq;
using System.Threading.Tasks;
using Stash.Cli.PackageManager;
using Stash.Registry.Contracts;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// End-to-end tests for the CLI <see cref="RegistryClient.RevokeRole"/> path
/// (formerly <c>stash pkg owner remove</c>, now <c>stash pkg role revoke</c>)
/// against the real in-process registry.
/// </summary>
/// <remarks>
/// These drive the production <see cref="RegistryClient"/> — its real JSON
/// serialization and URL construction — through the registry's actual
/// <c>DELETE /packages/{scope}/{name}/roles</c> self-service endpoint and
/// <c>[FromBody] RevokeRoleRequest</c> binding. That makes them the wire-contract
/// guard for the D18 last-owner invariant: a mismatched JSON key or wrong route
/// would surface as a non-204 here, not a silently-green mock.
///
/// The HTTP endpoint, last-owner invariant, and status mapping themselves are
/// covered server-side by <see cref="RegistryAuthzRoleRevokeTests"/>; these tests
/// cover the CLI client's half of the round-trip.
/// </remarks>
public sealed class RegistryClientRemoveOwnerTests : RegistryAuthzTestBase
{
    // The registry's self-service role endpoints live under /api/v1/packages; RegistryClient
    // builds its URLs as "{baseUrl}/packages/...", so the base is the /api/v1 root.
    private const string ApiBase = "http://localhost/api/v1";

    [Fact]
    public async Task RevokeRole_SecondOwnerPresent_Returns204AndRevokesRole()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string publishToken = await RegisterAndGetTokenAsync(serverClient, "adminro");
        await RegisterAndGetTokenAsync(serverClient, "bobro");

        await SeedScopeAsync(factory, "adminro", "adminro");
        await SeedPackageAsync(factory, "@adminro/widgets", "adminro");          // adminro is owner
        await SeedPackageRoleAsync(factory, "@adminro/widgets", "user", "bobro", "owner"); // bobro is 2nd owner

        var cli = new RegistryClient(ApiBase, serverClient, token: publishToken);

        bool result = cli.RevokeRole("@adminro/widgets", "user", "bobro");

        Assert.True(result);
        var roles = await GetPackageRolesAsync(factory, "@adminro/widgets");
        Assert.DoesNotContain(roles, r => r.PrincipalId == "bobro");
        Assert.Contains(roles, r => r.PrincipalId == "adminro" && r.Role == PackageRoles.Owner);
    }

    [Fact]
    public async Task RevokeRole_LastOwner_ThrowsWithServerMessage()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string publishToken = await RegisterAndGetTokenAsync(serverClient, "adminro2");
        await SeedScopeAsync(factory, "adminro2", "adminro2");
        await SeedPackageAsync(factory, "@adminro2/widgets", "adminro2"); // sole owner

        var cli = new RegistryClient(ApiBase, serverClient, token: publishToken);

        // Revoking the only owner is refused (409); RevokeRole surfaces the server's
        // message rather than collapsing it to an opaque bool.
        var ex = Assert.Throws<InvalidOperationException>(
            () => cli.RevokeRole("@adminro2/widgets", "user", "adminro2"));
        Assert.Contains("cannot remove the last owner of a package", ex.Message);

        // The owner row survives the refused revoke.
        var roles = await GetPackageRolesAsync(factory, "@adminro2/widgets");
        Assert.Contains(roles, r => r.PrincipalId == "adminro2" && r.Role == PackageRoles.Owner);
    }

    [Fact]
    public async Task RevokeRole_PrincipalHoldsNoRole_ThrowsNotFound()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string publishToken = await RegisterAndGetTokenAsync(serverClient, "adminro3");
        await SeedScopeAsync(factory, "adminro3", "adminro3");
        await SeedPackageAsync(factory, "@adminro3/widgets", "adminro3");

        var cli = new RegistryClient(ApiBase, serverClient, token: publishToken);

        // "nobody" holds no role on the package → 404 → surfaced through HandleNonSuccess
        // with the brief's "Not found:" prefix and the server's specific message.
        var ex = Assert.Throws<InvalidOperationException>(
            () => cli.RevokeRole("@adminro3/widgets", "user", "nobody"));
        Assert.Contains("Not found:", ex.Message);
        Assert.Contains("holds no role", ex.Message);
    }
}
