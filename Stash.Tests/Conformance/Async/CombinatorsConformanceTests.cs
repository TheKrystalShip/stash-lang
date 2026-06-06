using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Async;

/// <summary>
/// Conformance tests for §Async — Async Functions and Await, clauses D2, D4, D10, and Edit 7.
///
/// <para>
/// These tests prove that the Stash implementation honors the normative clauses of
/// <c>docs/Stash — Language Specification.md</c> §Async (L1428–1787), specifically:
/// </para>
/// <list type="bullet">
///   <item><b>D2</b> (L1513–1524) — <c>task.awaitAll</c> is the collect-all combinator:
///     never throws; faulted elements become <c>StashError</c> values with the original
///     error type preserved (e.g. <c>.type == "TypeError"</c>); cancelled elements have
///     <c>.type == "CancellationError"</c>. Fail-fast combinators (<c>task.all</c>,
///     <c>task.race</c>, <c>task.awaitAny</c>) rethrow the original error type.</item>
///   <item><b>D4</b> (L1536–1537) — <c>arr.par*</c> async-callback auto-flatten:
///     if the callback is <c>async</c>, its returned Future is automatically awaited, so
///     <c>arr.parMap([1,2,3], async (x) =&gt; x * 2)</c> returns <c>[2, 4, 6]</c>
///     — not an array of Futures.</item>
///   <item><b>D10</b> (L1526–1537) — <c>arr.par*</c> input-order preservation; first-error
///     fail-fast; <c>maxConcurrency = N</c> is accepted; <c>maxConcurrency &lt; 1</c>
///     throws <c>ValueError</c>.</item>
///   <item><b>Edit 7</b> (D10 extended) — <c>arr.parForEach</c> is side-effect-only and
///     returns <c>null</c>; contrasted with <c>arr.parMap</c> (returns result array) and
///     <c>arr.parFilter</c> (returns passing-elements array).</item>
/// </list>
///
/// <para>
/// These are conformance tests — they prove the <em>spec</em>, not guard implementation
/// regressions. The existing behavior suite at <c>Stash.Tests/Interpreting/Async/</c>
/// remains in place for regression coverage; the two suites are complementary.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class CombinatorsConformanceTests : StashTestBase
{
    // ─────────────────────────────────────────────────────────────────────────────
    // D2 — task.awaitAll is collect-all: never throws; error-type preservation
    // Spec: L1513–1524
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// D2: <c>task.awaitAll</c> never throws even when all constituent tasks fault.
    /// </summary>
    [Fact]
    public void D2_AwaitAll_AllFaulted_NeverThrows_PerSpecAsyncD2()
    {
        // Must not throw — awaitAll is collect-all, not fail-fast.
        RunStatements(@"
let f1 = task.run(() => { throw TypeError { message: ""a"" }; });
let f2 = task.run(() => { throw ValueError { message: ""b"" }; });
task.awaitAll([f1, f2]);
");
    }

    /// <summary>
    /// D2: A faulted element becomes a <c>StashError</c> value (not thrown).
    /// <c>typeof(elem) == "Error"</c> is true for a faulted element.
    /// </summary>
    [Fact]
    public void D2_AwaitAll_FaultedElement_IsStashErrorValue_PerSpecAsyncD2()
    {
        var result = Run(@"
let f = task.run(() => { throw TypeError { message: ""boom"" }; });
let results = task.awaitAll([f]);
let result = typeof(results[0]) == ""Error"";
");
        Assert.Equal(true, result);
    }

    /// <summary>
    /// D2: A faulted element's <c>.type</c> preserves the original error type
    /// (e.g. a task that throws <c>TypeError</c> yields an element with <c>.type == "TypeError"</c>).
    /// </summary>
    [Fact]
    public void D2_AwaitAll_FaultedElement_TypeIsPreserved_TypeError_PerSpecAsyncD2()
    {
        var result = Run(@"
let f = task.run(() => { throw TypeError { message: ""type mismatch"" }; });
let results = task.awaitAll([f]);
let result = results[0].type;
");
        Assert.Equal("TypeError", result);
    }

    /// <summary>
    /// D2: A faulted element's <c>.type</c> preserves the original error type
    /// (e.g. a task that throws <c>ValueError</c> yields an element with <c>.type == "ValueError"</c>).
    /// </summary>
    [Fact]
    public void D2_AwaitAll_FaultedElement_TypeIsPreserved_ValueError_PerSpecAsyncD2()
    {
        var result = Run(@"
let f = task.run(() => { throw ValueError { message: ""bad value"" }; });
let results = task.awaitAll([f]);
let result = results[0].type;
");
        Assert.Equal("ValueError", result);
    }

    /// <summary>
    /// D2: A cancelled element has <c>.type == "CancellationError"</c>.
    /// </summary>
    [Fact]
    public void D2_AwaitAll_CancelledElement_TypeIsCancellationError_PerSpecAsyncD2()
    {
        var result = Run(@"
let f = task.run(() => { time.sleep(10); });
task.cancel(f);
let i = 0;
while (task.status(f) == task.Status.Running && i < 40) {
    time.sleep(0.05);
    i = i + 1;
}
let results = task.awaitAll([f]);
let result = results[0].type;
");
        Assert.Equal("CancellationError", result);
    }

    /// <summary>
    /// D2: A successful element returns its value; a faulted element becomes a StashError —
    /// results are in input order, and the overall call never throws.
    /// </summary>
    [Fact]
    public void D2_AwaitAll_MixedResults_OrderPreservedNeverThrows_PerSpecAsyncD2()
    {
        var result = Run(@"
let ok = task.run(() => 42);
let bad = task.run(() => { throw TypeError { message: ""oops"" }; });
let results = task.awaitAll([ok, bad]);
let result = [results[0], results[1].type];
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(42L, list[0]);
        Assert.Equal("TypeError", list[1]);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // D2 — fail-fast combinators rethrow original error type
    // Spec: L1513–1518 ("task.all, task.race, and task.awaitAny are fail-fast…")
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// D2 (fail-fast): <c>task.awaitAny</c> rethrows the original error type — not wrapped
    /// to <c>RuntimeError</c> — when the first-completing task faults.
    /// </summary>
    [Fact]
    public void D2_AwaitAny_FaultedFirst_RethrowsOriginalType_PerSpecAsyncD2()
    {
        var error = RunCapturingError(@"
let f = task.run(() => { throw TypeError { message: ""original"" }; });
task.awaitAny([f]);
");
        Assert.Equal("TypeError", error.ErrorType);
        Assert.Contains("original", error.Message);
    }

    /// <summary>
    /// D2 (fail-fast): <c>task.all</c> rethrows the original error type when a constituent
    /// task faults. Awaiting the future returned by <c>task.all</c> throws the same typed error.
    /// </summary>
    [Fact]
    public void D2_All_FaultedTask_RethrowsOriginalType_PerSpecAsyncD2()
    {
        var error = RunCapturingError(@"
let f = task.run(() => { throw TypeError { message: ""original"" }; });
let combined = task.all([f]);
await combined;
");
        Assert.Equal("TypeError", error.ErrorType);
        Assert.Contains("original", error.Message);
    }

    /// <summary>
    /// D2 (fail-fast): <c>task.race</c> rethrows the original error type when the first-completing
    /// task faults. Awaiting the future returned by <c>task.race</c> throws the same typed error.
    /// </summary>
    [Fact]
    public void D2_Race_FaultedTask_RethrowsOriginalType_PerSpecAsyncD2()
    {
        var error = RunCapturingError(@"
let f = task.run(() => { throw ValueError { message: ""race-err"" }; });
let raceResult = task.race([f]);
await raceResult;
");
        Assert.Equal("ValueError", error.ErrorType);
        Assert.Contains("race-err", error.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // D4 — arr.par* async-callback flatten
    // Spec: L1536–1537
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// D4: <c>arr.parMap([1,2,3], async (x) =&gt; x * 2)</c> returns <c>[2, 4, 6]</c>
    /// — not an array of Futures. The async callback's Future is automatically awaited.
    /// </summary>
    [Fact]
    public void D4_ParMap_AsyncCallback_ReturnsFlattenedValues_NotFutures_PerSpecAsyncD4()
    {
        var result = Run(@"
let result = arr.parMap([1, 2, 3], async (x) => x * 2);
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(2L, list[0]);
        Assert.Equal(4L, list[1]);
        Assert.Equal(6L, list[2]);
        // Each element must be a resolved value, not a Future
        foreach (var item in list)
        {
            Assert.IsType<long>(item);
        }
    }

    /// <summary>
    /// D4: <c>arr.parFilter</c> with an async predicate: the Future is awaited and
    /// the resolved boolean determines inclusion. Result is not an array of Futures.
    /// </summary>
    [Fact]
    public void D4_ParFilter_AsyncCallback_ReturnsFlattenedElements_NotFutures_PerSpecAsyncD4()
    {
        var result = Run(@"
let result = arr.parFilter([1, 2, 3, 4], async (x) => x % 2 == 0);
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal(2L, list[0]);
        Assert.Equal(4L, list[1]);
    }

    /// <summary>
    /// D4: <c>arr.parForEach</c> with an async callback: the Future is awaited before
    /// completion — errors from async callbacks propagate to the caller.
    /// </summary>
    [Fact]
    public void D4_ParForEach_AsyncCallback_AwaitsCompletion_ErrorsPropagated_PerSpecAsyncD4()
    {
        var error = RunCapturingError(@"
arr.parForEach([1], async (x) => {
    throw ValueError { message: ""forEach-async-err"" };
});
");
        Assert.Equal("ValueError", error.ErrorType);
        Assert.Contains("forEach-async-err", error.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // D10 — arr.par* order preservation, fail-fast, maxConcurrency
    // Spec: L1526–1537
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// D10: <c>arr.parMap</c> returns results in input order even when async callbacks
    /// complete out of order (earlier index sleeps longer → completes last).
    /// </summary>
    [Fact]
    public void D10_ParMap_AsyncCallbacks_ResultsInInputOrder_PerSpecAsyncD10()
    {
        var result = Run(@"
// Element 0 sleeps the longest; element 2 finishes first.
// Input-order preservation means output must be [1, 2, 3].
let result = arr.parMap([1, 2, 3], async (x) => {
    time.sleep((4 - x) * 20);
    return x;
});
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(2L, list[1]);
        Assert.Equal(3L, list[2]);
    }

    /// <summary>
    /// D10: <c>arr.parFilter</c> preserves the relative order of passing elements
    /// even when async predicates complete out of order.
    /// </summary>
    [Fact]
    public void D10_ParFilter_AsyncCallbacks_PreservesInputOrder_PerSpecAsyncD10()
    {
        var result = Run(@"
// Even elements kept; callbacks complete in reverse input order.
// Output order must still be [2, 4].
let result = arr.parFilter([1, 2, 3, 4], async (x) => {
    time.sleep((5 - x) * 20);
    return x % 2 == 0;
});
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal(2L, list[0]);
        Assert.Equal(4L, list[1]);
    }

    /// <summary>
    /// D10 (fail-fast): <c>arr.parMap</c> rethrows the first error — with the original
    /// error type preserved — if any callback throws.
    /// </summary>
    [Fact]
    public void D10_ParMap_FailFast_RethrowsOriginalErrorType_PerSpecAsyncD10()
    {
        var error = RunCapturingError(@"
arr.parMap([1, 2, 3], (x) => {
    if (x == 2) { throw ValueError { message: ""parmap-err"" }; }
    return x;
});
");
        Assert.Equal("ValueError", error.ErrorType);
        Assert.Contains("parmap-err", error.Message);
    }

    /// <summary>
    /// D10: <c>maxConcurrency = N</c> is accepted and limits parallelism; must be &gt;= 1.
    /// Passing a valid positive value does not throw.
    /// </summary>
    [Fact]
    public void D10_ParMap_MaxConcurrency_Accepted_PerSpecAsyncD10()
    {
        // maxConcurrency = 2 must be accepted and produce correct results.
        var result = Run(@"
let result = arr.parMap([1, 2, 3], (x) => x * 10, 2);
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(10L, list[0]);
        Assert.Equal(20L, list[1]);
        Assert.Equal(30L, list[2]);
    }

    /// <summary>
    /// D10: <c>maxConcurrency &lt; 1</c> throws <c>ValueError</c> (not a generic error).
    /// </summary>
    [Fact]
    public void D10_ParMap_MaxConcurrencyZero_ThrowsValueError_PerSpecAsyncD10()
    {
        var error = RunCapturingError(@"
arr.parMap([1, 2, 3], (x) => x, 0);
");
        Assert.Equal("ValueError", error.ErrorType);
    }

    /// <summary>
    /// D10: <c>arr.parFilter</c> with <c>maxConcurrency &lt; 1</c> throws <c>ValueError</c>.
    /// </summary>
    [Fact]
    public void D10_ParFilter_MaxConcurrencyZero_ThrowsValueError_PerSpecAsyncD10()
    {
        var error = RunCapturingError(@"
arr.parFilter([1, 2], (x) => true, 0);
");
        Assert.Equal("ValueError", error.ErrorType);
    }

    /// <summary>
    /// D10: <c>arr.parForEach</c> with <c>maxConcurrency &lt; 1</c> throws <c>ValueError</c>.
    /// </summary>
    [Fact]
    public void D10_ParForEach_MaxConcurrencyZero_ThrowsValueError_PerSpecAsyncD10()
    {
        var error = RunCapturingError(@"
arr.parForEach([1, 2], (x) => { let y = x; }, 0);
");
        Assert.Equal("ValueError", error.ErrorType);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Edit 7 — arr.parForEach return value is null (D10 extended)
    // Spec: "D10 (extended) — arr.parForEach return value." (added by Edit 7)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Edit 7: <c>arr.parForEach</c> is side-effect-only and returns <c>null</c>.
    /// Proved by capturing its return value and asserting it equals null.
    /// </summary>
    [Fact]
    public void Edit7_ParForEach_ReturnsNull_PerSpecAsyncD10Edit7()
    {
        var result = Run(@"
let r = arr.parForEach([1, 2, 3], (x) => { let y = x * 2; });
let result = r;
");
        Assert.Null(result);
    }

    /// <summary>
    /// Edit 7: <c>arr.parForEach</c> with an async callback still returns <c>null</c>
    /// (not the Future, not a result array — the return value is always null).
    /// </summary>
    [Fact]
    public void Edit7_ParForEach_AsyncCallback_StillReturnsNull_PerSpecAsyncD10Edit7()
    {
        var result = Run(@"
let r = arr.parForEach([1, 2, 3], async (x) => { let y = await task.resolve(x * 2); });
let result = r;
");
        Assert.Null(result);
    }

    /// <summary>
    /// Edit 7 contrast: <c>arr.parMap</c> returns a result array (not null).
    /// Documents the distinction between parForEach (null) and parMap (array).
    /// <c>typeof(arr)</c> is <c>"array"</c> in Stash.
    /// </summary>
    [Fact]
    public void Edit7_ParMap_ReturnsArray_ContrastingWithParForEach_PerSpecAsyncD10Edit7()
    {
        var result = Run(@"
let r = arr.parMap([1, 2, 3], (x) => x * 2);
let result = typeof(r);
");
        Assert.Equal("array", result);
    }

    /// <summary>
    /// Edit 7 contrast: <c>arr.parFilter</c> returns the passing-elements array (not null).
    /// Documents the distinction between parForEach (null) and parFilter (array).
    /// <c>typeof(arr)</c> is <c>"array"</c> in Stash.
    /// </summary>
    [Fact]
    public void Edit7_ParFilter_ReturnsArray_ContrastingWithParForEach_PerSpecAsyncD10Edit7()
    {
        var result = Run(@"
let r = arr.parFilter([1, 2, 3], (x) => x > 1);
let result = typeof(r);
");
        Assert.Equal("array", result);
    }
}
