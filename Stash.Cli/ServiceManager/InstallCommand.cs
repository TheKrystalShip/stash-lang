using System;
using System.Collections.Generic;
using System.IO;
using Stash.Scheduler;
using Stash.Scheduler.Models;

namespace Stash.Cli.ServiceManager;

/// <summary>Implements <c>stash service install</c>.</summary>
public static class InstallCommand
{
    public static void Execute(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            Console.Error.WriteLine("Usage: stash service install <script> [options]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --name <name>               Service name (default: script filename)");
            Console.Error.WriteLine("  --schedule <cron>           Cron schedule (omit for long-running)");
            Console.Error.WriteLine("  --description <text>        Service description");
            Console.Error.WriteLine("  --user <user>               Run as this user");
            Console.Error.WriteLine("  --workdir <path>            Working directory");
            Console.Error.WriteLine("  --env KEY=VALUE             Environment variable (repeatable)");
            Console.Error.WriteLine("  --no-restart-on-failure     Disable restart on failure (default: enabled)");
            Console.Error.WriteLine("  --max-restarts <n>          Max restart attempts (default 0 = unlimited)");
            Console.Error.WriteLine("  --restart-delay <n>         Seconds between restarts (default 5)");
            Console.Error.WriteLine("  --system                    Install as system service (requires root)");
            Console.Error.WriteLine("  --platform-extra KEY=VALUE  Platform-specific extra (repeatable)");
            Environment.Exit(64);
            return;
        }

        string scriptPath = args[0];
        string? name = null;
        string? schedule = null;
        string? description = null;
        string? user = null;
        string? workdir = null;
        var env = new Dictionary<string, string>();
        bool restartOnFailure = true;
        int maxRestarts = 0;
        int restartDelay = 5;
        bool systemMode = false;
        var platformExtra = new Dictionary<string, string>();

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--name":
                    if (i + 1 >= args.Length) ExitMissingValue("--name");
                    name = args[++i];
                    break;
                case "--schedule":
                    if (i + 1 >= args.Length) ExitMissingValue("--schedule");
                    schedule = args[++i];
                    break;
                case "--description":
                    if (i + 1 >= args.Length) ExitMissingValue("--description");
                    description = args[++i];
                    break;
                case "--user":
                    if (i + 1 >= args.Length) ExitMissingValue("--user");
                    user = args[++i];
                    break;
                case "--workdir":
                    if (i + 1 >= args.Length) ExitMissingValue("--workdir");
                    workdir = args[++i];
                    break;
                case "--env":
                    if (i + 1 >= args.Length) ExitMissingValue("--env");
                    ParseKeyValue("--env", args[++i], env);
                    break;
                case "--no-restart-on-failure":
                    restartOnFailure = false;
                    break;
                case "--max-restarts":
                    if (i + 1 >= args.Length) ExitMissingValue("--max-restarts");
                    if (!int.TryParse(args[++i], out maxRestarts))
                    {
                        Console.Error.WriteLine("Error: --max-restarts must be an integer.");
                        Environment.Exit(1);
                    }
                    break;
                case "--restart-delay":
                    if (i + 1 >= args.Length) ExitMissingValue("--restart-delay");
                    if (!int.TryParse(args[++i], out restartDelay))
                    {
                        Console.Error.WriteLine("Error: --restart-delay must be an integer.");
                        Environment.Exit(1);
                    }
                    break;
                case "--system":
                    systemMode = true;
                    break;
                case "--platform-extra":
                    if (i + 1 >= args.Length) ExitMissingValue("--platform-extra");
                    ParseKeyValue("--platform-extra", args[++i], platformExtra);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    Environment.Exit(64);
                    break;
            }
        }

        // Derive name from script filename if not provided
        if (name is null)
        {
            string baseName = Path.GetFileNameWithoutExtension(scriptPath);
            name = baseName;
        }

        var definition = new ServiceDefinition
        {
            Name = name,
            ScriptPath = scriptPath,
            Description = description,
            Schedule = schedule,
            WorkingDirectory = workdir,
            Environment = env.Count > 0 ? env : null,
            User = user,
            RestartOnFailure = restartOnFailure,
            MaxRestarts = maxRestarts,
            RestartDelaySec = restartDelay,
            SystemMode = systemMode,
            PlatformExtras = platformExtra.Count > 0 ? platformExtra : null,
        };

        var manager = ServiceManagerFactory.Create(systemMode);
        ServiceResult result = manager.Install(definition);

        if (result.Success)
        {
            Console.WriteLine($"Service '{name}' installed successfully.");
            if (schedule is not null)
                Console.WriteLine($"  Schedule: {schedule}");
        }
        else
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            Environment.Exit(1);
        }
    }

    private static void ExitMissingValue(string flag)
    {
        Console.Error.WriteLine($"Error: {flag} requires a value.");
        Environment.Exit(64);
    }

    private static void ParseKeyValue(string flag, string value, Dictionary<string, string> target)
    {
        int eq = value.IndexOf('=');
        if (eq <= 0)
        {
            Console.Error.WriteLine($"Error: {flag} value must be in KEY=VALUE format.");
            Environment.Exit(64);
        }
        string key = value[..eq];
        string val = value[(eq + 1)..];
        target[key] = val;
    }
}
