using System;
using System.Collections.Generic;

namespace Stash.Cli.AstGraph.Models;

/// <summary>
/// Command-line options for the <c>stash ast</c> subcommand.
/// </summary>
internal sealed class AstOptions
{
    /// <summary>Path to the .stash source file to analyse.</summary>
    public string? FilePath { get; init; }

    /// <summary>Optional output file path. <c>null</c> means stdout.</summary>
    public string? OutputPath { get; init; }

    /// <summary>When <c>true</c>, run <c>SemanticResolver</c> and annotate nodes with scope info.</summary>
    public bool Semantic { get; init; }

    private const string Usage = """"
        Usage: stash ast [options] <file.stash>

        Generate a Graphviz DOT graph of the abstract syntax tree.

        Options:
          -o, --output <path>   Write DOT output to a file (default: stdout)
          -s, --semantic        Run semantic resolution and annotate nodes with scope info
          -h, --help            Show this help message

        Example:
          stash ast script.stash -o ast.dot
          dot -Tpng ast.dot -o ast.png
        """";

    /// <summary>
    /// Parses command-line arguments into an <see cref="AstOptions"/> instance.
    /// </summary>
    public static AstOptions Parse(string[] args)
    {
        string? filePath = null;
        string? outputPath = null;
        bool semantic = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    Console.WriteLine(Usage);
                    Environment.Exit(0);
                    break;
                case "-o" or "--output":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --output requires a value");
                        Environment.Exit(2);
                    }
                    outputPath = args[++i];
                    break;
                case "-s" or "--semantic":
                    semantic = true;
                    break;
                default:
                    if (args[i].StartsWith('-'))
                    {
                        Console.Error.WriteLine($"Error: Unknown option: {args[i]}");
                        Environment.Exit(2);
                    }
                    filePath = args[i];
                    break;
            }
        }

        if (filePath is null)
        {
            Console.Error.WriteLine("Error: No input file specified");
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            Environment.Exit(2);
        }

        return new AstOptions
        {
            FilePath = filePath,
            OutputPath = outputPath,
            Semantic = semantic,
        };
    }
}
