using System;

namespace Stash.Registry.Web.Auth;

/// <summary>
/// Server-side session record for an authenticated BFF user.
/// Lives only in <see cref="ISessionStore"/> — never serialized to the browser.
/// The cookie carries only the opaque session id; the JWT strings never leave the server.
/// </summary>
/// <remarks>
/// No <c>Role</c> field: <c>LoginResponse</c> does not return a role, and the only
/// UI that would need it is intentionally hardcoded to Read+Publish in v1 (admin tooling
/// is out of scope). A future phase that introduces role-gated UI adds a <c>whoami</c>
/// call at that point.
/// </remarks>
public sealed record BffSession
{
    /// <summary>The authenticated user's username.</summary>
    public required string Username { get; init; }

    /// <summary>
    /// The publish-ceiling JWT minted at login via <c>POST /api/v1/auth/tokens</c>.
    /// Used by <see cref="ISessionTokenAccessor"/> to supply the bearer token to
    /// <c>IAuthenticatedRegistryClient</c>. NEVER returned to the browser.
    /// </summary>
    public required string PublishTokenJwt { get; init; }

    /// <summary>
    /// The <c>tokenId</c> returned by <c>POST /api/v1/auth/tokens</c>.
    /// Used at logout to call <c>DELETE /api/v1/auth/tokens/{tokenId}</c>.
    /// NEVER returned to the browser.
    /// </summary>
    public required string PublishTokenId { get; init; }

    /// <summary>UTC expiry of the publish token (and the session).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
