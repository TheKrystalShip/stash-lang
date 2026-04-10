namespace Stash.Check;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.Analysis;

internal sealed class CheckRunner
{
    private readonly CheckOptions _options;

    /// <summary>
    /// Timing data from the most recent <see cref="Run"/> call, or an empty list if not yet run
    /// or timing was not enabled.
    /// </summary>
    public List<(string Pass, double Ms)> LastTiming { get; private set; } = new();

    public CheckRunner(CheckOptions options)
    {
        _options = options;
    }

    public CheckResult Run()
    {
        // Stdin mode: "-" in paths reads from Console.In
        if (_options.Paths.Contains("-"))
            return RunStdin();

        var timing = new List<(string Pass, double Ms)>();
        var totalSw = Stopwatch.StartNew();

        var discoverSw = Stopwatch.StartNew();
        var files = DiscoverFiles();
        discoverSw.Stop();
        timing.Add(("FileDiscovery", discoverSw.Elapsed.TotalMilliseconds));

        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var results = new List<FileResult>();

        // Cache ProjectConfig per directory (hierarchical load is expensive)
        var configCache = new ConcurrentDictionary<string, ProjectConfig>(StringComparer.Ordinal);

        // Local function: read, configure, and analyze a single file. Returns null on read error.
        FileResult? ProcessFile(string filePath)
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
                return null;
            }

            // Load and cache per-directory config, merging CLI overrides
            string? scriptDir = Path.GetDirectoryName(absolutePath);
            string cacheKey = scriptDir ?? "";
            var config = configCache.GetOrAdd(cacheKey,
                key => ProjectConfig.Load(key.Length > 0 ? key : null).WithCliOverrides(_options.Select, _options.Ignore));

            var analysis = engine.Analyze(uri, source, _options.NoImports, config);
            return new FileResult(uri, analysis);
        }

        var analysisSw = Stopwatch.StartNew();

        if (_options.NoImports)
        {
            // Imports disabled: AnalysisEngine is safe to use concurrently
            var resultsBag = new ConcurrentBag<FileResult>();
            System.Threading.Tasks.Parallel.ForEach(files, filePath =>
            {
                var result = ProcessFile(filePath);
                if (result is not null)
                    resultsBag.Add(result);
            });
            // Sort results by file path for deterministic output
            results = resultsBag.OrderBy(r => r.Uri.LocalPath, StringComparer.Ordinal).ToList();
        }
        else
        {
            // Imports enabled: ImportResolver uses a non-thread-safe HashSet — must run serially
            foreach (string filePath in files)
            {
                var result = ProcessFile(filePath);
                if (result is not null)
                    results.Add(result);
            }
        }

        analysisSw.Stop();

        totalSw.Stop();
        timing.Add(("Analysis", analysisSw.Elapsed.TotalMilliseconds));
        timing.Add(("Total", totalSw.Elapsed.TotalMilliseconds));
        LastTiming = timing;

        return new CheckResult(results);
    }

    /// <summary>
    /// Reads source from stdin and analyzes it using <c>--stdin-filename</c> as the virtual path.
    /// </summary>
    private CheckResult RunStdin()
    {
        string source = Console.In.ReadToEnd();
        string filename = _options.StdinFilename ?? "stdin.stash";
        string absolutePath = Path.GetFullPath(filename);
        var uri = new Uri(absolutePath);

        string? scriptDir = Path.GetDirectoryName(absolutePath);
        var config = ProjectConfig.Load(scriptDir).WithCliOverrides(_options.Select, _options.Ignore);

        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var analysis = engine.Analyze(uri, source, _options.NoImports, config);
        return new CheckResult(new List<FileResult> { new FileResult(uri, analysis) });
    }

    /// <summary>
    /// Inserts <c>// stash-disable-next-line SAXXXX</c> comments before each diagnostic in analyzed files.
    /// Returns the number of suppression comments inserted across all files.
    /// </summary>
    internal int AddSuppressions(CheckResult result)
    {
        int totalInserted = 0;
        string? reason = _options.Reason;

        foreach (var fileResult in result.Files)
        {
            if (!fileResult.Uri.IsFile) continue;
            string filePath = fileResult.Uri.LocalPath;

            var diagnostics = fileResult.Analysis.SemanticDiagnostics
                .Where(d => d.Code != null)
                .ToList();

            if (diagnostics.Count == 0) continue;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Warning: Cannot read '{filePath}': {ex.Message}");
                continue;
            }

            var lineList = new List<string>(lines);

            // Group by line, process descending so insertions don't shift later offsets
            var groups = diagnostics
                .GroupBy(d => d.Span.StartLine)
                .OrderByDescending(g => g.Key)
                .ToList();

            foreach (var group in groups)
            {
                int targetLine = group.Key - 1; // 0-indexed
                if (targetLine < 0 || targetLine >= lineList.Count) continue;

                string targetText = lineList[targetLine];
                string indent = GetIndent(targetText);

                string codesStr = string.Join(", ", group.Select(d => d.Code!).Distinct().OrderBy(c => c));
                string reasonSuffix = reason != null ? $" \u2014 {reason}" : "";
                string suppressionLine = $"{indent}// stash-disable-next-line {codesStr}{reasonSuffix}";

                lineList.Insert(targetLine, suppressionLine);
                totalInserted++;
            }

            try
            {
                File.WriteAllLines(filePath, lineList);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Warning: Cannot write '{filePath}': {ex.Message}");
            }
        }

        return totalInserted;
    }

    internal List<string> DiscoverFiles()
    {
        var files = new List<string>();

        foreach (string inputPath in _options.Paths)
        {
            if (inputPath == "-") continue; // Handled by RunStdin()

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
                        return true;
                }
                return false;
            });
        }

        return files;
    }

    private static string GetIndent(string line)
    {
        int len = 0;
        while (len < line.Length && (line[len] == ' ' || line[len] == '\t'))
            len++;
        return line[..len];
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
