namespace Stash.Tests.Embedding;

using System;
using System.Threading.Tasks;
using Stash.Hosting;
using Stash.Runtime.Types;
using Stash.Stdlib.BuiltIns;
using Xunit;

/// <summary>
/// Collection definition that serializes <see cref="StashHostDisposalTests"/> and
/// <see cref="StashHostStatefulnessTests"/> to avoid races on process-global static
/// hook slots (PromptBuiltIns / ProcessBuiltIns / CompleteBuiltIns).
///
/// These tests set and clear those static delegate slots. Running them in parallel
/// with tests in "PromptTests", "ProcessHistoryHandlers", or "CompleteTests" could
/// cause non-deterministic failures.
/// </summary>
[CollectionDefinition("StashHostStaticSlots", DisableParallelization = true)]
public sealed class StashHostStaticSlotsCollection { }

/// <summary>
/// Acceptance suite for P3 disposal behaviour of <see cref="StashHost"/>.
///
/// done_when coverage:
///   #1 — DisposeAsync_NullsProcessGlobalHookSlots
///   #2 — DisposeAsync_IsIdempotent
///   #3 — DisposeAsync_WithNullEngine_DoesNotThrow
///   #4 — DisposeAsync_ResetsPromptBootstrapHandler
/// </summary>
[Collection("StashHostStaticSlots")]
public class StashHostDisposalTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a host with a compiled + run script so it's fully initialised.
    /// </summary>
    private static async Task<StashHost> CreateInitialisedHostAsync()
    {
        var host = new StashHost();
        var s = await host.CompileAsync("let _x = 1;");
        await host.RunAsync(s);
        return host;
    }

    // ── #1: hook slots are null after disposal ────────────────────────────────

    /// <summary>
    /// Setting ProcessBuiltIns.HistoryAddHandler before constructing a host,
    /// then disposing the host, must null that slot (and the other history slots
    /// and the prompt/complete hooks).
    /// </summary>
    [Fact]
    public async Task DisposeAsync_NullsProcessGlobalHookSlots()
    {
        // Arrange: wire synthetic handlers so we can observe the nulling.
        ProcessBuiltIns.HistoryAddHandler = _ => { };
        ProcessBuiltIns.HistoryClearHandler = () => { };
        ProcessBuiltIns.HistoryListProvider = () => Array.Empty<string>();
        PromptBuiltIns.ResetBootstrapHandler = () => { };
        CompleteBuiltIns.RegisterHandler = (_, _) => { };

        StashHost host = await CreateInitialisedHostAsync();

        // Act
        await host.DisposeAsync();

        // Assert: all slots must be null after disposal.
        Assert.Null(ProcessBuiltIns.HistoryAddHandler);
        Assert.Null(ProcessBuiltIns.HistoryClearHandler);
        Assert.Null(ProcessBuiltIns.HistoryListProvider);
        Assert.Null(PromptBuiltIns.ResetBootstrapHandler);
        // CompleteBuiltIns.ResetAllForTesting() nulls all handler properties.
        Assert.Null(CompleteBuiltIns.RegisterHandler);
        Assert.Null(CompleteBuiltIns.SuggestHandler);
        Assert.Null(CompleteBuiltIns.UnregisterHandler);
        Assert.Null(CompleteBuiltIns.RegisteredHandler);
        Assert.Null(CompleteBuiltIns.PathHelperHandler);
        // PromptBuiltIns prompt/continuation fns are not public fields — verify via
        // the observable accessor (GetRegisteredPromptFn returns the internal _promptFn).
        Assert.Null(PromptBuiltIns.GetRegisteredPromptFn());
        Assert.Null(PromptBuiltIns.GetRegisteredContinuationFn());
    }

    // ── #2: double-dispose is safe ────────────────────────────────────────────

    /// <summary>
    /// Calling DisposeAsync twice on the same host must not throw.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var host = new StashHost();
        await host.DisposeAsync();

        // Second dispose must be a no-op with no exception.
        Exception? ex = await Record.ExceptionAsync(() => host.DisposeAsync().AsTask());
        Assert.Null(ex);
    }

    // ── #3: dispose from a finalization-like path (no engine) ────────────────

    /// <summary>
    /// DisposeAsync must not throw when invoked on a newly constructed host whose
    /// internal engine reference is null (simulates a finalization-like disposal path).
    /// </summary>
    [Fact]
    public async Task DisposeAsync_WithNullEngine_DoesNotThrow()
    {
        // A freshly-created host (no RunAsync / CallAsync ever called) should be
        // safely disposable — the engine may or may not be null internally, but the
        // disposal code must be null-safe regardless.
        var host = new StashHost();
        Exception? ex = await Record.ExceptionAsync(() => host.DisposeAsync().AsTask());
        Assert.Null(ex);
    }

    // ── #4: ResetBootstrapHandler is specifically observed ────────────────────

    /// <summary>
    /// PromptBuiltIns.ResetBootstrapHandler (a public property, not a method) is set
    /// before disposal and must be null after. This validates the "= null" assignment
    /// path in DisposeAsync.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_ResetsPromptBootstrapHandler()
    {
        PromptBuiltIns.ResetBootstrapHandler = () => { /* synthetic bootstrap reset */ };
        Assert.NotNull(PromptBuiltIns.ResetBootstrapHandler);

        var host = new StashHost();
        await host.DisposeAsync();

        Assert.Null(PromptBuiltIns.ResetBootstrapHandler);
    }

    // ── #5: dispose resets visible state — only new host sees clean state ─────

    /// <summary>
    /// Stateful engine contract: after disposing host1 and creating host2, host2 does
    /// not see host1's globals (proves the "dispose-and-new" semantics work).
    /// </summary>
    [Fact]
    public async Task DisposeAndNewHost_NewHostDoesNotSeeOldGlobals()
    {
        // Define and call a function in host1.
        StashHost host1 = new StashHost();
        var s1 = await host1.CompileAsync("fn secretFn() { return 999; }");
        await host1.RunAsync(s1);
        long val = await host1.CallAsync<long>("secretFn");
        Assert.Equal(999L, val);
        await host1.DisposeAsync();

        // host2 must not have secretFn. Calling it should fail with a RuntimeError.
        await using var host2 = new StashHost();
        var result = await host2.TryCallAsync<long>("secretFn");
        Assert.False(result.Success, "host2 must not see host1's 'secretFn' — new host has fresh state.");
    }
}
