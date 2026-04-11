namespace Stash.Scheduler.Models;

using System;

// Summary info for list display
public sealed class ServiceInfo
{
    public required string Name { get; init; }
    public required ServiceState State { get; init; }
    public string? Schedule { get; init; }
    public DateTime? LastRunTime { get; init; }
    public DateTime? NextRunTime { get; init; }
}
