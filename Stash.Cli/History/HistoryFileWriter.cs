namespace Stash.Cli.History;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Appends history entries to disk on each accepted line, maintaining an in-memory mirror
/// for snapshotting. Implements spec §5.4 writer semantics and §6 behavioral rules.
/// </summary>
internal sealed class HistoryFileWriter
{
    private readonly string _path;
    private readonly int _cap;
    private readonly TextWriter _stderr;
    private readonly List<string> _history;
    private readonly object _lock = new();
    private bool _disabled;

    public HistoryFileWriter(string path, int cap, IReadOnlyList<string> initial, TextWriter stderr)
    {
        _path = path;
        _cap = cap;
        _stderr = stderr;
        _history = new List<string>(initial);
    }

    /// <summary>Returns a fresh copy of the in-memory history, oldest-first.</summary>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_lock)
        {
            return new List<string>(_history);
        }
    }

    /// <summary>
    /// Appends an entry to in-memory history and (if not disabled) to the file on disk.
    /// Implements spec §5.4 + §6.1 + §6.2 + §6.3.
    /// </summary>
    public void Append(string entry)
    {
        // §6.3 — empty / whitespace-only lines never recorded
        if (string.IsNullOrEmpty(entry) || string.IsNullOrWhiteSpace(entry))
            return;

        // §6.2 — leading ASCII space skips history entirely
        if (entry[0] == ' ')
            return;

        lock (_lock)
        {
            // §6.1 — consecutive-duplicate collapse
            if (_history.Count > 0 && _history[^1] == entry)
                return;

            _history.Add(entry);

            // Cap eviction (in-memory mirror)
            if (_cap != int.MaxValue)
            {
                while (_history.Count > _cap)
                    _history.RemoveAt(0);
            }

            if (_disabled)
                return;

            TryWriteToFile(entry);
        }
    }

    /// <summary>
    /// Clears the in-memory history and truncates the on-disk file to just the header.
    /// Implements spec §6.6.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _history.Clear();

            if (_disabled)
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, "# stash history v1\n", new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                _disabled = true;
                _stderr.WriteLine($"stash: history disabled — cannot write {_path}: {ex.Message}");
            }
        }
    }

    private void TryWriteToFile(string entry)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (dir != null)
                Directory.CreateDirectory(dir);

            bool isNew = !File.Exists(_path);

            using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var encoding = new UTF8Encoding(false);
            // §5.1 — write the header on first write to a fresh file.
            if (isNew)
            {
                var headerBytes = encoding.GetBytes("# stash history v1\n");
                stream.Write(headerBytes);
            }
            var bytes = encoding.GetBytes(entry + "\n\n");
            stream.Write(bytes);
            stream.Flush();

            // On POSIX, set 0600 permissions if file was just created (best-effort)
            if (isNew && !OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _disabled = true;
            _stderr.WriteLine($"stash: history disabled — cannot write {_path}: {ex.Message}");
        }
    }
}
