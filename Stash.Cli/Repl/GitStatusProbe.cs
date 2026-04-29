namespace Stash.Cli.Repl;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// Probes the current working directory for git status information and returns
/// a <c>PromptGit</c> struct instance suitable for embedding in a <c>PromptContext</c>.
/// </summary>
/// <remarks>
/// Uses <c>git status --porcelain=v2 --branch --untracked-files=normal</c> with a
/// configurable timeout (default 150 ms). Returns <c>null</c> when git is not on PATH,
/// when the probe times out, or on any unexpected failure.
/// </remarks>
internal static class GitStatusProbe
{
    // Cached "git not found" verdict. null = not yet probed; false = not found; true = found.
    private static bool? _gitAvailable;

    /// <summary>
    /// Probes the specified directory for git status.
    /// </summary>
    /// <param name="cwd">Absolute path of the working directory to probe.</param>
    /// <param name="timeoutMs">Maximum milliseconds to wait for the git process.</param>
    /// <returns>
    /// A <c>PromptGit</c> <see cref="StashInstance"/> on success;
    /// <c>null</c> if git is unavailable, timed out, or an error occurred.
    /// </returns>
    public static StashInstance? Probe(string cwd, int timeoutMs)
    {
        try
        {
            // --- 1. Check git availability (cached after first probe) --------
            if (_gitAvailable == false)
                return null;

            if (_gitAvailable == null)
            {
                if (!CheckGitAvailable())
                {
                    _gitAvailable = false;
                    return null;
                }
                _gitAvailable = true;
            }

            // --- 2. Spawn git ------------------------------------------------
            var psi = new ProcessStartInfo
            {
                FileName               = "git",
                Arguments              = "status --porcelain=v2 --branch --untracked-files=normal",
                WorkingDirectory       = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                UseShellExecute        = false,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            // --- 3. Wait with timeout ----------------------------------------
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return null;
            }

            // --- 4. Not in a repo (non-zero exit) ----------------------------
            if (process.ExitCode != 0)
                return MakeGitInstance(false, "", false, 0, 0, 0, 0, 0);

            // --- 5. Parse porcelain v2 output --------------------------------
            string output = process.StandardOutput.ReadToEnd();

            string branch = "";
            string oid    = "";
            int ahead     = 0;
            int behind    = 0;
            int staged    = 0;
            int unstaged  = 0;
            int untracked = 0;

            foreach (string rawLine in output.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');

                if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
                {
                    oid = line.Substring("# branch.oid ".Length).Trim();
                }
                else if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
                {
                    string head = line.Substring("# branch.head ".Length).Trim();
                    branch = head == "(detached)"
                        ? (oid.Length >= 7 ? oid.Substring(0, 7) : oid)
                        : head;
                }
                else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
                {
                    // Format: +N -M
                    string ab = line.Substring("# branch.ab ".Length).Trim();
                    string[] parts = ab.Split(' ');
                    foreach (string part in parts)
                    {
                        if (part.StartsWith('+') && int.TryParse(part.AsSpan(1), out int a))
                            ahead = a;
                        else if (part.StartsWith('-') && int.TryParse(part.AsSpan(1), out int b))
                            behind = b;
                    }
                }
                else if (line.StartsWith("1 ", StringComparison.Ordinal) ||
                         line.StartsWith("2 ", StringComparison.Ordinal) ||
                         line.StartsWith("u ", StringComparison.Ordinal))
                {
                    // XY field is the second space-delimited token (index 1)
                    int spaceIdx = line.IndexOf(' ', 2);
                    if (spaceIdx >= 2 && spaceIdx + 1 < line.Length)
                    {
                        // XY starts right after "1 " (offset 2), length 2
                        // For 'u' lines the XY block also starts at offset 2
                        char x = line[2];
                        char y = line[3];
                        if (x != '.') staged++;
                        if (y != '.') unstaged++;
                    }
                }
                else if (line.StartsWith("? ", StringComparison.Ordinal))
                {
                    untracked++;
                }
            }

            // --- 6. isDirty -------------------------------------------------
            bool isDirty = (staged + unstaged + untracked) > 0;

            // --- 7. Return PromptGit instance --------------------------------
            return MakeGitInstance(true, branch, isDirty, staged, unstaged, untracked, ahead, behind);
        }
        catch
        {
            return null;
        }
    }

    // -------------------------------------------------------------------------

    private static bool CheckGitAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "git",
                Arguments              = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                UseShellExecute        = false,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(2000);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static StashInstance MakeGitInstance(
        bool isInRepo, string branch, bool isDirty,
        int stagedCount, int unstagedCount, int untrackedCount,
        int ahead, int behind)
    {
        return new StashInstance("PromptGit", new Dictionary<string, StashValue>
        {
            ["isInRepo"]       = StashValue.FromBool(isInRepo),
            ["branch"]         = StashValue.FromObj(branch),
            ["isDirty"]        = StashValue.FromBool(isDirty),
            ["stagedCount"]    = StashValue.FromInt(stagedCount),
            ["unstagedCount"]  = StashValue.FromInt(unstagedCount),
            ["untrackedCount"] = StashValue.FromInt(untrackedCount),
            ["ahead"]          = StashValue.FromInt(ahead),
            ["behind"]         = StashValue.FromInt(behind),
        });
    }
}
