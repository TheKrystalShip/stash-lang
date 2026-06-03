using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stash.Cli.PackageManager;
using Stash.Cli.PackageManager.Commands;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Tests.Registry.Authz;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// Integration tests for <see cref="ScopeCommand"/> against a real in-process registry.
/// Each test drives the command via <see cref="ScopeCommand.ExecuteCore"/> (the injectable
/// seam) with a <see cref="RegistryClient"/> backed by the
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> in-memory
/// transport.
/// </summary>
/// <remarks>
/// <para>
/// Test coverage:
/// <list type="bullet">
///   <item><description>claim → info round-trip: scope is persisted and readable via <c>info</c>.</description></item>
///   <item><description>Second claim by a different user fails with <see cref="InvalidOperationException"/> (non-zero exit).</description></item>
///   <item><description>Verified-mode claim prints the DNS-TXT challenge instead of reporting a completed claim.</description></item>
/// </list>
/// </para>
/// </remarks>
[Collection("CliTests")]
public sealed class PackageScopeCommandTests : RegistryAuthzTestBase
{
    private const string ApiBase = "http://localhost/api/v1";

    // ── claim → info round-trip ────────────────────────────────────────────────

    /// <summary>
    /// Claiming a scope under the default Claim policy creates the scope ownership row.
    /// A subsequent <c>info</c> call returns the owner and owner-type.
    /// </summary>
    [Fact]
    public async Task Scope_Claim_ThenInfo_ShowsOwner()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string ownerToken = await RegisterAndGetTokenAsync(serverClient, "scowner1");

        var cli = new RegistryClient(ApiBase, serverClient, token: ownerToken);

        // Capture stdout to verify claim output.
        // NOTE: registration auto-provisions @scowner1, so use a distinct scope name.
        var stdout = new StringWriter();
        var prevOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            ScopeCommand.ExecuteCore("claim", ["scowner1-products"], cli);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        string claimOutput = stdout.ToString();
        Assert.Contains("scowner1-products", claimOutput);
        Assert.Contains("claimed", claimOutput, StringComparison.OrdinalIgnoreCase);

        // info: read back the scope and verify owner details
        var infoOut = new StringWriter();
        Console.SetOut(infoOut);
        try
        {
            ScopeCommand.ExecuteCore("info", ["scowner1-products"], cli);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        string infoOutput = infoOut.ToString();
        Assert.Contains("scowner1-products", infoOutput);
        Assert.Contains(ScopeOwnerTypes.User.ToWire(), infoOutput);
        Assert.Contains("scowner1", infoOutput); // owner field (the username)
    }

    // ── second claim by a different user fails ─────────────────────────────────

    /// <summary>
    /// A second claim on an already-owned scope by a different authenticated user
    /// throws <see cref="InvalidOperationException"/> with a clear message.
    /// This surfaces as a non-zero exit code via <see cref="PackageCommands.Run"/>.
    /// </summary>
    [Fact]
    public async Task Scope_Claim_AlreadyOwned_ByDifferentUser_ThrowsWithMessage()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        // User A claims the scope
        string aliceToken = await RegisterAndGetTokenAsync(serverClient, "sc-alice");
        string malloryToken = await RegisterAndGetTokenAsync(serverClient, "sc-mallory");

        // Seed scope owned by alice (direct DB injection — same pattern as other integration tests)
        await SeedScopeAsync(factory, "sc-alice-corp", "sc-alice");

        // Mallory tries to claim the same scope
        var malloryCli = new RegistryClient(ApiBase, serverClient, token: malloryToken);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ScopeCommand.ExecuteCore("claim", ["sc-alice-corp"], malloryCli));

        // The exception message must surface the server's conflict reason (F02)
        Assert.False(string.IsNullOrEmpty(ex.Message));
        // "Conflict: ..." prefix from the brief's §General error mapping
        Assert.Contains("Conflict", ex.Message, StringComparison.OrdinalIgnoreCase);
        // The server returns a 409 with a message about the scope being already owned/reserved
        Assert.Contains("sc-alice-corp", ex.Message);
    }

    // ── --org flag sets owner_type=org ─────────────────────────────────────────

    /// <summary>
    /// Passing <c>--org &lt;name&gt;</c> sets <c>owner_type=org</c> and
    /// <c>owner=&lt;name&gt;</c> in the claim request.
    /// The subsequent <c>info</c> call reflects the org ownership.
    /// </summary>
    [Fact]
    public async Task Scope_Claim_WithOrgFlag_SetsOrgOwnerType()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(serverClient, "sc-orgadmin");

        // Create the org first (required for org-owned scope claim)
        using var setupClient = factory.CreateClient();
        setupClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        await setupClient.PostAsync($"{ApiBase}/orgs",
            new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { name = "sc-acme" }),
                System.Text.Encoding.UTF8, "application/json"));

        var adminCli = new RegistryClient(ApiBase, serverClient, token: adminToken);

        // Claim for the org
        var claimOut = new StringWriter();
        var prevOut = Console.Out;
        Console.SetOut(claimOut);
        try
        {
            ScopeCommand.ExecuteCore("claim", ["sc-acme-corp", "--org", "sc-acme"], adminCli);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        // info: verify org owner_type
        var infoOut = new StringWriter();
        Console.SetOut(infoOut);
        try
        {
            ScopeCommand.ExecuteCore("info", ["sc-acme-corp"], adminCli);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        string infoOutput = infoOut.ToString();
        Assert.Contains(ScopeOwnerTypes.Org.ToWire(), infoOutput);
        Assert.Contains("sc-acme", infoOutput);
    }

    // ── Verified policy: challenge is printed instead of reporting a claim ─────

    /// <summary>
    /// Under the Verified ownership policy a scope claim returns a DNS-TXT challenge.
    /// The command prints the challenge instructions (record_name / record_value /
    /// expires_at) rather than reporting a completed claim.
    /// </summary>
    [Fact]
    public async Task Scope_Claim_VerifiedMode_PrintsDnsTxtChallenge()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        try
        {
            using var factory = CreateVerifiedPolicyFactory(conn);
            using var serverClient = factory.CreateClient();

            string token = await RegisterAndGetTokenAsync(serverClient, "scv-user1");
            var cli = new RegistryClient(ApiBase, serverClient, token: token);

            var stdout = new StringWriter();
            var prevOut = Console.Out;
            Console.SetOut(stdout);
            try
            {
                ScopeCommand.ExecuteCore("claim", ["scv-corp1"], cli);
            }
            finally
            {
                Console.SetOut(prevOut);
            }

            string output = stdout.ToString();

            // Must print DNS-TXT challenge instructions, not a "claimed successfully" message
            Assert.Contains("pending", output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("claimed successfully", output, StringComparison.OrdinalIgnoreCase);

            // Must include all three challenge field labels (as displayed by ScopeCommand)
            Assert.Contains("Record name", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Record value", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Expires at", output, StringComparison.OrdinalIgnoreCase);

            // The DNS record name and value must be non-empty
            Assert.Contains("_stash-challenge.", output);
            Assert.Contains("stash-verify=", output);

            // Must NOT instruct the user to run a CLI verb that doesn't exist (F01)
            Assert.DoesNotContain("stash pkg scope verify", output);
        }
        finally
        {
            conn.Dispose();
        }
    }

    // ── info: scope not found returns clear error ──────────────────────────────

    /// <summary>
    /// Requesting info on a scope that does not exist throws
    /// <see cref="InvalidOperationException"/> with a clear "not found" message.
    /// </summary>
    [Fact]
    public async Task Scope_Info_NotFound_ThrowsWithMessage()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(serverClient, "scinfo-notfound");
        var cli = new RegistryClient(ApiBase, serverClient, token: token);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ScopeCommand.ExecuteCore("info", ["no-such-scope-xyzzy"], cli));

        Assert.Contains("no-such-scope-xyzzy", ex.Message);
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── unknown subcommand is rejected ────────────────────────────────────────

    [Fact]
    public async Task Scope_UnknownSubcommand_ThrowsArgumentException()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(serverClient, "scunk");
        var cli = new RegistryClient(ApiBase, serverClient, token: token);

        var ex = Assert.Throws<ArgumentException>(() =>
            ScopeCommand.ExecuteCore("delete", [], cli));

        Assert.Contains("delete", ex.Message);
        Assert.Contains("Unknown scope subcommand", ex.Message);
    }

    // ── Verified-mode factory (mirrors ScopeClaimVerifiedFlowTests) ───────────

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Stash.Registry.Program>
        CreateVerifiedPolicyFactory(SqliteConnection conn)
    {
        return new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSolutionRelativeContentRoot("Stash.Registry");
                builder.UseSetting("environment", "Development");
                builder.ConfigureTestServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(
                            Microsoft.EntityFrameworkCore.DbContextOptions<
                                Stash.Registry.Database.RegistryDbContext>));
                    if (descriptor != null) services.Remove(descriptor);
                    services.AddDbContext<Stash.Registry.Database.RegistryDbContext>(
                        options => options.UseSqlite(conn));

                    var configDescriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(RegistryConfig));
                    if (configDescriptor != null) services.Remove(configDescriptor);
                    var cfg = new RegistryConfig();
                    cfg.Security.ScopeOwnershipPolicy = ScopeOwnershipPolicyKind.Verified;
                    cfg.Auth.RegistrationEnabled = true;
                    services.AddSingleton(cfg);

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    scope.ServiceProvider.GetRequiredService<IRegistryDatabase>().Initialize();
                });
            });
    }
}
