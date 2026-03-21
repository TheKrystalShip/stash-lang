using System;

namespace Stash.Registry.Configuration;

public sealed class SecurityConfig
{
    public string MaxPackageSize { get; set; } = "10MB";
    public string RequiredIntegrity { get; set; } = "sha256";
    public string UnpublishWindow { get; set; } = "72h";
    public string? JwtSigningKey { get; set; }

    public long MaxPackageSizeBytes
    {
        get
        {
            string s = MaxPackageSize.Trim();
            if (s.EndsWith("MB", StringComparison.OrdinalIgnoreCase))
            {
                return long.Parse(s[..^2]) * 1024 * 1024;
            }

            if (s.EndsWith("KB", StringComparison.OrdinalIgnoreCase))
            {
                return long.Parse(s[..^2]) * 1024;
            }

            if (s.EndsWith("GB", StringComparison.OrdinalIgnoreCase))
            {
                return long.Parse(s[..^2]) * 1024 * 1024 * 1024;
            }

            return long.Parse(s);
        }
    }

    public TimeSpan UnpublishWindowTimeSpan
    {
        get
        {
            string s = UnpublishWindow.Trim();
            if (s.EndsWith("h", StringComparison.OrdinalIgnoreCase))
            {
                return TimeSpan.FromHours(double.Parse(s[..^1]));
            }

            if (s.EndsWith("d", StringComparison.OrdinalIgnoreCase))
            {
                return TimeSpan.FromDays(double.Parse(s[..^1]));
            }

            if (s.EndsWith("m", StringComparison.OrdinalIgnoreCase))
            {
                return TimeSpan.FromMinutes(double.Parse(s[..^1]));
            }

            return TimeSpan.FromHours(72);
        }
    }
}
