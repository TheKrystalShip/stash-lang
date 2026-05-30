using System.Threading.Tasks;

namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Single Policy Decision Point (PDP) for all registry authorization decisions.
/// </summary>
/// <remarks>
/// <para>
/// The PDP evaluates every authorization request as a two-step intersection:
/// <list type="number">
///   <item><description><b>Ceiling check (step 1):</b> the token ceiling must be sufficient for the action. Runs first; the admin role does NOT bypass this step.</description></item>
///   <item><description><b>Resource-side check (step 2):</b> package role, scope ownership, org membership, or visibility as appropriate for the action.</description></item>
/// </list>
/// </para>
/// <para>
/// Admin role short-circuit: when <see cref="UserPrincipal.Role"/> is <see cref="UserRole.Admin"/>,
/// the resource-side dimension resolves to effective <c>owner</c> on any package/scope/org.
/// The ceiling check still runs first.
/// </para>
/// </remarks>
public interface IRegistryAuthorizer
{
    /// <summary>
    /// Evaluates whether <paramref name="principal"/> may perform <paramref name="action"/>
    /// on <paramref name="resource"/>.
    /// </summary>
    /// <param name="principal">The authenticated (or anonymous) caller.</param>
    /// <param name="action">The registry action to authorise.</param>
    /// <param name="resource">The resource the action targets.</param>
    /// <returns>
    /// An <see cref="AuthzDecision"/> with <see cref="AuthzDecision.Allowed"/> set to
    /// <c>true</c> on success, or <c>false</c> with a typed <see cref="AuthzDenyReason"/>
    /// on failure.
    /// </returns>
    Task<AuthzDecision> AuthorizeAsync(
        Principal principal,
        RegistryAction action,
        ResourceRef resource);
}
