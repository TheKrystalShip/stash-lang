namespace Stash.Runtime;

using System.Collections.Generic;
using System.Diagnostics;
using Stash.Runtime.Types;

/// <summary>
/// Process management: tracking spawned processes, wait caching, exit callbacks,
/// and the interpreter's directory navigation stack.
/// Used exclusively by ProcessBuiltIns.
/// </summary>
public interface IProcessContext
{
    List<(StashInstance Handle, Process Process)> TrackedProcesses { get; }
    Dictionary<StashInstance, StashInstance> ProcessWaitCache { get; }
    Dictionary<StashInstance, List<IStashCallable>> ProcessExitCallbacks { get; }
    void CleanupTrackedProcesses();

    /// <summary>
    /// Directory navigation stack. The last entry is the current working directory.
    /// Initialized with the process's starting cwd. Capped at 256 entries.
    /// </summary>
    List<string> DirStack { get; }
}
