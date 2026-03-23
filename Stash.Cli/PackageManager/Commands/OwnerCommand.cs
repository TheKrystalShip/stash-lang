using System;
using System.Collections.Generic;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg owner</c> command for listing, adding, and removing
/// package owners on the registry.
/// </summary>
/// <remarks>
/// <para>
/// Supported sub-commands: <c>list</c>/<c>ls</c>, <c>add</c>, and <c>remove</c>.
/// The <c>add</c> and <c>remove</c> actions require an authenticated session
/// (i.e. a token stored via <c>stash pkg login</c>).
/// </para>
/// </remarks>
public static class OwnerCommand
{
    /// <summary>
    /// Executes the owner command with the given arguments.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments following <c>stash pkg owner</c>.  The first two
    /// positional arguments are the sub-command (<c>list</c>, <c>add</c>, or
    /// <c>remove</c>) and the package name.  A third positional argument (username)
    /// is required for <c>add</c> and <c>remove</c>.  The
    /// <c>--registry &lt;url&gt;</c> flag optionally overrides the default registry.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when required positional arguments are missing or the sub-command is
    /// unrecognised.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the user is not logged in for a mutating operation.
    /// </exception>
    public static void Execute(string[] args)
    {
        var positionalArgs = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--registry" && i + 1 < args.Length)
            {
                i++;
            }
            else
            {
                positionalArgs.Add(args[i]);
            }
        }

        if (positionalArgs.Count < 2)
        {
            throw new ArgumentException("Usage: stash pkg owner <list|add|remove> <package> [username]");
        }

        string action = positionalArgs[0];
        string packageName = positionalArgs[1];

        var (registryUrl, _) = RegistryResolver.Resolve(args);
        var config = UserConfig.Load();
        string? token = config.GetToken(registryUrl);
        var client = new RegistryClient(registryUrl, token);

        switch (action)
        {
            case "list":
            case "ls":
            {
                List<string>? owners = client.GetOwners(packageName);
                if (owners == null)
                {
                    Console.Error.WriteLine($"Package '{packageName}' not found.");
                    Environment.Exit(1);
                    return;
                }
                Console.WriteLine($"Owners of {packageName}:");
                foreach (string owner in owners)
                    {
                        Console.WriteLine($"  {owner}");
                    }

                    break;
            }
            case "add":
            {
                if (positionalArgs.Count < 3)
                    {
                        throw new ArgumentException("Usage: stash pkg owner add <package> <username>");
                    }

                    string username = positionalArgs[2];
                if (token == null)
                    {
                        throw new InvalidOperationException("Not logged in. Run 'stash pkg login'.");
                    }

                    bool ok = client.AddOwner(packageName, username);
                if (ok)
                    {
                        Console.WriteLine($"Added {username} as owner of {packageName}.");
                    }
                    else
                    {
                        Console.Error.WriteLine("Failed to add owner.");
                    }

                    break;
            }
            case "remove":
            {
                if (positionalArgs.Count < 3)
                    {
                        throw new ArgumentException("Usage: stash pkg owner remove <package> <username>");
                    }

                    string username = positionalArgs[2];
                if (token == null)
                    {
                        throw new InvalidOperationException("Not logged in. Run 'stash pkg login'.");
                    }

                    bool ok = client.RemoveOwner(packageName, username);
                if (ok)
                    {
                        Console.WriteLine($"Removed {username} from owners of {packageName}.");
                    }
                    else
                    {
                        Console.Error.WriteLine("Failed to remove owner.");
                    }

                    break;
            }
            default:
                throw new ArgumentException($"Unknown owner subcommand: {action}. Use list, add, or remove.");
        }
    }
}
