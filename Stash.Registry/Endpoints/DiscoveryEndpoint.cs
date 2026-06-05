using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;

namespace Stash.Registry.Endpoints;

/// <summary>
/// Registers the public discovery endpoint at
/// <c>GET /api/v1/.well-known/registry</c>.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint is a minimal-API <c>MapGet</c> (health-endpoint style) and does NOT live on a
/// controller.  It therefore carries no <c>[RegistryAuthorize]</c> attribute — capability
/// advertisement is static and requires no PDP consultation.
/// </para>
/// <para>
/// Revoked-token enforcement is handled upstream in <c>Startup.Configure</c>, which bypasses
/// the JTI gate for <c>/api/v1/.well-known</c> paths (same pattern as <c>/openapi</c>).
/// Anonymous callers and callers presenting a garbage or revoked Bearer token both receive 200.
/// </para>
/// <para>
/// <b>maxPageSize drift prevention:</b> <see cref="DiscoveryLimits.MaxPageSize"/> is sourced
/// from <see cref="PagingLimits.MaxPageSize"/> — the <em>same</em> <c>const int</c> used by the
/// <c>[Range(1, PagingLimits.MaxPageSize)]</c> attribute on <c>SearchQuery.pageSize</c> and
/// <c>VersionsQuery.pageSize</c>.  This makes an advertised/enforced drift a compile-time
/// impossibility.
/// </para>
/// </remarks>
public static class DiscoveryEndpoint
{
    /// <summary>
    /// Maps the <c>GET /api/v1/.well-known/registry</c> endpoint onto <paramref name="app"/>.
    /// </summary>
    /// <param name="app">The <see cref="WebApplication"/> to map the endpoint onto.</param>
    /// <param name="config">The live <see cref="RegistryConfig"/> instance (singleton).</param>
    public static void Map(WebApplication app, RegistryConfig config)
    {
        app.MapGet("/api/v1/.well-known/registry", () =>
        {
            var response = new DiscoveryResponse
            {
                Name = "Stash Package Registry",
                ApiVersion = "v1",
                BasePath = "/api/v1",
                Limits = new DiscoveryLimits
                {
                    MaxPackageSize = config.Security.MaxPackageSizeBytes,
                    MaxPageSize = PagingLimits.MaxPageSize,
                },
                Links = new DiscoveryLinks
                {
                    Search = "/api/v1/search",
                    Packages = "/api/v1/packages",
                    OpenApi = "/openapi/v1.json",
                    WellKnown = "/api/v1/.well-known/registry",
                },
                Features = new DiscoveryFeatures
                {
                    // Bucket-A — Metrics implemented in the registry-download-metrics feature.
                    Metrics = true,

                    // Bucket-B — not yet implemented, pinned false.
                    Advisories = false,
                    Provenance = false,
                    Signatures = false,
                    TrustedPublishing = false,
                    VerifiedPublishers = false,

                    // Bucket-A — exist today, true.
                    Organizations = true,
                    PrivatePackages = true,

                    // Reflects live CORS configuration.
                    Cors = config.Cors.Enabled,

                    // Bucket-A — audit log (widened filters, export, verify, tamper-evidence) shipped in registry-audit-log-v2.
                    Audit = true,
                },
            };

            return TypedResults.Ok(response);
        })
        .WithName("Discovery_GetRegistry")
        .Produces<DiscoveryResponse>(StatusCodes.Status200OK);
    }
}
