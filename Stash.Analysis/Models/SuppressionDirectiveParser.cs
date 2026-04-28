namespace Stash.Analysis;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Parses <c>// stash-disable</c> and <c>// stash-restore</c> suppression directives
/// from single-line comment tokens and builds a <see cref="SuppressionMap"/>.
/// </summary>
public static partial class SuppressionDirectiveParser
{
    private const string DisableNextLine = "stash-disable-next-line";
    private const string DisableLine = "stash-disable-line";
    private const string DisableFile = "stash-disable-file";
    private const string Disable = "stash-disable";
    private const string Restore = "stash-restore";

    [GeneratedRegex(@"^SA\d{4}$")]
    private static partial Regex CodePattern();

    /// <summary>
    /// Scans the token list for suppression directives and builds a <see cref="SuppressionMap"/>.
    /// Only <see cref="TokenType.SingleLineComment"/> tokens are examined.
    /// </summary>
    /// <param name="tokens">The full token list (including trivia) from lexing with <c>preserveTrivia: true</c>.</param>
    /// <returns>A populated <see cref="SuppressionMap"/>.</returns>
    public static SuppressionMap Parse(IReadOnlyList<Token> tokens)
    {
        var map = new SuppressionMap();

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type != TokenType.SingleLineComment) continue;

            // Strip leading "//" and trim
            var text = token.Lexeme;
            if (!text.StartsWith("//")) continue;
            var content = text.Substring(2).TrimStart();

            if (content.StartsWith(DisableNextLine, StringComparison.Ordinal))
            {
                var codesPart = content.Substring(DisableNextLine.Length);
                var codes = ParseCodes(codesPart, token.Span, map);
                int targetLine = FindNextCodeLine(tokens, i);
                if (targetLine > 0)
                {
                    map.AddLineSuppression(targetLine, codes);
                }
            }
            else if (content.StartsWith(DisableLine, StringComparison.Ordinal))
            {
                var codesPart = content.Substring(DisableLine.Length);
                var codes = ParseCodes(codesPart, token.Span, map);
                map.AddLineSuppression(token.Span.StartLine, codes);
            }
            else if (content.StartsWith(DisableFile, StringComparison.Ordinal))
            {
                // File-level suppression: stash-disable-file or stash-disable-file SA0201, SA0202
                var codesPart = content.Substring(DisableFile.Length);
                var codes = ParseCodes(codesPart, token.Span, map);
                map.SetFileLevelSuppression(codes);
            }
            else if (content.StartsWith(Disable, StringComparison.Ordinal))
            {
                var codesPart = content.Substring(Disable.Length);
                var codes = ParseCodes(codesPart, token.Span, map);
                // Start range from the next line (the directive line itself is not code)
                map.AddRangeSuppression(token.Span.StartLine + 1, null, codes);
            }
            else if (content.StartsWith(Restore, StringComparison.Ordinal))
            {
                var codesPart = content.Substring(Restore.Length);
                var codes = ParseCodes(codesPart, token.Span, map);
                map.RestoreRange(token.Span.StartLine - 1, codes);
            }
        }

        return map;
    }

    /// <summary>
    /// Parses comma-separated diagnostic codes from the text after a directive keyword.
    /// Returns <see langword="null"/> if no codes are specified (meaning "all").
    /// Emits SA0001/SA0002 diagnostics for invalid codes.
    /// </summary>
    private static HashSet<string>? ParseCodes(string text, SourceSpan directiveSpan, SuppressionMap map)
    {
        // Trim and check for empty (suppress all)
        text = text.TrimStart();
        if (string.IsNullOrEmpty(text)) return null;

        // Split on first non-code character that isn't comma or space
        // Codes are SA followed by 4 digits, comma-separated
        var codes = new HashSet<string>();
        bool encounteredCodeLikeToken = false;
        int pos = 0;

        while (pos < text.Length)
        {
            // Skip whitespace and commas
            while (pos < text.Length && (text[pos] == ' ' || text[pos] == ',' || text[pos] == '\t'))
                pos++;

            if (pos >= text.Length) break;

            // Check if this looks like a code (starts with SA or is an identifier)
            if (text[pos] == 'S' && pos + 1 < text.Length && text[pos + 1] == 'A')
            {
                // Read until space, comma, or end
                int start = pos;
                while (pos < text.Length && text[pos] != ' ' && text[pos] != ',' && text[pos] != '\t')
                    pos++;

                string candidate = text[start..pos];
                encounteredCodeLikeToken = true;

                if (CodePattern().IsMatch(candidate))
                {
                    // Valid format — check if it's a known code
                    if (DiagnosticDescriptors.AllByCode.ContainsKey(candidate))
                    {
                        codes.Add(candidate);
                    }
                    else
                    {
                        map.AddDirectiveDiagnostic(DiagnosticDescriptors.SA0001.CreateDiagnostic(directiveSpan, candidate));
                    }
                }
                else
                {
                    map.AddDirectiveDiagnostic(DiagnosticDescriptors.SA0002.CreateDiagnostic(directiveSpan, candidate));
                }
            }
            else
            {
                // Check for quoted reason string (e.g. "kept for side-effect")
                if (text[pos] == '"')
                {
                    // Quoted reason — stop parsing codes here, it's valid
                    break;
                }

                // Read the non-SA token
                int start = pos;
                while (pos < text.Length && text[pos] != ' ' && text[pos] != ',' && text[pos] != '\t')
                    pos++;

                string candidate = text[start..pos];

                // Check for common patterns like "—" or words that indicate a reason
                if (candidate.StartsWith("—") || candidate.StartsWith("-") || candidate == "//" || candidate.Length > 10)
                {
                    // This is likely a reason/explanation, stop parsing codes
                    break;
                }

                // Looks like a malformed code
                if (codes.Count == 0)
                {
                    encounteredCodeLikeToken = true;
                    map.AddDirectiveDiagnostic(DiagnosticDescriptors.SA0002.CreateDiagnostic(directiveSpan, candidate));
                }
                else
                {
                    // Already parsed some codes, rest is likely a reason
                    break;
                }
            }
        }

        if (codes.Count > 0) return codes;
        if (encounteredCodeLikeToken) return codes; // empty set = suppress nothing
        return null; // no codes specified = suppress all
    }

    /// <summary>
    /// Finds the line number of the next non-empty, non-comment line after the directive at index <paramref name="directiveIndex"/>.
    /// </summary>
    private static int FindNextCodeLine(IReadOnlyList<Token> tokens, int directiveIndex)
    {
        int directiveLine = tokens[directiveIndex].Span.StartLine;

        for (int j = directiveIndex + 1; j < tokens.Count; j++)
        {
            var t = tokens[j];
            if (t.Type is TokenType.SingleLineComment or TokenType.DocComment or TokenType.BlockComment)
            {
                continue;
            }
            if (t.Span.StartLine > directiveLine)
            {
                return t.Span.StartLine;
            }
        }

        return 0; // No code line found
    }
}
