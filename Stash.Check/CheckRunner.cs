namespace Stash.Check;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.Analysis;

internal sealed class CheckRunner
{
    private readonly CheckOptions _options;

    public CheckRunner(CheckOptions options)
    {
        _options = options;
    }

    public CheckResult Run()
    {
        var files = DiscoverFiles();
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var results = new List<FileResult>();

        foreach (string filePath in files)
        {
            string absolutePath = Path.GetFullPath(filePath);
            var uri = new Uri(absolutePath);
            string source;
            try
            {
                source = File.ReadAllText(absolutePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Warning: Cannot read '{absolutePath}': {ex.Message}");
                continue;
            }

            var analysis = engine.Analyze(uri, source, _options.NoImports);
            results.Add(new FileResult(uri, analysis));
        }

        return new CheckResult(results);
    }

    internal List<string> DiscoverFiles()
    {
        var files = new List<string>();

        foreach (string inputPath in _options.Paths)
        {
            string fullPath = Path.GetFullPath(inputPath);

            if (File.Exists(fullPath))
            {
                if (fullPath.EndsWith(".stash", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(fullPath);
                }
                else
                {
                    Console.Error.WriteLine($"Warning: '{inputPath}' is not a .stash file, skipping.");
                }
            }
            else if (Directory.Exists(fullPath))
            {
                foreach (string file in Directory.EnumerateFiles(fullPath, "*.stash", SearchOption.AllDirectories))
                {
                    files.Add(Path.GetFullPath(file));
                }
            }
            else
            {
                Console.Error.WriteLine($"Error: Path not found: '{inputPath}'");
                Environment.Exit(2);
            }
        }

        // Apply exclude globs
        if (_options.ExcludeGlobs.Count > 0)
        {
            string cwd = Directory.GetCurrentDirectory();
            files.RemoveAll(f =>
            {
                string relative = Path.GetRelativePath(cwd, f).Replace('\\', '/');
                foreach (string glob in _options.ExcludeGlobs)
                {
                    if (MatchesGlob(relative, glob))
                    {
                        return true;
                    }
                }
                return false;
            });
        }

        return files;
    }

    private static bool MatchesGlob(string path, string glob)
    {
        string pattern = "^" + Regex.Escape(glob)
            .Replace("\\*\\*/", "(.+/)?")
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(path, pattern, RegexOptions.IgnoreCase);
    }
}
