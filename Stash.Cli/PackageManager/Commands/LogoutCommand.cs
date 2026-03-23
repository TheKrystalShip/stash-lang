using System;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg logout</c> command for removing stored credentials
/// for a registry from the user configuration.
/// </summary>
/// <remarks>
/// <para>
/// Calls <see cref="UserConfig.RemoveToken"/> to delete the token for the target
/// registry.  If that registry was the configured default, the default is also
/// cleared.
/// </para>
/// </remarks>
public static class LogoutCommand
{
    /// <summary>
    /// Executes the logout command with the given arguments.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments following <c>stash pkg logout</c>.  The
    /// <c>--registry &lt;url&gt;</c> flag is required and identifies the registry
    /// to log out of.
    /// </param>
    public static void Execute(string[] args)
    {
        var (registryUrl, _) = RegistryResolver.Resolve(args, requireExplicit: true);

        var config = UserConfig.Load();
        config.RemoveToken(registryUrl);
        Console.WriteLine($"Logged out from {registryUrl}.");

        if (string.Equals(config.DefaultRegistry, registryUrl, StringComparison.OrdinalIgnoreCase))
        {
            config.DefaultRegistry = null;
            config.Save();
            Console.WriteLine($"  Cleared default registry (was {registryUrl}).");
        }
    }
}
