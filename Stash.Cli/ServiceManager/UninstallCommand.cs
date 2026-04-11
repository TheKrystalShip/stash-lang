using System;
using System.Linq;
using Stash.Scheduler;

namespace Stash.Cli.ServiceManager;

/// <summary>Implements <c>stash service uninstall</c>.</summary>
public static class UninstallCommand
{
    public static void Execute(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: stash service uninstall <name> [--system]");
            Environment.Exit(64);
            return;
        }

        string name = args[0];
        bool system = args.Contains("--system");
        var manager = ServiceManagerFactory.Create(system);
        ServiceResult result = manager.Uninstall(name);

        if (result.Success)
            Console.WriteLine($"Service '{name}' uninstalled successfully.");
        else
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            Environment.Exit(1);
        }
    }
}
