using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Guards the invariant that every VM catch handler uses <c>StashError.FromRuntimeError</c>
/// rather than constructing <see cref="StashError"/> directly. Direct construction silently omits
/// <see cref="StashError.BuiltInClrType"/> (required for typed-catch CLR-identity matching) and
/// <see cref="StashError.OriginalException"/> (required for bare <c>throw;</c> to preserve source
/// span and call stack). These fields are not visible from Stash code, so behavioral tests cannot
/// detect their absence — these tests inspect the C# objects directly.
/// </summary>
public class StashErrorFieldTests : Stash.Tests.Interpreting.StashTestBase
{
    // =========================================================================
    // 1. BuiltInClrType — main VM catch handler
    // =========================================================================

    [Fact]
    public void StashError_BuiltInClrType_IsSetForTypeError()
    {
        // conv.toInt(null) → TypeError from the main VM dispatch handler
        var source = @"
            let result = null;
            try { conv.toInt(null); } catch (e) { result = e; }
        ";
        var se = Assert.IsType<StashError>(Run(source));
        Assert.Equal(typeof(TypeError), se.BuiltInClrType);
    }

    [Fact]
    public void StashError_BuiltInClrType_IsSetForParseError()
    {
        // conv.toInt("abc") → ParseError from the main VM dispatch handler
        var source = @"
            let result = null;
            try { conv.toInt(""abc""); } catch (e) { result = e; }
        ";
        var se = Assert.IsType<StashError>(Run(source));
        Assert.Equal(typeof(ParseError), se.BuiltInClrType);
    }

    [Fact]
    public void StashError_BuiltInClrType_IsSetForIndexError()
    {
        // arr.removeAt out-of-range → IndexError from the main VM dispatch handler
        var source = @"
            let result = null;
            try { arr.removeAt([1, 2, 3], 99); } catch (e) { result = e; }
        ";
        var se = Assert.IsType<StashError>(Run(source));
        Assert.Equal(typeof(IndexError), se.BuiltInClrType);
    }

    [Fact]
    public void StashError_BuiltInClrType_IsSetForValueError()
    {
        // conv.toInt with unsupported base → ValueError from the main VM dispatch handler
        var source = @"
            let result = null;
            try { conv.toInt(""0"", 3); } catch (e) { result = e; }
        ";
        var se = Assert.IsType<StashError>(Run(source));
        Assert.Equal(typeof(ValueError), se.BuiltInClrType);
    }

    // =========================================================================
    // 2. OriginalException — main VM catch handler
    // =========================================================================

    [Fact]
    public void StashError_OriginalException_IsSetForCaughtBuiltInError()
    {
        // OriginalException must be the original C# RuntimeError that was thrown.
        // Used by bare throw; to rethrow with the original source span intact.
        var source = @"
            let result = null;
            try { conv.toInt(null); } catch (e) { result = e; }
        ";
        var se = Assert.IsType<StashError>(Run(source));
        Assert.NotNull(se.OriginalException);
        Assert.IsType<TypeError>(se.OriginalException);
    }

    [Fact]
    public void StashError_OriginalException_TypeMatchesBuiltInClrType()
    {
        // OriginalException's CLR type must equal BuiltInClrType — they must agree.
        var source = @"
            let result = null;
            try { conv.toInt(""abc""); } catch (e) { result = e; }
        ";
        var se = Assert.IsType<StashError>(Run(source));
        Assert.NotNull(se.OriginalException);
        Assert.Equal(se.BuiltInClrType, se.OriginalException!.GetType());
    }

    // =========================================================================
    // 3. User-thrown errors — BuiltInClrType must be null
    //    (Guards correct null assignment, not the bypass bug, but needed for
    //    completeness: a regression that always sets BuiltInClrType would break
    //    user-thrown errors whose type names don't correspond to any CLR type.)
    // =========================================================================

    [Fact]
    public void StashError_BuiltInClrType_IsNullForUserThrownDictError()
    {
        // Dict-thrown errors have a user-supplied type string; no CLR type backs them.
        var source = @"
            let result = null;
            try {
                throw { type: ""CustomError"", message: ""user error"" };
            } catch (e) {
                result = e;
            }
        ";
        var se = Assert.IsType<StashError>(Run(source));
        Assert.Null(se.BuiltInClrType);
    }

    // =========================================================================
    // 4. Defer path — VirtualMachine.Functions.cs defer-cleanup handlers
    //    Errors thrown inside defer blocks are collected as suppressed errors
    //    on the propagating exception. The defer handlers must also use
    //    FromRuntimeError so that BuiltInClrType and OriginalException are set.
    // =========================================================================

    [Fact]
    public void StashError_BuiltInClrType_IsSetForDeferSuppressedError()
    {
        // TypeError thrown inside a defer block → suppressed on the ParseError.
        // Guards VirtualMachine.Functions.cs defer-cleanup catch handler.
        var source = @"
            let result = null;
            fn throws() {
                defer { conv.toInt(null); }
                conv.toInt(""abc"");
            }
            try {
                throws();
            } catch (e) {
                result = e.suppressed[0];
            }
        ";
        var se = Assert.IsType<StashError>(Run(source));
        Assert.Equal(typeof(TypeError), se.BuiltInClrType);
    }

    [Fact]
    public void StashError_OriginalException_IsSetForDeferSuppressedError()
    {
        // OriginalException must be populated for suppressed errors from defer blocks.
        var source = @"
            let result = null;
            fn throws() {
                defer { conv.toInt(null); }
                conv.toInt(""abc"");
            }
            try {
                throws();
            } catch (e) {
                result = e.suppressed[0];
            }
        ";
        var se = Assert.IsType<StashError>(Run(source));
        Assert.NotNull(se.OriginalException);
        Assert.IsType<TypeError>(se.OriginalException);
    }
}
