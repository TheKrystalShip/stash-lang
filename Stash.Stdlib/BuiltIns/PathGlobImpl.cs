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
        return Regex.IsMatch(path, regex);
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

        sb.Append('$');
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
    /// Escapes regex metacharacters inside a character class, preserving the class
    /// syntax characters <c>-</c>, <c>^</c>, <c>]</c>, and <c>\</c> as-is because
    /// .NET's regex engine interprets them correctly inside <c>[…]</c>.
    /// </summary>
    private static string EscapeClassContents(string classContent)
    {
        // Inside a character class the only truly special chars are ] ^ - \.
        // . * + ? ( ) { } | are NOT special inside a class, so no escaping needed.
        // We pass the content through verbatim — the caller has already validated
        // the class has a closing ']', so the only risk is a stray backslash.
        return classContent;
    }
}
