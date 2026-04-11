namespace Stash.Scheduler.Models;

using System.Collections.Generic;

public sealed class ServiceDefinition
{
    // Portable fields
    public required string Name { get; init; }
    public required string ScriptPath { get; init; }
    public string? Description { get; init; }
    public string? Schedule { get; init; }  // null = long-running, cron = periodic
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
    public string? User { get; init; }
    public bool AutoStart { get; init; } = true;
    public bool RestartOnFailure { get; init; } = true;
    public int MaxRestarts { get; init; } = 0;       // 0 = unlimited
    public int RestartDelaySec { get; init; } = 5;
    public bool SystemMode { get; init; } = false;    // user mode by default

    // Platform escape hatch
    public IReadOnlyDictionary<string, string>? PlatformExtras { get; init; }
}
