namespace Stash.Check;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Stash.Analysis;

internal static class Program
{
    private const string Usage = @"Usage: stash-check [OPTIONS] [FILES/DIRS...]

Runs Stash static analysis and outputs diagnostics.

Arguments:
  FILES/DIRS...              One or more .stash files or directories (default: .)
  -                          Read source from stdin (requires --stdin-filename)

Options:
  --format <fmt>             Output format: text, sarif, json, github, grouped (default: text)
  --output <path>            Write output to a file instead of stdout
  --exclude <glob>           Glob pattern to exclude (repeatable)
  --severity <level>         Minimum severity: error, warning, information (default: information)
  --no-imports               Disable cross-file import resolution
  --fix                      Apply safe fixes in-place
  --unsafe-fixes             Apply safe and unsafe fixes in-place (implies --fix)
  --diff                     Show fixes as unified diff without applying
  --statistics               Show summary of diagnostics by rule
  --show-files               List files that would be analyzed
  --select <codes>           Only report these codes/prefixes (comma-separated, e.g. SA0201,SA03)
  --ignore <codes>           Suppress these codes/prefixes (comma-separated, e.g. SA0201,SA03)
  --add-suppress             Insert suppression comments for all current diagnostics in-place
  --reason <text>            Reason text appended to auto-inserted suppression comments
  --stdin-filename <file>    Virtual filename for stdin diagnostics (used with -)
  --watch                    Watch for file changes and re-analyze
  --timing                   Print pass timing breakdown
  --generate-docs <dir>      Generate rule documentation pages into <dir> and exit
  --version                  Print version and exit
  --help, -h                 Print this help and exit";

    internal static int Main(string[] args)
    {
        var startTime = DateTime.UtcNow;
        var options = CheckOptions.Parse(args);

        if (options.ShowHelp)
        {
            Console.WriteLine(Usage);
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine(GetVersion());
            return 0;
        }

        if (options.GenerateDocsDir is not null)
        {
            try
            {
                RuleDocGenerator.GenerateDocs(options.GenerateDocsDir);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error generating docs: {ex.Message}");
                return 2;
            }
            return 0;
        }

        if (options.Format is not ("text" or "sarif" or "json" or "github" or "grouped"))
        {
            Console.Error.WriteLine($"Error: Unsupported output format '{options.Format}'. Supported: text, sarif, json, github, grouped.");
            return 2;
        }

        var runner = new CheckRunner(options);

        // --show-files: list files that would be analyzed and exit
        if (options.ShowFiles)
        {
            List<string> files;
            try
            {
                files = runner.DiscoverFiles();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 2;
            }

            string cwd = Directory.GetCurrentDirectory();
            foreach (string file in files)
            {
                Console.WriteLine(Path.GetRelativePath(cwd, file).Replace('\\', '/'));
            }
            return 0;
        }

        CheckResult result;
        try
        {
            result = runner.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }

        // --watch: re-analyze on file changes
        if (options.Watch)
        {
            RunWatchMode(runner, options, args, startTime, result);
            return 0;
        }
        if (options.ShowStatistics)
        {
            WriteStatistics(result);
            return HasDiagnosticsAtOrAbove(result, GetMinLevel(options)) ? 1 : 0;
        }

        // --add-suppress: insert suppression comments in-place
        if (options.AddSuppress)
        {
            int total = runner.AddSuppressions(result);
            Console.Error.WriteLine($"Inserted {total} suppression comment(s).");
            return 0;
        }

        // --diff: show unified diff of fixes without applying
        if (options.Diff)
        {
            bool allowUnsafe = options.UnsafeFixes;
            var fixesByFile = FixApplier.CollectFixes(result, allowUnsafe);
            if (fixesByFile.Count == 0)
            {
                Console.Error.WriteLine("No fixes available.");
                return 0;
            }
            FixApplier.WriteDiff(result, fixesByFile, Console.Out);
            return 0;
        }

        // --fix / --unsafe-fixes: apply fixes in-place
        if (options.Fix || options.UnsafeFixes)
        {
            bool allowUnsafe = options.UnsafeFixes;
            var fixesByFile = FixApplier.CollectFixes(result, allowUnsafe);
            int fixedCount = FixApplier.ApplyFixes(fixesByFile);
            int totalFixes = fixesByFile.Values.Sum(l => l.Count);
            Console.Error.WriteLine($"Applied {totalFixes} fix(es) across {fixedCount} file(s).");
            return 0;
        }

        // --timing: print pass timing table
        if (options.Timing)
        {
            WriteTiming(runner.LastTiming);
        }

        IOutputFormatter formatter = options.Format switch
        {
            "sarif" => new SarifFormatter("stash-check " + string.Join(" ", args), startTime),
            "json" => new JsonFormatter(),
            "github" => new GitHubFormatter(),
            "grouped" => new GroupedFormatter(),
            _ => new TextFormatter()
        };

        if (options.OutputPath != null)
        {
            using var fileStream = File.Create(options.OutputPath);
            formatter.Write(result, fileStream);
        }
        else
        {
            using var stdout = Console.OpenStandardOutput();
            formatter.Write(result, stdout);
        }

        DiagnosticLevel minLevel = GetMinLevel(options);
        bool hasDiagnostics = HasDiagnosticsAtOrAbove(result, minLevel);
        return hasDiagnostics ? 1 : 0;
    }

    private static DiagnosticLevel GetMinLevel(CheckOptions options) => options.Severity switch
    {
        "error" => DiagnosticLevel.Error,
        "warning" => DiagnosticLevel.Warning,
        _ => DiagnosticLevel.Information
    };

    private static void WriteStatistics(CheckResult result)
    {
        var counts = new SortedDictionary<string, int>();

        foreach (var file in result.Files)
        {
            foreach (var err in file.Analysis.StructuredLexErrors)
            {
                counts.TryGetValue("STASH001", out int c);
                counts["STASH001"] = c + 1;
            }
            foreach (var err in file.Analysis.StructuredParseErrors)
            {
                counts.TryGetValue("STASH002", out int c);
                counts["STASH002"] = c + 1;
            }
            foreach (var diag in file.Analysis.SemanticDiagnostics)
            {
                string code = diag.Code ?? "SA0000";
                counts.TryGetValue(code, out int c);
                counts[code] = c + 1;
            }
        }

        if (counts.Count == 0)
        {
            Console.WriteLine("No diagnostics found.");
            return;
        }

        Console.WriteLine($"{"Rule",-10} | {"Count",5}");
        Console.WriteLine(new string('-', 10) + "-|-" + new string('-', 5));
        foreach (var (rule, count) in counts)
        {
            Console.WriteLine($"{rule,-10} | {count,5}");
        }
        Console.WriteLine();
        Console.WriteLine($"Total: {counts.Values.Sum()} diagnostics");
    }

    private static bool HasDiagnosticsAtOrAbove(CheckResult result, DiagnosticLevel minLevel)
    {
        foreach (var file in result.Files)
        {
            // Lex/parse errors are always "error" level
            if (minLevel <= DiagnosticLevel.Error)
            {
                if (file.Analysis.StructuredLexErrors.Count > 0 || file.Analysis.StructuredParseErrors.Count > 0)
                {
                    return true;
                }
            }

            foreach (var diag in file.Analysis.SemanticDiagnostics)
            {
                if (diag.Level <= minLevel)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetVersion()
    {
        var assembly = typeof(Program).Assembly;
        var version = assembly.GetName().Version;
        return version != null ? $"stash-check {version.Major}.{version.Minor}.{version.Build}" : "stash-check 0.1.0";
    }

    private static void WriteTiming(List<(string Pass, double Ms)> timing)
    {
        if (timing.Count == 0)
        {
            Console.Error.WriteLine("No timing data available.");
            return;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine($"{"Pass",-18} | {"Time (ms)",9}");
        Console.Error.WriteLine(new string('-', 18) + "-|-" + new string('-', 9));
        foreach (var (pass, ms) in timing)
        {
            Console.Error.WriteLine($"{pass,-18} | {ms,9:F1}");
        }
        Console.Error.WriteLine();
    }

    /// <summary>
    /// Runs in watch mode: prints initial results, then re-analyzes whenever .stash files change.
    /// Press Ctrl+C to exit.
    /// </summary>
    private static void RunWatchMode(CheckRunner runner, CheckOptions options, string[] args, DateTime startTime, CheckResult initialResult)
    {
        IOutputFormatter formatter = options.Format switch
        {
            "sarif" => new SarifFormatter("stash-check " + string.Join(" ", args), startTime),
            "json" => new JsonFormatter(),
            "github" => new GitHubFormatter(),
            "grouped" => new GroupedFormatter(),
            _ => new TextFormatter()
        };

        // Print initial results
        Console.Clear();
        Console.Error.WriteLine("[stash-check] Watching for changes. Press Ctrl+C to exit.");
        using (var stdout = Console.OpenStandardOutput())
        {
            formatter.Write(initialResult, stdout);
        }

        // Collect watch directories
        var watchDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in options.Paths)
        {
            if (path == "-") continue;
            string fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
                watchDirs.Add(fullPath);
            else if (File.Exists(fullPath))
            {
                string? dir = Path.GetDirectoryName(fullPath);
                if (dir != null) watchDirs.Add(dir);
            }
        }

        if (watchDirs.Count == 0)
        {
            watchDirs.Add(Directory.GetCurrentDirectory());
        }

        // Track content hashes to skip unchanged files
        var contentHashes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var debounceTimer = new Timer(_ =>
        {
            try
            {
                Console.Clear();
                Console.Error.WriteLine("[stash-check] Watching for changes. Press Ctrl+C to exit.");
                var newResult = runner.Run();
                if (options.Timing) WriteTiming(runner.LastTiming);
                using var stdout = Console.OpenStandardOutput();
                formatter.Write(newResult, stdout);
            }
            catch
            {
                // Swallow errors in watch mode
            }
        }, null, Timeout.Infinite, Timeout.Infinite);

        var watchers = new List<FileSystemWatcher>();
        foreach (string dir in watchDirs)
        {
            var watcher = new FileSystemWatcher(dir, "*.stash")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            void OnChanged(object s, FileSystemEventArgs e)
            {
                // Debounce: reset timer on each change, fire after 300ms of quiet
                debounceTimer.Change(300, Timeout.Infinite);
            }

            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += (s, e) => OnChanged(s, e);
            watchers.Add(watcher);
        }

        // Block until Ctrl+C
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            cts.Token.WaitHandle.WaitOne();
        }
        catch (OperationCanceledException) { }
        finally
        {
            foreach (var w in watchers)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
        }
    }
}
