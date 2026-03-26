using System;
using System.Collections.Generic;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg deprecate</c> and <c>stash pkg undeprecate</c>
/// commands for marking packages or versions as deprecated on the registry.
/// </summary>
/// <remarks>
/// <para>
/// Supported forms:
/// <list type="bullet">
///   <item><c>stash pkg deprecate &lt;package&gt; --message "..."</c> — deprecate an entire package</item>
///   <item><c>stash pkg deprecate &lt;package&gt; &lt;version&gt; --message "..."</c> — deprecate a specific version</item>
///   <item><c>stash pkg undeprecate &lt;package&gt;</c> — remove deprecation from a package</item>
///   <item><c>stash pkg undeprecate &lt;package&gt; &lt;version&gt;</c> — remove deprecation from a version</item>
/// </list>
/// </para>
/// </remarks>
public static class DeprecateCommand
{
    /// <summary>
    /// Executes the deprecate command.
    /// </summary>
    public static void Execute(string[] args)
    {
        // Parse named flags
        var positionalArgs = new List<string>();
        string? cliToken = null;
        string? message = null;
        string? alternative = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--registry" && i + 1 < args.Length)
            {
                i++; // skip value, handled by RegistryResolver
            }
            else if (args[i] == "--token" && i + 1 < args.Length)
            {
                cliToken = args[++i];
            }
            else if ((args[i] == "--message" || args[i] == "-m") && i + 1 < args.Length)
            {
                message = args[++i];
            }
            else if (args[i] == "--alternative" && i + 1 < args.Length)
            {
                alternative = args[++i];
            }
            else
            {
                positionalArgs.Add(args[i]);
            }
        }

        if (positionalArgs.Count < 1)
        {
            throw new ArgumentException(
                "Usage: stash pkg deprecate <package> [version] --message \"...\" [--alternative \"...\"]");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A deprecation message is required. Use --message \"...\" or -m \"...\".");
        }

        string packageName = positionalArgs[0];
        string? version = positionalArgs.Count >= 2 ? positionalArgs[1] : null;

        if (alternative != null && version != null)
        {
            throw new ArgumentException("--alternative is only supported for whole-package deprecation, not for a specific version.");
        }

        // Resolve auth
        string registryUrl = UserConfig.ResolveRegistryUrl(RegistryResolver.ParseRegistryFlag(args));
        string? token = cliToken ?? Environment.GetEnvironmentVariable("STASH_TOKEN");
        RegistryEntry? entry = null;
        if (string.IsNullOrEmpty(token))
        {
            var config = UserConfig.Load();
            entry = config.GetEntry(registryUrl);
            token = entry?.Token;
        }

        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                "Not logged in. Run 'stash pkg login', set the STASH_TOKEN environment variable, or use --token.");
        }

        var client = new RegistryClient(registryUrl, token, entry?.RefreshToken,
            entry?.ExpiresAt, entry?.MachineId, registryUrl);

        if (version != null)
        {
            client.DeprecateVersion(packageName, version, message);
            Console.WriteLine($"Deprecated {packageName}@{version}.");
        }
        else
        {
            client.DeprecatePackage(packageName, message, alternative);
            Console.WriteLine($"Deprecated {packageName}.");
        }

        if (alternative != null && version == null)
        {
            Console.WriteLine($"Suggested alternative: {alternative}");
        }
    }

    /// <summary>
    /// Executes the undeprecate command.
    /// </summary>
    public static void ExecuteUndo(string[] args)
    {
        var positionalArgs = new List<string>();
        string? cliToken = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--registry" && i + 1 < args.Length)
            {
                i++;
            }
            else if (args[i] == "--token" && i + 1 < args.Length)
            {
                cliToken = args[++i];
            }
            else
            {
                positionalArgs.Add(args[i]);
            }
        }

        if (positionalArgs.Count < 1)
        {
            throw new ArgumentException("Usage: stash pkg undeprecate <package> [version]");
        }

        string packageName = positionalArgs[0];
        string? version = positionalArgs.Count >= 2 ? positionalArgs[1] : null;

        string registryUrl = UserConfig.ResolveRegistryUrl(RegistryResolver.ParseRegistryFlag(args));
        string? token = cliToken ?? Environment.GetEnvironmentVariable("STASH_TOKEN");
        RegistryEntry? entry = null;
        if (string.IsNullOrEmpty(token))
        {
            var config = UserConfig.Load();
            entry = config.GetEntry(registryUrl);
            token = entry?.Token;
        }

        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                "Not logged in. Run 'stash pkg login', set the STASH_TOKEN environment variable, or use --token.");
        }

        var client = new RegistryClient(registryUrl, token, entry?.RefreshToken,
            entry?.ExpiresAt, entry?.MachineId, registryUrl);

        if (version != null)
        {
            client.UndeprecateVersion(packageName, version);
            Console.WriteLine($"Removed deprecation from {packageName}@{version}.");
        }
        else
        {
            client.UndeprecatePackage(packageName);
            Console.WriteLine($"Removed deprecation from {packageName}.");
        }
    }
}
