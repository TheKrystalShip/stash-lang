using System;
using System.Collections.Generic;

namespace Stash.Registry.Web.Rendering;

/// <summary>
/// Render-time gate for package-authored URLs: accepts only absolute http(s)/mailto URLs
/// and returns <c>null</c> for everything else (bare paths, <c>javascript:</c>, <c>data:</c>,
/// <c>vbscript:</c>, <c>file:</c>, relative paths, etc.).
/// </summary>
/// <remarks>
/// The same scheme allow-list as <see cref="ReadmeRenderer"/> (<c>http</c>, <c>https</c>,
/// <c>mailto</c>) so the two chokepoints are consistent.
/// </remarks>
public static class SafeUrl
{
    private static readonly HashSet<string> AllowedSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    /// <summary>
    /// Returns <paramref name="url"/> unchanged if it is an absolute URL with an allowed
    /// scheme (<c>http</c>, <c>https</c>, or <c>mailto</c>); otherwise returns <c>null</c>.
    /// </summary>
    /// <param name="url">The package-authored URL to validate.</param>
    /// <returns>
    /// The original <paramref name="url"/> string when safe, or <c>null</c> when the URL is
    /// null/whitespace, not an absolute URI, or carries a disallowed scheme.
    /// </returns>
    public static string? AllowExternal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        return AllowedSchemes.Contains(uri.Scheme) ? url : null;
    }
}
