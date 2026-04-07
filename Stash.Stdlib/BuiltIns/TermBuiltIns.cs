namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Stash.Runtime;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

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
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("term");

        // Color constants — string values accepted by term.color and term.style for foreground coloring.
        ns.Constant("BLACK", "black", "string", "black");
        ns.Constant("RED", "red", "string", "red");
        ns.Constant("GREEN", "green", "string", "green");
        ns.Constant("YELLOW", "yellow", "string", "yellow");
        ns.Constant("BLUE", "blue", "string", "blue");
        ns.Constant("MAGENTA", "magenta", "string", "magenta");
        ns.Constant("CYAN", "cyan", "string", "cyan");
        ns.Constant("WHITE", "white", "string", "white");
        ns.Constant("GRAY", "gray", "string", "gray");

        // term.color(text, color) — Wraps 'text' in ANSI escape codes to display it in the specified foreground color.
        //   'color' must be one of the term color constants (e.g. term.RED) or their string equivalents.
        ns.Function("color", [Param("text", "string"), Param("color", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var text = SvArgs.String(args, 0, "term.color");
            var color = SvArgs.String(args, 1, "term.color");
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
            return StashValue.FromObj($"\x1b[{code}m{text}\x1b[0m");
        }, returnType: "string");

        // term.bold(text) — Wraps 'text' in ANSI bold escape codes. Returns the styled string.
        ns.Function("bold", [Param("text", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var text = SvArgs.String(args, 0, "term.bold");
            return StashValue.FromObj($"\x1b[1m{text}\x1b[0m");
        }, returnType: "string");

        // term.dim(text) — Wraps 'text' in ANSI dim (faint) escape codes. Returns the styled string.
        ns.Function("dim", [Param("text", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var text = SvArgs.String(args, 0, "term.dim");
            return StashValue.FromObj($"\x1b[2m{text}\x1b[0m");
        }, returnType: "string");

        // term.underline(text) — Wraps 'text' in ANSI underline escape codes. Returns the styled string.
        ns.Function("underline", [Param("text", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var text = SvArgs.String(args, 0, "term.underline");
            return StashValue.FromObj($"\x1b[4m{text}\x1b[0m");
        }, returnType: "string");

        // term.style(text, opts) — Applies multiple styles to 'text' using an options dict.
        //   Supported keys: "bold" (bool), "dim" (bool), "underline" (bool), "color" (string).
        //   Returns the ANSI-styled string, or 'text' unchanged if no options are set.
        ns.Function("style", [Param("text", "string"), Param("opts", "dict")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var text = SvArgs.String(args, 0, "term.style");
            var opts = SvArgs.Dict(args, 1, "term.style");
            var codes = new List<string>();

            var boldVal = opts.Get("bold").ToObject();
            if (boldVal is true)
            {
                codes.Add("1");
            }

            var dimVal = opts.Get("dim").ToObject();
            if (dimVal is true)
            {
                codes.Add("2");
            }

            var underlineVal = opts.Get("underline").ToObject();
            if (underlineVal is true)
            {
                codes.Add("4");
            }

            var colorVal = opts.Get("color").ToObject();
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
                return StashValue.FromObj(text);
            }

            return StashValue.FromObj($"\x1b[{string.Join(";", codes)}m{text}\x1b[0m");
        }, returnType: "string");

        // term.strip(text) — Removes all ANSI escape sequences from 'text'. Returns the plain string.
        ns.Function("strip", [Param("text", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var text = SvArgs.String(args, 0, "term.strip");
            return StashValue.FromObj(Regex.Replace(text, @"\x1b\[[0-9;]*m", ""));
        }, returnType: "string");

        // term.width() — Returns the current terminal column width. Falls back to 80 if unavailable.
        ns.Function("width", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            try { return StashValue.FromInt((long)Console.WindowWidth); }
            catch { return StashValue.FromInt(80L); }
        }, returnType: "int");

        // term.isInteractive() — Returns true if stdin is connected to an interactive terminal (not redirected).
        ns.Function("isInteractive", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            try { return StashValue.FromBool(!Console.IsInputRedirected); }
            catch { return StashValue.False; }
        }, returnType: "bool");

        // term.clear() — Clears the terminal screen using ANSI escape sequences. Returns null.
        ns.Function("clear", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> _) =>
        {
            ctx.Output.Write("\x1b[2J\x1b[H");
            return StashValue.Null;
        });

        // term.table(rows [, headers]) — Formats a two-dimensional array as an ASCII table string.
        //   Optional 'headers' array is rendered as a separate header row with a divider line.
        ns.Function("table", [Param("rows", "array"), Param("headers", "array")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2) throw new RuntimeError("'term.table' expects 1 or 2 arguments.");
            var rows = SvArgs.StashList(args, 0, "term.table");
            List<StashValue>? headers = null;
            if (args.Length == 2)
            {
                var headersObj = args[1].ToObject();
                if (headersObj is List<StashValue> h) headers = h;
            }

            var allRows = new List<string[]>();
            if (headers != null)
            {
                allRows.Add(headers.Select(x => RuntimeValues.Stringify(x.ToObject())).ToArray());
            }

            foreach (StashValue row in rows)
            {
                if (row.ToObject() is List<StashValue> cols)
                {
                    allRows.Add(cols.Select(c => RuntimeValues.Stringify(c.ToObject())).ToArray());
                }
                else
                {
                    throw new RuntimeError("Each row in 'term.table' must be an array.");
                }
            }

            if (allRows.Count == 0)
            {
                return StashValue.FromObj("");
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

            return StashValue.FromObj(sb.ToString());
        }, returnType: "string", isVariadic: true);

        return ns.Build();
    }
}
