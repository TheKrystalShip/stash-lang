namespace Stash.Registry.Configuration;

public sealed class RateLimitingConfig
{
    public bool Enabled { get; set; }
    public RateLimitRule Auth { get; set; } = new() { MaxAttempts = 10, WindowSeconds = 300 };
    public RateLimitRule Publish { get; set; } = new() { MaxPerHour = 30 };
    public RateLimitRule Download { get; set; } = new() { MaxPerMinute = 120 };
    public RateLimitRule Search { get; set; } = new() { MaxPerMinute = 60 };
}

public sealed class RateLimitRule
{
    public int MaxAttempts { get; set; }
    public int WindowSeconds { get; set; }
    public int MaxPerHour { get; set; }
    public int MaxPerMinute { get; set; }
}
