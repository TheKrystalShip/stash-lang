using System;
using System.IO;

namespace Stash.Cli.PackageManager.Commands;

public static class PackageCommands
{
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
                case "init":
                    InitCommand.Execute(subArgs);
                    break;
                case "install":
                case "i":
                    InstallCommand.Execute(subArgs);
                    break;
                case "uninstall":
                case "remove":
                    UninstallCommand.Execute(subArgs);
                    break;
                case "list":
                case "ls":
                    ListCommand.Execute(subArgs);
                    break;
                case "pack":
                    PackCommand.Execute(subArgs);
                    break;
                case "update":
                    UpdateCommand.Execute(subArgs);
                    break;
                case "outdated":
                    OutdatedCommand.Execute(subArgs);
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
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: stash pkg <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  init              Create a new stash.json");
        Console.WriteLine("  install, i        Install dependencies");
        Console.WriteLine("  uninstall, remove Remove a dependency");
        Console.WriteLine("  list, ls          List installed packages");
        Console.WriteLine("  pack              Create a tarball from the current package");
        Console.WriteLine("  update            Update dependencies");
        Console.WriteLine("  outdated          Check for outdated dependencies");
        Console.WriteLine("  help              Show this help message");
    }
}
