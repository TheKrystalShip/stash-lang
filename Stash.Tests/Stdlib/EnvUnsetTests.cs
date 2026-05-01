using System;
using Stash.Runtime;

namespace Stash.Tests.Stdlib;

/// <summary>
/// Tests for <c>env.unset(name)</c> — the built-in that removes an OS environment variable.
/// Covers spec §9.4.
/// All tests use names prefixed with <c>STASH_TEST_UNSET_</c> to avoid interfering with
/// real environment variables. Cleanup is performed in try/finally blocks.
/// </summary>
public class EnvUnsetTests : Stash.Tests.Interpreting.StashTestBase
{
    // Unique prefix to avoid collisions with real env vars.
    private const string VarPrefix = "STASH_TEST_UNSET_";

    // =========================================================================
    // §9.4 Test #1 — env.unset returns true when the variable existed
    // =========================================================================

    [Fact]
    public void EnvUnset_ExistingVar_ReturnsTrue()
    {
        string varName = VarPrefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        System.Environment.SetEnvironmentVariable(varName, "hello");
        try
        {
            var result = Run($"""
                env.set("{varName}", "hello");
                let result = env.unset("{varName}");
                """);
            Assert.Equal(true, result);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
        }
    }

    // =========================================================================
    // §9.4 Test #2 — env.unset returns false when the variable did not exist
    // =========================================================================

    [Fact]
    public void EnvUnset_NeverSetVar_ReturnsFalse()
    {
        // Use a unique name that is guaranteed not to be set in the test environment.
        string varName = VarPrefix + "NEVER_SET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        System.Environment.SetEnvironmentVariable(varName, null); // ensure not set
        try
        {
            var result = Run($"""let result = env.unset("{varName}");""");
            Assert.Equal(false, result);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
        }
    }

    // =========================================================================
    // §9.4 Test #3 — env.unset("") throws ValueError
    // =========================================================================

    [Fact]
    public void EnvUnset_EmptyName_ThrowsValueError()
    {
        var ex = RunCapturingError("""env.unset("");""");
        Assert.Equal(StashErrorTypes.ValueError, ex.ErrorType);
    }

    // =========================================================================
    // §9.4 Test #4 — env.unset("A=B") throws ValueError
    // =========================================================================

    [Fact]
    public void EnvUnset_NameWithEquals_ThrowsValueError()
    {
        var ex = RunCapturingError("""env.unset("A=B");""");
        Assert.Equal(StashErrorTypes.ValueError, ex.ErrorType);
    }

    // =========================================================================
    // §9.4 Test #5 — env.unset(42) throws TypeError
    // =========================================================================

    [Fact]
    public void EnvUnset_NonStringArg_ThrowsTypeError()
    {
        // Spec §8: TypeError if `name` is not a string.
        // The implementation delegates to SvArgs.String which raises a RuntimeError;
        // the test verifies the error type is TypeError as specified.
        var ex = RunCapturingError("env.unset(42);");
        Assert.Equal(StashErrorTypes.TypeError, ex.ErrorType);
    }

    // =========================================================================
    // §9.4 Test #6 — after env.unset, env.get returns null
    // =========================================================================

    [Fact]
    public void EnvUnset_AfterUnset_GetReturnsNull()
    {
        string varName = VarPrefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        System.Environment.SetEnvironmentVariable(varName, "value");
        try
        {
            var result = Run($"""
                env.set("{varName}", "value");
                env.unset("{varName}");
                let result = env.get("{varName}");
                """);
            Assert.Null(result);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
        }
    }
}
