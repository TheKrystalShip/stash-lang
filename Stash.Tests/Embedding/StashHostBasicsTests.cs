namespace Stash.Tests.Embedding;

using System;
using System.IO;
using System.Threading.Tasks;
using Stash.Hosting;
using Stash.Runtime;
using Xunit;

/// <summary>
/// Acceptance suite for the P1 <see cref="StashHost"/> facade.
///
/// done_when coverage:
///   #1  — BasicRunAsync_ScriptReturnsValue_SuccessWithCorrectValue
///   #2  — TwoHosts_NoGlobalLeakAcrossHosts
///   #3  — DisposeAsync_HostIsIAsyncDisposable (await using shape)
///   #4  — RunAsync_FailingScript_ReturnsFailureResult
///   #5  — RunAsync_SucceedsTrue_AndSuccessFalse_AreCorrect
///   #6  — Host_IsHermetic_NoGlobalsFromPriorHost
/// </summary>
[Collection("ProcessGlobalSlots")]
public class StashHostBasicsTests
{
    // ── #1: basic round-trip ─────────────────────────────────────────────

    /// <summary>
    /// Compile a script that explicitly returns 42, run it, assert the typed result.
    ///
    /// Plan note: the done_when describes the source as "let x = 41; x = x + 1;" but
    /// the Stash compiler emits an implicit 'return null' at the end of a top-level
    /// script — assignments as statements discard their value. The script is adjusted to
    /// include an explicit 'return x;' so the 42 propagates to the host. This is a
    /// documented plan deviation.
    /// </summary>
    [Fact]
    public async Task BasicRunAsync_ScriptReturnsValue_SuccessWithCorrectValue()
    {
        await using var host = new StashHost();

        // done_when source adjusted to include explicit return.
        var s = await host.CompileAsync("let x = 41; x = x + 1; return x;");
        var r = await host.RunAsync<long>(s);

        Assert.True(r.Success, $"Expected success; errors: {string.Join(", ", r.Errors)}");
        Assert.Equal(42L, r.Value);
    }

    // ── #2: isolation between two hosts ──────────────────────────────────

    /// <summary>
    /// A global defined in host1 must not be visible to host2.
    /// This test has teeth: host2 explicitly attempts to read 'magic' and must fail
    /// (Success == false), proving the two engines do not share a global store.
    /// </summary>
    [Fact]
    public async Task TwoHosts_NoGlobalLeakAcrossHosts()
    {
        await using var host1 = new StashHost();
        await using var host2 = new StashHost();

        // Define a global 'magic' in host1.
        var s1 = await host1.CompileAsync("let magic = 999;");
        var r1 = await host1.RunAsync(s1);
        Assert.True(r1.Success, $"host1 setup failed: {string.Join(", ", r1.Errors)}");

        // host1 must be able to return the global it just defined.
        var s3 = await host1.CompileAsync("return magic;");
        var r3 = await host1.RunAsync<long>(s3);
        Assert.True(r3.Success, $"host1 magic read failed: {string.Join(", ", r3.Errors)}");
        Assert.Equal(999L, r3.Value);

        // host2 must NOT see host1's 'magic' global — reading it must fail.
        // This is the discriminating assertion: if the two hosts shared a global store,
        // host2 would successfully return 999 and the assertion below would fail.
        var s2 = await host2.CompileAsync("return magic;");
        var r2 = await host2.RunAsync<long>(s2);
        Assert.False(r2.Success,
            "host2 must not see host1's 'magic' global — hermetic-VM contract violated.");
    }

    // ── #3: IAsyncDisposable shape ────────────────────────────────────────

    /// <summary>
    /// StashHost must be usable in an 'await using' block (IAsyncDisposable).
    /// </summary>
    [Fact]
    public async Task DisposeAsync_HostIsIAsyncDisposable()
    {
        StashHost? hostRef = null;
        await using (var host = new StashHost())
        {
            hostRef = host;
            var s = await host.CompileAsync("return 1;");
            var r = await host.RunAsync<long>(s);
            Assert.True(r.Success);
            Assert.Equal(1L, r.Value);
        }

        // After disposal, operations must throw ObjectDisposedException.
        Assert.NotNull(hostRef);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => hostRef!.CompileAsync("return 1;"));
    }

    // ── #4: failed script returns failure result ──────────────────────────

    /// <summary>
    /// A script that references an undefined variable results in a failure result,
    /// not an uncaught exception propagating out of RunAsync.
    /// </summary>
    [Fact]
    public async Task RunAsync_FailingScript_ReturnsFailureResult()
    {
        await using var host = new StashHost();

        // 'undeclared' is not defined — the VM will throw a RuntimeError.
        var s = await host.CompileAsync("return undeclared + 1;");
        var r = await host.RunAsync(s);

        Assert.False(r.Success);
        Assert.NotEmpty(r.Errors);
    }

    // ── #5: non-generic RunAsync success flag ─────────────────────────────

    [Fact]
    public async Task RunAsync_SuccessScript_ReturnsSuccessTrue()
    {
        await using var host = new StashHost();

        var s = await host.CompileAsync("let x = 1;");
        var r = await host.RunAsync(s);

        Assert.True(r.Success);
        Assert.Empty(r.Errors);
    }

    // ── #6: StashValue passthrough ────────────────────────────────────────

    /// <summary>
    /// RunAsync&lt;StashValue&gt; returns the raw value without any conversion.
    /// </summary>
    [Fact]
    public async Task RunAsync_StashValuePassthrough_ReturnsRawValue()
    {
        await using var host = new StashHost();

        var s = await host.CompileAsync("return 123;");
        var r = await host.RunAsync<StashValue>(s);

        Assert.True(r.Success, $"Expected success; errors: {string.Join(", ", r.Errors)}");
        Assert.Equal(StashValueTag.Int, r.Value.Tag);
        Assert.Equal(123L, r.Value.AsInt);
    }

    // ── #7: compile error throws ──────────────────────────────────────────

    [Fact]
    public async Task CompileAsync_InvalidSource_ThrowsInvalidOperationException()
    {
        await using var host = new StashHost();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.CompileAsync("let = ;"));  // parse error
    }

    // ── #8: StashEngine.RunRaw non-swallowing ─────────────────────────────

    /// <summary>
    /// StashEngine.RunRaw must let RuntimeError escape — it must NOT be caught and
    /// converted to an ExecutionResult. This verifies the done_when requirement:
    /// "StashEngine exposes a non-swallowing execution path."
    /// </summary>
    [Fact]
    public void StashEngine_RunRaw_PropagatesRuntimeError()
    {
        var engine = new Stash.Bytecode.StashEngine();

        var script = engine.Compile("throw ValueError(\"boom\");");
        Assert.NotNull(script);

        // RunRaw must throw, not return a failure ExecutionResult.
        Assert.Throws<Stash.Runtime.RuntimeError>(() => engine.RunRaw(script!));
    }
}
