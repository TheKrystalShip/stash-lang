namespace Stash.Registry.Services.Metrics;

/// <summary>
/// Named status codes for a completed download event persisted in <c>download_events.status</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is a server-internal bounded domain — the closed set of values that may appear
/// in the <c>status</c> column of the <c>download_events</c> table.  Never inline a
/// raw string literal at a write site; always reference a member of this class.
/// </para>
/// <para>
/// Currently only <c>Success</c> is written — this is the only outcome for which a row
/// is ever inserted (404s, hidden-package rejections, and mid-stream disconnects are
/// excluded and produce no row at all).  Additional status codes are reserved for future
/// phases (e.g. <c>Incomplete</c> if retention-sweep differentiation is added).
/// </para>
/// </remarks>
public static class DownloadEventStatus
{
    /// <summary>
    /// The tarball stream was written to completion with HTTP <c>200 OK</c>.
    /// This is the only value written by <c>MetricsBackgroundService</c> in M3.
    /// </summary>
    public const string Success = "success";
}
