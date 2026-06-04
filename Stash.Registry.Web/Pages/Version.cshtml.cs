using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Services;

namespace Stash.Registry.Web.Pages;

/// <summary>
/// Page model for the version detail page (<c>GET /packages/@{scope}/{name}/v/{version}</c>).
/// Loads a single version's metadata via <see cref="IRegistryClient.GetVersionAsync"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Registry 404 (null response) → sets <see cref="VersionNotFound"/> = true; view shows 404 status.</item>
///   <item><see cref="RegistryClientException"/> with 5xx → sets 502; view shows error banner.</item>
///   <item>No <c>@Html.Raw</c> in this page — version detail has no README column.</item>
/// </list>
/// </remarks>
public sealed class VersionModel : PageModel
{
    private readonly IRegistryClient _registryClient;

    public VersionModel(IRegistryClient registryClient)
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

    /// <summary>The version string segment from the URL (e.g. <c>1.2.3</c>).</summary>
    [BindProperty(SupportsGet = true)]
    public string Version { get; set; } = string.Empty;

    // ── Page state ────────────────────────────────────────────────────────────

    /// <summary>
    /// The version detail response from the registry, or <c>null</c> when not found.
    /// </summary>
    public VersionDetailResponse? VersionDetail { get; private set; }

    /// <summary>
    /// <c>true</c> when the registry returned 404. The page renders the 404 status.
    /// </summary>
    public bool VersionNotFound { get; private set; }

    /// <summary>
    /// Error message to display when the registry is unreachable (502), or <c>null</c> on success.
    /// </summary>
    public string? RegistryError { get; private set; }

    /// <summary>
    /// Validation message when the registry rejected the request with 400, or <c>null</c> otherwise.
    /// Distinct from <see cref="RegistryError"/> so the view renders a concise inline alert
    /// without the "Registry Unavailable" heading that only belongs with 5xx responses.
    /// </summary>
    public string? ValidationError { get; private set; }

    // ── Derived helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// The fully-qualified package display name including the <c>@</c> prefix.
    /// </summary>
    public string FullDisplayName => $"@{Scope}/{Name}";

    /// <summary>
    /// The breadcrumb link back to the package detail page.
    /// </summary>
    public string PackageDetailUrl => $"/packages/@{Scope}/{Name}";

    // ── Handler ───────────────────────────────────────────────────────────────

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            VersionDetail = await _registryClient.GetVersionAsync(Scope, Name, Version, cancellationToken);

            if (VersionDetail is null)
            {
                VersionNotFound = true;
                Response.StatusCode = StatusCodes.Status404NotFound;
                return Page();
            }
        }
        catch (RegistryClientException ex) when ((int)ex.StatusCode == StatusCodes.Status400BadRequest)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            ValidationError = ex.ErrorMessage ?? "The request was invalid.";
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
