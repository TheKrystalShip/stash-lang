using System;
using System.Net.Http;
using System.Threading.Tasks;
using Stash.Cli.PackageManager;
using Stash.Cli.PackageManager.Commands;
using Stash.Registry.Contracts;
using Stash.Tests.Registry.Authz;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// Integration tests for <see cref="VisibilityCommand"/> against a real in-process registry.
/// Each test drives the command via <see cref="VisibilityCommand.ExecuteCore"/> (the injectable
/// seam) with a <see cref="RegistryClient"/> backed by the
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> in-memory
/// transport.
/// </summary>
/// <remarks>
/// <para>
/// The visibility-flip test confirms the observable: after setting private with a publish
/// token, an unauthenticated GET on the same package URL returns a non-success status code
/// (the visibility policy hides the package from anonymous callers). A before/after probe
/// confirms the flip — not just the final state.
/// </para>
/// <para>
/// The unknown-tier test confirms that the bounded <see cref="Visibilities"/> set gates
/// user input: an unrecognised tier throws <see cref="ArgumentException"/> (non-zero exit)
/// with a clear message listing the valid values.
/// </para>
/// </remarks>
[Collection("CliTests")]
public sealed class PackageVisibilityCommandTests : RegistryAuthzTestBase
{
    private const string ApiBase = "http://localhost/api/v1";

    // ── visibility set: flip to private, confirm anonymous read is rejected ───

    [Fact]
    public async Task Visibility_Set_Private_AnonymousGetIsRejected()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        // Register owner; seed scope + public package
        string ownerToken = await RegisterAndGetTokenAsync(serverClient, "visowner");

        await SeedScopeAsync(factory, "visowner", "visowner");
        await SeedPackageAsync(factory, "@visowner/widget", "visowner", visibility: Visibilities.Public.ToWire());

        var cli = new RegistryClient(ApiBase, serverClient, token: ownerToken);

        // Before the flip: anonymous GET succeeds (public package)
        using var anonClientBefore = factory.CreateClient();
        var beforeResp = await anonClientBefore.GetAsync($"{ApiBase}/packages/visowner/widget");
        Assert.True(beforeResp.IsSuccessStatusCode,
            $"Expected anonymous GET to succeed on public package, got {(int)beforeResp.StatusCode}.");

        // Flip visibility to private via the command
        VisibilityCommand.ExecuteCore("set", ["@visowner/widget", Visibilities.Private.ToWire()], cli);

        // After the flip: anonymous GET must fail (visibility policy hides the package)
        using var anonClientAfter = factory.CreateClient();
        var afterResp = await anonClientAfter.GetAsync($"{ApiBase}/packages/visowner/widget");
        Assert.False(afterResp.IsSuccessStatusCode,
            $"Expected anonymous GET to be rejected after setting private, got {(int)afterResp.StatusCode}.");
    }

    // ── visibility set: idempotent — setting same tier again succeeds ─────────

    [Fact]
    public async Task Visibility_Set_SameTier_IsIdempotent()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(serverClient, "visidem");

        await SeedScopeAsync(factory, "visidem", "visidem");
        await SeedPackageAsync(factory, "@visidem/pkg", "visidem", visibility: Visibilities.Public.ToWire());

        var cli = new RegistryClient(ApiBase, serverClient, token: ownerToken);

        // Setting public on an already-public package must not throw
        VisibilityCommand.ExecuteCore("set", ["@visidem/pkg", Visibilities.Public.ToWire()], cli);
    }

    // ── visibility set: unknown tier is rejected with a clear message ─────────

    [Fact]
    public async Task Visibility_Set_UnknownTier_ThrowsArgumentException()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(serverClient, "visunk");

        await SeedScopeAsync(factory, "visunk", "visunk");
        await SeedPackageAsync(factory, "@visunk/pkg", "visunk");

        var cli = new RegistryClient(ApiBase, serverClient, token: ownerToken);

        // "unlisted" is not a valid tier
        var ex = Assert.Throws<ArgumentException>(() =>
            VisibilityCommand.ExecuteCore("set", ["@visunk/pkg", "unlisted"], cli));

        Assert.Contains("unlisted", ex.Message);
        Assert.Contains("Unknown visibility tier", ex.Message);
        // The message must list the valid tiers (bounded-domain guidance)
        Assert.Contains("public", ex.Message);
        Assert.Contains("private", ex.Message);
        Assert.Contains("internal", ex.Message);
    }

    // ── visibility: 'get' subverb is rejected (not implemented) ──────────────

    [Fact]
    public async Task Visibility_Get_IsRejectedWithArgumentException()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(serverClient, "visget");

        await SeedScopeAsync(factory, "visget", "visget");
        await SeedPackageAsync(factory, "@visget/pkg", "visget");

        var cli = new RegistryClient(ApiBase, serverClient, token: ownerToken);

        // 'get' is intentionally absent — should throw (non-zero exit)
        var ex = Assert.Throws<ArgumentException>(() =>
            VisibilityCommand.ExecuteCore("get", ["@visget/pkg"], cli));

        Assert.Contains("get", ex.Message);
    }
}
