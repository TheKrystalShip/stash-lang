using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Async;

/// <summary>
/// Conformance tests for §Async — Async Functions and Await, clauses D6–D9 and Edit 6.
///
/// <para>
/// These tests prove that the Stash implementation honors the normative clauses of
/// <c>docs/Stash — Language Specification.md</c> §Async (L1428–1787), specifically:
/// </para>
/// <list type="bullet">
///   <item><b>D6</b> (L1464–1469) — <c>await</c> is blocking and uncolored.</item>
///   <item><b>D7</b> (L1471–1479) — Error-type fidelity through <c>await</c>.</item>
///   <item><b>D8</b> (L1481–1485 + Edit 6) — Double-await is idempotent; <c>await</c>
///     on a settled Future replays the cached outcome without blocking.</item>
///   <item><b>D9</b> (L1487–1498) — Future is a first-class value.</item>
/// </list>
///
/// <para>
/// <b>Edit 6</b> extends D8 with the settled-future-replay clause: awaiting a Future
/// whose status is <c>task.Status.Completed</c> returns the cached result; awaiting one
/// whose status is <c>task.Status.Failed</c> rethrows the same typed error; awaiting one
/// whose status is <c>task.Status.Cancelled</c> throws <c>CancellationError</c>.
/// The body never runs more than once.
/// </para>
///
/// <para>
/// These are conformance tests — they prove the <em>spec</em>, not guard implementation
/// regressions. The existing behavior suite at <c>Stash.Tests/Interpreting/Async/</c>
/// remains in place for regression coverage; the two suites are complementary.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class FuturesCoreConformanceTests : StashTestBase
{
    // ─────────────────────────────────────────────────────────────────────────────
    // D6 — await is blocking and uncolored
    // Spec: L1464–1469
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// D6: <c>await</c> works at the top level (no async context required).
    /// </summary>
    [Fact]
    public void D6_AwaitAtTopLevel_ReturnsValue_PerSpecAsyncD6()
    {
        var result = Run(@"
let f = task.run(() => 42);
let result = await f;
");
        Assert.Equal(42L, result);
    }

    /// <summary>
    /// D6: <c>await</c> works inside a non-async function.
    /// Only <c>async fn</c> / <c>task.run</c> spawns a new task; <c>await</c> merely joins.
    /// </summary>
    [Fact]
    public void D6_AwaitInsideNonAsyncFn_Blocks_PerSpecAsyncD6()
    {
        var result = Run(@"
fn joinIt() {
    let f = task.run(() => 77);
    return await f;
}
let result = joinIt();
");
        Assert.Equal(77L, result);
    }

    /// <summary>
    /// D6: <c>await</c> works inside a loop — it does not require an async context.
    /// </summary>
    [Fact]
    public void D6_AwaitInsideLoop_Accumulates_PerSpecAsyncD6()
    {
        var result = Run(@"
let sum = 0;
for (let i in [1, 2, 3]) {
    let f = task.resolve(i);
    sum = sum + await f;
}
let result = sum;
");
        Assert.Equal(6L, result);
    }

    /// <summary>
    /// D6: <c>await</c> on a non-Future value evaluates to the value unchanged (pass-through).
    /// </summary>
    [Fact]
    public void D6_AwaitNonFuture_PassThrough_PerSpecAsyncD6()
    {
        var result = Run(@"
let result = await 99;
");
        Assert.Equal(99L, result);
    }

    /// <summary>
    /// D6: <c>await</c> on <c>null</c> passes through <c>null</c>.
    /// </summary>
    [Fact]
    public void D6_AwaitNull_PassThrough_PerSpecAsyncD6()
    {
        var result = Run(@"
let result = await null;
");
        Assert.Null(result);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // D7 — Error-type fidelity through await
    // Spec: L1471–1479
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// D7: A <c>TypeError</c> thrown inside a task survives <c>await</c> with its type name intact.
    /// "Type fidelity" means the error's type name (as seen by Stash code via <c>e.type</c>) is
    /// preserved, not that the C# class identity is preserved. Stash <c>throw TypeError { ... }</c>
    /// produces a <c>UserRuntimeError</c> whose <c>ErrorType</c> is <c>"TypeError"</c>.
    /// </summary>
    [Fact]
    public void D7_TypeError_PreservedThroughAwait_PerSpecAsyncD7()
    {
        var error = RunCapturingError(@"
let f = task.run(() => { throw TypeError { message: ""type mismatch"" }; });
await f;
");
        Assert.Equal("TypeError", error.ErrorType);
        Assert.Contains("type mismatch", error.Message);
    }

    /// <summary>
    /// D7: A <c>ValueError</c> thrown inside a task survives <c>await</c> with its type name intact.
    /// </summary>
    [Fact]
    public void D7_ValueError_PreservedThroughAwait_PerSpecAsyncD7()
    {
        var error = RunCapturingError(@"
let f = task.run(() => { throw ValueError { message: ""bad value"" }; });
await f;
");
        Assert.Equal("ValueError", error.ErrorType);
        Assert.Contains("bad value", error.Message);
    }

    /// <summary>
    /// D7: An <c>IOError</c> thrown inside a task survives <c>await</c> with its type name intact.
    /// </summary>
    [Fact]
    public void D7_IOError_PreservedThroughAwait_PerSpecAsyncD7()
    {
        var error = RunCapturingError(@"
let f = task.run(() => { throw IOError { message: ""disk error"" }; });
await f;
");
        Assert.Equal("IOError", error.ErrorType);
        Assert.Contains("disk error", error.Message);
    }

    /// <summary>
    /// D7: A <c>StateError</c> thrown inside a task survives <c>await</c> with its type name intact.
    /// </summary>
    [Fact]
    public void D7_StateError_PreservedThroughAwait_PerSpecAsyncD7()
    {
        var error = RunCapturingError(@"
let f = task.run(() => { throw StateError { message: ""invalid state"" }; });
await f;
");
        Assert.Equal("StateError", error.ErrorType);
        Assert.Contains("invalid state", error.Message);
    }

    /// <summary>
    /// D7: <c>throw "string"</c> inside a task wraps to <c>RuntimeError</c> with the string
    /// as the message. The error type name is <c>"RuntimeError"</c>.
    /// </summary>
    [Fact]
    public void D7_ThrowString_WrapsToRuntimeError_PerSpecAsyncD7()
    {
        var error = RunCapturingError(@"
let f = task.run(() => { throw ""oops""; });
await f;
");
        // A Stash string throw produces a RuntimeError (type name "RuntimeError") with the
        // string as its message.
        Assert.Equal("RuntimeError", error.ErrorType);
        Assert.Equal("oops", error.Message);
    }

    /// <summary>
    /// D7: Cancellation produces <c>CancellationError</c> when the cancelled Future is awaited.
    /// </summary>
    [Fact]
    public void D7_CancelledFuture_ThrowsCancellationError_PerSpecAsyncD7()
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

    /// <summary>
    /// D7: Error type and message are visible through an in-script <c>try/catch</c>.
    /// Proves the Stash user sees a typed error object with <c>.type</c> matching the original type.
    /// </summary>
    [Fact]
    public void D7_ErrorTypeVisibleInScriptCatch_PerSpecAsyncD7()
    {
        var result = Run(@"
let f = task.run(() => { throw ValueError { message: ""caught"" }; });
let errType = """";
let errMsg = """";
try {
    await f;
} catch (e) {
    errType = e.type;
    errMsg = e.message;
}
let result = [errType, errMsg];
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal("ValueError", list[0]);
        Assert.Equal("caught", list[1]);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // D8 — Double-await is idempotent; await unwraps exactly one level
    // D8 (extended / Edit 6) — await on a settled Future replays the cached outcome
    // Spec: L1481–1485 + Edit 6
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// D8: Double-await of a completed Future returns the same cached result both times.
    /// The body runs exactly once.
    /// </summary>
    [Fact]
    public void D8_DoubleAwait_CompletedFuture_ReturnsCachedResult_PerSpecAsyncD8()
    {
        var result = Run(@"
let f = task.run(() => 55);
let r1 = await f;
let r2 = await f;
let result = [r1, r2];
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(55L, list[0]);
        Assert.Equal(55L, list[1]);
    }

    /// <summary>
    /// D8: <c>await</c> unwraps exactly one level of Future-of-Future.
    /// One await on a Future wrapping a Future yields the inner Future (not its value).
    /// </summary>
    [Fact]
    public void D8_AwaitFutureOfFuture_UnwrapsOneLevel_PerSpecAsyncD8()
    {
        var result = Run(@"
let inner = task.resolve(42);
let outer = task.resolve(inner);
let oneLevel = await outer;
let result = typeof(oneLevel);
");
        Assert.Equal("Future", result);
    }

    /// <summary>
    /// D8 + Edit 6 (Failed-replay): second await of a faulted Future rethrows the same typed
    /// error — same type and message — not a wrapped copy. The body never runs more than once.
    /// </summary>
    [Fact]
    public void D8Edit6_DoubleAwait_FaultedFuture_RethrowsSameTypedError_PerSpecAsyncD8()
    {
        var result = Run(@"
let f = task.run(() => { throw TypeError { message: ""original"" }; });
let firstType = """";
let firstMsg = """";
let secondType = """";
let secondMsg = """";
try {
    await f;
} catch (e) {
    firstType = e.type;
    firstMsg = e.message;
}
try {
    await f;
} catch (e) {
    secondType = e.type;
    secondMsg = e.message;
}
let result = [firstType, firstMsg, secondType, secondMsg];
");
        var list = Assert.IsType<List<object?>>(result);
        // First await: original error type and message
        Assert.Equal("TypeError", list[0]);
        Assert.Equal("original", list[1]);
        // Second await: SAME type and message — cached, not re-thrown with a different wrapper
        Assert.Equal("TypeError", list[2]);
        Assert.Equal("original", list[3]);
    }

    /// <summary>
    /// D8 + Edit 6 (Cancelled-replay): second await of a cancelled Future throws
    /// <c>CancellationError</c> without blocking. Subsequent awaits also throw <c>CancellationError</c>.
    /// </summary>
    [Fact]
    public void D8Edit6_DoubleAwait_CancelledFuture_ThrowsCancellationError_PerSpecAsyncD8()
    {
        var result = Run(@"
let f = task.run(() => { time.sleep(10); });
task.cancel(f);
// Wait for the cooperative cancel to take effect.
let i = 0;
while (task.status(f) == task.Status.Running && i < 40) {
    time.sleep(0.05);
    i = i + 1;
}
// Both awaits must throw CancellationError — non-blocking replay.
let firstType = """";
let secondType = """";
try {
    await f;
} catch (e) {
    firstType = e.type;
}
try {
    await f;
} catch (e) {
    secondType = e.type;
}
let result = [firstType, secondType];
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal("CancellationError", list[0]);
        Assert.Equal("CancellationError", list[1]);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // D9 — Future is a first-class value
    // Spec: L1487–1498
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// D9: <c>typeof(future) == "Future"</c> is true.
    /// </summary>
    [Fact]
    public void D9_TypeOf_IsFuture_PerSpecAsyncD9()
    {
        var result = Run(@"
let f = task.run(() => 1);
let result = typeof(f);
");
        Assert.Equal("Future", result);
    }

    /// <summary>
    /// D9: Reference identity — two variables holding the same Future are <c>==</c>.
    /// </summary>
    [Fact]
    public void D9_ReferenceIdentity_SameFuture_Equal_PerSpecAsyncD9()
    {
        var result = Run(@"
let f = task.resolve(1);
let g = f;
let result = f == g;
");
        Assert.Equal(true, result);
    }

    /// <summary>
    /// D9: Reference identity — two separately-created Futures are not <c>==</c>.
    /// </summary>
    [Fact]
    public void D9_ReferenceIdentity_SeparateFutures_NotEqual_PerSpecAsyncD9()
    {
        var result = Run(@"
let f1 = task.resolve(1);
let f2 = task.resolve(1);
let result = f1 == f2;
");
        Assert.Equal(false, result);
    }

    /// <summary>
    /// D9: <c>conv.toStr(future)</c> produces <c>"&lt;Future:Completed&gt;"</c> for a completed Future.
    /// </summary>
    [Fact]
    public void D9_ConvToStr_CompletedFuture_PerSpecAsyncD9()
    {
        var result = Run(@"
let f = task.resolve(42);
await f;
let result = conv.toStr(f);
");
        Assert.Equal("<Future:Completed>", result);
    }

    /// <summary>
    /// D9: <c>conv.toStr(future)</c> produces <c>"&lt;Future:Failed&gt;"</c> for a faulted Future.
    /// </summary>
    [Fact]
    public void D9_ConvToStr_FailedFuture_PerSpecAsyncD9()
    {
        var result = Run(@"
let f = task.run(() => { throw ValueError { message: ""x"" }; });
task.awaitAll([f]);
let result = conv.toStr(f);
");
        Assert.Equal("<Future:Failed>", result);
    }

    /// <summary>
    /// D9: <c>conv.toStr(future)</c> produces <c>"&lt;Future:Cancelled&gt;"</c> for a cancelled Future.
    /// </summary>
    [Fact]
    public void D9_ConvToStr_CancelledFuture_PerSpecAsyncD9()
    {
        var result = Run(@"
let f = task.run(() => { time.sleep(10); });
task.cancel(f);
let i = 0;
while (task.status(f) == task.Status.Running && i < 40) {
    time.sleep(0.05);
    i = i + 1;
}
let result = conv.toStr(f);
");
        Assert.Equal("<Future:Cancelled>", result);
    }

    /// <summary>
    /// D9: Futures can be stored in an array and later awaited.
    /// </summary>
    [Fact]
    public void D9_StoredInArray_CanBeAwaited_PerSpecAsyncD9()
    {
        var result = Run(@"
let f1 = task.resolve(10);
let f2 = task.resolve(20);
let futures = [f1, f2];
let r1 = await futures[0];
let r2 = await futures[1];
let result = [r1, r2];
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(10L, list[0]);
        Assert.Equal(20L, list[1]);
    }

    /// <summary>
    /// D9: Futures can be stored in a dict.
    /// </summary>
    [Fact]
    public void D9_StoredInDict_CanBeAwaited_PerSpecAsyncD9()
    {
        var result = Run(@"
let f = task.resolve(""stored"");
let d = { key: f };
let result = await d[""key""];
");
        Assert.Equal("stored", result);
    }

    /// <summary>
    /// D9: Futures can be stored in a struct field.
    /// </summary>
    [Fact]
    public void D9_StoredInStructField_CanBeAwaited_PerSpecAsyncD9()
    {
        var result = Run(@"
struct Holder { fut }
let f = task.resolve(77);
let h = Holder { fut: f };
let result = await h.fut;
");
        Assert.Equal(77L, result);
    }
}
