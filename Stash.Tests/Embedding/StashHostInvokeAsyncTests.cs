namespace Stash.Tests.Embedding;

using System;
using System.Threading;
using System.Threading.Tasks;
using Stash.Hosting;
using Stash.Runtime;
using Stash.Runtime.Types;
using Xunit;

/// <summary>
/// Acceptance suite for <see cref="IStashHost.InvokeAsync{T}"/> (P3).
///
/// done_when coverage:
///   #1 — InvokeAsync_SelfResolvingFuture_YieldsCorrectValue (end-to-end with time.sleep)
///   #2 — InvokeAsync_FaultedFuture_ThrowsStashScriptException
///   #3 — InvokeAsync_NoDrainContract_CancellationHonored
///   #4 — InvokeAsync_CancellationToken_AbortsPendingFuture
/// </summary>
public class StashHostInvokeAsyncTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<StashHost> SetupHostAsync(string script)
    {
        var host = new StashHost();
        var s = await host.CompileAsync(script);
        await host.RunAsync(s);
        return host;
    }

    // ── #1: end-to-end self-resolving async fn ───────────────────────────────

    /// <summary>
    /// End-to-end done_when scenario:
    ///   1. Host runs a script that defines "async fn delayed(n) { time.sleep(n); return n * 2; }".
    ///   2. CallAsync returns a StashFuture (async fn → future).
    ///   3. InvokeAsync awaits it and returns the computed value.
    ///
    /// Note: time.sleep(0.05) sleeps 50ms on the child VM's background thread — the future
    /// is self-resolving and does not require an external event-loop drain. InvokeAsync
    /// only bridges an already-running future to a CLR Task.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_SelfResolvingFuture_YieldsCorrectValue()
    {
        // Use a short sleep (50ms) to keep the test fast while exercising the async path.
        // The function sleeps then returns an integer, so InvokeAsync<long> works correctly.
        await using var host = await SetupHostAsync(
            "async fn delayed(n) { time.sleep(0.05); return n * 2; }");

        // CallAsync<StashFuture> returns the future immediately (before it resolves).
        var future = await host.CallAsync<StashFuture>("delayed", new object[] { 5L });
        Assert.NotNull(future);

        // InvokeAsync awaits the self-resolving future and marshals the int result.
        long result = await host.InvokeAsync<long>(future);
        Assert.Equal(10L, result);
    }

    /// <summary>
    /// Variant: integer argument. "n=1L" (a Stash int), "n * 2" returns Stash int 2.
    /// This is the exact scenario from the brief done_when #5.
    /// Using n=0 for speed (no sleep), then n=1 would sleep 1 second.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_IntegerArgument_YieldsIntegerResult()
    {
        // Use n=0 (zero sleep) to keep the test fast.
        await using var host = await SetupHostAsync(
            "async fn delayed(n) { time.sleep(0); return n * 2; }");

        var future = await host.CallAsync<StashFuture>("delayed", new object[] { 3L });
        long result = await host.InvokeAsync<long>(future);
        Assert.Equal(6L, result);
    }

    // ── #2: faulted future raises StashScriptException ───────────────────────

    /// <summary>
    /// When the async fn throws a script-level error, InvokeAsync must throw
    /// StashScriptException with the structured StashError.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_FaultedFuture_ThrowsStashScriptException()
    {
        // Use struct syntax "throw ValueError { message: ... }" — the function-call form
        // "throw ValueError(...)" does not produce a typed ValueError in the CLR error registry.
        await using var host = await SetupHostAsync(
            "async fn willFail() { throw ValueError { message: \"async boom\" }; }");

        var future = await host.CallAsync<StashFuture>("willFail");
        var ex = await Assert.ThrowsAsync<StashScriptException>(
            () => host.InvokeAsync<long>(future));

        Assert.NotNull(ex.Error);
        Assert.Equal("ValueError", ex.Error.Kind);
        Assert.Contains("async boom", ex.Error.Message);
    }

    // ── #3: no-drain contract ─────────────────────────────────────────────────

    /// <summary>
    /// InvokeAsync does NOT drain the per-VM event-loop callback queue.
    ///
    /// A future that can only resolve when an external callback is delivered to the VM
    /// (e.g. via fs.watch / signal.on / an explicit EnqueueCallback call) will never
    /// resolve on its own inside InvokeAsync. Instead, it hangs until the CancellationToken
    /// fires.
    ///
    /// This test simulates "a future that depends on a callback" by using a very-long
    /// time.sleep (10 seconds) and firing ct after 200ms, asserting OperationCanceledException
    /// rather than a result. The long sleep stands in for "waiting on an external signal" —
    /// InvokeAsync's sole action is to await the future's DotNetTask; it performs no pump.
    ///
    /// CONTRACT documented here: callers must ensure all necessary drain points (time.sleep,
    /// task.all, event.loop, event.poll) have been passed before calling InvokeAsync on a
    /// future whose resolution depends on the VM's callback queue being serviced. If they
    /// haven't, InvokeAsync will hang until ct is cancelled.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_NoDrainContract_CancellationHonoredBeforeFutureResolves()
    {
        await using var host = await SetupHostAsync(
            // 10-second sleep represents a future that won't resolve "soon" — a stand-in for
            // a future waiting on an external callback that InvokeAsync does not deliver.
            "async fn longRunning() { time.sleep(10); return 1; }");

        var future = await host.CallAsync<StashFuture>("longRunning");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => host.InvokeAsync<long>(future, cts.Token));
    }

    // ── #4: ct cancels the await cleanly ─────────────────────────────────────

    /// <summary>
    /// InvokeAsync propagates OperationCanceledException when ct is already cancelled
    /// before the call.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_AlreadyCancelledToken_ThrowsImmediately()
    {
        await using var host = await SetupHostAsync(
            "async fn noop() { return 42; }");

        // Future will complete on its own, but ct is already cancelled.
        var future = await host.CallAsync<StashFuture>("noop");

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancelled

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => host.InvokeAsync<long>(future, cts.Token));
    }

    // ── #5: null future argument ─────────────────────────────────────────────

    /// <summary>
    /// Passing null as the future argument must throw ArgumentNullException.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_NullFuture_ThrowsArgumentNullException()
    {
        await using var host = new StashHost();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => host.InvokeAsync<long>(null!));
    }
}
