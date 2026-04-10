namespace Stash.Format;

using System;
using System.Collections.Generic;
using Stash.Analysis;

internal sealed class FormatOptions
{
    public bool Write { get; init; }
    public bool Check { get; init; }
    public bool Diff { get; init; }
    public List<string> ExcludeGlobs { get; init; } = new();
    public List<string> Paths { get; init; } = new();
    public bool ShowHelp { get; init; }
    public bool ShowVersion { get; init; }

    // CLI override values — null means "not specified by CLI; use .stashformat or default"
    public int? IndentSizeOverride { get; init; }
    public bool? UseTabsOverride { get; init; }
    public TrailingCommaStyle? TrailingCommaOverride { get; init; }
    public EndOfLineStyle? EndOfLineOverride { get; init; }
    public bool? BracketSpacingOverride { get; init; }
    public int? PrintWidthOverride { get; init; }

    /// <summary>Explicit path to a <c>.stashformat</c> config file, or <see langword="null"/> to auto-discover.</summary>
    public string? ConfigPath { get; init; }

    public int? RangeStart { get; init; }
    public int? RangeEnd { get; init; }

    // Convenience properties used by consumers expecting plain values
    public int IndentSize => IndentSizeOverride ?? 2;
    public bool UseTabs => UseTabsOverride ?? false;

    public static FormatOptions Parse(string[] args)
    {
        bool write = false;
        bool check = false;
        bool diff = false;
        int? indentSizeOverride = null;
        bool? useTabsOverride = null;
        TrailingCommaStyle? trailingCommaOverride = null;
        EndOfLineStyle? endOfLineOverride = null;
        bool? bracketSpacingOverride = null;
        int? printWidthOverride = null;
        string? configPath = null;
        int? rangeStart = null;
        int? rangeEnd = null;
        var excludeGlobs = new List<string>();
        bool showHelp = false;
        bool showVersion = false;
        var paths = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--write":
                case "-w":
                    write = true;
                    break;

                case "--check":
                case "-c":
                    check = true;
                    break;

                case "--diff":
                case "-d":
                    diff = true;
                    break;

                case "--indent-size":
                case "-i":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine($"Error: {args[i]} requires a value.");
                        Environment.Exit(2);
                    }
                    string indentArg = args[++i];
                    if (!int.TryParse(indentArg, out int parsedIndent) || parsedIndent <= 0)
                    {
                        Console.Error.WriteLine($"Error: --indent-size must be a positive integer, got '{indentArg}'.");
                        Environment.Exit(2);
                    }
                    indentSizeOverride = parsedIndent;
                    break;

                case "--use-tabs":
                case "-t":
                    useTabsOverride = true;
                    break;

                case "--trailing-comma":
                case "-tc":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine($"Error: {args[i]} requires a value.");
                        Environment.Exit(2);
                    }
                    string tcArg = args[++i].ToLowerInvariant();
                    trailingCommaOverride = tcArg switch
                    {
                        "all" => TrailingCommaStyle.All,
                        "none" => TrailingCommaStyle.None,
                        _ => null
                    };
                    if (trailingCommaOverride == null && tcArg is not ("all" or "none"))
                    {
                        Console.Error.WriteLine($"Error: --trailing-comma must be 'none' or 'all', got '{tcArg}'.");
                        Environment.Exit(2);
                    }
                    break;

                case "--end-of-line":
                case "-eol":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine($"Error: {args[i]} requires a value.");
                        Environment.Exit(2);
                    }
                    string eolArg = args[++i].ToLowerInvariant();
                    endOfLineOverride = eolArg switch
                    {
                        "lf" => EndOfLineStyle.Lf,
                        "crlf" => EndOfLineStyle.Crlf,
                        "auto" => EndOfLineStyle.Auto,
                        _ => null
                    };
                    if (endOfLineOverride == null && eolArg is not ("lf" or "crlf" or "auto"))
                    {
                        Console.Error.WriteLine($"Error: --end-of-line must be 'lf', 'crlf', or 'auto', got '{eolArg}'.");
                        Environment.Exit(2);
                    }
                    break;

                case "--print-width":
                case "-pw":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine($"Error: {args[i]} requires a value.");
                        Environment.Exit(2);
                    }
                    string pwArg = args[++i];
                    if (!int.TryParse(pwArg, out int parsedPw) || parsedPw <= 0)
                    {
                        Console.Error.WriteLine($"Error: --print-width must be a positive integer, got '{pwArg}'.");
                        Environment.Exit(2);
                    }
                    printWidthOverride = parsedPw;
                    break;

                case "--bracket-spacing":
                case "-bs":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine($"Error: {args[i]} requires a value.");
                        Environment.Exit(2);
                    }
                    string bsArg = args[++i].ToLowerInvariant();
                    if (bsArg == "true") bracketSpacingOverride = true;
                    else if (bsArg == "false") bracketSpacingOverride = false;
                    else
                    {
                        Console.Error.WriteLine($"Error: --bracket-spacing must be 'true' or 'false', got '{bsArg}'.");
                        Environment.Exit(2);
                    }
                    break;

                case "--config":
                case "-cfg":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine($"Error: {args[i]} requires a value.");
                        Environment.Exit(2);
                    }
                    configPath = args[++i];
                    break;

                case "--range-start":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine($"Error: {args[i]} requires a value.");
                        Environment.Exit(2);
                    }
                    string rsArg = args[++i];
                    if (!int.TryParse(rsArg, out int parsedRs) || parsedRs <= 0)
                    {
                        Console.Error.WriteLine($"Error: --range-start must be a positive integer, got '{rsArg}'.");
                        Environment.Exit(2);
                    }
                    rangeStart = parsedRs;
                    break;

                case "--range-end":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine($"Error: {args[i]} requires a value.");
                        Environment.Exit(2);
                    }
                    string reArg = args[++i];
                    if (!int.TryParse(reArg, out int parsedRe) || parsedRe <= 0)
                    {
                        Console.Error.WriteLine($"Error: --range-end must be a positive integer, got '{reArg}'.");
                        Environment.Exit(2);
                    }
                    rangeEnd = parsedRe;
                    break;

                case "--exclude":
                case "-e":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine($"Error: {args[i]} requires a value.");
                        Environment.Exit(2);
                    }
                    excludeGlobs.Add(args[++i]);
                    break;

                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                case "--version":
                case "-v":
                    showVersion = true;
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

        return new FormatOptions
        {
            Write = write,
            Check = check,
            Diff = diff,
            IndentSizeOverride = indentSizeOverride,
            UseTabsOverride = useTabsOverride,
            TrailingCommaOverride = trailingCommaOverride,
            EndOfLineOverride = endOfLineOverride,
            BracketSpacingOverride = bracketSpacingOverride,
            PrintWidthOverride = printWidthOverride,
            ConfigPath = configPath,
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            ExcludeGlobs = excludeGlobs,
            Paths = paths,
            ShowHelp = showHelp,
            ShowVersion = showVersion
        };
    }
}
