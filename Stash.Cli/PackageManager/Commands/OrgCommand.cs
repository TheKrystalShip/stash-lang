using System;
using Stash.Registry.Contracts;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg org</c> command for creating and inspecting organizations
/// and managing their members and teams on the registry.
/// </summary>
/// <remarks>
/// <para>
/// Supported sub-commands and grammar:
/// <list type="bullet">
///   <item><description><c>org create &lt;org&gt; [--display-name &lt;name&gt;]</c></description></item>
///   <item><description><c>org info &lt;org&gt;</c></description></item>
///   <item><description><c>org member add &lt;org&gt; &lt;username&gt; [--role owner|member]</c></description></item>
///   <item><description><c>org member remove &lt;org&gt; &lt;username&gt;</c></description></item>
///   <item><description><c>org team add &lt;org&gt; &lt;team&gt;</c></description></item>
///   <item><description><c>org team member add &lt;org&gt; &lt;team&gt; &lt;username&gt;</c></description></item>
/// </list>
/// </para>
/// <para>
/// All write operations require a publish-ceiling token.
/// <c>org info</c> is an anonymous read (no token required).
/// </para>
/// <para>
/// The <c>--role</c> option for <c>org member add</c> is a bounded set; valid values are
/// referenced through <see cref="OrgRoles"/> constants — never inline literals.
/// </para>
/// <para>
/// Membership and team listing is not available: <see cref="OrgDetailResponse"/> carries
/// flat metadata only and there is no <c>GET .../members</c> route. This is tracked in
/// <c>.kanban/0-backlog/bugs/Org members and teams have no read path (OrgDetailResponse omits them).md</c>.
/// </para>
/// </remarks>
public static class OrgCommand
{
    /// <summary>
    /// Executes the org command with the given arguments, resolving the registry
    /// client from the environment.
    /// </summary>
    /// <param name="args">Command-line arguments following <c>stash pkg org</c>.</param>
    public static void Execute(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return;
        }

        string subcommand = args[0];
        string[] subArgs = args[1..];

        if (subcommand is "help" or "--help" or "-h")
        {
            PrintHelp();
            return;
        }

        var (registryUrl, _) = RegistryResolver.Resolve(args);
        var client = ResolveClient(registryUrl, ExtractCliToken(args));

        ExecuteCore(subcommand, subArgs, client);
    }

    /// <summary>
    /// Core logic that drives the sub-verb dispatch against a pre-built
    /// <see cref="RegistryClient"/>. Used directly by integration tests.
    /// </summary>
    /// <param name="subcommand">
    /// The org sub-command: <c>create</c>, <c>info</c>, <c>member</c>, or <c>team</c>.
    /// </param>
    /// <param name="subArgs">Positional and flag arguments following the sub-command.</param>
    /// <param name="client">The registry client to use for all HTTP calls.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when required positional arguments are missing or the sub-command (or a
    /// nested sub-verb) is unknown.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an operation fails on the server side.
    /// </exception>
    internal static void ExecuteCore(string subcommand, string[] subArgs, RegistryClient client)
    {
        switch (subcommand)
        {
            case "create":
                Create(subArgs, client);
                break;
            case "info":
                Info(subArgs, client);
                break;
            case "member":
                ExecuteMemberCore(subArgs, client);
                break;
            case "team":
                ExecuteTeamCore(subArgs, client);
                break;
            default:
                Console.Error.WriteLine($"Unknown org subcommand: {subcommand}");
                Console.Error.WriteLine();
                PrintHelp();
                throw new ArgumentException(
                    $"Unknown org subcommand: '{subcommand}'. Use create, info, member, or team.");
        }
    }

    // ── Nested dispatch: org member ────────────────────────────────────────────

    private static void ExecuteMemberCore(string[] args, RegistryClient client)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Missing member sub-verb. Use: add, remove");
            PrintMemberHelp();
            throw new ArgumentException("Missing org member sub-verb. Use 'add' or 'remove'.");
        }

        string verb = args[0];
        string[] verbArgs = args[1..];

        switch (verb)
        {
            case "add":
                MemberAdd(verbArgs, client);
                break;
            case "remove":
                MemberRemove(verbArgs, client);
                break;
            default:
                Console.Error.WriteLine($"Unknown org member sub-verb: {verb}");
                Console.Error.WriteLine();
                PrintMemberHelp();
                throw new ArgumentException(
                    $"Unknown org member sub-verb: '{verb}'. Use 'add' or 'remove'.");
        }
    }

    // ── Nested dispatch: org team ──────────────────────────────────────────────

    private static void ExecuteTeamCore(string[] args, RegistryClient client)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Missing team sub-verb. Use: add, member");
            PrintTeamHelp();
            throw new ArgumentException("Missing org team sub-verb. Use 'add' or 'member'.");
        }

        string verb = args[0];
        string[] verbArgs = args[1..];

        switch (verb)
        {
            case "add":
                TeamAdd(verbArgs, client);
                break;
            case "member":
                ExecuteTeamMemberCore(verbArgs, client);
                break;
            default:
                Console.Error.WriteLine($"Unknown org team sub-verb: {verb}");
                Console.Error.WriteLine();
                PrintTeamHelp();
                throw new ArgumentException(
                    $"Unknown org team sub-verb: '{verb}'. Use 'add' or 'member'.");
        }
    }

    // ── Nested dispatch: org team member ──────────────────────────────────────

    private static void ExecuteTeamMemberCore(string[] args, RegistryClient client)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Missing team member sub-verb. Use: add");
            PrintTeamMemberHelp();
            throw new ArgumentException("Missing org team member sub-verb. Use 'add'.");
        }

        string verb = args[0];
        string[] verbArgs = args[1..];

        switch (verb)
        {
            case "add":
                TeamMemberAdd(verbArgs, client);
                break;
            default:
                Console.Error.WriteLine($"Unknown org team member sub-verb: {verb}");
                Console.Error.WriteLine();
                PrintTeamMemberHelp();
                throw new ArgumentException(
                    $"Unknown org team member sub-verb: '{verb}'. Use 'add'.");
        }
    }

    // ── Sub-verb implementations ───────────────────────────────────────────────

    private static void Create(string[] args, RegistryClient client)
    {
        // Parse: org create <org> [--display-name <name>] [--registry <url>] [--token <value>]
        string? orgName = null;
        string? displayName = null;

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--registry" || args[i] == "--token") && i + 1 < args.Length)
            {
                i++;
                continue;
            }

            if (args[i] == "--display-name" && i + 1 < args.Length)
            {
                displayName = args[i + 1];
                i++;
                continue;
            }

            if (!args[i].StartsWith("--") && orgName == null)
            {
                orgName = args[i];
            }
        }

        if (string.IsNullOrEmpty(orgName))
        {
            throw new ArgumentException("Usage: stash pkg org create <org> [--display-name <name>]");
        }

        var result = client.CreateOrg(orgName, displayName);
        if (result == null)
        {
            throw new InvalidOperationException(
                $"Failed to create organization '{orgName}'. " +
                "The name may already be taken or the token lacks sufficient permissions.");
        }

        Console.WriteLine($"Organization '{result.Name}' created successfully.");
        Console.WriteLine($"  Id          : {result.Id}");
        if (!string.IsNullOrEmpty(result.DisplayName))
        {
            Console.WriteLine($"  Display name: {result.DisplayName}");
        }
        Console.WriteLine($"  Created at  : {result.CreatedAt}");
        Console.WriteLine($"  Created by  : {result.CreatedBy}");
    }

    private static void Info(string[] args, RegistryClient client)
    {
        // Parse: org info <org> [--registry <url>] [--token <value>]
        string? orgName = null;

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--registry" || args[i] == "--token") && i + 1 < args.Length)
            {
                i++;
                continue;
            }

            if (!args[i].StartsWith("--") && orgName == null)
            {
                orgName = args[i];
            }
        }

        if (string.IsNullOrEmpty(orgName))
        {
            throw new ArgumentException("Usage: stash pkg org info <org>");
        }

        var result = client.GetOrg(orgName);
        if (result == null)
        {
            throw new InvalidOperationException($"Organization '{orgName}' not found.");
        }

        Console.WriteLine($"Organization: {result.Name}");
        Console.WriteLine($"  Id          : {result.Id}");
        Console.WriteLine($"  Display name: {result.DisplayName ?? "(none)"}");
        Console.WriteLine($"  Created at  : {result.CreatedAt}");
        Console.WriteLine($"  Created by  : {result.CreatedBy}");
        Console.WriteLine();
        Console.WriteLine("Note: member and team listing is not available — OrgDetailResponse carries flat");
        Console.WriteLine("metadata only and there is no server read path for membership.");
        Console.WriteLine("See: .kanban/0-backlog/bugs/Org members and teams have no read path (OrgDetailResponse omits them).md");
    }

    private static void MemberAdd(string[] args, RegistryClient client)
    {
        // Parse: org member add <org> <username> [--role owner|member] [--registry <url>] [--token <value>]
        var positional = new System.Collections.Generic.List<string>();
        string? role = null;

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--registry" || args[i] == "--token") && i + 1 < args.Length)
            {
                i++;
                continue;
            }

            if (args[i] == "--role" && i + 1 < args.Length)
            {
                role = args[i + 1];
                i++;
                continue;
            }

            if (!args[i].StartsWith("--"))
            {
                positional.Add(args[i]);
            }
        }

        if (positional.Count < 2)
        {
            throw new ArgumentException(
                $"Usage: stash pkg org member add <org> <username> [--role {OrgRoles.Owner}|{OrgRoles.Member}]");
        }

        string orgName = positional[0];
        string username = positional[1];

        // Validate --role if supplied (bounded set: owner or member)
        if (role != null)
        {
            ValidateOrgRole(role);
        }

        bool ok = client.AddOrgMember(orgName, username, role);
        if (ok)
        {
            string assignedRole = role ?? OrgRoles.Member;
            Console.WriteLine($"Added '{username}' to organization '{orgName}' with role '{assignedRole}'.");
        }
        else
        {
            throw new InvalidOperationException(
                $"Failed to add '{username}' to organization '{orgName}'. " +
                "Check that the user exists and the token has sufficient permissions.");
        }
    }

    private static void MemberRemove(string[] args, RegistryClient client)
    {
        // Parse: org member remove <org> <username> [--registry <url>] [--token <value>]
        var positional = new System.Collections.Generic.List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--registry" || args[i] == "--token") && i + 1 < args.Length)
            {
                i++;
                continue;
            }

            if (!args[i].StartsWith("--"))
            {
                positional.Add(args[i]);
            }
        }

        if (positional.Count < 2)
        {
            throw new ArgumentException("Usage: stash pkg org member remove <org> <username>");
        }

        string orgName = positional[0];
        string username = positional[1];

        bool ok = client.RemoveOrgMember(orgName, username);
        if (ok)
        {
            Console.WriteLine($"Removed '{username}' from organization '{orgName}'.");
        }
        else
        {
            throw new InvalidOperationException(
                $"Failed to remove '{username}' from organization '{orgName}'. " +
                "Check that the user is a member and the token has sufficient permissions.");
        }
    }

    private static void TeamAdd(string[] args, RegistryClient client)
    {
        // Parse: org team add <org> <team> [--registry <url>] [--token <value>]
        var positional = new System.Collections.Generic.List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--registry" || args[i] == "--token") && i + 1 < args.Length)
            {
                i++;
                continue;
            }

            if (!args[i].StartsWith("--"))
            {
                positional.Add(args[i]);
            }
        }

        if (positional.Count < 2)
        {
            throw new ArgumentException("Usage: stash pkg org team add <org> <team>");
        }

        string orgName = positional[0];
        string teamName = positional[1];

        var result = client.CreateTeam(orgName, teamName);
        if (result == null)
        {
            throw new InvalidOperationException(
                $"Failed to create team '{teamName}' in organization '{orgName}'. " +
                "Check that the org exists and the token has sufficient permissions.");
        }

        Console.WriteLine($"Team '{result.Name}' created in organization '{orgName}'.");
        Console.WriteLine($"  Id        : {result.Id}");
        Console.WriteLine($"  Created at: {result.CreatedAt}");
    }

    private static void TeamMemberAdd(string[] args, RegistryClient client)
    {
        // Parse: org team member add <org> <team> <username> [--registry <url>] [--token <value>]
        var positional = new System.Collections.Generic.List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--registry" || args[i] == "--token") && i + 1 < args.Length)
            {
                i++;
                continue;
            }

            if (!args[i].StartsWith("--"))
            {
                positional.Add(args[i]);
            }
        }

        if (positional.Count < 3)
        {
            throw new ArgumentException("Usage: stash pkg org team member add <org> <team> <username>");
        }

        string orgName = positional[0];
        string teamName = positional[1];
        string username = positional[2];

        bool ok = client.AddTeamMember(orgName, teamName, username);
        if (ok)
        {
            Console.WriteLine($"Added '{username}' to team '{teamName}' in organization '{orgName}'.");
        }
        else
        {
            throw new InvalidOperationException(
                $"Failed to add '{username}' to team '{teamName}' in organization '{orgName}'. " +
                "Check that the org, team, and user exist and the token has sufficient permissions.");
        }
    }

    // ── Validation helpers ─────────────────────────────────────────────────────

    private static void ValidateOrgRole(string role)
    {
        bool valid = string.Equals(role, OrgRoles.Owner, StringComparison.Ordinal)
            || string.Equals(role, OrgRoles.Member, StringComparison.Ordinal);

        if (!valid)
        {
            throw new ArgumentException(
                $"Unknown org role: '{role}'. Valid roles: {OrgRoles.Owner}, {OrgRoles.Member}.");
        }
    }

    // ── Client resolution ──────────────────────────────────────────────────────

    private static string? ExtractCliToken(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--token")
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static RegistryClient ResolveClient(string registryUrl, string? cliToken)
    {
        string? token = cliToken ?? Environment.GetEnvironmentVariable("STASH_TOKEN");

        if (!string.IsNullOrEmpty(token))
        {
            return new RegistryClient(registryUrl, token);
        }

        var config = UserConfig.Load();
        var entry = config.GetEntry(registryUrl);
        token = entry?.Token;

        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                "Not logged in. Run 'stash pkg login', set the STASH_TOKEN environment variable, or use --token.");
        }

        return new RegistryClient(registryUrl, token, entry!.RefreshToken, entry.ExpiresAt, entry.MachineId, registryUrl);
    }

    // ── Help ───────────────────────────────────────────────────────────────────

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: stash pkg org <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  create <org> [--display-name <name>]         Create a new organization");
        Console.WriteLine("  info   <org>                                  Show flat org metadata (id, name, display_name, created_at, created_by)");
        Console.WriteLine("  member add    <org> <username> [--role ...]   Add a user to the org");
        Console.WriteLine("  member remove <org> <username>                Remove a user from the org");
        Console.WriteLine("  team   add    <org> <team>                    Create a new team within the org");
        Console.WriteLine("  team   member add <org> <team> <username>     Add a user to a team");
        Console.WriteLine();
        Console.WriteLine($"  --role <{OrgRoles.Owner}|{OrgRoles.Member}>   Org role for member add (default: {OrgRoles.Member})");
        Console.WriteLine();
        Console.WriteLine("Note: 'org info' shows flat metadata only. Member and team listing is not available —");
        Console.WriteLine("OrgDetailResponse has no membership fields and there is no GET .../members route.");
        Console.WriteLine("See: .kanban/0-backlog/bugs/Org members and teams have no read path (OrgDetailResponse omits them).md");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --registry <url>    Registry URL");
        Console.WriteLine("  --token <value>     Use an existing token for authentication");
    }

    private static void PrintMemberHelp()
    {
        Console.WriteLine("Usage: stash pkg org member <add|remove> [options]");
        Console.WriteLine();
        Console.WriteLine("  add    <org> <username> [--role owner|member]   Add a user to the org");
        Console.WriteLine("  remove <org> <username>                         Remove a user from the org");
    }

    private static void PrintTeamHelp()
    {
        Console.WriteLine("Usage: stash pkg org team <add|member> [options]");
        Console.WriteLine();
        Console.WriteLine("  add    <org> <team>                   Create a new team within the org");
        Console.WriteLine("  member add <org> <team> <username>    Add a user to a team");
    }

    private static void PrintTeamMemberHelp()
    {
        Console.WriteLine("Usage: stash pkg org team member add <org> <team> <username>");
    }
}
