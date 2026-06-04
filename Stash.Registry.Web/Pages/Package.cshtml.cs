using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Services;

namespace Stash.Registry.Web.Pages;

/// <summary>
/// Page model for the package detail page (<c>GET /packages/@{scope}/{name}</c>).
/// Loads the package via <see cref="IRegistryClient.GetPackageAsync"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Registry 404 → sets <see cref="NotFound"/> = true; view shows 404 status.</item>
///   <item><see cref="RegistryClientException"/> with 5xx → sets 502; view shows error banner.</item>
///   <item>README rendering is explicitly deferred to phase P5. The README column is a static placeholder.</item>
/// </list>
/// </remarks>
public sealed class PackageModel : PageModel
{
    private readonly IRegistryClient _registryClient;

    public PackageModel(IRegistryClient registryClient)
    {
        _registryClient = registryClient;
    }

    // ── Bound route values ────────────────────────────────────────────────────

    /// <summary>The scope segment (without leading <c>@</c>) from the URL.</summary>
    [BindProperty(SupportsGet = true)]
    public string Scope { get; set; } = string.Empty;

    /// <summary>The package name segment from the URL.</summary>
    [BindProperty(SupportsGet = true)]
    public string Name { get; set; } = string.Empty;

    // ── Page state ────────────────────────────────────────────────────────────

    /// <summary>
    /// The package detail response from the registry, or <c>null</c> when the package was not found.
    /// </summary>
    public PackageDetailResponse? Package { get; private set; }

    /// <summary>
    /// <c>true</c> when the registry returned 404. The page renders the 404 status.
    /// </summary>
    public bool PackageNotFound { get; private set; }

    /// <summary>
    /// Error message to display when the registry is unreachable (502), or <c>null</c> on success.
    /// </summary>
    public string? RegistryError { get; private set; }

    // ── Derived helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// The fully-qualified package display name including the <c>@</c> prefix.
    /// </summary>
    public string FullDisplayName => $"@{Scope}/{Name}";

    /// <summary>
    /// The install command shown in the install widget.
    /// </summary>
    public string InstallCommand => $"stash pkg add @{Scope}/{Name}";

    /// <summary>
    /// The versions from <see cref="Package"/>, sorted by <see cref="VersionDetailResponse.PublishedAt"/> descending.
    /// Empty when the package has no published versions.
    /// </summary>
    public IReadOnlyList<VersionDetailResponse> SortedVersions
    {
        get
        {
            if (Package is null)
                return Array.Empty<VersionDetailResponse>();

            return Package.Versions.Values
                .OrderByDescending(v => v.PublishedAt)
                .ToList();
        }
    }

    /// <summary>
    /// The dependencies of the latest version, or an empty dictionary when there is no latest version.
    /// </summary>
    public IReadOnlyDictionary<string, object> LatestDependencies
    {
        get
        {
            if (Package?.Latest is { } latest &&
                Package.Versions.TryGetValue(latest, out var latestVersion))
            {
                return latestVersion.Dependencies;
            }

            return new Dictionary<string, object>();
        }
    }

    // ── Handler ───────────────────────────────────────────────────────────────

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Package = await _registryClient.GetPackageAsync(Scope, Name, cancellationToken);

            if (Package is null)
            {
                PackageNotFound = true;
                Response.StatusCode = StatusCodes.Status404NotFound;
                return Page();
            }
        }
        catch (RegistryClientException ex) when ((int)ex.StatusCode >= 500)
        {
            Response.StatusCode = StatusCodes.Status502BadGateway;
            RegistryError = "The package registry is currently unreachable.";
        }
        catch (Exception)
        {
            Response.StatusCode = StatusCodes.Status502BadGateway;
            RegistryError = "The package registry is currently unreachable.";
        }

        return Page();
    }
}
