namespace Stash.Runtime;

using System;

/// <summary>
/// Controls how <c>VMContext.DrainCallbacks</c> waits for and delivers queued callbacks.
/// </summary>
/// <remarks>
/// Used by <c>time.sleep</c> (Until mode), <c>event.poll</c> (Poll mode), and
/// <c>event.loop</c> (Forever mode) to share a single drain chokepoint.
/// </remarks>
public abstract class WaitMode
{
    private WaitMode() { }

    /// <summary>
    /// Drain everything currently in the queue and return immediately.
    /// Used by <c>event.poll</c>.
    /// </summary>
    public sealed class PollMode : WaitMode { }

    /// <summary>
    /// Park on the queue signal until <paramref name="Deadline"/> (wall-clock) is reached,
    /// draining on each wake-up and recomputing remaining time.  Used by <c>time.sleep</c>.
    /// </summary>
    public sealed class UntilMode : WaitMode
    {
        public DateTimeOffset Deadline { get; }
        public UntilMode(DateTimeOffset deadline) => Deadline = deadline;
    }

    /// <summary>
    /// Park and drain indefinitely until cancellation.  Used by <c>event.loop</c>.
    /// </summary>
    public sealed class ForeverMode : WaitMode { }

    // ── Factory properties ──────────────────────────────────────────────────
    public static readonly PollMode Poll = new();
    public static readonly ForeverMode Forever = new();
    public static WaitMode Until(DateTimeOffset deadline) => new UntilMode(deadline);
}
