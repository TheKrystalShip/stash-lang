using System;

namespace Stash.Registry.Endpoints;

/// <summary>
/// Shared utility methods used across authentication and token management endpoints.
/// </summary>
/// <remarks>
/// Currently provides token expiry parsing used by the token creation endpoint (e.g.
/// <c>AuthEndpoints</c>) to convert human-readable duration strings into absolute
/// <see cref="DateTime"/> values. Additional auth helpers may be added here as the
/// authentication surface grows.
/// </remarks>
public static class AuthHelper
{
    /// <summary>
    /// Parses a human-readable token expiry string into an absolute UTC expiry
    /// <see cref="DateTime"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Supported suffix formats (case-insensitive):
    /// <list type="table">
    ///   <listheader><term>Suffix</term><description>Unit</description></listheader>
    ///   <item><term><c>d</c></term><description>Days — e.g. <c>"30d"</c></description></item>
    ///   <item><term><c>h</c></term><description>Hours — e.g. <c>"24h"</c></description></item>
    ///   <item><term><c>m</c></term><description>Minutes — e.g. <c>"60m"</c></description></item>
    /// </list>
    /// If the string does not match any recognised suffix, the method falls back to a
    /// default expiry of 90 days from the current UTC time.
    /// </para>
    /// </remarks>
    /// <param name="expiry">
    /// A trimmed duration string such as <c>"30d"</c>, <c>"12h"</c>, or <c>"90m"</c>.
    /// </param>
    /// <returns>
    /// The absolute UTC <see cref="DateTime"/> at which the token should expire.
    /// </returns>
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
        if (int.TryParse(s, out int defaultDays))
        {
            return DateTime.UtcNow.AddDays(defaultDays);
        }

        throw new FormatException($"Unrecognised token expiry format: '{expiry}'. Use formats like '30d', '12h', or '90m'.");
    }
}
