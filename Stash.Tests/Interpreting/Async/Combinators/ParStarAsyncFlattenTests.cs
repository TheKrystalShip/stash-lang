namespace Stash.Tests.Interpreting.Async.Combinators;

using Stash.Runtime.Errors;
using Stash.Tests.Interpreting;

/// <summary>
/// D4 — arr.par* async callback flatten contract:
///   When the callback passed to arr.parMap / arr.parFilter / arr.parForEach is async,
///   its returned Future is unwrapped to the resolved value before any further processing —
///   the same IsAsync flatten pattern used by task.run.
/// </summary>
public class ParStarAsyncFlattenTests : StashTestBase
{
    // ── arr.parMap ────────────────────────────────────────────────────────────

    [Fact]
    public void ParMap_AsyncCallback_ReturnsResolvedValues()
    {
        var result = Run(@"
let result = arr.parMap([1, 2, 3], async (x) => x * 2);
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(2L, list[0]);
        Assert.Equal(4L, list[1]);
        Assert.Equal(6L, list[2]);
    }

    [Fact]
    public void ParMap_AsyncCallback_DoesNotReturnFutures()
    {
        var result = Run(@"
let result = arr.parMap([1, 2, 3], async (x) => x * 2);
");
        var list = Assert.IsType<List<object?>>(result);
        // Each element must be a resolved long, not a Future object
        foreach (var item in list)
        {
            Assert.IsType<long>(item);
        }
    }

    [Fact]
    public void ParMap_SyncCallback_UnchangedBehavior()
    {
        // Ensure sync callbacks still work correctly (no regression)
        var result = Run(@"
let result = arr.parMap([1, 2, 3], (x) => x * 2);
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2L, list[0]);
        Assert.Equal(4L, list[1]);
        Assert.Equal(6L, list[2]);
    }

    [Fact]
    public void ParMap_ThrowingAsyncCallback_FailsFast()
    {
        // "throw ValueError { ... }" in Stash becomes UserRuntimeError with ErrorType="ValueError"
        var err = RunCapturingError(@"
arr.parMap([1, 2, 3], async (x) => {
    if (x == 2) { throw ValueError { message: ""bad"" }; }
    return x;
});
");
        Assert.Equal("ValueError", err.ErrorType);
        Assert.Equal("bad", err.Message);
    }

    [Fact]
    public void ParMap_ThrowingAsyncCallback_SameTypesAsSync()
    {
        // Async throw must produce the same error type as sync throw (both are UserRuntimeError
        // carrying the Stash type name — the unwrap must not change the error type).
        var asyncErr = RunCapturingError(@"
arr.parMap([1], async (x) => { throw TypeError { message: ""async-err"" }; });
");
        var syncErr = RunCapturingError(@"
arr.parMap([1], (x) => { throw TypeError { message: ""async-err"" }; });
");
        Assert.Equal(syncErr.GetType(), asyncErr.GetType());
        Assert.Equal(syncErr.ErrorType, asyncErr.ErrorType);
    }

    // ── arr.parFilter ─────────────────────────────────────────────────────────

    [Fact]
    public void ParFilter_AsyncCallback_FiltersOnUnwrappedBool()
    {
        var result = Run(@"
let result = arr.parFilter([1, 2, 3, 4], async (x) => x % 2 == 0);
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal(2L, list[0]);
        Assert.Equal(4L, list[1]);
    }

    [Fact]
    public void ParFilter_SyncCallback_UnchangedBehavior()
    {
        var result = Run(@"
let result = arr.parFilter([1, 2, 3, 4], (x) => x % 2 == 0);
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2L, list[0]);
        Assert.Equal(4L, list[1]);
    }

    [Fact]
    public void ParFilter_ThrowingAsyncCallback_FailsFast()
    {
        var err = RunCapturingError(@"
arr.parFilter([1, 2, 3], async (x) => {
    if (x == 2) { throw ValueError { message: ""filter-err"" }; }
    return true;
});
");
        Assert.Equal("ValueError", err.ErrorType);
        Assert.Equal("filter-err", err.Message);
    }

    // ── arr.parForEach ────────────────────────────────────────────────────────

    [Fact]
    public void ParForEach_AsyncCallback_CompletesAllCallbacksBeforeReturning()
    {
        // Under the old (unfixed) behavior, parForEach returned before inner Futures resolved,
        // meaning a throwing async callback was silently swallowed. Under the new behavior,
        // FlattenAsyncCallbackResult blocks until the Future resolves, surfacing the error.
        var err = RunCapturingError(@"
arr.parForEach([1], async (x) => {
    throw ValueError { message: ""forEach-err"" };
});
");
        Assert.Equal("ValueError", err.ErrorType);
        Assert.Equal("forEach-err", err.Message);
    }

    [Fact]
    public void ParForEach_AsyncCallback_ThrowingPropagatesToCaller()
    {
        // Under old behavior: parForEach returns before inner Futures resolve, so the throw
        // is never observed. Under new behavior: GetResult() is called, surfacing the error.
        var err = RunCapturingError(@"
arr.parForEach([""a""], async (x) => {
    throw TypeError { message: ""type-err"" };
});
");
        Assert.Equal("TypeError", err.ErrorType);
    }

    [Fact]
    public void ParForEach_SyncCallback_UnchangedBehavior()
    {
        // Sync parForEach still completes without error
        RunStatements(@"
arr.parForEach([1, 2, 3], (x) => { let y = x * 2; });
");
    }
}
