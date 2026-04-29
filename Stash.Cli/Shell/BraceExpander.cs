using System.Collections.Generic;

namespace Stash.Cli.Shell;

/// <summary>
/// Implements §6 step 2 of the argument-expansion pipeline: brace expansion.
///
/// Supported patterns:
///   {a,b,c}   — two or more comma-separated alternatives (≥1 top-level comma required).
///   {a,{b,c}} — nested braces expand recursively.
///   {a,b}-{1,2} — multiple brace groups produce the full cross-product.
///
/// NOT expanded (literal passthrough):
///   {a}       — single element, no comma.
///   {}        — empty braces.
///   {1..5}    — range syntax is not supported in v1 (future work).
///   {a,b      — unbalanced (no closing brace) → literal.
///
/// Quoting: the caller (ArgExpander) is responsible for skipping this method
/// on tokens where WasQuoted == true. This method operates on plain strings
/// with no quote-context information.
///
/// Brace patterns introduced by ${...} interpolation ARE expanded (bash-like):
/// expansion runs on every unquoted token after interpolation has produced
/// its final string content.
///
/// Algorithm complexity: the parser is O(n) per brace level; the total output
/// size is inherently exponential in the number of brace groups (k groups of
/// size m each produce m^k results), which is unavoidable and correct.
/// </summary>
internal static class BraceExpander
{
    /// <summary>
    /// Expand brace patterns in <paramref name="input"/> into the cross-product
    /// of all alternatives. Returns a list with one entry per expanded variant.
    /// Returns a single-element list containing the original string when no
    /// expandable brace pattern is present.
    /// </summary>
    public static List<string> Expand(string input)
    {
        if (input.Length == 0)
            return new List<string>(1) { input };

        if (!input.Contains('{'))
            return new List<string>(1) { input };

        return ExpandCore(input);
    }

    // ── Core recursive expander ───────────────────────────────────────────────

    private static List<string> ExpandCore(string input)
    {
        // Find the FIRST top-level brace group that has at least one top-level comma.
        // Scan left-to-right. Track depth; top-level commas are at depth == 1.
        int braceStart = -1;
        int braceEnd = -1;
        bool hasComma = false;
        int depth = 0;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '{')
            {
                if (depth == 0)
                {
                    // Begin a new candidate group.
                    braceStart = i;
                    hasComma = false;
                }
                depth++;
            }
            else if (c == '}')
            {
                if (depth > 0)
                    depth--;

                if (depth == 0 && braceStart >= 0)
                {
                    if (hasComma)
                    {
                        // Found a valid expandable group.
                        braceEnd = i;
                        break;
                    }
                    else
                    {
                        // No top-level comma — e.g. {a} or {} or {1..5}. Not expandable.
                        // Reset and continue scanning for the next '{'.
                        braceStart = -1;
                    }
                }
            }
            else if (c == ',' && depth == 1)
            {
                hasComma = true;
            }
        }

        // No expandable brace group found.
        if (braceStart < 0 || braceEnd < 0)
            return new List<string>(1) { input };

        string prefix = input[..braceStart];
        string inner  = input[(braceStart + 1)..braceEnd];
        string suffix = input[(braceEnd + 1)..];

        // Split inner on top-level commas.
        List<string> parts = SplitOnTopLevelCommas(inner);

        // For each alternative, recursively expand the reassembled string
        // so that subsequent brace groups (in suffix) and nested groups (in
        // the part itself) are handled correctly.
        var result = new List<string>(parts.Count);
        foreach (string part in parts)
        {
            List<string> expanded = ExpandCore(prefix + part + suffix);
            result.AddRange(expanded);
        }

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Split <paramref name="inner"/> on commas that are at nesting depth 0
    /// (i.e., not inside nested braces). Adjacent commas produce empty-string
    /// parts, preserving bash semantics for {a,} and {,a}.
    /// </summary>
    private static List<string> SplitOnTopLevelCommas(string inner)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '{')
                depth++;
            else if (c == '}')
                depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(inner[start..i]);
                start = i + 1;
            }
        }

        // Last (or only) segment.
        parts.Add(inner[start..]);
        return parts;
    }
}
