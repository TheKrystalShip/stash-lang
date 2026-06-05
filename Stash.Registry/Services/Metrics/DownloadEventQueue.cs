using System.Threading;
using System.Threading.Channels;

namespace Stash.Registry.Services.Metrics;

/// <summary>
/// Default implementation of <see cref="IDownloadEventQueue"/> backed by a bounded
/// <see cref="Channel{T}"/> with drop-on-full semantics.
/// </summary>
/// <remarks>
/// <para>
/// The channel capacity is set to <c>4096</c> events — enough to buffer bursts without
/// growing unboundedly.  When the channel is full, <see cref="Enqueue"/> silently drops
/// the event (best-effort, never stalls a request thread).
/// </para>
/// <para>
/// Registered in DI as a <b>singleton</b> so the same channel instance is shared
/// across all HTTP requests and the background drainer.
/// </para>
/// </remarks>
public sealed class DownloadEventQueue : IDownloadEventQueue
{
    /// <summary>Default channel capacity (number of events buffered before dropping).</summary>
    public const int DefaultCapacity = 4096;

    private readonly Channel<DownloadEvent> _channel;

    /// <summary>
    /// Initialises a new <see cref="DownloadEventQueue"/> with the specified capacity.
    /// </summary>
    /// <param name="capacity">
    /// The maximum number of events the channel can buffer before drops occur.
    /// Defaults to <see cref="DefaultCapacity"/> when called from DI.
    /// </param>
    public DownloadEventQueue(int capacity = DefaultCapacity)
    {
        _channel = Channel.CreateBounded<DownloadEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleWriter = false,
            SingleReader = true,
        });
    }

    /// <inheritdoc/>
    public ChannelReader<DownloadEvent> Reader => _channel.Reader;

    /// <inheritdoc/>
    public void Enqueue(DownloadEvent ev)
    {
        // TryWrite is synchronous and non-blocking. If the channel is full, the event
        // is silently dropped — a minor metrics under-count is preferable to slowing
        // or blocking the download response.
        _channel.Writer.TryWrite(ev);
    }
}
