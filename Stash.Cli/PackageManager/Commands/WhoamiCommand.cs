using System;
using System.Collections.Generic;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg whoami</c> command for showing the authenticated
/// username for the configured registry.
/// </summary>
public static class WhoamiCommand
{
    /// <summary>
    /// Executes the whoami command with the given arguments.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments following <c>stash pkg whoami</c>.  The optional
    /// <c>--registry &lt;url&gt;</c> flag selects a specific registry; the optional
    /// <c>--verbose</c> / <c>-v</c> flag prints additional user details.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when not logged in, the registry is unreachable, or the response is
    /// malformed.
    /// </exception>
    public static void Execute(string[] args)
    {
        bool verbose = false;
        var remaining = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--verbose" || args[i] == "-v")
            {
                verbose = true;
            }
            else
            {
                remaining.Add(args[i]);
            }
        }

        var (registryUrl, _) = RegistryResolver.Resolve(remaining.ToArray(), requireExplicit: false);

        var config = UserConfig.Load();
        var entry = config.GetEntry(registryUrl);
        var client = new RegistryClient(registryUrl, entry?.Token, entry?.RefreshToken,
            entry?.ExpiresAt, entry?.MachineId, registryUrl);

        var info = client.WhoamiDetailed();

        if (!verbose)
        {
            Console.WriteLine(info.Username);
            return;
        }

        Console.WriteLine(info.Username);
        Console.WriteLine($"role: {info.Role ?? "(none)"}");
        Console.WriteLine($"registry: {registryUrl}");
    }
}
