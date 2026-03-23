using System;
using System.IO;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Entry point for all <c>stash pkg</c> sub-commands, dispatching to the
/// appropriate command class based on the first argument.
/// </summary>
/// <remarks>
/// <para>
/// Recognised sub-commands and their aliases:
/// <c>init</c>, <c>install</c>/<c>i</c>, <c>uninstall</c>/<c>remove</c>,
/// <c>list</c>/<c>ls</c>, <c>pack</c>, <c>update</c>, <c>outdated</c>,
/// <c>publish</c>, <c>search</c>, <c>info</c>, <c>login</c>, <c>logout</c>,
/// <c>owner</c>, <c>unpublish</c>, and <c>help</c>.
/// </para>
/// <para>
/// Exceptions of types <see cref="InvalidOperationException"/>,
/// <see cref="InvalidDataException"/>, <see cref="ArgumentException"/>,
/// <see cref="FormatException"/>, and <see cref="System.Net.Http.HttpRequestException"/>
/// are caught, printed to <c>stderr</c>, and translated to a non-zero exit code.
/// </para>
/// </remarks>
public static class PackageCommands
{
    /// <summary>
    /// Parses the first element of <paramref name="args"/> as a sub-command name
    /// and dispatches execution to the corresponding command class.
    /// </summary>
    /// <param name="args">
    /// The command-line arguments following <c>stash pkg</c>, where the first
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
                case "publish":
                    PublishCommand.Execute(subArgs);
                    break;
                case "search":
                    SearchCommand.Execute(subArgs);
                    break;
                case "info":
                    InfoCommand.Execute(subArgs);
                    break;
                case "login":
                    LoginCommand.Execute(subArgs);
                    break;
                case "logout":
                    LogoutCommand.Execute(subArgs);
                    break;
                case "owner":
                    OwnerCommand.Execute(subArgs);
                    break;
                case "unpublish":
                    UnpublishCommand.Execute(subArgs);
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
        catch (FormatException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Prints the usage summary and available sub-commands to standard output.
    /// </summary>
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
        Console.WriteLine("  publish           Publish package to registry");
        Console.WriteLine("  search            Search registry for packages");
        Console.WriteLine("  info              Show package information");
        Console.WriteLine("  login             Authenticate with registry");
        Console.WriteLine("  logout            Remove stored credentials");
        Console.WriteLine("  owner             Manage package owners");
        Console.WriteLine("  unpublish         Remove a published version");
        Console.WriteLine("  help              Show this help message");
    }
}
