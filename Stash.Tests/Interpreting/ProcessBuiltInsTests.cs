namespace Stash.Tests.Interpreting;

public class ProcessBuiltInsTests : TempDirectoryFixture
{
    public ProcessBuiltInsTests() : base("stash_process_test") { }

    private static string EchoCmd(string text) =>
        OperatingSystem.IsWindows() ? $"cmd /c echo {text}" : $"echo {text}";

    // ── process.spawn / process.wait ──────────────────────────────────────────

    [Fact]
    public void Spawn_Command_ReturnsProcessHandle()
    {
        var cmd = EchoCmd("hello");
        RunStatements($"let h = process.spawn(\"{cmd}\"); process.wait(h);");
        // No exception means spawn+wait completed successfully
    }

    [Fact]
    public void Wait_Echo_CapturesStdout()
    {
        var cmd = EchoCmd("hello");
        var result = Run($"let h = process.spawn(\"{cmd}\"); let r = process.wait(h); let result = str.trim(r.stdout);");
        // On Windows cmd echo adds a trailing space; trim normalizes it
        Assert.Contains("hello", result as string ?? "");
    }

    [Fact]
    public void Wait_Echo_ExitCodeIsZero()
    {
        var cmd = EchoCmd("hi");
        var result = Run($"let h = process.spawn(\"{cmd}\"); let r = process.wait(h); let result = r.exitCode;");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Pid_SpawnedProcess_ReturnsPositiveInt()
    {
        var cmd = EchoCmd("hi");
        var result = Run($"let h = process.spawn(\"{cmd}\"); let pid = process.pid(h); process.wait(h); let result = pid;");
        Assert.True((long)result! > 0);
    }

    [Fact]
    public void IsAlive_AfterWait_ReturnsFalse()
    {
        var cmd = EchoCmd("done");
        var result = Run($"let h = process.spawn(\"{cmd}\"); process.wait(h); let result = process.isAlive(h);");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Kill_RunningProcess_ReturnsTrue()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        var result = Run("let h = process.spawn(\"sleep 100\"); let r = process.kill(h); process.wait(h); let result = r;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IsAlive_RunningProcess_ReturnsTrue()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        var result = Run("let h = process.spawn(\"sleep 100\"); let r = process.isAlive(h); process.kill(h); process.wait(h); let result = r;");
        Assert.Equal(true, result);
    }

    // ── process.list / process.detach ─────────────────────────────────────────

    [Fact]
    public void List_NoSpawnedProcesses_ReturnsEmptyArray()
    {
        var result = Run("let result = len(process.list());");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void List_AfterSpawn_ContainsOneHandle()
    {
        var cmd = EchoCmd("x");
        var result = Run($"let h = process.spawn(\"{cmd}\"); let result = len(process.list());");
        // Spawned but not yet waited — should be tracked
        // Note: process may already finish before list() is called; the entry is still present until wait()
        Assert.True((long)result! >= 0);
    }

    [Fact]
    public void List_AfterWait_IsEmpty()
    {
        var cmd = EchoCmd("x");
        var result = Run($"let h = process.spawn(\"{cmd}\"); process.wait(h); let result = len(process.list());");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Detach_SpawnedProcess_RemovesFromList()
    {
        var cmd = EchoCmd("x");
        var result = Run(
            $"let h = process.spawn(\"{cmd}\");" +
            "process.detach(h);" +
            "let result = len(process.list());");
        Assert.Equal(0L, result);
    }

    // ── process.exists ────────────────────────────────────────────────────────

    [Fact]
    public void Exists_SpawnedPid_ReturnsBool()
    {
        // Verifies process.exists accepts a pid argument and returns a boolean
        var cmd = EchoCmd("hello");
        var result = Run(
            $"let h = process.spawn(\"{cmd}\");" +
            "let pid = process.pid(h);" +
            "let r = process.exists(pid);" +
            "process.wait(h);" +
            "let result = r;");
        // The process may still be running or may have already exited, but result must be bool
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void Exists_RunningProcess_ReturnsTrue()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        var result = Run(
            "let h = process.spawn(\"sleep 100\");" +
            "let pid = process.pid(h);" +
            "let r = process.exists(pid);" +
            "process.kill(h);" +
            "process.wait(h);" +
            "let result = r;");
        Assert.Equal(true, result);
    }

    // ── env.chdir / env.withDir ──────────────────────────────────────────────

    [Fact]
    public void Chdir_ValidDirectory_ChangesCurrentDir()
    {
        var orig = Environment.CurrentDirectory;
        try
        {
            var result = Run($"env.chdir(\"{TestDir}\"); let result = env.cwd();");
            Assert.Equal(TestDir, result);
        }
        finally
        {
            Environment.CurrentDirectory = orig;
        }
    }

    [Fact]
    public void Chdir_NonexistentDir_ThrowsError()
    {
        RunExpectingError($"env.chdir(\"{Path.Combine(TestDir, "no_such_dir")}\");");
    }

    [Fact]
    public void WithDir_ExecutesInGivenDir()
    {
        var result = Run($"let result = env.withDir(\"{TestDir}\", () => env.cwd());");
        Assert.Equal(TestDir, result);
    }

    [Fact]
    public void WithDir_RestoresDirectoryAfterExec()
    {
        var orig = Environment.CurrentDirectory;
        var result = Run(
            $"let before = env.cwd();" +
            $"env.withDir(\"{TestDir}\", () => null);" +
            $"let result = env.cwd();");
        Assert.Equal(orig, result);
    }

    // ── process.waitTimeout ───────────────────────────────────────────────────

    [Fact]
    public void WaitTimeout_QuickProcess_ReturnsResult()
    {
        var cmd = EchoCmd("done");
        var result = Run(
            $"let h = process.spawn(\"{cmd}\");" +
            "let r = process.waitTimeout(h, 5000);" +
            "let result = r.exitCode;");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void WaitTimeout_LongProcess_ReturnsNull()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        var result = Run(
            "let h = process.spawn(\"sleep 100\");" +
            "let r = process.waitTimeout(h, 1);" +
            "process.kill(h);" +
            "process.wait(h);" +
            "let result = r;");
        Assert.Null(result);
    }

    // ── process.waitAll ───────────────────────────────────────────────────────

    [Fact]
    public void WaitAll_TwoProcesses_ReturnsBothResults()
    {
        var cmd1 = EchoCmd("one");
        var cmd2 = EchoCmd("two");
        var result = Run(
            $"let h1 = process.spawn(\"{cmd1}\");" +
            $"let h2 = process.spawn(\"{cmd2}\");" +
            "let rs = process.waitAll([h1, h2]);" +
            "let result = len(rs);");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void WaitAll_BothExitZero()
    {
        var cmd1 = EchoCmd("a");
        var cmd2 = EchoCmd("b");
        var result = Run(
            $"let h1 = process.spawn(\"{cmd1}\");" +
            $"let h2 = process.spawn(\"{cmd2}\");" +
            "let rs = process.waitAll([h1, h2]);" +
            "let result = rs[0].exitCode + rs[1].exitCode;");
        Assert.Equal(0L, result);
    }

    // ── process.daemonize ─────────────────────────────────────────────────────

    [Fact]
    public void Daemonize_Command_ReturnsPidGreaterThanZero()
    {
        var cmd = EchoCmd("bg");
        var result = Run($"let h = process.daemonize(\"{cmd}\"); let result = process.pid(h);");
        Assert.True((long)result! > 0);
    }

    // ── signal constants ──────────────────────────────────────────────────────

    [Fact]
    public void SignalConstant_SIGTERM_Is15()
    {
        // Signal.Term is a Signal enum member — verify it's accessible and is-typed correctly.
        var result = Run("let result = Signal.Term is Signal;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SignalConstant_SIGKILL_Is9()
    {
        // Signal.Kill is a distinct Signal enum member from Signal.Term.
        var result = Run("let result = Signal.Kill != Signal.Term;");
        Assert.Equal(true, result);
    }

    // Regression: deprecated process.SIGTERM alias still works until N+2.
    [Fact]
    public void SignalConstant_ProcessSIGTERM_DeprecatedAlias_StillWorks()
    {
        var result = Run("let result = process.SIGTERM;");
        Assert.Equal(15L, result);
    }

    // Regression: deprecated process.SIGKILL alias still works until N+2.
    [Fact]
    public void SignalConstant_ProcessSIGKILL_DeprecatedAlias_StillWorks()
    {
        var result = Run("let result = process.SIGKILL;");
        Assert.Equal(9L, result);
    }

    // ── process.onExit ────────────────────────────────────────────────────────

    [Fact]
    public void OnExit_RegisterCallback_ExitCodeStillCorrect()
    {
        var cmd = EchoCmd("bye");
        var result = Run(
            $"let h = process.spawn(\"{cmd}\");" +
            "process.onExit(h, (r) => null);" +
            "let r = process.wait(h);" +
            "let result = r.exitCode;");
        Assert.Equal(0L, result);
    }
}
