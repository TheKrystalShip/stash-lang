using System;
using Stash.Registry.Contracts;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg visibility set</c> command for changing the
/// visibility tier of a package on the registry.
/// </summary>
/// <remarks>
/// <para>
/// Only the <c>set</c> sub-verb is supported. There is no <c>visibility get</c>:
/// <c>PackageDetailResponse</c> exposes no <c>visibility</c> field and the registry
/// has no <c>GET .../visibility</c> route. The read-path gap is tracked in
/// <c>.kanban/0-backlog/bugs/Package visibility has no read path (PackageDetailResponse omits visibility).md</c>.
/// </para>
/// <para>
/// Grammar:
/// <list type="bullet">
///   <item><description><c>visibility set &lt;package&gt; &lt;public|internal|private&gt;</c></description></item>
/// </list>
/// </para>
/// <para>
/// The valid tier set is the bounded set <see cref="Visibilities"/> from
/// <c>Stash.Registry.Contracts</c> — no inline string literals at the comparison site.
/// </para>
/// </remarks>
public static class VisibilityCommand
{
    /// <summary>
    /// Executes the visibility command with the given arguments, resolving the registry
    /// client from the environment.
    /// </summary>
    /// <param name="args">Command-line arguments following <c>stash pkg visibility</c>.</param>
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
    /// <param name="subcommand">The visibility sub-command; currently only <c>set</c> is supported.</param>
    /// <param name="subArgs">Positional and flag arguments following the sub-command.</param>
    /// <param name="client">The registry client to use for all HTTP calls.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when required positional arguments are missing, the tier is unrecognised,
    /// or the sub-command itself is not <c>set</c>.
    /// </exception>
    internal static void ExecuteCore(string subcommand, string[] subArgs, RegistryClient client)
    {
        switch (subcommand)
        {
            case "set":
                Set(subArgs, client);
                break;
            default:
                Console.Error.WriteLine($"Unknown visibility subcommand: {subcommand}");
                Console.Error.WriteLine();
                PrintHelp();
                throw new ArgumentException(
                    $"Unknown visibility subcommand: '{subcommand}'. Only 'set' is supported. " +
                    "('get' is not available — see the backlog bug for the read-path gap.)");
        }
    }

    // ── Sub-verb implementation ────────────────────────────────────────────────

    private static void Set(string[] args, RegistryClient client)
    {
        // Parse: visibility set <pkg> <tier> [--registry <url>] [--token <value>]
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
            throw new ArgumentException(
                "Usage: stash pkg visibility set <package> <public|private|internal>");
        }

        string packageName = positional[0];
        string tier = positional[1];

        ValidateVisibilityTier(tier);

        // SetVisibility throws on any non-2xx with the server's ErrorResponse surfaced.
        client.SetVisibility(packageName, tier);
        Console.WriteLine($"Visibility of {packageName} set to '{tier}'.");
    }

    // ── Validation helpers ─────────────────────────────────────────────────────

    private static void ValidateVisibilityTier(string tier)
    {
        if (!VisibilityHelpers.IsValid(tier))
        {
            throw new ArgumentException(
                $"Unknown visibility tier: '{tier}'. Valid tiers: public, private, internal.");
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
        Console.WriteLine("Usage: stash pkg visibility <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine($"  set <package> <public|private|internal>    Change package visibility");
        Console.WriteLine();
        Console.WriteLine("Note: 'visibility get' is not available — PackageDetailResponse has no visibility");
        Console.WriteLine("field and there is no GET .../visibility route. See the deferred backlog bug:");
        Console.WriteLine("  .kanban/0-backlog/bugs/Package visibility has no read path (PackageDetailResponse omits visibility).md");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --registry <url>    Registry URL");
        Console.WriteLine("  --token <value>     Use an existing token for authentication");
    }
}
