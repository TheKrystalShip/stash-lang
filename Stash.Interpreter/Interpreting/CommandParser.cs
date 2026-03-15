using System.Collections.Generic;
using System.Text;

namespace Stash.Interpreting;

/// <summary>
/// Splits a command string into a program name and arguments,
/// handling double-quoted and single-quoted strings.
/// </summary>
public static class CommandParser
{
    /// <summary>
    /// Splits a raw command string into tokens.
    /// The first token is the program name; the rest are arguments.
    /// Double-quoted and single-quoted strings are treated as single tokens
    /// with the quotes stripped. Backslash escapes within double quotes
    /// are preserved (the program receives the literal backslash sequence).
    /// </summary>
    public static (string Program, List<string> Arguments) Parse(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        int i = 0;

        while (i < command.Length)
        {
            char c = command[i];

            if (c == '"')
            {
                // Double-quoted segment: collect until closing quote
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
                {
                    i++; // skip closing quote
                }
            }
            else if (c == '\'')
            {
                // Single-quoted segment: collect until closing quote (no escapes)
                i++; // skip opening quote
                while (i < command.Length && command[i] != '\'')
                {
                    current.Append(command[i]);
                    i++;
                }
                if (i < command.Length)
                {
                    i++; // skip closing quote
                }
            }
            else if (char.IsWhiteSpace(c))
            {
                // Whitespace: flush current token
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                i++;
            }
            else
            {
                current.Append(c);
                i++;
            }
        }

        // Flush last token
        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        if (tokens.Count == 0)
        {
            return ("", new List<string>());
        }

        string program = tokens[0];
        var args = tokens.Count > 1 ? tokens.GetRange(1, tokens.Count - 1) : new List<string>();
        return (program, args);
    }
}
