using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Static dispatch table that maps a <see cref="RegistryAction"/> to its
/// <see cref="ResourceRef"/> using only HTTP route values.
/// </summary>
/// <remarks>
/// Resolvers are pure — they never touch the database or the request body.  Route
/// values are URL-decoded the same way the controllers do today
/// (<see cref="Uri.UnescapeDataString"/>).
/// </remarks>
public static class RegistryActionResourceResolver
{
    /// <summary>
    /// Resolves the <see cref="ResourceRef"/> for <paramref name="action"/> from
    /// the current request's route data.
    /// </summary>
    /// <param name="action">The registry action that the endpoint will perform.</param>
    /// <param name="routeValues">
    /// The <see cref="RouteValueDictionary"/> from <c>AuthorizationFilterContext.RouteData.Values</c>.
    /// </param>
    /// <param name="context">The current HTTP context (unused for route-only resolvers).</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="action"/> does not have a registered resolver.
    /// Every action decorated with <c>[RegistryAuthorize]</c> must have an entry here.
    /// </exception>
    public static ResourceRef Resolve(
        RegistryAction action,
        RouteValueDictionary routeValues,
        HttpContext context)
    {
        // Helper: read a route value, URL-decoding it to match today's controller behaviour.
        static string Route(RouteValueDictionary rv, string key) =>
            Uri.UnescapeDataString(rv[key]?.ToString() ?? string.Empty);

        return action switch
        {
            // ── Package (version routes) — carry {version} so the filter can
            //    emit the pre-refactor version-scoped 404 message on denial. ──
            RegistryAction.ReadPackageVersion or
            RegistryAction.DownloadPackageVersion
                => PackageRoute.From(Route(routeValues, "scope"), Route(routeValues, "name")) with
                   { Version = Route(routeValues, "version") },

            // ── Package (non-version routes) ─────────────────────────────────
            RegistryAction.ReadPackageMetadata or
            RegistryAction.CreatePackage or
            RegistryAction.PublishVersion or
            RegistryAction.PublishPackage or
            RegistryAction.UnpublishVersion or
            RegistryAction.DeprecatePackage or
            RegistryAction.DeprecateVersion or
            RegistryAction.ChangePackageVisibility or
            RegistryAction.ListPackageRoles or
            RegistryAction.AssignPackageRole or
            RegistryAction.RevokePackageRole or
            RegistryAction.DeletePackage
                => PackageRoute.From(Route(routeValues, "scope"), Route(routeValues, "name")),

            // ── Scope ─────────────────────────────────────────────────────────
            RegistryAction.ResolveScope or
            RegistryAction.VerifyScope or
            RegistryAction.DeleteScope or
            RegistryAction.ClaimScope
                => new ScopeResource(Route(routeValues, "scope")),

            // ── Org ───────────────────────────────────────────────────────────
            RegistryAction.CreateOrg or
            RegistryAction.ReadOrg or
            RegistryAction.AddOrgMember or
            RegistryAction.RemoveOrgMember or
            RegistryAction.CreateTeam or
            RegistryAction.AddTeamMember or
            RegistryAction.DeleteOrg
                => new OrgResource(Route(routeValues, "org")),

            // ── Token / self ──────────────────────────────────────────────────
            RegistryAction.RevokeOwnToken
                => new TokenResource(Route(routeValues, "id")),

            RegistryAction.Whoami or
            RegistryAction.ListOwnTokens or
            RegistryAction.IssueToken
                => new PrincipalSelfResource(),

            // ── Search ────────────────────────────────────────────────────────
            RegistryAction.Search
                => new SearchResource(),

            // ── Admin ─────────────────────────────────────────────────────────
            RegistryAction.ReadAdminStats or
            RegistryAction.ManageUser or
            RegistryAction.AdminAssignPackageRole or
            RegistryAction.AdminRevokePackageRole or
            RegistryAction.ReadAuditLog
                => new AdminResource(),

            _ => throw new InvalidOperationException(
                $"No resource resolver registered for RegistryAction.{action}. " +
                "Add an entry to RegistryActionResourceResolver.Resolve.")
        };
    }
}
