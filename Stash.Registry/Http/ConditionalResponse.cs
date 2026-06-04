using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Stash.Registry.Http;

/// <summary>
/// Shared helper for emitting ETag / Last-Modified / Cache-Control headers and
/// evaluating conditional-request preconditions on read endpoints.
/// </summary>
/// <remarks>
/// <para>
/// Used by <c>GET …/versions</c> (P3) and <c>GET …/readme</c> (P4) to apply a
/// uniform caching policy without duplicating the header-writing or comparison logic.
/// </para>
/// <para>
/// The weak ETag formula is <c>W/"&lt;UpdatedAt-ticks&gt;-&lt;etagSuffix&gt;"</c>.
/// For <c>/versions</c> the suffix is the total version count; for <c>/readme</c>
/// it is the byte length of the raw README content.
/// </para>
/// <para>
/// Conditional evaluation follows RFC 7232 §4.1: a 304 is returned when
/// <c>If-None-Match</c> matches (weak comparison) OR
/// <c>If-Modified-Since</c> is greater-than-or-equal to <c>Last-Modified</c>.
/// The response headers are written in all cases (200 and 304) so that clients
/// always receive up-to-date validators.
/// </para>
/// </remarks>
public static class ConditionalResponse
{
    /// <summary>
    /// Sets <c>ETag</c>, <c>Last-Modified</c>, and <c>Cache-Control</c> response headers,
    /// then returns <c>true</c> when the conditional preconditions are satisfied
    /// (indicating the caller should return <c>304 Not Modified</c>).
    /// </summary>
    /// <param name="context">The current <see cref="HttpContext"/>.</param>
    /// <param name="updatedAt">
    /// The resource's last-modified timestamp (UTC).  Sub-second precision is truncated
    /// to whole seconds for <c>Last-Modified</c>, matching HTTP date resolution.
    /// </param>
    /// <param name="etagSuffix">
    /// The second component of the weak ETag (e.g. total version count or README byte length).
    /// </param>
    /// <returns>
    /// <c>true</c> when the response should be <c>304 Not Modified</c> (no body);
    /// <c>false</c> when the full resource body should be sent.
    /// </returns>
    public static bool SetHeadersAndCheckNotModified(
        HttpContext context,
        DateTime updatedAt,
        int etagSuffix)
    {
        // Ensure the timestamp is treated as UTC (SQLite round-trips can lose DateTimeKind).
        var updatedAtUtc = DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc);

        // Truncate to whole seconds for HTTP date resolution.
        var lastModified = new DateTimeOffset(
            new DateTime(
                updatedAtUtc.Year, updatedAtUtc.Month, updatedAtUtc.Day,
                updatedAtUtc.Hour, updatedAtUtc.Minute, updatedAtUtc.Second,
                DateTimeKind.Utc));

        // Weak ETag: W/"<ticks>-<suffix>"
        string rawEtag = $"\"{updatedAtUtc.Ticks}-{etagSuffix}\"";
        var etag = new EntityTagHeaderValue(rawEtag, isWeak: true);

        // Write response headers.
        var responseHeaders = context.Response.GetTypedHeaders();
        responseHeaders.ETag = etag;
        responseHeaders.LastModified = lastModified;
        context.Response.Headers.CacheControl = "public, max-age=60";

        // Evaluate If-None-Match (weak ETag comparison).
        var requestHeaders = context.Request.GetTypedHeaders();
        if (requestHeaders.IfNoneMatch is { Count: > 0 } ifNoneMatch)
        {
            foreach (var candidate in ifNoneMatch)
            {
                if (candidate.Compare(etag, useStrongComparison: false))
                    return true;
            }
        }

        // Evaluate If-Modified-Since (second-precision comparison).
        if (requestHeaders.IfModifiedSince is { } ims)
        {
            if (lastModified <= ims)
                return true;
        }

        return false;
    }
}
