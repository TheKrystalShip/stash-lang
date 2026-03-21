using System;
using System.Collections.Generic;

namespace Stash.Cli.PackageManager.Commands;

public static class OwnerCommand
{
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
