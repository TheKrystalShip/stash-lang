namespace Stash.Tests.Parsing;

using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

/// <summary>
/// Tests for lambda doc-comment extraction: when a <c>let</c> or <c>const</c> declaration
/// has a leading <c>///</c> doc comment and its initializer is a lambda expression, the
/// <c>@throws</c> metadata is extracted into <see cref="LambdaExpr.Throws"/> and the prose
/// (with <c>@throws</c> lines removed) into <see cref="LambdaExpr.Documentation"/>.
/// </summary>
public class LambdaDocCommentTests
{
    private static List<Stmt> ParseProgram(string source)
    {
        var tokens = new Lexer(source).ScanTokens();
        return new Parser(tokens).ParseProgram();
    }

    [Fact]
    public void Lambda_AssignedToLet_WithThrowsTag_PopulatesThrows()
    {
        var stmts = ParseProgram("""
            /// @throws IOError when bad
            let f = () => { };
            """);

        var decl = Assert.IsType<VarDeclStmt>(stmts[0]);
        var lambda = Assert.IsType<LambdaExpr>(decl.Initializer);

        Assert.NotNull(lambda.Throws);
        Assert.Single(lambda.Throws!);
        Assert.Equal("IOError", lambda.Throws![0].ErrorType);
        Assert.Equal("when bad", lambda.Throws[0].Description);
    }

    [Fact]
    public void Lambda_AssignedToConst_WithThrowsTag_PopulatesThrows()
    {
        var stmts = ParseProgram("""
            /// @throws ParseError if input is invalid
            const handler = (x) => x;
            """);

        var decl = Assert.IsType<ConstDeclStmt>(stmts[0]);
        var lambda = Assert.IsType<LambdaExpr>(decl.Initializer);

        Assert.NotNull(lambda.Throws);
        Assert.Single(lambda.Throws!);
        Assert.Equal("ParseError", lambda.Throws![0].ErrorType);
        Assert.Equal("if input is invalid", lambda.Throws[0].Description);
    }

    [Fact]
    public void Lambda_WithoutDocComment_ThrowsIsNull()
    {
        var stmts = ParseProgram("let f = () => { };");

        var decl = Assert.IsType<VarDeclStmt>(stmts[0]);
        var lambda = Assert.IsType<LambdaExpr>(decl.Initializer);

        Assert.Null(lambda.Throws);
        Assert.Null(lambda.Documentation);
    }

    [Fact]
    public void Lambda_NotALambdaInitializer_NoCrash()
    {
        // A doc comment on a let-binding to a non-lambda value must not crash.
        var stmts = ParseProgram("""
            /// @throws IOError when bad
            let x = 5;
            """);

        // Simply must not throw — no lambda, so no metadata to populate.
        var decl = Assert.IsType<VarDeclStmt>(stmts[0]);
        Assert.IsType<LiteralExpr>(decl.Initializer);
    }

    [Fact]
    public void Lambda_WithDocComment_RemovesThrowsFromProse()
    {
        var stmts = ParseProgram("""
            /// Reads a file.
            /// @throws IOError when the file cannot be read
            /// @return the file contents
            let readFile = (path) => { };
            """);

        var decl = Assert.IsType<VarDeclStmt>(stmts[0]);
        var lambda = Assert.IsType<LambdaExpr>(decl.Initializer);

        // Documentation prose should NOT contain the @throws line.
        Assert.NotNull(lambda.Documentation);
        Assert.DoesNotContain("@throws", lambda.Documentation!);
        Assert.Contains("Reads a file.", lambda.Documentation);

        // @throws should be in the structured list.
        Assert.NotNull(lambda.Throws);
        Assert.Single(lambda.Throws!);
        Assert.Equal("IOError", lambda.Throws![0].ErrorType);
    }
}
