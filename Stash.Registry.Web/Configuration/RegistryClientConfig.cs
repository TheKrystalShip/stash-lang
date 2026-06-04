namespace Stash.Registry.Web.Configuration;

/// <summary>
/// Typed configuration for the registry client.
/// Bound from the <c>Registry</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class RegistryClientConfig
{
    /// <summary>
    /// The base URL of the registry origin (e.g. <c>http://localhost:5290</c>).
    /// The website appends <c>/api/v1/…</c> itself.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5290";
}
