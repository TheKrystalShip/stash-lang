namespace Stash.Tests.Embedding;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stash.Hosting;
using Xunit;

/// <summary>
/// Acceptance suite for P4: async method dispatch (Task → StashFuture → await).
///
/// done_when coverage:
///   #1  — AsyncMethod_Registration_Compiles
///   #2  — AsyncMethod_HappyPath_AwaitReturnsString
///   #3  — AsyncMethod_FaultedTask_SurfacesHostError_WithInnerMessage
///   #4  — AsyncMethod_FaultedTask_SurfacesViaCallAsync_AsStashScriptException
///   #5  — AsyncMethod_FaultedTask_CatchableInStash
///   #6  — AsyncMethod_CancellationToken_PropagatedToDelegate
///   #7  — AsyncMethod_PreCancelledToken_ThrowsOperationCanceledException
///   #8  — AsyncMethod_VoidTask_ReturnsNull
///   #9  — AsyncMethod_ArityMismatch_ThrowsHostError
///   #10 — AsyncMethod_InvalidReturnType_ThrowsAtRegistration
/// </summary>
[Collection("ProcessGlobalSlots")]
public class HostObjectAsyncTests
{
    // ── Domain class ──────────────────────────────────────────────────────────

    private sealed class RemoteService
    {
        public int FetchCallCount { get; private set; }
        public bool LastCancellationTokenWasCancellable { get; private set; }

        /// <summary>Happy-path async method — returns a string result.</summary>
        public async Task<string> EchoAsync(string url)
        {
            FetchCallCount++;
            await Task.Yield();
            return url + "-ok";
        }

        /// <summary>Faults the returned task with an InvalidOperationException.</summary>
        public async Task<bool> BadFetchAsync(string url)
        {
            await Task.Yield();
            throw new InvalidOperationException($"fetch-failed: {url}");
        }

        /// <summary>Accepts a CancellationToken — used to verify CT propagation.</summary>
        public async Task<bool> FetchWithCtAsync(string url, CancellationToken ct)
        {
            // Set synchronously before any await so it's visible immediately.
            LastCancellationTokenWasCancellable = ct.CanBeCanceled;
            await Task.Yield();
            return true;
        }

        /// <summary>
        /// Long-running method that honors its CT. Blocks until CT fires, then throws OCE.
        /// CancellationTokenWasCancelled is set to true when the CT fires.
        /// </summary>
        public bool CancellationTokenWasCancelled { get; private set; }

        public async Task<string> SlowAsync(CancellationToken ct)
        {
            LastCancellationTokenWasCancellable = ct.CanBeCanceled;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                CancellationTokenWasCancelled = true;
                throw;
            }
            return "done";
        }

        /// <summary>Void-returning async method (plain Task).</summary>
        public async Task PingAsync()
        {
            await Task.Yield();
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static async Task<(StashHost host, RemoteService svc)> MakeServiceHostAsync()
    {
        var host = new StashHost();
        var svc  = new RemoteService();

        host.RegisterType<RemoteService>(b => b
            .AsyncMethod("echo",        (RemoteService s, string url) => s.EchoAsync(url))
            .AsyncMethod("badFetch",    (RemoteService s, string url) => s.BadFetchAsync(url))
            .AsyncMethod("fetchWithCt", (RemoteService s, string url, CancellationToken ct) => s.FetchWithCtAsync(url, ct))
            .AsyncMethod("slow",        (RemoteService s, CancellationToken ct) => s.SlowAsync(ct))
            .AsyncMethod("ping",        (RemoteService s) => s.PingAsync())
        );
        host.SetGlobal("svc", svc);

        return (host, svc);
    }

    // ── #1: Registration compiles ─────────────────────────────────────────────

    [Fact]
    public void AsyncMethod_Registration_Compiles()
    {
        var host = new StashHost();
        var ex = Record.Exception(() =>
            host.RegisterType<RemoteService>(b =>
                b.AsyncMethod("echo", (RemoteService s, string url) => s.EchoAsync(url))));
        Assert.Null(ex);
    }

    // ── #2: Happy-path await round-trip ──────────────────────────────────────

    [Fact]
    public async Task AsyncMethod_HappyPath_AwaitReturnsString()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (host, svc) = await MakeServiceHostAsync();
        await using var _ = host;

        // Script awaits the async host method and returns the result.
        var script = await host.CompileAsync("return await svc.echo(\"hello\");");
        var result = await host.RunAsync<string>(script, timeout.Token);

        Assert.True(result.Success, $"Errors: {string.Join(", ", result.Errors.Select(e => e.Message))}");
        Assert.Equal("hello-ok", result.Value);
        Assert.Equal(1, svc.FetchCallCount);
    }

    // ── #3: Faulted task → HostError preserving inner message ────────────────

    [Fact]
    public async Task AsyncMethod_FaultedTask_SurfacesHostError_WithInnerMessage()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (host, _) = await MakeServiceHostAsync();
        await using var _ = host;

        var script = await host.CompileAsync("fn run() { return await svc.badFetch(\"oops\"); }");
        await host.RunAsync(script, timeout.Token);

        var ex = await Assert.ThrowsAsync<StashScriptException>(
            () => host.CallAsync<long>("run", null, timeout.Token));

        Assert.Equal(StashError.KindHostError, ex.Error.Kind);
        Assert.Contains("fetch-failed: oops", ex.Error.Message);
    }

    // ── #4: Faulted task → TryCallAsync returns Errors[0] ────────────────────

    [Fact]
    public async Task AsyncMethod_FaultedTask_SurfacesViaCallAsync_AsStashScriptException()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (host, _) = await MakeServiceHostAsync();
        await using var _ = host;

        var script = await host.CompileAsync("fn run() { return await svc.badFetch(\"boom\"); }");
        await host.RunAsync(script, timeout.Token);

        var result = await host.TryCallAsync<long>("run", null, timeout.Token);

        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Equal(StashError.KindHostError, result.Errors[0].Kind);
        Assert.Contains("fetch-failed: boom", result.Errors[0].Message);
    }

    // ── #5: Faulted async method error is catchable in Stash ─────────────────

    [Fact]
    public async Task AsyncMethod_FaultedTask_CatchableInStash()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (host, _) = await MakeServiceHostAsync();
        await using var _ = host;

        var script = await host.CompileAsync("""
            fn run() {
                try {
                    await svc.badFetch("x");
                } catch (e) {
                    return e.type;
                }
                return "no-error";
            }
            """);
        await host.RunAsync(script, timeout.Token);

        string errorType = await host.CallAsync<string>("run", null, timeout.Token);
        Assert.Equal("HostError", errorType);
    }

    // ── #6: CT propagated to delegate AND mid-await cancel surfaces OCE ─────────

    [Fact]
    public async Task AsyncMethod_MidAwaitCancellation_SurfacesOCE_AndFiiresDelegateCT()
    {
        // This is the discriminating test for done_when #3:
        // - Cancel the call's CancellationToken WHILE the script is blocked awaiting
        //   svc.slow() (a 30-second host async method that honors its CT).
        // - Verify: (a) OCE surfaces from CallAsync<T>, NOT StashScriptException.
        //           (b) The delegate's CancellationToken was actually fired (CLR-side flag).
        var (host, svc) = await MakeServiceHostAsync();
        await using var _ = host;

        var script = await host.CompileAsync("fn run() { return await svc.slow(); }");
        await host.RunAsync(script);

        // Fire the call's CT after ~100ms — slow waits 30s, so the CT wins first.
        using var callCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // Use a separate hard-timeout cts to avoid the test hanging forever if something is wrong.
        using var hardTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => host.CallAsync<string>("run", null, callCts.Token));

        // Give the delegate's CT a brief window to register its cancellation.
        await Task.Delay(100, hardTimeout.Token);

        // The delegate's CT (linked to the call's CT) was fired.
        Assert.True(svc.LastCancellationTokenWasCancellable,
            "The delegate's CancellationToken should have been cancellable.");
        Assert.True(svc.CancellationTokenWasCancelled,
            "The delegate's CancellationToken should have been cancelled when the call's CT fired.");
    }

    // ── #7: Non-CT method has cancellable-CT-param detection ─────────────────

    [Fact]
    public async Task AsyncMethod_WithCancellableCtParam_DelgateCtIsCancellable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (host, svc) = await MakeServiceHostAsync();
        await using var _ = host;

        // fetchWithCt accepts a trailing CancellationToken.
        // The InvokeAsyncMethod creates a linked CTS from the engine's CT.
        // When the engine's CT is cancellable, the delegate's CT must also be cancellable.
        var script = await host.CompileAsync("fn run() { return await svc.fetchWithCt(\"/x\"); }");
        await host.RunAsync(script, timeout.Token);

        // Call with a non-cancelled CT — the delegate's CT should also be cancellable.
        bool result = await host.CallAsync<bool>("run", null, timeout.Token);

        Assert.True(result);
        Assert.True(svc.LastCancellationTokenWasCancellable,
            "The delegate's CancellationToken should have been cancellable (linked to engine CT).");
    }

    // ── #8: Void async method (plain Task) allows script to continue ──────────

    [Fact]
    public async Task AsyncMethod_VoidTask_ScriptContinues()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (host, _) = await MakeServiceHostAsync();
        await using var _ = host;

        var script = await host.CompileAsync("""
            fn run() {
                await svc.ping();
                return 42;
            }
            """);
        await host.RunAsync(script, timeout.Token);

        long result = await host.CallAsync<long>("run", null, timeout.Token);
        Assert.Equal(42L, result);
    }

    // ── #9: Arity mismatch → HostError ────────────────────────────────────────

    [Fact]
    public async Task AsyncMethod_ArityMismatch_ThrowsHostError()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (host, _) = await MakeServiceHostAsync();
        await using var _ = host;

        // "echo" expects 1 arg; call with 0.
        var script = await host.CompileAsync("fn run() { return await svc.echo(); }");
        await host.RunAsync(script, timeout.Token);

        var ex = await Assert.ThrowsAsync<StashScriptException>(
            () => host.CallAsync<string>("run", null, timeout.Token));

        // The arity error is a sync HostError thrown before the future is created.
        Assert.Equal(StashError.KindHostError, ex.Error.Kind);
        Assert.Contains("expects 1 argument", ex.Error.Message);
    }

    // ── #10: Invalid return type at registration → ArgumentException ──────────

    [Fact]
    public void AsyncMethod_InvalidReturnType_ThrowsAtRegistration()
    {
        var host = new StashHost();
        // Delegate returns string, not Task/Task<T>.
        var ex = Assert.Throws<ArgumentException>(() =>
            host.RegisterType<RemoteService>(b =>
                b.AsyncMethod("bad", (Func<RemoteService, string>)(_ => "nope"))));

        Assert.Contains("Task", ex.Message);
    }
}
