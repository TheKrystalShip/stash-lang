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
    public void IsSugarName_CdPwdExitQuit_False_PhaseD()
    {
        // Phase D: these commands moved to built-in aliases; no longer handled by TryDesugar.
        Assert.False(ShellSugarDesugarer.IsSugarName("cd"));
        Assert.False(ShellSugarDesugarer.IsSugarName("pwd"));
        Assert.False(ShellSugarDesugarer.IsSugarName("exit"));
        Assert.False(ShellSugarDesugarer.IsSugarName("quit"));
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
        string source = ShellSugarDesugarer.DesugarCd(["/tmp"]);
        Assert.Equal("env.chdir(\"/tmp\");", source);
    }

    [Fact]
    public void Cd_NoArgs_DesugarsToChdirHome()
    {
        string source = ShellSugarDesugarer.DesugarCd([]);

        // On POSIX the home variable is HOME; on Windows it is USERPROFILE.
        string expectedVar = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "USERPROFILE"
            : "HOME";
        Assert.Equal($"env.chdir(env.get(\"{expectedVar}\"));", source);
    }

    [Fact]
    public void Cd_DashLiteral_DesugarsToPopDirAndPrint()
    {
        string source = ShellSugarDesugarer.DesugarCd(["-"]);
        Assert.Equal("env.popDir(); io.println(env.cwd());", source);
    }

    [Fact]
    public void Cd_TooManyArgs_ThrowsCommandError()
    {
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.DesugarCd(["/a", "/b"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Equal("cd: too many arguments", ex.Message);
    }

    [Fact]
    public void Cd_ArgWithSpecialChars_EscapesProperly()
    {
        // A path containing a double-quote and backslash must be escaped.
        string path = "/home/user/my\"path\\here";
        string source = ShellSugarDesugarer.DesugarCd([path]);
        Assert.Equal("env.chdir(\"/home/user/my\\\"path\\\\here\");", source);
    }

    [Fact]
    public void Cd_ArgWithNewline_EscapesNewline()
    {
        string path = "/tmp/a\nb";
        string source = ShellSugarDesugarer.DesugarCd([path]);
        Assert.Equal("env.chdir(\"/tmp/a\\nb\");", source);
    }

    // ── pwd ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Pwd_NoArgs_DesugarsToPrintCwd()
    {
        string source = ShellSugarDesugarer.DesugarPwd([]);
        Assert.Equal("io.println(env.cwd());", source);
    }

    [Fact]
    public void Pwd_WithArg_ThrowsCommandError()
    {
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.DesugarPwd(["/tmp"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Equal("pwd: too many arguments", ex.Message);
    }

    // ── exit ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Exit_NoArgs_DesugarsToExitZero()
    {
        string source = ShellSugarDesugarer.DesugarExit("exit", []);
        Assert.Equal("env.exit(0);", source);
    }

    [Fact]
    public void Exit_NumericArg_DesugarsToExitWithCode()
    {
        string source = ShellSugarDesugarer.DesugarExit("exit", ["7"]);
        Assert.Equal("env.exit(7);", source);
    }

    [Fact]
    public void Exit_NonNumericArg_ThrowsCommandError()
    {
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.DesugarExit("exit", ["abc"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Equal("exit: numeric argument required", ex.Message);
    }

    [Fact]
    public void Exit_TooManyArgs_ThrowsCommandError()
    {
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.DesugarExit("exit", ["0", "1"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Equal("exit: too many arguments", ex.Message);
    }

    // ── quit (alias of exit) ──────────────────────────────────────────────────

    [Fact]
    public void Quit_AliasOfExit_SameDesugaring()
    {
        string exitSource = ShellSugarDesugarer.DesugarExit("exit", ["3"]);
        string quitSource = ShellSugarDesugarer.DesugarExit("quit", ["3"]);
        // Both produce the same Stash snippet.
        Assert.Equal("env.exit(3);", exitSource);
        Assert.Equal("env.exit(3);", quitSource);
    }

    [Fact]
    public void Quit_TooManyArgs_ThrowsCommandErrorWithQuitName()
    {
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.DesugarExit("quit", ["0", "1"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Equal("quit: too many arguments", ex.Message);
    }

    [Fact]
    public void Quit_NonNumericArg_ThrowsCommandErrorWithQuitName()
    {
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.DesugarExit("quit", ["abc"]));
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
        string source = ShellSugarDesugarer.DesugarHistory([]);
        Assert.Equal("for entry in process.historyList() { io.println(entry); }", source);
    }

    [Fact]
    public void History_DashC_ReturnsClear()
    {
        string source = ShellSugarDesugarer.DesugarHistory(["-c"]);
        Assert.Equal("process.historyClear();", source);
    }

    [Fact]
    public void History_PositiveInt_ReturnsSliceLoop()
    {
        string source = ShellSugarDesugarer.DesugarHistory(["5"]);
        Assert.NotNull(source);
        Assert.Contains("process.historyList()", source, StringComparison.Ordinal);
        Assert.Contains("5", source, StringComparison.Ordinal);
    }

    [Fact]
    public void History_Zero_ReturnsNullStatement()
    {
        string source = ShellSugarDesugarer.DesugarHistory(["0"]);
        Assert.Equal("null;", source);
    }

    [Fact]
    public void History_NegativeInt_ThrowsCommandError()
    {
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.DesugarHistory(["-1"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
    }

    [Fact]
    public void History_NonNumericArg_ThrowsCommandError()
    {
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.DesugarHistory(["foo"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
    }

    [Fact]
    public void History_TooManyArgs_ThrowsCommandError()
    {
        var ex = Assert.Throws<RuntimeError>(
            () => ShellSugarDesugarer.DesugarHistory(["5", "extra"]));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
    }

    [Fact]
    public void IsSugarName_History_False_PhaseD()
    {
        // Phase D: history moved to built-in aliases.
        Assert.False(ShellSugarDesugarer.IsSugarName("history"));
    }
}
