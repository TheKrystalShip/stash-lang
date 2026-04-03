namespace Stash.Format;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Stash.Analysis;

internal sealed class FormatRunner
{
    private readonly FormatOptions _options;

    public FormatRunner(FormatOptions options)
    {
        _options = options;
    }

    public FormatResult Run()
    {
        var files = DiscoverFiles();
        var results = new List<FileFormatResult>();

        foreach (string filePath in files)
        {
            string absolutePath = Path.GetFullPath(filePath);
            string source;
            try
            {
                source = File.ReadAllText(absolutePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Error: Cannot read '{absolutePath}': {ex.Message}");
                results.Add(new FileFormatResult(absolutePath, Error: ex.Message));
                continue;
            }

            string formatted;
            try
            {
                var formatter = new StashFormatter(_options.IndentSize, _options.UseTabs);
                formatted = formatter.Format(source);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: Failed to parse '{absolutePath}': {ex.Message}");
                results.Add(new FileFormatResult(absolutePath, Error: ex.Message));
                continue;
            }

            bool changed = !string.Equals(source, formatted, StringComparison.Ordinal);
            results.Add(new FileFormatResult(absolutePath, Original: source, Formatted: formatted, Changed: changed));
        }

        return new FormatResult(results);
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
