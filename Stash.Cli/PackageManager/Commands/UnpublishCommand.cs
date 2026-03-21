using System;

namespace Stash.Cli.PackageManager.Commands;

public static class UnpublishCommand
{
    public static void Execute(string[] args)
    {
        string? spec = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--registry" && i + 1 < args.Length)
            {
                i++;
            }
            else if (spec == null)
            {
                spec = args[i];
            }
        }

        if (spec == null)
        {
            throw new ArgumentException("Usage: stash pkg unpublish <name>@<version>");
        }

        var (registryUrl, _) = RegistryResolver.Resolve(args);

        int atIdx = spec.LastIndexOf('@');
        if (atIdx <= 0)
        {
            throw new ArgumentException("Format: <name>@<version>. Example: stash pkg unpublish my-package@1.0.0");
        }

        string packageName = spec[..atIdx];
        string version = spec[(atIdx + 1)..];

        var config = UserConfig.Load();
        string? token = config.GetToken(registryUrl);
        if (token == null)
        {
            throw new InvalidOperationException($"Not logged in to registry '{registryUrl}'. Run 'stash pkg login'.");
        }

        var client = new RegistryClient(registryUrl, token);
        client.Unpublish(packageName, version);

        Console.WriteLine($"Unpublished {packageName}@{version} from {registryUrl}.");
    }
}
