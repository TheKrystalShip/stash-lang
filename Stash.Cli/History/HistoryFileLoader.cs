namespace Stash.Cli.History;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Static helpers for resolving the history file path, reading its entries on startup,
/// and atomically rewriting it when trimming to the cap.
/// </summary>
internal static class HistoryFileLoader
{
    private static readonly Regex HeaderRegex = new(@"^# stash history v(\d+)$", RegexOptions.Compiled);

    /// <summary>
    /// Returns the absolute path of the history file per spec §4, or <c>null</c> when
    /// persistence is explicitly disabled (<c>STASH_HISTORY_FILE=""</c> or <c>STASH_HISTORY_SIZE=0</c>).
    /// </summary>
    public static string? ResolvePath()
    {
        // Empty env var → disabled
        string? explicitPath = Environment.GetEnvironmentVariable("STASH_HISTORY_FILE");
        if (explicitPath != null)
        {
            if (explicitPath.Length == 0)
                return null; // explicitly disabled
            return Path.GetFullPath(explicitPath);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ResolveWindows();
        return ResolvePosix();
    }

    private static string ResolvePosix()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string? xdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        string stateDir;
        if (!string.IsNullOrEmpty(xdgState))
        {
            stateDir = Path.Combine(xdgState, "stash");
        }
        else
        {
            stateDir = Path.Combine(home, ".local", "state", "stash");
        }

        try
        {
            Directory.CreateDirectory(stateDir);
            return Path.Combine(stateDir, "history");
        }
        catch
        {
            // Cannot create state dir — fall back
            return Path.Combine(home, ".stash_history");
        }
    }

    private static string ResolveWindows()
    {
        string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(localAppData))
        {
            string dir = Path.Combine(localAppData, "stash");
            try
            {
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "history");
            }
            catch { }
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".stash_history");
    }

    /// <summary>
    /// Returns the entry cap. Default 10000. <c>STASH_HISTORY_SIZE</c> overrides.
    /// Returns <c>0</c> if disabled, <c>int.MaxValue</c> if negative (unlimited).
    /// </summary>
    public static int GetCap()
    {
        string? sizeEnv = Environment.GetEnvironmentVariable("STASH_HISTORY_SIZE");
        if (sizeEnv != null)
        {
            if (int.TryParse(sizeEnv, out int parsed))
            {
                if (parsed == 0)
                    return 0;
                if (parsed < 0)
                    return int.MaxValue;
                return parsed;
            }
        }
        return 10000;
    }

    /// <summary>
    /// Reads the history file and returns its entries oldest-first.
    /// Enforces the cap on startup; rewrites atomically if trimming is needed.
    /// </summary>
    public static List<string> Load(string path, int cap, TextWriter stderr)
    {
        try
        {
            if (!File.Exists(path))
                return new List<string>();

            string raw = File.ReadAllText(path, new UTF8Encoding(false));
            if (raw.Length == 0)
                return new List<string>();

            // Split into lines to check header
            int headerEnd = raw.IndexOf('\n');
            string firstLine = headerEnd >= 0 ? raw[..headerEnd].TrimEnd() : raw.TrimEnd();
            string body;

            var match = HeaderRegex.Match(firstLine);
            if (match.Success)
            {
                int version = int.Parse(match.Groups[1].Value);
                if (version != 1)
                {
                    stderr.WriteLine($"stash: history file has unknown format version v{version}; reading anyway");
                }
                body = headerEnd >= 0 ? raw[(headerEnd + 1)..] : string.Empty;
            }
            else
            {
                // No header — treat entire content as body
                body = raw;
            }

            // Split on blank lines (\n\n)
            var chunks = body.Split("\n\n", StringSplitOptions.None);
            var entries = new List<string>(chunks.Length);
            foreach (var chunk in chunks)
            {
                string trimmed = chunk.TrimEnd();
                if (trimmed.Length > 0)
                    entries.Add(trimmed);
            }

            // Cap enforcement on startup
            if (cap != int.MaxValue && entries.Count > cap)
            {
                int drop = entries.Count - cap;
                entries.RemoveRange(0, drop);
                RewriteAtomic(path, entries, stderr);
            }

            return entries;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"stash: history disabled — cannot read {path}: {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>
    /// Atomically rewrites the history file with the given entries (temp-file-and-rename).
    /// </summary>
    public static void RewriteAtomic(string path, IReadOnlyList<string> entries, TextWriter stderr)
    {
        string tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (dir != null)
                Directory.CreateDirectory(dir);

            var encoding = new UTF8Encoding(false);
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, encoding);
                writer.NewLine = "\n";
                writer.Write("# stash history v1\n");
                foreach (var entry in entries)
                {
                    writer.Write(entry);
                    writer.Write("\n\n");
                }
                writer.Flush();
                stream.Flush(true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            try { File.Delete(tempPath); } catch { }
            stderr.WriteLine($"stash: history trim failed: {ex.Message}");
        }
    }
}
