namespace Stash.Analysis;

using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;

public class AnalysisResult
{
    public List<Token> Tokens { get; }
    public List<Stmt> Statements { get; }
    public List<string> LexErrors { get; }
    public List<string> ParseErrors { get; }
    public List<DiagnosticError> StructuredLexErrors { get; }
    public List<DiagnosticError> StructuredParseErrors { get; }
    public ScopeTree Symbols { get; }
    public List<SemanticDiagnostic> SemanticDiagnostics { get; }
    public Dictionary<string, ImportResolver.ModuleInfo> NamespaceImports { get; }

    public AnalysisResult(List<Token> tokens, List<Stmt> statements,
        List<string> lexErrors, List<string> parseErrors,
        List<DiagnosticError> structuredLexErrors, List<DiagnosticError> structuredParseErrors,
        ScopeTree symbols, List<SemanticDiagnostic> semanticDiagnostics,
        Dictionary<string, ImportResolver.ModuleInfo>? namespaceImports = null)
    {
        Tokens = tokens;
        Statements = statements;
        LexErrors = lexErrors;
        ParseErrors = parseErrors;
        StructuredLexErrors = structuredLexErrors;
        StructuredParseErrors = structuredParseErrors;
        Symbols = symbols;
        SemanticDiagnostics = semanticDiagnostics;
        NamespaceImports = namespaceImports ?? new Dictionary<string, ImportResolver.ModuleInfo>();
    }

    /// <summary>
    /// Resolves a namespace member by looking up the dot prefix in imported namespaces.
    /// Returns the matching symbol from the imported module, or null.
    /// </summary>
    public (SymbolInfo Symbol, ImportResolver.ModuleInfo Module)? ResolveNamespaceMember(string text, int lspLine, int lspCharacter, string memberName)
    {
        var lines = text.Split('\n');
        if (lspLine >= lines.Length)
        {
            return null;
        }

        var prefix = TextUtilities.FindDotPrefix(lines[lspLine], lspCharacter);
        if (prefix == null)
        {
            return null;
        }

        if (!NamespaceImports.TryGetValue(prefix, out var moduleInfo))
        {
            return null;
        }

        var symbol = moduleInfo.Symbols.GetTopLevel().FirstOrDefault(s => s.Name == memberName);
        if (symbol == null)
        {
            return null;
        }

        return (symbol, moduleInfo);
    }

    /// <summary>
    /// Determines whether the token at the given 1-based source position is a dict literal key.
    /// A dict key is an Identifier preceded by LeftBrace/Comma and followed by Colon,
    /// excluding struct init fields (which resolve to Field symbols).
    /// </summary>
    public bool IsDictKey(int line, int column)
    {
        for (int i = 0; i < Tokens.Count; i++)
        {
            var token = Tokens[i];
            if (token.Type != TokenType.Identifier)
            {
                continue;
            }

            if (token.Span.StartLine != line || token.Span.StartColumn != column)
            {
                continue;
            }

            // Check: preceded by { or , (skip trivia tokens)
            bool precededByBraceOrComma = false;
            for (int j = i - 1; j >= 0; j--)
            {
                var prev = Tokens[j].Type;
                if (prev is TokenType.LeftBrace or TokenType.Comma)
                {
                    precededByBraceOrComma = true;
                    break;
                }
                // Skip trivia (comments, etc.)
                if (prev is TokenType.SingleLineComment or TokenType.BlockComment or TokenType.DocComment or TokenType.Shebang)
                {
                    continue;
                }

                break;
            }

            if (!precededByBraceOrComma)
            {
                return false;
            }

            // Check: followed by Colon
            bool followedByColon = false;
            for (int j = i + 1; j < Tokens.Count; j++)
            {
                var next = Tokens[j].Type;
                if (next == TokenType.Colon)
                {
                    followedByColon = true;
                    break;
                }
                if (next is TokenType.SingleLineComment or TokenType.BlockComment or TokenType.DocComment or TokenType.Shebang)
                {
                    continue;
                }

                break;
            }

            if (!followedByColon)
            {
                return false;
            }

            // Exclude struct init fields: if this identifier resolves to a Field symbol, it's a struct init
            var def = Symbols.FindDefinition(token.Lexeme, line, column);
            return def == null || def.Kind != SymbolKind.Field;
        }

        return false;
    }
}
