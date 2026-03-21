using System;

namespace Stash.Cli.PackageManager.Commands;

public static class SearchCommand
{
    public static void Execute(string[] args)
    {
        string? query = null;
        int page = 1;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--registry" && i + 1 < args.Length)
            {
                i++;
            }
            else if (args[i] == "--page" && i + 1 < args.Length)
            {
                page = int.Parse(args[++i]);
            }
            else if (query == null)
            {
                query = args[i];
            }
        }

        if (query == null)
        {
            throw new ArgumentException("Usage: stash pkg search <query>");
        }

        var (registryUrl, _) = RegistryResolver.Resolve(args);
        var config = UserConfig.Load();
        var client = new RegistryClient(registryUrl, config.GetToken(registryUrl));

        var results = client.Search(query, page);
        if (results == null || results.Packages.Count == 0)
        {
            Console.WriteLine("No packages found.");
            return;
        }

        Console.WriteLine($"Found {results.TotalCount} packages (page {results.Page}/{results.TotalPages}):");
        Console.WriteLine();

        foreach (var pkg in results.Packages)
        {
            string desc = pkg.Description ?? "";
            if (desc.Length > 60)
            {
                desc = desc[..57] + "...";
            }

            Console.WriteLine($"  {pkg.Name,-30} {pkg.Latest ?? "",-12} {desc}");
        }
    }
}
