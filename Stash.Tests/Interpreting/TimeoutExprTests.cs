namespace Stash.Tests.Interpreting;

using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Analysis;
using Stash.Runtime.Types;

public class TimeoutExprTests : StashTestBase
{
    private static List<Token> Scan(string source) => new Lexer(source).ScanTokens();

    private static List<Stmt> ParseProgram(string source)
    {
        var tokens = new Lexer(source).ScanTokens();
        return new Parser(tokens).ParseProgram();
    }

    private static string Format(string source) =>
        new StashFormatter(2, useTabs: false).Format(source);

    // ===== 1. Lexer Tests =====

    [Fact]
    public void Timeout_Keyword_IsRecognized()
    {
        var tokens = Scan("timeout");
        var token = tokens.First(t => t.Type != TokenType.Eof);
        Assert.Equal(TokenType.Identifier, token.Type);
        Assert.Equal("timeout", token.Lexeme);
    }

    // ===== 2. Parser Tests =====

    [Fact]
    public void Timeout_BasicParse_ProducesTimeoutExpr()
    {
        var stmts = ParseProgram("timeout 5s { 42; }");
        var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        var timeout = Assert.IsType<TimeoutExpr>(exprStmt.Expression);
        Assert.IsType<LiteralExpr>(timeout.Duration);
        Assert.NotNull(timeout.Body);
    }

    // ===== 3. Formatter Tests =====

    [Fact]
    public void Timeout_Format_RoundTrips()
    {
        string source = "let r = timeout 5s { 42; };";
        string first = Format(source);
        string second = Format(first);
        Assert.Equal(first, second);
    }

    // ===== 4. Basic Execution =====

    [Fact]
    public void Timeout_NoTimeout_ReturnsValue()
    {
        var result = Run("let result = timeout 5s { 42; };");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Timeout_StringResult_ReturnsString()
    {
        var result = Run("""let result = timeout 5s { "hello"; };""");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Timeout_MultiStatement_ReturnsLast()
    {
        var result = Run("""
            let result = timeout 5s {
                let x = 10;
                let y = 20;
                x + y;
            };
            """);
        Assert.Equal(30L, result);
    }

    [Fact]
    public void Timeout_NullBody_ReturnsNull()
    {
        var result = Run("let result = timeout 5s { let x = 1; };");
        Assert.Null(result);
    }

    // ===== 5. Timeout Behavior =====

    [Fact]
    public void Timeout_ExceedsDuration_ThrowsTimeoutError()
    {
        var err = RunCapturingError("""
            timeout 100ms {
                while (true) { }
            }
            """);
        Assert.Equal("TimeoutError", err.ErrorType);
        Assert.Contains("timed out", err.Message);
    }

    [Fact]
    public void Timeout_ErrorIsCatchable_WithTry()
    {
        var result = Run("""
            let e = try timeout 100ms {
                while (true) { }
            };
            let result = e is Error;
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Timeout_ErrorType_IsTimeoutError()
    {
        var result = Run("""
            let e = try timeout 100ms {
                while (true) { }
            };
            let result = e.type;
            """);
        Assert.Equal("TimeoutError", result);
    }

    // ===== 6. Duration Expressions =====

    [Fact]
    public void Timeout_DurationVariable_Works()
    {
        var result = Run("""
            let dur = 5s;
            let result = timeout dur { 42; };
            """);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Timeout_IntegerMilliseconds_Works()
    {
        var result = Run("let result = timeout 5000 { 42; };");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Timeout_FunctionCallDuration_Works()
    {
        var result = Run("""
            fn getDur() { return 5s; }
            let result = timeout getDur() { 42; };
            """);
        Assert.Equal(42L, result);
    }

    // ===== 7. Nesting =====

    [Fact]
    public void Timeout_Nested_InnerTimesOutFirst()
    {
        var result = Run("""
            let inner = try timeout 100ms {
                while (true) { }
            };
            let result = timeout 5s { inner is Error; };
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Timeout_Nested_OuterStillCountsDown()
    {
        var result = Run("""
            let e = try timeout 200ms {
                let inner = try timeout 5s { 1; };
                while (true) { }
            };
            let result = e.type;
            """);
        Assert.Equal("TimeoutError", result);
    }

    [Fact]
    public void Timeout_TryExprInBody_ReturnsError()
    {
        var result = Run("""
            let result = timeout 5s {
                let e = try (1 / 0);
                e is Error;
            };
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Timeout_TryTimeoutInBody_ReturnsError()
    {
        var result = Run("""
            let result = timeout 10s {
                let inner = try timeout 100ms {
                    while (true) { }
                };
                inner is Error;
            };
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Timeout_TryTimeoutInBody_ErrorType()
    {
        var result = Run("""
            let result = timeout 10s {
                let inner = try timeout 100ms {
                    while (true) { }
                };
                inner.type;
            };
            """);
        Assert.Equal("TimeoutError", result);
    }

    [Fact]
    public void Timeout_TryCatchInBody_CatchesError()
    {
        var result = Run("""
            let result = timeout 5s {
                let msg = "";
                try {
                    throw "test error";
                } catch (e) {
                    msg = e.message;
                }
                msg;
            };
            """);
        Assert.Equal("test error", result);
    }

    // ===== 8. Error Conditions =====

    [Fact]
    public void Timeout_ZeroDuration_Throws()
    {
        var err = RunCapturingError("timeout 0s { 42; }");
        Assert.Contains("positive", err.Message);
    }

    [Fact]
    public void Timeout_NegativeDuration_Throws()
    {
        var err = RunCapturingError("timeout (-1) { 42; }");
        Assert.Contains("positive", err.Message);
    }

    [Fact]
    public void Timeout_NonDuration_Throws()
    {
        var err = RunCapturingError("""timeout "bad" { 42; }""");
        Assert.Contains("duration", err.Message);
    }

    // ===== 9. Composition with Other Features =====

    [Fact]
    public void Timeout_WithRetry_Composes()
    {
        var result = Run("let result = retry (3) { return timeout 5s { 42; }; };");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Timeout_BodyException_Propagates()
    {
        var err = RunCapturingError("""timeout 5s { throw "oops"; }""");
        Assert.Equal("oops", err.Message);
    }

    [Fact]
    public void Timeout_ClosureCapture_Works()
    {
        var result = Run("""
            let x = 10;
            let result = timeout 5s { x + 5; };
            """);
        Assert.Equal(15L, result);
    }

    #region Soft Keyword — Identifier Uses

    [Fact]
    public void Timeout_VariableDeclaration_ParsesAsLetStmt()
    {
        var result = Run("let timeout = 30s; let result = timeout;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(30_000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Timeout_VariableAssignment_Works()
    {
        var result = Run("let timeout = 5s; timeout = 10s; let result = timeout;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(10_000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Timeout_AsParameterName_Works()
    {
        var result = Run("fn f(timeout) { return timeout; } let result = f(99);");
        Assert.Equal(99L, result);
    }

    [Fact]
    public void Timeout_InForLoopVariable_Works()
    {
        var result = Run("let arr = [1, 2, 3]; let result = 0; for (let timeout in arr) { result = result + timeout; }");
        Assert.Equal(6L, result);
    }

    [Fact]
    public void Timeout_PropertyAccess_Works()
    {
        var result = Run("let obj = { timeout: 42 }; let result = obj.timeout;");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Timeout_DictKey_Works()
    {
        var result = Run("let cfg = { timeout: 30 }; let result = cfg[\"timeout\"];");
        Assert.Equal(30L, result);
    }

    [Fact]
    public void Timeout_InArithmeticExpression_Works()
    {
        var result = Run("let timeout = 10; let result = timeout + 5;");
        Assert.Equal(15L, result);
    }

    [Fact]
    public void Timeout_InReturnStatement_Works()
    {
        var result = Run("fn f() { let timeout = 77; return timeout; } let result = f();");
        Assert.Equal(77L, result);
    }

    [Fact]
    public void Timeout_ClosureCapture_AsVariable_Works()
    {
        var result = Run("let timeout = 5; fn f() { return timeout; } let result = f();");
        Assert.Equal(5L, result);
    }

    [Fact]
    public void Timeout_SelfReferencing_OuterIsKeyword_InnerIsVariable()
    {
        // timeout timeout { } — outer is TimeoutExpr keyword, inner is the duration variable
        var result = Run("let timeout = 100ms; let result = timeout timeout { 42; };");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Timeout_KeywordExpressionStillWorks()
    {
        var result = Run("let result = timeout 30s { 42; };");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Timeout_GroupedDuration_Works()
    {
        var result = Run("let result = timeout (5s + 5s) { 99; };");
        Assert.Equal(99L, result);
    }

    [Fact]
    public void Timeout_FunctionCallDuration_ViaNamedFunction_Works()
    {
        var result = Run("fn getDuration() { return 100ms; } let result = timeout getDuration() { 55; };");
        Assert.Equal(55L, result);
    }

    [Fact]
    public void Timeout_AmbiguousParenCall_ParsesAsCall()
    {
        // timeout(x) — timeout as callable identifier, not TimeoutExpr
        var result = Run("fn timeout(x) { return x * 2; } let result = timeout(21);");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Timeout_IsCheckOnVariable_Works()
    {
        var result = Run("let timeout = 5; let result = timeout is int;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Timeout_StructFieldNamed_Works()
    {
        var result = Run("struct Config { timeout: int } let c = Config { timeout: 30 }; let result = c.timeout;");
        Assert.Equal(30L, result);
    }

    #endregion
}
