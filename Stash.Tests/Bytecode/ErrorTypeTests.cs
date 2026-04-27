using Stash.Runtime;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Integration tests for struct-based error throw semantics — verifying that throwing
/// a built-in error type struct preserves the type name, message field, and additional
/// properties, and that typed catch clauses match (or skip) accordingly.
/// All tests extend <see cref="Stash.Tests.Interpreting.StashTestBase"/>.
/// </summary>
public class ErrorTypeTests : Stash.Tests.Interpreting.StashTestBase
{
    // =========================================================================
    // 1. Struct throw — error type is preserved
    // =========================================================================

    [Fact]
    public void StructThrow_ValueError_TypePreserved()
    {
        var ex = RunCapturingError(@"
            fn f() { throw ValueError { message: ""bad value"" }; }
            f();
        ");
        Assert.Equal(StashErrorTypes.ValueError, ex.ErrorType);
    }

    [Fact]
    public void StructThrow_TypeError_TypePreserved()
    {
        var ex = RunCapturingError(@"
            fn f() { throw TypeError { message: ""bad type"" }; }
            f();
        ");
        Assert.Equal(StashErrorTypes.TypeError, ex.ErrorType);
    }

    [Fact]
    public void StructThrow_ParseError_TypePreserved()
    {
        var ex = RunCapturingError(@"
            fn f() { throw ParseError { message: ""bad parse"" }; }
            f();
        ");
        Assert.Equal(StashErrorTypes.ParseError, ex.ErrorType);
    }

    // =========================================================================
    // 2. Struct throw — typed catch matches
    // =========================================================================

    [Fact]
    public void StructThrow_CaughtByTypedCatch_Matches()
    {
        var source = @"
            let result = ""not caught"";
            try {
                throw ValueError { message: ""x"" };
            } catch (ValueError e) {
                result = ""caught"";
            }
        ";
        Assert.Equal("caught", Run(source));
    }

    // =========================================================================
    // 3. Struct throw — wrong typed catch does NOT match
    // =========================================================================

    [Fact]
    public void StructThrow_NotCaughtByWrongType()
    {
        var ex = RunCapturingError(@"
            try {
                throw ValueError { message: ""wrong type"" };
            } catch (TypeError e) {
                // should not catch ValueError
            }
        ");
        Assert.Equal(StashErrorTypes.ValueError, ex.ErrorType);
    }

    // =========================================================================
    // 4. Struct throw — catch-all fallback
    // =========================================================================

    [Fact]
    public void StructThrow_CaughtByCatchAll_Fallback()
    {
        var source = @"
            let result = ""not caught"";
            try {
                throw ValueError { message: ""fallback"" };
            } catch (e) {
                result = ""caught"";
            }
        ";
        Assert.Equal("caught", Run(source));
    }

    // =========================================================================
    // 5. Struct throw — message field accessible in catch body
    // =========================================================================

    [Fact]
    public void StructThrow_MessageField_Accessible()
    {
        var source = @"
            let result = """";
            try {
                throw ValueError { message: ""hello from struct"" };
            } catch (ValueError e) {
                result = e.message;
            }
        ";
        Assert.Equal("hello from struct", Run(source));
    }

    // =========================================================================
    // 6. CommandError — additional properties accessible
    // =========================================================================

    [Fact]
    public void StructThrow_CommandError_PropertiesAccessible()
    {
        var source = @"
            let result = 0;
            try {
                throw CommandError { message: ""failed"", exitCode: 42, stderr: ""oops"", stdout: """", command: ""foo"" };
            } catch (CommandError e) {
                result = e.exitCode;
            }
        ";
        Assert.Equal(42L, Run(source));
    }

    // =========================================================================
    // 7. Backwards compatibility — dict throw still works
    // =========================================================================

    [Fact]
    public void DictThrow_StillWorks_BackwardsCompat()
    {
        var source = @"
            let result = """";
            try {
                throw { type: ""ValueError"", message: ""dict-thrown"" };
            } catch (ValueError e) {
                result = e.message;
            }
        ";
        Assert.Equal("dict-thrown", Run(source));
    }

    // =========================================================================
    // 8. Struct throw without message field — instance is stringified
    // =========================================================================

    [Fact]
    public void StructThrow_NoMessageField_StringifiesInstance()
    {
        // Define a custom struct with no message field and throw it.
        // The VM should stringify the instance as the error message.
        var ex = RunCapturingError(@"
            struct NoMsg { code: int }
            throw NoMsg { code: 1 };
        ");
        Assert.Equal("NoMsg", ex.ErrorType);
        Assert.False(string.IsNullOrEmpty(ex.Message));
    }

    // =========================================================================
    // LockError — registration and behavior
    // =========================================================================

    [Fact]
    public void LockError_IsCatchableByType()
    {
        var source = @"
            let result = ""not caught"";
            try {
                throw LockError { message: ""lock timeout"", path: ""/var/run/test.lock"" };
            } catch (LockError e) {
                result = ""caught"";
            }
        ";
        Assert.Equal("caught", Run(source));
    }

    [Fact]
    public void LockError_HasPathField()
    {
        var source = @"
            let result = """";
            try {
                throw LockError { message: ""lock timeout"", path: ""/var/run/test.lock"" };
            } catch (LockError e) {
                result = e.path;
            }
        ";
        Assert.Equal("/var/run/test.lock", Run(source));
    }

    [Fact]
    public void LockError_IsThrowableAsStruct_TypePreserved()
    {
        var ex = RunCapturingError(@"
            throw LockError { message: ""lock failed"", path: ""/tmp/test.lock"" };
        ");
        Assert.Equal(StashErrorTypes.LockError, ex.ErrorType);
    }
}
