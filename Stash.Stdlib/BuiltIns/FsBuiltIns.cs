namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using Stash.Runtime.Errors;

/// <summary>
/// Registers the <c>fs</c> namespace built-in functions for filesystem operations.
/// </summary>
/// <remarks>
/// <para>
/// Provides 34 functions for reading, writing, and inspecting the filesystem:
/// <c>fs.readFile</c>, <c>fs.writeFile</c>, <c>fs.appendFile</c>, <c>fs.readLines</c>,
/// <c>fs.createFile</c>, <c>fs.exists</c>, <c>fs.dirExists</c>, <c>fs.pathExists</c>,
/// <c>fs.createDir</c>, <c>fs.listDir</c>, <c>fs.delete</c>, <c>fs.copy</c>, <c>fs.move</c>,
/// <c>fs.size</c>, <c>fs.stat</c>, <c>fs.glob</c>, <c>fs.walk</c>, <c>fs.isFile</c>,
/// <c>fs.isDir</c>, <c>fs.isSymlink</c>, <c>fs.symlink</c>, <c>fs.modifiedAt</c>,
/// <c>fs.readable</c>, <c>fs.writable</c>, <c>fs.executable</c>, <c>fs.tempFile</c>,
/// <c>fs.tempDir</c>, <c>fs.getPermissions</c>, <c>fs.setPermissions</c>,
/// <c>fs.setReadOnly</c>, <c>fs.setExecutable</c>, <c>fs.chown</c>, <c>fs.watch</c>, and <c>fs.unwatch</c>.
/// </para>
/// <para>
/// This namespace is only registered when the <see cref="StashCapabilities.FileSystem"/>
/// capability is enabled. All path arguments support <c>~</c> home-directory expansion.
/// </para>
/// </remarks>
[StashNamespace(Capability = StashCapabilities.FileSystem)]
public static partial class FsBuiltIns
{
    // ── File watcher state ────────────────────────────────────────────────
    private static readonly ConcurrentDictionary<StashInstance, WatcherState> _activeWatchers = new(ReferenceEqualityComparer.Instance);

    private sealed class WatcherState : IDisposable
    {
        public FileSystemWatcher OsWatcher { get; }
        public IStashCallable Callback { get; }
        public IInterpreterContext Context { get; }
        public int DebounceMs { get; }
        private readonly ConcurrentDictionary<string, System.Threading.Timer> _debounceTimers = new();

        public WatcherState(FileSystemWatcher watcher, IStashCallable callback, IInterpreterContext context, int debounceMs)
        {
            OsWatcher = watcher;
            Callback = callback;
            Context = context;
            DebounceMs = debounceMs;
        }

        public void FireCallback(StashEnumValue eventType, string path, string? oldPath)
        {
            if (DebounceMs <= 0 || eventType.MemberName == "Renamed")
            {
                // No debounce or rename events — fire immediately
                InvokeCallback(eventType, path, oldPath);
                return;
            }

            string key = $"{path}:{eventType.MemberName}";
            if (_debounceTimers.TryGetValue(key, out System.Threading.Timer? existing))
            {
                existing.Change(DebounceMs, Timeout.Infinite);
            }
            else
            {
                var timer = new System.Threading.Timer(_ =>
                {
                    _debounceTimers.TryRemove(key, out System.Threading.Timer? _);
                    InvokeCallback(eventType, path, oldPath);
                }, null, DebounceMs, Timeout.Infinite);
                if (!_debounceTimers.TryAdd(key, timer))
                {
                    timer.Dispose();
                    if (_debounceTimers.TryGetValue(key, out System.Threading.Timer? other))
                    {
                        other.Change(DebounceMs, Timeout.Infinite);
                    }
                }
            }
        }

        private void InvokeCallback(StashEnumValue eventType, string path, string? oldPath)
        {
            try
            {
                var eventFields = new Dictionary<string, StashValue>
                {
                    ["type"] = StashValue.FromObj(eventType),
                    ["path"] = StashValue.FromObj(path),
                    ["oldPath"] = oldPath is not null ? StashValue.FromObj(oldPath) : StashValue.Null
                };
                var watchEvent = new StashInstance("WatchEvent", eventFields);
                Context.InvokeCallbackDirect(Callback, new StashValue[] { StashValue.FromObj(watchEvent) });
            }
            catch
            {
                // Errors in watcher callbacks are non-fatal (same as sys.onSignal)
            }
        }

        public void Dispose()
        {
            try { OsWatcher.EnableRaisingEvents = false; } catch { }
            foreach (var timer in _debounceTimers.Values)
            {
                try { timer.Dispose(); } catch { }
            }
            _debounceTimers.Clear();
            try { OsWatcher.Dispose(); } catch { }
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "chown", SetLastError = true)]
    private static extern int LibcChown(string pathname, uint owner, uint group);

    private static void ChownFile(string path, int uid, int gid)
    {
        if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
            throw new IOError($"fs.chown: path not found: '{path}'.");

        uint owner = uid == -1 ? unchecked((uint)-1) : (uint)uid;
        uint group = gid == -1 ? unchecked((uint)-1) : (uint)gid;
        int result = LibcChown(path, owner, group);
        if (result != 0)
        {
            int errno = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            string msg = errno switch
            {
                1  => $"fs.chown: permission denied — must be superuser or file owner.",
                2  => $"fs.chown: path not found: '{path}'.",
                13 => $"fs.chown: permission denied — must be superuser or file owner.",
                22 => $"fs.chown: invalid uid {uid} or gid {gid}.",
                _  => $"fs.chown: failed with errno {errno}."
            };
            throw new IOError(msg);
        }
    }

    // ── Stash enum declarations ───────────────────────────────────────────────

    /// <summary>File system event types for fs.watch callbacks.</summary>
    [StashEnum]
    public enum WatchEventType { Created, Modified, Deleted, Renamed }

    // ── Stash struct declarations ─────────────────────────────────────────────

    /// <summary>Read/write/execute permission bits for a single principal.</summary>
    [StashStruct]
    public sealed record FilePermission(bool Read, bool Write, bool Execute);

    /// <summary>File permission bits for owner, group, and others.</summary>
    [StashStruct]
    public sealed record FilePermissions(FilePermission Owner, FilePermission Group, FilePermission Others);

    /// <summary>A file system change event emitted by fs.watch.</summary>
    [StashStruct]
    public sealed record WatchEvent(WatchEventType Type, string Path, string OldPath);

    /// <summary>Options for fs.watch.</summary>
    [StashStruct]
    public sealed record WatchOptions(bool Recursive, string Filter, long BufferSize, long Debounce);

    /// <summary>An opaque handle to a running file system watcher.</summary>
    [StashStruct]
    public sealed record Watcher();

    // ── Functions ─────────────────────────────────────────────────────────────

    /// <summary>Reads the entire contents of a file as a string. Throws on I/O error.</summary>
    /// <param name="path">The file path to read.</param>
    /// <param name="encoding">Optional encoding name (default "utf-8"). Supported: "utf-8", "ascii", "latin1", "utf-16", "utf-32".</param>
    /// <exception cref="IOError">if the file does not exist or cannot be read</exception>
    /// <exception cref="ValueError">if the encoding name is not recognised</exception>
    [StashFn]
    private static string ReadFile(IInterpreterContext ctx, string path, string? encoding = null)
    {
        path = ctx.ExpandTilde(path);
        var enc = encoding switch
        {
            null      => Encoding.UTF8,
            "utf-8"   => Encoding.UTF8,
            "ascii"   => Encoding.ASCII,
            "latin1"  => Encoding.Latin1,
            "utf-16"  => Encoding.Unicode,
            "utf-32"  => Encoding.UTF32,
            var e     => throw new ValueError($"fs.readFile: unsupported encoding '{e}'. Valid values: \"utf-8\", \"ascii\", \"latin1\", \"utf-16\", \"utf-32\"."),
        };

        try { return System.IO.File.ReadAllText(path, enc); }
        catch (System.IO.IOException e) { throw new IOError($"Cannot read file '{path}': {e.Message}"); }
    }

    /// <summary>Writes a string to a file, creating or overwriting it. Returns null.</summary>
    /// <param name="path">The file path to write.</param>
    /// <param name="content">The string content to write.</param>
    /// <param name="encoding">Optional encoding name (default "utf-8"). Supported: "utf-8", "ascii", "latin1", "utf-16", "utf-32".</param>
    /// <exception cref="IOError">if the file cannot be created or written</exception>
    /// <exception cref="ValueError">if the encoding name is not recognised</exception>
    [StashFn]
    private static void WriteFile(IInterpreterContext ctx, string path, string content, string? encoding = null)
    {
        path = ctx.ExpandTilde(path);
        var enc = encoding switch
        {
            null      => Encoding.UTF8,
            "utf-8"   => Encoding.UTF8,
            "ascii"   => Encoding.ASCII,
            "latin1"  => Encoding.Latin1,
            "utf-16"  => Encoding.Unicode,
            "utf-32"  => Encoding.UTF32,
            var e     => throw new ValueError($"fs.writeFile: unsupported encoding '{e}'. Valid values: \"utf-8\", \"ascii\", \"latin1\", \"utf-16\", \"utf-32\"."),
        };

        try { System.IO.File.WriteAllText(path, content, enc); }
        catch (System.IO.IOException e) { throw new IOError($"Cannot write file '{path}': {e.Message}"); }
    }

    /// <summary>Returns true if a file exists at the given path.</summary>
    /// <param name="path">The file path to check.</param>
    /// <returns>True if the file exists, false otherwise.</returns>
    [StashFn]
    private static bool Exists(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        return System.IO.File.Exists(path);
    }

    /// <summary>Returns true if a directory exists at the given path.</summary>
    /// <param name="path">The directory path to check.</param>
    /// <returns>True if the directory exists, false otherwise.</returns>
    [StashFn]
    private static bool DirExists(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        return System.IO.Directory.Exists(path);
    }

    /// <summary>Returns true if either a file or directory exists at the given path.</summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if a file or directory exists at the path, false otherwise.</returns>
    [StashFn]
    private static bool PathExists(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        return System.IO.File.Exists(path) || System.IO.Directory.Exists(path);
    }

    /// <summary>Creates a directory and all necessary parent directories. No-ops if the directory already exists.</summary>
    /// <param name="path">The directory path to create.</param>
    /// <exception cref="IOError">if the directory cannot be created</exception>
    /// <returns>null</returns>
    [StashFn]
    private static void CreateDir(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        try { System.IO.Directory.CreateDirectory(path); }
        catch (System.IO.IOException e) { throw new IOError($"Cannot create directory '{path}': {e.Message}"); }
    }

    /// <summary>Deletes a file or recursively deletes a directory at the given path. Throws if the path does not exist.</summary>
    /// <param name="path">The file or directory path to delete.</param>
    /// <exception cref="IOError">if the path does not exist or cannot be deleted</exception>
    /// <returns>null</returns>
    [StashFn]
    private static void Delete(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
            else if (System.IO.Directory.Exists(path))
            {
                System.IO.Directory.Delete(path, true);
            }
            else
            {
                throw new IOError($"Path does not exist: '{path}'.");
            }
        }
        catch (System.IO.IOException e) { throw new IOError($"Cannot delete '{path}': {e.Message}"); }
    }

    /// <summary>Copies a file from src to dst. Returns null.</summary>
    /// <param name="src">The source file path.</param>
    /// <param name="dst">The destination file path.</param>
    /// <param name="overwrite">Optional bool (default true). When false and dst already exists, throws an error.</param>
    /// <exception cref="IOError">if the destination already exists and overwrite is false, or if the copy fails</exception>
    [StashFn]
    private static void Copy(IInterpreterContext ctx, string src, string dst, bool overwrite = true)
    {
        src = ctx.ExpandTilde(src);
        dst = ctx.ExpandTilde(dst);

        if (!overwrite && System.IO.File.Exists(dst))
            throw new IOError($"fs.copy: destination '{dst}' already exists. Pass overwrite: true to replace it.");

        try { System.IO.File.Copy(src, dst, overwrite); }
        catch (System.IO.IOException e) { throw new IOError($"Cannot copy '{src}' to '{dst}': {e.Message}"); }
    }

    /// <summary>Moves or renames a file from src to dst. Returns null.</summary>
    /// <param name="src">The source file path.</param>
    /// <param name="dst">The destination file path.</param>
    /// <param name="overwrite">Optional bool (default true). When false and dst already exists, throws an error.</param>
    /// <exception cref="IOError">if the destination already exists and overwrite is false, or if the move fails</exception>
    [StashFn]
    private static void Move(IInterpreterContext ctx, string src, string dst, bool overwrite = true)
    {
        src = ctx.ExpandTilde(src);
        dst = ctx.ExpandTilde(dst);

        if (!overwrite && System.IO.File.Exists(dst))
            throw new IOError($"fs.move: destination '{dst}' already exists. Pass overwrite: true to replace it.");

        try { System.IO.File.Move(src, dst, overwrite); }
        catch (System.IO.IOException e) { throw new IOError($"Cannot move '{src}' to '{dst}': {e.Message}"); }
    }

    /// <summary>Returns the size of a file in bytes.</summary>
    /// <param name="path">The file path.</param>
    /// <exception cref="IOError">if the file does not exist or cannot be accessed</exception>
    /// <returns>The file size in bytes as an integer.</returns>
    [StashFn]
    private static long Size(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        try { return new System.IO.FileInfo(path).Length; }
        catch (System.IO.IOException e) { throw new IOError($"Cannot get size of '{path}': {e.Message}"); }
    }

    /// <summary>Returns an array of file and directory paths directly inside the given directory.</summary>
    /// <param name="path">The directory path to list.</param>
    /// <param name="filter">Optional glob pattern (e.g. "*.txt") to filter results. When omitted, all entries are returned.</param>
    /// <exception cref="IOError">if the directory does not exist or cannot be listed</exception>
    [StashFn]
    private static List<StashValue> ListDir(IInterpreterContext ctx, string path, string? filter = null)
    {
        path = ctx.ExpandTilde(path);
        string actualFilter = filter ?? "*";

        try
        {
            var entries = System.IO.Directory.GetFileSystemEntries(path, actualFilter);
            var result = new List<StashValue>(entries.Length);
            foreach (var entry in entries)
            {
                result.Add(StashValue.FromObj(entry));
            }

            return result;
        }
        catch (System.IO.IOException e) { throw new IOError($"Cannot list directory '{path}': {e.Message}"); }
    }

    /// <summary>Appends content to a file, creating it if it doesn't exist. Returns null.</summary>
    /// <param name="path">The file path to append to.</param>
    /// <param name="content">The string content to append.</param>
    /// <exception cref="IOError">if the file cannot be opened or written</exception>
    /// <returns>null</returns>
    [StashFn(ReturnType = "null")]
    private static void AppendFile(IInterpreterContext ctx, string path, string content)
    {
        path = ctx.ExpandTilde(path);

        try { System.IO.File.AppendAllText(path, content); }
        catch (System.IO.IOException e) { throw new IOError($"Cannot append to file '{path}': {e.Message}"); }
    }

    /// <summary>Reads a file and returns an array of lines.</summary>
    /// <param name="path">The file path to read.</param>
    /// <exception cref="IOError">if the file does not exist or cannot be read</exception>
    /// <returns>An array of strings, one per line.</returns>
    [StashFn]
    private static List<StashValue> ReadLines(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        try
        {
            var lines = System.IO.File.ReadAllLines(path);
            var result = new List<StashValue>(lines.Length);
            foreach (var line in lines)
                result.Add(StashValue.FromObj(line));
            return result;
        }
        catch (System.IO.IOException e) { throw new IOError($"Cannot read file '{path}': {e.Message}"); }
    }

    /// <summary>Returns an array of file paths matching the glob pattern.</summary>
    /// <param name="pattern">The glob pattern (e.g. "src/**/*.cs").</param>
    /// <exception cref="IOError">if the base directory does not exist or cannot be read</exception>
    /// <returns>An array of matching file path strings.</returns>
    [StashFn]
    private static List<StashValue> Glob(IInterpreterContext ctx, string pattern)
    {
        pattern = ctx.ExpandTilde(pattern);
        try
        {
            string dir = System.IO.Path.GetDirectoryName(pattern) ?? ".";
            string filePattern = System.IO.Path.GetFileName(pattern);
            if (string.IsNullOrEmpty(dir))
            {
                dir = ".";
            }

            if (string.IsNullOrEmpty(filePattern))
            {
                filePattern = "*";
            }

            var files = System.IO.Directory.GetFiles(dir, filePattern, System.IO.SearchOption.AllDirectories);
            var result = new List<StashValue>(files.Length);
            foreach (var f in files)
                result.Add(StashValue.FromObj(f));
            return result;
        }
        catch (System.IO.IOException e) { throw new IOError($"fs.glob failed: {e.Message}"); }
    }

    /// <summary>Returns true if the path points to a regular file.</summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the path is an existing regular file.</returns>
    [StashFn]
    private static bool IsFile(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        return System.IO.File.Exists(path);
    }

    /// <summary>Returns true if the path points to a directory.</summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the path is an existing directory.</returns>
    [StashFn]
    private static bool IsDir(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        return System.IO.Directory.Exists(path);
    }

    /// <summary>Returns true if the path points to a symbolic link.</summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the path is an existing symbolic link.</returns>
    [StashFn]
    private static bool IsSymlink(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        try
        {
            var info = new System.IO.FileInfo(path);
            return info.Exists && info.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint);
        }
        catch (System.IO.IOException)
        {
            return false;
        }
    }

    /// <summary>Creates a temporary file and returns its path.</summary>
    /// <returns>The path to the newly created temporary file.</returns>
    [StashFn]
    private static string TempFile(IInterpreterContext _)
    {
        return System.IO.Path.GetTempFileName();
    }

    /// <summary>Creates a temporary directory and returns its path.</summary>
    /// <returns>The path to the newly created temporary directory.</returns>
    [StashFn]
    private static string TempDir(IInterpreterContext _)
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Returns the last modification time of a file as a Unix timestamp (seconds since epoch).</summary>
    /// <param name="path">The file path.</param>
    /// <exception cref="IOError">if the file does not exist or cannot be accessed</exception>
    /// <returns>The last modified time as a float (seconds since Unix epoch).</returns>
    [StashFn]
    private static double ModifiedAt(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        try
        {
            var info = new System.IO.FileInfo(path);
            return (double)new System.DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds() / 1000.0;
        }
        catch (System.IO.IOException e) { throw new IOError($"Cannot get modified time for '{path}': {e.Message}"); }
    }

    /// <summary>Recursively lists all files under the given directory path.</summary>
    /// <param name="path">The directory path to walk.</param>
    /// <exception cref="IOError">if the directory does not exist or cannot be read</exception>
    /// <returns>An array of file path strings for all files found recursively.</returns>
    [StashFn]
    private static List<StashValue> Walk(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        try
        {
            var files = System.IO.Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories);
            var result = new List<StashValue>(files.Length);
            foreach (var f in files)
                result.Add(StashValue.FromObj(f));
            return result;
        }
        catch (System.IO.IOException e) { throw new IOError($"fs.walk failed: {e.Message}"); }
    }

    /// <summary>Returns true if the file at the given path is readable by the current process.</summary>
    /// <param name="path">The file path to check.</param>
    /// <returns>True if the file exists and is readable.</returns>
    [StashFn]
    private static bool Readable(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        try
        {
            if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
            {
                return false;
            }

            using var stream = System.IO.File.OpenRead(path);
            return true;
        }
        catch (System.UnauthorizedAccessException) { return false; }
        catch (System.IO.IOException) { return false; }
    }

    /// <summary>Returns true if the file at the given path is writable by the current process.</summary>
    /// <param name="path">The file path to check.</param>
    /// <returns>True if the file exists and is writable.</returns>
    [StashFn]
    private static bool Writable(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        try
        {
            if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
            {
                return false;
            }

            if (System.IO.File.Exists(path))
            {
                using var stream = System.IO.File.OpenWrite(path);
                return true;
            }
            // For directories, check by attempting to create a temp file
            var testFile = System.IO.Path.Combine(path, System.IO.Path.GetRandomFileName());
            using (System.IO.File.Create(testFile)) { }
            System.IO.File.Delete(testFile);
            return true;
        }
        catch (System.UnauthorizedAccessException) { return false; }
        catch (System.IO.IOException) { return false; }
    }

    /// <summary>Returns true if the file at the given path is executable. On Unix, checks execute permission bits. On Windows, checks file extension.</summary>
    /// <param name="path">The file path to check.</param>
    /// <returns>True if the file exists and is executable.</returns>
    [StashFn]
    private static bool Executable(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        try
        {
            if (!System.IO.File.Exists(path))
            {
                return false;
            }

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
            {
                // On Windows, check file extension
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                return ext is ".exe" or ".cmd" or ".bat" or ".com" or ".ps1";
            }
            else
            {
                // On Unix, check executable permission via file mode
                var mode = System.IO.File.GetUnixFileMode(path);
                return (mode & (System.IO.UnixFileMode.UserExecute |
                                System.IO.UnixFileMode.GroupExecute |
                                System.IO.UnixFileMode.OtherExecute)) != 0;
            }
        }
        catch (System.IO.IOException) { return false; }
    }

    /// <summary>Creates an empty file at the given path, or updates its last-modified time if it already exists.</summary>
    /// <param name="path">The file path to create or touch.</param>
    /// <exception cref="IOError">if the file cannot be created or its modification time cannot be updated</exception>
    /// <returns>null</returns>
    [StashFn]
    private static void CreateFile(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.SetLastWriteTimeUtc(path, System.DateTime.UtcNow);
            }
            else
            {
                using (System.IO.File.Create(path)) { }
            }
        }
        catch (System.IO.IOException e) { throw new IOError($"Cannot create file '{path}': {e.Message}"); }
    }

    /// <summary>Creates a symbolic link at linkPath pointing to target.</summary>
    /// <param name="target">The target path the symlink will point to.</param>
    /// <param name="linkPath">The path where the symbolic link will be created.</param>
    /// <exception cref="IOError">if the symbolic link cannot be created</exception>
    /// <returns>null</returns>
    [StashFn(ReturnType = "null")]
    private static void Symlink(IInterpreterContext ctx, string target, string linkPath)
    {
        target = ctx.ExpandTilde(target);
        linkPath = ctx.ExpandTilde(linkPath);

        try
        {
            System.IO.File.CreateSymbolicLink(linkPath, target);
        }
        catch (System.IO.IOException e) { throw new IOError($"Cannot create symlink '{linkPath}': {e.Message}"); }
    }

    /// <summary>Returns a dictionary with file metadata including size, isFile, isDir, isSymlink, modified, created, and name.</summary>
    /// <param name="path">The file or directory path.</param>
    /// <exception cref="IOError">if the path does not exist or cannot be accessed</exception>
    /// <returns>A dictionary with keys: size (int), isFile (bool), isDir (bool), isSymlink (bool), modified (float), created (float), name (string).</returns>
    [StashFn(ReturnType = "dict")]
    private static StashValue Stat(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);

        try
        {
            var info = new System.IO.FileInfo(path);
            if (!info.Exists && !System.IO.Directory.Exists(path))
            {
                throw new IOError($"Path does not exist: '{path}'.");
            }

            var isDir = System.IO.Directory.Exists(path);
            var result = new StashDictionary();
            result.Set("size", StashValue.FromInt(isDir ? 0L : info.Length));
            result.Set("isFile", StashValue.FromBool(info.Exists && !isDir));
            result.Set("isDir", StashValue.FromBool(isDir));
            bool isSymlink = false;
            try { isSymlink = (System.IO.File.GetAttributes(path) & System.IO.FileAttributes.ReparsePoint) != 0; } catch { }
            result.Set("isSymlink", StashValue.FromBool(isSymlink));
            result.Set("modified", StashValue.FromFloat((double)new System.DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds() / 1000.0));
            result.Set("created", StashValue.FromFloat((double)new System.DateTimeOffset(info.CreationTimeUtc).ToUnixTimeMilliseconds() / 1000.0));
            result.Set("name", StashValue.FromObj(System.IO.Path.GetFileName(path)));
            return StashValue.FromObj(result);
        }
        catch (RuntimeError) { throw; }
        catch (System.IO.IOException e) { throw new IOError($"Cannot stat '{path}': {e.Message}"); }
    }

    /// <summary>Returns a FilePermissions struct describing the read/write/execute permissions for owner, group, and others.</summary>
    /// <param name="path">The path to inspect.</param>
    /// <exception cref="IOError">if the path does not exist or permissions cannot be read</exception>
    /// <returns>A FilePermissions struct with owner, group, and others fields (each a FilePermission with read, write, execute bools).</returns>
    [StashFn(ReturnType = "FilePermissions")]
    private static StashValue GetPermissions(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);

        try
        {
            if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                throw new IOError($"Path does not exist: '{path}'.");

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
            {
                bool isReadOnly;
                bool isExe;

                if (System.IO.File.Exists(path))
                {
                    var info = new System.IO.FileInfo(path);
                    isReadOnly = info.IsReadOnly;
                    var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                    isExe = ext is ".exe" or ".cmd" or ".bat" or ".com" or ".ps1";
                }
                else
                {
                    // Directory
                    var attrs = System.IO.File.GetAttributes(path);
                    isReadOnly = (attrs & System.IO.FileAttributes.ReadOnly) != 0;
                    isExe = false;
                }

                return StashValue.FromObj(new StashInstance("FilePermissions", new Dictionary<string, StashValue>
                {
                    ["owner"] = StashValue.FromObj(new StashInstance("FilePermission", new Dictionary<string, StashValue>
                    {
                        ["read"] = StashValue.True,
                        ["write"] = StashValue.FromBool(!isReadOnly),
                        ["execute"] = StashValue.FromBool(isExe),
                    })),
                    ["group"] = StashValue.FromObj(new StashInstance("FilePermission", new Dictionary<string, StashValue>
                    {
                        ["read"] = StashValue.True,
                        ["write"] = StashValue.FromBool(!isReadOnly),
                        ["execute"] = StashValue.FromBool(isExe),
                    })),
                    ["others"] = StashValue.FromObj(new StashInstance("FilePermission", new Dictionary<string, StashValue>
                    {
                        ["read"] = StashValue.True,
                        ["write"] = StashValue.FromBool(!isReadOnly),
                        ["execute"] = StashValue.FromBool(isExe),
                    })),
                }));
            }
            else
            {
                var mode = System.IO.File.GetUnixFileMode(path);

                var owner = new StashInstance("FilePermission", new Dictionary<string, StashValue>
                {
                    ["read"] = StashValue.FromBool((mode & System.IO.UnixFileMode.UserRead) != 0),
                    ["write"] = StashValue.FromBool((mode & System.IO.UnixFileMode.UserWrite) != 0),
                    ["execute"] = StashValue.FromBool((mode & System.IO.UnixFileMode.UserExecute) != 0),
                });

                var group = new StashInstance("FilePermission", new Dictionary<string, StashValue>
                {
                    ["read"] = StashValue.FromBool((mode & System.IO.UnixFileMode.GroupRead) != 0),
                    ["write"] = StashValue.FromBool((mode & System.IO.UnixFileMode.GroupWrite) != 0),
                    ["execute"] = StashValue.FromBool((mode & System.IO.UnixFileMode.GroupExecute) != 0),
                });

                var others = new StashInstance("FilePermission", new Dictionary<string, StashValue>
                {
                    ["read"] = StashValue.FromBool((mode & System.IO.UnixFileMode.OtherRead) != 0),
                    ["write"] = StashValue.FromBool((mode & System.IO.UnixFileMode.OtherWrite) != 0),
                    ["execute"] = StashValue.FromBool((mode & System.IO.UnixFileMode.OtherExecute) != 0),
                });

                return StashValue.FromObj(new StashInstance("FilePermissions", new Dictionary<string, StashValue>
                {
                    ["owner"] = StashValue.FromObj(owner),
                    ["group"] = StashValue.FromObj(group),
                    ["others"] = StashValue.FromObj(others),
                }));
            }
        }
        catch (RuntimeError) { throw; }
        catch (System.UnauthorizedAccessException e) { throw new IOError($"Cannot get permissions for '{path}': {e.Message}"); }
        catch (System.IO.IOException e) { throw new IOError($"Cannot get permissions for '{path}': {e.Message}"); }
    }

    /// <summary>Sets file permissions from a FilePermissions struct. On Unix, sets full rwx bits for owner/group/others. On Windows, controls the read-only attribute based on owner write permission.</summary>
    /// <param name="path">The file path to modify.</param>
    /// <param name="permissions">A FilePermissions struct with owner, group, and others fields.</param>
    /// <exception cref="IOError">if the path does not exist or permissions cannot be set</exception>
    /// <exception cref="TypeError">if `permissions` is not a FilePermissions struct, or if owner/group/others is not a FilePermission struct</exception>
    [StashFn(ReturnType = "null")]
    private static void SetPermissions(IInterpreterContext ctx, string path, StashValue permissions)
    {
        if (permissions.ToObject() is not StashInstance perms || perms.TypeName != "FilePermissions")
            throw new TypeError("Second argument to 'fs.setPermissions' must be a FilePermissions.");
        path = ctx.ExpandTilde(path);

        try
        {
            if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                throw new IOError($"Path does not exist: '{path}'.");

            var ownerInst = perms.GetField("owner", null).ToObject() as StashInstance
                ?? throw new TypeError("'owner' field must be a FilePermission struct.");
            var groupInst = perms.GetField("group", null).ToObject() as StashInstance
                ?? throw new TypeError("'group' field must be a FilePermission struct.");
            var othersInst = perms.GetField("others", null).ToObject() as StashInstance
                ?? throw new TypeError("'others' field must be a FilePermission struct.");

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
            {
                // Windows: only write permission (ReadOnly attribute) can be controlled
                bool ownerWrite = ownerInst.GetField("write", null).ToObject() as bool? ?? false;
                if (System.IO.File.Exists(path))
                {
                    new System.IO.FileInfo(path).IsReadOnly = !ownerWrite;
                }
                else
                {
                    // Directory
                    var attrs = System.IO.File.GetAttributes(path);
                    if (!ownerWrite)
                        System.IO.File.SetAttributes(path, attrs | System.IO.FileAttributes.ReadOnly);
                    else
                        System.IO.File.SetAttributes(path, attrs & ~System.IO.FileAttributes.ReadOnly);
                }
            }
            else
            {
                var mode = System.IO.UnixFileMode.None;

                if (ownerInst.GetField("read", null).ToObject() as bool? ?? false) mode |= System.IO.UnixFileMode.UserRead;
                if (ownerInst.GetField("write", null).ToObject() as bool? ?? false) mode |= System.IO.UnixFileMode.UserWrite;
                if (ownerInst.GetField("execute", null).ToObject() as bool? ?? false) mode |= System.IO.UnixFileMode.UserExecute;

                if (groupInst.GetField("read", null).ToObject() as bool? ?? false) mode |= System.IO.UnixFileMode.GroupRead;
                if (groupInst.GetField("write", null).ToObject() as bool? ?? false) mode |= System.IO.UnixFileMode.GroupWrite;
                if (groupInst.GetField("execute", null).ToObject() as bool? ?? false) mode |= System.IO.UnixFileMode.GroupExecute;

                if (othersInst.GetField("read", null).ToObject() as bool? ?? false) mode |= System.IO.UnixFileMode.OtherRead;
                if (othersInst.GetField("write", null).ToObject() as bool? ?? false) mode |= System.IO.UnixFileMode.OtherWrite;
                if (othersInst.GetField("execute", null).ToObject() as bool? ?? false) mode |= System.IO.UnixFileMode.OtherExecute;

                System.IO.File.SetUnixFileMode(path, mode);
            }
        }
        catch (RuntimeError) { throw; }
        catch (System.UnauthorizedAccessException e) { throw new IOError($"Cannot set permissions on '{path}': {e.Message}"); }
        catch (System.IO.IOException e) { throw new IOError($"Cannot set permissions on '{path}': {e.Message}"); }
    }

    /// <summary>Sets or clears the read-only state of a file. On Unix, toggles write bits. On Windows, sets the ReadOnly file attribute.</summary>
    /// <param name="path">The file path to modify.</param>
    /// <param name="readOnly">True to make the file read-only, false to make it writable.</param>
    /// <exception cref="IOError">if the path does not exist or the read-only state cannot be changed</exception>
    [StashFn(ReturnType = "null")]
    private static void SetReadOnly(IInterpreterContext ctx, string path, bool readOnly)
    {
        path = ctx.ExpandTilde(path);

        try
        {
            if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                throw new IOError($"Path does not exist: '{path}'.");

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
            {
                if (System.IO.File.Exists(path))
                {
                    new System.IO.FileInfo(path).IsReadOnly = readOnly;
                }
                else
                {
                    var attrs = System.IO.File.GetAttributes(path);
                    if (readOnly)
                        System.IO.File.SetAttributes(path, attrs | System.IO.FileAttributes.ReadOnly);
                    else
                        System.IO.File.SetAttributes(path, attrs & ~System.IO.FileAttributes.ReadOnly);
                }
            }
            else
            {
                var mode = System.IO.File.GetUnixFileMode(path);
                if (readOnly)
                {
                    mode &= ~(System.IO.UnixFileMode.UserWrite |
                               System.IO.UnixFileMode.GroupWrite |
                               System.IO.UnixFileMode.OtherWrite);
                }
                else
                {
                    mode |= System.IO.UnixFileMode.UserWrite;
                }
                System.IO.File.SetUnixFileMode(path, mode);
            }
        }
        catch (RuntimeError) { throw; }
        catch (System.UnauthorizedAccessException e) { throw new IOError($"Cannot set read-only on '{path}': {e.Message}"); }
        catch (System.IO.IOException e) { throw new IOError($"Cannot set read-only on '{path}': {e.Message}"); }
    }

    /// <summary>Changes the owner and group of a file or directory. Unix only.</summary>
    /// <param name="path">The file or directory path</param>
    /// <param name="uid">New owner user ID (-1 to leave unchanged)</param>
    /// <param name="gid">New owner group ID (-1 to leave unchanged)</param>
    /// <exception cref="NotSupportedError">on Windows, where uid/gid ownership changes are not supported</exception>
    /// <exception cref="IOError">if the path does not exist or the ownership change fails</exception>
    [StashFn(ReturnType = "null")]
    private static void Chown(IInterpreterContext ctx, string path, long uid, long gid)
    {
        path = ctx.ExpandTilde(path);

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows))
        {
            throw new NotSupportedError("fs.chown: file ownership change by uid/gid is not supported on Windows. " +
                "Use fs.setPermissions() for access control.");
        }

        ChownFile(path, (int)uid, (int)gid);
    }

    /// <summary>Sets or clears the executable permission on a file. On Unix, toggles the user execute bit (adds on true, clears all execute bits on false). On Windows, this is a no-op since executability is determined by file extension.</summary>
    /// <param name="path">The file path to modify.</param>
    /// <param name="executable">True to make executable, false to remove execute permission.</param>
    /// <exception cref="IOError">if the path does not exist or the executable bit cannot be changed</exception>
    [StashFn(ReturnType = "null")]
    private static void SetExecutable(IInterpreterContext ctx, string path, bool executable)
    {
        path = ctx.ExpandTilde(path);

        try
        {
            if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                throw new IOError($"Path does not exist: '{path}'.");

            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
            {
                var mode = System.IO.File.GetUnixFileMode(path);
                if (executable)
                {
                    mode |= System.IO.UnixFileMode.UserExecute;
                }
                else
                {
                    mode &= ~(System.IO.UnixFileMode.UserExecute |
                               System.IO.UnixFileMode.GroupExecute |
                               System.IO.UnixFileMode.OtherExecute);
                }
                System.IO.File.SetUnixFileMode(path, mode);
            }
            // On Windows, executable status is determined by file extension -- no action needed.
        }
        catch (RuntimeError) { throw; }
        catch (System.UnauthorizedAccessException e) { throw new IOError($"Cannot set executable on '{path}': {e.Message}"); }
        catch (System.IO.IOException e) { throw new IOError($"Cannot set executable on '{path}': {e.Message}"); }
    }

    /// <summary>Watches a file or directory for changes and invokes a callback for each event. Returns a Watcher handle that can be passed to fs.unwatch() to stop watching.</summary>
    /// <remarks>The callback receives a WatchEvent struct with type (a WatchEventType enum: Created, Modified, Deleted, Renamed), path (absolute), and oldPath (for renames only). Callbacks execute in an isolated forked context. Value-type variables from the parent scope are snapshotted; mutations to reference types (dicts, struct instances) are visible in both directions. Events are debounced by default (100ms window) to suppress duplicate OS notifications. Set debounce to 0 in WatchOptions to receive every raw event.</remarks>
    /// <param name="path">File or directory path to watch.</param>
    /// <param name="callback">Function receiving a WatchEvent on each change.</param>
    /// <param name="options">Optional WatchOptions struct: recursive (bool), filter (string glob), bufferSize (int bytes), debounce (int ms).</param>
    /// <exception cref="IOError">if the path does not exist or the watcher cannot be started</exception>
    /// <exception cref="TypeError">if `path` or `callback` has the wrong type</exception>
    [StashFn(Raw = true, ReturnType = "Watcher")]
    private static StashValue Watch(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 2 || args.Length > 3)
            throw new RuntimeError("'fs.watch' expects 2 or 3 arguments.");
        var path = SvArgs.String(args, 0, "fs.watch");
        var callback = SvArgs.Callable(args, 1, "fs.watch");
        path = ctx.ExpandTilde(path);
        path = System.IO.Path.GetFullPath(path);

        if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
            throw new IOError($"Cannot watch '{path}': path does not exist.");

        // Parse options
        bool recursive = false;
        string filter = "*";
        int bufferSize = 8192;
        int debounceMs = 100;

        if (args.Length > 2 && args[2].IsObj && args[2].AsObj is StashInstance opts && opts.TypeName == "WatchOptions")
        {
            if (opts.GetField("recursive", null).ToObject() is bool r) recursive = r;
            if (opts.GetField("filter", null).ToObject() is string f) filter = f;
            if (opts.GetField("bufferSize", null).ToObject() is long bs) bufferSize = (int)bs;
            if (opts.GetField("debounce", null).ToObject() is long db) debounceMs = (int)db;
        }

        // Determine watch path and filter for FileSystemWatcher
        string watchPath;
        string watchFilter;
        if (System.IO.File.Exists(path))
        {
            watchPath = System.IO.Path.GetDirectoryName(path)!;
            watchFilter = System.IO.Path.GetFileName(path);
        }
        else
        {
            watchPath = path;
            watchFilter = filter;
        }

        var watcher = new FileSystemWatcher(watchPath, watchFilter)
        {
            IncludeSubdirectories = recursive,
            InternalBufferSize = bufferSize,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
        };

        // Create the handle
        var handle = new StashInstance("Watcher", new Dictionary<string, StashValue>());

        // Create watcher state (manages debouncing + callback invocation)
        var state = new WatcherState(watcher, callback, ctx, debounceMs);
        _activeWatchers[handle] = state;
        ctx.TrackedWatchers.Add((handle, watcher));

        // Attach event handlers
        watcher.Created += (_, e) => state.FireCallback(new StashEnumValue("WatchEventType", "Created"), e.FullPath, null);
        watcher.Changed += (_, e) => state.FireCallback(new StashEnumValue("WatchEventType", "Modified"), e.FullPath, null);
        watcher.Deleted += (_, e) => state.FireCallback(new StashEnumValue("WatchEventType", "Deleted"), e.FullPath, null);
        watcher.Renamed += (_, e) => state.FireCallback(new StashEnumValue("WatchEventType", "Renamed"), e.FullPath, e.OldFullPath);
        watcher.Error += (_, e) =>
        {
            // Log warning but don't crash -- watcher remains active
            try
            {
                IInterpreterContext child = ctx.Fork();
                // Use log.warn pattern -- just write to error output
                child.ErrorOutput.WriteLine($"[WARN] fs.watch: {e.GetException().Message}");
            }
            catch { /* Best-effort warning */ }
        };

        // Start watching
        try
        {
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            // Cleanup on failure
            _activeWatchers.TryRemove(handle, out _);
            ctx.TrackedWatchers.RemoveAll(e => ReferenceEquals(e.Handle, handle));
            state.Dispose();
            throw new IOError($"Cannot watch '{path}': {ex.Message}");
        }

        return StashValue.FromObj(handle);
    }

    /// <summary>Stops a file watcher previously created by fs.watch(). Disposes the underlying OS watcher and removes it from tracking. Calling fs.unwatch() on an already-stopped watcher is a no-op.</summary>
    /// <param name="watcher">The Watcher handle returned by fs.watch().</param>
    /// <exception cref="TypeError">if `watcher` is not a Watcher handle</exception>
    [StashFn(ReturnType = "null")]
    private static void Unwatch(IInterpreterContext ctx, StashValue watcher)
    {
        if (watcher.ToObject() is not StashInstance handle || handle.TypeName != "Watcher")
            throw new TypeError("First argument to 'fs.unwatch' must be a Watcher.");

        if (_activeWatchers.TryRemove(handle, out WatcherState? state))
        {
            state.Dispose();
        }

        // Remove from tracked list (best-effort -- may not be in this context's list)
        ctx.TrackedWatchers.RemoveAll(e => ReferenceEquals(e.Handle, handle));
    }

    /// <summary>Reads the entire contents of a file as a byte array.</summary>
    /// <param name="path">The file path</param>
    /// <exception cref="IOError">if the file does not exist or cannot be read</exception>
    /// <returns>The file contents as byte[]</returns>
    [StashFn(ReturnType = "byte[]")]
    private static StashValue ReadBytes(IInterpreterContext ctx, string path)
    {
        path = ctx.ExpandTilde(path);
        try
        {
            return StashValue.FromObj(new StashByteArray(System.IO.File.ReadAllBytes(path)));
        }
        catch (System.IO.IOException e)
        {
            throw new IOError($"Cannot read file '{path}': {e.Message}");
        }
    }

    /// <summary>Writes raw bytes to a file, creating or overwriting it.</summary>
    /// <param name="path">The file path</param>
    /// <param name="data">The byte array to write</param>
    /// <exception cref="IOError">if the file cannot be created or written</exception>
    [StashFn(ReturnType = "null")]
    private static void WriteBytes(IInterpreterContext ctx, string path, byte[] data)
    {
        path = ctx.ExpandTilde(path);
        try
        {
            System.IO.File.WriteAllBytes(path, data);
        }
        catch (System.IO.IOException e)
        {
            throw new IOError($"Cannot write file '{path}': {e.Message}");
        }
    }

    /// <summary>Appends raw bytes to a file.</summary>
    /// <param name="path">The file path</param>
    /// <param name="data">The byte array to append</param>
    /// <exception cref="IOError">if the file cannot be opened or written</exception>
    [StashFn(ReturnType = "null")]
    private static void AppendBytes(IInterpreterContext ctx, string path, byte[] data)
    {
        path = ctx.ExpandTilde(path);
        try
        {
            using var stream = new System.IO.FileStream(path, System.IO.FileMode.Append, System.IO.FileAccess.Write);
            stream.Write(data);
        }
        catch (System.IO.IOException e)
        {
            throw new IOError($"Cannot append to file '{path}': {e.Message}");
        }
    }
}
