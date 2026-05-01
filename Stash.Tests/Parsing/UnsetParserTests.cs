using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

namespace Stash.Tests.Parsing;

/// <summary>
/// Parser tests for the soft-keyword <c>unset</c> statement.
/// Covers spec §9.3 — parse errors and soft-keyword disambiguation.
/// </summary>
public class UnsetParserTests
{
    private static List<Stmt> ParseProgram(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        return new Parser(tokens).ParseProgram();
    }

    /// <summary>
    /// Asserts that the parser records at least one error for the given source.
    /// </summary>
    private static void RunExpectingParseError(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        var parser = new Parser(tokens);
        parser.ParseProgram();
        Assert.NotEmpty(parser.Errors);
    }

    // =========================================================================
    // §9.3 — inputs that must NOT parse (parse error expected)
    // =========================================================================

    [Fact]
    public void UnsetSemicolon_IsValidExpressionStatement()
    {
        // `unset;` — because `unset` is a soft keyword that only activates when followed by
        // an Identifier, `unset;` (followed by ';') is NOT the unset statement at all.
        // It is parsed as an expression statement referencing the identifier `unset`.
        // Per spec §4.4, the disambiguation happens before any parse error can fire.
        var tokens = new Lexer("unset;", "<test>").ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        Assert.Empty(parser.Errors);
        // Should be an ExprStmt, NOT an UnsetStmt
        var stmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        Assert.IsType<IdentifierExpr>(stmt.Expression);
    }

    [Fact]
    public void ParseError_UnsetWithTrailingComma()
    {
        // `unset a,;` — trailing comma without a following identifier → parse error
        RunExpectingParseError("unset a,;");
    }

    [Fact]
    public void ParseError_UnsetWithDottedPath()
    {
        // `unset foo.bar;` — dotted path is not a bare identifier → parse error
        RunExpectingParseError("unset foo.bar;");
    }

    [Fact]
    public void ParseError_UnsetWithIndexedPath()
    {
        // `unset foo["b"];` — indexed expression is not a bare identifier → parse error
        RunExpectingParseError("""unset foo["b"];""");
    }

    // =========================================================================
    // §9.3 — inputs that MUST parse (soft-keyword as identifier)
    // =========================================================================

    [Fact]
    public void SoftKeyword_LetUnset_ParsesAsVarDecl()
    {
        // `let unset = 1;` — 'unset' is an identifier name here, not the statement keyword
        var stmts = ParseProgram("let unset = 1;");
        var decl = Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
        Assert.Equal("unset", decl.Name.Lexeme);
    }

    [Fact]
    public void SoftKeyword_UnsetCallExpr_ParsesAsCall()
    {
        // `unset();` — `unset` followed by `(` is NOT the unset statement;
        // IsUnsetKeyword() requires the next token to be an Identifier, not '('.
        var stmts = ParseProgram("fn unset() {} unset();");
        Assert.Equal(2, stmts.Count);
        var callStmt = Assert.IsType<ExprStmt>(stmts[1]);
        Assert.IsType<CallExpr>(callStmt.Expression);
    }

    [Fact]
    public void SoftKeyword_UnsetDotAccess_ParsesAsExprStmt()
    {
        // `unset.field;` — `unset` followed by `.` is an expression, not the keyword
        var stmts = ParseProgram("let unset = 1; unset.field;");
        Assert.Equal(2, stmts.Count);
        var exprStmt = Assert.IsType<ExprStmt>(stmts[1]);
        Assert.IsType<DotExpr>(exprStmt.Expression);
    }

    [Fact]
    public void SoftKeyword_UnsetAssignment_ParsesAsExprStmt()
    {
        // `unset = 5;` — assignment to a variable named 'unset'
        var stmts = ParseProgram("let unset = 1; unset = 5;");
        Assert.Equal(2, stmts.Count);
        var exprStmt = Assert.IsType<ExprStmt>(stmts[1]);
        Assert.IsType<AssignExpr>(exprStmt.Expression);
    }

    // =========================================================================
    // Positive parse — verify UnsetStmt is produced in keyword position
    // =========================================================================

    [Fact]
    public void UnsetStmt_SingleTarget_ParsesAsUnsetStmt()
    {
        var stmts = ParseProgram("let x = 1; unset x;");
        Assert.Equal(2, stmts.Count);
        Assert.IsType<UnsetStmt>(stmts[1]);
    }

    [Fact]
    public void UnsetStmt_MultipleTargets_ParsesAllTargets()
    {
        var stmts = ParseProgram("let a = 1; let b = 2; unset a, b;");
        Assert.Equal(3, stmts.Count);
        var unsetStmt = Assert.IsType<UnsetStmt>(stmts[2]);
        Assert.Equal(2, unsetStmt.Targets.Count);
        Assert.Equal("a", unsetStmt.Targets[0].Name);
        Assert.Equal("b", unsetStmt.Targets[1].Name);
    }
}
