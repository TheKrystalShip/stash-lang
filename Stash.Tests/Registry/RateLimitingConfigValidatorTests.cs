using System;
using Stash.Registry.Configuration;
using Xunit;

namespace Stash.Tests.Registry;

public class RateLimitingConfigValidatorTests
{
    [Fact]
    public void Validate_Disabled_AllowsZeroValues()
    {
        var config = new RateLimitingConfig
        {
            Enabled = false,
            Auth = new RateLimitRule { MaxAttempts = 0, WindowSeconds = 0 },
            Publish = new RateLimitRule { MaxAttempts = 0, WindowSeconds = 0 },
            Download = new RateLimitRule { MaxAttempts = 0, WindowSeconds = 0 },
            Search = new RateLimitRule { MaxAttempts = 0, WindowSeconds = 0 },
        };

        RateLimitingConfigValidator.Validate(config);
    }

    [Fact]
    public void Validate_DefaultConfig_Succeeds()
    {
        var config = new RegistryConfig().RateLimiting;
        config.Enabled = true;
        // The class-level defaults set Auth = 10/300; the others use MaxPerHour/MaxPerMinute.
        // We must give them valid MaxAttempts/WindowSeconds too since the validator checks all rules.
        config.Publish = new RateLimitRule { MaxAttempts = 5, WindowSeconds = 60, MaxPerHour = 30 };
        config.Download = new RateLimitRule { MaxAttempts = 120, WindowSeconds = 60, MaxPerMinute = 120 };
        config.Search = new RateLimitRule { MaxAttempts = 60, WindowSeconds = 60, MaxPerMinute = 60 };

        RateLimitingConfigValidator.Validate(config);
    }

    [Fact]
    public void Validate_EnabledWithZeroMaxAttempts_Throws()
    {
        var config = new RateLimitingConfig
        {
            Enabled = true,
            Auth = new RateLimitRule { MaxAttempts = 0, WindowSeconds = 60 },
            Publish = new RateLimitRule { MaxAttempts = 5, WindowSeconds = 60 },
            Download = new RateLimitRule { MaxAttempts = 5, WindowSeconds = 60 },
            Search = new RateLimitRule { MaxAttempts = 5, WindowSeconds = 60 },
        };

        var ex = Assert.Throws<InvalidOperationException>(() => RateLimitingConfigValidator.Validate(config));
        Assert.Contains("Auth:MaxAttempts", ex.Message);
    }

    [Fact]
    public void Validate_EnabledWithZeroWindowSeconds_Throws()
    {
        var config = new RateLimitingConfig
        {
            Enabled = true,
            Auth = new RateLimitRule { MaxAttempts = 10, WindowSeconds = 300 },
            Publish = new RateLimitRule { MaxAttempts = 5, WindowSeconds = 0 },
            Download = new RateLimitRule { MaxAttempts = 5, WindowSeconds = 60 },
            Search = new RateLimitRule { MaxAttempts = 5, WindowSeconds = 60 },
        };

        var ex = Assert.Throws<InvalidOperationException>(() => RateLimitingConfigValidator.Validate(config));
        Assert.Contains("Publish:WindowSeconds", ex.Message);
    }

    [Fact]
    public void Validate_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RateLimitingConfigValidator.Validate(null!));
    }
}
