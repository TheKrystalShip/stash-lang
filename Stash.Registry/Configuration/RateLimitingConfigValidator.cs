using System;

namespace Stash.Registry.Configuration;

/// <summary>
/// Validates the bound <see cref="RateLimitingConfig"/> at startup.
/// Only enforced when <see cref="RateLimitingConfig.Enabled"/> is <c>true</c>;
/// disabled configurations are not inspected since the values are unused.
/// </summary>
public static class RateLimitingConfigValidator
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if any rule has a non-positive
    /// <see cref="RateLimitRule.MaxAttempts"/> or <see cref="RateLimitRule.WindowSeconds"/>
    /// while rate limiting is enabled.
    /// </summary>
    public static void Validate(RateLimitingConfig config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (!config.Enabled) return;

        ValidateRule("Auth", config.Auth);
        ValidateRule("Publish", config.Publish);
        ValidateRule("Download", config.Download);
        ValidateRule("Search", config.Search);
    }

    private static void ValidateRule(string name, RateLimitRule rule)
    {
        if (rule is null)
        {
            throw new InvalidOperationException(
                $"Invalid Registry:RateLimiting:{name}: rule is missing.");
        }

        if (rule.MaxAttempts <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid Registry:RateLimiting:{name}:MaxAttempts value '{rule.MaxAttempts}': must be a positive integer when rate limiting is enabled.");
        }

        if (rule.WindowSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid Registry:RateLimiting:{name}:WindowSeconds value '{rule.WindowSeconds}': must be a positive integer when rate limiting is enabled.");
        }
    }
}
