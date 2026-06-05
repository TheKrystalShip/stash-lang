namespace Stash.Tests.Interpreting.Async.CancellationAndTimeout;

using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Tests.Interpreting;

/// <summary>
/// D3 — Genuine cancellation contract:
///   task.cancel(f) fires the child's linked CTS; the child VM cooperatively exits at the
///   next park point (e.g. time.sleep); the .NET Task ends Canceled (not Faulted); so
///   task.status(f) returns "Cancelled" and awaiting f throws CancellationError.
/// </summary>
public class CancellationTests : StashTestBase
{
    // ── Status becomes Cancelled ──────────────────────────────────────────────

    /// <summary>
    /// After task.cancel, poll task.status across a few sleep ticks until the child
    /// cooperatively exits at its time.sleep park point.  Status must eventually be
    /// "Cancelled", never "Failed".
    ///
    /// Cooperative detail: we do NOT read status immediately after cancel — we give
    /// the child bounded time to observe the CTS and exit.
    /// </summary>
    [Fact]
    public void CancellationAndTimeout_Cancel_StatusBecomeCancelled()
    {
        var result = Run(@"
let f = task.run(() => { time.sleep(10); });
task.cancel(f);
// Poll until the child exits cooperatively at its time.sleep park point (bounded).
let i = 0;
while (task.status(f) == task.Status.Running && i < 40) {
    time.sleep(0.05);
    i = i + 1;
}
let result = task.status(f);
");
        var status = Assert.IsType<StashEnumValue>(result);
        Assert.Equal("Cancelled", status.MemberName);
    }

    [Fact]
    public void CancellationAndTimeout_Cancel_StatusIsNotFailed()
    {
        var result = Run(@"
let f = task.run(() => { time.sleep(10); });
task.cancel(f);
let i = 0;
while (task.status(f) == task.Status.Running && i < 40) {
    time.sleep(0.05);
    i = i + 1;
}
let result = task.status(f);
");
        var status = Assert.IsType<StashEnumValue>(result);
        Assert.NotEqual("Failed", status.MemberName);
    }

    // ── Await after cancel throws CancellationError ───────────────────────────

    /// <summary>
    /// After task.cancel, once the child has exited (status == Cancelled), awaiting
    /// the future must throw CancellationError (not a bare RuntimeError).
    ///
    /// Bounded: we first poll until Cancelled so the await is against an already-
    /// finished task and cannot hang.
    /// </summary>
    [Fact]
    public void CancellationAndTimeout_AwaitAfterCancel_ThrowsCancellationError()
    {
        var error = RunCapturingError(@"
let f = task.run(() => { time.sleep(10); });
task.cancel(f);
let i = 0;
while (task.status(f) == task.Status.Running && i < 40) {
    time.sleep(0.05);
    i = i + 1;
}
await f;
");
        Assert.IsType<CancellationError>(error);
    }

    [Fact]
    public void CancellationAndTimeout_AwaitAfterCancel_ErrorTypeIsCancellationError()
    {
        // In-script try/catch: verify the .type field the Stash user sees.
        var result = Run(@"
let f = task.run(() => { time.sleep(10); });
task.cancel(f);
let i = 0;
while (task.status(f) == task.Status.Running && i < 40) {
    time.sleep(0.05);
    i = i + 1;
}
let result = """";
try {
    await f;
} catch (e) {
    result = e.type;
}
");
        Assert.Equal("CancellationError", result);
    }

    // ── task.await path throws CancellationError ──────────────────────────────

    [Fact]
    public void CancellationAndTimeout_TaskAwaitAfterCancel_ThrowsCancellationError()
    {
        var error = RunCapturingError(@"
let f = task.run(() => { time.sleep(10); });
task.cancel(f);
let i = 0;
while (task.status(f) == task.Status.Running && i < 40) {
    time.sleep(0.05);
    i = i + 1;
}
task.await(f);
");
        Assert.IsType<CancellationError>(error);
    }

    // ── Cancel already-completed task is harmless ─────────────────────────────

    [Fact]
    public void CancellationAndTimeout_CancelAfterCompletion_DoesNotThrow()
    {
        RunStatements(@"
let f = task.run(() => 42);
task.await(f);
task.cancel(f);
");
    }

    [Fact]
    public void CancellationAndTimeout_CancelCalledTwice_DoesNotThrow()
    {
        RunStatements(@"
let f = task.run(() => { time.sleep(10); });
task.cancel(f);
task.cancel(f);
");
    }

    // ── Cancel-then-awaitAll does not throw (collect-all semantics) ──────────

    [Fact]
    public void CancellationAndTimeout_CancelThenAwaitAll_DoesNotThrow()
    {
        // awaitAll is a collect-all combinator — it must not throw on a cancelled future.
        // The element type correction ("CancellationError" vs "TaskCancelled") is D2 (P3).
        RunStatements(@"
let f = task.run(() => { time.sleep(10); });
task.cancel(f);
let i = 0;
while (task.status(f) == task.Status.Running && i < 40) {
    time.sleep(0.05);
    i = i + 1;
}
let results = task.awaitAll([f]);
");
    }

    [Fact]
    public void CancellationAndTimeout_CancelThenAwaitAll_ResultElementIsStashError()
    {
        // The element for a cancelled future must be a StashError (not null, not the future).
        var result = Run(@"
let f = task.run(() => { time.sleep(10); });
task.cancel(f);
let i = 0;
while (task.status(f) == task.Status.Running && i < 40) {
    time.sleep(0.05);
    i = i + 1;
}
let results = task.awaitAll([f]);
let elem = results[0];
let result = typeof(elem) == ""Error"";
");
        Assert.Equal(true, result);
    }
}
