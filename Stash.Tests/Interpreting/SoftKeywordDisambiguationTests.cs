using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

namespace Stash.Tests.Interpreting;

public class SoftKeywordDisambiguationTests : StashTestBase
{
    private static List<Token> Scan(string source) => new Lexer(source).ScanTokens();

    private static List<Stmt> ParseProgram(string source)
    {
        var tokens = new Lexer(source).ScanTokens();
        return new Parser(tokens).ParseProgram();
    }

    // ===== 1. Lexer Tests — all soft keywords produce Identifier tokens =====

    [Fact]
    public void Async_LexedAs_Identifier()
    {
        var token = Scan("async").First(t => t.Type != TokenType.Eof);
        Assert.Equal(TokenType.Identifier, token.Type);
        Assert.Equal("async", token.Lexeme);
    }

    [Fact]
    public void Await_LexedAs_Identifier()
    {
        var token = Scan("await").First(t => t.Type != TokenType.Eof);
        Assert.Equal(TokenType.Identifier, token.Type);
        Assert.Equal("await", token.Lexeme);
    }

    [Fact]
    public void Defer_LexedAs_Identifier()
    {
        var token = Scan("defer").First(t => t.Type != TokenType.Eof);
        Assert.Equal(TokenType.Identifier, token.Type);
        Assert.Equal("defer", token.Lexeme);
    }

    [Fact]
    public void Lock_LexedAs_Identifier()
    {
        var token = Scan("lock").First(t => t.Type != TokenType.Eof);
        Assert.Equal(TokenType.Identifier, token.Type);
        Assert.Equal("lock", token.Lexeme);
    }

    [Fact]
    public void Elevate_LexedAs_Identifier()
    {
        var token = Scan("elevate").First(t => t.Type != TokenType.Eof);
        Assert.Equal(TokenType.Identifier, token.Type);
        Assert.Equal("elevate", token.Lexeme);
    }

    [Fact]
    public void Retry_LexedAs_Identifier()
    {
        var token = Scan("retry").First(t => t.Type != TokenType.Eof);
        Assert.Equal(TokenType.Identifier, token.Type);
        Assert.Equal("retry", token.Lexeme);
    }

    // ===== 2. Identifier usage — keywords as variable names parse as identifiers =====

    [Fact]
    public void Async_UsedAsVariableName_ParsesAsIdentifier()
    {
        var stmts = ParseProgram("let async = 42;");
        Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
    }

    [Fact]
    public void Await_UsedAsVariableName_ParsesAsIdentifier()
    {
        var stmts = ParseProgram("let await = 42;");
        Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
    }

    [Fact]
    public void Defer_UsedAsVariableName_ParsesAsIdentifier()
    {
        // 'defer' followed by '=' is a variable declaration, not DeferStmt
        var stmts = ParseProgram("let defer = 42;");
        Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
    }

    [Fact]
    public void Lock_UsedAsVariableName_ParsesAsIdentifier()
    {
        var stmts = ParseProgram("let lock = 42;");
        Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
    }

    [Fact]
    public void Elevate_UsedAsVariableName_ParsesAsIdentifier()
    {
        var stmts = ParseProgram("let elevate = 42;");
        Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
    }

    [Fact]
    public void Retry_UsedAsVariableName_ParsesAsIdentifier()
    {
        var stmts = ParseProgram("let retry = 42;");
        Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
    }

    // ===== 3. Keyword disambiguation — keywords in keyword positions parse as AST nodes =====

    [Fact]
    public void Defer_FollowedByIdentifier_ParsesAsDeferStmt()
    {
        // defer followed by an identifier → DeferStmt
        var stmts = ParseProgram("fn noop() {} defer noop();");
        Assert.Equal(2, stmts.Count);
        Assert.IsType<DeferStmt>(stmts[1]);
    }

    [Fact]
    public void Defer_FollowedByBlock_ParsesAsDeferStmt()
    {
        var stmts = ParseProgram("defer { 42; }");
        Assert.IsType<DeferStmt>(Assert.Single(stmts));
    }

    [Fact]
    public void Elevate_FollowedByBlock_ParsesAsElevateStmt()
    {
        var stmts = ParseProgram("elevate { 42; }");
        Assert.IsType<ElevateStmt>(Assert.Single(stmts));
    }

    [Fact]
    public void Lock_FollowedByStringPath_ParsesAsLockStmt()
    {
        var stmts = ParseProgram("lock \"/tmp/test.lock\" { 42; }");
        Assert.IsType<LockStmt>(Assert.Single(stmts));
    }

    [Fact]
    public void Retry_FollowedByParenAndBlock_ParsesAsRetryExpr()
    {
        // retry(n) { body } → RetryExpr in ExprStmt
        var stmts = ParseProgram("retry (1) { 42; }");
        var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        Assert.IsType<RetryExpr>(exprStmt.Expression);
    }

    [Fact]
    public void Async_FollowedByFn_ParsesAsAsyncFnDecl()
    {
        var stmts = ParseProgram("async fn greet() { }");
        var fnDecl = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.True(fnDecl.IsAsync);
    }

    [Fact]
    public void Await_FollowedByExpression_ParsesAsAwaitExpr()
    {
        // await followed by a function call → AwaitExpr in VarDeclStmt initializer
        var stmts = ParseProgram("async fn f() { } let x = await f();");
        Assert.Equal(2, stmts.Count);
        var varDecl = Assert.IsType<VarDeclStmt>(stmts[1]);
        Assert.IsType<AwaitExpr>(varDecl.Initializer);
    }

    // ===== 4. Disambiguation — function calls on keyword-named variables =====

    [Fact]
    public void Defer_FollowedByParen_ParsesAsFunctionCall()
    {
        // defer(...) → function call on 'defer' variable, NOT DeferStmt
        // IsDeferKeyword() returns false when next token is LeftParen
        var stmts = ParseProgram("defer(5);");
        var callStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        Assert.IsType<CallExpr>(callStmt.Expression);
    }

    [Fact]
    public void Elevate_FollowedByParenButNoBlock_ParsesAsFunctionCall()
    {
        // elevate(x) without a following { } → function call, NOT ElevateStmt
        // IsElevateKeyword() returns false when ) is not followed by {
        var stmts = ParseProgram("elevate(5);");
        var callStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        Assert.IsType<CallExpr>(callStmt.Expression);
    }

    [Fact]
    public void Lock_FollowedByParenButNoBlock_ParsesAsFunctionCall()
    {
        // lock(x) where ) is not followed by { or lock options → function call, NOT LockStmt
        // IsLockKeyword() delegates to IsFollowedByLockOptionsOrBlockAfterParens which returns false
        var stmts = ParseProgram("lock(5);");
        var callStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        Assert.IsType<CallExpr>(callStmt.Expression);
    }

    [Fact]
    public void Retry_FollowedByParenButNoRetryClause_ParsesAsFunctionCall()
    {
        // retry(x) where ) is not followed by a retry clause → function call, NOT RetryExpr
        // IsRetryKeyword() calls IsFollowedByRetryClauseAfterParens which returns false here
        var stmts = ParseProgram("retry(5);");
        var callStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        Assert.IsType<CallExpr>(callStmt.Expression);
    }

    // ===== 5. Runtime tests — keywords used as identifiers work at runtime =====

    [Fact]
    public void Async_AsVariableName_WorksAtRuntime()
    {
        var result = Run("let async = 42; let result = async;");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Await_AsVariableName_WorksAtRuntime()
    {
        var result = Run("let await = \"hello\"; let result = await;");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Defer_AsVariableName_WorksAtRuntime()
    {
        var result = Run("let defer = 99; let result = defer;");
        Assert.Equal(99L, result);
    }

    [Fact]
    public void Lock_AsVariableName_WorksAtRuntime()
    {
        var result = Run("let lock = true; let result = lock;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Elevate_AsVariableName_WorksAtRuntime()
    {
        var result = Run("let elevate = 3.14; let result = elevate;");
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void Retry_AsVariableName_WorksAtRuntime()
    {
        var result = Run("let retry = \"yes\"; let result = retry;");
        Assert.Equal("yes", result);
    }

    // ===== 6. Property access on keyword-named variables =====

    [Fact]
    public void Async_PropertyAccess_ParsesAsIdentifier()
    {
        // async.something → async used as identifier with property access
        var stmts = ParseProgram("let async = { x: 1 }; async.x;");
        Assert.Equal(2, stmts.Count);
        var exprStmt = Assert.IsType<ExprStmt>(stmts[1]);
        Assert.IsType<DotExpr>(exprStmt.Expression);
    }
}
