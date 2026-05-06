namespace Stash.Tests.Interpreting;

/// <summary>
/// Integration tests for <c>process.exec</c> and <c>process.pipeline</c> —
/// Phase A: API surface, type-checking, and basic capture-mode execution.
/// </summary>
[Collection("SystemCwdTests")]
public class ProcessExecApiTests : TempDirectoryFixture
{
    public ProcessExecApiTests() : base("stash_process_exec_test") { }

    // ── process.replace (renamed from exec) ───────────────────────────────────
    // NOTE: process.replace calls execvp on Unix (replaces the process image) or
    // spawns+exits on Windows. It cannot be tested inline in xUnit — doing so
    // would replace/exit the test host process. Tests are omitted intentionally.
    // Existing ProcessBuiltInsTests cover the registration surface.

    // ── process.exec — capture mode (default) ─────────────────────────────────

    [Fact]
    public void Exec_BasicCapture_ReturnsCommandResult()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Run(@"
            let r = process.exec(""echo"", [""hello""]);
            let result = str.trim(r.stdout);
        ");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Exec_BasicCapture_ExitCodeZero()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Run(@"
            let r = process.exec(""echo"", [""hi""]);
            let result = r.exitCode;
        ");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Exec_MultipleArgs_ConcatenatedInOutput()
    {
        if (OperatingSystem.IsWindows()) return;
        // printf treats all args as format parts; use echo for simple multi-arg
        object? result = Run(@"
            let r = process.exec(""printf"", [""%s %s"", ""hello"", ""world""]);
            let result = r.stdout;
        ");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Exec_StrictMode_FailingCommand_Throws()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Throws<Stash.Runtime.RuntimeError>(() =>
            RunStatements(@"process.exec(""false"", [], ExecOptions { strict: true });"));
    }

    [Fact]
    public void Exec_StrictMode_SuccessfulCommand_DoesNotThrow()
    {
        if (OperatingSystem.IsWindows()) return;
        RunStatements(@"process.exec(""true"", [], ExecOptions { strict: true });");
    }

    [Fact]
    public void Exec_FailingCommand_NoStrictMode_ExitCodeNonZero()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Run(@"
            let r = process.exec(""false"", []);
            let result = r.exitCode;
        ");
        Assert.Equal(1L, result);
    }

    [Fact]
    public void Exec_NullProgramArg_ThrowsTypeError()
    {
        Assert.Throws<Stash.Runtime.RuntimeError>(() =>
            RunStatements("process.exec(null, []);"));
    }

    [Fact]
    public void Exec_EmptyProgramArg_ThrowsValueError()
    {
        Assert.Throws<Stash.Runtime.RuntimeError>(() =>
            RunStatements(@"process.exec("""", []);"));
    }

    [Fact]
    public void Exec_NonExistentProgram_ThrowsCommandError()
    {
        var err = RunCapturingError(@"process.exec(""__nonexistent_program_xyz"", []);");
        Assert.Equal(Stash.Runtime.StashErrorTypes.CommandError, err.ErrorType);
    }

    [Fact]
    public void Exec_ArgsNotArray_ThrowsTypeError()
    {
        var err = RunCapturingError(@"process.exec(""echo"", ""notarray"");");
        Assert.Equal(Stash.Runtime.StashErrorTypes.TypeError, err.ErrorType);
    }

    // ── process.exec — stream mode ────────────────────────────────────────────

    [Fact]
    public void Exec_StreamMode_ReturnsStreamingProcess()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Run(@"
            let s = process.exec(""printf"", [""%s\n%s\n"", ""a"", ""b""], ExecOptions { mode: ExecMode.Stream });
            let lines = [];
            for (let line in s) {
                arr.push(lines, line);
            }
            let result = lines;
        ");
        Assert.Equal(new System.Collections.Generic.List<object?> { "a", "b" }, result);
    }

    [Fact]
    public void Exec_StreamMode_ExitCodeAfterIteration()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Run(@"
            let s = process.exec(""true"", [], ExecOptions { mode: ExecMode.Stream });
            for (let _ in s) {}
            let result = s.exitCode;
        ");
        Assert.Equal(0L, result);
    }

    // ── process.exec — passthrough mode ──────────────────────────────────────

    [Fact]
    public void Exec_PassthroughMode_ReturnsCommandResult()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Run(@"
            let r = process.exec(""true"", [], ExecOptions { mode: ExecMode.Passthrough });
            let result = r.exitCode;
        ");
        Assert.Equal(0L, result);
    }

    // ── process.exec — opts type-checking ────────────────────────────────────

    [Fact]
    public void Exec_NonStructOpts_ThrowsTypeError()
    {
        var err = RunCapturingError(@"process.exec(""echo"", [""hi""], 42);");
        Assert.Equal(Stash.Runtime.StashErrorTypes.TypeError, err.ErrorType);
    }

    // ── process.exec — redirect ────────────────────────────────────────────────

    [Fact]
    public void Exec_RedirectStdoutToFile_WritesFile()
    {
        if (OperatingSystem.IsWindows()) return;
        string outFile = System.IO.Path.Combine(TestDir, "out.txt");
        Run($@"
            let opts = ExecOptions {{ redirect: RedirectSpec {{ stream: ""stdout"", target: ""{outFile.Replace("\\", "/")}"", append: false }} }};
            process.exec(""printf"", [""%s"", ""redirected""], opts);
            let result = fs.readFile(""{outFile.Replace("\\", "/")}"");
        ");
        Assert.True(System.IO.File.Exists(outFile));
        Assert.Equal("redirected", System.IO.File.ReadAllText(outFile));
    }

    [Fact]
    public void Exec_CwdOption_DoesNotMutateParentCwd()
    {
        if (OperatingSystem.IsWindows()) return;
        string parentCwdBefore = System.Environment.CurrentDirectory;
        string tempDir = System.IO.Path.GetTempPath().TrimEnd('/');
        object? result = Run($@"
            let r = process.exec(""pwd"", [], ExecOptions {{ cwd: ""{tempDir}"" }});
            let result = str.trim(r.stdout);
        ");
        // Resolve any symlinks (e.g. /tmp on macOS) before comparing.
        string actualChildCwd = System.IO.Path.GetFullPath((string)result!);
        string expectedChildCwd = System.IO.Path.GetFullPath(tempDir);
        Assert.Equal(expectedChildCwd, actualChildCwd);
        Assert.Equal(parentCwdBefore, System.Environment.CurrentDirectory);
    }

    [Fact]
    public void Exec_EnvOption_PassesEnvironmentToChild()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Run(@"
            let envVars = { STASH_TEST_VAR: ""hello-from-stash"" };
            let r = process.exec(""sh"", [""-c"", ""echo $STASH_TEST_VAR""],
                ExecOptions { env: envVars });
            let result = str.trim(r.stdout);
        ");
        Assert.Equal("hello-from-stash", result);
    }

    [Fact]
    public void Pipeline_CwdOption_AppliesToEveryStage()
    {
        if (OperatingSystem.IsWindows()) return;
        string parentCwdBefore = System.Environment.CurrentDirectory;
        string tempDir = System.IO.Path.GetTempPath().TrimEnd('/');
        object? result = Run($@"
            let r = process.pipeline(
                [PipelineStage {{ program: ""pwd"", args: [] }},
                 PipelineStage {{ program: ""cat"", args: [] }}],
                ExecOptions {{ cwd: ""{tempDir}"" }}
            );
            let result = str.trim(r.stdout);
        ");
        string actualChildCwd = System.IO.Path.GetFullPath((string)result!);
        string expectedChildCwd = System.IO.Path.GetFullPath(tempDir);
        Assert.Equal(expectedChildCwd, actualChildCwd);
        Assert.Equal(parentCwdBefore, System.Environment.CurrentDirectory);
    }

    // ── process.pipeline ─────────────────────────────────────────────────────

    [Fact]
    public void Pipeline_TwoStages_ChainsOutput()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Run(@"
            let r = process.pipeline([
                PipelineStage { program: ""printf"", args: [""%s\n%s\n"", ""hello"", ""world""] },
                PipelineStage { program: ""grep"", args: [""world""] }
            ]);
            let result = str.trim(r.stdout);
        ");
        Assert.Equal("world", result);
    }

    [Fact]
    public void Pipeline_EmptyStages_ThrowsValueError()
    {
        var err = RunCapturingError("process.pipeline([]);");
        Assert.Equal(Stash.Runtime.StashErrorTypes.ValueError, err.ErrorType);
    }

    [Fact]
    public void Pipeline_StreamMode_IteratesLastStageOutput()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Run(@"
            let s = process.pipeline([
                PipelineStage { program: ""printf"", args: [""%s\n%s\n"", ""a"", ""b""] },
                PipelineStage { program: ""cat"", args: [] }
            ], ExecOptions { mode: ExecMode.Stream });
            let lines = [];
            for (let line in s) {
                arr.push(lines, line);
            }
            let result = lines;
        ");
        Assert.Equal(new System.Collections.Generic.List<object?> { "a", "b" }, result);
    }

    [Fact]
    public void Pipeline_PassthroughMode_ThrowsValueError()
    {
        if (OperatingSystem.IsWindows()) return;
        var err = RunCapturingError(@"
            process.pipeline([
                PipelineStage { program: ""echo"", args: [""hi""] }
            ], ExecOptions { mode: ExecMode.Passthrough });
        ");
        Assert.Equal(Stash.Runtime.StashErrorTypes.ValueError, err.ErrorType);
    }
}
