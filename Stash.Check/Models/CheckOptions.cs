namespace Stash.Check;

using System;
using System.Collections.Generic;

internal sealed class CheckOptions
{
    public string Format { get; init; } = "sarif";
    public string? OutputPath { get; init; }
    public List<string> ExcludeGlobs { get; init; } = new();
    public string Severity { get; init; } = "information";
    public bool NoImports { get; init; }
    public List<string> Paths { get; init; } = new();
    public bool ShowVersion { get; init; }
    public bool ShowHelp { get; init; }

    public static CheckOptions Parse(string[] args)
    {
        string format = "sarif";
        string? outputPath = null;
        var excludeGlobs = new List<string>();
        string severity = "information";
        bool noImports = false;
        bool showVersion = false;
        bool showHelp = false;
        var paths = new List<string>();

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

                case "--version":
                    showVersion = true;
                    break;

                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                default:
                    if (args[i].StartsWith('-'))
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
            ShowHelp = showHelp
        };
    }
}
