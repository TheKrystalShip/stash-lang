using Stash.Registry.Contracts;

namespace Stash.Registry.Web.Services;

/// <summary>
/// Typed client interface for the Stash package registry REST API.
/// Implementations call the registry server-to-server; the browser never sees the registry directly.
/// </summary>
/// <remarks>
/// <para>
/// All methods accept a <see cref="CancellationToken"/> for cooperative cancellation.
/// </para>
/// <para>
/// When the registry returns HTTP 404, nullable methods return <see langword="null"/> rather than
/// throwing. For all other non-success status codes, implementations throw
/// <see cref="RegistryClientException"/> carrying the status code and any parsed error fields.
/// </para>
/// </remarks>
public interface IRegistryClient
{
    /// <summary>
    /// Calls <c>GET /api/v1/search</c> and returns a paged list of matching package summaries.
    /// </summary>
    /// <param name="query">The search query parameters (free text, filters, sort, paging).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResponse<PackageSummaryResponse>> SearchAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls <c>GET /api/v1/packages/{scope}/{name}</c> and returns the package detail,
    /// or <see langword="null"/> when the registry returns 404.
    /// </summary>
    /// <param name="scope">The package scope segment (without leading <c>@</c>).</param>
    /// <param name="name">The package name segment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PackageDetailResponse?> GetPackageAsync(
        string scope,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls <c>GET /api/v1/packages/{scope}/{name}/versions</c> and returns a paged list of version details,
    /// or <see langword="null"/> when the registry returns 404.
    /// </summary>
    /// <param name="scope">The package scope segment.</param>
    /// <param name="name">The package name segment.</param>
    /// <param name="query">Paging parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResponse<VersionDetailResponse>?> GetVersionsAsync(
        string scope,
        string name,
        VersionsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls <c>GET /api/v1/packages/{scope}/{name}/readme</c> and returns the README response,
    /// or <see langword="null"/> when the registry returns 404.
    /// </summary>
    /// <param name="scope">The package scope segment.</param>
    /// <param name="name">The package name segment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ReadmeResponse?> GetReadmeAsync(
        string scope,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls <c>GET /api/v1/packages/{scope}/{name}/{version}</c> and returns the version detail,
    /// or <see langword="null"/> when the registry returns 404.
    /// </summary>
    /// <param name="scope">The package scope segment.</param>
    /// <param name="name">The package name segment.</param>
    /// <param name="version">The version string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<VersionDetailResponse?> GetVersionAsync(
        string scope,
        string name,
        string version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls <c>GET /api/v1/.well-known/registry</c> and returns the discovery response.
    /// Used by the <c>/health</c> page and for future capability-detection.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DiscoveryResponse> GetDiscoveryAsync(CancellationToken cancellationToken = default);
}
