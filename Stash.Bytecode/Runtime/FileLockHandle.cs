namespace Stash.Bytecode;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

/// <summary>
/// Thrown when a lock cannot be acquired within the specified timeout.
/// Caught by ExecuteLockBegin and converted to a Stash LockError.
/// </summary>
internal sealed class LockAcquisitionException : Exception
{
    public string LockPath { get; }
    public LockAcquisitionException(string path, string message) : base(message)
    {
        LockPath = path;
    }
}

/// <summary>
/// Represents an acquired OS-level advisory file lock.
/// On Unix, FileShare.None triggers flock(2) LOCK_EX semantics.
/// On Windows, FileShare.None triggers LockFileEx.
/// </summary>
internal sealed class FileLockHandle : IDisposable
{
    private FileStream? _stream;

    /// <summary>The normalized (absolute) path of the lock file.</summary>
    public string NormalizedPath { get; }

    private FileLockHandle(string normalizedPath, FileStream stream)
    {
        NormalizedPath = normalizedPath;
        _stream = stream;
    }

    /// <summary>
    /// Acquire an exclusive lock on the specified file path.
    /// </summary>
    /// <param name="path">Normalized (absolute) path to lock file.</param>
    /// <param name="waitMs">Maximum wait in milliseconds. null = wait forever. 0 = non-blocking.</param>
    /// <param name="staleMs">Age threshold (ms) for stale lock detection. null = no stale detection.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation during wait.</param>
    public static FileLockHandle Acquire(string path, long? waitMs, long? staleMs, CancellationToken ct)
    {
        const int PollIntervalMs = 50;
        long deadline = waitMs.HasValue ? Environment.TickCount64 + waitMs.Value : long.MaxValue;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                WritePid(stream);
                return new FileLockHandle(path, stream);
            }
            catch (IOException)
            {
                // Lock is held by another process.
                // Try stale detection if configured.
                if (staleMs.HasValue && TryStealStaleLock(path, staleMs.Value, out FileLockHandle? stolenHandle))
                    return stolenHandle!;

                if (Environment.TickCount64 >= deadline)
                {
                    string waitDesc = waitMs == 0 ? "non-blocking" : $"{waitMs}ms";
                    throw new LockAcquisitionException(path,
                        $"Failed to acquire lock on '{path}' ({waitDesc}): lock is held by another process.");
                }

                Thread.Sleep(PollIntervalMs);
            }
        }
    }

    private static void WritePid(FileStream stream)
    {
        stream.Position = 0;
        stream.SetLength(0);
        using var writer = new StreamWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.WriteLine(Environment.ProcessId);
        writer.Flush();
        stream.Flush();
    }

    private static bool TryStealStaleLock(string path, long staleMs, out FileLockHandle? handle)
    {
        handle = null;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return false;

            // Check file age
            double ageMs = (DateTimeOffset.UtcNow - fi.LastWriteTimeUtc).TotalMilliseconds;
            if (ageMs < staleMs) return false;

            // Try to read the PID from the lock file (using shared read access)
            int storedPid = 0;
            try
            {
                using var readStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(readStream);
                string? line = reader.ReadLine();
                if (line != null) int.TryParse(line.Trim(), out storedPid);
            }
            catch { /* ignore read errors — treat as unreadable PID */ }

            // If PID is still alive, don't steal
            if (storedPid > 0 && IsProcessAlive(storedPid, fi.LastWriteTimeUtc))
                return false;

            // PID is dead or unreadable — try to steal the lock
            try
            {
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                WritePid(stream);
                handle = new FileLockHandle(path, stream);
                return true;
            }
            catch (IOException)
            {
                return false; // Another process grabbed it first
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcessAlive(int pid, DateTimeOffset lockFileWriteTime)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            // On Windows, PIDs can be reused. Verify start time to detect recycled PIDs.
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    if (proc.StartTime.ToUniversalTime() > lockFileWriteTime.UtcDateTime)
                        return false; // Same PID, different process — treat as stale
                }
                catch { /* StartTime may be inaccessible for system processes */ }
            }
            return true;
        }
        catch (ArgumentException)
        {
            return false; // Process not found
        }
    }

    /// <summary>Release the OS file lock by closing the stream.</summary>
    public void Release()
    {
        if (_stream == null) return;
        var s = _stream;
        _stream = null;
        try { s.Dispose(); }
        catch { /* best effort */ }
    }

    public void Dispose() => Release();
}
