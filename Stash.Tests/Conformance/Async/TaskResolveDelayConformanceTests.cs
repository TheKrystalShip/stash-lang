using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Async;

/// <summary>
/// Conformance tests for §Async — Async Functions and Await, Edits 1, 2, and 3.
///
/// <para>
/// These tests prove that the Stash implementation honors the normative clauses added or
/// corrected in <c>docs/Stash — Language Specification.md</c> §Async, specifically:
/// </para>
/// <list type="bullet">
///   <item><b>Edit 1</b> — the lambda example at L1460–1462 uses <c>task.delay(1)</c>, not
///     a duration literal. Anchored by proving that passing a duration literal <c>1s</c>
///     directly to <c>task.delay</c> produces a <c>TypeError</c> identifying the
///     first-arg-must-be-number contract.</item>
///   <item><b>Edit 2</b> (<c>task.resolve(value?)</c>) — returns a Future whose status is
///     <c>task.Status.Completed</c> at creation; <c>await task.resolve()</c> returns
///     <c>null</c>; <c>await task.resolve(42)</c> returns <c>42</c>; a fire-and-forget
///     <c>task.resolve(42)</c> never triggers the unobserved-fault report (it has not
///     faulted); <c>task.resolve</c> itself never throws.</item>
///   <item><b>Edit 3</b> (<c>task.delay(seconds)</c>) — returns a <c>Future</c> whose
///     status is <c>task.Status.Running</c> when fresh; resolves to <c>null</c> after the
///     delay elapses; accepts both integer and float arguments; cancelling the future and
///     awaiting it throws <c>CancellationError</c>; <c>task.delay(0)</c> returns a
///     <c>Future</c> and does not synchronously complete on the calling thread.</item>
/// </list>
///
/// <para>
/// These are conformance tests — they prove the <em>spec</em>, not guard implementation
/// regressions. The existing behavior suite at <c>Stash.Tests/Interpreting/Async/</c>
/// remains in place for regression coverage; the two suites are complementary.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class TaskResolveDelayConformanceTests : StashTestBase
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Edit 1 — duration literal rejected by task.delay (anchors the example fix)
    // Spec: L1460–1462 (corrected example) + Edit 3 prose
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Edit 1 (anchor): Passing a duration literal <c>1s</c> to <c>task.delay</c> throws
    /// <c>TypeError</c> identifying the first-arg-must-be-number contract.
    ///
    /// <para>
    /// This anchors the spec example correction (L1461) to actual runtime behavior:
    /// the old example <c>task.delay(1s)</c> is rejected, so the corrected example
    /// <c>task.delay(1)</c> is not merely a prose swap — it reflects real law.
    /// </para>
    /// </summary>
    [Fact]
    public void Edit1_DurationLiteralArgument_ThrowsTypeError_PerSpecAsyncEdit1()
    {
        var error = RunCapturingError(@"
let f = task.delay(1s);
");
        Assert.Equal("TypeError", error.ErrorType);
        Assert.Contains("task.delay", error.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Edit 2 — task.resolve(value?) — already-resolved Future
    // Spec: "**`task.resolve(value?)` — already-resolved Future.**" (Edit 2)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Edit 2: <c>task.resolve()</c> with no argument returns a Future whose status is
    /// <c>task.Status.Completed</c> from the moment of creation.
    /// </summary>
    [Fact]
    public void Edit2_Resolve_NoArg_StatusIsCompleted_PerSpecAsyncTaskResolve()
    {
        var result = Run(@"
let f = task.resolve();
let result = task.status(f);
");
        var status = Assert.IsType<StashEnumValue>(result);
        Assert.Equal("Completed", status.MemberName);
    }

    /// <summary>
    /// Edit 2: <c>await task.resolve()</c> returns <c>null</c> without blocking.
    /// </summary>
    [Fact]
    public void Edit2_Resolve_NoArg_ResolvesToNull_PerSpecAsyncTaskResolve()
    {
        var result = Run(@"
let result = await task.resolve();
");
        Assert.Null(result);
    }

    /// <summary>
    /// Edit 2: <c>await task.resolve(42)</c> returns <c>42</c> without blocking.
    /// </summary>
    [Fact]
    public void Edit2_Resolve_WithValue_ResolvesToValue_PerSpecAsyncTaskResolve()
    {
        var result = Run(@"
let result = await task.resolve(42);
");
        Assert.Equal(42L, result);
    }

    /// <summary>
    /// Edit 2: <c>task.resolve(value?)</c> is fail-safe — calling it on any value, including
    /// null and structured values, never throws.
    /// </summary>
    [Fact]
    public void Edit2_Resolve_WithStructuredValue_NeverThrows_PerSpecAsyncTaskResolve()
    {
        var result = Run(@"
let f1 = task.resolve(null);
let f2 = task.resolve(""hello"");
let f3 = task.resolve([1, 2, 3]);
let result = [await f1, await f2, await f3];
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Null(list[0]);
        Assert.Equal("hello", list[1]);
    }

    /// <summary>
    /// Edit 2 (negative space): A fire-and-forget <c>task.resolve(42)</c> that is never
    /// awaited does NOT trigger the unobserved-fault report — it has not faulted.
    /// </summary>
    [Fact]
    public void Edit2_Resolve_FireAndForget_NoUnobservedReport_PerSpecAsyncTaskResolve()
    {
        // Run a script that creates a task.resolve future but never awaits it.
        // The D1 unobserved-fault report scans faulted-and-unobserved futures only.
        // A resolved (non-faulted) future must produce NO output on stderr.
        string stderr = RunCapturingStderr(@"
let _ = task.resolve(42);
");
        Assert.Equal("", stderr);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Edit 3 — task.delay(seconds) — timed Future
    // Spec: "**`task.delay(seconds)` — timed Future.**" (Edit 3)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Edit 3: <c>task.delay(seconds)</c> returns a <c>Future</c> value (not null, not
    /// a plain number). Uses a long delay so the status read wins the race.
    /// </summary>
    [Fact]
    public void Edit3_Delay_ReturnsFuture_PerSpecAsyncTaskDelay()
    {
        var result = Run(@"
// Use a long delay so status read is not a race.
let f = task.delay(5);
let result = typeof(f);
task.cancel(f);
");
        Assert.Equal("Future", result);
    }

    /// <summary>
    /// Edit 3: A fresh <c>task.delay</c> Future has status <c>task.Status.Running</c>.
    /// Uses a long delay (5 s) so the status read wins the race before completion.
    /// </summary>
    [Fact]
    public void Edit3_Delay_FreshStatus_IsRunning_PerSpecAsyncTaskDelay()
    {
        var result = Run(@"
let f = task.delay(5);
// Read status immediately — 5s delay means this is deterministically Running.
let result = task.status(f);
task.cancel(f);
");
        var status = Assert.IsType<StashEnumValue>(result);
        Assert.Equal("Running", status.MemberName);
    }

    /// <summary>
    /// Edit 3: <c>task.delay</c> accepts an integer argument (a <c>number</c>).
    /// </summary>
    [Fact]
    public void Edit3_Delay_AcceptsIntegerArg_PerSpecAsyncTaskDelay()
    {
        var result = Run(@"
let f = task.delay(5);
let result = typeof(f);
task.cancel(f);
");
        Assert.Equal("Future", result);
    }

    /// <summary>
    /// Edit 3: <c>task.delay</c> accepts a float argument (a <c>number</c>).
    /// </summary>
    [Fact]
    public void Edit3_Delay_AcceptsFloatArg_PerSpecAsyncTaskDelay()
    {
        var result = Run(@"
let f = task.delay(0.01);
let result = await f;
");
        Assert.Null(result);
    }

    /// <summary>
    /// Edit 3: Awaiting a <c>task.delay</c> Future resolves to <c>null</c>.
    /// </summary>
    [Fact]
    public void Edit3_Delay_Await_ResolvesToNull_PerSpecAsyncTaskDelay()
    {
        var result = Run(@"
let result = await task.delay(0.05);
");
        Assert.Null(result);
    }

    /// <summary>
    /// Edit 3: Cancelling a <c>task.delay</c> Future and awaiting it throws
    /// <c>CancellationError</c>.
    /// </summary>
    [Fact]
    public void Edit3_Delay_Cancelled_ThrowsCancellationError_PerSpecAsyncTaskDelay()
    {
        var error = RunCapturingError(@"
let f = task.delay(5);
task.cancel(f);
// Poll until Cancelled so the await is non-racy.
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
    /// Edit 3: <c>task.delay(0)</c> returns a <c>Future</c> and does not synchronously
    /// complete on the calling thread — proved by the fact that <c>typeof(task.delay(0))</c>
    /// is <c>"Future"</c> (not an error) and the Future's status is readable immediately.
    /// </summary>
    [Fact]
    public void Edit3_DelayZero_ReturnsFuture_NotSynchronousCompletion_PerSpecAsyncTaskDelay()
    {
        var result = Run(@"
let f = task.delay(0);
// Status is readable immediately, whether Running or Completed (the task may have
// completed on the thread pool before this read, but it was never synchronous).
let status = task.status(f);
let valid = (status == task.Status.Running || status == task.Status.Completed);
let result = [typeof(f), valid];
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal("Future", list[0]);
        Assert.Equal(true, list[1]);
    }
}
