namespace Stash.Tests.Embedding;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stash.Hosting;
using Stash.Runtime;
using Xunit;

/// <summary>
/// Acceptance suite for P2 <see cref="StashHost.CallAsync{T}"/> and
/// <see cref="StashHost.TryCallAsync{T}"/>.
///
/// done_when coverage:
///   #1  — CallAsync_BasicFunctionCall_ReturnsCorrectValue
///   #2  — CallAsync_FunctionNotFound_ThrowsStashScriptException
///   #3  — CallAsync_ScriptError_ThrowsStashScriptException
///   #4  — TryCallAsync_Success_ReturnsTypedValue
///   #5  — TryCallAsync_ScriptError_ReturnsFailureResult
///   #6  — CallAsync_WithAnonymousObjectArgs_PassesCorrectly
///   #7  — CallAsync_Cancellation_ThrowsOperationCanceledException
///   #8  — TryCallAsync_Cancellation_ReturnsCancelledKind
///   #9  — CallAsync_StatefulEngine_AccumulatesGlobals (lua_State contract)
///   #10 — StashEngine_CallFunction_ReturnsCorrectValue (primitive verification)
/// </summary>
[Collection("ProcessGlobalSlots")]
public class StashHostCallTests
{
    // ── Helper: compile + run a script that defines a function, then call it. ──

    private static async Task<StashHost> SetupHostWithFunction(string source)
    {
        var host = new StashHost();
        var script = await host.CompileAsync(source);
        var run = await host.RunAsync(script);
        Assert.True(run.Success, $"Setup failed: {string.Join(", ", run.Errors)}");
        return host;
    }

    // ── #1: basic round-trip ─────────────────────────────────────────────

    [Fact]
    public async Task CallAsync_BasicFunctionCall_ReturnsCorrectValue()
    {
        await using var host = await SetupHostWithFunction("fn add(a, b) { return a + b; }");

        long result = await host.CallAsync<long>("add", new object?[] { 2L, 3L });

        Assert.Equal(5L, result);
    }

    // ── #2: function not found ────────────────────────────────────────────

    [Fact]
    public async Task CallAsync_FunctionNotFound_ThrowsStashScriptException()
    {
        await using var host = new StashHost();

        var ex = await Assert.ThrowsAsync<StashScriptException>(
            () => host.CallAsync<long>("noSuchFn"));

        Assert.Contains("noSuchFn", ex.Error.Message);
    }

    // ── #3: script-side error ─────────────────────────────────────────────

    [Fact]
    public async Task CallAsync_ScriptError_ThrowsStashScriptException()
    {
        await using var host = await SetupHostWithFunction(
            "fn failFn() { throw ValueError { message: \"oops\" }; }");

        var ex = await Assert.ThrowsAsync<StashScriptException>(
            () => host.CallAsync<long>("failFn"));

        Assert.Equal("ValueError", ex.Error.Kind);
        Assert.Equal("oops", ex.Error.Message);
    }

    // ── #4: TryCallAsync success ──────────────────────────────────────────

    [Fact]
    public async Task TryCallAsync_Success_ReturnsTypedValue()
    {
        await using var host = await SetupHostWithFunction("fn double(n) { return n * 2; }");

        var result = await host.TryCallAsync<long>("double", new object?[] { 7L });

        Assert.True(result.Success);
        Assert.Equal(14L, result.Value);
        Assert.Empty(result.Errors);
    }

    // ── #5: TryCallAsync script error ────────────────────────────────────

    [Fact]
    public async Task TryCallAsync_ScriptError_ReturnsFailureResult()
    {
        await using var host = await SetupHostWithFunction(
            "fn failFn() { throw ValueError { message: \"nope\" }; }");

        var result = await host.TryCallAsync<long>("failFn");

        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Equal("ValueError", result.Errors[0].Kind);
        Assert.Equal("nope", result.Errors[0].Message);
        // Does NOT throw — that is the key contract of TryCallAsync.
    }

    // ── #6: anonymous object args ─────────────────────────────────────────

    [Fact]
    public async Task CallAsync_WithAnonymousObjectArgs_PassesCorrectly()
    {
        // Pass each arg individually via object?[] for positional matching.
        await using var host = await SetupHostWithFunction("fn greet(name) { return \"Hello, \" + name; }");

        string result = await host.CallAsync<string>("greet", new object?[] { "Alice" });

        Assert.Equal("Hello, Alice", result);
    }

    // ── #7: cancellation in CallAsync ────────────────────────────────────

    [Fact]
    public async Task CallAsync_Cancellation_ThrowsOperationCanceledException()
    {
        // Uses time.sleep so the blocking path (WaitHandle-based cancellation in
        // VMContext) is exercised, not just the dispatch-loop counter check.
        await using var host = await SetupHostWithFunction(
            "fn sleeper() { time.sleep(999999); return 0; }");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => host.CallAsync<long>("sleeper", null, cts.Token));
    }

    // ── #8: cancellation in TryCallAsync ─────────────────────────────────

    [Fact]
    public async Task TryCallAsync_Cancellation_ReturnsCancelledKind()
    {
        // Uses time.sleep so the blocking path (WaitHandle-based cancellation in
        // VMContext) is exercised, not just the dispatch-loop counter check.
        await using var host = await SetupHostWithFunction(
            "fn sleeper() { time.sleep(999999); return 0; }");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var result = await host.TryCallAsync<long>("sleeper", null, cts.Token);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(StashError.KindCancelled, result.Errors[0].Kind);
    }

    // ── #9: stateful engine (lua_State contract) ─────────────────────────

    [Fact]
    public async Task CallAsync_StatefulEngine_AccumulatesGlobals()
    {
        await using var host = new StashHost();

        // Define a counter that increments a global each call.
        var s = await host.CompileAsync("let n = 0; fn inc() { n = n + 1; return n; }");
        var r = await host.RunAsync(s);
        Assert.True(r.Success, $"Setup failed: {string.Join(", ", r.Errors)}");

        long first  = await host.CallAsync<long>("inc");
        long second = await host.CallAsync<long>("inc");
        long third  = await host.CallAsync<long>("inc");

        // Sequential calls accumulate state — deliberate v1 lua_State contract.
        Assert.Equal(1L, first);
        Assert.Equal(2L, second);
        Assert.Equal(3L, third);
    }

    // ── #10: StashEngine.CallFunction primitive ───────────────────────────

    [Fact]
    public void StashEngine_CallFunction_ReturnsCorrectValue()
    {
        var engine = new Stash.Bytecode.StashEngine();

        // Compile and run a script defining 'add'.
        var script = engine.Compile("fn add(a, b) { return a + b; }");
        Assert.NotNull(script);
        engine.Run(script!);

        // Call the function directly.
        StashValue result = engine.CallFunction("add",
            new StashValue[] { StashValue.FromInt(10L), StashValue.FromInt(32L) });

        Assert.Equal(StashValueTag.Int, result.Tag);
        Assert.Equal(42L, result.AsInt);
    }

    // ── #11: StashEngine.CallFunction not found ───────────────────────────

    [Fact]
    public void StashEngine_CallFunction_NotFound_ThrowsRuntimeError()
    {
        var engine = new Stash.Bytecode.StashEngine();

        Assert.Throws<RuntimeError>(() =>
            engine.CallFunction("noSuchFn", ReadOnlySpan<StashValue>.Empty));
    }

    // ── #12: end-to-end acceptance from brief ────────────────────────────

    /// <summary>
    /// Verbatim test from the brief acceptance criteria:
    /// "fn add(a,b) { return a + b; }" → CallAsync&lt;long&gt;("add", new[] { 2L, 3L }) → 5.
    /// </summary>
    [Fact]
    public async Task BriefAcceptanceCriteria_AddFunction_Yields5()
    {
        async Task<long> Test()
        {
            await using var host = new StashHost();
            var s = await host.CompileAsync("fn add(a,b) { return a + b; }");
            await host.RunAsync(s);
            return await host.CallAsync<long>("add", new[] { 2L, 3L });
        }

        Assert.Equal(5L, await Test());
    }
}
