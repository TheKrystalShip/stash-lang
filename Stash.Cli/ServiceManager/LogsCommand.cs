using System;
using System.Collections.Generic;
using System.Threading;
using Stash.Scheduler.Logging;

namespace Stash.Cli.ServiceManager;

/// <summary>Implements <c>stash service logs &lt;name&gt; [options]</c>.</summary>
public static class LogsCommand
{
    public static void Execute(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: stash service logs <name> [options]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --follow         Tail the log file");
            Console.Error.WriteLine("  --date YYYY-MM-DD  Show log for a specific date");
            Console.Error.WriteLine("  --lines <n>      Number of lines to show (default 50)");
            Environment.Exit(64);
            return;
        }

        string name = args[0];
        bool follow = false;
        string? date = null;
        int lines = 50;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--follow":
                case "-f":
                    follow = true;
                    break;
                case "--date":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --date requires a value (YYYY-MM-DD).");
                        Environment.Exit(64);
                    }
                    date = args[++i];
                    break;
                case "--lines":
                case "-n":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --lines requires a value.");
                        Environment.Exit(64);
                    }
                    if (!int.TryParse(args[++i], out lines))
                    {
                        Console.Error.WriteLine("Error: --lines must be an integer.");
                        Environment.Exit(1);
                    }
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    Environment.Exit(64);
                    break;
            }
        }

        if (follow)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            ServiceLogManager.Follow(name, cts.Token);
        }
        else
        {
            IReadOnlyList<string> logLines = ServiceLogManager.ReadLines(name, lines, date);
            if (logLines.Count == 0)
            {
                Console.WriteLine($"No log entries found for '{name}'.");
                return;
            }
            foreach (string line in logLines)
                Console.WriteLine(line);
        }
    }
}
