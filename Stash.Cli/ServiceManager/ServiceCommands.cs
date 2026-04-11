using System;
using System.IO;

namespace Stash.Cli.ServiceManager;

/// <summary>
/// Entry point for all <c>stash service</c> sub-commands, dispatching to the
/// appropriate command class based on the first argument.
/// </summary>
public static class ServiceCommands
{
    /// <summary>
    /// Parses the first element of <paramref name="args"/> as a sub-command name
    /// and dispatches execution to the corresponding command class.
    /// </summary>
    /// <param name="args">
    /// The command-line arguments following <c>stash service</c>, where the first
    /// element is the sub-command name and the remainder are passed to the
    /// sub-command's <c>Execute</c> method.
    /// </param>
    public static void Run(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return;
        }

        string subcommand = args[0];
        string[] subArgs = args[1..];

        try
        {
            switch (subcommand)
            {
                case "install":
                    InstallCommand.Execute(subArgs);
                    break;
                case "uninstall":
                    UninstallCommand.Execute(subArgs);
                    break;
                case "start":
                    StartCommand.Execute(subArgs);
                    break;
                case "stop":
                    StopCommand.Execute(subArgs);
                    break;
                case "restart":
                    RestartCommand.Execute(subArgs);
                    break;
                case "enable":
                    EnableCommand.Execute(subArgs);
                    break;
                case "disable":
                    DisableCommand.Execute(subArgs);
                    break;
                case "status":
                    StatusCommand.Execute(subArgs);
                    break;
                case "list":
                    ListCommand.Execute(subArgs);
                    break;
                case "logs":
                    LogsCommand.Execute(subArgs);
                    break;
                case "clean":
                    CleanCommand.Execute(subArgs);
                    break;
                case "help":
                case "--help":
                case "-h":
                    PrintHelp();
                    break;
                default:
                    Console.Error.WriteLine($"Unknown command: {subcommand}");
                    Console.Error.WriteLine();
                    PrintHelp();
                    Environment.Exit(64);
                    break;
            }
        }
        catch (PlatformNotSupportedException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: stash service <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  install       Install a Stash script as a service");
        Console.WriteLine("  uninstall     Remove an installed service");
        Console.WriteLine("  start         Start a stopped service");
        Console.WriteLine("  stop          Stop a running service");
        Console.WriteLine("  restart       Restart a service");
        Console.WriteLine("  enable        Enable auto-start on boot");
        Console.WriteLine("  disable       Disable auto-start on boot");
        Console.WriteLine("  status        Show status (all services or one)");
        Console.WriteLine("  list          List all Stash-managed services");
        Console.WriteLine("  logs          Show service logs");
        Console.WriteLine("  clean         Remove orphaned sidecar files");
        Console.WriteLine("  help          Show this help message");
    }
}
