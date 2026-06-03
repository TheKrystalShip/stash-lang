using System;
using Stash.Registry.Contracts;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg scope</c> command for claiming and inspecting
/// package namespace scopes on the registry.
/// </summary>
/// <remarks>
/// <para>
/// Supported sub-commands: <c>claim</c> and <c>info</c>.
/// </para>
/// <para>
/// Grammar:
/// <list type="bullet">
///   <item><description><c>scope claim &lt;scope&gt; [--org &lt;org&gt;]</c></description></item>
///   <item><description><c>scope info &lt;scope&gt;</c></description></item>
/// </list>
/// </para>
/// <para>
/// Owner type is a bounded set — always resolved through <see cref="ScopeOwnerTypes"/>
/// constants, never inline literals.  For the user case the owner is resolved from
/// <c>GET /auth/whoami</c>.  The <c>--org</c> flag switches to
/// <see cref="ScopeOwnerTypes.Org"/> and sets the owner to the named org.
/// </para>
/// <para>
/// Under the Verified scope-ownership policy the <c>POST /scopes</c> response carries a
/// <see cref="ScopeChallengeBody"/> DNS-TXT challenge.  When a challenge is present the
/// command prints the DNS record instructions instead of reporting a completed claim.
/// </para>
/// </remarks>
public static class ScopeCommand
{
    /// <summary>
    /// Executes the scope command with the given arguments, resolving the registry
    /// client from the environment.
    /// </summary>
    /// <param name="args">Command-line arguments following <c>stash pkg scope</c>.</param>
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
    /// <param name="subcommand">The scope sub-command: <c>claim</c> or <c>info</c>.</param>
    /// <param name="subArgs">Positional and flag arguments following the sub-command.</param>
    /// <param name="client">The registry client to use for all HTTP calls.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when required positional arguments are missing or the sub-command is unknown.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the claim fails (e.g. the scope is already owned by another user) or
    /// the scope is not found on <c>info</c>.
    /// </exception>
    internal static void ExecuteCore(string subcommand, string[] subArgs, RegistryClient client)
    {
        switch (subcommand)
        {
            case "claim":
                Claim(subArgs, client);
                break;
            case "info":
                Info(subArgs, client);
                break;
            default:
                Console.Error.WriteLine($"Unknown scope subcommand: {subcommand}");
                Console.Error.WriteLine();
                PrintHelp();
                throw new ArgumentException(
                    $"Unknown scope subcommand: '{subcommand}'. Use claim or info.");
        }
    }

    // ── Sub-verb implementations ───────────────────────────────────────────────

    private static void Claim(string[] args, RegistryClient client)
    {
        // Parse: scope claim <scope> [--org <org>] [--registry <url>] [--token <value>]
        string? scopeName = null;
        string? orgName = null;

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--registry" || args[i] == "--token") && i + 1 < args.Length)
            {
                i++;
                continue;
            }

            if (args[i] == "--org" && i + 1 < args.Length)
            {
                orgName = args[i + 1];
                i++;
                continue;
            }

            if (!args[i].StartsWith("--") && scopeName == null)
            {
                scopeName = args[i];
            }
        }

        // Strip leading '@' if the user included it (e.g. @acme → acme)
        if (scopeName != null && scopeName.StartsWith("@", StringComparison.Ordinal))
        {
            scopeName = scopeName[1..];
        }

        if (string.IsNullOrEmpty(scopeName))
        {
            throw new ArgumentException("Usage: stash pkg scope claim <scope> [--org <org>]");
        }

        // Resolve owner_type and owner.
        // --org <name>  → owner_type=org, owner=<name>
        // (no --org)    → owner_type=user, owner=<authenticated username from whoami>
        string ownerType;
        string owner;

        if (orgName != null)
        {
            ownerType = ScopeOwnerTypes.Org;
            owner = orgName;
        }
        else
        {
            ownerType = ScopeOwnerTypes.User;
            // Resolve authenticated user — throws clearly if not logged in.
            var info = client.WhoamiDetailed();
            if (string.IsNullOrEmpty(info.Username))
            {
                throw new InvalidOperationException(
                    "Cannot resolve current username. Try logging in with 'stash pkg login'.");
            }
            owner = info.Username;
        }

        var result = client.ClaimScope(scopeName, ownerType, owner);
        if (result == null)
        {
            throw new InvalidOperationException(
                $"Failed to claim scope '{scopeName}'. " +
                "It may already be owned by another user or org. " +
                "Use 'stash pkg scope info <scope>' to check the current owner.");
        }

        // Verified policy: when the response includes a DNS-TXT challenge,
        // print the instructions instead of reporting a completed claim.
        if (result.Challenge != null)
        {
            Console.WriteLine($"Scope '{scopeName}' claim is pending verification.");
            Console.WriteLine("Add the following DNS TXT record to prove ownership:");
            Console.WriteLine();
            Console.WriteLine($"  Record name : {result.Challenge.RecordName}");
            Console.WriteLine($"  Record value: {result.Challenge.RecordValue}");
            Console.WriteLine($"  Expires at  : {result.Challenge.ExpiresAt}");
            Console.WriteLine();
            Console.WriteLine("Once the record is in place, run 'stash pkg scope verify' to complete the claim.");
            return;
        }

        // Normal (Claim/Open policy): claim completed.
        Console.WriteLine($"Scope '{scopeName}' claimed successfully.");
        Console.WriteLine($"  Owner type: {result.OwnerType}");
        Console.WriteLine($"  Owner     : {result.Owner}");
    }

    private static void Info(string[] args, RegistryClient client)
    {
        // Parse: scope info <scope> [--registry <url>] [--token <value>]
        string? scopeName = null;

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--registry" || args[i] == "--token") && i + 1 < args.Length)
            {
                i++;
                continue;
            }

            if (!args[i].StartsWith("--") && scopeName == null)
            {
                scopeName = args[i];
            }
        }

        // Strip leading '@' if the user included it
        if (scopeName != null && scopeName.StartsWith("@", StringComparison.Ordinal))
        {
            scopeName = scopeName[1..];
        }

        if (string.IsNullOrEmpty(scopeName))
        {
            throw new ArgumentException("Usage: stash pkg scope info <scope>");
        }

        var result = client.GetScope(scopeName);
        if (result == null)
        {
            throw new InvalidOperationException($"Scope '{scopeName}' not found.");
        }

        Console.WriteLine($"Scope: @{result.Scope}");
        Console.WriteLine($"  Owner type: {result.OwnerType}");
        Console.WriteLine($"  Owner     : {result.Owner ?? "(none)"}");

        if (!string.IsNullOrEmpty(result.State))
        {
            Console.WriteLine($"  State     : {result.State}");
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
        Console.WriteLine("Usage: stash pkg scope <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  claim <scope> [--org <org>]    Claim a scope (default: owned by the authenticated user)");
        Console.WriteLine("  info  <scope>                  Show scope owner and state");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --org <org>         Claim on behalf of an organization");
        Console.WriteLine("  --registry <url>    Registry URL");
        Console.WriteLine("  --token <value>     Use an existing token for authentication");
    }
}
