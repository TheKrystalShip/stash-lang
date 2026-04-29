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

        // term.color(text, color [, bgColor]) — Wraps 'text' in ANSI escape codes for the specified foreground color.
        //   Accepts ANSI color names ("red", "brightcyan"), 256-color SGR fragments ("38;5;81"),
        //   24-bit hex colors ("#RRGGBB"), or empty string for no color.
        //   Optional 'bgColor' applies a background color.
        ns.Function("color", [Param("text", "string"), Param("color", "string"), Param("bgColor", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'term.color' requires 2 or 3 arguments.");

            var text = SvArgs.String(args, 0, "term.color");
            var color = SvArgs.String(args, 1, "term.color");
            string? fgCode = ResolveColorCode(color, isBackground: false, "term.color");

            string? bgCode = null;
            if (args.Length == 3)
            {
                var bgColor = SvArgs.String(args, 2, "term.color");
                bgCode = ResolveColorCode(bgColor, isBackground: true, "term.color");
            }

            if (fgCode is null && bgCode is null)
                return StashValue.FromObj(text);

            string codes = (fgCode, bgCode) switch
            {
                (not null, not null) => $"{fgCode};{bgCode}",
                (not null, null)     => fgCode!,
                (null, not null)     => bgCode!,
                _                    => "" // unreachable
            };
            return StashValue.FromObj($"\x1b[{codes}m{text}\x1b[0m");
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Wraps text in ANSI escape codes for terminal color.\n@param text The text to colorize\n@param color Foreground color: ANSI name (\"red\", \"brightcyan\"), 256-color SGR fragment (\"38;5;81\"), 24-bit hex (\"#RRGGBB\"), or empty string for none\n@param bgColor Optional background color in the same accepted forms\n@return The ANSI-styled string (or text unchanged if both colors are empty)");

        // term.bold(text) — Wraps 'text' in ANSI bold escape codes. Returns the styled string.
        ns.Function("bold", [Param("text", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var text = SvArgs.String(args, 0, "term.bold");
            return StashValue.FromObj($"\x1b[1m{text}\x1b[0m");
        }, returnType: "string",
            documentation: "Returns the text wrapped in ANSI bold formatting.\n@param text The text to style\n@return The ANSI bold-styled string");

        // term.dim(text) — Wraps 'text' in ANSI dim (faint) escape codes. Returns the styled string.
        ns.Function("dim", [Param("text", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var text = SvArgs.String(args, 0, "term.dim");
            return StashValue.FromObj($"\x1b[2m{text}\x1b[0m");
        }, returnType: "string",
            documentation: "Returns the text wrapped in ANSI dim formatting.\n@param text The text to style\n@return The ANSI dim-styled string");

        // term.underline(text) — Wraps 'text' in ANSI underline escape codes. Returns the styled string.
        ns.Function("underline", [Param("text", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var text = SvArgs.String(args, 0, "term.underline");
            return StashValue.FromObj($"\x1b[4m{text}\x1b[0m");
        }, returnType: "string",
            documentation: "Returns the text with ANSI underline formatting.\n@param text The text to style\n@return The ANSI underlined string");

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
                string? colorCode = ResolveColorCode(color, isBackground: false, "term.style");
                if (colorCode is not null)
                    codes.Add(colorCode);
            }

            if (codes.Count == 0)
            {
                return StashValue.FromObj(text);
            }

            return StashValue.FromObj($"\x1b[{string.Join(";", codes)}m{text}\x1b[0m");
        }, returnType: "string",
            documentation: "Applies multiple ANSI styles to text.\n@param text The text to style\n@param opts A dict of style options: bold (bool), dim (bool), underline (bool), color (string)\n@return The ANSI-styled string, or text unchanged if no options are set");

        // term.strip(text) — Removes all ANSI escape sequences from 'text'. Returns the plain string.
        ns.Function("strip", [Param("text", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var text = SvArgs.String(args, 0, "term.strip");
            return StashValue.FromObj(Regex.Replace(text, @"\x1b\[[0-9;]*m", ""));
        }, returnType: "string",
            documentation: "Removes all ANSI escape codes from the text.\n@param text The text to strip\n@return The plain text without any ANSI escape sequences");

        // term.width() — Returns the current terminal column width. Falls back to 80 if unavailable.
        ns.Function("width", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            try { return StashValue.FromInt((long)Console.WindowWidth); }
            catch { return StashValue.FromInt(80L); }
        }, returnType: "int",
            documentation: "Returns the terminal width in columns. Falls back to 80 if the width cannot be determined.\n@return The number of columns in the terminal");

        // term.isInteractive() — Returns true if stdin is connected to an interactive terminal (not redirected).
        ns.Function("isInteractive", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            try { return StashValue.FromBool(!Console.IsInputRedirected); }
            catch { return StashValue.False; }
        }, returnType: "bool",
            documentation: "Returns true if the terminal supports interactive input (stdin is not redirected).\n@return true if running in an interactive terminal, false if stdin is redirected");

        // term.zeroWidth(text) — Wraps text in readline non-printing markers (\x01 / \x02).
        ns.Function("zeroWidth", [Param("text", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var text = SvArgs.String(args, 0, "term.zeroWidth");
            return StashValue.FromObj("\x01" + text + "\x02");
        }, returnType: "string",
            documentation: "Wraps text in private-use markers (\\x01 and \\x02) so prompt-width calculation skips it. Use for OSC sequences and other non-SGR escapes that the auto-detector does not strip.\n@param text The non-printing text to wrap\n@return The wrapped string");

        // term.colorsEnabled() — Returns true when ANSI color output is appropriate.
        ns.Function("colorsEnabled", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            // STASH_FORCE_COLOR=1 overrides everything
            if (Environment.GetEnvironmentVariable("STASH_FORCE_COLOR") == "1")
                return StashValue.True;
            // NO_COLOR or redirected output disables colors
            bool noColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
            bool redirected = Console.IsOutputRedirected;
            return StashValue.FromBool(!noColor && !redirected);
        }, returnType: "bool",
            documentation: "Returns true if ANSI color output is appropriate for the current terminal. Follows the NO_COLOR convention (https://no-color.org) and checks whether stdout is redirected. Set STASH_FORCE_COLOR=1 to override and always enable colors.\n@return true if colors are enabled, false otherwise");

        // term.clear() — Clears the terminal screen using ANSI escape sequences. Returns null.
        ns.Function("clear", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> _) =>
        {
            ctx.Output.Write("\x1b[2J\x1b[H");
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Clears the terminal screen.\n@return null");

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
        }, returnType: "string", isVariadic: true,
            documentation: "Formats data as an ASCII table with the given headers and rows.\n@param rows A 2D array of values to render as table rows\n@param headers Optional array of column header strings\n@return A formatted ASCII table string");

        return ns.Build();
    }

    /// <summary>
    /// Resolves a color string into an ANSI SGR code fragment (without ESC[ or trailing 'm').
    /// Accepts: empty string (returns null = no color), ANSI color names (red, brightcyan, gray/grey),
    /// 24-bit hex (#RRGGBB), or raw 256-color/truecolor SGR fragments (e.g. "38;5;81", "38;2;200;100;50").
    /// </summary>
    private static string? ResolveColorCode(string color, bool isBackground, string callerName)
    {
        if (string.IsNullOrEmpty(color))
            return null;

        // 24-bit hex: #RRGGBB
        if (color.Length == 7 && color[0] == '#')
        {
            if (TryParseHexByte(color, 1, out byte r) &&
                TryParseHexByte(color, 3, out byte g) &&
                TryParseHexByte(color, 5, out byte b))
            {
                string prefix = isBackground ? "48" : "38";
                return $"{prefix};2;{r};{g};{b}";
            }
            throw new RuntimeError($"Invalid hex color '{color}' in '{callerName}'. Expected '#RRGGBB'.");
        }

        // Raw SGR fragment: starts with a digit, contains only digits and ';'
        if (color.Length > 0 && color[0] >= '0' && color[0] <= '9')
        {
            foreach (char c in color)
            {
                if (!((c >= '0' && c <= '9') || c == ';'))
                    throw new RuntimeError($"Invalid color SGR fragment '{color}' in '{callerName}'.");
            }
            return color;
        }

        // ANSI color name (case-insensitive). Background variants add 10 to the foreground code.
        int baseFg = color.ToLowerInvariant() switch
        {
            "black"          => 30,
            "red"            => 31,
            "green"          => 32,
            "yellow"         => 33,
            "blue"           => 34,
            "magenta"        => 35,
            "cyan"           => 36,
            "white"          => 37,
            "gray" or "grey" => 90,
            "brightblack"    => 90,
            "brightred"      => 91,
            "brightgreen"    => 92,
            "brightyellow"   => 93,
            "brightblue"     => 94,
            "brightmagenta"  => 95,
            "brightcyan"     => 96,
            "brightwhite"    => 97,
            _ => -1
        };

        if (baseFg < 0)
        {
            throw new RuntimeError(
                $"Unknown color '{color}' in '{callerName}'. Use a term color constant " +
                "(term.RED, etc.), a 'bright*' variant, '#RRGGBB' hex, " +
                "or a '38;5;N' / '38;2;R;G;B' SGR fragment.");
        }

        int code = isBackground ? baseFg + 10 : baseFg;
        return code.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool TryParseHexByte(string s, int offset, out byte value)
    {
        int hi = HexDigit(s[offset]);
        int lo = HexDigit(s[offset + 1]);
        if (hi < 0 || lo < 0) { value = 0; return false; }
        value = (byte)((hi << 4) | lo);
        return true;
    }

    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => 10 + (c - 'a'),
        >= 'A' and <= 'F' => 10 + (c - 'A'),
        _ => -1,
    };
}
