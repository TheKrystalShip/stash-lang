using System;
using System.Collections.Generic;

namespace Stash.Cli.Completion;

internal static class SmartCaseMatcher
{
    /// <summary>
    /// Returns true if <paramref name="candidate"/> starts with <paramref name="prefix"/>
    /// using smart-case matching: if <paramref name="prefix"/> contains any uppercase letter,
    /// the match is case-sensitive; otherwise it is case-insensitive.
    /// </summary>
    public static bool Matches(string prefix, string candidate)
    {
        if (prefix.Length == 0)
            return true;
        if (candidate.Length < prefix.Length)
            return false;

        bool caseSensitive = HasUpper(prefix);
        StringComparison comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return candidate.StartsWith(prefix, comparison);
    }

    /// <summary>
    /// Computes the longest common prefix of <paramref name="strings"/>.
    /// When <paramref name="caseSensitive"/> is false, characters are compared
    /// case-insensitively but the returned prefix preserves the casing of the
    /// first string in the sequence.
    /// Returns <see cref="string.Empty"/> if the sequence is empty.
    /// </summary>
    public static string LongestCommonPrefix(IEnumerable<string> strings, bool caseSensitive)
    {
        string? first = null;
        int lcpLen = int.MaxValue;

        foreach (string s in strings)
        {
            if (first is null)
            {
                first = s;
                lcpLen = s.Length;
                continue;
            }

            int len = Math.Min(lcpLen, s.Length);
            int common = 0;
            while (common < len)
            {
                char c1 = first[common];
                char c2 = s[common];
                bool match = caseSensitive
                    ? c1 == c2
                    : char.ToLowerInvariant(c1) == char.ToLowerInvariant(c2);
                if (!match) break;
                common++;
            }
            lcpLen = common;
        }

        if (first is null)
            return string.Empty;
        return first[..lcpLen];
    }

    /// <summary>Returns true if <paramref name="s"/> contains at least one uppercase letter.</summary>
    public static bool HasUpper(string s)
    {
        foreach (char c in s)
            if (char.IsUpper(c)) return true;
        return false;
    }
}
