namespace Stash.Scheduler.Models;

using System;

public sealed class ServiceStatus
{
    public required string Name { get; init; }
    public required ServiceState State { get; init; }
    public string? Schedule { get; init; }
    public string? ScriptPath { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? User { get; init; }
    public DateTime? LastRunTime { get; init; }
    public DateTime? NextRunTime { get; init; }
    public int? LastExitCode { get; init; }
    public int RestartCount { get; init; }
    public DateTime? InstalledAt { get; init; }
    public string? Mode { get; init; }        // "user" or "system"
    public string? PlatformInfo { get; init; } // e.g., "systemd (stash-health-check.timer)"
}

public enum ServiceState
{
    Unknown,
    Active,      // running or scheduled
    Inactive,    // installed but not enabled
    Running,     // currently executing (long-running)
    Stopped,     // explicitly stopped
    Failed,      // last run failed
    Orphaned,    // sidecar exists but OS service missing
    Unmanaged    // OS service exists but no sidecar
}
