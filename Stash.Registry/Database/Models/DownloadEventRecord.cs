using System;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing a single raw download event.
/// </summary>
/// <remarks>
/// <para>
/// A row is inserted for every successfully-served tarball download (HTTP 200, full
/// stream written to completion). Rows in this table are subject to retention sweeps
/// (configured by <c>Metrics:Raw:RetentionDays</c>); permanent aggregates live in
/// <see cref="DownloadRollupHourlyRecord"/> and <see cref="DownloadRollupDailyRecord"/>.
/// </para>
/// <para>
/// This is a standalone audit-style table — no FK to <c>packages</c> or
/// <c>versions</c> — so that rollup data survives package deletion.
/// </para>
/// <para>
/// Column names use <c>snake_case</c>. The <c>status</c> column stores a named
/// status code (e.g. <c>"success"</c>); the closed set of status values is defined
/// in M3 when the write path is added.
/// </para>
/// </remarks>
public sealed class DownloadEventRecord
{
    /// <summary>Auto-generated surrogate key (maps to <c>id</c> BIGINT).</summary>
    public long Id { get; set; }

    /// <summary>The fully-qualified package name (e.g. <c>@scope/name</c>).</summary>
    public string PackageName { get; set; } = "";

    /// <summary>The exact semantic version string.</summary>
    public string Version { get; set; } = "";

    /// <summary>The UTC timestamp at which the download completed successfully.</summary>
    public DateTime Ts { get; set; }

    /// <summary>
    /// The IP address of the requesting client, transformed by the configured
    /// <see cref="Stash.Registry.Configuration.IpHandlingMode"/> (raw, truncated,
    /// hashed, or <c>null</c> for <c>off</c>).
    /// </summary>
    public string? Ip { get; set; }

    /// <summary>The <c>User-Agent</c> header value, or <c>null</c> if not present.</summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Named status code for the download outcome. The closed set of values is defined
    /// in <c>DownloadEventStatus</c> (landed in M3). This column accepts <c>null</c>
    /// until the write path is wired in M3.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>The number of bytes written to the response stream.</summary>
    public long BytesServed { get; set; }

    /// <summary>The authenticated username of the requester, or <c>null</c> for anonymous downloads.</summary>
    public string? RequesterUser { get; set; }
}
