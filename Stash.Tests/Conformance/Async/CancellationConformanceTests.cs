using System.Threading;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Async;

/// <summary>
/// Conformance tests for §Async — Async Functions and Await, clauses D3, Edit 4, and Edit 5.
///
/// <para>
/// These tests prove that the Stash implementation honors the normative clauses of
/// <c>docs/Stash — Language Specification.md</c> §Async (L1428–1787), specifically:
/// </para>
/// <list type="bullet">
///   <item><b>D3</b> (cooperative cancellation) — <c>task.cancel(future)</c> transitions the
///     future to <c>task.Status.Cancelled</c> at the next park point; awaiting a cancelled
///     future throws <c>CancellationError</c>; a status read taken immediately after
///     <c>task.cancel</c> may still observe <c>task.Status.Running</c> (race) — both branches
///     are sealed law.</item>
///   <item><b>Edit 4</b> (idempotency + return + TypeError) — <c>task.cancel(future)</c>
///     returns <c>null</c>; calling it on an already-settled Future (Completed, Failed, or
///     Cancelled) is a no-op that returns <c>null</c> without raising; calling it twice on the
///     same future is also a no-op; calling it on a non-Future value throws
///     <c>TypeError</c>.</item>
///   <item><b>Edit 5</b> (closed enum + qualified access) — <c>task.Status.Running</c>,
///     <c>task.Status.Completed</c>, <c>task.Status.Failed</c>, and
///     <c>task.Status.Cancelled</c> all resolve to valid enum values; a bare <c>Status</c>
///     identifier fails resolution with an undefined-name runtime error.</item>
///   <item><b>Timeout distinction</b> — <c>task.timeout(ms, fn)</c> throws
///     <c>TimeoutError</c> on deadline, never <c>CancellationError</c>, even though it
///     cancels the underlying work.</item>
///   <item><b><c>event.loop()</c> cancellation</b> — invoking <c>event.loop()</c> with a
///     fired cancellation token throws <c>CancellationError</c>.</item>
/// </list>
///
/// <para>
/// These are conformance tests — they prove the <em>spec</em>, not guard implementation
/// regressions. The existing behavior suite at <c>Stash.Tests/Interpreting/Async/</c>
/// remains in place for regression coverage; the two suites are complementary.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class CancellationConformanceTests : StashTestBase
{
    // ─────────────────────────────────────────────────────────────────────────────
    // D3 — cooperative cancellation + CancellationError on await
    // Spec: "Cancellation, timeout, and task status." paragraph (L1551–L1574)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// D3: <c>task.cancel(future)</c> eventually transitions the future to
    /// <c>task.Status.Cancelled</c> (at the next park point). Once the status is
    /// Cancelled, awaiting the future throws <c>CancellationError</c>.
    /// </summary>
    [Fact]
    public void D3_Cancel_EventuallyTransitionsToCancelled_PerSpecAsyncD3()
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
        Assert.Equal("Cancelled", status.MemberName);
    }

    /// <summary>
    /// D3: Awaiting a cancelled future throws <c>CancellationError</c>.
    /// Status is polled to Cancelled first so the await is non-racy.
    /// </summary>
    [Fact]
    public void D3_AwaitCancelled_ThrowsCancellationError_PerSpecAsyncD3()
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
        Assert.Equal("CancellationError", error.ErrorType);
    }

    /// <summary>
    /// D3 (race seal): A status read taken <em>immediately</em> after <c>task.cancel</c>
    /// may still observe <c>task.Status.Running</c> (cooperative cancel race). The spec
    /// seals BOTH branches as law: the status must be either Running or Cancelled —
    /// never Failed or Completed.
    /// </summary>
    [Fact]
    public void D3_ImmediateStatusAfterCancel_IsRunningOrCancelled_PerSpecAsyncD3()
    {
        var result = Run(@"
let f = task.run(() => { time.sleep(10); });
task.cancel(f);
// Read immediately (before any poll loop) — may race
let result = task.status(f);
");
        var status = Assert.IsType<StashEnumValue>(result);
        Assert.True(
            status.MemberName == "Running" || status.MemberName == "Cancelled",
            $"Expected Running or Cancelled immediately after cancel; got '{status.MemberName}'");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Edit 4 — task.cancel returns null + idempotency + TypeError on non-Future
    // Spec: "task.cancel(future) returns null. It is idempotent…" (Edit 4)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Edit 4: <c>task.cancel(future)</c> returns <c>null</c> — proved by capturing the
    /// return value from a running future's cancel call and asserting null.
    /// </summary>
    [Fact]
    public void Edit4_Cancel_ReturnsNull_PerSpecAsyncEdit4()
    {
        var result = Run(@"
let f = task.run(() => { time.sleep(10); });
let result = task.cancel(f);
");
        Assert.Null(result);
    }

    /// <summary>
    /// Edit 4: Cancelling an already-<c>Completed</c> Future is a no-op — returns
    /// <c>null</c> without raising, and the status remains <c>task.Status.Completed</c>.
    /// </summary>
    [Fact]
    public void Edit4_Cancel_OnCompleted_IsNoOp_ReturnsNull_PerSpecAsyncEdit4()
    {
        var result = Run(@"
let f = task.resolve(42);
// f is already Completed (task.resolve returns a pre-resolved future)
let r = task.cancel(f);
let statusAfter = task.status(f);
// Return both to assert: cancel returned null AND status is still Completed
let result = [r, statusAfter];
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Null(list[0]); // cancel returns null
        var status = Assert.IsType<StashEnumValue>(list[1]);
        Assert.Equal("Completed", status.MemberName); // status unchanged
    }

    /// <summary>
    /// Edit 4: Calling <c>task.cancel</c> a second time on the same cancelled Future
    /// is also a no-op — returns <c>null</c> without raising.
    /// </summary>
    [Fact]
    public void Edit4_Cancel_SecondCall_IsNoOp_ReturnsNull_PerSpecAsyncEdit4()
    {
        var result = Run(@"
let f = task.run(() => { time.sleep(10); });
task.cancel(f);
// Wait for cancellation to propagate
let i = 0;
while (task.status(f) == task.Status.Running && i < 40) {
    time.sleep(0.05);
    i = i + 1;
}
// Second cancel on an already-cancelled future — must be no-op
let result = task.cancel(f);
");
        Assert.Null(result);
    }

    /// <summary>
    /// Edit 4: <c>task.cancel(42)</c> — non-Future argument — throws <c>TypeError</c>.
    /// Proved by asserting the error type name is "TypeError".
    /// </summary>
    [Fact]
    public void Edit4_Cancel_NonFuture_ThrowsTypeError_PerSpecAsyncEdit4()
    {
        var error = RunCapturingError(@"task.cancel(42);");
        Assert.Equal("TypeError", error.ErrorType);
    }

    /// <summary>
    /// Edit 4: <c>task.cancel(""hello"")</c> — string argument — also throws <c>TypeError</c>.
    /// </summary>
    [Fact]
    public void Edit4_Cancel_StringArg_ThrowsTypeError_PerSpecAsyncEdit4()
    {
        var error = RunCapturingError(@"task.cancel(""hello"");");
        Assert.Equal("TypeError", error.ErrorType);
    }

    /// <summary>
    /// All task.* builtins consuming a Future argument throw TypeError on a non-Future value.
    /// Spec: "All <c>task.*</c> builtins that consume a <c>Future</c> argument
    /// (<c>task.await</c>, <c>task.status</c>, <c>task.cancel</c>) throw <c>TypeError</c>
    /// when given a non-Future value." (Edit 4 paragraph)
    /// </summary>
    [Fact]
    public void TaskAwait_NonFuture_ThrowsTypeError_PerSpecAsyncValidation()
    {
        var error = RunCapturingError("task.await(42);");
        Assert.Equal("TypeError", error.ErrorType);
    }

    /// <summary>
    /// All task.* builtins consuming a Future argument throw TypeError on a non-Future value.
    /// Spec: "All <c>task.*</c> builtins that consume a <c>Future</c> argument
    /// (<c>task.await</c>, <c>task.status</c>, <c>task.cancel</c>) throw <c>TypeError</c>
    /// when given a non-Future value." (Edit 4 paragraph)
    /// </summary>
    [Fact]
    public void TaskStatus_NonFuture_ThrowsTypeError_PerSpecAsyncValidation()
    {
        var error = RunCapturingError("task.status(42);");
        Assert.Equal("TypeError", error.ErrorType);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Edit 5 — task.Status is a closed enum; qualified access; no top-level Status
    // Spec: "task.status(future) reports a future's lifecycle state. It returns a
    //        value of the closed enum task.Status…" (Edit 5)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Edit 5: <c>task.Status.Running</c> resolves to a valid enum value.
    /// </summary>
    [Fact]
    public void Edit5_TaskStatusRunning_ResolvesAsEnumValue_PerSpecAsyncEdit5()
    {
        var result = Run(@"
let f = task.run(() => { time.sleep(10); });
let result = task.status(f) == task.Status.Running;
task.cancel(f);
");
        // The status at the moment of read is Running (task hasn't been cancelled yet)
        // OR it may have flipped, but the comparison itself must not throw.
        // Since we can't guarantee timing, assert the expression evaluates to a bool (not throws).
        Assert.IsType<bool>(result);
    }

    /// <summary>
    /// Edit 5: <c>task.Status.Completed</c> resolves to a valid enum value.
    /// Proved by using it in a comparison with a pre-resolved future's status.
    /// </summary>
    [Fact]
    public void Edit5_TaskStatusCompleted_ResolvesAsEnumValue_PerSpecAsyncEdit5()
    {
        var result = Run(@"
let f = task.resolve(42);
let result = task.status(f) == task.Status.Completed;
");
        Assert.Equal(true, result);
    }

    /// <summary>
    /// Edit 5: <c>task.Status.Failed</c> resolves to a valid enum value.
    /// Proved by using it in a comparison against the status of a faulted future
    /// (observed via awaitAll so no D1 report fires).
    /// </summary>
    [Fact]
    public void Edit5_TaskStatusFailed_ResolvesAsEnumValue_PerSpecAsyncEdit5()
    {
        var result = Run(@"
let f = task.run(() => { throw TypeError { message: ""err"" }; });
// Wait for fault and observe so D1 doesn't fire
let i = 0;
while (task.status(f) == task.Status.Running && i < 40) {
    time.sleep(0.05);
    i = i + 1;
}
task.awaitAll([f]);
let result = task.status(f) == task.Status.Failed;
");
        Assert.Equal(true, result);
    }

    /// <summary>
    /// Edit 5: <c>task.Status.Cancelled</c> resolves to a valid enum value.
    /// Proved by using it in a comparison after cancel propagates.
    /// </summary>
    [Fact]
    public void Edit5_TaskStatusCancelled_ResolvesAsEnumValue_PerSpecAsyncEdit5()
    {
        var result = Run(@"
let f = task.run(() => { time.sleep(10); });
task.cancel(f);
let i = 0;
while (task.status(f) == task.Status.Running && i < 40) {
    time.sleep(0.05);
    i = i + 1;
}
let result = task.status(f) == task.Status.Cancelled;
");
        Assert.Equal(true, result);
    }

    /// <summary>
    /// Edit 5: A bare <c>Status</c> identifier (without <c>task.</c> qualifier) fails
    /// resolution with an undefined-name runtime error. There is no top-level <c>Status</c>
    /// binding in the §Async surface.
    /// </summary>
    [Fact]
    public void Edit5_BareStatusIdentifier_FailsResolution_PerSpecAsyncEdit5()
    {
        // Status (without task.) must not resolve — must throw RuntimeError.
        RunExpectingError(@"let x = Status.Running;");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Timeout distinction — task.timeout throws TimeoutError, not CancellationError
    // Spec: "task.timeout(ms, fn) runs fn under a deadline and is distinct from
    //        external cancellation: when the deadline elapses it throws TimeoutError
    //        (never CancellationError)…" (L1571–L1574)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Timeout distinction: <c>task.timeout(ms, fn)</c> throws <c>TimeoutError</c> on
    /// deadline expiry — never <c>CancellationError</c>, even though it cancels the
    /// underlying work.
    /// </summary>
    [Fact]
    public void Timeout_OnDeadline_ThrowsTimeoutError_NotCancellationError_PerSpecAsyncTimeout()
    {
        var error = RunCapturingError(@"
task.timeout(50, () => { time.sleep(10); });
");
        Assert.Equal("TimeoutError", error.ErrorType);
    }

    /// <summary>
    /// Timeout distinction: the error thrown by <c>task.timeout</c> is specifically
    /// <c>TimeoutError</c>, never <c>CancellationError</c>. Script-visible via
    /// try/catch and the <c>.type</c> field.
    /// </summary>
    [Fact]
    public void Timeout_CaughtInScript_TypeIsTimeoutError_PerSpecAsyncTimeout()
    {
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

    // ─────────────────────────────────────────────────────────────────────────────
    // event.loop() cancellation — throws CancellationError when token fires
    // Spec: event.loop() "blocks and drains indefinitely until the script's cancellation
    //        token fires, then throws CancellationError." (L1683)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>event.loop()</c> cancellation: invoking from a script with a fired cancellation
    /// token throws <c>CancellationError</c>. Uses an explicit CancellationTokenSource
    /// and a background thread (the <c>StashTestBase.Run</c> harness uses no token, so
    /// <c>event.loop</c> would park forever without this setup).
    /// </summary>
    [Fact]
    public void EventLoop_FiredCancellationToken_ThrowsCancellationError_PerSpecAsyncEventLoop()
    {
        const string source = "event.loop();";

        var tokens = new Lexer(source, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);

        var cts = new CancellationTokenSource();
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals(), cts.Token);

        // Cancel from a background thread after a short delay.
        var bgThread = new Thread(() =>
        {
            Thread.Sleep(100);
            cts.Cancel();
        })
        { IsBackground = true };
        bgThread.Start();

        Assert.ThrowsAny<CancellationError>(() => vm.Execute(chunk));
        bgThread.Join(TimeSpan.FromSeconds(5));
    }
}
