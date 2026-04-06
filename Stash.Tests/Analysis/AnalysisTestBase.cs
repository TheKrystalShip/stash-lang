using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Analysis;

namespace Stash.Tests.Analysis;

public abstract class AnalysisTestBase
{
    protected static ScopeTree Analyze(string source, bool includeBuiltIns = false)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector { IncludeBuiltIns = includeBuiltIns };
        return collector.Collect(stmts);
    }

    protected static List<SemanticDiagnostic> Validate(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var scopeTree = collector.Collect(stmts);
        var validator = new SemanticValidator(scopeTree);
        return validator.Validate(stmts);
    }

    protected static AnalysisResult FullAnalyze(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var tree = collector.Collect(stmts);
        var validator = new SemanticValidator(tree);
        var diagnostics = validator.Validate(stmts);
        return new AnalysisResult(tokens, stmts, new List<string>(), new List<string>(), new List<DiagnosticError>(), new List<DiagnosticError>(), tree, diagnostics);
    }
}
