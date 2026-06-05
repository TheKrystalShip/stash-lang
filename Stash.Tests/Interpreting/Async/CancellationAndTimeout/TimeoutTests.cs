namespace Stash.Tests.Interpreting.Async.CancellationAndTimeout;

using Stash.Runtime.Errors;
using Stash.Tests.Interpreting;

/// <summary>
/// D3 — Timeout vs cancellation distinction:
///   task.timeout(ms, fn) must still throw TimeoutError, not CancellationError,
///   after the genuine-cancellation changes land.  The distinction is:
///     - task.cancel(f)  → external cancel → CancellationError + status Cancelled
///     - task.timeout(ms, fn) → internal CTS timer → TimeoutError
/// </summary>
public class TimeoutTests : StashTestBase
{
    // ── task.timeout still throws TimeoutError ────────────────────────────────

    [Fact]
    public void CancellationAndTimeout_Timeout_ThrowsTimeoutError()
    {
        var error = RunCapturingError(@"
task.timeout(50, () => { time.sleep(10); });
");
        Assert.IsType<TimeoutError>(error);
    }

    [Fact]
    public void CancellationAndTimeout_Timeout_ErrorTypeIsTimeoutError()
    {
        // In-script: verify .type field the user sees.
        var result = Run(@"
let result = """";
try {
    task.timeout(50, () => { time.sleep(10); });
} catch (e) {
    result = e.type;
}
");
        Assert.Equal("TimeoutError", result);
    }

    [Fact]
    public void CancellationAndTimeout_Timeout_NotCancellationError()
    {
        var error = RunCapturingError(@"
task.timeout(50, () => { time.sleep(10); });
");
        Assert.IsNotType<CancellationError>(error);
    }

    // ── task.timeout with async lambda still throws TimeoutError ─────────────

    [Fact]
    public void CancellationAndTimeout_TimeoutAsync_ThrowsTimeoutError()
    {
        var error = RunCapturingError(@"
task.timeout(50, async () => { time.sleep(10); });
");
        Assert.IsType<TimeoutError>(error);
    }

    // ── task.timeout succeeds when work completes within the deadline ─────────

    [Fact]
    public void CancellationAndTimeout_Timeout_CompletesInTime_ReturnsValue()
    {
        var result = Run(@"let result = task.timeout(5000, () => 42);");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void CancellationAndTimeout_Timeout_MessageContainsMs()
    {
        var error = RunCapturingError(@"
task.timeout(50, () => { time.sleep(10); });
");
        Assert.Contains("50", error.Message);
    }
}
