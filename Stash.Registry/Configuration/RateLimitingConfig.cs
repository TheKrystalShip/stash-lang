namespace Stash.Registry.Configuration;

/// <summary>
/// Configuration for API rate limiting, broken down by request category.
/// </summary>
/// <remarks>
/// When <see cref="Enabled"/> is <c>true</c>, incoming requests are throttled according to the
/// per-category <see cref="RateLimitRule"/> objects. Each rule is independent and targets a
/// different API surface area. Configured in the <c>RateLimiting</c> section of <c>appsettings.json</c>.
/// </remarks>
public sealed class RateLimitingConfig
{
    /// <summary>Gets or sets whether rate limiting is active. Defaults to <c>false</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the rate limit rule applied to authentication endpoints (login, token creation).</summary>
    public RateLimitRule Auth { get; set; } = new() { MaxAttempts = 10, WindowSeconds = 300 };

    /// <summary>Gets or sets the rate limit rule applied to package publish operations.</summary>
    public RateLimitRule Publish { get; set; } = new() { MaxPerHour = 30 };

    /// <summary>Gets or sets the rate limit rule applied to package download requests.</summary>
    public RateLimitRule Download { get; set; } = new() { MaxPerMinute = 120 };

    /// <summary>Gets or sets the rate limit rule applied to package search requests.</summary>
    public RateLimitRule Search { get; set; } = new() { MaxPerMinute = 60 };
}

/// <summary>
/// A single rate-limiting rule that caps requests by count within a rolling time window.
/// </summary>
/// <remarks>
/// Depending on the API category, either the attempt-based window (<see cref="MaxAttempts"/> /
/// <see cref="WindowSeconds"/>) or the throughput-based limits (<see cref="MaxPerHour"/> /
/// <see cref="MaxPerMinute"/>) will be evaluated.
/// </remarks>
public sealed class RateLimitRule
{
    /// <summary>Gets or sets the maximum number of attempts allowed within <see cref="WindowSeconds"/>.</summary>
    public int MaxAttempts { get; set; }

    /// <summary>Gets or sets the rolling window duration in seconds used with <see cref="MaxAttempts"/>.</summary>
    public int WindowSeconds { get; set; }

    /// <summary>Gets or sets the maximum number of requests permitted per hour.</summary>
    public int MaxPerHour { get; set; }

    /// <summary>Gets or sets the maximum number of requests permitted per minute.</summary>
    public int MaxPerMinute { get; set; }
}
