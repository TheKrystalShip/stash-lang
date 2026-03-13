namespace Stash.Lsp.Analysis;

using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

public class AnalysisResult
{
    public List<Token> Tokens { get; }
    public List<Stmt> Statements { get; }
    public List<string> LexErrors { get; }
    public List<string> ParseErrors { get; }
    public ScopeTree Symbols { get; }

    public AnalysisResult(List<Token> tokens, List<Stmt> statements,
        List<string> lexErrors, List<string> parseErrors, ScopeTree symbols)
    {
        Tokens = tokens;
        Statements = statements;
        LexErrors = lexErrors;
        ParseErrors = parseErrors;
        Symbols = symbols;
    }
}
