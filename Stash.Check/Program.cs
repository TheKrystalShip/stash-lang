namespace Stash.Check;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Analysis;

internal static class Program
{
    private const string Usage = @"Usage: stash-check [OPTIONS] [FILES/DIRS...]

Runs Stash static analysis and outputs diagnostics.

Arguments:
  FILES/DIRS...              One or more .stash files or directories (default: .)
  -                          Read source from stdin (requires --stdin-filename)

Options:
  --format <fmt>             Output format: text, sarif (default: text)
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

        if (options.Format is not ("text" or "sarif"))
        {
            Console.Error.WriteLine($"Error: Unsupported output format '{options.Format}'. Supported: text, sarif.");
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

        // --statistics: show summary table
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

        IOutputFormatter formatter = options.Format switch
        {
            "sarif" => new SarifFormatter("stash-check " + string.Join(" ", args), startTime),
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
}
