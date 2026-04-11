using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Scheduler;
using Stash.Scheduler.Models;

namespace Stash.Cli.ServiceManager;

/// <summary>Implements <c>stash service status [name]</c>.</summary>
public static class StatusCommand
{
    public static void Execute(string[] args)
    {
        bool system = args.Any(a => a == "--system");
        string[] filtered = args.Where(a => a != "--system").ToArray();

        if (filtered.Length > 0 && !filtered[0].StartsWith('-'))
        {
            ShowSingle(filtered[0], system);
        }
        else
        {
            ShowAll(system);
        }
    }

    private static void ShowSingle(string name, bool system)
    {
        var manager = ServiceManagerFactory.Create(system);
        ServiceStatus status = manager.GetStatus(name);

        string scheduleDisplay = status.Schedule is not null
            ? $"{status.Schedule} ({DescribeSchedule(status.Schedule)})"
            : "(long-running)";

        Console.WriteLine($"Service:          {status.Name}");
        Console.WriteLine($"Status:           {TableFormatter.FormatState(status.State)}");
        Console.WriteLine($"Schedule:         {scheduleDisplay}");
        Console.WriteLine($"Script:           {status.ScriptPath ?? "-"}");
        Console.WriteLine($"Working Dir:      {status.WorkingDirectory ?? "-"}");
        Console.WriteLine($"User:             {status.User ?? "-"}");

        string lastRun = status.LastRunTime.HasValue
            ? $"{status.LastRunTime.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} (exit code {status.LastExitCode ?? 0})"
            : "-";
        Console.WriteLine($"Last Run:         {lastRun}");

        string nextRun = status.NextRunTime.HasValue
            ? status.NextRunTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "-";
        Console.WriteLine($"Next Run:         {nextRun}");

        string installed = status.InstalledAt.HasValue
            ? status.InstalledAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "-";
        Console.WriteLine($"Installed:        {installed}");
        Console.WriteLine($"Mode:             {status.Mode ?? "user"}");
        Console.WriteLine($"Platform:         {status.PlatformInfo ?? "-"}");
    }

    private static void ShowAll(bool system)
    {
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

    private static string DescribeSchedule(string cron)
    {
        // Very basic human-readable hints for common cron patterns
        return cron switch
        {
            "* * * * *"     => "every minute",
            "*/5 * * * *"   => "every 5 minutes",
            "*/10 * * * *"  => "every 10 minutes",
            "*/15 * * * *"  => "every 15 minutes",
            "*/30 * * * *"  => "every 30 minutes",
            "0 * * * *"     => "every hour",
            "0 0 * * *"     => "daily at midnight",
            "0 2 * * *"     => "daily at 02:00",
            "0 0 * * 0"     => "weekly on Sunday",
            "0 0 1 * *"     => "monthly on the 1st",
            _               => "custom schedule",
        };
    }
}
