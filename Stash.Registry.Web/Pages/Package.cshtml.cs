using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Rendering;
using Stash.Registry.Web.Services;

namespace Stash.Registry.Web.Pages;

/// <summary>
/// Page model for the package detail page (<c>GET /packages/@{scope}/{name}</c>).
/// Loads the package and its README via <see cref="IRegistryClient"/> in parallel,
/// then renders the README through <see cref="IReadmeRenderer.RenderToSafeHtml"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Registry 404 → sets <see cref="PackageNotFound"/> = true; view shows 404 status.</item>
///   <item><see cref="RegistryClientException"/> with 5xx → sets 502; view shows error banner.</item>
///   <item>README null or empty → <see cref="ReadmeHtml"/> is <see langword="null"/>; view shows empty-state.</item>
///   <item>README present → <see cref="ReadmeHtml"/> is an <see cref="HtmlString"/> from
///   <see cref="IReadmeRenderer.RenderToSafeHtml"/> — the sole <c>@Html.Raw</c> site in the project.</item>
/// </list>
/// </remarks>
public sealed class PackageModel : PageModel
{
    private readonly IRegistryClient _registryClient;
    private readonly IReadmeRenderer _readmeRenderer;

    public PackageModel(IRegistryClient registryClient, IReadmeRenderer readmeRenderer)
    {
        _registryClient = registryClient;
        _readmeRenderer = readmeRenderer;
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

    /// <summary>
    /// Sanitized README HTML, or <c>null</c> when the package has no README (empty or missing).
    /// This is the sole property in the project whose value is emitted via <c>@Html.Raw()</c>.
    /// It is populated <em>only</em> from <see cref="IReadmeRenderer.RenderToSafeHtml"/>.
    /// </summary>
    public HtmlString? ReadmeHtml { get; private set; }

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
            // Fetch package metadata and README in parallel.
            var packageTask = _registryClient.GetPackageAsync(Scope, Name, cancellationToken);
            var readmeTask = _registryClient.GetReadmeAsync(Scope, Name, cancellationToken);

            Package = await packageTask;

            if (Package is null)
            {
                PackageNotFound = true;
                Response.StatusCode = StatusCodes.Status404NotFound;
                return Page();
            }

            // Fetch README — a missing or empty README is the empty-state, not an error.
            ReadmeResponse? readme = null;
            try
            {
                readme = await readmeTask;
            }
            catch
            {
                // README is secondary content; suppress any fetch error — show empty-state instead.
                readme = null;
            }

            if (readme is not null && !string.IsNullOrWhiteSpace(readme.Content))
            {
                // The SOLE assignment of ReadmeHtml — always from IReadmeRenderer.RenderToSafeHtml.
                ReadmeHtml = _readmeRenderer.RenderToSafeHtml(readme.Content);
            }
            // else: ReadmeHtml stays null → view shows "This package has no README." empty-state.
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
