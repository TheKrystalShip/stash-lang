using System;

namespace Stash.Cli.PackageManager;

/// <summary>
/// Resolves the target registry URL for a CLI command from command-line flags or
/// the user's persisted <see cref="UserConfig"/>.
/// </summary>
/// <remarks>
/// <para>
/// The resolution order is:
/// <list type="number">
///   <item><description>An explicit <c>--registry &lt;url&gt;</c> flag on the command line.</description></item>
///   <item><description>The <see cref="UserConfig.DefaultRegistry"/> stored in <c>~/.stash/config.json</c>.</description></item>
/// </list>
/// </para>
/// </remarks>
public static class RegistryResolver
{
    /// <summary>
    /// Determines the registry URL to use for the current command, optionally
    /// requiring an explicit <c>--registry</c> flag.
    /// </summary>
    /// <param name="args">The raw command-line argument array passed to the CLI entry point.</param>
    /// <param name="requireExplicit">
    /// When <c>true</c>, the method throws if <c>--registry</c> is not present on
    /// the command line and does not fall back to the configured default.
    /// </param>
    /// <returns>
    /// A tuple of (<c>registryUrl</c>, <c>wasExplicit</c>) where <c>wasExplicit</c>
    /// is <c>true</c> when the URL was sourced from the <c>--registry</c> flag rather
    /// than from the user config.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="requireExplicit"/> is <c>true</c> and no
    /// <c>--registry</c> flag is found, or when no default registry is configured.
    /// </exception>
    public static (string registryUrl, bool wasExplicit) Resolve(string[] args, bool requireExplicit = false)
    {
        string? explicitUrl = ParseRegistryFlag(args);

        if (explicitUrl is not null)
        {
            Console.WriteLine($"Registry: {explicitUrl}");
            return (explicitUrl, true);
        }

        if (requireExplicit)
        {
            throw new InvalidOperationException("The --registry flag is required for this command.");
        }

        UserConfig config = UserConfig.Load();

        if (!string.IsNullOrEmpty(config.DefaultRegistry))
        {
            Console.WriteLine($"Registry: {config.DefaultRegistry}");
            return (config.DefaultRegistry, false);
        }

        throw new InvalidOperationException("No default registry configured. Run 'stash pkg login --registry <url>' to set one.");
    }

    /// <summary>
    /// Scans the argument array for a <c>--registry</c> flag and returns the
    /// following element as the registry URL.
    /// </summary>
    /// <param name="args">The raw command-line argument array to scan.</param>
    /// <returns>
    /// The registry URL string that follows <c>--registry</c>, or <c>null</c> when
    /// the flag is not present.
    /// </returns>
    public static string? ParseRegistryFlag(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--registry")
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
