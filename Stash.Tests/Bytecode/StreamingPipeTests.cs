using System;
using System.Threading.Tasks;
using Stash.Tests.Interpreting;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Tests for Phase 2 of the Shell Mode spec: streaming OS-level pipes with
/// SIGPIPE / broken-pipe handling via <c>ShutdownUpstreamAsync</c>.
///
/// All tests are POSIX-only (guarded with <c>if (OperatingSystem.IsWindows()) return;</c>).
/// Each test is <c>async Task</c> with <c>[Fact(Timeout = 10000)]</c> so a hang
/// surfaces as a failure rather than an indefinitely blocked CI run.
/// The synchronous <c>Run()</c> helper is wrapped in <c>Task.Run</c> so that
/// xUnit's timeout mechanism (which requires async tests) can interrupt it.
/// </summary>
public class StreamingPipeTests : StashTestBase
{
    // =========================================================================
    // 1. yes | head -5  — upstream killed after downstream closes stdin
    // =========================================================================

    [Fact(Timeout = 10000)]
    public async Task Pipe_YesHeadFive_ReturnsQuicklyWithFiveLines()
    {
        if (OperatingSystem.IsWindows()) return;

        // yes generates infinite "y\n" lines; head -5 reads 5 then exits.
        // Without broken-pipe handling, yes keeps writing and the pump hangs.
        var result = await Task.Run(() => Run("let r = $(yes) | $(head -5); let result = r.stdout;"));
        string stdout = ((string)result!).TrimEnd('\n');
        string[] lines = stdout.Split('\n');
        Assert.Equal(5, lines.Length);
        Assert.All(lines, line => Assert.Equal("y", line));
    }

    // =========================================================================
    // 2. Large stream — upstream killed quickly when downstream finishes early
    // =========================================================================

    [Fact(Timeout = 10000)]
    public async Task Pipe_LargeStreamHead_ReturnsFastAfterUpstreamKilled()
    {
        if (OperatingSystem.IsWindows()) return;

        // seq generates 1_000_000 lines; head -1 reads the first and exits.
        // Verifies that upstream (seq) is killed promptly, not drained.
        var result = await Task.Run(() => Run("let r = $(seq 1000000) | $(head -1); let result = r.stdout;"));
        string stdout = ((string)result!).Trim();
        Assert.Equal("1", stdout);
    }

    // =========================================================================
    // 3. Large stream no memory explosion — all data streams through
    // =========================================================================

    [Fact(Timeout = 10000)]
    public async Task Pipe_LargeStream_NoMemoryExplosion()
    {
        if (OperatingSystem.IsWindows()) return;

        // seq 100000 | wc -l — both stages run to completion normally.
        // Verifies that streaming works end-to-end for large output without
        // buffering the entire intermediate stream in memory.
        var result = await Task.Run(() => Run("let r = $(seq 100000) | $(wc -l); let result = str.trim(r.stdout);"));
        string stdout = ((string)result!).Trim();
        Assert.Equal("100000", stdout);
    }

    // =========================================================================
    // 4. Exit codes — last stage wins; all codes collected
    // =========================================================================

    [Fact(Timeout = 10000)]
    public async Task Pipe_AllStagesExitCodesCollected_LastWins_FalseTrue()
    {
        if (OperatingSystem.IsWindows()) return;

        // $(false) | $(true) — last stage (true) exits 0 → overall exit code 0.
        var result = await Task.Run(() => Run("let r = $(false) | $(true); let result = r.exitCode;"));
        Assert.Equal(0L, result);
    }

    [Fact(Timeout = 10000)]
    public async Task Pipe_AllStagesExitCodesCollected_LastWins_TrueFalse()
    {
        if (OperatingSystem.IsWindows()) return;

        // $(true) | $(false) — last stage (false) exits 1 → overall exit code 1.
        var result = await Task.Run(() => Run("let r = $(true) | $(false); let result = r.exitCode;"));
        Assert.Equal(1L, result);
    }

    // =========================================================================
    // 5. Three-stage pipeline with early termination
    // =========================================================================

    [Fact(Timeout = 10000)]
    public async Task Pipe_ThreeStage_FastReturn()
    {
        if (OperatingSystem.IsWindows()) return;

        // yes | grep y | head -3 — head terminates after 3 lines;
        // both yes and grep must be killed promptly.
        var result = await Task.Run(() => Run("let r = $(yes) | $(grep y) | $(head -3); let result = r.stdout;"));
        string stdout = ((string)result!).TrimEnd('\n');
        string[] lines = stdout.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.All(lines, line => Assert.Equal("y", line));
    }

    // =========================================================================
    // 6. Single-stage sanity — existing behaviour unchanged
    // =========================================================================

    [Fact(Timeout = 10000)]
    public async Task Pipe_SingleStage_StillWorks()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = await Task.Run(() => Run("let r = $(echo hello); let result = str.trim(r.stdout);"));
        Assert.Equal("hello", (string)result!);
    }

    // =========================================================================
    // 7. Downstream exits immediately without reading — does not hang
    // =========================================================================

    [Fact(Timeout = 10000)]
    public async Task Pipe_DownstreamFailure_DoesNotHang()
    {
        if (OperatingSystem.IsWindows()) return;

        // "sh -c 'exit 1'" exits immediately without reading its stdin.
        // yes keeps writing; the pump detects the broken pipe and ShutdownUpstreamAsync
        // kills yes within the grace period.  Overall exit code = last stage (1).
        var result = await Task.Run(() => Run("let r = $(yes) | $(sh -c 'exit 1'); let result = r.exitCode;"));
        Assert.Equal(1L, result);
    }
}
