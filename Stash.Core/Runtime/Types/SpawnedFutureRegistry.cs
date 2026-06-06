namespace Stash.Runtime.Types;

using System.Collections.Concurrent;
using System.Collections.Generic;

/// <summary>
/// Per-root-VM thread-safe registry of every user-visible <see cref="StashFuture"/>
/// spawned during a script run. Shared by reference across all child VMs so that
/// futures created on background threads register into the same set as the root.
///
/// <para>
/// At script exit the CLI driver scans this registry for futures that are
/// <c>IsFaulted &amp;&amp; !Observed &amp;&amp; !IsCancelled</c> and writes a warning
/// block to stderr (D1 — unobserved-task report). Hosts using EmbeddedMode skip the
/// report but can inspect the registry via the engine API if desired.
/// </para>
/// </summary>
public sealed class SpawnedFutureRegistry
{
    // ConcurrentDictionary<TKey, byte> gives us thread-safe add/enumerate without
    // locks. Byte value is unused (poor man's ConcurrentHashSet).
    private readonly ConcurrentDictionary<StashFuture, byte> _futures =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Registers a future. Called immediately after a user-visible StashFuture is
    /// created (before it is returned to the caller).  Thread-safe — called from
    /// background task threads.
    /// </summary>
    public void Register(StashFuture future) => _futures.TryAdd(future, 0);

    /// <summary>
    /// Returns the set of futures that are faulted, not observed, and not cancelled.
    /// Safe to call from any thread; skips still-running futures so exit never blocks.
    /// </summary>
    public IEnumerable<StashFuture> UnobservedFaults()
    {
        foreach (var pair in _futures)
        {
            StashFuture f = pair.Key;
            // Skip non-faulted (still running, completed, or cancelled)
            if (!f.IsFaulted) continue;
            // Skip cancelled tasks — the user purposely cancelled and didn't await
            if (f.IsCancelled) continue;
            // Skip observed — the outcome was consumed by a combinator
            if (f.Observed) continue;
            yield return f;
        }
    }

    /// <summary>
    /// Blocks (up to <paramref name="timeoutMs"/> total) until every registered future
    /// that is not still-running has completed. This is NOT called by the runtime —
    /// it exists only for use in tests that need deterministic timing without relying
    /// on fixed sleep durations. Long-running or in-flight futures are skipped (timeout
    /// guards against indefinite blocking).
    /// </summary>
    public void WaitForNonRunning(int timeoutMs = 2000)
    {
        var tasks = new System.Collections.Generic.List<System.Threading.Tasks.Task>();
        foreach (var pair in _futures)
        {
            var f = pair.Key;
            if (!f.IsCompleted) // still running — skip (don't block)
            {
                // Only wait if it's likely to complete soon (not a long sleep task).
                // We attempt to wait with a short per-task timeout.
                tasks.Add(f.DotNetTask);
            }
        }
        if (tasks.Count > 0)
        {
            try { System.Threading.Tasks.Task.WaitAll(tasks.ToArray(), timeoutMs); }
            catch { /* faulted tasks are fine; we just want them terminal */ }
        }
    }
}
