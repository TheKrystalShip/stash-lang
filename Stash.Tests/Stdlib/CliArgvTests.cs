namespace Stash.Tests.Stdlib;

using Stash.Tests.Interpreting;
using Xunit;

/// <summary>
/// Tests for cli.argv() and cli.argc().
/// Covers the three invocation modes:
///   - stash script.stash a b → ["a","b"]
///   - stash -c '...' → []  (REPL / inline mode)
///   - REPL → []
/// </summary>
public class CliArgvTests : StashTestBase
{
    // =========================================================================
    // cli.argv() — returns ScriptArgs as array<string>
    // =========================================================================

    [Fact]
    public void Argv_NoArgs_ReturnsEmptyArray()
    {
        var result = Run("""
            let result = len(cli.argv());
        """);
        // Default RunWithArgs passes no args → empty
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Argv_WithArgs_ReturnsAllArgs()
    {
        var result = RunWithArgs("""
            let result = len(cli.argv());
        """, ["a", "b"]);
        Assert.Equal(2L, result);
    }

    [Fact]
    public void Argv_WithArgs_ReturnsCorrectFirstArg()
    {
        var result = RunWithArgs("""
            let a = cli.argv();
            let result = a[0];
        """, ["hello", "world"]);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Argv_WithArgs_ReturnsCorrectSecondArg()
    {
        var result = RunWithArgs("""
            let a = cli.argv();
            let result = a[1];
        """, ["hello", "world"]);
        Assert.Equal("world", result);
    }

    [Fact]
    public void Argv_InlineMode_ReturnsEmptyArray()
    {
        // Simulates `stash -c '...'` invocation — ScriptArgs is null or empty
        var result = RunWithArgs("""
            let result = len(cli.argv());
        """, []);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Argv_ReplMode_ReturnsEmptyArray()
    {
        // REPL mode → ScriptArgs is null → cli.argv() returns []
        var result = Run("""
            let result = len(cli.argv());
        """);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Argv_ReturnsStringElements()
    {
        var result = RunWithArgs("""
            let a = cli.argv();
            let result = typeof(a[0]);
        """, ["test"]);
        Assert.Equal("string", result);
    }

    // =========================================================================
    // cli.argc() — returns ScriptArgs.Length as int
    // =========================================================================

    [Fact]
    public void Argc_NoArgs_ReturnsZero()
    {
        var result = Run("""
            let result = cli.argc();
        """);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Argc_WithArgs_ReturnsCorrectCount()
    {
        var result = RunWithArgs("""
            let result = cli.argc();
        """, ["a", "b", "c"]);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Argc_InlineMode_ReturnsZero()
    {
        var result = RunWithArgs("""
            let result = cli.argc();
        """, []);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Argc_ReturnsInt()
    {
        var result = RunWithArgs("""
            let result = typeof(cli.argc());
        """, ["x"]);
        Assert.Equal("int", result);
    }

    // =========================================================================
    // cli.argv() / cli.argc() consistency
    // =========================================================================

    [Fact]
    public void ArgvAndArgc_Consistent()
    {
        var result = RunWithArgs("""
            let result = len(cli.argv()) == cli.argc();
        """, ["a", "b", "c"]);
        Assert.Equal(true, result);
    }

    // =========================================================================
    // cli.argv() is the default argv source for cli.tryParse
    // =========================================================================

    [Fact]
    public void Argv_UsedAsFallbackByTryParse()
    {
        // When no explicit argv is passed to cli.tryParse, it falls back to cli.argv() / ScriptArgs
        var result = RunWithArgs("""
            let schema = cli.schema({ input: cli.positional("string") });
            let r = cli.tryParse(schema);
            let result = r.value.input;
        """, ["myfile.txt"]);
        Assert.Equal("myfile.txt", result);
    }
}
