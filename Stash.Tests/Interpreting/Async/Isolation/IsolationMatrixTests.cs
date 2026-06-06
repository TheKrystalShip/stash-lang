namespace Stash.Tests.Interpreting.Async.Isolation;

using Stash.Runtime.Errors;
using Stash.Tests.Interpreting;
// Note: UserRuntimeError removed — cycle throws ValueError directly (sealed subclass of RuntimeError)

/// <summary>
/// Isolation matrix (D6 §Async-child global isolation):
///   - Mutable captured values are deep-cloned at fork time; child mutations do not
///     affect the parent ("call-local mutation").
///   - Frozen (readonly / freeze) values are shared by reference across the boundary.
///   - A cycle in a non-frozen captured value → ValueError at the fork point.
///   - A Future handle is shared (not cloned) across the isolation boundary.
///
/// Applies to: async fn, task.run callbacks, arr.par* callbacks.
/// </summary>
public class IsolationMatrixTests : StashTestBase
{
    // ── async fn: mutable value is cloned → child mutation stays local ────────

    [Fact]
    public void Isolation_AsyncFn_CapturedMutableDict_MutationIsCallLocal()
    {
        var result = Run(@"
let shared = { count: 0 };
async fn mutator() {
    shared.count = shared.count + 1;
    return shared.count;
}
let childVal = await mutator();
// childVal == 1 (child incremented its clone)
// shared.count == 0 (parent's copy unchanged)
let result = [childVal, shared.count];
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal(1L, list[0]);  // child saw 0 → incremented to 1
        Assert.Equal(0L, list[1]);  // parent unchanged
    }

    [Fact]
    public void Isolation_AsyncFn_CapturedMutableArray_MutationIsCallLocal()
    {
        var result = Run(@"
let shared = [1, 2, 3];
async fn appender() {
    arr.push(shared, 4);
    return len(shared);
}
let childLen = await appender();
let result = [childLen, len(shared)];
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(4L, list[0]);  // child appended → 4
        Assert.Equal(3L, list[1]);  // parent unchanged → 3
    }

    // ── task.run: mutable value is cloned → child mutation stays local ────────

    [Fact]
    public void Isolation_TaskRun_CapturedMutableDict_MutationIsCallLocal()
    {
        var result = Run(@"
let shared = { count: 0 };
let f = task.run(() => {
    shared.count = shared.count + 1;
    return shared.count;
});
let childVal = await f;
let result = [childVal, shared.count];
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(1L, list[0]);  // child incremented its clone
        Assert.Equal(0L, list[1]);  // parent unchanged
    }

    // ── arr.par*: mutable value is cloned → child mutation stays local ────────

    [Fact]
    public void Isolation_ParMap_CapturedMutableDict_MutationIsCallLocal()
    {
        var result = Run(@"
let shared = { count: 0 };
let results = arr.parMap([1, 2, 3], (x) => {
    shared.count = shared.count + 1;
    return x * 2;
});
// shared.count must still be 0 — each callback got its own clone
let result = [results[0], results[1], results[2], shared.count];
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2L, list[0]);   // parMap results correct
        Assert.Equal(4L, list[1]);
        Assert.Equal(6L, list[2]);
        Assert.Equal(0L, list[3]);   // parent count unchanged
    }

    [Fact]
    public void Isolation_ParMapAsync_CapturedMutableDict_MutationIsCallLocal()
    {
        var result = Run(@"
let shared = { count: 0 };
let results = arr.parMap([1, 2, 3], async (x) => {
    shared.count = shared.count + 1;
    return x * 2;
});
let result = [results[0], results[1], results[2], shared.count];
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2L, list[0]);
        Assert.Equal(4L, list[1]);
        Assert.Equal(6L, list[2]);
        Assert.Equal(0L, list[3]);  // parent count unchanged
    }

    // ── Frozen (readonly) values: shared by reference ─────────────────────────

    [Fact]
    public void Isolation_FrozenValue_SharedByReference_ReadSucceeds()
    {
        // A frozen dict can be read from inside async fn (shared ref, not a clone).
        var result = Run(@"
readonly let config = { host: ""localhost"", port: 8080 };
async fn readConfig() {
    return config.host;
}
let result = await readConfig();
");
        Assert.Equal("localhost", result);
    }

    [Fact]
    public void Isolation_FrozenValue_WriteThrowsReadOnlyError()
    {
        // A frozen dict shared across the boundary: any write attempt throws ReadOnlyError.
        var error = RunCapturingError(@"
readonly let config = { host: ""localhost"" };
async fn tryWrite() {
    config.host = ""changed"";
}
await tryWrite();
");
        Assert.IsType<ReadOnlyError>(error);
    }

    [Fact]
    public void Isolation_FrozenNestedValue_ReadSucceeds()
    {
        var result = Run(@"
readonly let cfg = { db: { host: ""db.internal"", port: 5432 } };
async fn readNested() {
    return cfg.db.port;
}
let result = await readNested();
");
        Assert.Equal(5432L, result);
    }

    // ── Cycle in non-frozen value: throws ValueError at fork ──────────────────

    [Fact]
    public void Isolation_AsyncFn_CycleInCapturedValue_ThrowsValueError()
    {
        // async fn sees a cyclic dict at fork — throws synchronously at the call site.
        // The runtime throws ValueError directly (not a UserRuntimeError wrapper).
        var error = RunCapturingError(@"
let d = {};
d[""self""] = d;
async fn withCycle() {
    let x = d;
    return 1;
}
withCycle();
");
        Assert.IsType<ValueError>(error);
    }

    [Fact]
    public void Isolation_AsyncFn_CycleMessage_ContainsCyclePath()
    {
        // done_when says "ValueError with the cycle path". The runtime message includes
        // path information such as 'path: <root> -> ["self"]'. Assert both "cycle" and
        // the path marker to pin the path contents, not just the category word.
        var error = RunCapturingError(@"
let d = {};
d[""self""] = d;
async fn withCycle() { let x = d; return 1; }
withCycle();
");
        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("self", error.Message, StringComparison.OrdinalIgnoreCase);  // key name appears in path
    }

    [Fact]
    public void Isolation_TaskRun_CycleInCapturedValue_FaultsFuture()
    {
        // task.run faults the returned Future (exception from the child thread).
        var result = Run(@"
let d = {};
d[""self""] = d;
let f = task.run(() => { let x = d; return 1; });
let results = task.awaitAll([f]);
let result = results[0].type;
");
        Assert.Equal("ValueError", result);
    }

    [Fact]
    public void Isolation_ParMap_CycleInCapturedValue_ThrowsValueError()
    {
        // parMap with a cyclic captured value throws at fork.
        // The runtime throws ValueError directly (not a UserRuntimeError wrapper).
        var error = RunCapturingError(@"
let d = {};
d[""self""] = d;
arr.parMap([1], (x) => {
    let v = d;
    return x;
});
");
        Assert.IsType<ValueError>(error);
    }

    // ── Future handle: shared by reference across isolation boundary ───────────

    [Fact]
    public void Isolation_FutureHandle_SharedNotCloned_SameReferenceIdentity()
    {
        // A Future created in the parent is passed into an async fn.
        // The child returns it. The parent checks reference identity (==).
        var result = Run(@"
let parentFuture = task.resolve(""original"");
async fn returnsFuture() {
    return parentFuture;
}
let childReturned = await returnsFuture();
// childReturned should be the exact same Future object as parentFuture
let result = parentFuture == childReturned;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Isolation_FutureHandle_CapturedByTaskRun_SameReference()
    {
        // Same test for task.run: a captured Future is returned unchanged (shared ref).
        var result = Run(@"
let parentFuture = task.resolve(42);
let f = task.run(() => { return parentFuture; });
let childReturned = await f;
let result = parentFuture == childReturned;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Isolation_FutureHandle_ParentCanAwaitChildReturnedFuture()
    {
        // If child returns the parent's Future, the parent can await the returned value.
        var result = Run(@"
let parentFuture = task.resolve(99);
async fn passThrough() {
    return parentFuture;
}
let childResult = await passThrough();
// childResult IS parentFuture (shared ref); awaiting it gives 99
let result = await childResult;
");
        Assert.Equal(99L, result);
    }
}
