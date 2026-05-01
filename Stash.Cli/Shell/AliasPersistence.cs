namespace Stash.Cli.Shell;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Stash.Bytecode;
using Stash.Runtime;
using Stash.Stdlib.BuiltIns;

/// <summary>
/// Reads and writes the managed <c>aliases.stash</c> persistence file (spec §10).
///
/// <para>
/// Path resolution mirrors <see cref="RcFileLoader"/> (§10.2):
/// <list type="bullet">
///   <item>Linux/macOS: <c>$XDG_CONFIG_HOME/stash/aliases.stash</c> or
///         <c>~/.config/stash/aliases.stash</c></item>
///   <item>Windows: <c>%APPDATA%/stash/aliases.stash</c></item>
/// </list>
/// </para>
/// </summary>
internal static class AliasPersistence
{
    // ── Test support ─────────────────────────────────────────────────────────

    /// <summary>
    /// When non-null, <see cref="GetPath"/> returns this value instead of computing
    /// from environment variables. Set in tests to avoid touching real user config.
    /// </summary>
    public static string? PathOverride { get; set; }

    // ── Path resolution ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the canonical path to <c>aliases.stash</c>, applying XDG-aware resolution.
    /// Does NOT create the parent directory; callers that write must do so themselves.
    /// </summary>
    public static string GetPath()
    {
        if (PathOverride is not null)
            return PathOverride;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "stash", "aliases.stash");
        }

        // Linux/macOS: $XDG_CONFIG_HOME/stash/aliases.stash else ~/.config/stash/aliases.stash
        string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdg))
            return Path.Combine(xdg, "stash", "aliases.stash");

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "stash", "aliases.stash");
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sources <c>aliases.stash</c> (or <paramref name="pathOverride"/>) into the VM,
    /// evaluating each statement tolerantly. After loading, each newly defined alias is
    /// tagged with <see cref="AliasRegistry.AliasSource.Saved"/>.
    /// Errors are printed to <see cref="Console.Error"/> but do not abort processing.
    /// </summary>
    /// <returns>Count of aliases successfully loaded from the file.</returns>
    public static int Load(VirtualMachine vm, ShellRunner runner, string? pathOverride = null)
    {
        string path = pathOverride ?? GetPath();
        if (!File.Exists(path))
            return 0;

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"stash: warning: {path}: {ex.Message}");
            return 0;
        }

        // Snapshot existing alias names so we can identify newly added entries.
        var before = new HashSet<string>(vm.AliasRegistry.Names(), StringComparer.Ordinal);

        // Evaluate each statement tolerantly (individual error → warn and continue).
        // Split on physical lines; accumulate until IsStashInputComplete returns true.
        string[] lines = content.Split('\n');
        int i = 0;
        int loaded = 0;

        while (i < lines.Length)
        {
            int startLine = i + 1; // 1-based for warning messages
            string raw = lines[i].TrimEnd('\r');
            i++;

            // Skip blank lines and comment lines.
            if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("//"))
                continue;

            // Accumulate multi-line statements (e.g. alias.define with multi-line AliasOptions).
            var sb = new StringBuilder(raw);
            while (i < lines.Length && !MultiLineReader.IsStashInputComplete(sb.ToString()))
            {
                sb.Append('\n').Append(lines[i].TrimEnd('\r'));
                i++;
            }

            string stmt = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(stmt))
                continue;

            try
            {
                ShellRunner.EvaluateSource(stmt, vm);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"stash: warning: {path}:{startLine}: {ex.Message}");
                continue;
            }

            // Tag any newly registered aliases as Saved.
            foreach (string name in vm.AliasRegistry.Names())
            {
                if (!before.Contains(name) &&
                    vm.AliasRegistry.TryGet(name, out AliasRegistry.AliasEntry? entry) &&
                    entry is not null)
                {
                    entry.Source = AliasRegistry.AliasSource.Saved;
                    before.Add(name);
                    loaded++;
                }
            }
        }

        return loaded;
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes one or all non-builtin aliases to the managed <c>aliases.stash</c> file and
    /// marks saved entries with <see cref="AliasRegistry.AliasSource.Saved"/>.
    ///
    /// <para>
    /// When <paramref name="singleName"/> is <see langword="null"/>, all non-builtin aliases
    /// are saved (full rewrite). When a name is given, that alias is merged into the existing
    /// file content — preserving other entries that may have been edited by hand.
    /// </para>
    /// <para>
    /// Function aliases must reference a named top-level <c>fn</c> with no captured upvalues
    /// (option (c) — see spec §20 decision log). Lambda-bodied or capturing-closure aliases
    /// throw <see cref="StashErrorTypes.AliasError"/>.
    /// </para>
    /// </summary>
    /// <param name="vm">The active VM whose alias registry is the source of truth.</param>
    /// <param name="singleName">
    ///   Alias name to save, or <see langword="null"/> to save all non-builtin aliases.
    /// </param>
    /// <returns>The path of the file that was written.</returns>
    public static string Save(VirtualMachine vm, string? singleName = null)
    {
        string path = GetPath();
        EnsureParentDir(path);

        if (singleName is not null)
        {
            // Single-entry update: merge into existing file to preserve other entries.
            if (!vm.AliasRegistry.TryGet(singleName, out AliasRegistry.AliasEntry? entry) || entry is null)
                throw new RuntimeError(
                    $"alias '{singleName}' is not defined",
                    null, StashErrorTypes.AliasError);

            if (entry.Source == AliasRegistry.AliasSource.Builtin)
                throw new RuntimeError(
                    $"cannot save built-in alias '{singleName}'",
                    null, StashErrorTypes.AliasError);

            // Serialize first — may throw for unpersistable fn aliases before touching the file.
            string serialized = SerializeEntry(entry);

            string existing = File.Exists(path) ? File.ReadAllText(path) : BuildHeader();
            string merged = RemoveEntryFromContent(singleName, existing);

            // Ensure file ends with a newline before appending.
            if (merged.Length > 0 && merged[^1] != '\n')
                merged += Environment.NewLine;
            merged += serialized + Environment.NewLine;

            File.WriteAllText(path, merged, Encoding.UTF8);
            entry.Source = AliasRegistry.AliasSource.Saved;
        }
        else
        {
            // Full rewrite: all non-builtin entries, sorted by name.
            var toSave = vm.AliasRegistry.All()
                .Where(e => e.Source != AliasRegistry.AliasSource.Builtin)
                .OrderBy(e => e.Name, StringComparer.Ordinal)
                .ToList();

            // Validate ALL entries before touching the file (fail-fast).
            var lines = new List<string>(toSave.Count);
            foreach (var e in toSave)
                lines.Add(SerializeEntry(e)); // may throw AliasError

            var sb = new StringBuilder(BuildHeader());
            foreach (string line in lines)
                sb.AppendLine(line);

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

            foreach (var e in toSave)
                e.Source = AliasRegistry.AliasSource.Saved;
        }

        return path;
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes the <c>alias.define("name", ...)</c> statement for <paramref name="name"/>
    /// from <c>aliases.stash</c> and rewrites the file.
    /// </summary>
    /// <returns>1 if the entry was found and removed; 0 if the file was missing or
    /// the entry was not present.</returns>
    public static int RemoveSaved(string name)
    {
        string path = GetPath();
        if (!File.Exists(path))
            return 0;

        string existing;
        try { existing = File.ReadAllText(path); }
        catch { return 0; }

        string updated = RemoveEntryFromContent(name, existing);
        if (updated == existing)
            return 0;

        File.WriteAllText(path, updated, Encoding.UTF8);
        return 1;
    }

    // ── Serialization ─────────────────────────────────────────────────────────

    private static string BuildHeader()
    {
        return
            $"// Generated by Stash. Edit at your own risk; `alias --save` rewrites this file.{Environment.NewLine}" +
            $"// Last updated: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}{Environment.NewLine}" +
            Environment.NewLine;
    }

    private static string SerializeEntry(AliasRegistry.AliasEntry entry)
    {
        string escapedName = ShellSugarDesugarer.EscapeForStashString(entry.Name);
        string bodyStr = SerializeBody(entry);
        string? optsStr = SerializeOptions(entry);

        return optsStr is null
            ? $"alias.define(\"{escapedName}\", {bodyStr});"
            : $"alias.define(\"{escapedName}\", {bodyStr}, AliasOptions {{{optsStr}}});";
    }

    private static string SerializeBody(AliasRegistry.AliasEntry entry)
    {
        if (entry.Kind == AliasRegistry.AliasKind.Template)
            return $"\"{AliasShellSugar.EscapeBodyForStash(entry.TemplateBody!)}\"";

        // Function alias — option (c): only top-level fns (zero upvalues) are persistable.
        string? fnName = entry.FunctionBody?.TopLevelFunctionName;
        if (fnName is null)
            throw new RuntimeError(
                $"cannot save function alias '{entry.Name}': function aliases must reference a " +
                "top-level fn (lambdas/closures cannot be persisted)",
                null, StashErrorTypes.AliasError);

        return fnName; // Stash identifier — no quotes needed
    }

    private static string? SerializeOptions(AliasRegistry.AliasEntry entry)
    {
        var parts = new List<string>();

        if (entry.Description is not null)
            parts.Add($" description: \"{ShellSugarDesugarer.EscapeForStashString(entry.Description)}\"");

        if (entry.Confirm is not null)
            parts.Add($" confirm: \"{ShellSugarDesugarer.EscapeForStashString(entry.Confirm)}\"");

        if (entry.Before is not null)
        {
            string? fnName = entry.Before.TopLevelFunctionName;
            if (fnName is null)
                throw new RuntimeError(
                    $"cannot save alias '{entry.Name}': 'before' hook must be a top-level fn " +
                    "(lambdas/closures cannot be persisted)",
                    null, StashErrorTypes.AliasError);
            parts.Add($" before: {fnName}");
        }

        if (entry.After is not null)
        {
            string? fnName = entry.After.TopLevelFunctionName;
            if (fnName is null)
                throw new RuntimeError(
                    $"cannot save alias '{entry.Name}': 'after' hook must be a top-level fn " +
                    "(lambdas/closures cannot be persisted)",
                    null, StashErrorTypes.AliasError);
            parts.Add($" after: {fnName}");
        }

        if (entry.Override)
            parts.Add(" override: true");

        if (parts.Count == 0)
            return null;

        return string.Join(",", parts) + " ";
    }

    // ── File manipulation helpers ─────────────────────────────────────────────

    /// <summary>
    /// Returns <paramref name="content"/> with the <c>alias.define("name", ...)</c> statement
    /// for <paramref name="name"/> removed. Handles single-line and multi-line statements.
    /// </summary>
    private static string RemoveEntryFromContent(string name, string content)
    {
        string prefix = $"alias.define(\"{ShellSugarDesugarer.EscapeForStashString(name)}\",";

        string[] lines = content.Split('\n');
        var output = new List<string>(lines.Length);
        int i = 0;

        while (i < lines.Length)
        {
            string line = lines[i];
            string trimmed = line.TrimEnd('\r').TrimStart();

            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                // Skip this statement (possibly multi-line — consume until semicolon terminator).
                bool foundSemi = trimmed.TrimEnd('\r', ' ').EndsWith(";", StringComparison.Ordinal);
                i++;
                while (!foundSemi && i < lines.Length)
                {
                    string cont = lines[i].TrimEnd('\r', ' ');
                    foundSemi = cont.EndsWith(";", StringComparison.Ordinal);
                    i++;
                }
                continue;
            }

            output.Add(line);
            i++;
        }

        return string.Join("\n", output);
    }

    private static void EnsureParentDir(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
