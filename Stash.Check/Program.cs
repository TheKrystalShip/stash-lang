namespace Stash.Check;

using System;
using System.IO;
using Stash.Analysis;

internal static class Program
{
    private const string Usage = @"Usage: stash-check [OPTIONS] [FILES/DIRS...]

Runs Stash static analysis and outputs diagnostics in SARIF v2.1.0 format.

Arguments:
  FILES/DIRS...         One or more .stash files or directories (default: .)

Options:
  --format <fmt>        Output format: sarif (default: sarif)
  --output <path>       Write output to a file instead of stdout
  --exclude <glob>      Glob pattern to exclude (repeatable)
  --severity <level>    Minimum severity: error, warning, information (default: information)
  --no-imports          Disable cross-file import resolution
  --version             Print version and exit
  --help, -h            Print this help and exit";

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

        if (options.Format != "sarif")
        {
            Console.Error.WriteLine($"Error: Unsupported output format '{options.Format}'. Only 'sarif' is supported.");
            return 2;
        }

        var runner = new CheckRunner(options);
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

        string commandLine = "stash-check " + string.Join(" ", args);
        var formatter = new SarifFormatter(commandLine, startTime);

        // Filter by severity before counting for exit code
        DiagnosticLevel minLevel = options.Severity switch
        {
            "error" => DiagnosticLevel.Error,
            "warning" => DiagnosticLevel.Warning,
            _ => DiagnosticLevel.Information
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

        // Determine exit code based on severity threshold
        bool hasDiagnostics = HasDiagnosticsAtOrAbove(result, minLevel);
        return hasDiagnostics ? 1 : 0;
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
