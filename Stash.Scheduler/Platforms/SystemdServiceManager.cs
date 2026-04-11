namespace Stash.Scheduler.Platforms;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Stash.Scheduler;
using Stash.Scheduler.Logging;
using Stash.Scheduler.Models;
using Stash.Scheduler.Validation;

internal sealed class SystemdServiceManager : IServiceManager
{
    private readonly bool _systemMode;

    public SystemdServiceManager(bool systemMode)
    {
        _systemMode = systemMode;
    }

    // ── Availability ─────────────────────────────────────────────────────────

    public bool IsAvailable() => FindExecutableOnPath("systemctl") is not null;

    // ── Unit file paths ───────────────────────────────────────────────────────

    private string GetUnitDirectory()
    {
        if (_systemMode)
            return "/etc/systemd/system";

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "systemd", "user");
    }

    private string GetServiceUnitPath(string serviceName) =>
        Path.Combine(GetUnitDirectory(), $"stash-{serviceName}.service");

    private string GetTimerUnitPath(string serviceName) =>
        Path.Combine(GetUnitDirectory(), $"stash-{serviceName}.timer");

    private static string GetServiceUnitName(string serviceName) =>
        $"stash-{serviceName}.service";

    private static string GetTimerUnitName(string serviceName) =>
        $"stash-{serviceName}.timer";

    // ── Install ───────────────────────────────────────────────────────────────

    public ServiceResult Install(ServiceDefinition definition)
    {
        // 1. Validate
        ValidationResult validation = InputValidator.ValidateAll(definition);
        if (!validation.Success)
            return ServiceResult.Fail(validation.Error!);

        string resolvedScript = validation.ResolvedScriptPath;
        string workingDir = definition.WorkingDirectory
            ?? Path.GetDirectoryName(resolvedScript) ?? "/";
        bool isPeriodic = definition.Schedule is not null;

        // 2. Ensure unit directory exists
        string unitDir = GetUnitDirectory();
        try
        {
            if (!Directory.Exists(unitDir))
                Directory.CreateDirectory(unitDir);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail($"Failed to create unit directory '{unitDir}': {ex.Message}");
        }

        // 3. Generate and write service unit
        string serviceContent = BuildServiceUnit(definition, resolvedScript, workingDir, isPeriodic);
        ServiceResult writeService = WriteUnitFileAtomic(GetServiceUnitPath(definition.Name), serviceContent);
        if (!writeService.Success) return writeService;

        // 4. If periodic: generate and write timer unit
        if (isPeriodic)
        {
            string timerContent = BuildTimerUnit(definition.Name, definition.Description, validation.ParsedSchedule!);
            ServiceResult writeTimer = WriteUnitFileAtomic(GetTimerUnitPath(definition.Name), timerContent);
            if (!writeTimer.Success) return writeTimer;
        }

        // 5. daemon-reload
        var (reloadOk, _, reloadErr) = RunSystemctl("daemon-reload");
        if (!reloadOk)
            return ServiceResult.Fail($"systemctl daemon-reload failed: {reloadErr}");

        // 6. AutoStart — enable + start the primary scheduling unit
        if (definition.AutoStart)
        {
            string unitName = isPeriodic
                ? GetTimerUnitName(definition.Name)
                : GetServiceUnitName(definition.Name);

            var (enableOk, _, enableErr) = RunSystemctl("enable", "--now", unitName);
            if (!enableOk)
                return ServiceResult.Fail($"systemctl enable --now failed: {enableErr}");
        }

        // 7. Write sidecar
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

        // 8. Create log directory
        ServiceLogManager.EnsureLogDirectory(definition.Name);

        return ServiceResult.Ok();
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────

    public ServiceResult Uninstall(string serviceName)
    {
        bool isPeriodic = IsPeriodicService(serviceName);
        string primaryUnit = isPeriodic
            ? GetTimerUnitName(serviceName)
            : GetServiceUnitName(serviceName);

        // 1. Stop
        RunSystemctl("stop", primaryUnit);

        // 2. Disable
        RunSystemctl("disable", primaryUnit);

        // 3. Remove unit files
        try
        {
            string servicePath = GetServiceUnitPath(serviceName);
            string timerPath = GetTimerUnitPath(serviceName);

            if (File.Exists(servicePath)) File.Delete(servicePath);
            if (File.Exists(timerPath)) File.Delete(timerPath);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail($"Failed to remove unit files: {ex.Message}");
        }

        // 4. daemon-reload
        RunSystemctl("daemon-reload");

        // 5. Delete sidecar
        SidecarManager.Delete(serviceName);

        // 6. Delete logs
        ServiceLogManager.RemoveLogDirectory(serviceName);

        return ServiceResult.Ok();
    }

    // ── Start / Stop / Restart / Enable / Disable ─────────────────────────────

    public ServiceResult Start(string serviceName)
    {
        var (ok, _, err) = RunSystemctl("start", GetPrimaryUnitName(serviceName));
        return ok ? ServiceResult.Ok() : ServiceResult.Fail($"systemctl start failed: {err}");
    }

    public ServiceResult Stop(string serviceName)
    {
        var (ok, _, err) = RunSystemctl("stop", GetPrimaryUnitName(serviceName));
        return ok ? ServiceResult.Ok() : ServiceResult.Fail($"systemctl stop failed: {err}");
    }

    public ServiceResult Restart(string serviceName)
    {
        var (ok, _, err) = RunSystemctl("restart", GetPrimaryUnitName(serviceName));
        return ok ? ServiceResult.Ok() : ServiceResult.Fail($"systemctl restart failed: {err}");
    }

    public ServiceResult Enable(string serviceName)
    {
        var (ok, _, err) = RunSystemctl("enable", GetPrimaryUnitName(serviceName));
        return ok ? ServiceResult.Ok() : ServiceResult.Fail($"systemctl enable failed: {err}");
    }

    public ServiceResult Disable(string serviceName)
    {
        var (ok, _, err) = RunSystemctl("disable", GetPrimaryUnitName(serviceName));
        return ok ? ServiceResult.Ok() : ServiceResult.Fail($"systemctl disable failed: {err}");
    }

    // ── GetStatus ─────────────────────────────────────────────────────────────

    public ServiceStatus GetStatus(string serviceName)
    {
        SidecarData? sidecar = SidecarManager.Read(serviceName);
        bool isPeriodic = sidecar?.Schedule is not null || IsPeriodicService(serviceName);

        string serviceUnit = GetServiceUnitName(serviceName);

        Dictionary<string, string> serviceProps = QueryUnitProperties(serviceUnit,
            "ActiveState", "SubState",
            "ExecMainStartTimestamp", "ExecMainExitTimestamp",
            "ExecMainStatus", "NRestarts");

        Dictionary<string, string> timerProps = isPeriodic
            ? QueryUnitProperties(GetTimerUnitName(serviceName),
                "ActiveState", "NextElapseUSecRealtime")
            : new Dictionary<string, string>();

        ServiceState state = DetermineState(serviceProps, sidecar, isPeriodic, timerProps);

        DateTime? lastRun = ParseSystemdTimestamp(
            serviceProps.GetValueOrDefault("ExecMainStartTimestamp"));
        DateTime? nextRun = isPeriodic
            ? ParseSystemdUsecTimestamp(timerProps.GetValueOrDefault("NextElapseUSecRealtime"))
            : null;

        int.TryParse(serviceProps.GetValueOrDefault("ExecMainStatus"), out int exitCode);
        int.TryParse(serviceProps.GetValueOrDefault("NRestarts"), out int restarts);

        DateTime? installedAt = null;
        if (sidecar?.InstalledAt is not null)
        {
            if (DateTime.TryParse(sidecar.InstalledAt, null,
                DateTimeStyles.RoundtripKind, out DateTime parsed))
            {
                installedAt = parsed;
            }
        }

        string platformInfo = isPeriodic
            ? $"systemd ({GetTimerUnitName(serviceName)})"
            : $"systemd ({serviceUnit})";

        return new ServiceStatus
        {
            Name = serviceName,
            State = state,
            Schedule = sidecar?.Schedule,
            ScriptPath = sidecar?.ScriptPath,
            LastRunTime = lastRun,
            NextRunTime = nextRun,
            LastExitCode = exitCode,
            RestartCount = restarts,
            InstalledAt = installedAt,
            Mode = sidecar?.Mode ?? (_systemMode ? "system" : "user"),
            PlatformInfo = platformInfo,
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

        // Also discover unmanaged stash-* units from systemctl
        var (ok, output, _) = RunSystemctl("list-units", "--all", "--no-legend", "--plain", "stash-*");
        if (ok && !string.IsNullOrWhiteSpace(output))
        {
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // Format: UNIT LOAD ACTIVE SUB DESCRIPTION
                string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 1) continue;

                string? name = ExtractServiceNameFromUnit(parts[0]);
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
        // Phase 1 stub — full log parsing in Phase 2
        return Array.Empty<ExecutionRecord>();
    }

    // ── Unit file generation ──────────────────────────────────────────────────

    private string BuildServiceUnit(
        ServiceDefinition def,
        string resolvedScript,
        string workingDir,
        bool isOneshot)
    {
        var sb = new StringBuilder();

        string description = EscapeSystemdValue(def.Description ?? $"Stash service: {def.Name}");

        sb.AppendLine("[Unit]");
        sb.AppendLine($"Description={description}");
        sb.AppendLine();
        sb.AppendLine("[Service]");
        sb.AppendLine($"Type={( isOneshot ? "oneshot" : "simple" )}");
        sb.AppendLine($"ExecStart=stash {EscapeSystemdPath(resolvedScript)}");
        sb.AppendLine($"WorkingDirectory={EscapeSystemdPath(workingDir)}");

        if (def.Environment is not null)
        {
            foreach (var (key, value) in def.Environment)
                sb.AppendLine($"Environment=\"{key}={EscapeSystemdEnvValue(value)}\"");
        }

        if (!isOneshot)
        {
            string restart = def.RestartOnFailure ? "on-failure" : "no";
            sb.AppendLine($"Restart={restart}");
            sb.AppendLine($"RestartSec={def.RestartDelaySec}");

            if (def.MaxRestarts > 0)
                sb.AppendLine($"StartLimitBurst={def.MaxRestarts}");
        }

        // Logging — user mode uses %h so systemd expands to the service user's home
        string logPath = _systemMode
            ? ServiceLogManager.GetCurrentLogPath(def.Name)
            : $"%h/.local/share/stash/logs/{def.Name}/current.log";

        sb.AppendLine($"StandardOutput=append:{logPath}");
        sb.AppendLine($"StandardError=append:{logPath}");

        if (def.PlatformExtras is not null)
        {
            foreach (var (key, value) in def.PlatformExtras)
                sb.AppendLine($"{key}={value}");
        }

        sb.AppendLine();
        sb.AppendLine("[Install]");
        sb.AppendLine($"WantedBy={( _systemMode ? "multi-user.target" : "default.target" )}");

        return sb.ToString();
    }

    private static string BuildTimerUnit(string serviceName, string? description, CronExpression cron)
    {
        string desc = EscapeSystemdValue(description is not null
            ? $"Timer for Stash service: {description}"
            : $"Timer for Stash service: {serviceName}");

        var sb = new StringBuilder();
        sb.AppendLine("[Unit]");
        sb.AppendLine($"Description={desc}");
        sb.AppendLine();
        sb.AppendLine("[Timer]");
        sb.AppendLine($"OnCalendar={cron.ToSystemdCalendar()}");
        sb.AppendLine("Persistent=true");
        sb.AppendLine();
        sb.AppendLine("[Install]");
        sb.AppendLine("WantedBy=timers.target");

        return sb.ToString();
    }

    // ── Escaping ──────────────────────────────────────────────────────────────

    private static string EscapeSystemdValue(string value) =>
        value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

    private static string EscapeSystemdPath(string path)
    {
        if (path.IndexOf(' ') >= 0)
            return $"\"{path.Replace("\"", "\\\"")}\"";
        return path;
    }

    private static string EscapeSystemdEnvValue(string value) =>
        value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ');

    // ── Atomic file write ─────────────────────────────────────────────────────

    private static ServiceResult WriteUnitFileAtomic(string path, string content)
    {
        string tmpPath = path + ".tmp";
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            File.WriteAllBytes(tmpPath, bytes);

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                // 0644 — world-readable, required by systemd
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
            return ServiceResult.Fail($"Failed to write unit file '{path}': {ex.Message}");
        }
    }

    // ── systemctl invocation ──────────────────────────────────────────────────

    private (bool success, string output, string error) RunSystemctl(params string[] args)
    {
        var psi = new ProcessStartInfo("systemctl")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (!_systemMode)
            psi.ArgumentList.Add("--user");

        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            using Process? process = Process.Start(psi);
            if (process is null)
                return (false, string.Empty, "Failed to start systemctl process.");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();

            bool completed = process.WaitForExit(10_000);
            if (!completed)
            {
                try { process.Kill(); } catch { /* best-effort */ }
                return (false, stdout, "systemctl timed out after 10 seconds.");
            }

            return (process.ExitCode == 0, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, $"Failed to run systemctl: {ex.Message}");
        }
    }

    // ── Status helpers ────────────────────────────────────────────────────────

    private Dictionary<string, string> QueryUnitProperties(string unitName, params string[] properties)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        string propList = string.Join(",", properties);
        var (ok, output, _) = RunSystemctl("show", $"--property={propList}", unitName);
        if (!ok || string.IsNullOrWhiteSpace(output))
            return result;

        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            result[line.Substring(0, eq)] = line.Substring(eq + 1);
        }

        return result;
    }

    private static ServiceState DetermineState(
        Dictionary<string, string> props,
        SidecarData? sidecar,
        bool isPeriodic,
        Dictionary<string, string> timerProps)
    {
        bool hasSidecar = sidecar is not null;

        if (!props.TryGetValue("ActiveState", out string? activeState)
            || string.IsNullOrEmpty(activeState))
        {
            return hasSidecar ? ServiceState.Orphaned : ServiceState.Unknown;
        }

        if (!hasSidecar)
            return ServiceState.Unmanaged;

        return activeState switch
        {
            "active" when isPeriodic => ServiceState.Active,
            "active" => props.GetValueOrDefault("SubState") == "running"
                ? ServiceState.Running
                : ServiceState.Active,
            "inactive" => ServiceState.Inactive,
            "failed" => ServiceState.Failed,
            _ => ServiceState.Unknown,
        };
    }

    private static DateTime? ParseSystemdTimestamp(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == "0" || value == "n/a")
            return null;

        if (DateTime.TryParse(value, null,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out DateTime dt))
        {
            return dt.ToUniversalTime();
        }

        return null;
    }

    private static DateTime? ParseSystemdUsecTimestamp(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == "0")
            return null;

        if (ulong.TryParse(value, out ulong usec) && usec > 0)
            return DateTimeOffset.FromUnixTimeMilliseconds((long)(usec / 1000)).UtcDateTime;

        return null;
    }

    // ── Misc helpers ──────────────────────────────────────────────────────────

    private bool IsPeriodicService(string serviceName)
    {
        SidecarData? sidecar = SidecarManager.Read(serviceName);
        if (sidecar is not null) return sidecar.Schedule is not null;

        return File.Exists(GetTimerUnitPath(serviceName));
    }

    private string GetPrimaryUnitName(string serviceName) =>
        IsPeriodicService(serviceName)
            ? GetTimerUnitName(serviceName)
            : GetServiceUnitName(serviceName);

    private static string? ExtractServiceNameFromUnit(string unit)
    {
        const string prefix = "stash-";
        if (!unit.StartsWith(prefix, StringComparison.Ordinal)) return null;

        string withoutPrefix = unit.Substring(prefix.Length);
        int dot = withoutPrefix.LastIndexOf('.');
        return dot > 0 ? withoutPrefix.Substring(0, dot) : null;
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
