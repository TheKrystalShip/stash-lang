namespace Stash.Analysis;

using System.Collections.Generic;
using Stash.Lexing;

/// <summary>
/// Attaches documentation comments to symbols by matching DocComment tokens
/// to the declaration that immediately follows them.
/// </summary>
/// <remarks>
/// <para>
/// Supports two comment styles recognised by the lexer:
/// </para>
/// <list type="bullet">
///   <item><description><c>///</c> single-line doc comments — one or more consecutive tokens are joined with newlines.</description></item>
///   <item><description><c>/** … */</c> block doc comments — the surrounding delimiters and leading <c>*</c> prefixes are stripped.</description></item>
/// </list>
/// <para>
/// After a doc comment is collected, <see cref="FindNextSymbol"/> scans <see cref="ScopeTree.All"/>
/// to find a declarable symbol (function, struct, enum, variable, constant, or method) whose
/// declaration starts on the same line or the line immediately after the last doc comment token.
/// The extracted text is then stored in <see cref="SymbolInfo.Documentation"/>, which is surfaced
/// by hover and completion handlers.
/// </para>
/// <para>
/// This resolver is called by <see cref="AnalysisEngine"/> after symbol collection and import
/// resolution, so imported symbols are also eligible to receive documentation from the source file
/// that defines them.
/// </para>
/// </remarks>
public static class DocCommentResolver
{
    /// <summary>
    /// Scans the token list for DocComment tokens and attaches their text
    /// to matching symbols in the scope tree.
    /// </summary>
    public static void Resolve(List<Token> tokens, ScopeTree symbols)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type != TokenType.DocComment)
            {
                continue;
            }

            // Collect consecutive doc comment tokens (for /// style)
            var docTokens = new List<Token> { tokens[i] };
            int j = i + 1;
            while (j < tokens.Count)
            {
                // Skip whitespace/newline trivia between consecutive /// lines
                if (tokens[j].Type == TokenType.DocComment)
                {
                    docTokens.Add(tokens[j]);
                    j++;
                }
                else
                {
                    break;
                }
            }

            // The end line of the last doc comment
            int docEndLine = docTokens[docTokens.Count - 1].Span.EndLine;

            // Find the symbol declared right after the doc comment (within 1 line)
            var symbol = FindNextSymbol(symbols, docEndLine);
            if (symbol != null)
            {
                symbol.Documentation = ExtractDocText(docTokens);
            }

            // Skip past the consumed doc tokens
            i = j - 1;
        }
    }

    /// <summary>
    /// Finds a symbol whose declaration starts on or within 1 line after docEndLine.
    /// Checks both the symbol's Span (name position) and FullSpan (full declaration).
    /// </summary>
    private static SymbolInfo? FindNextSymbol(ScopeTree symbols, int docEndLine)
    {
        SymbolInfo? best = null;
        int bestLine = int.MaxValue;

        foreach (var sym in symbols.All)
        {
            // Skip built-ins (line 0) and child symbols like parameters/fields
            if (sym.Span.StartLine == 0)
            {
                continue;
            }

            // Only match top-level declarable things
            if (sym.Kind is not (SymbolKind.Function or SymbolKind.Struct or SymbolKind.Enum
                or SymbolKind.Variable or SymbolKind.Constant or SymbolKind.Method))
            {
                continue;
            }

            // The declaration must start right after the doc comment (next line or same line for block comments)
            int declLine = sym.FullSpan?.StartLine ?? sym.Span.StartLine;
            if (declLine >= docEndLine && declLine <= docEndLine + 1 && declLine < bestLine)
            {
                best = sym;
                bestLine = declLine;
            }
        }

        return best;
    }

    /// <summary>
    /// Extracts the documentation text from doc comment tokens.
    /// Handles both /// (single-line) and /** */ (block) styles.
    /// Parses @param and @return tags into structured format.
    /// </summary>
    private static string ExtractDocText(List<Token> docTokens)
    {
        if (docTokens.Count == 1 && docTokens[0].Lexeme.StartsWith("/**"))
        {
            return ExtractBlockDocText(docTokens[0].Lexeme);
        }

        // Multiple /// lines
        var lines = new List<string>();
        foreach (var token in docTokens)
        {
            var line = token.Lexeme;
            // Strip leading /// and optional space
            if (line.StartsWith("///"))
            {
                line = line.Substring(3);
                if (line.StartsWith(" "))
                {
                    line = line.Substring(1);
                }
            }
            lines.Add(line);
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Extracts text from a /** ... */ block comment.
    /// Strips the delimiters and leading * on each line.
    /// </summary>
    private static string ExtractBlockDocText(string lexeme)
    {
        // Remove /** and */
        var body = lexeme.Substring(3);
        if (body.EndsWith("*/"))
        {
            body = body.Substring(0, body.Length - 2);
        }

        var rawLines = body.Split('\n');
        var lines = new List<string>();

        foreach (var rawLine in rawLines)
        {
            var trimmed = rawLine.Trim();
            // Strip leading * (JSDoc-style line prefix)
            if (trimmed.StartsWith("* "))
            {
                trimmed = trimmed.Substring(2);
            }
            else if (trimmed == "*")
            {
                trimmed = "";
            }

            lines.Add(trimmed);
        }

        // Remove empty leading/trailing lines
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join("\n", lines);
    }
}
