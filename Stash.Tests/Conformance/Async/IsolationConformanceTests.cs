using Stash.Runtime.Errors;
using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Async;

/// <summary>
/// Conformance tests for §Async — Async Functions and Await, §Async-child global isolation.
///
/// <para>
/// These tests prove that the Stash implementation honors the normative clauses of
/// <c>docs/Stash — Language Specification.md</c> §Async (current line range ~L1608–1657),
/// specifically the child global isolation model:
/// </para>
/// <list type="bullet">
///   <item><b>Frozen-share</b> (L1614–1616) — <c>readonly</c> declarations are shared by
///     reference across the fork. Any write to a frozen value throws <c>ReadOnlyError</c>.</item>
///   <item><b>Deep-clone</b> (L1617–1621) — Non-frozen reference-typed values (mutable arrays,
///     dicts) are deep-cloned at fork time. Mutations inside the child are call-local — they
///     affect only the child's clone, not the parent's original.</item>
///   <item><b>Cycle → ValueError</b> (L1655–1657) — If a non-frozen captured value contains a
///     cycle, the deep-clone at fork time throws <c>ValueError</c> with the cycle path in the
///     message.</item>
///   <item><b>Future-handle sharing</b> (L1509–1511, D9) — A <c>Future</c> handle is shared
///     (not cloned) across the isolation boundary. A child task can return a Future created by the
///     parent, and the parent can <c>await</c> the returned handle.</item>
/// </list>
///
/// <para>
/// These are conformance tests — they prove the <em>spec</em>, not guard implementation
/// regressions. The existing behavior suite at <c>Stash.Tests/Interpreting/Async/Isolation/</c>
/// remains in place for regression coverage; the two suites are complementary.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class IsolationConformanceTests : StashTestBase
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Frozen-share — shared by reference, writes throw ReadOnlyError
    // Spec: §Async-child global isolation ~L1614–1616
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Frozen-share: a <c>readonly</c> dict is shared by reference across the fork.
    /// The child can read any field from the frozen value without a copy being made.
    /// Spec: L1614–1616 — "Frozen values are shared by reference across the fork."
    /// </summary>
    [Fact]
    public void FrozenShare_ReadOnlyDict_ChildCanRead_PerSpecAsyncIsolation()
    {
        var result = Run(@"
readonly let config = { host: ""localhost"", port: 8080 };
async fn readConfig() {
    return config.host;
}
let result = await readConfig();
");
        Assert.Equal("localhost", result);
    }

    /// <summary>
    /// Frozen-share: any write attempt to a frozen value from within a child task throws
    /// <c>ReadOnlyError</c>. The error propagates back to the parent via <c>await</c>.
    /// Spec: L1644 — "Frozen values share by reference; any attempt to write throws ReadOnlyError."
    /// </summary>
    [Fact]
    public void FrozenShare_WriteAttempt_ThrowsReadOnlyError_PerSpecAsyncIsolation()
    {
        var error = RunCapturingError(@"
readonly let config = { host: ""localhost"" };
async fn tryWrite() {
    config.host = ""changed"";
}
await tryWrite();
");
        Assert.IsType<ReadOnlyError>(error);
    }

    /// <summary>
    /// Frozen-share: a frozen nested object graph — the entire reachable graph is frozen.
    /// The child can read deeply-nested fields; any write attempt on any level throws
    /// <c>ReadOnlyError</c>.
    /// Spec: L1614–1616 (shared by reference) + the deep-freeze semantics at L806–813.
    /// </summary>
    [Fact]
    public void FrozenShare_NestedFrozenGraph_ChildCanReadDeep_PerSpecAsyncIsolation()
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

    /// <summary>
    /// Frozen-share: a <c>readonly</c> array is shared by reference. The child can read
    /// elements; any mutation attempt throws <c>ReadOnlyError</c>.
    /// Spec: L1614–1616.
    /// </summary>
    [Fact]
    public void FrozenShare_FrozenArray_WriteThrowsReadOnlyError_PerSpecAsyncIsolation()
    {
        var error = RunCapturingError(@"
readonly let items = [1, 2, 3];
async fn tryPush() {
    arr.push(items, 4);
}
await tryPush();
");
        Assert.IsType<ReadOnlyError>(error);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Deep-clone — non-frozen values are deep-cloned at fork; mutations are call-local
    // Spec: §Async-child global isolation ~L1617–1621
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deep-clone: a mutable dict captured by an <c>async fn</c> is deep-cloned at fork
    /// time. The child's mutation stays call-local — the parent's original is unchanged.
    /// Spec: L1617–1621 — "Non-frozen reference-typed values are deep-cloned into the child
    /// at fork time. Mutations to a captured, non-frozen value inside an async fn body are
    /// call-local — they affect only the child's clone, not the parent's original."
    /// </summary>
    [Fact]
    public void DeepClone_MutableDict_ChildMutationIsCallLocal_PerSpecAsyncIsolation()
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
        Assert.Equal(1L, list[0]);  // child incremented its clone → 1
        Assert.Equal(0L, list[1]);  // parent unchanged → 0
    }

    /// <summary>
    /// Deep-clone: a mutable array captured by an <c>async fn</c> is deep-cloned at fork
    /// time. The child's push stays call-local — the parent's array is unchanged.
    /// Spec: L1617–1621.
    /// </summary>
    [Fact]
    public void DeepClone_MutableArray_ChildMutationIsCallLocal_PerSpecAsyncIsolation()
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

    /// <summary>
    /// Deep-clone: a mutable dict captured by a <c>task.run</c> lambda is deep-cloned at fork
    /// time. The child's mutation stays call-local.
    /// Spec: L1617–1621 (applies equally to task.run and async fn).
    /// </summary>
    [Fact]
    public void DeepClone_TaskRun_MutableDict_ChildMutationIsCallLocal_PerSpecAsyncIsolation()
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
        Assert.Equal(1L, list[0]);  // child saw 0 → incremented → 1
        Assert.Equal(0L, list[1]);  // parent unchanged
    }

    /// <summary>
    /// Deep-clone: arr.parMap callbacks each receive their own deep-clone of any captured
    /// mutable dict. After all callbacks complete, the parent's original is still unchanged.
    /// Spec: L1617–1621 (arr.par* bodies run as forked child VMs).
    /// </summary>
    [Fact]
    public void DeepClone_ParMap_EachCallbackGetsOwnClone_PerSpecAsyncIsolation()
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
        Assert.Equal(2L, list[0]);
        Assert.Equal(4L, list[1]);
        Assert.Equal(6L, list[2]);
        Assert.Equal(0L, list[3]);  // parent count unchanged
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Cycle → ValueError at fork
    // Spec: §Async-child global isolation ~L1655–1657
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cycle: a non-frozen captured value that contains a reference cycle throws
    /// <c>ValueError</c> synchronously at the async fn call site (fork time).
    /// Spec: L1655–1657 — "If a non-frozen value to be captured contains a cycle, the
    /// deep-clone at fork time throws ValueError with the cycle path in the message."
    /// </summary>
    [Fact]
    public void Cycle_AsyncFn_ThrowsValueError_PerSpecAsyncIsolation()
    {
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

    /// <summary>
    /// Cycle: the <c>ValueError</c> message contains both the word "cycle" (or "circular")
    /// and the cycle-path key that forms the loop. The spec says "with the cycle path in
    /// the message" — we assert the key name appears so the path content is pinned.
    /// Spec: L1655–1657.
    /// </summary>
    [Fact]
    public void Cycle_AsyncFn_ErrorMessage_ContainsCyclePathKey_PerSpecAsyncIsolation()
    {
        var error = RunCapturingError(@"
let d = {};
d[""self""] = d;
async fn withCycle() { let x = d; return 1; }
withCycle();
");
        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("self", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cycle: a task.run callback with a cyclic captured value faults the returned Future
    /// (the error becomes a faulted result observable via task.awaitAll). The error type is
    /// "ValueError".
    /// Spec: L1655–1657 (task.run is also a fork site).
    /// </summary>
    [Fact]
    public void Cycle_TaskRun_FaultsFuture_ErrorTypeIsValueError_PerSpecAsyncIsolation()
    {
        var result = Run(@"
let d = {};
d[""self""] = d;
let f = task.run(() => { let x = d; return 1; });
let results = task.awaitAll([f]);
let result = results[0].type;
");
        Assert.Equal("ValueError", result);
    }

    /// <summary>
    /// Cycle: arr.parMap with a cyclic captured value throws <c>ValueError</c> at the
    /// fork point (fail-fast — the error is propagated from the first faulted callback).
    /// Spec: L1655–1657 (arr.par* callbacks are fork sites).
    /// </summary>
    [Fact]
    public void Cycle_ParMap_ThrowsValueError_PerSpecAsyncIsolation()
    {
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

    // ─────────────────────────────────────────────────────────────────────────────
    // Future-handle sharing — Futures are shared (not cloned) across the fork
    // Spec: D9, L1509–1511
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Future-handle sharing: a Future created by the parent and captured inside an
    /// <c>async fn</c> is shared (not cloned) — the child receives the same object.
    /// The parent confirms reference identity via <c>==</c> after the child returns the handle.
    /// Spec: L1509–1511 — "A Future handle is shared, not cloned, across the isolation
    /// boundary — a child task can return a Future created by the parent and the parent
    /// can await it."
    /// </summary>
    [Fact]
    public void FutureHandle_SharedNotCloned_ReferenceIdentity_PerSpecAsyncIsolation()
    {
        var result = Run(@"
let parentFuture = task.resolve(""original"");
async fn returnsFuture() {
    return parentFuture;
}
let childReturned = await returnsFuture();
// childReturned is the exact same Future object as parentFuture
let result = parentFuture == childReturned;
");
        Assert.Equal(true, result);
    }

    /// <summary>
    /// Future-handle sharing: a Future captured by a <c>task.run</c> lambda is returned
    /// unchanged — same reference identity as the parent's original.
    /// Spec: L1509–1511.
    /// </summary>
    [Fact]
    public void FutureHandle_TaskRun_SharedByReference_PerSpecAsyncIsolation()
    {
        var result = Run(@"
let parentFuture = task.resolve(42);
let f = task.run(() => { return parentFuture; });
let childReturned = await f;
let result = parentFuture == childReturned;
");
        Assert.Equal(true, result);
    }

    /// <summary>
    /// Future-handle sharing: the parent can <c>await</c> a Future that was created by the
    /// parent but returned by the child — the handle is live and awaitable after crossing the
    /// boundary.
    /// Spec: L1509–1511 — "the parent can await it."
    /// </summary>
    [Fact]
    public void FutureHandle_ReturnedByChild_ParentCanAwait_PerSpecAsyncIsolation()
    {
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
