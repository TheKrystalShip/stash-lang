using System.Collections.Generic;
using System.Text;

namespace Stash.Common;

/// <summary>
/// Splits a command string into a program name and arguments,
/// handling double-quoted and single-quoted strings.
/// </summary>
public static class CommandParser
{
    /// <summary>
    /// Splits a raw command string into tokens, tracking whether each token
    /// was (at least partially) quoted. Quoting suppresses glob expansion.
    /// </summary>
    /// <returns>
    /// List of <c>(Token, WasQuoted)</c> pairs. The first entry is the program;
    /// the rest are arguments. <c>WasQuoted</c> is <c>true</c> when any portion
    /// of the token was inside single or double quotes.
    /// </returns>
    public static List<(string Token, bool WasQuoted)> ParseWithQuotedFlags(string command)
    {
        var tokens = new List<(string, bool)>();
        var current = new StringBuilder();
        bool wasQuoted = false;
        int i = 0;

        while (i < command.Length)
        {
            char c = command[i];

            if (c == '"')
            {
                wasQuoted = true;
                i++; // skip opening quote
                while (i < command.Length && command[i] != '"')
                {
                    if (command[i] == '\\' && i + 1 < command.Length)
                    {
                        // Preserve escape sequences for the target program
                        current.Append(command[i]);
                        current.Append(command[i + 1]);
                        i += 2;
                    }
                    else
                    {
                        current.Append(command[i]);
                        i++;
                    }
                }
                if (i < command.Length)
                    i++; // skip closing quote
            }
            else if (c == '\'')
            {
                wasQuoted = true;
                i++; // skip opening quote
                while (i < command.Length && command[i] != '\'')
                {
                    current.Append(command[i]);
                    i++;
                }
                if (i < command.Length)
                    i++; // skip closing quote
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add((current.ToString(), wasQuoted));
                    current.Clear();
                    wasQuoted = false;
                }
                i++;
            }
            else
            {
                current.Append(c);
                i++;
            }
        }

        if (current.Length > 0)
            tokens.Add((current.ToString(), wasQuoted));

        return tokens;
    }

    /// <summary>
    /// Splits a raw command string into tokens.
    /// The first token is the program name; the rest are arguments.
    /// Double-quoted and single-quoted strings are treated as single tokens
    /// with the quotes stripped. Backslash escapes within double quotes
    /// are preserved (the program receives the literal backslash sequence).
    /// </summary>
    public static (string Program, List<string> Arguments) Parse(string command)
    {
        var parsed = ParseWithQuotedFlags(command);
        if (parsed.Count == 0)
            return ("", new List<string>());

        string program = parsed[0].Token;
        var args = new List<string>(parsed.Count - 1);
        for (int i = 1; i < parsed.Count; i++)
            args.Add(parsed[i].Token);
        return (program, args);
    }
}
