namespace Stash.Scheduler.Platforms;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Stash.Scheduler;
using Stash.Scheduler.Logging;
using Stash.Scheduler.Models;
using Stash.Scheduler.Validation;

internal sealed class WindowsTaskServiceManager : IServiceManager
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private readonly bool _systemMode;

    // Task Scheduler properties managed by the service definition — must not be overridden via PlatformExtras
    private static readonly HashSet<string> BlockedTaskKeys = new(StringComparer.Ordinal)
    {
        "Command", "Arguments", "UserId", "RunLevel"
    };

    private static readonly string[] DayNames =
    {
        "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"
    };

    private static readonly string[] MonthNames =
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    };

    public WindowsTaskServiceManager(bool systemMode)
    {
        _systemMode = systemMode;
    }

    // ── Naming helpers ────────────────────────────────────────────────────────

    private static string GetTaskPath(string serviceName) =>
        $@"\Stash\stash-{serviceName}";

    // ── Availability ──────────────────────────────────────────────────────────

    public bool IsAvailable() =>
        FindExecutableOnPath("schtasks.exe") is not null ||
        FindExecutableOnPath("schtasks") is not null;

    // ── Install ───────────────────────────────────────────────────────────────

    public ServiceResult Install(ServiceDefinition definition)
    {
        // 1. Validate
        ValidationResult validation = InputValidator.ValidateAll(definition);
        if (!validation.Success)
            return ServiceResult.Fail(validation.Error!);

        string resolvedScript = validation.ResolvedScriptPath;

        // 1b. Validate Windows-specific blocked keys in PlatformExtras
        if (definition.PlatformExtras is not null)
        {
            foreach (string key in definition.PlatformExtras.Keys)
            {
                if (BlockedTaskKeys.Contains(key))
                    return ServiceResult.Fail(
                        $"PlatformExtra key '{key}' is blocked for Windows Task Scheduler: " +
                        "this key is managed by the service definition fields.");
            }
        }

        // 2. Generate task XML
        XDocument taskXml;
        try
        {
            taskXml = GenerateTaskXml(definition, resolvedScript);
        }
        catch (InvalidOperationException ex)
        {
            return ServiceResult.Fail(ex.Message);
        }

        // 3. Write XML to a temp file (Task Scheduler requires UTF-16)
        string tempFile = Path.Combine(Path.GetTempPath(), $"stash-task-{definition.Name}.xml");
        try
        {
            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.Unicode,
                Indent = true,
            };
            using (var writer = XmlWriter.Create(tempFile, settings))
            {
                taskXml.Save(writer);
            }
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail($"Failed to write task XML to temp file: {ex.Message}");
        }

        ServiceResult createResult;
        try
        {
            // 4. Register task via schtasks.exe
            string taskPath = GetTaskPath(definition.Name);
            var (exitCode, output) = RunSchtasks("/create", "/tn", taskPath, "/xml", tempFile, "/f");
            createResult = exitCode == 0
                ? ServiceResult.Ok()
                : ServiceResult.Fail($"schtasks /create failed: {output}");
        }
        finally
        {
            // 5. Delete temp XML file (best-effort)
            try
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
            catch { /* best-effort */ }
        }

        if (!createResult.Success) return createResult;

        // 6. Write sidecar metadata
        SidecarManager.Write(new SidecarData
        {
            Name = definition.Name,
            ScriptPath = resolvedScript,
            InstalledAt = DateTime.UtcNow.ToString("O"),
            InstalledBy = Environment.UserName,
            Mode = _systemMode ? "system" : "user",
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
        string taskPath = GetTaskPath(serviceName);

        // 1. Delete the task
        var (exitCode, output) = RunSchtasks("/delete", "/tn", taskPath, "/f");
        if (exitCode != 0)
            return ServiceResult.Fail($"schtasks /delete failed: {output}");

        // 2. Delete sidecar
        SidecarManager.Delete(serviceName);

        // 3. Delete logs
        ServiceLogManager.RemoveLogDirectory(serviceName);

        return ServiceResult.Ok();
    }

    // ── Start / Stop / Restart / Enable / Disable ─────────────────────────────

    public ServiceResult Start(string serviceName)
    {
        string taskPath = GetTaskPath(serviceName);
        var (exitCode, output) = RunSchtasks("/run", "/tn", taskPath);
        return exitCode == 0
            ? ServiceResult.Ok()
            : ServiceResult.Fail($"schtasks /run failed: {output}");
    }

    public ServiceResult Stop(string serviceName)
    {
        string taskPath = GetTaskPath(serviceName);
        var (exitCode, output) = RunSchtasks("/end", "/tn", taskPath);
        return exitCode == 0
            ? ServiceResult.Ok()
            : ServiceResult.Fail($"schtasks /end failed: {output}");
    }

    public ServiceResult Restart(string serviceName)
    {
        Stop(serviceName); // Best-effort — task might not be running
        return Start(serviceName);
    }

    public ServiceResult Enable(string serviceName)
    {
        string taskPath = GetTaskPath(serviceName);
        var (exitCode, output) = RunSchtasks("/change", "/tn", taskPath, "/enable");
        return exitCode == 0
            ? ServiceResult.Ok()
            : ServiceResult.Fail($"schtasks /change /enable failed: {output}");
    }

    public ServiceResult Disable(string serviceName)
    {
        string taskPath = GetTaskPath(serviceName);
        var (exitCode, output) = RunSchtasks("/change", "/tn", taskPath, "/disable");
        return exitCode == 0
            ? ServiceResult.Ok()
            : ServiceResult.Fail($"schtasks /change /disable failed: {output}");
    }

    // ── GetStatus ─────────────────────────────────────────────────────────────

    public ServiceStatus GetStatus(string serviceName)
    {
        SidecarData? sidecar = SidecarManager.Read(serviceName);
        string taskPath = GetTaskPath(serviceName);

        var (exitCode, output) = RunSchtasks("/query", "/tn", taskPath, "/fo", "csv", "/v", "/nh");

        ServiceState state;
        DateTime? lastRun = null;
        DateTime? nextRun = null;

        if (exitCode != 0)
        {
            state = sidecar is not null ? ServiceState.Orphaned : ServiceState.Unknown;
        }
        else
        {
            (state, lastRun, nextRun) = ParseSchtasksQueryCsv(output, sidecar);
        }

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
            LastRunTime = lastRun,
            NextRunTime = nextRun,
            InstalledAt = installedAt,
            Mode = sidecar?.Mode ?? (_systemMode ? "system" : "user"),
            PlatformInfo = $"Task Scheduler ({taskPath})",
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

        // Discover unmanaged \Stash\ tasks from schtasks
        var (exitCode, output) = RunSchtasks("/query", "/fo", "csv", "/v", "/nh");
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.Contains(@"\Stash\", StringComparison.OrdinalIgnoreCase)) continue;

                string? name = ExtractServiceNameFromCsvLine(line);
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

    // ── Task XML generation ───────────────────────────────────────────────────

    /// <summary>
    /// Generates the Task Scheduler XML document for the given service definition.
    /// </summary>
    internal XDocument GenerateTaskXml(ServiceDefinition def, string resolvedScriptPath)
    {
        string description = def.Description ?? $"Stash service: {def.Name}";
        string workingDir = def.WorkingDirectory
            ?? Path.GetDirectoryName(resolvedScriptPath) ?? @"C:\";
        string logPath = ServiceLogManager.GetCurrentLogPath(def.Name);
        bool isPeriodic = def.Schedule is not null;

        // Build cmd.exe argument string with log redirection
        string commandArgs = BuildCommandArguments(def, resolvedScriptPath, logPath);

        // Triggers
        IReadOnlyList<XElement> triggers;
        if (!isPeriodic)
        {
            triggers = new[] { new XElement(Ns + "BootTrigger") };
        }
        else
        {
            CronExpression cron = CronExpression.Parse(def.Schedule!);
            triggers = CronToTriggers(cron);
        }

        // Principal
        string userId;
        string logonType;
        if (_systemMode)
        {
            userId = def.User ?? "S-1-5-18"; // SYSTEM account SID
            logonType = "Password";
        }
        else
        {
            userId = def.User ?? Environment.UserName;
            logonType = "InteractiveToken";
        }

        // ExecutionTimeLimit: PT0S (unlimited) for long-running, PT72H for periodic
        string executionTimeLimit = isPeriodic ? "PT72H" : "PT0S";

        var settingsElements = new List<XElement>
        {
            new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
            new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
            new XElement(Ns + "StopIfGoingOnBatteries", "false"),
            new XElement(Ns + "AllowHardTerminate", "true"),
            new XElement(Ns + "StartWhenAvailable", "true"),
            new XElement(Ns + "RunOnlyIfNetworkAvailable", "false"),
            new XElement(Ns + "Enabled", def.AutoStart ? "true" : "false"),
            new XElement(Ns + "Hidden", "false"),
            new XElement(Ns + "RunOnlyIfIdle", "false"),
            new XElement(Ns + "WakeToRun", "false"),
            new XElement(Ns + "ExecutionTimeLimit", executionTimeLimit),
        };

        // RestartOnFailure — only for long-running services
        if (!isPeriodic && def.RestartOnFailure)
        {
            int delaySec = def.RestartDelaySec;
            int count = def.MaxRestarts > 0 ? def.MaxRestarts : 999999;
            settingsElements.Add(new XElement(Ns + "RestartOnFailure",
                new XElement(Ns + "Interval", $"PT{delaySec}S"),
                new XElement(Ns + "Count", count.ToString(CultureInfo.InvariantCulture))));
        }

        // Platform extras
        if (def.PlatformExtras is not null)
        {
            foreach (var (key, value) in def.PlatformExtras)
            {
                settingsElements.Add(new XElement(Ns + key, value));
            }
        }

        return new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(Ns + "Task",
                new XAttribute("version", "1.2"),
                new XElement(Ns + "RegistrationInfo",
                    new XElement(Ns + "Description", description),
                    new XElement(Ns + "Author", "Stash")),
                new XElement(Ns + "Triggers", triggers),
                new XElement(Ns + "Principals",
                    new XElement(Ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(Ns + "UserId", userId),
                        new XElement(Ns + "LogonType", logonType),
                        new XElement(Ns + "RunLevel", "LeastPrivilege"))),
                new XElement(Ns + "Settings", settingsElements),
                new XElement(Ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(Ns + "Exec",
                        new XElement(Ns + "Command", "cmd.exe"),
                        new XElement(Ns + "Arguments", commandArgs),
                        new XElement(Ns + "WorkingDirectory", workingDir)))));
    }

    /// <summary>
    /// Converts a <see cref="CronExpression"/> to a list of Task Scheduler
    /// <c>CalendarTrigger</c> XML elements.
    /// </summary>
    internal static IReadOnlyList<XElement> CronToTriggers(CronExpression expr)
    {
        bool allMinutes = expr.Minutes.Length == 60;
        bool allHours   = expr.Hours.Length   == 24;
        bool allDom     = expr.DaysOfMonth.Length == 31;
        bool allMonths  = expr.Months.Length   == 12;
        bool allDow     = expr.DaysOfWeek.Length == 7;

        // Case 1: Pure repetition — all fields except (optionally) minutes are wildcards.
        // Handles "* * * * *" and "*/N * * * *" patterns.
        if (allHours && allDom && allMonths && allDow)
        {
            int intervalMinutes;
            if (allMinutes)
            {
                intervalMinutes = 1;
            }
            else if (TryGetUniformStepInterval(expr.Minutes, 60, out int step))
            {
                intervalMinutes = step;
            }
            else
            {
                // Case 1b: Hourly execution at specific minute(s) — generate PT60M repetition per minute offset
                var hourlyTriggers = new List<XElement>();
                foreach (int minute in expr.Minutes)
                {
                    hourlyTriggers.Add(new XElement(Ns + "CalendarTrigger",
                        new XElement(Ns + "StartBoundary", $"2026-01-01T00:{minute:D2}:00"),
                        new XElement(Ns + "Repetition",
                            new XElement(Ns + "Interval", "PT60M"),
                            new XElement(Ns + "Duration", "P1D"),
                            new XElement(Ns + "StopAtDurationEnd", "false")),
                        new XElement(Ns + "ScheduleByDay",
                            new XElement(Ns + "DaysInterval", "1"))));
                }
                return hourlyTriggers;
            }

            return new[]
            {
                new XElement(Ns + "CalendarTrigger",
                    new XElement(Ns + "StartBoundary", "2026-01-01T00:00:00"),
                    new XElement(Ns + "Repetition",
                        new XElement(Ns + "Interval", $"PT{intervalMinutes}M"),
                        new XElement(Ns + "Duration", "P1D"),
                        new XElement(Ns + "StopAtDurationEnd", "false")),
                    new XElement(Ns + "ScheduleByDay",
                        new XElement(Ns + "DaysInterval", "1")))
            };
        }

        {
            // Case 2: General expansion — generate one CalendarTrigger per (hour, minute) pair.
            // The schedule type (day/week/month) is determined by the DOW and DOM constraints.
            int[] hoursToUse   = expr.Hours;
            int[] minutesToUse = expr.Minutes;

            var result = new List<XElement>();

            foreach (int hour in hoursToUse)
            {
                foreach (int minute in minutesToUse)
                {
                    result.Add(BuildCalendarTrigger(hour, minute, expr, allDom, allDow, allMonths));
                }
            }

            return result;
        }
    }

    // ── XML building helpers ──────────────────────────────────────────────────

    private static XElement BuildCalendarTrigger(
        int hour, int minute,
        CronExpression expr,
        bool allDom, bool allDow, bool allMonths)
    {
        string startBoundary = $"2026-01-01T{hour:D2}:{minute:D2}:00";

        XElement scheduleElement;

        if (!allDow)
        {
            // Weekly schedule — list the specific days of week
            var dowElements = expr.DaysOfWeek
                .Select(d => new XElement(Ns + DayNames[d]))
                .ToArray();

            scheduleElement = new XElement(Ns + "ScheduleByWeek",
                new XElement(Ns + "WeeksInterval", "1"),
                new XElement(Ns + "DaysOfWeek", dowElements));
        }
        else if (!allDom)
        {
            // Monthly schedule — list specific days and months
            var domElements = expr.DaysOfMonth
                .Select(d => new XElement(Ns + "Day", d.ToString(CultureInfo.InvariantCulture)))
                .ToArray();

            XElement monthsElement;
            if (allMonths)
            {
                // All months — list all 12
                monthsElement = new XElement(Ns + "Months",
                    MonthNames.Select(m => new XElement(Ns + m)).ToArray());
            }
            else
            {
                // Specific months (month values are 1-based, MonthNames is 0-based)
                monthsElement = new XElement(Ns + "Months",
                    expr.Months.Select(m => new XElement(Ns + MonthNames[m - 1])).ToArray());
            }

            scheduleElement = new XElement(Ns + "ScheduleByMonth",
                new XElement(Ns + "DaysOfMonth", domElements),
                monthsElement);
        }
        else
        {
            // Daily schedule
            scheduleElement = new XElement(Ns + "ScheduleByDay",
                new XElement(Ns + "DaysInterval", "1"));
        }

        return new XElement(Ns + "CalendarTrigger",
            new XElement(Ns + "StartBoundary", startBoundary),
            scheduleElement);
    }

    private static string BuildCommandArguments(
        ServiceDefinition def,
        string resolvedScriptPath,
        string logPath)
    {
        var sb = new StringBuilder();

        // Environment variable assignments using cmd.exe "set" syntax
        if (def.Environment is not null && def.Environment.Count > 0)
        {
            foreach (var (key, value) in def.Environment)
            {
                // Remove double quotes and newlines from key/value to avoid cmd.exe parsing issues
                string safeKey = key.Replace("\"", "");
                string safeValue = value
                    .Replace("\"", "")
                    .Replace("\r\n", " ")
                    .Replace("\r", " ")
                    .Replace("\n", " ");
                sb.Append($"set \"{safeKey}={safeValue}\" && ");
            }
        }

        // Main stash command with output redirection
        sb.Append($"stash \"{resolvedScriptPath}\"");
        sb.Append($" >> \"{logPath}\" 2>&1");

        // Wrap everything in /c "..."
        return $"/c \"{sb}\"";
    }

    // ── Status parsing ────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the CSV output of <c>schtasks /query /fo csv /v /nh</c> to determine
    /// service state, last run time, and next run time.
    /// </summary>
    internal static (ServiceState State, DateTime? LastRun, DateTime? NextRun) ParseSchtasksQueryCsv(
        string output, SidecarData? sidecar)
    {
        bool hasSidecar = sidecar is not null;

        if (string.IsNullOrWhiteSpace(output))
            return (hasSidecar ? ServiceState.Orphaned : ServiceState.Unknown, null, null);

        // schtasks /fo csv output (with /v /nh):
        // Column indices (0-based): 1=TaskName, 2=NextRunTime, 3=Status, 5=LastRunTime, 6=LastResult
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            string[] fields = ParseCsvLine(trimmed);
            if (fields.Length < 7) continue;

            // Skip header lines (if any slipped through)
            if (string.Equals(fields[3], "Status", StringComparison.OrdinalIgnoreCase)) continue;

            string status     = fields[3];
            string lastRunStr = fields[5];
            string nextRunStr = fields[2];

            ServiceState state = MapSchtasksStatus(status, hasSidecar);
            DateTime? lastRun = ParseSchtasksDateTime(lastRunStr);
            DateTime? nextRun = ParseSchtasksDateTime(nextRunStr);

            return (state, lastRun, nextRun);
        }

        return (hasSidecar ? ServiceState.Orphaned : ServiceState.Unknown, null, null);
    }

    private static ServiceState MapSchtasksStatus(string status, bool hasSidecar)
    {
        if (!hasSidecar) return ServiceState.Unmanaged;

        return status.Trim() switch
        {
            "Running" => ServiceState.Running,
            "Ready" => ServiceState.Active,
            "Queued" => ServiceState.Active,
            "Disabled" => ServiceState.Inactive,
            _ => ServiceState.Unknown,
        };
    }

    private static DateTime? ParseSchtasksDateTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "N/A" || value == "Never")
            return null;

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out DateTime dt))
        {
            return dt.ToUniversalTime();
        }

        return null;
    }

    // ── CVS parsing ───────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a single CSV line, handling quoted fields that may contain commas.
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        int i = 0;

        while (i <= line.Length)
        {
            if (i == line.Length)
            {
                fields.Add(string.Empty);
                break;
            }

            if (line[i] == '"')
            {
                // Quoted field
                i++; // skip opening quote
                var sb = new StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                        }
                        else
                        {
                            i++; // skip closing quote
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(line[i++]);
                    }
                }
                fields.Add(sb.ToString());
                // Skip trailing comma
                if (i < line.Length && line[i] == ',') i++;
            }
            else
            {
                // Unquoted field
                int comma = line.IndexOf(',', i);
                if (comma < 0)
                {
                    fields.Add(line.Substring(i));
                    break;
                }
                fields.Add(line.Substring(i, comma - i));
                i = comma + 1;
            }
        }

        return fields.ToArray();
    }

    // ── Misc helpers ──────────────────────────────────────────────────────────

    private static string? ExtractServiceNameFromCsvLine(string line)
    {
        const string prefix = @"\Stash\stash-";

        int idx = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        string afterPrefix = line.Substring(idx + prefix.Length);

        // Trim CSV quote/comma/whitespace
        afterPrefix = afterPrefix.Split(new[] { '"', ',', '\r' }, 2)[0].Trim();
        return string.IsNullOrEmpty(afterPrefix) ? null : afterPrefix;
    }

    /// <summary>
    /// Determines if the given minute values form a uniform step interval
    /// starting at 0 that exactly divides the period (e.g., 0, 5, 10, ..., 55 → step=5).
    /// </summary>
    private static bool TryGetUniformStepInterval(int[] values, int period, out int step)
    {
        step = 0;
        if (values.Length < 2) return false;
        if (values[0] != 0) return false;

        step = values[1] - values[0];
        if (step <= 0) return false;

        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] - values[i - 1] != step)
            {
                step = 0;
                return false;
            }
        }

        return period % step == 0 && values.Length == period / step;
    }

    // ── schtasks.exe invocation ───────────────────────────────────────────────

    private static (int ExitCode, string Output) RunSchtasks(params string[] arguments)
    {
        string? schtasksPath = FindExecutableOnPath("schtasks.exe")
            ?? FindExecutableOnPath("schtasks");

        if (schtasksPath is null)
            return (-1, "schtasks.exe not found on PATH");

        var psi = new ProcessStartInfo(schtasksPath)
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
                return (-1, "Failed to start schtasks.exe process.");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();

            bool completed = process.WaitForExit(30_000);
            if (!completed)
            {
                try { process.Kill(); } catch { /* best-effort */ }
                return (-1, "schtasks.exe timed out after 30 seconds.");
            }

            return (process.ExitCode, stdout + stderr);
        }
        catch (Exception ex)
        {
            return (-1, $"Failed to run schtasks.exe: {ex.Message}");
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
