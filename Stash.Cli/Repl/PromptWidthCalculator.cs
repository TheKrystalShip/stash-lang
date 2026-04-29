namespace Stash.Cli.Repl;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Computes the visible width of a prompt string by stripping zero-width marker regions
/// (<c>\x01</c>…<c>\x02</c>) and ANSI SGR escape sequences (<c>ESC [ … m</c>).
/// </summary>
internal static partial class PromptWidthCalculator
{
    /// <summary>Matches ANSI SGR sequences of the form <c>ESC [ &lt;digits/semicolons&gt; m</c>.</summary>
    [GeneratedRegex(@"\x1b\[[\d;]*m")]
    private static partial Regex AnsiSgrRegex();

    /// <summary>
    /// Returns the visible character width of <paramref name="prompt"/> after stripping
    /// zero-width marker regions and ANSI SGR escape sequences.
    /// East-Asian wide-character handling is deferred to a future phase; v1 uses
    /// <c>string.Length</c> on the stripped text.
    /// </summary>
    public static int VisibleWidth(string prompt)
    {
        string stripped = StripZeroWidthRegions(prompt);
        stripped = AnsiSgrRegex().Replace(stripped, "");
        return stripped.Length;
    }

    private static string StripZeroWidthRegions(string s)
    {
        if (!s.Contains('\x01'))
            return s;

        var sb = new StringBuilder(s.Length);
        bool inZeroWidth = false;
        foreach (char c in s)
        {
            if (c == '\x01') { inZeroWidth = true; continue; }
            if (c == '\x02') { inZeroWidth = false; continue; }
            if (!inZeroWidth)
                sb.Append(c);
        }
        return sb.ToString();
    }
}
