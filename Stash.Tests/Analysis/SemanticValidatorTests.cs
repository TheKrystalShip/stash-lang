using System.Collections.Generic;
using System.Linq;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Lsp.Analysis;

namespace Stash.Tests.Analysis;

public class SemanticValidatorTests
{
    private static List<SemanticDiagnostic> Validate(string source)
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

    [Fact]
    public void BreakOutsideLoop_ReportsError()
    {
        var diagnostics = Validate("break;");

        var d = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticLevel.Error, d.Level);
        Assert.Contains("'break' used outside of a loop.", d.Message);
    }

    [Fact]
    public void BreakInsideLoop_NoError()
    {
        var diagnostics = Validate("while (true) { break; }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ContinueOutsideLoop_ReportsError()
    {
        var diagnostics = Validate("continue;");

        var d = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticLevel.Error, d.Level);
        Assert.Contains("'continue' used outside of a loop.", d.Message);
    }

    [Fact]
    public void ContinueInsideLoop_NoError()
    {
        var diagnostics = Validate("for (let i in [1]) { continue; }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ReturnOutsideFunction_ReportsError()
    {
        var diagnostics = Validate("return 1;");

        var d = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticLevel.Error, d.Level);
        Assert.Contains("'return' used outside of a function.", d.Message);
    }

    [Fact]
    public void ReturnInsideFunction_NoError()
    {
        var diagnostics = Validate("fn foo() { return 1; }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ConstReassignment_ReportsError()
    {
        var diagnostics = Validate("const X = 1; X = 2;");

        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Cannot reassign constant 'X'.") &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void LetReassignment_NoError()
    {
        var diagnostics = Validate("let x = 1; x = 2;");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void WrongArity_ReportsError()
    {
        var diagnostics = Validate("fn add(a, b) { return a + b; } add(1);");

        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Expected 2 arguments but got 1.") &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void CorrectArity_NoError()
    {
        var diagnostics = Validate("fn add(a, b) { return a + b; } add(1, 2);");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ZeroArityFunction_WrongCall_ReportsError()
    {
        var diagnostics = Validate("fn greet() {} greet(1);");

        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Expected 0 arguments but got 1.") &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void NestedBreak_InLoop_NoError()
    {
        // break inside if inside while — loopDepth is still 1, no error
        var diagnostics = Validate("while (true) { if (true) { break; } }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ReturnInsideNestedFunction_NoError()
    {
        var diagnostics = Validate("fn outer() { fn inner() { return 1; } }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void BreakInsideFunction_InsideLoop_NoError()
    {
        var diagnostics = Validate("fn foo() { while (true) { break; } }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void UndefinedVariable_ReportsWarning()
    {
        var diagnostics = Validate("let x = y;");

        Assert.Contains(diagnostics, d =>
            d.Message.Contains("'y' is not defined.") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void DefinedVariable_NoWarning()
    {
        var diagnostics = Validate("let x = 1; let y = x;");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ValidProgram_NoErrors()
    {
        var diagnostics = Validate("let x = 1; fn foo(a) { return a + x; } foo(2);");

        Assert.Empty(diagnostics);
    }
}
