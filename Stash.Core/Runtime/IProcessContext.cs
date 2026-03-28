namespace Stash.Runtime;

using System.Collections.Generic;
using System.Diagnostics;
using Stash.Runtime.Types;

/// <summary>
/// Process management: tracking spawned processes, wait caching, exit callbacks.
/// Used exclusively by ProcessBuiltIns.
/// </summary>
public interface IProcessContext
{
    List<(StashInstance Handle, Process Process)> TrackedProcesses { get; }
    Dictionary<StashInstance, StashInstance> ProcessWaitCache { get; }
    Dictionary<StashInstance, List<IStashCallable>> ProcessExitCallbacks { get; }
    void CleanupTrackedProcesses();
}
