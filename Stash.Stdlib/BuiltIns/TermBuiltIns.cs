namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Stash.Runtime;
using Stash.Stdlib.Abstractions;

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
[StashNamespace]
public static partial class TermBuiltIns
{
    // Color constants — string values accepted by term.color and term.style for foreground coloring.
    [StashConst] public const string BLACK   = "black";
    [StashConst] public const string RED     = "red";
    [StashConst] public const string GREEN   = "green";
    [StashConst] public const string YELLOW  = "yellow";
    [StashConst] public const string BLUE    = "blue";
    [StashConst] public const string MAGENTA = "magenta";
    [StashConst] public const string CYAN    = "cyan";
    [StashConst] public const string WHITE   = "white";
    [StashConst] public const string GRAY    = "gray";

    /// <summary>Wraps text in ANSI escape codes for terminal color.</summary>
    /// <param name="text">The text to colorize</param>
    /// <param name="color">Foreground color: ANSI name ("red", "brightcyan"), 256-color SGR fragment ("38;5;81"), 24-bit hex ("#RRGGBB"), or empty string for none</param>
    /// <param name="rest">Optional background color in the same accepted forms</param>
    /// <exception cref="StashErrorTypes.ValueError">if a color string is not a recognised ANSI name, '#RRGGBB' hex, or valid SGR fragment</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    /// <returns>The ANSI-styled string (or text unchanged if both colors are empty)</returns>
    [StashFn(ReturnType = "string")]
    private static string Color(string text, string color, params StashValue[] rest)
    {
        if (rest.Length > 1)
            throw new RuntimeError("'term.color' requires 2 or 3 arguments.");

        string? fgCode = ResolveColorCode(color, isBackground: false, "term.color");

        string? bgCode = null;
        if (rest.Length == 1)
        {
            var bgColor = SvArgs.String(rest, 0, "term.color");
            bgCode = ResolveColorCode(bgColor, isBackground: true, "term.color");
        }

        if (fgCode is null && bgCode is null)
            return text;

        string codes = (fgCode, bgCode) switch
        {
            (not null, not null) => $"{fgCode};{bgCode}",
            (not null, null)     => fgCode!,
            (null, not null)     => bgCode!,
            _                    => "" // unreachable
        };
        return $"\x1b[{codes}m{text}\x1b[0m";
    }

    /// <summary>Returns the text wrapped in ANSI bold formatting.</summary>
    /// <param name="text">The text to style</param>
    /// <returns>The ANSI bold-styled string</returns>
    [StashFn(ReturnType = "string")]
    public static string Bold(string text) => $"\x1b[1m{text}\x1b[0m";

    /// <summary>Returns the text wrapped in ANSI dim formatting.</summary>
    /// <param name="text">The text to style</param>
    /// <returns>The ANSI dim-styled string</returns>
    [StashFn(ReturnType = "string")]
    public static string Dim(string text) => $"\x1b[2m{text}\x1b[0m";

    /// <summary>Returns the text with ANSI underline formatting.</summary>
    /// <param name="text">The text to style</param>
    /// <returns>The ANSI underlined string</returns>
    [StashFn(ReturnType = "string")]
    public static string Underline(string text) => $"\x1b[4m{text}\x1b[0m";

    /// <summary>Applies multiple ANSI styles to text.</summary>
    /// <param name="text">The text to style</param>
    /// <param name="opts">A dict of style options: bold (bool), dim (bool), underline (bool), color (string)</param>
    /// <exception cref="StashErrorTypes.TypeError">if opts is not a dictionary</exception>
    /// <exception cref="StashErrorTypes.ValueError">if the color field is not a recognised ANSI name, '#RRGGBB' hex, or valid SGR fragment</exception>
    /// <returns>The ANSI-styled string, or text unchanged if no options are set</returns>
    [StashFn(ReturnType = "string")]
    public static string Style(string text, [StashParam(Type = "dict")] StashValue opts)
    {
        var dict = SvArgs.Dict(new[] { opts }, 0, "term.style");
        var codes = new List<string>();

        if (dict.Get("bold").ToObject() is true) codes.Add("1");
        if (dict.Get("dim").ToObject() is true) codes.Add("2");
        if (dict.Get("underline").ToObject() is true) codes.Add("4");

        if (dict.Get("color").ToObject() is string color)
        {
            string? colorCode = ResolveColorCode(color, isBackground: false, "term.style");
            if (colorCode is not null) codes.Add(colorCode);
        }

        if (codes.Count == 0) return text;
        return $"\x1b[{string.Join(";", codes)}m{text}\x1b[0m";
    }

    /// <summary>Removes all ANSI escape codes from the text.</summary>
    /// <param name="text">The text to strip</param>
    /// <returns>The plain text without any ANSI escape sequences</returns>
    [StashFn(ReturnType = "string")]
    public static string Strip(string text) => Regex.Replace(text, @"\x1b\[[0-9;]*m", "");

    /// <summary>Returns the terminal width in columns. Falls back to 80 if the width cannot be determined.</summary>
    /// <returns>The number of columns in the terminal</returns>
    [StashFn(ReturnType = "int")]
    public static long Width()
    {
        try { return (long)Console.WindowWidth; }
        catch { return 80L; }
    }

    /// <summary>Returns true if the terminal supports interactive input (stdin is not redirected).</summary>
    /// <returns>true if running in an interactive terminal, false if stdin is redirected</returns>
    [StashFn(ReturnType = "bool")]
    public static bool IsInteractive()
    {
        try { return !Console.IsInputRedirected; }
        catch { return false; }
    }

    /// <summary>Wraps text in private-use markers (\x01 and \x02) so prompt-width calculation skips it. Use for OSC sequences and other non-SGR escapes that the auto-detector does not strip.</summary>
    /// <param name="text">The non-printing text to wrap</param>
    /// <returns>The wrapped string</returns>
    [StashFn(ReturnType = "string")]
    public static string ZeroWidth(string text) => "\x01" + text + "\x02";

    /// <summary>Returns true if ANSI color output is appropriate for the current terminal. Follows the NO_COLOR convention (https://no-color.org) and checks whether stdout is redirected. Set STASH_FORCE_COLOR=1 to override and always enable colors.</summary>
    /// <returns>true if colors are enabled, false otherwise</returns>
    [StashFn(ReturnType = "bool")]
    public static bool ColorsEnabled()
    {
        if (Environment.GetEnvironmentVariable("STASH_FORCE_COLOR") == "1")
            return true;
        bool noColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        bool redirected = Console.IsOutputRedirected;
        return !noColor && !redirected;
    }

    /// <summary>Clears the terminal screen.</summary>
    /// <returns>null</returns>
    [StashFn(ReturnType = "null")]
    private static void Clear(IInterpreterContext ctx)
    {
        ctx.Output.Write("\x1b[2J\x1b[H");
    }

    /// <summary>Formats data as an ASCII table with the given headers and rows.</summary>
    /// <param name="rows">A 2D array of values to render as table rows</param>
    /// <param name="rest">Optional array of column header strings</param>
    /// <exception cref="StashErrorTypes.TypeError">if any element of rows is not an array</exception>
    /// <returns>A formatted ASCII table string</returns>
    [StashFn(ReturnType = "string")]
    private static string Table(List<StashValue> rows, params StashValue[] rest)
    {
        if (rest.Length > 1) throw new RuntimeError("'term.table' expects 1 or 2 arguments.");
        List<StashValue>? headers = null;
        if (rest.Length == 1)
        {
            var headersObj = rest[0].ToObject();
            if (headersObj is List<StashValue> h) headers = h;
        }
        var allRows = new List<string[]>();
        if (headers != null)
            allRows.Add(headers.Select(x => RuntimeValues.Stringify(x.ToObject())).ToArray());
        foreach (StashValue row in rows)
        {
            if (row.ToObject() is List<StashValue> cols)
                allRows.Add(cols.Select(c => RuntimeValues.Stringify(c.ToObject())).ToArray());
            else
                throw new RuntimeError("Each row in 'term.table' must be an array.");
        }
        if (allRows.Count == 0) return "";
        int colCount = allRows.Max(r => r.Length);
        var widths = new int[colCount];
        foreach (var row in allRows)
            for (int i = 0; i < row.Length; i++)
                if (row[i].Length > widths[i]) widths[i] = row[i].Length;
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
                sb.AppendLine(separator);
        }
        sb.Append(separator);
        return sb.ToString();
    }

    private static string? ResolveColorCode(string color, bool isBackground, string callerName)
    {
        if (string.IsNullOrEmpty(color))
            return null;

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

        if (color.Length > 0 && color[0] >= '0' && color[0] <= '9')
        {
            foreach (char c in color)
            {
                if (!((c >= '0' && c <= '9') || c == ';'))
                    throw new RuntimeError($"Invalid color SGR fragment '{color}' in '{callerName}'.");
            }
            return color;
        }

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
