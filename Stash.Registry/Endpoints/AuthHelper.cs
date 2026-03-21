using System;

namespace Stash.Registry.Endpoints;

public static class AuthHelper
{
    public static DateTime ParseTokenExpiry(string expiry)
    {
        string s = expiry.Trim();
        if (s.EndsWith("d", StringComparison.OrdinalIgnoreCase))
        {
            int days = int.Parse(s[..^1]);
            return DateTime.UtcNow.AddDays(days);
        }
        if (s.EndsWith("h", StringComparison.OrdinalIgnoreCase))
        {
            int hours = int.Parse(s[..^1]);
            return DateTime.UtcNow.AddHours(hours);
        }
        if (s.EndsWith("m", StringComparison.OrdinalIgnoreCase))
        {
            int minutes = int.Parse(s[..^1]);
            return DateTime.UtcNow.AddMinutes(minutes);
        }
        return DateTime.UtcNow.AddDays(90);
    }
}
