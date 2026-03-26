using System;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg search</c> command for querying the registry for
/// packages matching a search term.
/// </summary>
/// <remarks>
/// <para>
/// Results are printed in a tabular format showing the package name, latest version,
/// and a truncated description.  Pagination is supported via the <c>--page</c> flag.
/// </para>
/// </remarks>
public static class SearchCommand
{
    /// <summary>
    /// Executes the search command with the given arguments.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments following <c>stash pkg search</c>.  The first
    /// positional argument is the search query (required).  The optional
    /// <c>--page &lt;n&gt;</c> flag selects a results page, and
    /// <c>--registry &lt;url&gt;</c> overrides the default registry.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when no search query is provided.
    /// </exception>
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
        var entry = config.GetEntry(registryUrl);
        var client = new RegistryClient(registryUrl, entry?.Token, entry?.RefreshToken,
            entry?.ExpiresAt, entry?.MachineId, registryUrl);

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
