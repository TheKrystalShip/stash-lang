using System;

namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Marks a controller action that performs its authorization decision inline (imperatively)
/// rather than through <see cref="RegistryAuthorizeAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// Applied to exactly the four endpoints whose pre/post-PDP logic cannot yet be expressed
/// as a pure resource-action pair in <see cref="RegistryAction"/>:
/// <list type="bullet">
///   <item><see cref="Stash.Registry.Controllers.PackagesController.PublishPackage"/> — dynamic action choice (<c>CreatePackage</c> vs <c>PublishVersion</c>) depends on a DB existence check; folded into PDP in <c>registry-authz-pdp-completion</c>.</item>
///   <item><see cref="Stash.Registry.Controllers.OrganizationsController.DeleteOrg"/> — uses <c>AddOrgMember</c> authz action (known wrong-action bug); corrected in <c>registry-authz-pdp-completion</c>.</item>
///   <item><see cref="Stash.Registry.Controllers.ScopesController.ClaimScope"/> — post-PDP owner-type and org-ownership checks require inline logic; folded into PDP in <c>registry-authz-pdp-completion</c>.</item>
///   <item><see cref="Stash.Registry.Controllers.ScopesController.DeleteScope"/> — post-PDP audit targeting <c>@{scope}</c> requires inline coordination; folded into PDP in <c>registry-authz-pdp-completion</c>.</item>
/// </list>
/// </para>
/// <para>
/// <see cref="AuthzDispatchCoverageMetaTests"/> requires every non-<c>[PublicEndpoint]</c>
/// action to carry either <see cref="RegistryAuthorizeAttribute"/> or this attribute.
/// The exact set of endpoints marked with this attribute is pinned by a meta-test assertion —
/// adding or removing this marker requires updating that assertion, forcing reviewer attention.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ImperativeAuthzAttribute : Attribute
{
    /// <summary>
    /// Gets the human-readable reason that explains why this endpoint cannot yet use
    /// <see cref="RegistryAuthorizeAttribute"/> and names the deferred work item.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Initialises the attribute with the reason string.
    /// </summary>
    /// <param name="reason">
    /// A brief description of why the inline authorization is necessary and which
    /// future feature (e.g. <c>registry-authz-pdp-completion</c>) will resolve it.
    /// </param>
    public ImperativeAuthzAttribute(string reason)
    {
        Reason = reason;
    }
}
