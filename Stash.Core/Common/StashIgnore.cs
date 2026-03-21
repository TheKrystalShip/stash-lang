using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Stash.Common;

public sealed class StashIgnore
{
    private static readonly string[] _defaultPatterns =
    [
        ".git/",
        "stashes/",
        "stash-lock.json",
        ".env",
    ];

    private readonly List<(Regex Pattern, bool Negated, bool DirectoryOnly)> _rules;

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
