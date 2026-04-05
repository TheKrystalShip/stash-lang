using System.Runtime.InteropServices;

namespace Stash.Common;

/// <summary>
/// P/Invoke wrappers for POSIX syscalls (kill, execvp) on Unix platforms.
/// </summary>
/// <remarks>
/// <para>
/// These wrappers are used by the Stash interpreter on Linux and macOS to deliver
/// signals to child processes (e.g. SIGINT to a foreground shell command) and to replace
/// the current process image via <c>exec</c> when the Stash <c>exec</c> built-in is called.
/// </para>
/// <para>
/// All members are guarded at the call site — callers must check
/// <see cref="System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform"/> before
/// invoking any method in this class, as the underlying native library is not present on Windows.
/// </para>
/// </remarks>
public static partial class UnixSignal
{
    /// <summary>
    /// Sends a signal to a process by PID.
    /// Returns 0 on success, -1 on failure.
    /// </summary>
    /// <param name="pid">The process ID of the target process.</param>
    /// <param name="sig">The signal number to send (e.g. 2 for SIGINT, 15 for SIGTERM).</param>
    /// <returns>0 on success; -1 if an error occurred, with <c>errno</c> set by the OS.</returns>
    [LibraryImport("libc", SetLastError = true)]
    public static partial int kill(int pid, int sig);

    /// <summary>
    /// Sends a signal to a process. Returns 0 on success, -1 on failure.
    /// Only call this on Unix platforms.
    /// </summary>
    /// <param name="pid">The process ID of the target process.</param>
    /// <param name="sig">The signal number to send.</param>
    /// <returns>0 on success; -1 on failure.</returns>
    public static int Kill(int pid, int sig)
    {
        return kill(pid, sig);
    }

    /// <summary>
    /// Replaces the current process with the specified program.
    /// On success, this call does not return.
    /// On failure, returns -1 with errno set.
    /// </summary>
    /// <param name="file">The program name or path to execute.</param>
    /// <param name="argv">A null-terminated argument vector, where <c>argv[0]</c> is conventionally the program name.</param>
    /// <returns>This function does not return on success. On failure it returns -1 with <c>errno</c> set.</returns>
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int execvp(string file, string?[] argv);

    /// <summary>
    /// Replaces the current process image with the specified command.
    /// The argv array must be null-terminated (last element must be null).
    /// On success this never returns. On failure returns -1.
    /// </summary>
    /// <param name="program">The program name or path to execute, also used as <c>argv[0]</c>.</param>
    /// <param name="args">Additional arguments to pass to the program. Must not include a null terminator — this method appends one.</param>
    /// <returns>This method does not return on success. On failure it returns -1.</returns>
    public static int Exec(string program, string[] args)
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
