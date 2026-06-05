using System;

namespace Stash.Registry.Services.Metrics;

/// <summary>
/// An in-process transport record representing a single successfully-completed download.
/// Enqueued by <see cref="IDownloadEventQueue"/> and drained by
/// <see cref="MetricsBackgroundService"/> into the <c>download_events</c> table.
/// </summary>
/// <remarks>
/// <para>
/// All fields are captured on the request thread at completion time; the background
/// service receives plain data values with no live <c>HttpContext</c> references.
/// The <c>Ip</c> field carries the already-transformed IP string (the result of
/// <c>IIpHasher.Apply(HttpContext.Connection.RemoteIpAddress)</c>) — raw IP addresses
/// are never placed in this record.
/// </para>
/// </remarks>
public sealed class DownloadEvent
{
    /// <summary>The fully-qualified package name (e.g. <c>@scope/name</c>).</summary>
    public string PackageName { get; init; } = "";

    /// <summary>The exact semantic version string (e.g. <c>1.2.3</c>).</summary>
    public string Version { get; init; } = "";

    /// <summary>
    /// The UTC timestamp at which the download stream completed successfully.
    /// </summary>
    public DateTime Ts { get; init; }

    /// <summary>
    /// The transformed IP address string — output of
    /// <c>IIpHasher.Apply(HttpContext.Connection.RemoteIpAddress)</c>.
    /// <c>null</c> when mode is <c>off</c> or the remote address was unavailable.
    /// </summary>
    public string? Ip { get; init; }

    /// <summary>The <c>User-Agent</c> header value, or <c>null</c> if absent.</summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// The number of bytes written to the response stream.
    /// Populated from <c>VersionRecord.StorageBytes</c> (written at publish time).
    /// </summary>
    public long BytesServed { get; init; }

    /// <summary>
    /// The authenticated username of the requester, or <c>null</c> for anonymous downloads.
    /// </summary>
    public string? RequesterUser { get; init; }
}
