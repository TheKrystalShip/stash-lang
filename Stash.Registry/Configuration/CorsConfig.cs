using System.Collections.Generic;

namespace Stash.Registry.Configuration;

/// <summary>
/// Configuration for the CORS layer. Off by default — no <c>Access-Control-*</c> headers
/// are emitted and no preflight <c>OPTIONS</c> handling is attached when
/// <see cref="Enabled"/> is <c>false</c>.
/// </summary>
/// <remarks>
/// Configured in the <c>Cors</c> section of <c>appsettings.json</c>. When
/// <see cref="Enabled"/> is <c>true</c>, <c>AddCors</c> and <c>UseCors</c> are registered in the
/// pipeline using the values below; when <c>false</c>, neither is registered and the server
/// behaves byte-identically to a build that has no CORS support at all.
/// </remarks>
public sealed class CorsConfig
{
    /// <summary>Gets or sets whether CORS is active. Defaults to <c>false</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the list of allowed origins. An empty list means no origin is permitted.
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of allowed HTTP methods.
    /// The shipped <c>appsettings.json</c> default is <c>["GET", "HEAD"]</c>.
    /// </summary>
    /// <remarks>
    /// The C# property default is intentionally an empty list to avoid the .NET config-binder
    /// list-append quirk: if a non-empty default list were present, binding a config section
    /// containing the same values would append rather than replace them.
    /// </remarks>
    public List<string> AllowedMethods { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of allowed request headers.
    /// The shipped <c>appsettings.json</c> default is <c>Content-Type</c>, <c>Authorization</c>,
    /// <c>If-None-Match</c>, and <c>If-Modified-Since</c>.
    /// </summary>
    /// <remarks>
    /// The C# property default is intentionally an empty list to avoid the .NET config-binder
    /// list-append quirk.
    /// </remarks>
    public List<string> AllowedHeaders { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the CORS policy allows credentials (cookies, HTTP auth, client-side
    /// certificates). Defaults to <c>false</c>.
    /// </summary>
    public bool AllowCredentials { get; set; }
}
