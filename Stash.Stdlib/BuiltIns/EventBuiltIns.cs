namespace Stash.Stdlib.BuiltIns;

using Stash.Runtime;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>event</c> namespace built-in functions for the callback-marshaling event loop.
/// </summary>
/// <remarks>
/// <para>
/// Provides two yield-point functions that drain queued callbacks (registered via
/// <c>fs.watch</c>, <c>signal.on</c>, and future timer primitives) inline on the VM thread:
/// </para>
/// <list type="bullet">
///   <item><c>event.poll()</c> — drain everything currently queued, return immediately.</item>
///   <item><c>event.loop()</c> — block and drain until cancellation.</item>
/// </list>
/// <para>
/// Both functions are no-ops when called from inside a queued callback (reentrancy guard
/// lives inside <c>VMContext.DrainCallbacks</c> — run-to-completion task model).
/// </para>
/// </remarks>
[StashNamespace]
public static partial class EventBuiltIns
{
    /// <summary>
    /// Drains everything currently queued and returns immediately without blocking.
    /// Any callbacks registered before this call have their mutations visible to the
    /// caller by the time <c>event.poll()</c> returns.
    /// </summary>
    /// <remarks>
    /// When called from inside a queued callback (<c>_isDraining</c> is set),
    /// this is a no-op — the run-to-completion task model prevents re-entrant draining.
    /// </remarks>
    [StashFn]
    public static void Poll(IInterpreterContext ctx)
    {
        if (!ctx.SupportsCallbackDrain)
            throw new RuntimeError("'event.poll' requires a VM context with an event-loop pump; this host does not provide one.");
        ctx.DrainCallbacks(WaitMode.Poll);
    }

    /// <summary>
    /// Blocks and drains queued callbacks indefinitely until the script's
    /// <see cref="System.Threading.CancellationToken"/> is cancelled.
    /// Use this to keep a script alive while waiting for events (e.g.
    /// <c>fs.watch</c> or <c>signal.on</c> callbacks).
    /// </summary>
    /// <remarks>
    /// When called from inside a queued callback (<c>_isDraining</c> is set),
    /// this is a no-op — the run-to-completion task model prevents re-entrant draining.
    /// </remarks>
    /// <exception cref="CancellationError">when the script's cancellation token is triggered</exception>
    [StashFn]
    public static void Loop(IInterpreterContext ctx)
    {
        if (!ctx.SupportsCallbackDrain)
            throw new RuntimeError("'event.loop' requires a VM context with an event-loop pump; this host does not provide one.");
        ctx.DrainCallbacks(WaitMode.Forever);
    }
}
