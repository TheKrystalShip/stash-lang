using System.Runtime.InteropServices;

namespace Stash.Interpreting;

/// <summary>
/// P/Invoke wrapper for the POSIX kill() syscall on Unix platforms.
/// </summary>
internal static partial class UnixSignal
{
    /// <summary>
    /// Sends a signal to a process by PID.
    /// Returns 0 on success, -1 on failure.
    /// </summary>
    [LibraryImport("libc", SetLastError = true)]
    internal static partial int kill(int pid, int sig);

    /// <summary>
    /// Sends a signal to a process. Returns 0 on success, -1 on failure.
    /// Only call this on Unix platforms.
    /// </summary>
    internal static int Kill(int pid, int sig)
    {
        return kill(pid, sig);
    }
}
