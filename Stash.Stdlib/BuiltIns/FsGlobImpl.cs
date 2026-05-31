namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Filesystem glob expansion with <em>conventional</em> (segment-aware) glob semantics:
/// <list type="bullet">
///   <item><c>*</c> matches any run of characters within a single path segment (does NOT cross <c>/</c>).</item>
///   <item><c>**</c> matches across path segments (zero or more directories).</item>
///   <item><c>?</c> matches a single character within a segment.</item>
///   <item><c>[...]</c> is a character class (<c>[!...]</c>/<c>[^...]</c> negates).</item>
/// </list>
/// This is deliberately DISTINCT from <see cref="PathGlobImpl"/>, which implements bash
/// <c>[[ ]]</c> semantics (where <c>*</c> crosses <c>/</c>) for the pure string predicate
/// <c>path.match</c>. <c>fs.glob</c> enumerates the disk; <c>path.match</c> tests a string.
///
/// Not Stash-visible; used only by <see cref="FsBuiltIns"/>'s <c>glob</c>.
/// </summary>
internal static class FsGlobImpl
{
    private static readonly char[] GlobChars = { '*', '?', '[' };

    /// <summary>
    /// Expands <paramref name="pattern"/> against the real filesystem and returns the
    /// matching FILE paths (directories are not returned), normalized to <c>/</c>
    /// separators and sorted ordinally for deterministic output. A pattern with no glob
    /// metacharacter is treated as an exact path (returned iff the file exists).
    /// May throw <see cref="IOException"/>/<see cref="UnauthorizedAccessException"/> during
    /// enumeration; the caller is responsible for translating those to a Stash error.
    /// </summary>
    internal static List<string> Expand(string pattern)
    {
        // No wildcards: exact-path check (mirrors a literal shell glob with nullglob off
        // collapsing to the literal when it exists).
        if (pattern.IndexOfAny(GlobChars) < 0)
            return File.Exists(pattern) ? new List<string> { pattern } : new List<string>();

        string norm = pattern.Replace('\\', '/');
        string[] segs = norm.Split('/');

        // The enumeration root is the longest leading run of segments that contain no glob
        // metacharacter. .NET's Directory.EnumerateFiles only accepts a wildcard in the
        // search pattern (leaf), never in the directory portion — so we enumerate from the
        // static prefix and filter every candidate with the compiled glob regex. This is
        // what lets a mid-path wildcard like ".kanban/4-done/*/plan.yaml" work at all.
        int firstGlob = 0;
        for (; firstGlob < segs.Length; firstGlob++)
            if (segs[firstGlob].IndexOfAny(GlobChars) >= 0)
                break;

        bool absolute = norm.StartsWith("/", StringComparison.Ordinal);
        string staticPrefix = string.Join("/", segs.Take(firstGlob));
        string enumRoot = staticPrefix.Length == 0 ? (absolute ? "/" : ".") : staticPrefix;

        if (!Directory.Exists(enumRoot))
            return new List<string>();

        var regex = new Regex(GlobToRegex(norm), RegexOptions.Singleline | RegexOptions.CultureInvariant);
        var matches = new List<string>();
        foreach (var f in Directory.EnumerateFiles(enumRoot, "*", SearchOption.AllDirectories))
        {
            string cand = f.Replace('\\', '/');
            // When enumerating from ".", .NET prefixes results with "./"; strip it so the
            // candidate is in the same rootless shape as a pattern like "*.txt" or "a/*.cs".
            if (enumRoot == "." && cand.StartsWith("./", StringComparison.Ordinal))
                cand = cand.Substring(2);
            if (regex.IsMatch(cand))
                matches.Add(cand);
        }

        matches.Sort(StringComparer.Ordinal);
        return matches;
    }

    /// <summary>
    /// Translates a conventional glob pattern into an anchored regex. Exposed internally so
    /// the segment-aware semantics can be reasoned about and tested independently of disk I/O.
    /// </summary>
    internal static string GlobToRegex(string pattern)
    {
        var sb = new StringBuilder(pattern.Length * 2 + 2);
        sb.Append('^');

        int i = 0;
        int n = pattern.Length;
        while (i < n)
        {
            char c = pattern[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < n && pattern[i + 1] == '*')
                    {
                        // Globstar. Handle the three positional forms so "a/**/b" also
                        // matches "a/b" (zero intervening directories):
                        bool slashBefore = i == 0 || pattern[i - 1] == '/';
                        bool slashAfter = i + 2 < n && pattern[i + 2] == '/';
                        if (slashBefore && slashAfter)
                        {
                            // "/**/" or leading "**/": zero or more whole directory segments.
                            sb.Append("(?:[^/]*/)*");
                            i += 3; // consume "**" and the trailing '/'
                        }
                        else
                        {
                            // trailing "/**", or a bare "**": match anything (crosses '/').
                            sb.Append(".*");
                            i += 2;
                        }
                    }
                    else
                    {
                        // Single star: stay within a path segment.
                        sb.Append("[^/]*");
                        i++;
                    }
                    break;

                case '?':
                    sb.Append("[^/]");
                    i++;
                    break;

                case '[':
                    int close = FindClassEnd(pattern, i);
                    if (close < 0)
                    {
                        // Unclosed class: treat '[' as a literal.
                        sb.Append("\\[");
                        i++;
                    }
                    else
                    {
                        sb.Append('[');
                        int j = i + 1;
                        if (j <= close && (pattern[j] == '!' || pattern[j] == '^'))
                        {
                            sb.Append('^');
                            j++;
                        }
                        // A ']' immediately after the (optional) negation is a literal member.
                        if (j < close && pattern[j] == ']')
                        {
                            sb.Append("\\]");
                            j++;
                        }
                        for (; j < close; j++)
                        {
                            char cc = pattern[j];
                            // Escape regex-meaningful class chars; leave ranges (a-z) intact.
                            if (cc == '\\' || cc == '^' || cc == ']' || cc == '[')
                                sb.Append('\\');
                            sb.Append(cc);
                        }
                        sb.Append(']');
                        i = close + 1;
                    }
                    break;

                default:
                    // Escape every other regex metacharacter; '/' is literal in .NET regex.
                    if (c is '.' or '+' or '(' or ')' or '{' or '}' or '|' or '^' or '$' or '\\')
                        sb.Append('\\');
                    sb.Append(c);
                    i++;
                    break;
            }
        }

        sb.Append('$');
        return sb.ToString();
    }

    /// <summary>
    /// Returns the index of the ']' that closes the character class opened at
    /// <paramref name="open"/>, or -1 if the class is never closed. A leading '!'/'^'
    /// negation and a ']' as the first member are handled as literals (bash semantics).
    /// </summary>
    private static int FindClassEnd(string p, int open)
    {
        int i = open + 1;
        if (i < p.Length && (p[i] == '!' || p[i] == '^'))
            i++;
        if (i < p.Length && p[i] == ']')
            i++;
        for (; i < p.Length; i++)
            if (p[i] == ']')
                return i;
        return -1;
    }
}
