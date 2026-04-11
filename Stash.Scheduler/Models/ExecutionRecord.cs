namespace Stash.Scheduler.Models;

using System;

public sealed class ExecutionRecord
{
    public required DateTime Timestamp { get; init; }
    public required int ExitCode { get; init; }
    public TimeSpan? Duration { get; init; }
    public string? Output { get; init; }
}
