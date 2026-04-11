using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Scheduler;
using Stash.Scheduler.Models;

namespace Stash.Cli.ServiceManager;

/// <summary>Implements <c>stash service list</c>.</summary>
public static class ListCommand
{
    public static void Execute(string[] args)
    {
        bool system = args.Contains("--system");
        var manager = ServiceManagerFactory.Create(system);
        IReadOnlyList<ServiceInfo> services = manager.List();

        if (services.Count == 0)
        {
            Console.WriteLine("No Stash-managed services found.");
            return;
        }

        string[] headers = ["NAME", "STATUS", "SCHEDULE", "LAST RUN", "NEXT RUN"];
        var rows = new List<string[]>(services.Count);

        foreach (ServiceInfo svc in services)
        {
            string schedule = svc.Schedule ?? "(long-running)";
            string lastRun = svc.LastRunTime.HasValue
                ? svc.LastRunTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : "-";
            string nextRun = svc.NextRunTime.HasValue
                ? svc.NextRunTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : "-";
            rows.Add([svc.Name, TableFormatter.FormatState(svc.State), schedule, lastRun, nextRun]);
        }

        TableFormatter.Print(headers, rows);
    }

}
