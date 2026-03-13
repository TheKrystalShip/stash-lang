using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Lsp.Analysis;

namespace Stash.Tests.Analysis;

public class LspFeaturesRound3Tests
{
    private static ScopeTree Analyze(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        return collector.Collect(stmts);
    }

    private static AnalysisResult FullAnalyze(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var tree = collector.Collect(stmts);
        var validator = new SemanticValidator(tree);
        var diagnostics = validator.Validate(stmts);
        return new AnalysisResult(tokens, stmts,
            new List<string>(), new List<string>(),
            new List<DiagnosticError>(), new List<DiagnosticError>(),
            tree, diagnostics);
    }

    // ──────────────────────────────────────────────────────────
    // 1. Structured Diagnostics Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void StructuredErrors_LexerProducesStructuredErrors()
    {
        var lexer = new Lexer("let x = \"hello", "<test>");
        lexer.ScanTokens();
        Assert.NotEmpty(lexer.StructuredErrors);
        Assert.Contains("Unterminated", lexer.StructuredErrors[0].Message);
        Assert.Equal("<test>", lexer.StructuredErrors[0].Span.File);
    }

    [Fact]
    public void StructuredErrors_ParserProducesStructuredErrors()
    {
        var lexer = new Lexer("let x = ;", "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        parser.ParseProgram();
        Assert.NotEmpty(parser.StructuredErrors);
        Assert.All(parser.StructuredErrors, e => Assert.NotNull(e.Span));
    }

    [Fact]
    public void StructuredErrors_SpanHasCorrectPosition()
    {
        var lexer = new Lexer("let x = \"unterminated", "<test>");
        lexer.ScanTokens();
        Assert.NotEmpty(lexer.StructuredErrors);
        Assert.Equal(1, lexer.StructuredErrors[0].Span.StartLine);
    }

    // ──────────────────────────────────────────────────────────
    // 2. Selection Range Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void SelectionRange_NestedExpression_HasMultipleContainingSpans()
    {
        var source = "let x = 1 + 2;";
        var result = FullAnalyze(source);
        Assert.Single(result.Statements);
        var stmt = result.Statements[0] as VarDeclStmt;
        Assert.NotNull(stmt);
        Assert.NotNull(stmt!.Initializer);
        var binary = stmt.Initializer as BinaryExpr;
        Assert.NotNull(binary);
        // The binary expr span should be contained within the statement span
        Assert.True(binary!.Span.StartColumn >= stmt.Span.StartColumn);
    }

    [Fact]
    public void SelectionRange_FunctionBody_ContainsInnerStatements()
    {
        var source = "fn test() {\n  let x = 1;\n}";
        var result = FullAnalyze(source);
        var fn = result.Statements[0] as FnDeclStmt;
        Assert.NotNull(fn);
        Assert.Single(fn!.Body.Statements);
        var inner = fn.Body.Statements[0];
        Assert.True(inner.Span.StartLine >= fn.Span.StartLine);
        Assert.True(inner.Span.EndLine <= fn.Span.EndLine);
    }

    // ──────────────────────────────────────────────────────────
    // 3. Document Links Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void DocumentLinks_ImportStmt_HasPathToken()
    {
        var source = "import { foo } from \"lib/utils.stash\";";
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        Assert.Empty(parser.Errors);
        var importStmt = stmts[0] as ImportStmt;
        Assert.NotNull(importStmt);
        Assert.Equal("lib/utils.stash", importStmt!.Path.Literal as string);
    }

    [Fact]
    public void DocumentLinks_ImportAsStmt_HasPathToken()
    {
        var source = "import \"lib/utils.stash\" as utils;";
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        Assert.Empty(parser.Errors);
        var importAs = stmts[0] as ImportAsStmt;
        Assert.NotNull(importAs);
        Assert.Equal("lib/utils.stash", importAs!.Path.Literal as string);
    }

    // ──────────────────────────────────────────────────────────
    // 4. Code Action Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void CodeAction_UndefinedVariable_ProducesWarning()
    {
        var result = FullAnalyze("let myVar = 1;\nmyVr;");
        Assert.Contains(result.SemanticDiagnostics,
            d => d.Message.Contains("myVr") && d.Message.Contains("is not defined"));
    }

    [Fact]
    public void CodeAction_SimilarSymbol_IsVisibleForSuggestion()
    {
        var tree = Analyze("let count = 0;\ncont;");
        var symbols = tree.GetVisibleSymbols(2, 1);
        Assert.Contains(symbols, s => s.Name == "count");
    }

    [Fact]
    public void CodeAction_BreakOutsideLoop_ProducesError()
    {
        var result = FullAnalyze("break;");
        Assert.Contains(result.SemanticDiagnostics,
            d => d.Message == "'break' used outside of a loop.");
    }

    // ──────────────────────────────────────────────────────────
    // 5. Workspace Symbols Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void WorkspaceSymbols_AllSymbols_ReturnsAllDeclarations()
    {
        var tree = Analyze("let x = 1;\nfn test() {\n  let y = 2;\n}\nstruct Point { a, b }");
        var all = tree.All;
        Assert.Contains(all, s => s.Name == "x");
        Assert.Contains(all, s => s.Name == "test");
        Assert.Contains(all, s => s.Name == "y");
        Assert.Contains(all, s => s.Name == "Point");
        Assert.Contains(all, s => s.Name == "a");
        Assert.Contains(all, s => s.Name == "b");
    }

    [Fact]
    public void WorkspaceSymbols_FilterByName_Works()
    {
        var tree = Analyze("let alpha = 1;\nlet beta = 2;\nfn gamma() {}");
        var all = tree.All;
        var filtered = all.Where(s => s.Name.Contains("al", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(filtered);
        Assert.Equal("alpha", filtered[0].Name);
    }
}
