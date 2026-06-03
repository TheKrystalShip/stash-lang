using System;
using System.Threading.Tasks;
using Stash.Cli.PackageManager;
using Stash.Cli.PackageManager.Commands;
using Stash.Tests.Registry.Authz;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// Integration tests for <see cref="RoleCommand"/> against a real in-process registry.
/// Each test drives the command via <see cref="RoleCommand.ExecuteCore"/> (the injectable
/// seam) with a <see cref="RegistryClient"/> backed by the
/// <see cref="WebApplicationFactory{TEntryPoint}"/> in-memory transport.
/// </summary>
/// <remarks>
/// <para>
/// These tests use <see cref="RegistryAuthzFactory.Create"/> which spins up the full
/// registry application (UseSolutionRelativeContentRoot("Stash.Registry")) with an
/// isolated in-memory SQLite database. The token issued by
/// <see cref="RegistryAuthzTestBase.RegisterAndGetTokenAsync"/> is upgraded to the
/// publish ceiling required by all three role sub-verbs.
/// </para>
/// <para>
/// The <c>role assign → list → revoke</c> round-trip validates the full lifecycle.
/// The 409 (last-owner) and 404 (no-such-role) paths are tested directly via the
/// corresponding <see cref="RoleCommand.ExecuteCore"/> calls, asserting non-zero exit
/// via the <see cref="InvalidOperationException"/> that bubbles up through the command.
/// </para>
/// </remarks>
[Collection("CliTests")]
public sealed class PackageRoleCommandTests : RegistryAuthzTestBase
{
    private const string ApiBase = "http://localhost/api/v1";

    // ── Round-trip: assign → list (DB check) → revoke ─────────────────────────

    [Fact]
    public async Task Role_Assign_List_Revoke_RoundTrip_WithPublishToken()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        // Register owner + second user; publish token for owner
        string ownerToken = await RegisterAndGetTokenAsync(serverClient, "roleowner");
        await RegisterAndGetTokenAsync(serverClient, "rolebob");

        await SeedScopeAsync(factory, "roleowner", "roleowner");
        await SeedPackageAsync(factory, "@roleowner/pkg", "roleowner");

        var cli = new RegistryClient(ApiBase, serverClient, token: ownerToken);

        // ── assign: grant rolebob the maintainer role ──────────────────────────
        RoleCommand.ExecuteCore(
            "assign",
            ["@roleowner/pkg", "user", "rolebob", "maintainer"],
            cli);

        var rolesAfterAssign = await GetPackageRolesAsync(factory, "@roleowner/pkg");
        Assert.Contains(rolesAfterAssign, r => r.PrincipalId == "rolebob" && r.Role == "maintainer");

        // ── list: exercise the command code path (writes to stdout, not captured) ──
        // The command must run without throwing against the real package+role data.
        RoleCommand.ExecuteCore("list", ["@roleowner/pkg"], cli);

        // Also verify the content via the client directly (no stdout capture needed).
        var listResult = cli.GetRoles("@roleowner/pkg");
        Assert.NotNull(listResult);
        Assert.Contains(listResult!.Roles, r => r.PrincipalId == "rolebob" && r.Role == "maintainer");

        // ── revoke: remove rolebob's role ──────────────────────────────────────
        RoleCommand.ExecuteCore(
            "revoke",
            ["@roleowner/pkg", "user", "rolebob"],
            cli);

        var rolesAfterRevoke = await GetPackageRolesAsync(factory, "@roleowner/pkg");
        Assert.DoesNotContain(rolesAfterRevoke, r => r.PrincipalId == "rolebob");
        // owner row survives
        Assert.Contains(rolesAfterRevoke, r => r.PrincipalId == "roleowner" && r.Role == "owner");
    }

    // ── assign: unknown principal-type rejects with non-zero exit ─────────────

    [Fact]
    public async Task Role_Assign_UnknownPrincipalType_ThrowsArgumentException()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(serverClient, "rpt1");
        await SeedScopeAsync(factory, "rpt1", "rpt1");
        await SeedPackageAsync(factory, "@rpt1/pkg", "rpt1");

        var cli = new RegistryClient(ApiBase, serverClient, token: ownerToken);

        // "robot" is not a valid principal type
        var ex = Assert.Throws<ArgumentException>(() =>
            RoleCommand.ExecuteCore(
                "assign",
                ["@rpt1/pkg", "robot", "someone", "owner"],
                cli));

        Assert.Contains("robot", ex.Message);
        Assert.Contains("Unknown principal type", ex.Message);
    }

    // ── assign: unknown role rejects with non-zero exit ───────────────────────

    [Fact]
    public async Task Role_Assign_UnknownRole_ThrowsArgumentException()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(serverClient, "rpt2");
        await SeedScopeAsync(factory, "rpt2", "rpt2");
        await SeedPackageAsync(factory, "@rpt2/pkg", "rpt2");

        var cli = new RegistryClient(ApiBase, serverClient, token: ownerToken);

        // "superuser" is not a valid package role
        var ex = Assert.Throws<ArgumentException>(() =>
            RoleCommand.ExecuteCore(
                "assign",
                ["@rpt2/pkg", "user", "someone", "superuser"],
                cli));

        Assert.Contains("superuser", ex.Message);
        Assert.Contains("Unknown role", ex.Message);
    }

    // ── revoke: 409 last-owner surfaces server message ─────────────────────────

    [Fact]
    public async Task Role_Revoke_LastOwner_ThrowsWithServerMessage()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(serverClient, "rpt3");
        await SeedScopeAsync(factory, "rpt3", "rpt3");
        await SeedPackageAsync(factory, "@rpt3/pkg", "rpt3"); // sole owner

        var cli = new RegistryClient(ApiBase, serverClient, token: ownerToken);

        // Revoking the only owner → 409 → surfaced as InvalidOperationException
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RoleCommand.ExecuteCore(
                "revoke",
                ["@rpt3/pkg", "user", "rpt3"],
                cli));

        Assert.Contains("Conflict:", ex.Message);
        Assert.Contains("cannot remove the last owner", ex.Message);
    }

    // ── revoke: 404 no-such-role surfaces server message ──────────────────────

    [Fact]
    public async Task Role_Revoke_NoSuchRole_ThrowsNotFound()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(serverClient, "rpt4");
        await RegisterAndGetTokenAsync(serverClient, "rpt4nobody");
        await SeedScopeAsync(factory, "rpt4", "rpt4");
        await SeedPackageAsync(factory, "@rpt4/pkg", "rpt4");

        var cli = new RegistryClient(ApiBase, serverClient, token: ownerToken);

        // "rpt4nobody" holds no role → 404 → surfaced as InvalidOperationException
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RoleCommand.ExecuteCore(
                "revoke",
                ["@rpt4/pkg", "user", "rpt4nobody"],
                cli));

        Assert.Contains("Not found:", ex.Message);
        Assert.Contains("holds no role", ex.Message);
    }
}
