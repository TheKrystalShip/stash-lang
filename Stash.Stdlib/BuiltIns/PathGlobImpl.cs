namespace Stash.Stdlib.BuiltIns;

using System;
using System.Text;
using System.Text.RegularExpressions;
using Stash.Runtime;

/// <summary>
/// Glob-to-regex translator with bash <c>[[ ]]</c> semantics (shopt -s globstar).
/// Not Stash-visible; used only by <see cref="PathBuiltIns.Match"/>.
/// </summary>
internal static class PathGlobImpl
{
    // Extglob openers: any of @!+?* immediately followed by '('.
    private static readonly char[] ExtglobPrefixes = { '@', '!', '+', '?', '*' };

    /// <summary>
    /// Returns true iff <paramref name="path"/> matches <paramref name="pattern"/>
    /// under bash <c>[[ ]]</c> globstar semantics.
    /// </summary>
    /// <exception cref="RuntimeError">
    /// Thrown when <paramref name="pattern"/> contains an extglob construct
    /// (<c>@(</c>, <c>!(</c>, <c>+(</c>, <c>?(</c>, <c>*(</c>).
    /// </exception>
    internal static bool Matches(string path, string pattern)
    {
        string regex = GlobToRegex(pattern);
        try
        {
            return Regex.IsMatch(path, regex, RegexOptions.Singleline | RegexOptions.CultureInvariant);
        }
        catch (RegexParseException)
        {
            // Malformed patterns fall back to literal equality (Decision Log: never throw).
            return path == pattern;
        }
    }

    /// <summary>
    /// Translates a bash glob pattern to an anchored, case-sensitive regex.
    /// </summary>
    private static string GlobToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        int i = 0;

        while (i < pattern.Length)
        {
            char c = pattern[i];

            // --- extglob detection (must precede * and ? handling) ---
            if (c is '@' or '!' or '+' or '?' or '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '(')
                {
                    throw new RuntimeError(
                        $"path.match: extglob is not supported (got '{c}(')");
                }
            }

            switch (c)
            {
                case '*':
                    // ** and bare * both match any character sequence (including '/').
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        sb.Append(".*");
                        i += 2;
                    }
                    else
                    {
                        sb.Append(".*");
                        i++;
                    }
                    break;

                case '?':
                    // Matches exactly one character (any, including '/').
                    sb.Append('.');
                    i++;
                    break;

                case '[':
                    // Character class. Walk until the closing ']'.
                    // If no ']' is found, treat '[' as a literal.
                    int classEnd = FindClassEnd(pattern, i);
                    if (classEnd < 0)
                    {
                        // Malformed — emit literal '\['.
                        sb.Append(@"\[");
                        i++;
                    }
                    else
                    {
                        // Consume the class contents including '[' and ']'.
                        string classContent = pattern.Substring(i + 1, classEnd - i - 1);

                        // Rewrite leading '!' as '^' for negated classes.
                        if (classContent.Length > 0 && classContent[0] == '!')
                            classContent = "^" + classContent.Substring(1);

                        sb.Append('[');
                        // Escape any regex special chars inside the class that aren't
                        // part of the class syntax (i.e. not '^', '-', ']').
                        sb.Append(EscapeClassContents(classContent));
                        sb.Append(']');
                        i = classEnd + 1;
                    }
                    break;

                case '\\':
                    // Escape sequence: '\x' → literal x.
                    if (i + 1 < pattern.Length)
                    {
                        char escaped = pattern[i + 1];
                        sb.Append(Regex.Escape(escaped.ToString()));
                        i += 2;
                    }
                    else
                    {
                        // Trailing '\' — treat as literal backslash (malformed, but mirror bash).
                        sb.Append(@"\\");
                        i++;
                    }
                    break;

                default:
                    // Literal character — escape any regex metachar.
                    sb.Append(Regex.Escape(c.ToString()));
                    i++;
                    break;
            }
        }

        sb.Append(@"\z");
        return sb.ToString();
    }

    /// <summary>
    /// Finds the index of the closing <c>]</c> for a character class starting at
    /// <paramref name="start"/> (which points to the opening <c>[</c>).
    /// Returns -1 if no closing <c>]</c> is found (malformed class).
    /// </summary>
    private static int FindClassEnd(string pattern, int start)
    {
        // Inside [...], a leading ']' after '[' or '[^' is literal (bash rule).
        int i = start + 1;

        // Skip leading '^' or '!'
        if (i < pattern.Length && (pattern[i] == '^' || pattern[i] == '!'))
            i++;

        // A ']' immediately here is a literal member of the class, not the end.
        if (i < pattern.Length && pattern[i] == ']')
            i++;

        while (i < pattern.Length)
        {
            if (pattern[i] == ']')
                return i;
            i++;
        }

        return -1; // unclosed
    }

    /// <summary>
    /// Escapes characters inside a bash glob character class so that the result
    /// is safe to embed in a .NET regex <c>[…]</c> while preserving bash semantics.
    ///
    /// Bash treats <c>\x</c> inside a class as the two literal characters <c>\</c>
    /// and <c>x</c>; .NET regex would interpret <c>\d</c>, <c>\s</c>, <c>\w</c> etc.
    /// as shorthand character classes.  We therefore escape every <c>\</c> as
    /// <c>\\</c> (a regex-literal backslash).
    ///
    /// A leading <c>]</c> in the class content is a literal member in bash but
    /// would close the class early in .NET regex; we escape it as <c>\]</c>.
    ///
    /// <c>^</c> and <c>-</c> are left as-is because the caller has already
    /// validated their positions (leading <c>!</c>→<c>^</c> rewrite happened
    /// before this call; ranges like <c>A-Z</c> need the bare <c>-</c>).
    /// </summary>
    private static string EscapeClassContents(string classContent)
    {
        // Order matters: escape backslashes first so we don't double-escape
        // the backslashes we introduce when escaping ']'.
        string result = classContent.Replace("\\", "\\\\");

        // If the content starts with ']' (or '^]' for negated classes), that ']'
        // is a literal member in bash but would close the .NET class immediately.
        // Escape it to '\]'.
        if (result.Length > 0 && result[0] == ']')
            result = @"\]" + result.Substring(1);
        else if (result.Length > 1 && result[0] == '^' && result[1] == ']')
            result = @"^\]" + result.Substring(2);

        return result;
    }
}
