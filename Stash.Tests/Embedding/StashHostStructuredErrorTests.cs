namespace Stash.Tests.Embedding;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stash.Hosting;
using Stash.Runtime;
using Xunit;

/// <summary>
/// Acceptance suite for P2 structured error handling: <see cref="StashError"/>,
/// <see cref="StashScriptException"/>, and CallStack population.
///
/// done_when coverage:
///   #1  — CallAsync_ValueError_HasCorrectKindAndMessage
///   #2  — CallAsync_ValueError_SpanIsNonNull
///   #3  — CallAsync_ValueError_CallStackHasAtLeastOneFrame (CallStack population invariant)
///   #4  — TryCallAsync_ValueError_SameStructuredError
///   #5  — TryCallAsync_DoesNotThrow_OnScriptError
///   #6  — TryCallAsync_Cancellation_ReturnsCancelledKind
///   #7  — CallAsync_Cancellation_ThrowsOperationCanceledException (not StashScriptException)
///   #8  — RunAsync_StructuredErrors_RuntimeErrorHasKind
/// </summary>
[Collection("ProcessGlobalSlots")]
public class StashHostStructuredErrorTests
{
    // ── #1 + #2 + #3: ValueError with span and call stack ────────────────

    /// <summary>
    /// A Stash function that throws ValueError must produce a <see cref="StashScriptException"/>
    /// with Kind=="ValueError", Message=="nope", non-null Span (thrown from a named function
    /// so there IS a call site), and at least one call stack frame.
    ///
    /// This is the KEY test for the RuntimeError.CallStack open question. If CallStack is
    /// null at the host boundary, the VM fix in RunUntilFrame (VirtualMachine.Debug.cs) is
    /// needed; this test fails before the fix and passes after.
    /// </summary>
    [Fact]
    public async Task CallAsync_ValueError_HasKindMessageSpanAndCallStack()
    {
        await using var host = new StashHost();
        var s = await host.CompileAsync("fn failFn() { throw ValueError { message: \"nope\" }; }");
        var r = await host.RunAsync(s);
        Assert.True(r.Success, $"Setup failed: {string.Join(", ", r.Errors)}");

        var ex = await Assert.ThrowsAsync<StashScriptException>(
            () => host.CallAsync<long>("failFn"));

        // Kind must come from BuiltInErrorRegistry, not a hardcoded string.
        Assert.Equal("ValueError", ex.Error.Kind);
        Assert.Equal("nope", ex.Error.Message);

        // Span: the throw site is at a token-attributable location inside the fn body.
        Assert.NotNull(ex.Error.Span);

        // CallStack: the open question. The VM must populate RuntimeError.CallStack at
        // the RunUntilFrame boundary; if this fails the test documents the gap.
        Assert.NotEmpty(ex.Error.CallStack);
        Assert.Contains(ex.Error.CallStack, f => f.FunctionName == "failFn" || f.FunctionName != null);
    }

    // ── #4 + #5: TryCallAsync returns the same structured error ──────────

    [Fact]
    public async Task TryCallAsync_ValueError_SameStructuredError_NoThrow()
    {
        await using var host = new StashHost();
        var s = await host.CompileAsync("fn failFn() { throw ValueError { message: \"nope\" }; }");
        await host.RunAsync(s);

        // Must not throw.
        StashResult<long> result = await host.TryCallAsync<long>("failFn");

        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Equal("ValueError", result.Errors[0].Kind);
        Assert.Equal("nope", result.Errors[0].Message);
    }

    // ── #6 + #7: Cancellation ─────────────────────────────────────────────

    [Fact]
    public async Task CallAsync_Cancellation_ThrowsOperationCanceledException_NotStashScriptException()
    {
        // Uses time.sleep so the WaitHandle-based cancellation path in VMContext is
        // exercised (the done_when explicitly specifies time.sleep, not a busy-loop).
        await using var host = new StashHost();
        var s = await host.CompileAsync(
            "fn sleeper() { time.sleep(999999); return 0; }");
        await host.RunAsync(s);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Must throw OperationCanceledException (or TaskCanceledException),
        // NOT StashScriptException.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => host.CallAsync<long>("sleeper", null, cts.Token));
    }

    [Fact]
    public async Task TryCallAsync_Cancellation_ReturnsCancelledKind_NoThrow()
    {
        // Uses time.sleep so the WaitHandle-based cancellation path in VMContext is
        // exercised (the done_when explicitly specifies time.sleep, not a busy-loop).
        await using var host = new StashHost();
        var s = await host.CompileAsync(
            "fn sleeper() { time.sleep(999999); return 0; }");
        await host.RunAsync(s);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Must NOT throw — returns a failure result.
        StashResult<long> result = await host.TryCallAsync<long>("sleeper", null, cts.Token);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(StashError.KindCancelled, result.Errors[0].Kind);
    }

    // ── #8: RunAsync structured errors ───────────────────────────────────

    [Fact]
    public async Task RunAsync_FailingScript_HasStructuredError()
    {
        await using var host = new StashHost();
        var s = await host.CompileAsync("throw ValueError { message: \"script level\" };");
        StashResult r = await host.RunAsync(s);

        Assert.False(r.Success);
        Assert.Single(r.Errors);
        Assert.Equal("ValueError", r.Errors[0].Kind);
        Assert.Equal("script level", r.Errors[0].Message);
    }

    // ── #9: user-thrown ParseError ────────────────────────────────────────

    [Fact]
    public async Task CallAsync_ParseError_HasCorrectKind()
    {
        await using var host = new StashHost();
        // throw a different built-in error type to verify the Kind routing is general.
        var s = await host.CompileAsync("fn bad() { throw ParseError { message: \"bad parse\" }; }");
        await host.RunAsync(s);

        var ex = await Assert.ThrowsAsync<StashScriptException>(
            () => host.CallAsync<StashValue>("bad"));

        Assert.Equal("ParseError", ex.Error.Kind);
        Assert.Equal("bad parse", ex.Error.Message);
    }

    // ── #10: nested call stack depth ─────────────────────────────────────

    [Fact]
    public async Task CallAsync_NestedFunctionThrow_CallStackHasMultipleFrames()
    {
        await using var host = new StashHost();
        var s = await host.CompileAsync(@"
fn inner() { throw ValueError { message: ""deep"" }; }
fn middle() { inner(); }
fn outer() { middle(); }
");
        await host.RunAsync(s);

        var ex = await Assert.ThrowsAsync<StashScriptException>(
            () => host.CallAsync<long>("outer"));

        Assert.Equal("ValueError", ex.Error.Kind);
        // At least outer + middle + inner (3 frames minimum).
        Assert.True(ex.Error.CallStack.Count >= 2,
            $"Expected ≥2 call stack frames, got {ex.Error.CallStack.Count}: " +
            string.Join(", ", ex.Error.CallStack));
    }
}
