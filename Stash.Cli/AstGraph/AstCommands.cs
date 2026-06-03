using System;
using Stash.Cli.AstGraph.Models;

namespace Stash.Cli.AstGraph;

/// <summary>
/// Entry point for the <c>stash ast</c> subcommand.
/// </summary>
internal static class AstCommands
{
    /// <summary>
    /// Dispatches the <c>stash ast</c> subcommand.
    /// </summary>
    /// <param name="args">Arguments following <c>stash ast</c>.</param>
    public static void Run(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return;
        }

        if (args[0] is "-h" or "--help")
        {
            PrintHelp();
            return;
        }

        var options = AstOptions.Parse(args);
        var runner = new AstRunner(options);
        var result = runner.Run();

        if (result.HasErrors)
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            Environment.Exit(result.ExitCode);
        }

        if (options.OutputPath is not null)
        {
            try
            {
                System.IO.File.WriteAllText(options.OutputPath, result.Dot);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: Cannot write output file: {ex.Message}");
                Environment.Exit(2);
            }
        }
        else
        {
            Console.Write(result.Dot);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: stash ast [options] <file.stash>");
        Console.WriteLine();
        Console.WriteLine("Generate a Graphviz DOT graph of the abstract syntax tree.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <path>   Write DOT output to a file (default: stdout)");
        Console.WriteLine("  -s, --semantic        Run semantic resolution and annotate nodes with scope info");
        Console.WriteLine("  -h, --help            Show this help message");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  stash ast script.stash -o ast.dot");
        Console.WriteLine("  dot -Tpng ast.dot -o ast.png");
    }
}
