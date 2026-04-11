using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Scheduler;
using Stash.Scheduler.Models;

namespace Stash.Cli.ServiceManager;

/// <summary>Implements <c>stash service clean</c> — removes orphaned sidecar files.</summary>
public static class CleanCommand
{
    public static void Execute(string[] args)
    {
        bool system = args.Contains("--system");
        var manager = ServiceManagerFactory.Create(system);
        IReadOnlyList<ServiceInfo> services = manager.List();

        var orphaned = new List<string>();
        foreach (ServiceInfo svc in services)
        {
            if (svc.State == ServiceState.Orphaned)
                orphaned.Add(svc.Name);
        }

        if (orphaned.Count == 0)
        {
            Console.WriteLine("Nothing to clean — no orphaned sidecar files found.");
            return;
        }

        Console.WriteLine($"Found {orphaned.Count} orphaned sidecar(s):");
        foreach (string name in orphaned)
            Console.WriteLine($"  {name}");
        Console.WriteLine();

        foreach (string name in orphaned)
        {
            ServiceResult result = manager.Uninstall(name);
            if (result.Success)
                Console.WriteLine($"  Removed: {name}");
            else
                Console.Error.WriteLine($"  Failed to remove '{name}': {result.Error}");
        }

        Console.WriteLine();
        Console.WriteLine($"Cleaned {orphaned.Count} orphaned sidecar(s).");
    }
}
