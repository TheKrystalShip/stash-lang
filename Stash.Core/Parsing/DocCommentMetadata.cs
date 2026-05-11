namespace Stash.Parsing;

using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Parsing.AST;

/// <summary>
/// Parses the raw text of a doc comment (lines already stripped of leading <c>///</c>
/// and joined by <c>\n</c>) into structured metadata.
/// </summary>
/// <remarks>
/// <c>@throws</c> lines are extracted into structured <see cref="ThrowsEntry"/> records and
/// removed from the prose <c>Documentation</c> string. All other tag lines (<c>@param</c>,
/// <c>@return</c>, etc.) are preserved in the prose unchanged.
/// </remarks>
public static class DocCommentMetadata
{
    /// <summary>
    /// Parses raw doc-comment text into prose documentation plus a structured throws list.
    /// </summary>
    /// <param name="raw">
    /// The joined doc-comment text (lines already stripped of <c>///</c> prefixes, joined with <c>\n</c>),
    /// or <see langword="null"/>.
    /// </param>
    /// <param name="baseSpan">
    /// Approximate source span used as the <see cref="ThrowsEntry.Span"/> for every extracted entry.
    /// Per-line span precision is not required for diagnostics.
    /// </param>
    /// <returns>
    /// A tuple where:
    /// <list type="bullet">
    ///   <item><description><c>Documentation</c> is the prose text with <c>@throws</c> lines removed,
    ///   or <see langword="null"/> if the result is empty.</description></item>
    ///   <item><description><c>Throws</c> is the list of parsed throws entries,
    ///   or <see langword="null"/> if no <c>@throws</c> tags were found.</description></item>
    /// </list>
    /// </returns>
    public static (string? Documentation, IReadOnlyList<ThrowsEntry>? Throws) Extract(string? raw, SourceSpan baseSpan)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null);

        var proseLines = new List<string>();
        var throws = new List<ThrowsEntry>();

        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("@throws", StringComparison.Ordinal)
                && (trimmed.Length == 7 || trimmed[7] == ' '))
            {
                var rest = trimmed.Length > 8 ? trimmed.Substring(8).Trim() : string.Empty;
                if (string.IsNullOrEmpty(rest))
                    continue;

                ParseThrowsLine(rest, baseSpan, throws);
            }
            else
            {
                proseLines.Add(line);
            }
        }

        string? prose = string.Join("\n", proseLines).Trim();
        if (string.IsNullOrEmpty(prose)) prose = null;

        return (prose, throws.Count > 0 ? throws : null);
    }

    /// <summary>
    /// Parses the portion of a <c>@throws</c> line after the <c>@throws</c> keyword:
    /// one or more comma-separated identifiers followed by an optional description.
    /// </summary>
    private static void ParseThrowsLine(string rest, SourceSpan baseSpan, List<ThrowsEntry> target)
    {
        int pos = 0;
        var typeNames = new List<string>(2);

        while (pos < rest.Length)
        {
            // Skip leading whitespace
            while (pos < rest.Length && rest[pos] == ' ') pos++;

            // Read identifier chars
            int identStart = pos;
            while (pos < rest.Length && (char.IsLetterOrDigit(rest[pos]) || rest[pos] == '_'))
                pos++;

            if (pos == identStart)
                break; // no identifier found — stop

            typeNames.Add(rest[identStart..pos]);

            // Skip optional whitespace after identifier
            while (pos < rest.Length && rest[pos] == ' ') pos++;

            // If next char is comma, consume it and continue looking for more type names
            if (pos < rest.Length && rest[pos] == ',')
            {
                pos++;
                continue;
            }

            // Otherwise, everything remaining is the description
            break;
        }

        if (typeNames.Count == 0)
            return;

        string? description = pos < rest.Length ? rest[pos..].Trim() : null;
        if (string.IsNullOrEmpty(description)) description = null;

        foreach (var typeName in typeNames)
        {
            target.Add(new ThrowsEntry(typeName, description, baseSpan));
        }
    }
}
