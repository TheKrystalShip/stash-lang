namespace Stash.Scheduler.Platforms;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Stash.Scheduler;
using Stash.Scheduler.Logging;
using Stash.Scheduler.Models;
using Stash.Scheduler.Validation;

internal sealed class LaunchdServiceManager : IServiceManager
{
    private readonly bool _systemMode;

    // Plist keys controlled by the service definition — must not be overridden via PlatformExtras
    private static readonly HashSet<string> BlockedPlistKeys = new(StringComparer.Ordinal)
    {
        "ProgramArguments", "Label", "UserName",
        "WorkingDirectory", "KeepAlive", "RunAtLoad",
        "StartCalendarInterval", "StandardOutPath", "StandardErrorPath",
        "EnvironmentVariables"
    };

    public LaunchdServiceManager(bool systemMode)
    {
        _systemMode = systemMode;
    }

    // ── Availability ─────────────────────────────────────────────────────────

    public bool IsAvailable() => FindExecutableOnPath("launchctl") is not null;

    // ── Plist paths ───────────────────────────────────────────────────────────

    private string GetPlistDirectory()
    {
        if (_systemMode)
            return "/Library/LaunchDaemons";

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "LaunchAgents");
    }

    private string GetPlistPath(string serviceName) =>
        Path.Combine(GetPlistDirectory(), $"com.stash.{serviceName}.plist");

    private static string GetLabel(string serviceName) =>
        $"com.stash.{serviceName}";

    // ── Install ───────────────────────────────────────────────────────────────

    public ServiceResult Install(ServiceDefinition definition)
    {
        // 1. Validate
        ValidationResult validation = InputValidator.ValidateAll(definition);
        if (!validation.Success)
            return ServiceResult.Fail(validation.Error!);

        string resolvedScript = validation.ResolvedScriptPath;

        // 1b. Validate launchd-specific blocked keys in PlatformExtras
        if (definition.PlatformExtras is not null)
        {
            foreach (string key in definition.PlatformExtras.Keys)
            {
                if (BlockedPlistKeys.Contains(key))
                    return ServiceResult.Fail(
                        $"PlatformExtra key '{key}' is blocked for launchd: " +
                        "this key is managed by the service definition fields.");
            }
        }

        // 2. Ensure plist directory exists
        string plistDir = GetPlistDirectory();
        try
        {
            if (!Directory.Exists(plistDir))
                Directory.CreateDirectory(plistDir);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail($"Failed to create plist directory '{plistDir}': {ex.Message}");
        }

        // 3. Generate plist
        XDocument plist;
        try
        {
            plist = GeneratePlist(definition, resolvedScript);
        }
        catch (InvalidOperationException ex)
        {
            return ServiceResult.Fail(ex.Message);
        }

        // 4. Write plist atomically
        string plistPath = GetPlistPath(definition.Name);
        ServiceResult writePlist = WritePlistAtomic(plistPath, plist);
        if (!writePlist.Success) return writePlist;

        // 5. launchctl load <plist>
        if (definition.AutoStart)
        {
            var (exitCode, output) = RunLaunchctl("load", plistPath);
            if (exitCode != 0)
                return ServiceResult.Fail($"launchctl load failed: {output}");
        }

        // 6. Write sidecar metadata
        SidecarManager.Write(new SidecarData
        {
            Name = definition.Name,
            ScriptPath = resolvedScript,
            InstalledAt = DateTime.UtcNow.ToString("O"),
            InstalledBy = Environment.UserName,
            Mode = _systemMode ? "system" : "user",
            StashVersion = "1.0.0",
            Schedule = definition.Schedule,
            Description = definition.Description,
            PlatformExtras = definition.PlatformExtras is not null
                ? new Dictionary<string, string>(definition.PlatformExtras)
                : null,
        });

        // 7. Create log directory
        ServiceLogManager.EnsureLogDirectory(definition.Name);

        return ServiceResult.Ok();
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────

    public ServiceResult Uninstall(string serviceName)
    {
        string plistPath = GetPlistPath(serviceName);

        // 1. Unload the service (this also stops it)
        if (File.Exists(plistPath))
            RunLaunchctl("unload", plistPath);

        // 2. Delete plist file
        try
        {
            if (File.Exists(plistPath))
                File.Delete(plistPath);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail($"Failed to remove plist file: {ex.Message}");
        }

        // 3. Delete sidecar
        SidecarManager.Delete(serviceName);

        // 4. Delete logs
        ServiceLogManager.RemoveLogDirectory(serviceName);

        return ServiceResult.Ok();
    }

    // ── Start / Stop / Restart / Enable / Disable ─────────────────────────────

    public ServiceResult Start(string serviceName)
    {
        var (exitCode, output) = RunLaunchctl("start", GetLabel(serviceName));
        return exitCode == 0
            ? ServiceResult.Ok()
            : ServiceResult.Fail($"launchctl start failed: {output}");
    }

    public ServiceResult Stop(string serviceName)
    {
        var (exitCode, output) = RunLaunchctl("stop", GetLabel(serviceName));
        return exitCode == 0
            ? ServiceResult.Ok()
            : ServiceResult.Fail($"launchctl stop failed: {output}");
    }

    public ServiceResult Restart(string serviceName)
    {
        Stop(serviceName); // Best-effort — service might not be running
        return Start(serviceName);
    }

    public ServiceResult Enable(string serviceName)
    {
        string plistPath = GetPlistPath(serviceName);
        if (!File.Exists(plistPath))
            return ServiceResult.Fail($"Plist file not found: {plistPath}");

        var (exitCode, output) = RunLaunchctl("load", plistPath);
        return exitCode == 0
            ? ServiceResult.Ok()
            : ServiceResult.Fail($"launchctl load failed: {output}");
    }

    public ServiceResult Disable(string serviceName)
    {
        string plistPath = GetPlistPath(serviceName);
        if (!File.Exists(plistPath))
            return ServiceResult.Fail($"Plist file not found: {plistPath}");

        var (exitCode, output) = RunLaunchctl("unload", plistPath);
        return exitCode == 0
            ? ServiceResult.Ok()
            : ServiceResult.Fail($"launchctl unload failed: {output}");
    }

    // ── GetStatus ─────────────────────────────────────────────────────────────

    public ServiceStatus GetStatus(string serviceName)
    {
        SidecarData? sidecar = SidecarManager.Read(serviceName);
        string label = GetLabel(serviceName);

        var (exitCode, output) = RunLaunchctl("list", label);
        ServiceState state = ParseLaunchctlListState(exitCode, output, sidecar);

        DateTime? installedAt = null;
        if (sidecar?.InstalledAt is not null)
        {
            if (DateTime.TryParse(sidecar.InstalledAt, null,
                DateTimeStyles.RoundtripKind, out DateTime parsed))
            {
                installedAt = parsed;
            }
        }

        return new ServiceStatus
        {
            Name = serviceName,
            State = state,
            Schedule = sidecar?.Schedule,
            ScriptPath = sidecar?.ScriptPath,
            InstalledAt = installedAt,
            Mode = sidecar?.Mode ?? (_systemMode ? "system" : "user"),
            PlatformInfo = $"launchd ({label})",
        };
    }

    // ── List ──────────────────────────────────────────────────────────────────

    public IReadOnlyList<ServiceInfo> List()
    {
        var results = new List<ServiceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Managed services from sidecar
        foreach (string name in SidecarManager.ListAll())
        {
            seen.Add(name);
            ServiceStatus status = GetStatus(name);
            results.Add(new ServiceInfo
            {
                Name = name,
                State = status.State,
                Schedule = status.Schedule,
                LastRunTime = status.LastRunTime,
                NextRunTime = status.NextRunTime,
            });
        }

        // Discover unmanaged com.stash.* labels from launchctl list
        var (exitCode, output) = RunLaunchctl("list");
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // Format: PID\tLastExitStatus\tLabel (tab-separated)
                string[] parts = line.Split('\t');
                if (parts.Length < 3) continue;

                string lineLabel = parts[2].Trim();
                if (!lineLabel.StartsWith("com.stash.", StringComparison.Ordinal)) continue;

                string? name = ExtractServiceNameFromLabel(lineLabel);
                if (name is null || seen.Contains(name)) continue;

                seen.Add(name);
                results.Add(new ServiceInfo
                {
                    Name = name,
                    State = ServiceState.Unmanaged,
                });
            }
        }

        return results;
    }

    // ── GetHistory ────────────────────────────────────────────────────────────

    public IReadOnlyList<ExecutionRecord> GetHistory(string serviceName, int maxRecords = 20)
    {
        var records = new List<ExecutionRecord>();
        string logDir = ServiceLogManager.GetLogDirectory(serviceName);
        if (!Directory.Exists(logDir)) return records;

        var logFiles = Directory.GetFiles(logDir, "*.log")
            .OrderByDescending(f => f)
            .ToList();

        foreach (string logFile in logFiles)
        {
            if (records.Count >= maxRecords) break;
            var fi = new FileInfo(logFile);
            records.Add(new ExecutionRecord
            {
                Timestamp = fi.LastWriteTimeUtc,
                ExitCode = 0,
                Duration = null,
                Output = null,
            });
        }

        return records;
    }

    // ── Plist generation ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates the launchd plist XML for the given service definition.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if cron expansion exceeds 100 StartCalendarInterval entries.
    /// </exception>
    internal XDocument GeneratePlist(ServiceDefinition def, string resolvedScriptPath)
    {
        string label = GetLabel(def.Name);
        string logPath = ServiceLogManager.GetCurrentLogPath(def.Name);
        string workingDir = def.WorkingDirectory
            ?? Path.GetDirectoryName(resolvedScriptPath) ?? "/";
        bool isPeriodic = def.Schedule is not null;

        var dictElements = new List<XNode>();

        // Label
        dictElements.Add(new XElement("key", "Label"));
        dictElements.Add(new XElement("string", label));

        // ProgramArguments
        dictElements.Add(new XElement("key", "ProgramArguments"));
        dictElements.Add(new XElement("array",
            new XElement("string", "stash"),
            new XElement("string", resolvedScriptPath)));

        // WorkingDirectory
        dictElements.Add(new XElement("key", "WorkingDirectory"));
        dictElements.Add(new XElement("string", workingDir));

        // Scheduling
        if (!isPeriodic)
        {
            // Long-running: keep alive and start on load
            dictElements.Add(new XElement("key", "KeepAlive"));
            dictElements.Add(new XElement("true"));
            dictElements.Add(new XElement("key", "RunAtLoad"));
            dictElements.Add(new XElement("true"));
        }
        else
        {
            // Periodic: expand cron to StartCalendarInterval
            CronExpression cron = CronExpression.Parse(def.Schedule!);
            IReadOnlyList<Dictionary<string, int>> intervals = ExpandCronToCalendarIntervals(cron);

            var intervalDicts = intervals.Select(entry =>
            {
                var entryElements = new List<XNode>();
                foreach (var (key, value) in entry)
                {
                    entryElements.Add(new XElement("key", key));
                    entryElements.Add(new XElement("integer", value));
                }
                return (XNode)new XElement("dict", entryElements);
            });

            dictElements.Add(new XElement("key", "StartCalendarInterval"));
            dictElements.Add(new XElement("array", intervalDicts));
        }

        // User (system mode only)
        if (_systemMode && def.User is not null)
        {
            dictElements.Add(new XElement("key", "UserName"));
            dictElements.Add(new XElement("string", def.User));
        }

        // Environment variables
        if (def.Environment is not null && def.Environment.Count > 0)
        {
            var envElements = new List<XNode>();
            foreach (var (key, value) in def.Environment)
            {
                envElements.Add(new XElement("key", key));
                envElements.Add(new XElement("string", value));
            }

            dictElements.Add(new XElement("key", "EnvironmentVariables"));
            dictElements.Add(new XElement("dict", envElements));
        }

        // Log redirection
        dictElements.Add(new XElement("key", "StandardOutPath"));
        dictElements.Add(new XElement("string", logPath));
        dictElements.Add(new XElement("key", "StandardErrorPath"));
        dictElements.Add(new XElement("string", logPath));

        // Platform extras (custom plist keys)
        if (def.PlatformExtras is not null)
        {
            foreach (var (key, value) in def.PlatformExtras)
            {
                dictElements.Add(new XElement("key", key));
                dictElements.Add(new XElement("string", value));
            }
        }

        return new XDocument(
            new XDocumentType("plist",
                "-//Apple//DTD PLIST 1.0//EN",
                "http://www.apple.com/DTDs/PropertyList-1.0.dtd",
                null),
            new XElement("plist",
                new XAttribute("version", "1.0"),
                new XElement("dict", dictElements)));
    }

    /// <summary>
    /// Expands a <see cref="CronExpression"/> into a list of launchd
    /// <c>StartCalendarInterval</c> dictionaries. Each dictionary omits wildcard
    /// fields and includes only the constrained keys: Minute, Hour, Day, Month,
    /// Weekday.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the cross-product expansion would exceed 100 entries.
    /// </exception>
    internal static IReadOnlyList<Dictionary<string, int>> ExpandCronToCalendarIntervals(CronExpression expr)
    {
        // Determine which fields are "all values" (wildcard)
        bool allMinutes = expr.Minutes.Length  == 60;
        bool allHours   = expr.Hours.Length    == 24;
        bool allDom     = expr.DaysOfMonth.Length == 31;
        bool allMonths  = expr.Months.Length   == 12;
        bool allDow     = expr.DaysOfWeek.Length == 7;

        // Collect non-wildcard fields for cross-product
        var fields = new List<(string Key, int[] Values)>();
        if (!allMinutes) fields.Add(("Minute",  expr.Minutes));
        if (!allHours)   fields.Add(("Hour",    expr.Hours));
        if (!allDom)     fields.Add(("Day",     expr.DaysOfMonth));
        if (!allMonths)  fields.Add(("Month",   expr.Months));
        if (!allDow)     fields.Add(("Weekday", expr.DaysOfWeek));

        // All wildcards → run every minute, single empty entry
        if (fields.Count == 0)
            return new[] { new Dictionary<string, int>() };

        // Cross-product expansion
        var results = new List<Dictionary<string, int>> { new() };

        foreach (var (key, values) in fields)
        {
            var expanded = new List<Dictionary<string, int>>(results.Count * values.Length);
            foreach (var existing in results)
            {
                foreach (int value in values)
                {
                    var entry = new Dictionary<string, int>(existing) { [key] = value };
                    expanded.Add(entry);
                }
            }

            if (expanded.Count > 100)
                throw new InvalidOperationException(
                    $"Cron expression expands to {expanded.Count} StartCalendarInterval entries, " +
                    "which exceeds the limit of 100. Simplify the cron expression.");

            results = expanded;
        }

        return results;
    }

    // ── Status helpers ────────────────────────────────────────────────────────

    internal static ServiceState ParseLaunchctlListState(int exitCode, string output, SidecarData? sidecar)
    {
        bool hasSidecar = sidecar is not null;

        // Non-zero exit from `launchctl list <label>` means service is not loaded
        if (exitCode != 0)
            return hasSidecar ? ServiceState.Orphaned : ServiceState.Unknown;

        if (!hasSidecar)
            return ServiceState.Unmanaged;

        // `launchctl list <label>` returns a dictionary format:
        //   {
        //       "Label" = "com.stash.myservice";
        //       "LastExitStatus" = 0;
        //       "PID" = 1234;
        //   };
        int? pid = null;
        int? lastExitStatus = null;

        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim().TrimEnd(';');

            if (trimmed.Contains("\"PID\""))
            {
                string[] parts = trimmed.Split('=', 2);
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int parsedPid))
                    pid = parsedPid;
            }
            else if (trimmed.Contains("\"LastExitStatus\""))
            {
                string[] parts = trimmed.Split('=', 2);
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int parsedExit))
                    lastExitStatus = parsedExit;
            }
        }

        // If PID is present and positive, process is currently running
        if (pid is > 0)
            return ServiceState.Running;

        // If last exit status is non-zero, the service failed
        if (lastExitStatus is not null and not 0)
            return ServiceState.Failed;

        return ServiceState.Active;
    }

    // ── Misc helpers ──────────────────────────────────────────────────────────

    private static string? ExtractServiceNameFromLabel(string label)
    {
        const string prefix = "com.stash.";
        if (!label.StartsWith(prefix, StringComparison.Ordinal)) return null;
        string name = label.Substring(prefix.Length);
        return string.IsNullOrEmpty(name) ? null : name;
    }

    // ── Atomic plist write ────────────────────────────────────────────────────

    private static ServiceResult WritePlistAtomic(string path, XDocument document)
    {
        string tmpPath = path + ".tmp";
        try
        {
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                document.Save(fs);
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                // 0644 — world-readable, required by launchd
                File.SetUnixFileMode(tmpPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }

            File.Move(tmpPath, path, overwrite: true);
            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* best-effort */ }
            return ServiceResult.Fail($"Failed to write plist file '{path}': {ex.Message}");
        }
    }

    // ── launchctl invocation ──────────────────────────────────────────────────

    private static (int ExitCode, string Output) RunLaunchctl(params string[] arguments)
    {
        string? launchctlPath = FindExecutableOnPath("launchctl");
        if (launchctlPath is null)
            return (-1, "launchctl not found on PATH");

        var psi = new ProcessStartInfo(launchctlPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in arguments)
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return (-1, "Failed to start launchctl process.");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();

            bool completed = process.WaitForExit(10_000);
            if (!completed)
            {
                try { process.Kill(); } catch { /* best-effort */ }
                return (-1, "launchctl timed out after 10 seconds.");
            }

            return (process.ExitCode, stdout + stderr);
        }
        catch (Exception ex)
        {
            return (-1, $"Failed to run launchctl: {ex.Message}");
        }
    }

    private static string? FindExecutableOnPath(string name)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv is null) return null;

        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string fullPath = Path.Combine(dir, name);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }
}
