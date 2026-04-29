using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Stash.Bytecode;

/// <summary>
/// File-glob expander used by command argument expansion.
/// Supports: <c>*</c> (any chars except '/'), <c>?</c> (single char except '/'),
/// <c>[abc]</c> / <c>[a-z]</c> / <c>[!abc]</c> (char class), <c>**</c> (recurse into dirs).
/// </summary>
internal static class GlobExpander
{
    /// <summary>
    /// Returns <c>true</c> if <paramref name="arg"/> contains any glob metachar
    /// (<c>*</c>, <c>?</c>, <c>[</c>).
    /// </summary>
    internal static bool HasGlobChars(string arg) =>
        arg.IndexOfAny(_metaChars) >= 0;

    private static readonly char[] _metaChars = ['*', '?', '['];

    /// <summary>
    /// Expand <paramref name="pattern"/> against the filesystem.
    /// Relative patterns are resolved from the current working directory.
    /// Absolute patterns (starting with <c>/</c>) are resolved from the root.
    /// Returns matched paths sorted in deterministic order.
    /// Excludes dotfiles unless the pattern component itself starts with <c>.</c>.
    /// Returns an empty list when nothing matches — the caller decides whether to throw.
    /// </summary>
    internal static List<string> Expand(string pattern)
    {
        // Normalise to forward slashes for component splitting
        string normalized = pattern.Replace('\\', '/');

        bool isAbsolute = normalized.StartsWith('/');
        string baseDir = isAbsolute ? "/" : Directory.GetCurrentDirectory();
        string relativePart = isAbsolute ? normalized.TrimStart('/') : normalized;

        string[] components = relativePart.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 0)
            return new List<string>();

        var results = new List<string>();
        string startRel = isAbsolute ? "/" : "";
        ExpandInto(results, baseDir, components, 0, startRel);

        var options = StringComparer.Ordinal;
        results.Sort(options);
        return results;
    }

    // ── Core recursive walker ─────────────────────────────────────────────────

    private static void ExpandInto(
        List<string> results,
        string baseDir,
        string[] components,
        int compIdx,
        string relPath)
    {
        if (compIdx >= components.Length)
        {
            // All components matched — emit the path if non-empty
            if (relPath.Length > 0 && relPath != "/")
                results.Add(relPath);
            return;
        }

        string comp = components[compIdx];
        bool isLast = compIdx == components.Length - 1;

        if (comp == "**")
        {
            // Match zero additional dirs: continue with next component in the same dir
            ExpandInto(results, baseDir, components, compIdx + 1, relPath);

            // Match one or more dirs: recurse into each subdirectory
            IEnumerable<string> subdirs;
            try { subdirs = Directory.EnumerateDirectories(baseDir); }
            catch { return; }

            foreach (string subdir in subdirs)
            {
                string name = Path.GetFileName(subdir);
                // ** never descends into dotdirs
                if (name.StartsWith('.'))
                    continue;

                string newRel = BuildRelPath(relPath, name);
                // Keep the ** component to allow multi-level recursion
                ExpandInto(results, subdir, components, compIdx, newRel);
            }
        }
        else if (HasGlobMeta(comp))
        {
            bool patternStartsWithDot = comp.StartsWith('.');
            Regex regex = ComponentToRegex(comp);

            IEnumerable<string> entries;
            try
            {
                entries = isLast
                    ? Directory.EnumerateFileSystemEntries(baseDir)
                    : Directory.EnumerateDirectories(baseDir);
            }
            catch { return; }

            foreach (string entry in entries)
            {
                string name = Path.GetFileName(entry);

                // Skip dotfiles unless the pattern explicitly starts with '.'
                if (!patternStartsWithDot && name.StartsWith('.'))
                    continue;

                if (!regex.IsMatch(name))
                    continue;

                string newRel = BuildRelPath(relPath, name);

                if (isLast)
                    results.Add(newRel);
                else
                    ExpandInto(results, entry, components, compIdx + 1, newRel);
            }
        }
        else
        {
            // Literal component — no pattern matching needed
            string fullPath = Path.Combine(baseDir, comp);
            string newRel = BuildRelPath(relPath, comp);

            if (isLast)
            {
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                    results.Add(newRel);
            }
            else
            {
                if (Directory.Exists(fullPath))
                    ExpandInto(results, fullPath, components, compIdx + 1, newRel);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool HasGlobMeta(string component) =>
        component.IndexOfAny(_metaChars) >= 0;

    private static string BuildRelPath(string relPath, string name)
    {
        if (relPath.Length == 0)
            return name;
        if (relPath == "/")
            return "/" + name;
        return relPath + "/" + name;
    }

    /// <summary>
    /// Convert a single path component glob pattern to a <see cref="Regex"/>
    /// anchored at both ends. Handles <c>*</c>, <c>?</c>, <c>[...]</c>, <c>[!...]</c>.
    /// </summary>
    private static Regex ComponentToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        int i = 0;
        while (i < pattern.Length)
        {
            char c = pattern[i];
            if (c == '*')
            {
                // A single or double * in a component both mean "any chars except /"
                // (** is only special at the component level, handled in ExpandInto)
                sb.Append("[^/]*");
                i++;
                // Swallow a second * if present (** within a single component)
                if (i < pattern.Length && pattern[i] == '*')
                    i++;
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
                i++;
            }
            else if (c == '[')
            {
                // Find closing bracket, respecting ] immediately after [ or [!
                int j = i + 1;
                if (j < pattern.Length && pattern[j] == '!')
                    j++;
                if (j < pattern.Length && pattern[j] == ']')
                    j++;
                while (j < pattern.Length && pattern[j] != ']')
                    j++;

                if (j < pattern.Length)
                {
                    // Valid bracket expression
                    string inner = pattern.Substring(i + 1, j - i - 1); // inside [ ]
                    if (inner.StartsWith('!'))
                        sb.Append("[^").Append(EscapeCharClassContent(inner[1..])).Append(']');
                    else
                        sb.Append('[').Append(EscapeCharClassContent(inner)).Append(']');
                    i = j + 1;
                }
                else
                {
                    // No closing bracket — treat '[' as literal
                    sb.Append(Regex.Escape("["));
                    i++;
                }
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
                i++;
            }
        }
        sb.Append('$');

        var options = OperatingSystem.IsWindows()
            ? RegexOptions.IgnoreCase
            : RegexOptions.None;
        return new Regex(sb.ToString(), options);
    }

    /// <summary>
    /// Escapes regex metacharacters inside a character class, but preserves
    /// range syntax (<c>a-z</c>) and the caret that is already handled by the caller.
    /// </summary>
    private static string EscapeCharClassContent(string inner)
    {
        // Inside [] only a few chars need escaping for .NET regex: \, ^, ]
        // Ranges like a-z must pass through unchanged.
        var sb = new StringBuilder(inner.Length);
        foreach (char ch in inner)
        {
            if (ch == '\\')
                sb.Append("\\\\");
            else
                sb.Append(ch);
        }
        return sb.ToString();
    }
}
