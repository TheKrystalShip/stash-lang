using System;

namespace Stash.Cli.PackageManager;

public static class RegistryResolver
{
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
