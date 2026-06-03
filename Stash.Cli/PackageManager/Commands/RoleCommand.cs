using System;
using System.Net.Http;
using Stash.Registry.Contracts;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg role</c> command for listing, assigning, and revoking
/// per-package roles on the registry.
/// </summary>
/// <remarks>
/// <para>
/// Supported sub-commands: <c>list</c>, <c>assign</c>, and <c>revoke</c>.
/// All three operate on the self-service publish route
/// <c>/packages/{scope}/{name}/roles</c> and require a token with
/// at least a <c>publish</c> ceiling.
/// </para>
/// <para>
/// Grammar:
/// <list type="bullet">
///   <item><description><c>role list &lt;pkg&gt;</c></description></item>
///   <item><description><c>role assign &lt;pkg&gt; &lt;user|team|org&gt; &lt;principal&gt; &lt;owner|maintainer|publisher|reader&gt;</c></description></item>
///   <item><description><c>role revoke &lt;pkg&gt; &lt;user|team|org&gt; &lt;principal&gt;</c></description></item>
/// </list>
/// </para>
/// </remarks>
public static class RoleCommand
{
    /// <summary>
    /// Executes the role command with the given arguments, resolving the registry
    /// client from the environment.
    /// </summary>
    /// <param name="args">Command-line arguments following <c>stash pkg role</c>.</param>
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
    /// <param name="subcommand">The role sub-command: <c>list</c>, <c>assign</c>, or <c>revoke</c>.</param>
    /// <param name="subArgs">Positional and flag arguments following the sub-command.</param>
    /// <param name="client">The registry client to use for all HTTP calls.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when required positional arguments are missing or the sub-command or
    /// principal-type/role value is unrecognised.
    /// </exception>
    internal static void ExecuteCore(string subcommand, string[] subArgs, RegistryClient client)
    {
        switch (subcommand)
        {
            case "list":
                List(subArgs, client);
                break;
            case "assign":
                Assign(subArgs, client);
                break;
            case "revoke":
                Revoke(subArgs, client);
                break;
            default:
                Console.Error.WriteLine($"Unknown role subcommand: {subcommand}");
                Console.Error.WriteLine();
                PrintHelp();
                throw new ArgumentException($"Unknown role subcommand: {subcommand}. Use list, assign, or revoke.");
        }
    }

    // ── Sub-verb implementations ───────────────────────────────────────────────

    private static void List(string[] args, RegistryClient client)
    {
        // Parse: role list <pkg> [--registry <url>] [--token <value>]
        string? packageName = null;
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--registry" || args[i] == "--token") && i + 1 < args.Length)
            {
                i++;
                continue;
            }

            if (!args[i].StartsWith("--") && packageName == null)
            {
                packageName = args[i];
            }
        }

        if (string.IsNullOrEmpty(packageName))
        {
            throw new ArgumentException("Usage: stash pkg role list <package>");
        }

        var result = client.GetRoles(packageName);
        if (result == null)
        {
            throw new InvalidOperationException($"Package '{packageName}' not found.");
        }

        if (result.Roles.Count == 0)
        {
            Console.WriteLine($"No roles assigned on {packageName}.");
            return;
        }

        Console.WriteLine($"Roles on {packageName}:");
        foreach (var roleEntry in result.Roles)
        {
            Console.WriteLine($"  {roleEntry.PrincipalType}/{roleEntry.PrincipalId}: {roleEntry.Role}");
        }
    }

    private static void Assign(string[] args, RegistryClient client)
    {
        // Parse: role assign <pkg> <user|team|org> <principal> <owner|maintainer|publisher|reader>
        // [--registry <url>] [--token <value>]
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

        if (positional.Count < 4)
        {
            throw new ArgumentException(
                "Usage: stash pkg role assign <package> <user|team|org> <principal> <owner|maintainer|publisher|reader>");
        }

        string packageName = positional[0];
        string principalType = positional[1];
        string principalId = positional[2];
        string role = positional[3];

        ValidatePrincipalType(principalType);
        ValidatePackageRole(role);

        bool ok = client.AssignRole(packageName, principalType, principalId, role);
        if (ok)
        {
            Console.WriteLine($"Assigned {principalType}/{principalId} the role '{role}' on {packageName}.");
        }
        else
        {
            throw new InvalidOperationException("Failed to assign role.");
        }
    }

    private static void Revoke(string[] args, RegistryClient client)
    {
        // Parse: role revoke <pkg> <user|team|org> <principal> [--registry <url>] [--token <value>]
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
            throw new ArgumentException(
                "Usage: stash pkg role revoke <package> <user|team|org> <principal>");
        }

        string packageName = positional[0];
        string principalType = positional[1];
        string principalId = positional[2];

        ValidatePrincipalType(principalType);

        // RevokeRole throws on 404/409 with the server's message; let the exception
        // surface through PackageCommands.Run's top-level catch → non-zero exit.
        bool ok = client.RevokeRole(packageName, principalType, principalId);
        if (ok)
        {
            Console.WriteLine($"Revoked {principalType}/{principalId}'s role on {packageName}.");
        }
    }

    // ── Validation helpers ─────────────────────────────────────────────────────

    private static void ValidatePrincipalType(string principalType)
    {
        bool valid = string.Equals(principalType, PrincipalTypes.User, StringComparison.Ordinal)
            || string.Equals(principalType, PrincipalTypes.Team, StringComparison.Ordinal)
            || string.Equals(principalType, PrincipalTypes.Org, StringComparison.Ordinal);

        if (!valid)
        {
            throw new ArgumentException(
                $"Unknown principal type: '{principalType}'. Valid types: {PrincipalTypes.User}, {PrincipalTypes.Team}, {PrincipalTypes.Org}.");
        }
    }

    private static void ValidatePackageRole(string role)
    {
        bool valid = string.Equals(role, PackageRoles.Owner, StringComparison.Ordinal)
            || string.Equals(role, PackageRoles.Maintainer, StringComparison.Ordinal)
            || string.Equals(role, PackageRoles.Publisher, StringComparison.Ordinal)
            || string.Equals(role, PackageRoles.Reader, StringComparison.Ordinal);

        if (!valid)
        {
            throw new ArgumentException(
                $"Unknown role: '{role}'. Valid roles: {PackageRoles.Owner}, {PackageRoles.Maintainer}, {PackageRoles.Publisher}, {PackageRoles.Reader}.");
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
        Console.WriteLine("Usage: stash pkg role <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  list   <package>                                            List roles on a package");
        Console.WriteLine("  assign <package> <user|team|org> <principal> <role>         Assign a role");
        Console.WriteLine("  revoke <package> <user|team|org> <principal>                Revoke a principal's role");
        Console.WriteLine();
        Console.WriteLine("Roles: owner, maintainer, publisher, reader");
        Console.WriteLine("Principal types: user, team, org");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --registry <url>    Registry URL");
        Console.WriteLine("  --token <value>     Use an existing token for authentication");
    }
}
