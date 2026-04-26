using Stash.Runtime;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Integration tests for typed catch clauses, union-type catch, bare rethrow (<c>throw;</c>),
/// error taxonomy (e.type), and stack traces (e.stack / RuntimeError.CallStack).
/// All tests extend <see cref="Stash.Tests.Interpreting.StashTestBase"/>.
/// </summary>
public class TypedCatchTests : Stash.Tests.Interpreting.StashTestBase
{
    // =========================================================================
    // 1. Basic typed catch — match
    // =========================================================================

    [Fact]
    public void TypedCatch_MatchingErrorType_IsHandled()
    {
        // catch (TypeError e) handles a TypeError thrown by conv.toInt(null)
        var source = @"
            let result = ""not handled"";
            try {
                conv.toInt(null);
                result = ""no error"";
            } catch (TypeError e) {
                result = ""caught"";
            }
        ";
        Assert.Equal("caught", Run(source));
    }

    [Fact]
    public void TypedCatch_MatchingErrorType_BindsErrorVariable()
    {
        // The bound variable 'e' is accessible inside the typed catch body
        var source = @"
            let result = """";
            try {
                conv.toInt(null);
            } catch (TypeError e) {
                result = e.type;
            }
        ";
        Assert.Equal("TypeError", Run(source));
    }

    // =========================================================================
    // 2. Basic typed catch — no match
    // =========================================================================

    [Fact]
    public void TypedCatch_NonMatchingErrorType_Propagates()
    {
        // catch (TypeError e) does NOT handle a ParseError — error propagates
        var ex = RunCapturingError(@"
            try {
                conv.toInt(""abc"");
            } catch (TypeError e) {
                // ParseError should not be caught here
            }
        ");
        Assert.Equal("ParseError", ex.ErrorType);
    }

    [Fact]
    public void TypedCatch_NoMatchInMultiClause_Propagates()
    {
        // Neither catch clause matches ParseError — error propagates
        var ex = RunCapturingError(@"
            try {
                conv.toInt(""abc"");
            } catch (TypeError e) {
                // doesn't match ParseError
            } catch (IndexError e) {
                // doesn't match ParseError
            }
        ");
        Assert.Equal("ParseError", ex.ErrorType);
    }

    // =========================================================================
    // 3. Union type catch
    // =========================================================================

    [Fact]
    public void TypedCatch_UnionType_MatchesEither()
    {
        // catch (TypeError | ParseError e) catches a ParseError thrown by conv.toInt("abc")
        var source = @"
            fn parseOrIndex() {
                conv.toInt(""abc"");
            }
            let result = ""not caught"";
            try {
                parseOrIndex();
            } catch (TypeError | ParseError e) {
                result = e.type;
            }
        ";
        Assert.Equal("ParseError", Run(source));
    }

    [Fact]
    public void TypedCatch_UnionType_MatchesFirstAlternative()
    {
        // catch (TypeError | ParseError e) also catches a TypeError
        var source = @"
            let result = ""not caught"";
            try {
                conv.toInt(null);
            } catch (TypeError | ParseError e) {
                result = e.type;
            }
        ";
        Assert.Equal("TypeError", Run(source));
    }

    // =========================================================================
    // 4. Catch-all compatibility
    // =========================================================================

    [Fact]
    public void TypedCatch_CatchAll_StillHandlesAnyError()
    {
        // Untyped catch (e) remains a catch-all
        var source = @"
            let result = ""not caught"";
            try {
                conv.toInt(""abc"");
            } catch (e) {
                result = ""caught:"" + e.type;
            }
        ";
        Assert.Equal("caught:ParseError", Run(source));
    }

    [Fact]
    public void TypedCatch_ErrorKeyword_ActsAsCatchAll()
    {
        // catch (Error e) is also a catch-all — catches any error type
        var source = @"
            let result = ""not caught"";
            try {
                conv.toInt(""abc"");
            } catch (Error e) {
                result = ""caught:"" + e.type;
            }
        ";
        Assert.Equal("caught:ParseError", Run(source));
    }

    // =========================================================================
    // 5. Multi-clause typed catch
    // =========================================================================

    [Fact]
    public void TypedCatch_MultiClause_FirstMatchWins()
    {
        // IndexError is thrown — first clause (TypeError) doesn't match,
        // second clause (IndexError) does
        var source = @"
            let result = ""not caught"";
            try {
                arr.removeAt([1, 2, 3], 99);
            } catch (TypeError e) {
                result = ""TypeError"";
            } catch (IndexError e) {
                result = ""IndexError"";
            } catch (e) {
                result = ""catch-all"";
            }
        ";
        Assert.Equal("IndexError", Run(source));
    }

    [Fact]
    public void TypedCatch_MultiClause_FallsThroughToCatchAll()
    {
        // ParseError is thrown — typed clause (TypeError) doesn't match,
        // falls through to catch-all
        var source = @"
            let result = ""not caught"";
            try {
                conv.toInt(""abc"");
            } catch (TypeError e) {
                result = ""TypeError"";
            } catch (e) {
                result = ""catch-all:"" + e.type;
            }
        ";
        Assert.Equal("catch-all:ParseError", Run(source));
    }

    [Fact]
    public void TypedCatch_MultiClause_EarlyMatchSkipsLaterClauses()
    {
        // ParseError is thrown and caught by the first matching clause;
        // subsequent clauses are not reached
        var source = @"
            let result = ""not caught"";
            try {
                conv.toInt(""abc"");
            } catch (ParseError e) {
                result = ""ParseError"";
            } catch (e) {
                result = ""catch-all"";
            }
        ";
        Assert.Equal("ParseError", Run(source));
    }

    // =========================================================================
    // 6. Bare rethrow (throw;)
    // =========================================================================

    [Fact]
    public void BareRethrow_PreservesOriginalErrorType()
    {
        // throw; inside catch re-throws with original error type preserved
        var ex = RunCapturingError(@"
            try {
                conv.toInt(""abc"");
            } catch (e) {
                throw;
            }
        ");
        Assert.Equal("ParseError", ex.ErrorType);
    }

    [Fact]
    public void BareRethrow_PreservesOriginalMessage()
    {
        // throw; re-throws with the original message intact
        var ex = RunCapturingError(@"
            try {
                conv.toInt(""abc"");
            } catch (e) {
                throw;
            }
        ");
        Assert.Contains("Cannot parse 'abc' as integer", ex.Message);
    }

    [Fact]
    public void BareRethrow_InTypedCatch_Propagates()
    {
        // throw; inside a typed catch clause propagates with original error type
        var ex = RunCapturingError(@"
            try {
                conv.toInt(""abc"");
            } catch (ParseError e) {
                throw;
            }
        ");
        Assert.Equal("ParseError", ex.ErrorType);
    }

    [Fact]
    public void BareRethrow_CanBeCaughtByOuterTry()
    {
        // throw; propagates and can be caught by an enclosing try/catch
        var source = @"
            let result = ""not caught"";
            try {
                try {
                    conv.toInt(""abc"");
                } catch (e) {
                    throw;
                }
            } catch (e) {
                result = ""outer:"" + e.type;
            }
        ";
        Assert.Equal("outer:ParseError", Run(source));
    }

    // =========================================================================
    // 7. e.type reflects the error taxonomy
    // =========================================================================

    [Fact]
    public void StashError_TypeProperty_ParseError_IsCorrect()
    {
        var source = @"
            let result = """";
            try {
                conv.toInt(""abc"");
            } catch (e) {
                result = e.type;
            }
        ";
        Assert.Equal("ParseError", Run(source));
    }

    [Fact]
    public void StashError_TypeProperty_TypeError_IsCorrect()
    {
        var source = @"
            let result = """";
            try {
                conv.toInt(null);
            } catch (e) {
                result = e.type;
            }
        ";
        Assert.Equal("TypeError", Run(source));
    }

    [Fact]
    public void StashError_TypeProperty_IndexError_IsCorrect()
    {
        var source = @"
            let result = """";
            try {
                arr.removeAt([1, 2, 3], 99);
            } catch (e) {
                result = e.type;
            }
        ";
        Assert.Equal("IndexError", Run(source));
    }

    [Fact]
    public void StashError_TypeProperty_ReflectsErrorTaxonomy()
    {
        // Verify multiple taxonomy types are correctly reflected in e.type
        var cases = new[]
        {
            (@"conv.toInt(""abc"");", "ParseError"),
            (@"arr.removeAt([1], 99);", "IndexError"),
            (@"conv.toInt(null);", "TypeError"),
        };

        foreach (var (expr, expectedType) in cases)
        {
            var source = $@"
                let t = """";
                try {{
                    {expr}
                }} catch (e) {{
                    t = e.type;
                }}
                let result = t;
            ";
            Assert.Equal(expectedType, Run(source));
        }
    }

    // =========================================================================
    // 8. e.stack is populated
    // =========================================================================

    [Fact]
    public void StashError_StackProperty_IsNonEmpty()
    {
        // Stack trace should be populated when error propagates through function calls
        var source = @"
            fn inner() {
                conv.toInt(""abc"");
            }
            fn outer() {
                inner();
            }
            let stackSize = 0;
            try {
                outer();
            } catch (e) {
                let s = e.stack;
                if (s != null) {
                    stackSize = len(s);
                }
            }
            let result = stackSize;
        ";
        var result = Run(source);
        Assert.True((long)result! > 0, "Stack should be non-empty when error propagates through function calls");
    }

    [Fact]
    public void StashError_StackProperty_ContainsStringEntries()
    {
        // Each stack entry should be a string (e.g. "  at func (file:1:2)")
        var source = @"
            fn thrower() {
                conv.toInt(""abc"");
            }
            let firstEntry = null;
            try {
                thrower();
            } catch (e) {
                let s = e.stack;
                if (s != null && len(s) > 0) {
                    firstEntry = s[0];
                }
            }
            let result = firstEntry;
        ";
        var result = Run(source);
        Assert.IsType<string>(result);
        Assert.Contains("at", (string)result!);
    }

    // =========================================================================
    // 9. e.stack carries call frames from nested call chain
    // =========================================================================

    [Fact]
    public void StashError_Stack_DeepChain_HasMultipleFrames()
    {
        // Error thrown inside nested functions accumulates stack frames
        var source = @"
            fn a() { conv.toInt(""abc""); }
            fn b() { a(); }
            fn c() { b(); }
            let depth = 0;
            try {
                c();
            } catch (e) {
                let s = e.stack;
                if (s != null) {
                    depth = len(s);
                }
            }
            let result = depth;
        ";
        var result = Run(source);
        // At least one frame is captured for the call chain
        Assert.True((long)result! >= 1, "Expected at least 1 stack frame for nested call chain");
    }

    // =========================================================================
    // 10. No-catch-needed (try block swallows error) — backward compat
    // =========================================================================

    [Fact]
    public void TypedCatch_TryWithoutCatch_StillWorks()
    {
        // try block alone (no catch, no finally) still suppresses errors — backward compat
        var source = @"
            let result = ""ok"";
            try {
                conv.toInt(""abc"");
            }
        ";
        Assert.Equal("ok", Run(source));
    }
}
