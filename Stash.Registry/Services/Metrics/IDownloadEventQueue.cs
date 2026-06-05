using System.Threading;
using System.Threading.Channels;

namespace Stash.Registry.Services.Metrics;

/// <summary>
/// A bounded, non-blocking in-process queue of <see cref="DownloadEvent"/> items
/// used to decouple the download hot-path from the metrics-persistence layer.
/// </summary>
/// <remarks>
/// <para>
/// Implemented as a bounded <see cref="Channel{T}"/> singleton registered in DI.
/// <see cref="Enqueue"/> is synchronous and non-blocking: if the channel is full the
/// event is silently dropped (best-effort, never stalls a download response).
/// </para>
/// <para>
/// The background drainer (<see cref="MetricsBackgroundService"/>) reads from
/// <see cref="Reader"/> in batches.
/// </para>
/// </remarks>
public interface IDownloadEventQueue
{
    /// <summary>
    /// The read side of the channel.  Used only by <see cref="MetricsBackgroundService"/>
    /// to drain events in batches.
    /// </summary>
    ChannelReader<DownloadEvent> Reader { get; }

    /// <summary>
    /// Enqueues <paramref name="ev"/> on the channel without blocking.
    /// If the channel is at capacity the event is dropped silently (best-effort).
    /// Must be called only at successful response completion (HTTP 200, full stream).
    /// </summary>
    /// <param name="ev">The completed-download event to enqueue.</param>
    void Enqueue(DownloadEvent ev);
}
