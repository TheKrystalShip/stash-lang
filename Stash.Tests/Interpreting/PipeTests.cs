namespace Stash.Tests.Interpreting;

/// <summary>
/// Comprehensive tests for the streaming pipe operator (|).
/// Covers basic chaining, exit codes, strict mode, interpolation,
/// streaming termination, and cross-platform behaviour.
/// </summary>
public class PipeTests : StashTestBase
{
    // =========================================================================
    // 1. Basic pipe — stdout forwarded
    // =========================================================================

    [Fact]
    public void Pipe_BasicPipe_StdoutForwarded()
    {
        var result = Run("let r = $(echo hello) | $(cat); let result = r.stdout;");
        Assert.Contains("hello", (string)result!);
    }

    // =========================================================================
    // 2. Exit code — comes from last pipeline stage
    // =========================================================================

    [Fact]
    public void Pipe_ExitCode_ComesFromLastStage()
    {
        // $(false) fails (exit code 1), but $(cat) reads empty stdin and exits 0.
        // Pipeline exit code reflects the last stage.
        var result = Run("let r = $(false) | $(cat); let result = r.exitCode;");
        Assert.Equal(0L, result);
    }

    // =========================================================================
    // 3. Three-stage chain
    // =========================================================================

    [Fact]
    public void Pipe_ThreeStageChain_Works()
    {
        var result = Run("let r = $(echo hello) | $(cat) | $(cat); let result = r.stdout;");
        Assert.Contains("hello", (string)result!);
    }

    // =========================================================================
    // 4. Grep filter (Unix only)
    // =========================================================================

    [Fact]
    [Trait("Category", "Unix")]
    public void Pipe_GrepFilter_FiltersOutput()
    {
        if (OperatingSystem.IsWindows()) return;
        var result = Run(@"let r = $(printf ""hello\nworld"") | $(grep world); let result = r.stdout;");
        Assert.Contains("world", (string)result!);
    }

    // =========================================================================
    // 5. String interpolation inside a pipe stage
    // =========================================================================

    [Fact]
    public void Pipe_InterpolationInStage_Works()
    {
        var result = Run(@"let x = ""hello""; let r = $(echo ${x}) | $(cat); let result = r.stdout;");
        Assert.Contains("hello", (string)result!);
    }

    // =========================================================================
    // 6. Result has CommandResult fields (stdout, exitCode)
    // =========================================================================

    [Fact]
    public void Pipe_Result_HasCommandResultFields()
    {
        var result = Run("let r = $(echo test) | $(cat); let result = r.stdout;");
        Assert.NotNull(result);
        Assert.Contains("test", (string)result!);
    }

    // =========================================================================
    // 7. Final-stage stderr is captured (Unix only)
    // =========================================================================

    [Fact]
    [Trait("Category", "Unix")]
    public void Pipe_FinalStageStderr_IsCaptured()
    {
        if (OperatingSystem.IsWindows()) return;
        // cat reads piped input; stderr is empty but must be a string field
        var result = Run("let r = $(echo hello) | $(cat); let result = r.stderr;");
        Assert.IsType<string>(result);
    }

    // =========================================================================
    // 8. Strict mode — last stage fails → throws RuntimeError
    // =========================================================================

    [Fact]
    public void Pipe_StrictLastStage_NonZeroExitThrows()
    {
        RunExpectingError("$(echo hello) | $!(false); let result = null;");
    }

    // =========================================================================
    // 9. Strict mode — last stage succeeds → no throw
    // =========================================================================

    [Fact]
    public void Pipe_StrictLastStage_ZeroExitSucceeds()
    {
        var result = Run("let r = $(echo hello) | $!(cat); let result = r.stdout;");
        Assert.Contains("hello", (string)result!);
    }

    // =========================================================================
    // 10. Pipe result stdout is accessible via field
    // =========================================================================

    [Fact]
    public void Pipe_Result_StringCoercedToStdout()
    {
        var result = Run("let r = $(echo hello) | $(cat); let result = str.trim(r.stdout);");
        Assert.Equal("hello", (string)result!);
    }

    // =========================================================================
    // 11. Streaming termination — head terminates producer (Unix only)
    // =========================================================================

    [Fact]
    [Trait("Category", "Unix")]
    public void Pipe_StreamingHeadTerminatesProducer()
    {
        if (OperatingSystem.IsWindows()) return;
        // yes generates infinite "y" lines; head -5 reads 5 then closes stdin.
        // The broken pipe from yes must be handled gracefully.
        var result = Run("let r = $(yes) | $(head -5); let result = r.stdout;");
        string stdout = (string)result!;
        string[] lines = stdout.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);
        Assert.All(lines, line => Assert.Equal("y", line));
    }

    // =========================================================================
    // 12. Empty first-stage output — second stage still runs and exits cleanly
    // =========================================================================

    [Fact]
    public void Pipe_EmptyFirstStageOutput_SecondStageStillRuns()
    {
        // $(true) produces no stdout; cat reads EOF from stdin and exits 0
        var result = Run("let r = $(true) | $(cat); let result = r.exitCode;");
        Assert.Equal(0L, result);
    }
}
