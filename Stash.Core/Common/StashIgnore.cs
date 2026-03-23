using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Stash.Common;

/// <summary>
/// Parses and evaluates <c>.stashignore</c> pattern files, determining which relative file
/// paths should be excluded from packaging and publishing operations.
/// </summary>
/// <remarks>
/// <para>
/// The pattern syntax mirrors <c>.gitignore</c> conventions:
/// <list type="bullet">
///   <item>Blank lines and lines beginning with <c>#</c> are ignored as comments.</item>
///   <item>A leading <c>!</c> negates a pattern, re-including previously excluded paths.</item>
///   <item>A trailing <c>/</c> restricts the pattern to directories only.</item>
///   <item>A leading <c>/</c> anchors the pattern to the project root.</item>
///   <item><c>*</c> matches any sequence of non-separator characters.</item>
///   <item><c>**</c> matches zero or more path segments (any directory depth).</item>
///   <item><c>?</c> matches a single non-separator character.</item>
/// </list>
/// </para>
/// <para>
/// Before user patterns are applied, <see cref="_defaultPatterns"/> are prepended so that
/// commonly ignored paths (e.g. <c>.git/</c>, <c>stashes/</c>, <c>stash-lock.json</c>)
/// are always excluded unless explicitly negated by a user pattern.
/// </para>
/// </remarks>
public sealed class StashIgnore
{
    /// <summary>
    /// The built-in patterns that are always applied before any user-supplied patterns,
    /// ensuring essential directories and files are excluded from published packages by default.
    /// </summary>
    private static readonly string[] _defaultPatterns =
    [
        ".git/",
        "stashes/",
        "stash-lock.json",
        ".env",
    ];

    /// <summary>
    /// The ordered list of compiled rules derived from both <see cref="_defaultPatterns"/>
    /// and any user-supplied patterns. Each tuple contains the compiled <see cref="Regex"/>,
    /// a flag indicating whether the rule is a negation (re-include), and a flag indicating
    /// whether the pattern targets directories only.
    /// </summary>
    private readonly List<(Regex Pattern, bool Negated, bool DirectoryOnly)> _rules;

    /// <summary>
    /// Loads a <see cref="StashIgnore"/> from the <c>.stashignore</c> file in the given
    /// directory, falling back to only the default patterns when no such file exists.
    /// </summary>
    /// <param name="directoryPath">
    /// The directory in which to look for a <c>.stashignore</c> file.
    /// </param>
    /// <returns>
    /// A <see cref="StashIgnore"/> instance initialised with <see cref="_defaultPatterns"/>
    /// plus any patterns read from <c>.stashignore</c>, or with only the default patterns
    /// if the file does not exist.
    /// </returns>
    public static StashIgnore Load(string directoryPath)
    {
        string ignoreFile = Path.Combine(directoryPath, ".stashignore");
        if (File.Exists(ignoreFile))
        {
            string[] lines = File.ReadAllLines(ignoreFile);
            return new StashIgnore(lines);
        }
        return new StashIgnore([]);
    }

    /// <summary>
    /// Creates a new <see cref="StashIgnore"/> with the built-in default patterns followed
    /// by the given user-supplied patterns.
    /// </summary>
    /// <param name="patterns">
    /// The user-provided patterns to append after the built-in defaults. Blank lines and
    /// comment lines are silently ignored during <see cref="AddRule"/>.
    /// </param>
    public StashIgnore(IEnumerable<string> patterns)
    {
        _rules = new List<(Regex, bool, bool)>();

        foreach (string defaultPattern in _defaultPatterns)
        {
            AddRule(defaultPattern);
        }

        foreach (string pattern in patterns)
        {
            AddRule(pattern);
        }
    }

    /// <summary>
    /// Determines whether the given relative path is excluded by the current set of rules.
    /// </summary>
    /// <param name="relativePath">
    /// The path relative to the project root to test, using either forward or backward
    /// slashes. A leading slash is stripped before evaluation.
    /// </param>
    /// <returns>
    /// <c>true</c> if the path is matched by an excluding rule (and not subsequently
    /// re-included by a negating rule); <c>false</c> otherwise.
    /// </returns>
    /// <remarks>
    /// Rules are applied in declaration order; later rules override earlier ones. For
    /// directory-only rules, every directory prefix of the path is tested so that files
    /// nested deep within an excluded directory are also excluded.
    /// </remarks>
    public bool IsExcluded(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');

        bool excluded = false;
        foreach (var (pattern, negated, directoryOnly) in _rules)
        {
            if (directoryOnly)
            {
                // A path is inside a directory if any directory prefix of that path matches the pattern.
                // E.g. ".git/config" has prefixes ".git" and ".git/config" — ".git" matches pattern for ".git/"
                string[] segments = normalized.Split('/');
                bool matchesDir = false;
                for (int i = 1; i <= segments.Length; i++)
                {
                    string prefix = string.Join("/", segments, 0, i);
                    if (pattern.IsMatch(prefix))
                    {
                        matchesDir = true;
                        break;
                    }
                }
                if (matchesDir)
                {
                    excluded = !negated;
                }
            }
            else
            {
                if (pattern.IsMatch(normalized))
                {
                    excluded = !negated;
                }
            }
        }

        return excluded;
    }

    /// <summary>
    /// Filters a collection of relative paths, returning only those not excluded by the
    /// current rules.
    /// </summary>
    /// <param name="relativePaths">The candidate relative paths to filter.</param>
    /// <returns>
    /// A new <see cref="List{T}"/> containing only the paths for which
    /// <see cref="IsExcluded"/> returns <c>false</c>, preserving input order.
    /// </returns>
    public List<string> Filter(IEnumerable<string> relativePaths)
    {
        var result = new List<string>();
        foreach (string path in relativePaths)
        {
            if (!IsExcluded(path))
            {
                result.Add(path);
            }
        }
        return result;
    }

    /// <summary>
    /// Parses a single raw pattern line and, if valid, appends a compiled rule to
    /// <see cref="_rules"/>.
    /// </summary>
    /// <param name="rawPattern">
    /// The raw pattern string from the ignore file. Blank lines, lines consisting solely of
    /// whitespace, and lines beginning with <c>#</c> are silently skipped.
    /// </param>
    /// <remarks>
    /// The method normalises the pattern (strips trailing whitespace, detects negation and
    /// directory-only flags, strips leading/trailing slashes) and delegates glob-to-regex
    /// translation to <see cref="GlobToRegex"/>.
    /// </remarks>
    private void AddRule(string rawPattern)
    {
        string pattern = rawPattern.TrimEnd();

        // Skip empty lines and comments
        if (string.IsNullOrEmpty(pattern) || pattern.StartsWith('#'))
        {
            return;
        }

        bool negated = pattern.StartsWith('!');
        if (negated)
        {
            pattern = pattern.Substring(1);
        }

        if (string.IsNullOrEmpty(pattern))
        {
            return;
        }

        bool directoryOnly = pattern.EndsWith('/');

        // Normalize forward slashes
        pattern = pattern.Replace('\\', '/');

        // Determine if anchored to root
        bool anchored = pattern.StartsWith('/');
        if (anchored)
        {
            pattern = pattern.TrimStart('/');
        }

        // Strip trailing slash for regex building (we track directoryOnly separately)
        if (directoryOnly)
        {
            pattern = pattern.TrimEnd('/');
        }

        string regexStr = GlobToRegex(pattern, anchored);
        var regex = new Regex(regexStr, RegexOptions.Compiled | RegexOptions.CultureInvariant);

        _rules.Add((regex, negated, directoryOnly));
    }

    /// <summary>
    /// Converts a glob pattern to an equivalent regular expression string.
    /// </summary>
    /// <param name="pattern">
    /// The glob pattern with leading slash, trailing slash, and negation prefix already
    /// stripped. May contain <c>*</c>, <c>**</c>, and <c>?</c> wildcards.
    /// </param>
    /// <param name="anchored">
    /// <c>true</c> if the pattern should match only from the beginning of the path (i.e.
    /// the original pattern started with <c>/</c> or contained a <c>/</c>); <c>false</c>
    /// if the pattern should match anywhere in the path hierarchy.
    /// </param>
    /// <returns>A regular expression string suitable for constructing a <see cref="Regex"/>.</returns>
    /// <remarks>
    /// <para>
    /// Wildcard translation rules:
    /// <list type="bullet">
    ///   <item><c>**</c> followed by an optional <c>/</c> at end-of-pattern → <c>.*</c>.</item>
    ///   <item><c>**</c> followed by <c>/</c> and more content → <c>(?:.+/)?</c>.</item>
    ///   <item><c>*</c> → <c>[^/]*</c> (any characters except the path separator).</item>
    ///   <item><c>?</c> → <c>[^/]</c> (one character except the path separator).</item>
    ///   <item>All other characters are regex-escaped.</item>
    /// </list>
    /// </para>
    /// </remarks>
    private static string GlobToRegex(string pattern, bool anchored)
    {
        var sb = new System.Text.StringBuilder();

        if (anchored)
        {
            sb.Append('^');
        }
        else
        {
            // Not anchored: match anywhere — either at root or after a slash
            // We'll handle this by either matching from start or after a directory separator
            if (!pattern.Contains('/'))
            {
                // Simple filename pattern — matches in any directory
                sb.Append("(?:^|.*/)");
            }
            else
            {
                // Pattern contains a slash — treat as anchored from root
                sb.Append('^');
            }
        }

        int i = 0;
        while (i < pattern.Length)
        {
            if (i + 1 < pattern.Length && pattern[i] == '*' && pattern[i + 1] == '*')
            {
                // ** — matches zero or more path segments
                int j = i + 2;
                // consume optional trailing slash
                if (j < pattern.Length && pattern[j] == '/')
                {
                    j++;
                }

                if (j >= pattern.Length)
                {
                    // ** at end of pattern (e.g. "docs/**") — match any remaining content
                    sb.Append(".*");
                }
                else
                {
                    // ** in middle or at start with more path following (e.g. "**/test", "src/**/foo")
                    sb.Append("(?:.+/)?");
                }
                i = j;
            }
            else if (pattern[i] == '*')
            {
                // * — matches any char except /
                sb.Append("[^/]*");
                i++;
            }
            else if (pattern[i] == '?')
            {
                sb.Append("[^/]");
                i++;
            }
            else
            {
                sb.Append(Regex.Escape(pattern[i].ToString()));
                i++;
            }
        }

        sb.Append('$');

        return sb.ToString();
    }
}
