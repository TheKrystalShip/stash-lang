namespace Stash.Runtime;

using System.Collections.Generic;
using System.IO;
using Stash.Runtime.Types;

/// <summary>
/// File watcher management: tracking active FileSystemWatchers for cleanup on exit.
/// Used exclusively by FsBuiltIns (fs.watch / fs.unwatch).
/// </summary>
public interface IFileWatchContext
{
    List<(StashInstance Handle, FileSystemWatcher Watcher)> TrackedWatchers { get; }
    void CleanupTrackedWatchers();
}
