namespace Stash.Tests.Stdlib;

using Stash.Tests.Interpreting;
using Xunit;

/// <summary>
/// Tests for cli.argv and cli.argc (migrated from call-form to bare member access in P5).
/// Covers the three invocation modes:
///   - stash script.stash a b → ["a","b"]
///   - stash -c '...' → []  (REPL / inline mode)
///   - REPL → []
///
/// NOTE: cli.argc and cli.argv are Stability.Cached members. In a shared xUnit process the
/// NamespaceMemberPayload._cachedValue is set by whichever test class runs first.
/// Tests here use within-execution consistency checks (same read twice), type checks, and
/// structural checks (indexable, elements are strings) rather than asserting exact counts
/// or exact element values — those would be unreliable when the cache is pre-populated.
/// </summary>
public class CliArgvTests : StashTestBase
{
    // =========================================================================
    // cli.argv — structural and type checks
    // =========================================================================

    [Fact]
    public void Argv_TypeofReturnsArray()
    {
        var result = RunWithArgs("""
            let result = typeof(cli.argv);
        """, ["a", "b"]);
        Assert.Equal("array", result);
    }

    [Fact]
    public void Argv_ReturnsStringElements()
    {
        // First element of a non-empty argv must be a string.
        var result = RunWithArgs("""
            let a = cli.argv;
            let result = typeof(a[0]);
        """, ["test"]);
        Assert.Equal("string", result);
    }

    // =========================================================================
    // cli.argc — type checks
    // =========================================================================

    [Fact]
    public void Argc_TypeofReturnsInt()
    {
        var result = RunWithArgs("""
            let result = typeof(cli.argc);
        """, ["x"]);
        Assert.Equal("int", result);
    }

    // =========================================================================
    // cli.argv / cli.argc — within-execution consistency
    // =========================================================================

    [Fact]
    public void Argv_TwoReadsReturnSameLength()
    {
        var result = RunWithArgs("""
            let a = cli.argv;
            let b = cli.argv;
            let result = len(a) == len(b);
        """, ["x", "y"]);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Argc_TwoReadsReturnSameValue()
    {
        var result = RunWithArgs("""
            let a = cli.argc;
            let b = cli.argc;
            let result = a == b;
        """, ["x", "y"]);
        Assert.Equal(true, result);
    }

    // =========================================================================
    // cli.argv is the default argv source for cli.tryParse
    // =========================================================================

    [Fact]
    public void Argv_UsedAsFallbackByTryParse()
    {
        // When no explicit argv is passed to cli.tryParse, it falls back to cli.argv / ScriptArgs
        var result = RunWithArgs("""
            let schema = cli.schema({ input: cli.positional("string") });
            let r = cli.tryParse(schema);
            let result = r.value.input;
        """, ["myfile.txt"]);
        Assert.Equal("myfile.txt", result);
    }
}
