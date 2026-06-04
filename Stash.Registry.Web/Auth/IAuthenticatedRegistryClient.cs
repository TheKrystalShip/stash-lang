using System.Threading;
using System.Threading.Tasks;
using Stash.Registry.Contracts;

namespace Stash.Registry.Web.Auth;

/// <summary>
/// Authenticated view of the registry, scoped to the signed-in user's session.
/// Every method threads the session's publish JWT as <c>Authorization: Bearer</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>C1 token-threading chokepoint.</b> This interface is the single entry-point for any
/// registry call that requires authentication. <see cref="HttpAuthenticatedRegistryClient"/>
/// sets the <c>Authorization</c> header unconditionally on every outgoing request — there
/// is no public code path that skips the header.
/// </para>
/// <para>
/// The DI factory for the concrete implementation throws <see cref="NoActiveSessionException"/>
/// if no session is in scope, making an un-authenticated call fail-closed at DI resolution
/// time rather than silently returning anonymous (public-only) results.
/// </para>
/// <para>
/// Implementations are registered <c>Scoped</c> (one per HTTP request).
/// </para>
/// </remarks>
public interface IAuthenticatedRegistryClient
{
    // ── Browse ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches packages owned by the authenticated user.
    /// Returns both public and private packages — the caller's publish token is threaded
    /// so the registry's visibility predicate includes private packages.
    /// </summary>
    Task<PagedResponse<PackageSummaryResponse>> SearchOwnedAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full package detail, including private packages.
    /// Returns <see langword="null"/> when the registry returns 404.
    /// </summary>
    Task<PackageDetailResponse?> GetPackageAsync(
        string scope,
        string name,
        CancellationToken cancellationToken = default);

    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>Returns the identity (username + role) of the authenticated user.</summary>
    Task<WhoamiResponse> WhoamiAsync(CancellationToken cancellationToken = default);

    // ── Token management ──────────────────────────────────────────────────────

    /// <summary>Returns all active tokens for the authenticated user.</summary>
    Task<TokenListResponse> ListTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a new token for the authenticated user.</summary>
    Task<TokenCreateResponse> CreateTokenAsync(
        TokenCreateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes (deletes) the token with the given <paramref name="tokenId"/>.</summary>
    Task RevokeTokenAsync(string tokenId, CancellationToken cancellationToken = default);

    // ── Package deprecation ───────────────────────────────────────────────────

    /// <summary>Marks an entire package as deprecated with a message and optional alternative.</summary>
    Task<DeprecationResponse> DeprecatePackageAsync(
        string scope,
        string name,
        DeprecatePackageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the package-level deprecation mark.</summary>
    Task<DeprecationResponse> UndeprecatePackageAsync(
        string scope,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a specific package version as deprecated.</summary>
    Task<DeprecationResponse> DeprecateVersionAsync(
        string scope,
        string name,
        string version,
        DeprecateVersionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the version-level deprecation mark.</summary>
    Task<DeprecationResponse> UndeprecateVersionAsync(
        string scope,
        string name,
        string version,
        CancellationToken cancellationToken = default);

    // ── Visibility ────────────────────────────────────────────────────────────

    /// <summary>
    /// Changes the visibility of a package.
    /// Wire values come from <see cref="Visibilities"/> — no inlined string literals.
    /// </summary>
    Task<SetVisibilityResponse> SetVisibilityAsync(
        string scope,
        string name,
        SetVisibilityRequest request,
        CancellationToken cancellationToken = default);
}
