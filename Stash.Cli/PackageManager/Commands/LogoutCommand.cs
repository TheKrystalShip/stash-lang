using System;

namespace Stash.Cli.PackageManager.Commands;

public static class LogoutCommand
{
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
