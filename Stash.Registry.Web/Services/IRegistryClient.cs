using Stash.Registry.Contracts;

namespace Stash.Registry.Web.Services;

/// <summary>
/// Typed client interface for the Stash package registry REST API.
/// Implementations call the registry server-to-server; the browser never sees the registry directly.
/// </summary>
/// <remarks>
/// Phase 2 expands this interface with the full method set (Search, GetPackage, etc.).
/// Phase 1 ships only <see cref="GetDiscoveryAsync"/> so the DI wiring and /health smoke test work
/// end-to-end without pulling forward future-phase surface.
/// </remarks>
public interface IRegistryClient
{
    /// <summary>
    /// Calls <c>GET /api/v1/.well-known/registry</c> and returns the discovery response.
    /// Used by the <c>/health</c> page and for future capability-detection.
    /// </summary>
    Task<DiscoveryResponse> GetDiscoveryAsync(CancellationToken cancellationToken = default);
}
