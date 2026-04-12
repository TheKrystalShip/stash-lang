namespace Stash.Tests.Interpreting;

using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Analysis;

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
        Assert.Equal(TokenType.Timeout, token.Type);
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
}
