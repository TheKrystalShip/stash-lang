namespace Stash.Interpreting;

using Stash.Runtime.Types;

/// <summary>
/// Per-interpreter registry for parallel task state. Holds the task.Status enum
/// used by <c>task.status()</c>.
/// </summary>
internal sealed class TaskRegistry
{
    internal StashEnum TaskStatusEnum { get; }

    internal TaskRegistry()
    {
        TaskStatusEnum = new StashEnum("Status", new System.Collections.Generic.List<string>
            { "Running", "Completed", "Failed", "Cancelled" });
    }
}
