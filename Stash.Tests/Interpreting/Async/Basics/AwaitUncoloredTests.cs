namespace Stash.Tests.Interpreting.Async.Basics;

using Stash.Tests.Interpreting;

/// <summary>
/// D6 — await is blocking and uncolored:
///   - await works at the top level (no async context required).
///   - await works inside a non-async function.
///   - await works inside loops.
///   - await on a non-Future value passes the value through unchanged.
///
/// Only <c>async fn</c> (or <c>task.run</c>) spawns a new parallel task;
/// <c>await</c> merely joins one. No "async context" is required — it is synchronous
/// (blocks the current thread until the Future resolves).
/// </summary>
public class AwaitUncoloredTests : StashTestBase
{
    // ── Top-level await ───────────────────────────────────────────────────────

    [Fact]
    public void AwaitUncolored_TopLevel_ResolvesValue()
    {
        // await at the top level (not inside any function) must work.
        var result = Run(@"
let f = task.run(() => 42);
let result = await f;
");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void AwaitUncolored_TopLevel_AsyncFn()
    {
        var result = Run(@"
async fn compute() { return 99; }
let f = compute();
let result = await f;
");
        Assert.Equal(99L, result);
    }

    [Fact]
    public void AwaitUncolored_TopLevel_ChainedAwaits()
    {
        // Multiple sequential top-level awaits.
        var result = Run(@"
async fn addOne(n) { return n + 1; }
let a = await addOne(10);
let b = await addOne(a);
let result = b;
");
        Assert.Equal(12L, result);
    }

    // ── await inside a non-async function ────────────────────────────────────

    [Fact]
    public void AwaitUncolored_InsideNonAsyncFn_ResolvesValue()
    {
        // D6: await works inside a plain (non-async) function.
        var result = Run(@"
fn syncWrapper(n) {
    let f = task.run(() => n * 2);
    return await f;
}
let result = syncWrapper(5);
");
        Assert.Equal(10L, result);
    }

    [Fact]
    public void AwaitUncolored_InsideNonAsyncFn_BlocksUntilComplete()
    {
        // The synchronous function must not return before the Future resolves.
        var result = Run(@"
fn waitForTask() {
    let f = task.run(() => { time.sleep(0.01); return ""done""; });
    return await f;
}
let result = waitForTask();
");
        Assert.Equal("done", result);
    }

    [Fact]
    public void AwaitUncolored_InsideNonAsyncFn_CalledFromNonAsync()
    {
        // A non-async fn that awaits, called from another non-async fn.
        var result = Run(@"
fn inner() {
    return await task.resolve(7);
}
fn outer() {
    return inner() + 1;
}
let result = outer();
");
        Assert.Equal(8L, result);
    }

    // ── await inside loops ────────────────────────────────────────────────────

    [Fact]
    public void AwaitUncolored_InsideLoop_AccumulatesResults()
    {
        // await in a for-in loop.
        var result = Run(@"
let total = 0;
for (let i in [1, 2, 3, 4]) {
    let f = task.resolve(i);
    total = total + await f;
}
let result = total;
");
        Assert.Equal(10L, result);
    }

    [Fact]
    public void AwaitUncolored_InsideWhileLoop_Works()
    {
        var result = Run(@"
let i = 0;
let sum = 0;
while (i < 3) {
    let f = task.resolve(i);
    sum = sum + await f;
    i = i + 1;
}
let result = sum;
");
        Assert.Equal(3L, result);  // 0+1+2
    }

    [Fact]
    public void AwaitUncolored_InsideLoop_WithAsyncFn()
    {
        var result = Run(@"
async fn double(x) { return x * 2; }
let results = [];
for (let n in [1, 2, 3]) {
    arr.push(results, await double(n));
}
let result = results;
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(2L, list[0]);
        Assert.Equal(4L, list[1]);
        Assert.Equal(6L, list[2]);
    }

    // ── await is synchronous (blocks the current thread) ─────────────────────

    [Fact]
    public void AwaitUncolored_Synchronous_StatementsAfterRunInOrder()
    {
        // Statements after await execute after the Future resolves — demonstrating that
        // await is blocking. We use a time-based probe: spawn a task that sleeps briefly,
        // record the time before and after await, and assert that at least the sleep time
        // elapsed — confirming await blocked (not just submitted-and-returned).
        var result = Run(@"
let before = time.millis();
let f = task.run(() => { time.sleep(0.1); return ""done""; });
let r = await f;
let after = time.millis();
// After await, the task must have completed (returned ""done"")
// and at least 80ms must have elapsed (blocking sleep of 100ms with 20ms margin).
let elapsed = after - before;
let result = [r, elapsed >= 80];
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal("done", list[0]);
        Assert.Equal(true, list[1]);  // at least 80ms elapsed → await is blocking
    }

    // ── async fn returns Future immediately ───────────────────────────────────

    [Fact]
    public void AwaitUncolored_AsyncFnCallReturnsFuture()
    {
        var result = Run(@"
async fn work() { return 1; }
let f = work();
let result = typeof(f);
");
        Assert.Equal("Future", result);
    }

    [Fact]
    public void AwaitUncolored_TaskRunReturnsFuture()
    {
        var result = Run(@"
let f = task.run(() => 1);
let result = typeof(f);
");
        Assert.Equal("Future", result);
    }
}
