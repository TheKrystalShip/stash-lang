namespace Stash.Interpreting;

using System.Collections.Concurrent;
using System.Threading;
using Stash.Interpreting.Types;

/// <summary>
/// Per-interpreter registry for parallel task state. Shared between a parent
/// interpreter and its forked children so that tasks spawned by any of them
/// can be awaited by any other.
/// </summary>
internal sealed class TaskRegistry
{
    private long _nextId;
    private int _nextDebugThreadId = 1;

    internal StashEnum TaskStatusEnum { get; }
    internal ConcurrentDictionary<long, TaskState> Tasks { get; } = new();

    internal sealed class TaskState
    {
        public System.Threading.Tasks.Task DotNetTask { get; set; } = null!;
        public object? Result { get; set; }
        public string? Error { get; set; }
        public StashEnumValue Status { get; set; } = null!;
        public CancellationTokenSource Cts { get; set; } = null!;
    }

    internal TaskRegistry()
    {
        TaskStatusEnum = new StashEnum("Status", new System.Collections.Generic.List<string>
            { "Running", "Completed", "Failed", "Cancelled" });
    }

    internal long NextId() => Interlocked.Increment(ref _nextId);
    internal int NextDebugThreadId() => Interlocked.Increment(ref _nextDebugThreadId);
}
