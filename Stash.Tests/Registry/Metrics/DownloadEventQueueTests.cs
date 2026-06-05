using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stash.Registry.Services.Metrics;
using Xunit;

namespace Stash.Tests.Registry.Metrics;

/// <summary>
/// Unit tests for <see cref="DownloadEventQueue"/> — the bounded
/// <see cref="System.Threading.Channels.Channel{T}"/>-backed queue.
/// </summary>
public sealed class DownloadEventQueueTests
{
    private static DownloadEvent MakeEvent(string pkg = "@a/foo", string ver = "1.0.0") => new()
    {
        PackageName = pkg,
        Version = ver,
        Ts = DateTime.UtcNow,
        Ip = "127.0.0.1",
        BytesServed = 1024,
    };

    // ── Registration as singleton ──────────────────────────────────────────────

    /// <summary>
    /// <see cref="DownloadEventQueue"/> implements <see cref="IDownloadEventQueue"/>.
    /// </summary>
    [Fact]
    public void DownloadEventQueue_Implements_IDownloadEventQueue()
    {
        IDownloadEventQueue queue = new DownloadEventQueue();
        Assert.NotNull(queue);
        Assert.NotNull(queue.Reader);
    }

    // ── Enqueue / drain ────────────────────────────────────────────────────────

    /// <summary>
    /// A single enqueued event is readable from <see cref="IDownloadEventQueue.Reader"/>.
    /// </summary>
    [Fact]
    public void Enqueue_SingleEvent_ReaderCanRead()
    {
        var queue = new DownloadEventQueue();
        var ev = MakeEvent();

        queue.Enqueue(ev);

        bool read = queue.Reader.TryRead(out var result);
        Assert.True(read, "Expected TryRead to return true after Enqueue.");
        Assert.Equal(ev.PackageName, result!.PackageName);
        Assert.Equal(ev.Version, result.Version);
    }

    /// <summary>
    /// Multiple enqueued events are readable in FIFO order.
    /// </summary>
    [Fact]
    public void Enqueue_MultipleEvents_DrainedInFifoOrder()
    {
        var queue = new DownloadEventQueue();
        var events = new[]
        {
            MakeEvent("@a/foo", "1.0.0"),
            MakeEvent("@a/foo", "2.0.0"),
            MakeEvent("@b/bar", "1.0.0"),
        };

        foreach (var ev in events)
            queue.Enqueue(ev);

        var drained = new List<DownloadEvent>();
        while (queue.Reader.TryRead(out var ev))
            drained.Add(ev);

        Assert.Equal(events.Length, drained.Count);
        for (int i = 0; i < events.Length; i++)
            Assert.Equal(events[i].Version, drained[i].Version);
    }

    // ── Non-blocking overflow ──────────────────────────────────────────────────

    /// <summary>
    /// When the channel is at capacity, <see cref="DownloadEventQueue.Enqueue"/> drops
    /// the event silently without blocking or throwing.
    /// </summary>
    [Fact]
    public void Enqueue_WhenFull_DropsEventSilently()
    {
        const int capacity = 4;
        var queue = new DownloadEventQueue(capacity);

        // Fill to capacity
        for (int i = 0; i < capacity; i++)
            queue.Enqueue(MakeEvent("@a/foo", $"1.0.{i}"));

        // One more — should be silently dropped
        var overflow = MakeEvent("@a/foo", "99.0.0");
        queue.Enqueue(overflow); // must not throw or block

        // Drain — only capacity events should be present
        int count = 0;
        while (queue.Reader.TryRead(out _))
            count++;

        Assert.Equal(capacity, count);
    }

    // ── Empty queue ────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="System.Threading.Channels.ChannelReader{T}.TryRead"/> returns
    /// <c>false</c> on an empty queue.
    /// </summary>
    [Fact]
    public void Reader_EmptyQueue_TryReadReturnsFalse()
    {
        var queue = new DownloadEventQueue();

        bool read = queue.Reader.TryRead(out _);

        Assert.False(read, "Expected TryRead to return false on an empty queue.");
    }

    // ── Default capacity is DefaultCapacity ───────────────────────────────────

    /// <summary>
    /// The default capacity is <see cref="DownloadEventQueue.DefaultCapacity"/>.
    /// A queue created with no arguments can hold at least that many events.
    /// </summary>
    [Fact]
    public void DefaultCapacity_IsHonoured()
    {
        var queue = new DownloadEventQueue();
        for (int i = 0; i < DownloadEventQueue.DefaultCapacity; i++)
            queue.Enqueue(MakeEvent());

        // All events must be in the channel (none dropped yet).
        int count = 0;
        while (queue.Reader.TryRead(out _))
            count++;

        Assert.Equal(DownloadEventQueue.DefaultCapacity, count);
    }
}
