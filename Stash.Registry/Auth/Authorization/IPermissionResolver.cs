using System.Threading.Tasks;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Resolves the effective package role for a given user on a given package,
/// unioning direct user grants, team-mediated grants, and org-mediated grants.
/// </summary>
/// <remarks>
/// The resolver consumes the existing <c>PackageRoleEntry</c> schema with no DB schema changes.
/// It fails closed on dangling references: a grant edge pointing at a deleted team/org/scope
/// yields no access rather than throwing or granting.
/// </remarks>
public interface IPermissionResolver
{
    /// <summary>
    /// Returns the highest effective role string (<c>owner</c>, <c>maintainer</c>,
    /// <c>publisher</c>, <c>reader</c>) for <paramref name="username"/> on
    /// <paramref name="packageName"/>, or <c>null</c> if the user has no access.
    /// </summary>
    /// <param name="username">The username to resolve for.</param>
    /// <param name="packageName">The full scoped package name, e.g. <c>@acme/widgets</c>.</param>
    Task<string?> GetEffectiveRoleAsync(string username, string packageName);

    /// <summary>
    /// Returns whether <paramref name="username"/> is a member of the organisation
    /// identified by <paramref name="orgId"/>.
    /// </summary>
    Task<bool> IsOrgMemberAsync(string username, string orgId);
}
