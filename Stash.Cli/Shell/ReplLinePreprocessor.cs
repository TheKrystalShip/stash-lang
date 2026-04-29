namespace Stash.Cli.Shell;

using System;
using System.Text;

/// <summary>
/// Applies REPL-only textual desugarings to a logical input line before it is lexed.
///
/// Currently handles:
///   <c>$?</c>  →  <c>process.lastExitCode()</c>
///
/// Replacements are only performed outside of string literals and comments.
/// </summary>
internal static class ReplLinePreprocessor
{
    private const string DollarQuestionReplacement = "process.lastExitCode()";

    /// <summary>
    /// Applies all REPL-only desugarings to <paramref name="line"/>.
    /// Returns the (possibly modified) string. If no desugarings apply the original
    /// string reference is returned unchanged (no allocation).
    /// </summary>
    public static string Apply(string line)
    {
        // Fast path: no '$?' present at all — avoid allocation.
        if (!line.Contains("$?", StringComparison.Ordinal))
            return line;

        var sb = new StringBuilder(line.Length + 32);
        var state = ParseState.Code;
        int i = 0;
        int len = line.Length;

        while (i < len)
        {
            char c = line[i];

            switch (state)
            {
                case ParseState.Code:
                    if (c == '/' && i + 1 < len && line[i + 1] == '/')
                    {
                        // Line comment — copy rest verbatim.
                        state = ParseState.InLineComment;
                        sb.Append(c);
                        i++;
                        break;
                    }

                    if (c == '/' && i + 1 < len && line[i + 1] == '*')
                    {
                        // Block comment start.
                        state = ParseState.InBlockComment;
                        sb.Append(c);
                        i++;
                        break;
                    }

                    if (c == '"')
                    {
                        state = ParseState.InDoubleQuote;
                        sb.Append(c);
                        i++;
                        break;
                    }

                    if (c == '\'')
                    {
                        state = ParseState.InSingleQuote;
                        sb.Append(c);
                        i++;
                        break;
                    }

                    if (c == '`')
                    {
                        state = ParseState.InBacktick;
                        sb.Append(c);
                        i++;
                        break;
                    }

                    // Check for $? substitution.
                    if (c == '$' && i + 1 < len && line[i + 1] == '?')
                    {
                        sb.Append(DollarQuestionReplacement);
                        i += 2; // skip '$' and '?'
                        break;
                    }

                    sb.Append(c);
                    i++;
                    break;

                case ParseState.InDoubleQuote:
                    if (c == '\\' && i + 1 < len)
                    {
                        // Escape sequence — copy both chars, skip next.
                        sb.Append(c);
                        sb.Append(line[i + 1]);
                        i += 2;
                        break;
                    }

                    if (c == '"')
                        state = ParseState.Code;

                    sb.Append(c);
                    i++;
                    break;

                case ParseState.InSingleQuote:
                    if (c == '\\' && i + 1 < len)
                    {
                        sb.Append(c);
                        sb.Append(line[i + 1]);
                        i += 2;
                        break;
                    }

                    if (c == '\'')
                        state = ParseState.Code;

                    sb.Append(c);
                    i++;
                    break;

                case ParseState.InBacktick:
                    if (c == '\\' && i + 1 < len)
                    {
                        sb.Append(c);
                        sb.Append(line[i + 1]);
                        i += 2;
                        break;
                    }

                    if (c == '`')
                        state = ParseState.Code;

                    sb.Append(c);
                    i++;
                    break;

                case ParseState.InLineComment:
                    // Everything until end-of-string is comment.
                    sb.Append(c);
                    i++;
                    break;

                case ParseState.InBlockComment:
                    if (c == '*' && i + 1 < len && line[i + 1] == '/')
                    {
                        sb.Append(c);
                        sb.Append('/');
                        i += 2;
                        state = ParseState.Code;
                        break;
                    }

                    sb.Append(c);
                    i++;
                    break;
            }
        }

        return sb.ToString();
    }

    private enum ParseState
    {
        Code,
        InDoubleQuote,
        InSingleQuote,
        InBacktick,
        InLineComment,
        InBlockComment,
    }
}
