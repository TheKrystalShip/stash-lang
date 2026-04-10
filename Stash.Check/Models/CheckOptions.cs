namespace Stash.Check;

using System;
using System.Collections.Generic;

internal sealed class CheckOptions
{
    public string Format { get; init; } = "text";
    public string? OutputPath { get; init; }
    public List<string> ExcludeGlobs { get; init; } = new();
    public string Severity { get; init; } = "information";
    public bool NoImports { get; init; }
    public List<string> Paths { get; init; } = new();
    public bool ShowVersion { get; init; }
    public bool ShowHelp { get; init; }
    public bool ShowStatistics { get; init; }
    public bool ShowFiles { get; init; }
    public bool Fix { get; init; }
    public bool UnsafeFixes { get; init; }
    public bool Diff { get; init; }

    // Phase 4 — Advanced Configuration
    /// <summary>Exclusive rule allow-list (--select). Only these codes/prefixes are reported.</summary>
    public List<string> Select { get; init; } = new();
    /// <summary>Additional codes/prefixes to suppress (--ignore).</summary>
    public List<string> Ignore { get; init; } = new();
    /// <summary>Inserts suppression comments for all current diagnostics in-place.</summary>
    public bool AddSuppress { get; init; }
    /// <summary>Optional reason text appended to auto-inserted suppression comments.</summary>
    public string? Reason { get; init; }
    /// <summary>Virtual filename used for diagnostics when reading from stdin (<c>-</c>).</summary>
    public string? StdinFilename { get; init; }
    /// <summary>Enables watch mode — re-analyze on file changes.</summary>
    public bool Watch { get; init; }
    /// <summary>Enables timing output — prints a breakdown of analysis pass durations.</summary>
    public bool Timing { get; init; }

    public static CheckOptions Parse(string[] args)
    {
        string format = "text";
        string? outputPath = null;
        var excludeGlobs = new List<string>();
        string severity = "information";
        bool noImports = false;
        bool showVersion = false;
        bool showHelp = false;
        bool showStatistics = false;
        bool showFiles = false;
        bool fix = false;
        bool unsafeFixes = false;
        bool diff = false;
        var paths = new List<string>();
        var select = new List<string>();
        var ignore = new List<string>();
        bool addSuppress = false;
        string? reason = null;
        string? stdinFilename = null;
        bool watch = false;
        bool timing = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--format":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --format requires a value.");
                        Environment.Exit(2);
                    }
                    format = args[++i];
                    break;

                case "--output":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --output requires a value.");
                        Environment.Exit(2);
                    }
                    outputPath = args[++i];
                    break;

                case "--exclude":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --exclude requires a value.");
                        Environment.Exit(2);
                    }
                    excludeGlobs.Add(args[++i]);
                    break;

                case "--severity":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --severity requires a value.");
                        Environment.Exit(2);
                    }
                    severity = args[++i].ToLowerInvariant();
                    if (severity is not ("error" or "warning" or "information"))
                    {
                        Console.Error.WriteLine($"Error: Invalid severity '{severity}'. Expected: error, warning, or information.");
                        Environment.Exit(2);
                    }
                    break;

                case "--no-imports":
                    noImports = true;
                    break;

                case "--fix":
                    fix = true;
                    break;

                case "--unsafe-fixes":
                    unsafeFixes = true;
                    break;

                case "--diff":
                    diff = true;
                    break;

                case "--statistics":
                    showStatistics = true;
                    break;

                case "--show-files":
                    showFiles = true;
                    break;

                case "--version":
                    showVersion = true;
                    break;

                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                case "--select":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --select requires a value.");
                        Environment.Exit(2);
                    }
                    foreach (string code in args[++i].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        select.Add(code);
                    break;

                case "--ignore":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --ignore requires a value.");
                        Environment.Exit(2);
                    }
                    foreach (string code in args[++i].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        ignore.Add(code);
                    break;

                case "--add-suppress":
                    addSuppress = true;
                    break;

                case "--reason":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --reason requires a value.");
                        Environment.Exit(2);
                    }
                    reason = args[++i];
                    break;

                case "--stdin-filename":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --stdin-filename requires a value.");
                        Environment.Exit(2);
                    }
                    stdinFilename = args[++i];
                    break;

                case "--watch":
                    watch = true;
                    break;

                case "--timing":
                    timing = true;
                    break;

                default:
                    if (args[i].StartsWith('-') && args[i] != "-")
                    {
                        Console.Error.WriteLine($"Error: Unknown option '{args[i]}'.");
                        Environment.Exit(2);
                    }
                    paths.Add(args[i]);
                    break;
            }
        }

        if (paths.Count == 0 && !showVersion && !showHelp)
        {
            paths.Add(".");
        }

        return new CheckOptions
        {
            Format = format,
            OutputPath = outputPath,
            ExcludeGlobs = excludeGlobs,
            Severity = severity,
            NoImports = noImports,
            Paths = paths,
            ShowVersion = showVersion,
            ShowHelp = showHelp,
            ShowStatistics = showStatistics,
            ShowFiles = showFiles,
            Fix = fix,
            UnsafeFixes = unsafeFixes,
            Diff = diff,
            Select = select,
            Ignore = ignore,
            AddSuppress = addSuppress,
            Reason = reason,
            StdinFilename = stdinFilename,
            Watch = watch,
            Timing = timing,
        };
    }
}
