namespace Stash.Format;

using System;
using System.Collections.Generic;

internal sealed class FormatOptions
{
    public bool Write { get; init; }
    public bool Check { get; init; }
    public bool Diff { get; init; }
    public int IndentSize { get; init; } = 2;
    public bool UseTabs { get; init; }
    public List<string> ExcludeGlobs { get; init; } = new();
    public List<string> Paths { get; init; } = new();
    public bool ShowHelp { get; init; }
    public bool ShowVersion { get; init; }

    public static FormatOptions Parse(string[] args)
    {
        bool write = false;
        bool check = false;
        bool diff = false;
        int indentSize = 2;
        bool useTabs = false;
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
                    indentSize = parsedIndent;
                    break;

                case "--use-tabs":
                case "-t":
                    useTabs = true;
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
            IndentSize = indentSize,
            UseTabs = useTabs,
            ExcludeGlobs = excludeGlobs,
            Paths = paths,
            ShowHelp = showHelp,
            ShowVersion = showVersion
        };
    }
}
