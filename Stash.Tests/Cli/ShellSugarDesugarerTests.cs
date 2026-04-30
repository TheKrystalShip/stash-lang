using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Stash.Cli.Shell;
using Stash.Runtime;

namespace Stash.Tests.Cli;

/// <summary>
/// Unit tests for <see cref="ShellSugarDesugarer"/> — the §11.2 shell built-in desugaring
/// logic. Tests operate directly on the desugarer, bypassing the runner and VM.
/// </summary>
public class ShellSugarDesugarerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Parses a raw line into a <see cref="ShellCommandLine"/> for test use.</summary>
    private static ShellCommandLine ParseLine(string line) => ShellLineLexer.Parse(line);

    // ── IsSugarName ───────────────────────────────────────────────────────────

    [Fact]
    public void IsSugarName_CdPwdExitQuit_True()
    {
        Assert.True(ShellSugarDesugarer.IsSugarName("cd"));
        Assert.True(ShellSugarDesugarer.IsSugarName("pwd"));
        Assert.True(ShellSugarDesugarer.IsSugarName("exit"));
        Assert.True(ShellSugarDesugarer.IsSugarName("quit"));
    }

    [Fact]
    public void IsSugarName_Other_False()
    {
        Assert.False(ShellSugarDesugarer.IsSugarName("ls"));
        Assert.False(ShellSugarDesugarer.IsSugarName("echo"));
        Assert.False(ShellSugarDesugarer.IsSugarName("CD"));     // case-sensitive
        Assert.False(ShellSugarDesugarer.IsSugarName(""));
    }

    // ── cd ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Cd_OneArg_DesugarsToChdir()
    {
        var line = ParseLine("cd /tmp");
        string? source = ShellSugarDesugarer.TryDesugar(line, ["/tmp"]);
        Assert.Equal("env.chdir(\"/tmp\");", source);
    }

    [Fact]
    public void Cd_NoArgs_DesugarsToChdirHome()
    {
        var line = ParseLine("cd");
        string? source = ShellSugarDesugarer.TryDesugar(line, []);

        // On POSIX the home variable is HOME; on Windows it is USERPROFILE.
        string expectedVar = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "USERPROFILE"
            : "HOME";
        Assert.Equal($"env.chdir(env.get(\"{expectedVar}\"));", source);
    }

    [Fact]
    public void Cd_DashLiteral_DesugarsToPopDirAndPrint()
    {
        var line = ParseLine("cd -");
        string? source = ShellSugarDesugarer.TryDesugar(line, ["-"]);
        Assert.Equal("env.popDir(); io.println(env.cwd());", source);
    }

    [Fact]
    public void Cd_TooManyArgs_ThrowsCommandError()
    {
        var line = ParseLine("cd /a /b");
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.TryDesugar(line, ["/a", "/b"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Equal("cd: too many arguments", ex.Message);
    }

    [Fact]
    public void Cd_ArgWithSpecialChars_EscapesProperly()
    {
        // A path containing a double-quote and backslash must be escaped.
        string path = "/home/user/my\"path\\here";
        var line = ParseLine("cd /tmp"); // program name only; expanded arg provided separately
        string? source = ShellSugarDesugarer.TryDesugar(line, [path]);
        Assert.Equal("env.chdir(\"/home/user/my\\\"path\\\\here\");", source);
    }

    [Fact]
    public void Cd_ArgWithNewline_EscapesNewline()
    {
        string path = "/tmp/a\nb";
        var line = ParseLine("cd /tmp");
        string? source = ShellSugarDesugarer.TryDesugar(line, [path]);
        Assert.Equal("env.chdir(\"/tmp/a\\nb\");", source);
    }

    // ── pwd ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Pwd_NoArgs_DesugarsToPrintCwd()
    {
        var line = ParseLine("pwd");
        string? source = ShellSugarDesugarer.TryDesugar(line, []);
        Assert.Equal("io.println(env.cwd());", source);
    }

    [Fact]
    public void Pwd_WithArg_ThrowsCommandError()
    {
        var line = ParseLine("pwd /tmp");
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.TryDesugar(line, ["/tmp"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Equal("pwd: too many arguments", ex.Message);
    }

    // ── exit ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Exit_NoArgs_DesugarsToExitZero()
    {
        var line = ParseLine("exit");
        string? source = ShellSugarDesugarer.TryDesugar(line, []);
        Assert.Equal("env.exit(0);", source);
    }

    [Fact]
    public void Exit_NumericArg_DesugarsToExitWithCode()
    {
        var line = ParseLine("exit 7");
        string? source = ShellSugarDesugarer.TryDesugar(line, ["7"]);
        Assert.Equal("env.exit(7);", source);
    }

    [Fact]
    public void Exit_NonNumericArg_ThrowsCommandError()
    {
        var line = ParseLine("exit abc");
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.TryDesugar(line, ["abc"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Equal("exit: numeric argument required", ex.Message);
    }

    [Fact]
    public void Exit_TooManyArgs_ThrowsCommandError()
    {
        var line = ParseLine("exit 0 1");
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.TryDesugar(line, ["0", "1"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Equal("exit: too many arguments", ex.Message);
    }

    // ── quit (alias of exit) ──────────────────────────────────────────────────

    [Fact]
    public void Quit_AliasOfExit_SameDesugaring()
    {
        var exitLine = ParseLine("exit 3");
        var quitLine = ParseLine("quit 3");
        string? exitSource = ShellSugarDesugarer.TryDesugar(exitLine, ["3"]);
        string? quitSource = ShellSugarDesugarer.TryDesugar(quitLine, ["3"]);
        // Both should produce the same snippet (exit code literal, not "exit"/"quit" in source).
        Assert.Equal("env.exit(3);", exitSource);
        Assert.Equal("env.exit(3);", quitSource);
    }

    [Fact]
    public void Quit_TooManyArgs_ThrowsCommandErrorWithQuitName()
    {
        var line = ParseLine("quit 0 1");
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.TryDesugar(line, ["0", "1"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Equal("quit: too many arguments", ex.Message);
    }

    [Fact]
    public void Quit_NonNumericArg_ThrowsCommandErrorWithQuitName()
    {
        var line = ParseLine("quit abc");
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.TryDesugar(line, ["abc"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Equal("quit: numeric argument required", ex.Message);
    }

    // ── piped / redirected lines are NOT desugared ────────────────────────────

    [Fact]
    public void Piped_ReturnsNull()
    {
        // pwd | cat — 2 stages → no desugaring.
        var line = ParseLine("pwd | cat");
        string? source = ShellSugarDesugarer.TryDesugar(line, []);
        Assert.Null(source);
    }

    [Fact]
    public void Redirected_ReturnsNull()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        // pwd > /dev/null — has a redirect → no desugaring.
        var line = ParseLine("pwd > /dev/null");
        string? source = ShellSugarDesugarer.TryDesugar(line, []);
        Assert.Null(source);
    }

    // ── EscapeForStashString coverage ─────────────────────────────────────────

    [Fact]
    public void Escape_TabCharacter_EscapesCorrectly()
    {
        string result = ShellSugarDesugarer.EscapeForStashString("a\tb");
        Assert.Equal("a\\tb", result);
    }

    [Fact]
    public void Escape_CarriageReturn_EscapesCorrectly()
    {
        string result = ShellSugarDesugarer.EscapeForStashString("a\rb");
        Assert.Equal("a\\rb", result);
    }

    // ── history ───────────────────────────────────────────────────────────────

    [Fact]
    public void History_NoArgs_ReturnsForLoop()
    {
        var line = ParseLine("history");
        string? source = ShellSugarDesugarer.TryDesugar(line, []);
        Assert.Equal("for entry in process.historyList() { io.println(entry); }", source);
    }

    [Fact]
    public void History_DashC_ReturnsClear()
    {
        var line = ParseLine("history -c");
        string? source = ShellSugarDesugarer.TryDesugar(line, ["-c"]);
        Assert.Equal("process.historyClear();", source);
    }

    [Fact]
    public void History_PositiveInt_ReturnsSliceLoop()
    {
        var line = ParseLine("history 5");
        string? source = ShellSugarDesugarer.TryDesugar(line, ["5"]);
        Assert.NotNull(source);
        Assert.Contains("process.historyList()", source);
        Assert.Contains("5", source);
    }

    [Fact]
    public void History_Zero_ReturnsNullStatement()
    {
        var line = ParseLine("history 0");
        string? source = ShellSugarDesugarer.TryDesugar(line, ["0"]);
        Assert.Equal("null;", source);
    }

    [Fact]
    public void History_NegativeInt_ThrowsCommandError()
    {
        var line = ParseLine("history -1");
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.TryDesugar(line, ["-1"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
    }

    [Fact]
    public void History_NonNumericArg_ThrowsCommandError()
    {
        var line = ParseLine("history foo");
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.TryDesugar(line, ["foo"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
    }

    [Fact]
    public void History_TooManyArgs_ThrowsCommandError()
    {
        var line = ParseLine("history 5 extra");
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.TryDesugar(line, ["5", "extra"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
    }

    [Fact]
    public void IsSugarName_History_True()
    {
        Assert.True(ShellSugarDesugarer.IsSugarName("history"));
    }
}
