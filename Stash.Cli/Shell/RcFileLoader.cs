using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Stash.Bytecode;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Cli.Shell;

/// <summary>
/// Locates and loads the Stash RC file (§12) at REPL start-up.
///
/// Path resolution order (first match wins — §12.1):
///   1. $XDG_CONFIG_HOME/stash/init.stash  (only when XDG_CONFIG_HOME is set and non-empty)
///   2. ~/.config/stash/init.stash
///   3. ~/.stashrc
/// </summary>
internal static class RcFileLoader
{
    /// <summary>
    /// Resolves the first-existing RC file path, or <see langword="null"/> if none exist.
    /// Never throws — permission / IO errors are treated as not-found.
    /// </summary>
    public static string? FindRcFile()
    {
        // ── Candidate 1: $XDG_CONFIG_HOME/stash/init.stash ──────────────────
        try
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrEmpty(xdg))
            {
                string xdgPath = Path.Combine(xdg, "stash", "init.stash");
                if (File.Exists(xdgPath))
                    return xdgPath;
            }
        }
        catch { }

        // ── Candidate 2: ~/.config/stash/init.stash ─────────────────────────
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                string configPath = Path.Combine(home, ".config", "stash", "init.stash");
                if (File.Exists(configPath))
                    return configPath;
            }
        }
        catch { }

        // ── Candidate 3: ~/.stashrc ──────────────────────────────────────────
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                string stashrcPath = Path.Combine(home, ".stashrc");
                if (File.Exists(stashrcPath))
                    return stashrcPath;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Reads the RC file and feeds each logical line through the appropriate evaluator (§12.2).
    ///
    /// Multi-line continuations (backslash, unbalanced brackets, trailing pipe) are coalesced
    /// using the same rules as the interactive REPL.
    ///
    /// Errors are printed to <see cref="Console.Error"/> as
    ///   <c>stash: warning: &lt;rcPath&gt;:&lt;lineNumber&gt;: &lt;message&gt;</c>
    /// and do NOT abort processing of remaining lines.
    /// </summary>
    /// <param name="rcPath">Absolute path to the RC file to load.</param>
    /// <param name="vm">The REPL virtual machine whose globals will receive declarations.</param>
    /// <param name="shellEnabled">
    ///   When <see langword="true"/>, shell-mode lines are routed through <see cref="ShellRunner"/>;
    ///   when <see langword="false"/>, every line is treated as Stash code.
    /// </param>
    public static void Load(string rcPath, VirtualMachine vm, bool shellEnabled)
    {
        string content;
        try
        {
            content = File.ReadAllText(rcPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"stash: warning: {rcPath}: {ex.Message}");
            return;
        }

        // Build shell classifier and runner once per load when shell mode is enabled.
        ShellLineClassifier? classifier = null;
        ShellRunner? runner = null;
        if (shellEnabled)
        {
            var ctx = new ShellContext
            {
                Vm = vm,
                PathCache = new PathExecutableCache(),
                Keywords = ShellContext.BuildKeywordSet(),
                Namespaces = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
                ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
            };
            classifier = new ShellLineClassifier(ctx);
            runner = new ShellRunner(ctx);
        }

        // Split into physical lines (handle both LF and CRLF).
        string[] physicalLines = content.Split('\n');

        int lineIndex = 0;
        while (lineIndex < physicalLines.Length)
        {
            int startLine = lineIndex + 1; // 1-based line number for warnings
            var accumulated = new StringBuilder();

            // Append the first physical line (trimming \r for CRLF files).
            accumulated.Append(physicalLines[lineIndex].TrimEnd('\r'));
            lineIndex++;

            // Accumulate additional physical lines as needed for continuation.
            while (true)
            {
                string current = accumulated.ToString();

                // ── Rule 1: trailing backslash continuation (both modes) ─────
                if (MultiLineReader.HasTrailingContinuationBackslash(current))
                {
                    // Strip the trailing backslash, ensure a space separator.
                    accumulated.Remove(accumulated.Length - 1, 1);
                    if (accumulated.Length > 0 && accumulated[^1] != ' ')
                        accumulated.Append(' ');

                    if (lineIndex < physicalLines.Length)
                    {
                        accumulated.Append(physicalLines[lineIndex].TrimEnd('\r'));
                        lineIndex++;
                    }
                    continue;
                }

                // ── Rule 2: shell-mode trailing-pipe continuation ────────────
                if (shellEnabled && classifier is not null && classifier.IsShellIncomplete(current))
                {
                    if (lineIndex < physicalLines.Length)
                    {
                        accumulated.Append('\n');
                        accumulated.Append(physicalLines[lineIndex].TrimEnd('\r'));
                        lineIndex++;
                        continue;
                    }
                    break;
                }

                // ── Rule 3: Stash bracket / string continuation ──────────────
                if (!MultiLineReader.IsStashInputComplete(current))
                {
                    if (lineIndex < physicalLines.Length)
                    {
                        accumulated.Append('\n');
                        accumulated.Append(physicalLines[lineIndex].TrimEnd('\r'));
                        lineIndex++;
                        continue;
                    }
                    break;
                }

                break;
            }

            string logicalLine = accumulated.ToString();

            // Skip blank lines.
            if (string.IsNullOrWhiteSpace(logicalLine))
                continue;

            // ── Route and execute ────────────────────────────────────────────
            try
            {
                if (shellEnabled && classifier is not null && runner is not null)
                {
                    LineMode mode = classifier.Classify(logicalLine);
                    if (mode is LineMode.Shell or LineMode.ShellForced or LineMode.ShellStrict)
                    {
                        runner.Run(logicalLine);
                        continue;
                    }
                }

                ShellRunner.EvaluateSource(logicalLine, vm);
            }
            catch (RuntimeError ex)
            {
                Console.Error.WriteLine($"stash: warning: {rcPath}:{startLine}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"stash: warning: {rcPath}:{startLine}: {ex.Message}");
            }
        }
    }
}
