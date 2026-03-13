namespace Stash.Lsp.Analysis;

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
}
