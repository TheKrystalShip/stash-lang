namespace Stash.Tests.Interpreting.Async.LifecycleEdges;

using Stash.Tests.Interpreting;

/// <summary>
/// D9 — Future is a first-class value:
///   - typeof(future) == "Future"
///   - == tests reference identity: same Future == itself; two different Futures !=
///   - Stringifies as &lt;Future:Status&gt; (e.g. &lt;Future:Running&gt;, &lt;Future:Completed&gt;)
///   - Storable in arrays, dicts, and struct fields
/// </summary>
public class FutureAsValueTests : StashTestBase
{
    // ── typeof(future) == "Future" ────────────────────────────────────────────

    [Fact]
    public void FutureAsValue_TypeOf_IsFuture()
    {
        var result = Run(@"
let f = task.resolve(1);
let result = typeof(f);
");
        Assert.Equal("Future", result);
    }

    [Fact]
    public void FutureAsValue_TypeOf_AsyncFnReturnedFuture()
    {
        var result = Run(@"
async fn work() { return 1; }
let f = work();
let result = typeof(f);
");
        Assert.Equal("Future", result);
    }

    [Fact]
    public void FutureAsValue_TypeOf_TaskRunReturnedFuture()
    {
        var result = Run(@"
let f = task.run(() => 1);
let result = typeof(f);
");
        Assert.Equal("Future", result);
    }

    [Fact]
    public void FutureAsValue_IsFuture_TypeCheck()
    {
        var result = Run(@"
let f = task.resolve(42);
let result = f is Future;
");
        Assert.Equal(true, result);
    }

    // ── Reference identity for == ─────────────────────────────────────────────

    [Fact]
    public void FutureAsValue_SameFuture_EqualToItself()
    {
        var result = Run(@"
let f = task.resolve(1);
let g = f;
let result = f == g;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void FutureAsValue_TwoDifferentFutures_NotEqual()
    {
        var result = Run(@"
let f1 = task.resolve(1);
let f2 = task.resolve(1);
let result = f1 == f2;
");
        Assert.Equal(false, result);
    }

    [Fact]
    public void FutureAsValue_SameFuture_NotNotEqual()
    {
        var result = Run(@"
let f = task.resolve(1);
let g = f;
let result = f != g;
");
        Assert.Equal(false, result);
    }

    [Fact]
    public void FutureAsValue_TwoDifferentFutures_AreNotEqual()
    {
        var result = Run(@"
let f1 = task.resolve(2);
let f2 = task.resolve(2);
let result = f1 != f2;
");
        Assert.Equal(true, result);
    }

    // ── Stringification → <Future:Status> ────────────────────────────────────

    [Fact]
    public void FutureAsValue_Stringify_CompletedFuture()
    {
        var result = Run(@"
let f = task.resolve(99);
await f;
let result = conv.toStr(f);
");
        Assert.Equal("<Future:Completed>", result);
    }

    [Fact]
    public void FutureAsValue_Stringify_RunningFuture()
    {
        var result = Run(@"
let f = task.run(() => { time.sleep(5); return 1; });
let s = conv.toStr(f);
task.cancel(f);
let result = s;
");
        Assert.Equal("<Future:Running>", result);
    }

    [Fact]
    public void FutureAsValue_Stringify_FailedFuture()
    {
        var result = Run(@"
let f = task.run(() => { throw ValueError { message: ""oops"" }; });
// Wait for it to fault.
task.awaitAll([f]);
let result = conv.toStr(f);
");
        Assert.Equal("<Future:Failed>", result);
    }

    [Fact]
    public void FutureAsValue_Stringify_CancelledFuture()
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

    [Fact]
    public void FutureAsValue_InterpolationMatchesToStr()
    {
        // Implicit stringification via interpolation must match conv.toStr.
        var result = Run(@"
let f = task.resolve(1);
await f;
let result = $""{f}"";
");
        Assert.Equal("<Future:Completed>", result);
    }

    // ── Storable in arrays ────────────────────────────────────────────────────

    [Fact]
    public void FutureAsValue_StorableInArray()
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
        Assert.Equal(2, list.Count);
        Assert.Equal(10L, list[0]);
        Assert.Equal(20L, list[1]);
    }

    [Fact]
    public void FutureAsValue_StorableInArray_TypeofPreserved()
    {
        var result = Run(@"
let f = task.resolve(1);
let arr = [f, 42, ""text""];
let result = typeof(arr[0]);
");
        Assert.Equal("Future", result);
    }

    // ── Storable in dicts ─────────────────────────────────────────────────────

    [Fact]
    public void FutureAsValue_StorableInDict()
    {
        var result = Run(@"
let f = task.resolve(""stored"");
let d = { myFuture: f };
let r = await d[""myFuture""];
let result = r;
");
        Assert.Equal("stored", result);
    }

    [Fact]
    public void FutureAsValue_StorableInDict_IdentityPreserved()
    {
        var result = Run(@"
let f = task.resolve(1);
let d = { key: f };
let result = d[""key""] == f;
");
        Assert.Equal(true, result);
    }

    // ── Storable in struct fields ─────────────────────────────────────────────

    [Fact]
    public void FutureAsValue_StorableInStructField()
    {
        var result = Run(@"
struct TaskHolder {
    fut
}
let f = task.resolve(77);
let h = TaskHolder { fut: f };
let r = await h.fut;
let result = r;
");
        Assert.Equal(77L, result);
    }

    [Fact]
    public void FutureAsValue_StorableInStructField_TypeofPreserved()
    {
        var result = Run(@"
struct Holder {
    fut
}
let f = task.resolve(1);
let h = Holder { fut: f };
let result = typeof(h.fut);
");
        Assert.Equal("Future", result);
    }

    [Fact]
    public void FutureAsValue_StorableInStructField_IdentityPreserved()
    {
        var result = Run(@"
struct Holder {
    fut
}
let f = task.resolve(1);
let h = Holder { fut: f };
let result = h.fut == f;
");
        Assert.Equal(true, result);
    }
}
