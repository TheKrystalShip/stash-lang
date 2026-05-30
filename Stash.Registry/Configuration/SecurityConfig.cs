using System;
using Stash.Registry.Auth.Authorization;

namespace Stash.Registry.Configuration;

public sealed class SecurityConfig
{
    public string MaxPackageSize { get; set; } = "10MB";
    public string RequiredIntegrity { get; set; } = "sha256";
    public string UnpublishWindow { get; set; } = "72h";
    public string? JwtSigningKey { get; set; }

    /// <summary>
    /// Deploy-time scope ownership policy for the unclaimed-scope branch of CreatePackage.
    /// Values: Open, Claim (default), Verified. Deserialized from appsettings.json "ScopeOwnershipPolicy".
    /// </summary>
    public ScopeOwnershipPolicyKind ScopeOwnershipPolicy { get; set; } = ScopeOwnershipPolicyKind.Claim;

    /// <summary>
    /// Server-enforced upper bound on how long an API token may live.
    /// <c>POST /api/v1/auth/tokens</c> rejects any <c>expires_in</c> that exceeds this value.
    /// Defaults to 90 days. Corresponds to <c>"MaxTokenLifetime": "90.00:00:00"</c> in
    /// appsettings.json (ISO 8601 duration or .NET TimeSpan format).
    /// </summary>
    public TimeSpan MaxTokenLifetime { get; set; } = new TimeSpan(90, 0, 0, 0);

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
