using System;
using System.Linq;
using Stash.Scheduler;

namespace Stash.Cli.ServiceManager;

/// <summary>Implements <c>stash service stop</c>.</summary>
public static class StopCommand
{
    public static void Execute(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: stash service stop <name> [--system]");
            Environment.Exit(64);
            return;
        }

        string name = args[0];
        bool system = args.Contains("--system");
        var manager = ServiceManagerFactory.Create(system);
        ServiceResult result = manager.Stop(name);

        if (result.Success)
            Console.WriteLine($"Service '{name}' stopped.");
        else
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            Environment.Exit(1);
        }
    }
}
