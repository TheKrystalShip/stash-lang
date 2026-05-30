namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// The requesting identity as understood by the PDP.
/// </summary>
public abstract record Principal;

/// <summary>
/// An unauthenticated (anonymous) caller — no JWT, no user record.
/// </summary>
public sealed record AnonymousPrincipal() : Principal;

/// <summary>
/// An authenticated registry user with a valid, non-revoked JWT.
/// </summary>
/// <param name="Username">The authenticated username (JWT <c>sub</c> claim).</param>
/// <param name="Role">The user's system role (<c>user</c> or <c>admin</c>).</param>
/// <param name="Ceiling">The coarse capability ceiling of the presenting token.</param>
/// <param name="TokenId">The JWT <c>jti</c> claim, used for audit entries.</param>
public sealed record UserPrincipal(
    string Username,
    UserRole Role,
    TokenCeiling Ceiling,
    string TokenId) : Principal;

// Reserved for future OIDC trusted publishing — design-only this phase.
// public sealed record OidcPrincipal(string Issuer, string Subject, ...) : Principal;

/// <summary>
/// The user's system-wide role, distinct from per-package roles.
/// </summary>
public enum UserRole
{
    /// <summary>A regular registry user.</summary>
    User,

    /// <summary>A registry administrator. Admin role short-circuits the resource-side check
    /// to effective <c>owner</c> on any package, but the ceiling check runs first.</summary>
    Admin
}
