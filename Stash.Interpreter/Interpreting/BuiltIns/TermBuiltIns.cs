namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the <c>term</c> namespace built-in functions for terminal styling and introspection.
/// </summary>
/// <remarks>
/// <para>
/// Provides ANSI escape code helpers: foreground coloring (<c>term.color</c>), text weight
/// (<c>term.bold</c>, <c>term.dim</c>), underlining (<c>term.underline</c>), combined styling
/// (<c>term.style</c>), stripping ANSI codes (<c>term.strip</c>), tabular formatting
/// (<c>term.table</c>), and screen clearing (<c>term.clear</c>).
/// </para>
/// <para>
/// Also exposes color name constants (<c>term.BLACK</c>, <c>term.RED</c>, <c>term.GREEN</c>,
/// <c>term.YELLOW</c>, <c>term.BLUE</c>, <c>term.MAGENTA</c>, <c>term.CYAN</c>,
/// <c>term.WHITE</c>, <c>term.GRAY</c>) and terminal introspection helpers
/// (<c>term.width</c>, <c>term.isInteractive</c>).
/// </para>
/// </remarks>
public static class TermBuiltIns
{
    /// <summary>
    /// Registers all <c>term</c> namespace functions and color constants into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Stash.Interpreting.Environment"/> to register functions in.</param>
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var term = new StashNamespace("term");

        // Color constants — string values accepted by term.color and term.style for foreground coloring.
        term.Define("BLACK", "black");
        term.Define("RED", "red");
        term.Define("GREEN", "green");
        term.Define("YELLOW", "yellow");
        term.Define("BLUE", "blue");
        term.Define("MAGENTA", "magenta");
        term.Define("CYAN", "cyan");
        term.Define("WHITE", "white");
        term.Define("GRAY", "gray");

        // term.color(text, color) — Wraps 'text' in ANSI escape codes to display it in the specified foreground color.
        //   'color' must be one of the term color constants (e.g. term.RED) or their string equivalents.
        term.Define("color", new BuiltInFunction("term.color", 2, (_, args) =>
        {
            if (args[0] is not string text)
            {
                throw new RuntimeError("First argument to 'term.color' must be a string.");
            }

            if (args[1] is not string color)
            {
                throw new RuntimeError("Second argument to 'term.color' must be a string.");
            }

            string code = color.ToLowerInvariant() switch
            {
                "black" => "30",
                "red" => "31",
                "green" => "32",
                "yellow" => "33",
                "blue" => "34",
                "magenta" => "35",
                "cyan" => "36",
                "white" => "37",
                "gray" or "grey" => "90",
                _ => throw new RuntimeError($"Unknown color '{color}'. Use term color constants: term.BLACK, term.RED, term.GREEN, term.YELLOW, term.BLUE, term.MAGENTA, term.CYAN, term.WHITE, term.GRAY.")
            };
            return $"\x1b[{code}m{text}\x1b[0m";
        }));

        // term.bold(text) — Wraps 'text' in ANSI bold escape codes. Returns the styled string.
        term.Define("bold", new BuiltInFunction("term.bold", 1, (_, args) =>
        {
            if (args[0] is not string text)
            {
                throw new RuntimeError("Argument to 'term.bold' must be a string.");
            }

            return $"\x1b[1m{text}\x1b[0m";
        }));

        // term.dim(text) — Wraps 'text' in ANSI dim (faint) escape codes. Returns the styled string.
        term.Define("dim", new BuiltInFunction("term.dim", 1, (_, args) =>
        {
            if (args[0] is not string text)
            {
                throw new RuntimeError("Argument to 'term.dim' must be a string.");
            }

            return $"\x1b[2m{text}\x1b[0m";
        }));

        // term.underline(text) — Wraps 'text' in ANSI underline escape codes. Returns the styled string.
        term.Define("underline", new BuiltInFunction("term.underline", 1, (_, args) =>
        {
            if (args[0] is not string text)
            {
                throw new RuntimeError("Argument to 'term.underline' must be a string.");
            }

            return $"\x1b[4m{text}\x1b[0m";
        }));

        // term.style(text, opts) — Applies multiple styles to 'text' using an options dict.
        //   Supported keys: "bold" (bool), "dim" (bool), "underline" (bool), "color" (string).
        //   Returns the ANSI-styled string, or 'text' unchanged if no options are set.
        term.Define("style", new BuiltInFunction("term.style", 2, (_, args) =>
        {
            if (args[0] is not string text)
            {
                throw new RuntimeError("First argument to 'term.style' must be a string.");
            }

            if (args[1] is not StashDictionary opts)
            {
                throw new RuntimeError("Second argument to 'term.style' must be a dict.");
            }

            var codes = new List<string>();

            var boldVal = opts.Get("bold");
            if (boldVal is true)
            {
                codes.Add("1");
            }

            var dimVal = opts.Get("dim");
            if (dimVal is true)
            {
                codes.Add("2");
            }

            var underlineVal = opts.Get("underline");
            if (underlineVal is true)
            {
                codes.Add("4");
            }

            var colorVal = opts.Get("color");
            if (colorVal is string color)
            {
                string colorCode = color.ToLowerInvariant() switch
                {
                    "black" => "30",
                    "red" => "31",
                    "green" => "32",
                    "yellow" => "33",
                    "blue" => "34",
                    "magenta" => "35",
                    "cyan" => "36",
                    "white" => "37",
                    "gray" or "grey" => "90",
                    _ => throw new RuntimeError($"Unknown color '{color}'. Use term color constants: term.BLACK, term.RED, etc.")
                };
                codes.Add(colorCode);
            }

            if (codes.Count == 0)
            {
                return text;
            }

            return $"\x1b[{string.Join(";", codes)}m{text}\x1b[0m";
        }));

        // term.strip(text) — Removes all ANSI escape sequences from 'text'. Returns the plain string.
        term.Define("strip", new BuiltInFunction("term.strip", 1, (_, args) =>
        {
            if (args[0] is not string text)
            {
                throw new RuntimeError("Argument to 'term.strip' must be a string.");
            }

            return Regex.Replace(text, @"\x1b\[[0-9;]*m", "");
        }));

        // term.width() — Returns the current terminal column width. Falls back to 80 if unavailable.
        term.Define("width", new BuiltInFunction("term.width", 0, (_, _) =>
        {
            try { return (long)Console.WindowWidth; }
            catch { return 80L; }
        }));

        // term.isInteractive() — Returns true if stdin is connected to an interactive terminal (not redirected).
        term.Define("isInteractive", new BuiltInFunction("term.isInteractive", 0, (_, _) =>
        {
            try { return !Console.IsInputRedirected; }
            catch { return (object?)false; }
        }));

        // term.clear() — Clears the terminal screen using ANSI escape sequences. Returns null.
        term.Define("clear", new BuiltInFunction("term.clear", 0, (interp, _) =>
        {
            interp.Output.Write("\x1b[2J\x1b[H");
            return null;
        }));

        // term.table(rows [, headers]) — Formats a two-dimensional array as an ASCII table string.
        //   Optional 'headers' array is rendered as a separate header row with a divider line.
        term.Define("table", new BuiltInFunction("term.table", -1, (_, args) =>
        {
            if (args.Count < 1 || args.Count > 2)
            {
                throw new RuntimeError("'term.table' expects 1 or 2 arguments.");
            }

            if (args[0] is not List<object?> rows)
            {
                throw new RuntimeError("First argument to 'term.table' must be an array of arrays.");
            }

            List<object?>? headers = null;
            if (args.Count == 2 && args[1] is List<object?> h)
            {
                headers = h;
            }

            var allRows = new List<string[]>();
            if (headers != null)
            {
                allRows.Add(headers.Select(x => RuntimeValues.Stringify(x)).ToArray());
            }

            foreach (var row in rows)
            {
                if (row is not List<object?> cols)
                {
                    throw new RuntimeError("Each row in 'term.table' must be an array.");
                }

                allRows.Add(cols.Select(c => RuntimeValues.Stringify(c)).ToArray());
            }

            if (allRows.Count == 0)
            {
                return "";
            }

            int colCount = allRows.Max(r => r.Length);
            var widths = new int[colCount];
            foreach (var row in allRows)
            {
                for (int i = 0; i < row.Length; i++)
                {
                    if (row[i].Length > widths[i])
                    {
                        widths[i] = row[i].Length;
                    }
                }
            }

            var sb = new System.Text.StringBuilder();
            string separator = "+" + string.Join("+", widths.Select(w => new string('-', w + 2))) + "+";

            sb.AppendLine(separator);
            for (int r = 0; r < allRows.Count; r++)
            {
                var row = allRows[r];
                sb.Append('|');
                for (int c = 0; c < colCount; c++)
                {
                    string cell = c < row.Length ? row[c] : "";
                    sb.Append(' ');
                    sb.Append(cell.PadRight(widths[c]));
                    sb.Append(" |");
                }
                sb.AppendLine();
                if (r == 0 && headers != null)
                {
                    sb.AppendLine(separator);
                }
            }
            sb.Append(separator);

            return sb.ToString();
        }));

        globals.Define("term", term);
    }
}
