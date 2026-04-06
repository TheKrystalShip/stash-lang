using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Analysis;

namespace Stash.Tests.Interpreting;

public class RetryExprTests : StashTestBase
{
    private static List<Token> Scan(string source) => new Lexer(source).ScanTokens();

    private static List<Stmt> ParseProgram(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.ParseProgram();
    }

    private static string Format(string source) =>
        new StashFormatter(2, useTabs: false).Format(source);

    // ===== 1. Lexer Tests =====

    [Fact]
    public void Lexer_Retry_ProducesKeywordToken()
    {
        var tokens = Scan("retry");
        var token = tokens.First(t => t.Type != TokenType.Eof);
        Assert.Equal(TokenType.Retry, token.Type);
        Assert.Equal("retry", token.Lexeme);
    }

    [Fact]
    public void Lexer_RetryPrefix_NotConfusedWithIdentifier()
    {
        var tokens = Scan("let retry_count = 0;");
        var identToken = tokens.First(t => t.Lexeme == "retry_count");
        Assert.Equal(TokenType.Identifier, identToken.Type);
    }

    // ===== 2. Parser Tests =====

    [Fact]
    public void Parser_RetryMinimal_ParsesCorrectly()
    {
        var stmts = ParseProgram("retry (3) { let x = 1; }");
        var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        var retry = Assert.IsType<RetryExpr>(exprStmt.Expression);
        Assert.IsType<LiteralExpr>(retry.MaxAttempts);
        Assert.Null(retry.NamedOptions);
        Assert.Null(retry.OptionsExpr);
        Assert.Null(retry.UntilClause);
        Assert.Null(retry.OnRetryClause);
        Assert.NotNull(retry.Body);
    }

    [Fact]
    public void Parser_RetryWithNamedOptions_ParsesOptions()
    {
        var stmts = ParseProgram("retry (5, delay: 1s, backoff: Backoff.Exponential) { doWork(); }");
        var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        var retry = Assert.IsType<RetryExpr>(exprStmt.Expression);
        Assert.NotNull(retry.NamedOptions);
        Assert.Equal(2, retry.NamedOptions!.Count);
        Assert.Equal("delay", retry.NamedOptions[0].Name.Lexeme);
        Assert.Equal("backoff", retry.NamedOptions[1].Name.Lexeme);
    }

    [Fact]
    public void Parser_RetryWithUntilLambda_ParsesUntilClause()
    {
        var stmts = ParseProgram("retry (3) until (r) => r == true { checkStatus(); }");
        var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        var retry = Assert.IsType<RetryExpr>(exprStmt.Expression);
        Assert.NotNull(retry.UntilClause);
        Assert.Null(retry.OnRetryClause);
    }

    [Fact]
    public void Parser_RetryWithOnRetryInline_ParsesOnRetryClause()
    {
        var stmts = ParseProgram("retry (3) onRetry (n, err) { log(); } { doWork(); }");
        var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        var retry = Assert.IsType<RetryExpr>(exprStmt.Expression);
        Assert.NotNull(retry.OnRetryClause);
        Assert.False(retry.OnRetryClause!.IsReference);
        Assert.Null(retry.UntilClause);
    }

    [Fact]
    public void Parser_RetryWithUntilAndOnRetry_ParsesBoth()
    {
        var stmts = ParseProgram("retry (3) onRetry (n, err) { log(); } until (r) => r { doWork(); }");
        var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        var retry = Assert.IsType<RetryExpr>(exprStmt.Expression);
        Assert.NotNull(retry.OnRetryClause);
        Assert.NotNull(retry.UntilClause);
    }

    [Fact]
    public void Parser_RetryOnRetryWithTypeHints_PopulatesTypeHintTokens()
    {
        var stmts = ParseProgram("retry (3) onRetry (n: int, err: Error) { } { }");
        var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        var retry = Assert.IsType<RetryExpr>(exprStmt.Expression);
        Assert.NotNull(retry.OnRetryClause);
        var onRetry = retry.OnRetryClause!;
        Assert.Equal("n", onRetry.ParamAttempt!.Lexeme);
        Assert.Equal("int", onRetry.ParamAttemptTypeHint!.Lexeme);
        Assert.Equal("err", onRetry.ParamError!.Lexeme);
        Assert.Equal("Error", onRetry.ParamErrorTypeHint!.Lexeme);
    }

    // ===== 3. Interpreter Tests — Basic Success/Failure =====

    [Fact]
    public void Retry_SucceedsFirst_ReturnsValue()
    {
        var result = Run("let result = retry (3) { return 42; };");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Retry_SucceedsAfterFailures_ReturnsValue()
    {
        var result = Run("""
            let attempts = 0;
            let result = retry (5) {
                attempts = attempts + 1;
                if (attempts < 3) { throw "fail"; }
                return "success";
            };
            """);
        Assert.Equal("success", result);
    }

    [Fact]
    public void Retry_AllAttemptsExhausted_ThrowsLastError()
    {
        var err = RunCapturingError("retry (3) { throw \"always fails\"; }");
        Assert.Equal("always fails", err.Message);
    }

    [Fact]
    public void Retry_MaxAttempts1_ExecutesOnce()
    {
        var result = Run("""
            let attempts = 0;
            try retry (1) {
                attempts = attempts + 1;
                throw "fail";
            };
            let result = attempts;
            """);
        Assert.Equal(1L, result);
    }

    [Fact]
    public void Retry_MaxAttempts0_ThrowsRetryExhaustedError()
    {
        var err = RunCapturingError("retry (0) { return 42; }");
        Assert.Equal("RetryExhaustedError", err.ErrorType);
    }

    [Fact]
    public void Retry_NegativeAttempts_ThrowsImmediately()
    {
        var err = RunCapturingError("retry (-1) { return 42; }");
        Assert.Contains("non-negative", err.Message);
    }

    [Fact]
    public void Retry_NonIntegerAttempts_ThrowsError()
    {
        var err = RunCapturingError("retry (\"three\") { return 42; }");
        Assert.Contains("integer", err.Message);
    }

    // ===== 4. Expression Return Value =====

    [Fact]
    public void Retry_AsExpression_ReturnsBodyValue()
    {
        var result = Run("let result = retry (1) { return 42; };");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Retry_AsExpression_ReturnsLastExpressionValue()
    {
        var result = Run("let result = retry (1) { return 5 * 2; };");
        Assert.Equal(10L, result);
    }

    [Fact]
    public void Retry_WithTry_CatchesExhaustion()
    {
        var result = Run("""
            let r = try retry (2) { throw "fail"; };
            let result = r is Error;
            """);
        Assert.Equal(true, result);
    }

    // ===== 5. until Clause =====

    [Fact]
    public void Retry_Until_SucceedsWhenPredicateTrue()
    {
        var result = Run("""
            let counter = 0;
            let result = retry (5) until (r) => r >= 3 {
                counter = counter + 1;
                return counter;
            };
            """);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Retry_Until_ExhaustedThrowsRetryExhaustedError()
    {
        var err = RunCapturingError("retry (3) until (r) => false { return 42; }");
        Assert.Equal("RetryExhaustedError", err.ErrorType);
        Assert.Contains("exhausted", err.Message);
    }

    [Fact]
    public void Retry_Until_NamedFunction()
    {
        var result = Run("""
            fn isReady(r) { return r == "ready"; }
            let counter = 0;
            let result = retry (5) until isReady {
                counter = counter + 1;
                let status = "";
                if (counter < 3) { status = "not ready"; } else { status = "ready"; }
                return status;
            };
            """);
        Assert.Equal("ready", result);
    }

    [Fact]
    public void Retry_Until_TwoParams()
    {
        var result = Run("""
            let result = retry (5) until (r, n) => n >= 3 {
                return "value";
            };
            """);
        Assert.Equal("value", result);
    }

    [Fact]
    public void Retry_Until_WithExceptions_RetriesBoth()
    {
        var result = Run("""
            let counter = 0;
            let result = retry (5) until (r) => r == "ok" {
                counter = counter + 1;
                if (counter == 1) { throw "error"; }
                let val = "";
                if (counter < 4) { val = "not ok"; } else { val = "ok"; }
                return val;
            };
            """);
        Assert.Equal("ok", result);
    }

    // ===== 6. onRetry Hook =====

    [Fact]
    public void Retry_OnRetryInline_Called()
    {
        var result = Run("""
            let log = [];
            let attempts = 0;
            retry (3) onRetry (n, err) {
                arr.push(log, n);
            } {
                attempts = attempts + 1;
                if (attempts < 3) { throw "fail"; }
                "done";
            }
            let result = len(log);
            """);
        Assert.Equal(2L, result);
    }

    [Fact]
    public void Retry_OnRetryFunction_Called()
    {
        var result = Run("""
            let log = [];
            fn myHook(n, err) { arr.push(log, n); }
            let attempts = 0;
            retry (3) onRetry myHook {
                attempts = attempts + 1;
                if (attempts < 3) { throw "fail"; }
                "done";
            }
            let result = len(log);
            """);
        Assert.Equal(2L, result);
    }

    [Fact]
    public void Retry_OnRetryNotCalledOnSuccess()
    {
        var result = Run("""
            let called = false;
            retry (3) onRetry (n, err) { called = true; } { 42; }
            let result = called;
            """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void Retry_OnRetryNotCalledAfterLastFailure()
    {
        var result = Run("""
            let count = 0;
            try retry (2) onRetry (n, err) { count = count + 1; } { throw "fail"; };
            let result = count;
            """);
        Assert.Equal(1L, result);
    }

    // ===== 7. Named Options =====

    [Fact]
    public void Retry_BackoffEnum_Accessible()
    {
        var result = Run("let result = typeof(Backoff);");
        Assert.Equal("enum", result);
    }

    [Fact]
    public void Retry_BackoffValues()
    {
        var result = Run("""
            let result = "${Backoff.Fixed},${Backoff.Linear},${Backoff.Exponential}";
            """);
        Assert.Equal("Backoff.Fixed,Backoff.Linear,Backoff.Exponential", result);
    }

    [Fact]
    public void Retry_WithDelay_Parses()
    {
        var result = Run("let result = retry (1, delay: 0s) { return 42; };");
        Assert.Equal(42L, result);
    }

    // ===== 8. Attempt Context =====

    [Fact]
    public void Retry_AttemptContext_Current()
    {
        var result = Run("""
            let attempts = 0;
            let result = retry (3) {
                attempts = attempts + 1;
                if (attempts < 3) { throw "fail"; }
                return attempt.current;
            };
            """);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Retry_AttemptContext_Max()
    {
        var result = Run("let result = retry (5) { return attempt.max; };");
        Assert.Equal(5L, result);
    }

    [Fact]
    public void Retry_AttemptContext_Remaining()
    {
        var result = Run("let result = retry (1) { return attempt.remaining; };");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Retry_AttemptContext_ErrorsAccumulate()
    {
        var result = Run("""
            let attempts = 0;
            let result = retry (3) {
                attempts = attempts + 1;
                if (attempts < 3) { throw "fail"; }
                return len(attempt.errors);
            };
            """);
        Assert.Equal(2L, result);
    }

    [Fact]
    public void Retry_AttemptContext_ErrorsEmptyOnFirst()
    {
        var result = Run("let result = retry (1) { return len(attempt.errors); };");
        Assert.Equal(0L, result);
    }

    // ===== 9. Error Type Filtering (on option) =====

    [Fact]
    public void Retry_OnFilter_RetriesMatchingType()
    {
        var result = Run("""
            let attempts = 0;
            let result = retry (3, on: ["CustomError"]) {
                attempts = attempts + 1;
                if (attempts < 3) { throw { type: "CustomError", message: "temp" }; }
                return "done";
            };
            """);
        Assert.Equal("done", result);
    }

    [Fact]
    public void Retry_OnFilter_PropagatesNonMatchingType()
    {
        var err = RunCapturingError("""
            retry (3, on: ["NetworkError"]) {
                throw { type: "AuthError", message: "unauthorized" };
            }
            """);
        Assert.Equal("AuthError", err.ErrorType);
    }

    // ===== 10. Composability =====

    [Fact]
    public void Retry_TryRetry_CatchesExhaustion()
    {
        var result = Run("""
            let r = try retry (2) { throw "fail"; };
            let result = r is Error;
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Retry_NestedRetry_InnerExhaustsOuterRetries()
    {
        var result = Run("""
            let r = try retry (2) {
                retry (2) { throw "inner fail"; }
            };
            let result = r is Error;
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Retry_InsideTryCatch_CaughtByEnclosingCatch()
    {
        var result = Run("""
            let caught = "";
            try {
                retry (2) { throw "fail"; }
            } catch (e) {
                caught = e.message;
            }
            let result = caught;
            """);
        Assert.Equal("fail", result);
    }

    // ===== 11. Scope Tests =====

    [Fact]
    public void Retry_FreshScopePerAttempt()
    {
        // Uses attempt.current directly (no let inside body to avoid resolver slot collision)
        var result = Run("""
            let result = retry (3) {
                if (attempt.current < 3) { throw "fail"; }
                attempt.current;
            };
            """);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Retry_OuterScopeAccess_MutatesOuter()
    {
        var result = Run("""
            let counter = 0;
            retry (3) {
                counter = counter + 1;
                if (counter < 3) { throw "fail"; }
                "done";
            }
            let result = counter;
            """);
        Assert.Equal(3L, result);
    }

    // ===== 12. Return/Break/Continue Propagation =====

    [Fact]
    public void Retry_ReturnInsideBody_ReturnsRetryValue()
    {
        // In the bytecode VM, return inside retry body returns from the closure
        // (providing the successful value), not from the enclosing function
        var result = Run("""
            fn test() {
                let r = retry (3) {
                    return 42;
                };
                return r;
            }
            let result = test();
            """);
        Assert.Equal(42L, result);
    }


    // === Resolver Slot Fix Tests ===

    [Fact]
    public void Retry_LetInBody_WithAttemptAccess_Works()
    {
        // Verifies that let declarations don't collide with the attempt context
        var result = Run(@"
let attempts = 0;
let result = retry (3) {
    let count = attempt.current;
    attempts = attempts + 1;
    if (attempts < 3) { throw ""fail""; }
    return count;
};
");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Retry_MultipleLetInBody_WithAttempt_Works()
    {
        var result = Run(@"
let attempts = 0;
let result = retry (3) {
    let a = attempt.current;
    let b = attempt.max;
    attempts = attempts + 1;
    if (attempts < 3) { throw ""fail""; }
    return a + b;
};
");
        Assert.Equal(6L, result);
    }

    [Fact]
    public void Retry_LetInOnRetryBody_Works()
    {
        // Verifies that let declarations in onRetry inline body work with hook params
        var result = Run(@"
let log = [];
let attempts = 0;
retry (3) onRetry (n, err) {
    let msg = ""attempt "" + conv.toStr(n);
    arr.push(log, msg);
} {
    attempts = attempts + 1;
    if (attempts < 3) { throw ""fail""; }
    return ""done"";
};
let result = len(log);
");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void Retry_Until_PredicateThrows_PropagatesImmediately()
    {
        // Spec §5.5: predicate throws → error propagates immediately (not retried)
        var result = Run(@"
let attempts = 0;
let err = try retry (5) until (r) => {
    throw { type: ""PredicateFailure"", message: ""bad predicate"" };
} {
    attempts = attempts + 1;
    42;
};
let result = attempts;
");
        // Should be 1 — predicate throws after first successful body execution,
        // error propagates immediately, no more attempts
        Assert.Equal(1L, result);
    }

    [Fact]
    public void Retry_OnRetryHookThrows_PropagatesImmediately_PredicatePath()
    {
        // Spec §6.4: hook throws → propagates immediately
        var result = Run(@"
let attempts = 0;
let err = try retry (5) onRetry (n, e) {
    throw ""hook failed"";
} until (r) => false {
    attempts = attempts + 1;
    42;
};
let result = attempts;
");
        // Body succeeds, predicate fails, hook throws → propagates after attempt 1
        Assert.Equal(1L, result);
    }
}
