using System.Runtime.InteropServices;

namespace Stash.Interpreting;

/// <summary>
/// P/Invoke wrappers for POSIX syscalls (kill, execvp) on Unix platforms.
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

    /// <summary>
    /// Replaces the current process with the specified program.
    /// On success, this call does not return.
    /// On failure, returns -1 with errno set.
    /// </summary>
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int execvp(string file, string?[] argv);

    /// <summary>
    /// Replaces the current process image with the specified command.
    /// The argv array must be null-terminated (last element must be null).
    /// On success this never returns. On failure returns -1.
    /// </summary>
    internal static int Exec(string program, string[] args)
    {
        // Build null-terminated argv array: [program, arg1, arg2, ..., null]
        var argv = new string?[args.Length + 2];
        argv[0] = program;
        for (int i = 0; i < args.Length; i++)
        {
            argv[i + 1] = args[i];
        }
        argv[argv.Length - 1] = null;

        return execvp(program, argv);
    }
}
