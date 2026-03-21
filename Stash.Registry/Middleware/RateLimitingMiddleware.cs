using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Stash.Registry.Configuration;

namespace Stash.Registry.Middleware;

public sealed class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitingConfig _config;
    private readonly ConcurrentDictionary<string, RateLimitBucket> _buckets = new();
    private int _cleanupCounter;

    public RateLimitingMiddleware(RequestDelegate next, RateLimitingConfig config)
    {
        _next = next;
        _config = config;
    }

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

    private sealed class RateLimitBucket
    {
        public DateTime WindowStart;
        public int Count;

        public RateLimitBucket(DateTime windowStart)
        {
            WindowStart = windowStart;
            Count = 0;
        }
    }
}
