using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Stash.Registry.Configuration;

namespace Stash.Registry.Middleware;

/// <summary>
/// ASP.NET Core middleware that enforces per-category rate limits on incoming requests.
/// </summary>
/// <remarks>
/// <para>
/// Categories: <c>auth</c>, <c>publish</c>, <c>download</c>, <c>search</c> — each with
/// configurable sliding-window buckets keyed by client IP address (or by username for
/// <c>publish</c> requests). Configuration is supplied via <see cref="RateLimitingConfig"/>.
/// Rate limiting can be disabled entirely by setting <see cref="RateLimitingConfig.Enabled"/>
/// to <see langword="false"/>.
/// </para>
/// <para>
/// <b>Sliding-window algorithm:</b> Each category+identifier pair maps to a
/// <see cref="RateLimitBucket"/> that records the window start time and the request count
/// within that window. On each request the elapsed time since the window start is compared
/// to the configured window duration. If the elapsed time exceeds the window, the bucket
/// is reset (counter back to zero, window start updated to now). The counter is then
/// incremented; if it exceeds the configured maximum the request is rejected with HTTP 429
/// and a <c>Retry-After</c> header indicating the seconds remaining in the current window.
/// Stale buckets (inactive for more than 30 minutes) are purged every 1 000 requests to
/// bound memory growth.
/// </para>
/// </remarks>
public sealed class RateLimitingMiddleware
{
    /// <summary>
    /// The next middleware in the ASP.NET Core pipeline.
    /// </summary>
    private readonly RequestDelegate _next;

    /// <summary>
    /// Rate-limiting configuration containing per-category limits and the global
    /// <see cref="RateLimitingConfig.Enabled"/> flag.
    /// </summary>
    private readonly RateLimitingConfig _config;

    /// <summary>
    /// Thread-safe dictionary mapping <c>{category}:{identifier}</c> keys to their
    /// corresponding <see cref="RateLimitBucket"/> sliding-window state.
    /// </summary>
    private readonly ConcurrentDictionary<string, RateLimitBucket> _buckets = new();

    /// <summary>
    /// A counter incremented on every request used to trigger periodic stale-bucket
    /// cleanup every 1 000 requests.
    /// </summary>
    private int _cleanupCounter;

    /// <summary>
    /// Initialises a new instance of <see cref="RateLimitingMiddleware"/> with the
    /// next pipeline delegate and rate-limiting configuration.
    /// </summary>
    /// <param name="next">The next middleware component in the ASP.NET Core pipeline.</param>
    /// <param name="config">The rate-limiting configuration.</param>
    public RateLimitingMiddleware(RequestDelegate next, RateLimitingConfig config)
    {
        _next = next;
        _config = config;
    }

    /// <summary>
    /// Inspects the incoming request, categorises it, checks the sliding-window bucket,
    /// and either forwards the request or short-circuits with HTTP 429.
    /// </summary>
    /// <remarks>
    /// Requests that do not match any known category (see <see cref="CategorizeRequest"/>)
    /// are passed through without rate-limiting. When rate-limited, the response body
    /// contains a JSON error object and the <c>Retry-After</c> header is set to the
    /// number of seconds until the current window expires.
    /// </remarks>
    /// <param name="context">The <see cref="HttpContext"/> for the current request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_config.Enabled)
        {
            await _next(context);
            return;
        }

        string path = context.Request.Path.Value ?? "";
        string method = context.Request.Method;
        string? category = CategorizeRequest(method, path);

        if (category == null)
        {
            await _next(context);
            return;
        }

        string identifier = GetIdentifier(context, category);
        string key = $"{category}:{identifier}";

        var (maxRequests, windowSeconds) = GetLimits(category);
        if (maxRequests <= 0)
        {
            await _next(context);
            return;
        }

        var now = DateTime.UtcNow;
        var bucket = _buckets.GetOrAdd(key, _ => new RateLimitBucket(now));

        bool rateLimited = false;
        int retryAfter = 0;

        lock (bucket)
        {
            double elapsed = (now - bucket.WindowStart).TotalSeconds;
            if (elapsed >= windowSeconds)
            {
                bucket.WindowStart = now;
                bucket.Count = 0;
            }

            bucket.Count++;

            if (bucket.Count > maxRequests)
            {
                retryAfter = (int)Math.Ceiling(windowSeconds - elapsed);
                if (retryAfter <= 0)
                {
                    retryAfter = 1;
                }

                rateLimited = true;
            }
        }

        if (rateLimited)
        {
            context.Response.StatusCode = 429;
            context.Response.Headers.RetryAfter = retryAfter.ToString();
            context.Response.ContentType = "application/json";
            byte[] body = System.Text.Encoding.UTF8.GetBytes(
                $"{{\"error\":\"Rate limit exceeded. Try again in {retryAfter} seconds.\"}}");
            await context.Response.Body.WriteAsync(body, 0, body.Length);
            return;
        }

        // Periodically clean up stale buckets
        if (System.Threading.Interlocked.Increment(ref _cleanupCounter) % 1000 == 0)
        {
            CleanupStaleBuckets(now);
        }

        await _next(context);
    }

    /// <summary>
    /// Maps an HTTP method and request path to a rate-limit category string.
    /// </summary>
    /// <remarks>
    /// Recognised patterns:
    /// <list type="bullet">
    ///   <item><description><c>POST *.../auth/login</c> → <c>"auth"</c></description></item>
    ///   <item><description><c>PUT *.../packages/...</c> → <c>"publish"</c></description></item>
    ///   <item><description><c>GET *.../packages/.../download</c> → <c>"download"</c></description></item>
    ///   <item><description><c>GET *.../search...</c> → <c>"search"</c></description></item>
    /// </list>
    /// Returns <see langword="null"/> for paths that do not match any category.
    /// </remarks>
    /// <param name="method">The HTTP method (e.g. <c>GET</c>, <c>POST</c>).</param>
    /// <param name="path">The request path value.</param>
    /// <returns>The category string, or <see langword="null"/> if the request is uncategorised.</returns>
    private string? CategorizeRequest(string method, string path)
    {
        if (method == "POST" && path.EndsWith("/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            return "auth";
        }

        if (method == "PUT" && path.Contains("/packages/", StringComparison.OrdinalIgnoreCase))
        {
            return "publish";
        }

        if (method == "GET" && path.Contains("/download", StringComparison.OrdinalIgnoreCase) && path.Contains("/packages/", StringComparison.OrdinalIgnoreCase))
        {
            return "download";
        }

        if (method == "GET" && path.Contains("/search", StringComparison.OrdinalIgnoreCase))
        {
            return "search";
        }

        return null;
    }

    /// <summary>
    /// Determines the rate-limit identifier for a request within a given category.
    /// </summary>
    /// <remarks>
    /// For <c>publish</c> requests the identifier is the authenticated username stored in
    /// <see cref="HttpContext.Items"/> under the key <c>"Username"</c>, so that publish
    /// limits are enforced per-user rather than per-IP. All other categories use the
    /// client's remote IP address. Falls back to <c>"unknown"</c> if the IP is unavailable.
    /// </remarks>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="category">The request category as returned by <see cref="CategorizeRequest"/>.</param>
    /// <returns>A string that uniquely identifies the client for rate-limiting purposes.</returns>
    private string GetIdentifier(HttpContext context, string category)
    {
        if (category == "publish")
        {
            if (context.Items.TryGetValue("Username", out object? username) && username is string u)
            {
                return u;
            }
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Returns the maximum request count and window duration (in seconds) for a given category.
    /// </summary>
    /// <remarks>
    /// Limits are sourced from <see cref="RateLimitingConfig"/>:
    /// <list type="bullet">
    ///   <item><description><c>auth</c> — <see cref="RateLimitingConfig.Auth"/> (attempts per window)</description></item>
    ///   <item><description><c>publish</c> — <see cref="RateLimitingConfig.Publish"/> (max per hour, 3 600 s window)</description></item>
    ///   <item><description><c>download</c> — <see cref="RateLimitingConfig.Download"/> (max per minute, 60 s window)</description></item>
    ///   <item><description><c>search</c> — <see cref="RateLimitingConfig.Search"/> (max per minute, 60 s window)</description></item>
    /// </list>
    /// Returns <c>(0, 0)</c> for unknown categories, which disables rate-limiting for that request.
    /// </remarks>
    /// <param name="category">The request category.</param>
    /// <returns>
    /// A tuple of <c>(maxRequests, windowSeconds)</c>. A <c>maxRequests</c> value of
    /// zero or less means no limit is applied.
    /// </returns>
    private (int maxRequests, int windowSeconds) GetLimits(string category)
    {
        return category switch
        {
            "auth" => (_config.Auth.MaxAttempts, _config.Auth.WindowSeconds),
            "publish" => (_config.Publish.MaxPerHour, 3600),
            "download" => (_config.Download.MaxPerMinute, 60),
            "search" => (_config.Search.MaxPerMinute, 60),
            _ => (0, 0)
        };
    }

    /// <summary>
    /// Removes stale <see cref="RateLimitBucket"/> entries from <see cref="_buckets"/> to
    /// prevent unbounded memory growth.
    /// </summary>
    /// <remarks>
    /// A bucket is considered stale when its <see cref="RateLimitBucket.WindowStart"/> is
    /// more than 30 minutes in the past relative to <paramref name="now"/>. This method is
    /// called once every 1 000 requests via the <see cref="_cleanupCounter"/> counter.
    /// </remarks>
    /// <param name="now">The current UTC time used as the reference point for staleness.</param>
    private void CleanupStaleBuckets(DateTime now)
    {
        var keysToRemove = new List<string>();
        foreach (var kvp in _buckets)
        {
            if ((now - kvp.Value.WindowStart).TotalMinutes > 30)
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        foreach (string key in keysToRemove)
        {
            _buckets.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Represents the sliding-window state for a single category+identifier pair.
    /// </summary>
    /// <remarks>
    /// All mutations to a bucket's fields must be performed while holding a
    /// <see langword="lock"/> on the bucket instance to prevent race conditions between
    /// concurrent requests.
    /// </remarks>
    private sealed class RateLimitBucket
    {
        /// <summary>
        /// The UTC timestamp at which the current sliding window started.
        /// </summary>
        public DateTime WindowStart;

        /// <summary>
        /// The number of requests received within the current sliding window.
        /// </summary>
        public int Count;

        /// <summary>
        /// Initialises a new <see cref="RateLimitBucket"/> with the given window start time
        /// and a request count of zero.
        /// </summary>
        /// <param name="windowStart">The UTC start time of the first window.</param>
        public RateLimitBucket(DateTime windowStart)
        {
            WindowStart = windowStart;
            Count = 0;
        }
    }
}
