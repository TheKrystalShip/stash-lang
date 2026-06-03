using System;
using System.IO;
using System.Threading.Tasks;
using Stash.Cli.PackageManager;
using Stash.Cli.PackageManager.Commands;
using Stash.Registry.Contracts;
using Stash.Tests.Registry.Authz;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// Integration tests for <see cref="OrgCommand"/> against a real in-process registry.
/// Each test drives the command via <see cref="OrgCommand.ExecuteCore"/> (the injectable
/// seam) with a <see cref="RegistryClient"/> backed by the
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> in-memory
/// transport.
/// </summary>
/// <remarks>
/// <para>
/// Test coverage:
/// <list type="bullet">
///   <item><description>org create: succeeds and prints flat metadata.</description></item>
///   <item><description>org create with --display-name: display_name appears in output.</description></item>
///   <item><description>org info: prints flat org metadata (id, name, display_name, created_at, created_by).</description></item>
///   <item><description>org info: not found throws with clear message.</description></item>
///   <item><description>org member add: succeeds (write path, no readback).</description></item>
///   <item><description>org member remove: succeeds (write path, no readback).</description></item>
///   <item><description>org team add: succeeds (write path, no readback).</description></item>
///   <item><description>org team member add: succeeds (write path, no readback).</description></item>
///   <item><description>Each subverb rejects missing required arguments with ArgumentException (non-zero exit).</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class PackageOrgCommandTests : RegistryAuthzTestBase
{
    private const string ApiBase = "http://localhost/api/v1";

    // ── org create ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creating an org succeeds and prints the org name and id.
    /// </summary>
    [Fact]
    public async Task Org_Create_Succeeds_PrintsOrgName()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(serverClient, "orgcreator1");
        var cli = new RegistryClient(ApiBase, serverClient, token: token);

        var stdout = new StringWriter();
        var prevOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            OrgCommand.ExecuteCore("create", ["org-create-test1"], cli);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        string output = stdout.ToString();
        Assert.Contains("org-create-test1", output);
        Assert.Contains("created", output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creating an org with --display-name includes the display name in the output.
    /// </summary>
    [Fact]
    public async Task Org_Create_WithDisplayName_PrintsDisplayName()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(serverClient, "orgcreator2");
        var cli = new RegistryClient(ApiBase, serverClient, token: token);

        var stdout = new StringWriter();
        var prevOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            OrgCommand.ExecuteCore("create", ["org-create-test2", "--display-name", "My Test Org"], cli);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        string output = stdout.ToString();
        Assert.Contains("org-create-test2", output);
        Assert.Contains("My Test Org", output);
    }

    // ── org info ───────────────────────────────────────────────────────────────

    /// <summary>
    /// After creating an org, <c>org info</c> prints all flat metadata fields
    /// (id, name, display_name, created_at, created_by) and the membership-deferral note.
    /// </summary>
    [Fact]
    public async Task Org_Info_PrintsFlatMetadata()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(serverClient, "orginfo-user1");
        var cli = new RegistryClient(ApiBase, serverClient, token: token);

        // Create the org first
        OrgCommand.ExecuteCore("create", ["org-info-test1"], cli);

        var stdout = new StringWriter();
        var prevOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            OrgCommand.ExecuteCore("info", ["org-info-test1"], cli);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        string output = stdout.ToString();
        Assert.Contains("org-info-test1", output);
        Assert.Contains("Id", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Display name", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Created at", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Created by", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orginfo-user1", output); // created_by field
        // Membership-deferral note must appear
        Assert.Contains("member and team listing is not available", output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Requesting <c>org info</c> on a non-existent org throws
    /// <see cref="InvalidOperationException"/> with a "not found" message.
    /// </summary>
    [Fact]
    public async Task Org_Info_NotFound_ThrowsWithMessage()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(serverClient, "orginfo-user2");
        var cli = new RegistryClient(ApiBase, serverClient, token: token);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            OrgCommand.ExecuteCore("info", ["no-such-org-xyzzy"], cli));

        Assert.Contains("no-such-org-xyzzy", ex.Message);
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── org member add ─────────────────────────────────────────────────────────

    /// <summary>
    /// Adding a registered user as an org member succeeds (write path — no readback).
    /// </summary>
    [Fact]
    public async Task Org_MemberAdd_Succeeds()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string creatorToken = await RegisterAndGetTokenAsync(serverClient, "org-creator-m1");
        var cli = new RegistryClient(ApiBase, serverClient, token: creatorToken);

        // Create org
        OrgCommand.ExecuteCore("create", ["org-member-add-test1"], cli);

        // Register a second user to add
        await RegisterAndGetTokenAsync(serverClient, "org-member-user1");

        // Add member — assert no exception and success output
        var stdout = new StringWriter();
        var prevOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            OrgCommand.ExecuteCore("member", ["add", "org-member-add-test1", "org-member-user1"], cli);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        string output = stdout.ToString();
        Assert.Contains("org-member-user1", output);
        Assert.Contains("org-member-add-test1", output);
    }

    /// <summary>
    /// Adding a member with an explicit <c>--role owner</c> flag succeeds and mentions the role.
    /// </summary>
    [Fact]
    public async Task Org_MemberAdd_WithRoleOwner_Succeeds()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string creatorToken = await RegisterAndGetTokenAsync(serverClient, "org-creator-m2");
        var cli = new RegistryClient(ApiBase, serverClient, token: creatorToken);

        OrgCommand.ExecuteCore("create", ["org-member-add-test2"], cli);
        await RegisterAndGetTokenAsync(serverClient, "org-member-user2");

        var stdout = new StringWriter();
        var prevOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            OrgCommand.ExecuteCore("member",
                ["add", "org-member-add-test2", "org-member-user2", "--role", OrgRoles.Owner], cli);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        string output = stdout.ToString();
        Assert.Contains("org-member-user2", output);
        Assert.Contains(OrgRoles.Owner, output);
    }

    // ── org member remove ──────────────────────────────────────────────────────

    /// <summary>
    /// Removing an org member succeeds (write path — no readback).
    /// </summary>
    [Fact]
    public async Task Org_MemberRemove_Succeeds()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string creatorToken = await RegisterAndGetTokenAsync(serverClient, "org-creator-mr1");
        var cli = new RegistryClient(ApiBase, serverClient, token: creatorToken);

        OrgCommand.ExecuteCore("create", ["org-member-remove-test1"], cli);
        await RegisterAndGetTokenAsync(serverClient, "org-rm-user1");

        // Add first, then remove
        OrgCommand.ExecuteCore("member", ["add", "org-member-remove-test1", "org-rm-user1"], cli);

        var stdout = new StringWriter();
        var prevOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            OrgCommand.ExecuteCore("member", ["remove", "org-member-remove-test1", "org-rm-user1"], cli);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        string output = stdout.ToString();
        Assert.Contains("org-rm-user1", output);
        Assert.Contains("org-member-remove-test1", output);
        Assert.Contains("Removed", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── org team add ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creating a team within an org succeeds (write path — no readback).
    /// </summary>
    [Fact]
    public async Task Org_TeamAdd_Succeeds()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string creatorToken = await RegisterAndGetTokenAsync(serverClient, "org-creator-t1");
        var cli = new RegistryClient(ApiBase, serverClient, token: creatorToken);

        OrgCommand.ExecuteCore("create", ["org-team-add-test1"], cli);

        var stdout = new StringWriter();
        var prevOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            OrgCommand.ExecuteCore("team", ["add", "org-team-add-test1", "alpha"], cli);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        string output = stdout.ToString();
        Assert.Contains("alpha", output);
        Assert.Contains("org-team-add-test1", output);
    }

    // ── org team member add ────────────────────────────────────────────────────

    /// <summary>
    /// Adding a user to a team within an org succeeds (write path — no readback).
    /// </summary>
    [Fact]
    public async Task Org_TeamMemberAdd_Succeeds()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string creatorToken = await RegisterAndGetTokenAsync(serverClient, "org-creator-tm1");
        var cli = new RegistryClient(ApiBase, serverClient, token: creatorToken);

        OrgCommand.ExecuteCore("create", ["org-team-member-add-test1"], cli);
        OrgCommand.ExecuteCore("team", ["add", "org-team-member-add-test1", "beta"], cli);
        await RegisterAndGetTokenAsync(serverClient, "org-tm-user1");

        var stdout = new StringWriter();
        var prevOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            OrgCommand.ExecuteCore("team",
                ["member", "add", "org-team-member-add-test1", "beta", "org-tm-user1"], cli);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        string output = stdout.ToString();
        Assert.Contains("org-tm-user1", output);
        Assert.Contains("beta", output);
        Assert.Contains("org-team-member-add-test1", output);
    }

    // ── Argument-validation rejections ────────────────────────────────────────

    /// <summary>
    /// <c>org create</c> without an org name throws <see cref="ArgumentException"/>.
    /// </summary>
    [Fact]
    public async Task Org_Create_MissingOrg_ThrowsArgumentException()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(serverClient, "orgarg-create");
        var cli = new RegistryClient(ApiBase, serverClient, token: token);

        var ex = Assert.Throws<ArgumentException>(() =>
            OrgCommand.ExecuteCore("create", [], cli));

        Assert.Contains("Usage", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>org info</c> without an org name throws <see cref="ArgumentException"/>.
    /// </summary>
    [Fact]
    public async Task Org_Info_MissingOrg_ThrowsArgumentException()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(serverClient, "orgarg-info");
        var cli = new RegistryClient(ApiBase, serverClient, token: token);

        var ex = Assert.Throws<ArgumentException>(() =>
            OrgCommand.ExecuteCore("info", [], cli));

        Assert.Contains("Usage", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>org member add</c> without required positional args throws <see cref="ArgumentException"/>.
    /// </summary>
    [Fact]
    public async Task Org_MemberAdd_MissingArgs_ThrowsArgumentException()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(serverClient, "orgarg-memberadd");
        var cli = new RegistryClient(ApiBase, serverClient, token: token);

        var ex = Assert.Throws<ArgumentException>(() =>
            OrgCommand.ExecuteCore("member", ["add"], cli));

        Assert.Contains("Usage", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>org member remove</c> without required positional args throws <see cref="ArgumentException"/>.
    /// </summary>
    [Fact]
    public async Task Org_MemberRemove_MissingArgs_ThrowsArgumentException()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(serverClient, "orgarg-memberrm");
        var cli = new RegistryClient(ApiBase, serverClient, token: token);

        var ex = Assert.Throws<ArgumentException>(() =>
            OrgCommand.ExecuteCore("member", ["remove"], cli));

        Assert.Contains("Usage", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>org team add</c> without required positional args throws <see cref="ArgumentException"/>.
    /// </summary>
    [Fact]
    public async Task Org_TeamAdd_MissingArgs_ThrowsArgumentException()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(serverClient, "orgarg-teamadd");
        var cli = new RegistryClient(ApiBase, serverClient, token: token);

        var ex = Assert.Throws<ArgumentException>(() =>
            OrgCommand.ExecuteCore("team", ["add"], cli));

        Assert.Contains("Usage", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>org team member add</c> without required positional args throws <see cref="ArgumentException"/>.
    /// </summary>
    [Fact]
    public async Task Org_TeamMemberAdd_MissingArgs_ThrowsArgumentException()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(serverClient, "orgarg-tmadd");
        var cli = new RegistryClient(ApiBase, serverClient, token: token);

        var ex = Assert.Throws<ArgumentException>(() =>
            OrgCommand.ExecuteCore("team", ["member", "add"], cli));

        Assert.Contains("Usage", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Unknown top-level org subcommand throws <see cref="ArgumentException"/> with a clear message.
    /// </summary>
    [Fact]
    public async Task Org_UnknownSubcommand_ThrowsArgumentException()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(serverClient, "orgarg-unknown");
        var cli = new RegistryClient(ApiBase, serverClient, token: token);

        var ex = Assert.Throws<ArgumentException>(() =>
            OrgCommand.ExecuteCore("delete", [], cli));

        Assert.Contains("delete", ex.Message);
        Assert.Contains("Unknown org subcommand", ex.Message);
    }

    /// <summary>
    /// <c>--role</c> with an invalid value throws <see cref="ArgumentException"/>.
    /// Only <see cref="OrgRoles.Owner"/> and <see cref="OrgRoles.Member"/> are valid.
    /// </summary>
    [Fact]
    public async Task Org_MemberAdd_InvalidRole_ThrowsArgumentException()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var serverClient = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(serverClient, "orgarg-badrole");
        var cli = new RegistryClient(ApiBase, serverClient, token: token);

        var ex = Assert.Throws<ArgumentException>(() =>
            OrgCommand.ExecuteCore("member",
                ["add", "some-org", "some-user", "--role", "admin"], cli));

        Assert.Contains("admin", ex.Message);
        Assert.Contains("Unknown org role", ex.Message);
    }
}
