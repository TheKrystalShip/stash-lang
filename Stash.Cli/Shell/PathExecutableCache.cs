using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Stash.Cli.Shell;

/// <summary>
/// Lazily indexes PATH directories and answers "is this name executable?" queries.
/// Results are cached with a 60-second TTL; the cache is invalidated explicitly
/// when the working directory or PATH changes.
/// </summary>
internal sealed class PathExecutableCache
{
    private readonly Dictionary<string, bool> _cache =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(60);
    private readonly object _lock = new();

    // Optional override for testing: when set, IsExecutable delegates to this
    // function instead of querying the real file system.
    private readonly Func<string, bool>? _isExecutableOverride;

    public PathExecutableCache(Func<string, bool>? isExecutableOverride = null)
    {
        _isExecutableOverride = isExecutableOverride;
    }

    /// <summary>
    /// Returns true if <paramref name="name"/> is an executable on PATH.
    /// Absolute/relative paths (containing a directory separator) always return false —
    /// they are treated as shell mode by the path-like classifier rule, not by this cache.
    /// </summary>
    public bool IsExecutable(string name)
    {
        if (_isExecutableOverride is not null)
            return _isExecutableOverride(name);

        lock (_lock)
        {
            RefreshIfStale();
            return _cache.TryGetValue(name, out bool found) && found;
        }
    }

    /// <summary>Invalidates the cache so the next query triggers a full rescan.</summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _cache.Clear();
            _lastRefresh = DateTime.MinValue;
        }
    }

    private void RefreshIfStale()
    {
        // Called while _lock is held.
        if (DateTime.UtcNow - _lastRefresh < _ttl)
            return;

        _cache.Clear();

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            _lastRefresh = DateTime.UtcNow;
            return;
        }

        char separator = OperatingSystem.IsWindows() ? ';' : ':';
        string[] dirs = pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        foreach (string dir in dirs)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;

                foreach (string filePath in Directory.EnumerateFiles(dir))
                {
                    string fileName = Path.GetFileName(filePath);
                    if (string.IsNullOrEmpty(fileName)) continue;
                    if (_cache.ContainsKey(fileName)) continue; // first PATH dir wins

                    if (IsFileExecutable(filePath))
                    {
                        _cache[fileName] = true;
                        // On Windows, also register the extension-stripped name so that typing
                        // 'notepad' resolves even though the file is 'notepad.exe' on PATH.
                        if (OperatingSystem.IsWindows())
                        {
                            string nameNoExt = Path.GetFileNameWithoutExtension(filePath);
                            if (!string.IsNullOrEmpty(nameNoExt) && !_cache.ContainsKey(nameNoExt))
                                _cache[nameNoExt] = true;
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        _lastRefresh = DateTime.UtcNow;
    }

    private static bool IsFileExecutable(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            // On Windows, executability is determined by the file extension.
            string ext = Path.GetExtension(filePath);
            string? pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD;.COM";
            foreach (string allowedExt in pathext.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(ext, allowedExt, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        else
        {
            // POSIX: check for execute bit.
            try
            {
                var mode = File.GetUnixFileMode(filePath);
                return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
