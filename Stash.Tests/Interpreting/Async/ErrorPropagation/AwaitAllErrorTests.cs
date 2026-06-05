namespace Stash.Tests.Interpreting.Async.ErrorPropagation;

using Stash.Runtime.Types;
using Stash.Tests.Interpreting;

/// <summary>
/// D2 — task.awaitAll error-type preservation contract:
///   task.awaitAll never throws; per-element failures become StashError values whose
///   .type matches the original thrown type (not the synthetic "TaskError").
///   Cancelled futures become StashError with .type == "CancellationError".
/// </summary>
public class AwaitAllErrorTests : StashTestBase
{
    // ── TypeError is preserved ────────────────────────────────────────────────

    [Fact]
    public void AwaitAll_FaultedWithTypeError_ElementTypeIsTypeError()
    {
        var result = Run(@"
let f = task.run(() => { throw TypeError { message: ""nope"" }; });
let results = task.awaitAll([f]);
let elem = results[0];
let result = elem.type;
");
        Assert.Equal("TypeError", result);
    }

    [Fact]
    public void AwaitAll_FaultedWithTypeError_ElementMessageIsPreserved()
    {
        var result = Run(@"
let f = task.run(() => { throw TypeError { message: ""nope"" }; });
let results = task.awaitAll([f]);
let elem = results[0];
let result = elem.message;
");
        Assert.Equal("nope", result);
    }

    [Fact]
    public void AwaitAll_FaultedWithTypeError_ElementIsStashError()
    {
        var result = Run(@"
let f = task.run(() => { throw TypeError { message: ""nope"" }; });
let results = task.awaitAll([f]);
let result = typeof(results[0]) == ""Error"";
");
        Assert.Equal(true, result);
    }

    // ── ValueError is preserved ───────────────────────────────────────────────

    [Fact]
    public void AwaitAll_FaultedWithValueError_ElementTypeIsValueError()
    {
        var result = Run(@"
let f = task.run(() => { throw ValueError { message: ""bad value"" }; });
let results = task.awaitAll([f]);
let result = results[0].type;
");
        Assert.Equal("ValueError", result);
    }

    // ── Cancelled future → CancellationError ─────────────────────────────────

    [Fact]
    public void AwaitAll_CancelledFuture_ElementTypeIsCancellationError()
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

    [Fact]
    public void AwaitAll_CancelledFuture_ElementIsStashError()
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
let result = typeof(results[0]) == ""Error"";
");
        Assert.Equal(true, result);
    }

    // ── Mixed (one ok, one error) — order and values preserved ───────────────

    [Fact]
    public void AwaitAll_MixedOkAndError_OkElementHasCorrectValue()
    {
        var result = Run(@"
let ok = task.run(() => 42);
let bad = task.run(() => { throw TypeError { message: ""oops"" }; });
let results = task.awaitAll([ok, bad]);
let result = results[0];
");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void AwaitAll_MixedOkAndError_ErrorElementHasCorrectType()
    {
        var result = Run(@"
let ok = task.run(() => 42);
let bad = task.run(() => { throw TypeError { message: ""oops"" }; });
let results = task.awaitAll([ok, bad]);
let result = results[1].type;
");
        Assert.Equal("TypeError", result);
    }

    [Fact]
    public void AwaitAll_MixedOkAndError_DoesNotThrow()
    {
        // awaitAll is collect-all — must not throw even when a task fails.
        RunStatements(@"
let ok = task.run(() => 42);
let bad = task.run(() => { throw TypeError { message: ""oops"" }; });
let results = task.awaitAll([ok, bad]);
");
    }

    // ── awaitAll is collect-all: still never throws ────────────────────────

    [Fact]
    public void AwaitAll_AllFaulted_DoesNotThrow()
    {
        RunStatements(@"
let f1 = task.run(() => { throw TypeError { message: ""a"" }; });
let f2 = task.run(() => { throw ValueError { message: ""b"" }; });
let results = task.awaitAll([f1, f2]);
");
    }

    [Fact]
    public void AwaitAll_AllFaulted_AllElementsAreStashErrors()
    {
        var result = Run(@"
let f1 = task.run(() => { throw TypeError { message: ""a"" }; });
let f2 = task.run(() => { throw ValueError { message: ""b"" }; });
let results = task.awaitAll([f1, f2]);
let result = typeof(results[0]) == ""Error"" && typeof(results[1]) == ""Error"";
");
        Assert.Equal(true, result);
    }
}
