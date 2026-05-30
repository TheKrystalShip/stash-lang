namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Deploy-time policy governing what happens when a caller attempts to
/// <see cref="RegistryAction.CreatePackage"/> in an unclaimed scope.
/// </summary>
/// <remarks>
/// This enum governs only the <em>unclaimed-scope</em> branch of <c>CreatePackage</c>.
/// Reserved scopes (<c>stash</c>, <c>admin</c>) and scopes owned by another principal
/// are always denied regardless of this setting.
/// </remarks>
public enum ScopeOwnershipPolicyKind
{
    /// <summary>
    /// An unclaimed scope is automatically claimed by the caller on first publish.
    /// </summary>
    Open,

    /// <summary>
    /// (Default) An unclaimed scope must be explicitly claimed via
    /// <c>POST /api/v1/scopes</c> before publishing into it.
    /// </summary>
    Claim,

    /// <summary>
    /// An unclaimed (or pending-verification) scope must be claimed AND verified
    /// before publishing is allowed.
    /// </summary>
    Verified
}
