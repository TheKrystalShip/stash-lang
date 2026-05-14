namespace Stash.Docs;

using System;
using System.IO;

/// <summary>
/// CLI entry point. Resolves the target Markdown path (CLI argument, then
/// <c>STASH_DOCS_TARGET</c> env var, then walking up from the binary to find
/// the repo's <c>docs/</c> directory) and overwrites the file in place.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string target = ResolveTarget(args);
            ReferenceGenerator.WriteTo(target);
            Console.WriteLine("Wrote " + target);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Stash.Docs: " + ex.Message);
            return 1;
        }
    }

    private static string ResolveTarget(string[] args)
    {
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
            return Path.GetFullPath(args[0]);

        string? fromEnv = Environment.GetEnvironmentVariable("STASH_DOCS_TARGET");
        if (!string.IsNullOrEmpty(fromEnv))
            return Path.GetFullPath(fromEnv!);

        // Walk up from the binary location looking for the repo root marker
        // (Stash.sln). Falling back to AppContext.BaseDirectory keeps the
        // command working from a published build, from `dotnet run`, and
        // from xUnit test hosts.
        string current = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && current != null; i++)
        {
            if (File.Exists(Path.Combine(current, "Stash.sln")))
                return Path.Combine(current, ReferenceGenerator.DefaultRelativePath);
            current = Path.GetDirectoryName(current)!;
        }

        throw new InvalidOperationException(
            "Could not locate repo root (Stash.sln). Pass an explicit output path as the first argument.");
    }
}
