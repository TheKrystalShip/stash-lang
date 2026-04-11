namespace Stash.Scheduler.Validation;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Stash.Scheduler;
using Stash.Scheduler.Models;

// ── Result types ─────────────────────────────────────────────────────────────

public readonly record struct ValidateScriptPathResult(bool Success, string? Error, string? ResolvedPath)
{
    public static ValidateScriptPathResult Ok(string resolvedPath) => new(true, null, resolvedPath);
    public static ValidateScriptPathResult Fail(string error)      => new(false, error, null);
}

public readonly record struct ValidateScheduleResult(bool Success, string? Error, CronExpression? ParsedSchedule)
{
    public static ValidateScheduleResult Ok(CronExpression? cron) => new(true, null, cron);
    public static ValidateScheduleResult Fail(string error)       => new(false, error, null);
}

public readonly record struct ValidatePlatformExtrasResult(
    bool Success,
    string? Error,
    IReadOnlyList<string> Warnings)
{
    public static ValidatePlatformExtrasResult Ok(IReadOnlyList<string> warnings) => new(true, null, warnings);
    public static ValidatePlatformExtrasResult Fail(string error)                 => new(false, error, Array.Empty<string>());
}

public sealed record ValidationResult(
    bool Success,
    string? Error,
    string ResolvedScriptPath,
    CronExpression? ParsedSchedule);

// ── Validator ─────────────────────────────────────────────────────────────────

public static class InputValidator
{
    // ^[a-zA-Z][a-zA-Z0-9_-]{0,63}$  →  1 letter prefix + up to 63 further chars = max 64 total
    private static readonly Regex _serviceNamePattern =
        new(@"^[a-zA-Z][a-zA-Z0-9_-]{0,63}$", RegexOptions.Compiled);

    private static readonly Regex _envKeyPattern =
        new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    // Keys that control privilege boundaries — blocked for user-mode installs
    private static readonly HashSet<string> _securitySensitiveKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "User", "Group", "CapabilityBoundingSet", "AmbientCapabilities",
            "NoNewPrivileges", "ProtectSystem", "ProtectHome", "PrivateTmp", "RootDirectory"
        };

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Validates a service name: must start with a letter, contain only
    /// <c>[a-zA-Z0-9_-]</c>, and be at most 64 characters long.
    /// </summary>
    public static ServiceResult ValidateServiceName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return ServiceResult.Fail("Service name cannot be null or empty.");

        if (name.Length > 64)
            return ServiceResult.Fail("Service name must not exceed 64 characters.");

        if (!_serviceNamePattern.IsMatch(name))
            return ServiceResult.Fail(
                "Service name must start with a letter and contain only letters, digits, hyphens, and underscores.");

        return ServiceResult.Ok();
    }

    /// <summary>
    /// Validates a script path: resolves to an absolute path, follows all symlinks,
    /// verifies the file exists and has a <c>.stash</c> extension, and (on Unix)
    /// confirms the file is owned by root or the current user.
    /// </summary>
    public static ValidateScriptPathResult ValidateScriptPath(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
            return ValidateScriptPathResult.Fail("Script path cannot be null or empty.");

        string resolved;
        try
        {
            resolved = Path.GetFullPath(scriptPath);
        }
        catch (Exception ex)
        {
            return ValidateScriptPathResult.Fail($"Script path is invalid: {ex.Message}");
        }

        if (!File.Exists(resolved))
            return ValidateScriptPathResult.Fail($"Script file not found: {resolved}");

        if (!resolved.EndsWith(".stash", StringComparison.OrdinalIgnoreCase))
            return ValidateScriptPathResult.Fail("Script file must have a .stash extension.");

        // Resolve the full symlink chain to the real file on disk
        try
        {
            string? realPath = new FileInfo(resolved)
                .ResolveLinkTarget(returnFinalTarget: true)?.FullName;

            if (realPath is not null && File.Exists(realPath))
                resolved = realPath;
        }
        catch (IOException)
        {
            // Circular or broken symlink chain — proceed with the canonical absolute path
        }

        // On Unix: script must be owned by root (UID 0) or the current user
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (!TryCheckFileOwnership(resolved, out string? ownerError))
                return ValidateScriptPathResult.Fail(ownerError!);
        }

        return ValidateScriptPathResult.Ok(resolved);
    }

    /// <summary>
    /// Validates an optional working directory path.
    /// Returns <see cref="ServiceResult.Ok()"/> when <paramref name="dir"/> is <see langword="null"/>
    /// (caller will default to the script's directory).
    /// </summary>
    public static ServiceResult ValidateWorkingDirectory(string? dir)
    {
        if (dir is null)
            return ServiceResult.Ok();

        string resolved;
        try
        {
            resolved = Path.GetFullPath(dir);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail($"Working directory path is invalid: {ex.Message}");
        }

        // Defense-in-depth: Path.GetFullPath() resolves '..' but we verify no segment equals '..'.
        string[] segments = resolved.Split(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(s => s == ".."))
            return ServiceResult.Fail("Working directory path contains '..' traversal segments.");

        if (!Directory.Exists(resolved))
            return ServiceResult.Fail($"Working directory not found: {resolved}");

        return ServiceResult.Ok();
    }

    /// <summary>
    /// Validates environment variable keys.
    /// Values are opaque and not validated.
    /// </summary>
    public static ServiceResult ValidateEnvironment(IReadOnlyDictionary<string, string>? env)
    {
        if (env is null)
            return ServiceResult.Ok();

        foreach (string key in env.Keys)
        {
            if (!_envKeyPattern.IsMatch(key))
                return ServiceResult.Fail(
                    $"Environment variable key '{key}' is invalid. " +
                    "Keys must match ^[a-zA-Z_][a-zA-Z0-9_]*$.");
        }

        return ServiceResult.Ok();
    }

    /// <summary>
    /// Validates an optional cron schedule string.
    /// Returns <see cref="ValidateScheduleResult.Ok"/> with a <see langword="null"/>
    /// <see cref="CronExpression"/> when <paramref name="schedule"/> is <see langword="null"/>
    /// (long-running mode).
    /// </summary>
    public static ValidateScheduleResult ValidateSchedule(string? schedule)
    {
        if (schedule is null)
            return ValidateScheduleResult.Ok(null);

        if (!CronExpression.TryParse(schedule, out CronExpression? cron))
            return ValidateScheduleResult.Fail($"Invalid cron expression: '{schedule}'.");

        return ValidateScheduleResult.Ok(cron);
    }

    /// <summary>
    /// Validates platform-specific extra keys/values destined for systemd unit files.
    /// </summary>
    /// <param name="extras">Optional platform-specific key-value pairs.</param>
    /// <param name="systemMode">
    ///   <see langword="true"/> when installing as a system service (requires elevated privileges);
    ///   <see langword="false"/> for user-mode installs.
    /// </param>
    public static ValidatePlatformExtrasResult ValidatePlatformExtras(
        IReadOnlyDictionary<string, string>? extras,
        bool systemMode)
    {
        if (extras is null)
            return ValidatePlatformExtrasResult.Ok(Array.Empty<string>());

        var warnings = new List<string>();

        foreach (var (key, value) in extras)
        {
            // User-mode installs may not escalate privileges via platform extras
            if (!systemMode && _securitySensitiveKeys.Contains(key))
                return ValidatePlatformExtrasResult.Fail(
                    $"Platform extra '{key}' is not permitted for user-mode installs.");

            // Newlines in values would break systemd INI format
            if (value.Contains('\n') || value.Contains('\r'))
                return ValidatePlatformExtrasResult.Fail(
                    $"Platform extra value for '{key}' must not contain newline characters.");
        }

        return ValidatePlatformExtrasResult.Ok(warnings);
    }

    /// <summary>
    /// Runs all validations against a <see cref="ServiceDefinition"/> and returns
    /// an aggregate result.
    /// </summary>
    public static ValidationResult ValidateAll(ServiceDefinition def)
    {
        ServiceResult nameResult = ValidateServiceName(def.Name);
        if (!nameResult.Success)
            return new ValidationResult(false, nameResult.Error, string.Empty, null);

        ValidateScriptPathResult pathResult = ValidateScriptPath(def.ScriptPath);
        if (!pathResult.Success)
            return new ValidationResult(false, pathResult.Error, string.Empty, null);

        ServiceResult wdResult = ValidateWorkingDirectory(def.WorkingDirectory);
        if (!wdResult.Success)
            return new ValidationResult(false, wdResult.Error, string.Empty, null);

        ServiceResult envResult = ValidateEnvironment(def.Environment);
        if (!envResult.Success)
            return new ValidationResult(false, envResult.Error, string.Empty, null);

        ValidateScheduleResult schedResult = ValidateSchedule(def.Schedule);
        if (!schedResult.Success)
            return new ValidationResult(false, schedResult.Error, string.Empty, null);

        ValidatePlatformExtrasResult extrasResult =
            ValidatePlatformExtras(def.PlatformExtras, def.SystemMode);
        if (!extrasResult.Success)
            return new ValidationResult(false, extrasResult.Error, string.Empty, null);

        return new ValidationResult(true, null, pathResult.ResolvedPath!, schedResult.ParsedSchedule);
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <paramref name="resolvedPath"/> is owned by root (UID 0) or
    /// the current user. Uses the <c>stat</c> command-line tool for
    /// cross-architecture compatibility on Linux.
    /// </summary>
    private static bool TryCheckFileOwnership(string resolvedPath, out string? error)
    {
        try
        {
            uint fileUid = GetFileUid(resolvedPath);
            if (fileUid == uint.MaxValue)
            {
                // Cannot determine ownership — skip the check (fail-open, not fail-closed,
                // to avoid breaking legitimate installs on unusual distributions)
                error = null;
                return true;
            }

            uint currentUid = GetCurrentUid();
            if (fileUid == 0 || fileUid == currentUid)
            {
                error = null;
                return true;
            }

            error = $"Script file is not owned by the current user or root (file owner UID: {fileUid}).";
            return false;
        }
        catch
        {
            error = null;
            return true;
        }
    }

    /// <summary>
    /// Returns the UID of the file owner, or <see cref="uint.MaxValue"/> on failure.
    /// Uses <c>/usr/bin/stat -c %u</c> for cross-architecture (x86-64 / arm64) compatibility.
    /// </summary>
    private static uint GetFileUid(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/stat",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("%u");
            psi.ArgumentList.Add(path);

            using Process? process = Process.Start(psi);
            if (process is null) return uint.MaxValue;

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return uint.TryParse(output, out uint uid) ? uid : uint.MaxValue;
        }
        catch
        {
            return uint.MaxValue;
        }
    }

    /// <summary>
    /// Returns the UID of the current process owner, or <see cref="uint.MaxValue"/> on failure.
    /// </summary>
    private static uint GetCurrentUid()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/id",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-u");

            using Process? process = Process.Start(psi);
            if (process is null) return uint.MaxValue;

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return uint.TryParse(output, out uint uid) ? uid : uint.MaxValue;
        }
        catch
        {
            return uint.MaxValue;
        }
    }
}
