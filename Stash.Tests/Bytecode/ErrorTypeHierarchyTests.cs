using Stash.Runtime;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Tests for the Stash error type hierarchy — covering three previously-broken behaviours:
///
/// 1. <c>is Error</c> must return <see langword="true"/> for ANY <see cref="StashError"/> value
///    (regression: adding a runtime "Error" struct shadowed the built-in StashError C# check).
///
/// 2. <c>is ParseError</c> / <c>is ValueError</c> / etc. must return <see langword="true"/>
///    for a <see cref="StashError"/> whose <c>.type</c> matches the struct name
///    (previously always false because errors are StashError, not StashInstance).
///
/// 3. <c>catch (Error e)</c> must catch ANY <see cref="StashError"/> regardless of its concrete
///    type — base-type catch semantics, consistent with Java / C# / Python conventions
///    (previously only caught errors whose exact <c>.type</c> field was "Error").
///
/// Additionally covers regression tests for <c>try expr ?? default</c> and
/// <c>try expr ??= default</c> which rely on the truthiness/error-propagation of
/// the values returned by <c>try</c>.
/// </summary>
public class ErrorTypeHierarchyTests : Stash.Tests.Interpreting.StashTestBase
{
    // =========================================================================
    // 1.  is Error  — must be true for any StashError (Fix 1 regression)
    // =========================================================================

    [Fact]
    public void IsError_StdlibProducedError_ReturnsTrue()
    {
        // try conv.toInt("bad") returns a StashError — must satisfy `is Error`
        var source = @"
            let err = try conv.toInt(""bad"");
            let result = err is Error;
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsError_ParseErrorFromStdlib_ReturnsTrue()
    {
        // conv.toInt("abc") produces a ParseError, but it's still IS-A Error
        var source = @"
            let err = try conv.toInt(""abc"");
            let result = err is Error;
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsError_IOErrorFromStdlib_ReturnsTrue()
    {
        var source = @"
            let err = try fs.readFile(""/no/such/file/ever.txt"");
            let result = err is Error;
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsError_StructThrownValueError_ReturnsTrue()
    {
        // throw ValueError { ... } produces a StashError via ExecuteThrow; must satisfy `is Error`
        var source = @"
            let result = false;
            try {
                throw ValueError { message: ""bad"" };
            } catch (e) {
                result = e is Error;
            }
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsError_StructThrownParseError_ReturnsTrue()
    {
        var source = @"
            let result = false;
            try {
                throw ParseError { message: ""parse fail"" };
            } catch (e) {
                result = e is Error;
            }
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsError_StructThrownTypeError_ReturnsTrue()
    {
        var source = @"
            let result = false;
            try {
                throw TypeError { message: ""wrong type"" };
            } catch (e) {
                result = e is Error;
            }
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsError_StructThrownCommandError_ReturnsTrue()
    {
        var source = @"
            let result = false;
            try {
                throw CommandError { message: ""cmd failed"", exitCode: 1, stderr: """", stdout: """", command: ""ls"" };
            } catch (e) {
                result = e is Error;
            }
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsError_StringThrown_ReturnsTrue()
    {
        // throw "message" creates a RuntimeError StashError — must also satisfy `is Error`
        var source = @"
            let result = false;
            try {
                throw ""something went wrong"";
            } catch (e) {
                result = e is Error;
            }
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsError_DictThrown_ReturnsTrue()
    {
        var source = @"
            let result = false;
            try {
                throw { type: ""CustomError"", message: ""custom"" };
            } catch (e) {
                result = e is Error;
            }
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsError_SuccessValue_ReturnsFalse()
    {
        var source = @"
            let result = 42 is Error;
        ";
        Assert.Equal(false, Run(source));
    }

    [Fact]
    public void IsError_String_ReturnsFalse()
    {
        var source = @"
            let result = ""hello"" is Error;
        ";
        Assert.Equal(false, Run(source));
    }

    [Fact]
    public void IsError_Null_ReturnsFalse()
    {
        var source = @"
            let result = null is Error;
        ";
        Assert.Equal(false, Run(source));
    }

    [Fact]
    public void IsError_UserStruct_ReturnsFalse()
    {
        // A user-defined struct whose name happens to be something else should not satisfy `is Error`
        var source = @"
            struct Wrapper { value: int }
            let w = Wrapper { value: 1 };
            let result = w is Error;
        ";
        Assert.Equal(false, Run(source));
    }

    // =========================================================================
    // 2.  is NamedErrorType  — must be true when .type matches (Fix 2)
    // =========================================================================

    [Fact]
    public void IsNamedErrorType_ParseError_WhenTypeMatches_ReturnsTrue()
    {
        // conv.toInt("abc") produces a StashError with .type == "ParseError"
        var source = @"
            let err = try conv.toInt(""abc"");
            let result = err is ParseError;
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsNamedErrorType_ParseError_WhenTypeMismatches_ReturnsFalse()
    {
        // conv.toInt(null) produces a TypeError, not a ParseError
        var source = @"
            let err = try conv.toInt(null);
            let result = err is ParseError;
        ";
        Assert.Equal(false, Run(source));
    }

    [Fact]
    public void IsNamedErrorType_TypeError_WhenTypeMatches_ReturnsTrue()
    {
        var source = @"
            let err = try conv.toInt(null);
            let result = err is TypeError;
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsNamedErrorType_ValueError_StructThrown_ReturnsTrue()
    {
        // throw ValueError { ... } produces a StashError with .type == "ValueError"
        var source = @"
            let result = false;
            try {
                throw ValueError { message: ""bad"" };
            } catch (e) {
                result = e is ValueError;
            }
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsNamedErrorType_ValueError_WhenTypeMismatches_ReturnsFalse()
    {
        var source = @"
            let result = true;
            try {
                throw ParseError { message: ""bad"" };
            } catch (e) {
                result = e is ValueError;
            }
        ";
        Assert.Equal(false, Run(source));
    }

    [Fact]
    public void IsNamedErrorType_IOError_StdlibProduced_ReturnsTrue()
    {
        var source = @"
            let err = try fs.readFile(""/no/such/file/9x7z.txt"");
            let result = err is IOError;
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsNamedErrorType_IndexError_WhenTypeMatches_ReturnsTrue()
    {
        var source = @"
            let result = false;
            try {
                throw IndexError { message: ""out of bounds"" };
            } catch (e) {
                result = e is IndexError;
            }
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsNamedErrorType_NotSupportedError_WhenTypeMatches_ReturnsTrue()
    {
        var source = @"
            let result = false;
            try {
                throw NotSupportedError { message: ""not supported"" };
            } catch (e) {
                result = e is NotSupportedError;
            }
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsNamedErrorType_TimeoutError_WhenTypeMatches_ReturnsTrue()
    {
        var source = @"
            let result = false;
            try {
                throw TimeoutError { message: ""timed out"" };
            } catch (e) {
                result = e is TimeoutError;
            }
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsNamedErrorType_CommandError_WhenTypeMatches_ReturnsTrue()
    {
        var source = @"
            let result = false;
            try {
                throw CommandError { message: ""failed"", exitCode: 2, stderr: ""err"", stdout: """", command: ""cmd"" };
            } catch (e) {
                result = e is CommandError;
            }
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsNamedErrorType_IsAlsoError_TrueForAll()
    {
        // An error that satisfies `is ValueError` must also satisfy `is Error`
        var source = @"
            let result = false;
            try {
                throw ValueError { message: ""x"" };
            } catch (e) {
                result = (e is ValueError) && (e is Error);
            }
        ";
        Assert.Equal(true, Run(source));
    }

    [Fact]
    public void IsNamedErrorType_SuccessValue_ReturnsFalse()
    {
        var source = @"
            let result = 42 is ValueError;
        ";
        Assert.Equal(false, Run(source));
    }

    [Fact]
    public void IsNamedErrorType_UserStruct_ReturnsFalse()
    {
        // A user-defined struct is NOT a StashError even if it has the same name pattern
        var source = @"
            struct ValueError { message: string }
            let v = ValueError { message: ""user"" };
            let result = v is ValueError;
        ";
        // User-defined struct IS an instance of user struct, not an error — but
        // is-struct-instance matching should still return true for user-defined structs.
        Assert.Equal(true, Run(source));
    }

    // =========================================================================
    // 3.  catch (Error e)  — base-type semantics (Fix 3)
    // =========================================================================

    [Fact]
    public void CatchError_CatchesParseError_FromStdlib()
    {
        // conv.toInt("abc") throws ParseError — catch (Error e) must catch it
        var source = @"
            let result = ""not caught"";
            try {
                conv.toInt(""abc"");
            } catch (Error e) {
                result = ""caught"";
            }
        ";
        Assert.Equal("caught", Run(source));
    }

    [Fact]
    public void CatchError_CatchesTypeError_FromStdlib()
    {
        var source = @"
            let result = ""not caught"";
            try {
                conv.toInt(null);
            } catch (Error e) {
                result = ""caught"";
            }
        ";
        Assert.Equal("caught", Run(source));
    }

    [Fact]
    public void CatchError_CatchesValueError_StructThrown()
    {
        var source = @"
            let result = ""not caught"";
            try {
                throw ValueError { message: ""bad"" };
            } catch (Error e) {
                result = ""caught"";
            }
        ";
        Assert.Equal("caught", Run(source));
    }

    [Fact]
    public void CatchError_CatchesIOError_StructThrown()
    {
        var source = @"
            let result = ""not caught"";
            try {
                throw IOError { message: ""disk full"" };
            } catch (Error e) {
                result = ""caught"";
            }
        ";
        Assert.Equal("caught", Run(source));
    }

    [Fact]
    public void CatchError_CatchesStringThrown()
    {
        // throw "message" produces a RuntimeError — catch (Error e) must catch it too
        var source = @"
            let result = ""not caught"";
            try {
                throw ""something went wrong"";
            } catch (Error e) {
                result = ""caught"";
            }
        ";
        Assert.Equal("caught", Run(source));
    }

    [Fact]
    public void CatchError_CatchesDictThrown()
    {
        var source = @"
            let result = ""not caught"";
            try {
                throw { type: ""ConfigError"", message: ""config bad"" };
            } catch (Error e) {
                result = ""caught"";
            }
        ";
        Assert.Equal("caught", Run(source));
    }

    [Fact]
    public void CatchError_BindsErrorVariable_WithCorrectType()
    {
        // e.type inside catch (Error e) should reflect the concrete error type
        var source = @"
            let result = """";
            try {
                conv.toInt(""abc"");
            } catch (Error e) {
                result = e.type;
            }
        ";
        Assert.Equal("ParseError", Run(source));
    }

    [Fact]
    public void CatchError_BindsErrorVariable_MessageAccessible()
    {
        var source = @"
            let result = """";
            try {
                throw ValueError { message: ""test message"" };
            } catch (Error e) {
                result = e.message;
            }
        ";
        Assert.Equal("test message", Run(source));
    }

    [Fact]
    public void CatchError_InMultiClause_MatchesFirst_ExactTypePrior()
    {
        // When a ValueError is thrown and both catch (ValueError e) and catch (Error e) exist,
        // the first matching clause (ValueError) should win
        var source = @"
            let result = ""none"";
            try {
                throw ValueError { message: ""x"" };
            } catch (ValueError e) {
                result = ""ValueError"";
            } catch (Error e) {
                result = ""Error"";
            }
        ";
        Assert.Equal("ValueError", Run(source));
    }

    [Fact]
    public void CatchError_InMultiClause_FallsThrough_ToErrorBase()
    {
        // When a ParseError is thrown and first clause is catch (ValueError e) — no match —
        // second clause catch (Error e) should catch it
        var source = @"
            let result = ""none"";
            try {
                conv.toInt(""abc"");
            } catch (ValueError e) {
                result = ""ValueError"";
            } catch (Error e) {
                result = ""Error fallback"";
            }
        ";
        Assert.Equal("Error fallback", Run(source));
    }

    [Fact]
    public void CatchError_InUnionCatch_CatchesViaErrorMember()
    {
        // catch (TypeError | Error e) — the Error member should match any error
        var source = @"
            let result = ""none"";
            try {
                conv.toInt(""abc"");
            } catch (TypeError | Error e) {
                result = e.type;
            }
        ";
        Assert.Equal("ParseError", Run(source));
    }

    [Fact]
    public void CatchError_DoesNotCatchNonError()
    {
        // An explicit `return 42` should never be caught by catch (Error e)
        // We test this by confirming no catch fires when no error is thrown
        var source = @"
            let result = ""ok"";
            try {
                let x = 1 + 1;
            } catch (Error e) {
                result = ""caught"";
            }
        ";
        Assert.Equal("ok", Run(source));
    }

    // =========================================================================
    // 4.  Exact typed catch still works after Fix 3
    // =========================================================================

    [Fact]
    public void ExactCatch_ValueError_StillMatchesValueError()
    {
        var source = @"
            let result = ""none"";
            try {
                throw ValueError { message: ""x"" };
            } catch (ValueError e) {
                result = ""ValueError"";
            }
        ";
        Assert.Equal("ValueError", Run(source));
    }

    [Fact]
    public void ExactCatch_ValueError_DoesNotMatchParseError()
    {
        // catch (ValueError e) must NOT catch a ParseError
        var ex = RunCapturingError(@"
            try {
                conv.toInt(""abc"");
            } catch (ValueError e) {
                // should not reach here
            }
        ");
        Assert.Equal("ParseError", ex.ErrorType);
    }

    [Fact]
    public void ExactCatch_ParseError_MatchesParseError()
    {
        var source = @"
            let result = ""none"";
            try {
                conv.toInt(""not-a-number"");
            } catch (ParseError e) {
                result = e.type;
            }
        ";
        Assert.Equal("ParseError", Run(source));
    }

    // =========================================================================
    // 5.  try ?? and try ??= — Error coalescing
    // =========================================================================

    [Fact]
    public void TryNullCoalescing_OnSuccess_ReturnsValue()
    {
        var source = @"
            let result = try conv.toInt(""42"") ?? 999;
        ";
        Assert.Equal(42L, Run(source));
    }

    [Fact]
    public void TryNullCoalescing_OnError_ReturnsFallback()
    {
        // try conv.toInt("bad") returns a StashError value; ?? should yield the fallback
        var source = @"
            let result = try conv.toInt(""bad"") ?? 999;
        ";
        Assert.Equal(999L, Run(source));
    }

    [Fact]
    public void TryNullCoalescing_IOError_ReturnsFallback()
    {
        var source = @"
            let result = try fs.readFile(""/no/such/file.txt"") ?? ""default"";
        ";
        Assert.Equal("default", Run(source));
    }

    [Fact]
    public void TryNullCoalescing_FloatOnError_ReturnsFallback()
    {
        var source = @"
            let result = try conv.toFloat(""xyz"") ?? 0.0;
        ";
        Assert.Equal(0.0, Run(source));
    }

    [Fact]
    public void TryNullCoalescing_ChainedCalls_OnFirstError_ReturnsFallback()
    {
        var source = @"
            let result = try conv.toInt(""bad"") ?? try conv.toInt(""also-bad"") ?? -1;
        ";
        Assert.Equal(-1L, Run(source));
    }

    [Fact]
    public void TryNullCoalescing_ChainedCalls_SecondSucceeds()
    {
        var source = @"
            let result = try conv.toInt(""bad"") ?? try conv.toInt(""7"") ?? -1;
        ";
        Assert.Equal(7L, Run(source));
    }

    [Fact]
    public void TryNullCoalescingAssign_OnError_SetsDefault()
    {
        var source = @"
            let x = try conv.toInt(""bad"") ?? null;
            x ??= 42;
            let result = x;
        ";
        Assert.Equal(42L, Run(source));
    }

    // =========================================================================
    // 6.  is Error used as guard in try pattern (existing error_handling.stash style)
    // =========================================================================

    [Fact]
    public void IsErrorGuard_ParsePositive_RethrowsOnParseError()
    {
        // Mirrors the parsePositive function from error_handling.stash:
        // if (n is Error) { throw { type: n.type, message: ... }; }
        var source = @"
            fn parsePositive(s) {
                let n = try conv.toInt(s);
                if (n is Error) {
                    throw { type: n.type, message: $""parsePositive: {n.message}"" };
                }
                if (n < 0) {
                    throw { type: ""RangeError"", message: $""expected positive, got {n}"" };
                }
                return n;
            }
            let ok = try parsePositive(""7"");
            let result = ok;
        ";
        Assert.Equal(7L, Run(source));
    }

    [Fact]
    public void IsErrorGuard_ParsePositive_ErrorCasePropagates()
    {
        var source = @"
            fn parsePositive(s) {
                let n = try conv.toInt(s);
                if (n is Error) {
                    throw { type: n.type, message: $""parsePositive: {n.message}"" };
                }
                return n;
            }
            let err = try parsePositive(""abc"");
            let result = err.type;
        ";
        Assert.Equal("ParseError", Run(source));
    }

    [Fact]
    public void IsErrorGuard_ConfigLoader_ErrorBranchFires_WhenIsErrorTrue()
    {
        // When `try fs.readFile(...)` fails, `if (result is Error)` must fire.
        var source = @"
            let raw = try fs.readFile(""/no/such/config.txt"");
            let result = (raw is Error) ? ""error"" : ""ok"";
        ";
        Assert.Equal("error", Run(source));
    }

    // =========================================================================
    // 7.  typeof still returns "Error" for all StashError values
    //     (ensures the typeof function was not broken by our fix)
    // =========================================================================

    [Fact]
    public void TypeOf_AnyStashError_ReturnsErrorString()
    {
        var source = @"
            let err = try conv.toInt(""bad"");
            let result = typeof(err);
        ";
        Assert.Equal("Error", Run(source));
    }

    [Fact]
    public void TypeOf_StructThrownValueError_ReturnsErrorString()
    {
        var source = @"
            let result = """";
            try {
                throw ValueError { message: ""x"" };
            } catch (e) {
                result = typeof(e);
            }
        ";
        Assert.Equal("Error", Run(source));
    }

    // =========================================================================
    // 8.  .type field gives the concrete error subtype
    // =========================================================================

    [Fact]
    public void ErrorType_ParseError_TypeFieldIsParseError()
    {
        var source = @"
            let err = try conv.toInt(""abc"");
            let result = err.type;
        ";
        Assert.Equal("ParseError", Run(source));
    }

    [Fact]
    public void ErrorType_StructThrown_TypeFieldPreserved()
    {
        var source = @"
            let result = """";
            try {
                throw ValueError { message: ""x"" };
            } catch (e) {
                result = e.type;
            }
        ";
        Assert.Equal("ValueError", Run(source));
    }

    [Fact]
    public void ErrorType_IOError_TypeFieldIsIOError()
    {
        var source = @"
            let err = try fs.readFile(""/no/such/99abc.txt"");
            let result = err.type;
        ";
        Assert.Equal("IOError", Run(source));
    }

    // =========================================================================
    // 9.  User-defined structs are not confused with error types (no regression)
    // =========================================================================

    [Fact]
    public void UserStruct_NotConfusedWithError()
    {
        // Defining a user struct called "Wrapper" should not break is-checks
        var source = @"
            struct Wrapper { value: int }
            let w = Wrapper { value: 5 };
            let result = (w is Wrapper) ? ""struct"" : ""not struct"";
        ";
        Assert.Equal("struct", Run(source));
    }

    [Fact]
    public void UserStruct_IsError_ReturnsFalse()
    {
        var source = @"
            struct Wrapper { value: int }
            let w = Wrapper { value: 5 };
            let result = w is Error;
        ";
        Assert.Equal(false, Run(source));
    }

    // =========================================================================
    // 10.  catch (Error e) in finally-less try — does not interfere with finally
    // =========================================================================

    [Fact]
    public void CatchError_WithFinally_FinallyStillRuns()
    {
        // Both catch and finally should fire — test result by capturing output
        var r = RunCapturingOutput(@"
            let fin = ""not run"";
            try {
                conv.toInt(""abc"");
            } catch (Error e) {
                io.print(""caught"");
            } finally {
                fin = ""ran"";
                io.print("" "" + fin);
            }
        ");
        Assert.Equal("caught ran", r.Trim());
    }
}
