using System;

namespace Stash.Registry.Web.Auth;

/// <summary>
/// Thrown by <see cref="CookieSessionTokenAccessor"/> when an authenticated registry
/// call is attempted but no active session is present in scope.
/// </summary>
/// <remarks>
/// This is the <em>fail-closed backstop</em> for the DI chokepoint — it is defence-in-depth
/// and should never be triggered on a normal anonymous request. The real auth gate is the
/// <see cref="SessionCookieAuthenticationHandler"/> + the <c>[Authorize]</c> page conventions,
/// which 302-redirect anonymous users to <c>/login</c> before the page model is constructed
/// and before <c>IAuthenticatedRegistryClient</c> is ever resolved.
/// </remarks>
public sealed class NoActiveSessionException : InvalidOperationException
{
    /// <inheritdoc/>
    public NoActiveSessionException()
        : base(
            "No active BFF session is present in the current request scope. " +
            "This should never be reached on a normal anonymous request — the " +
            "SessionCookieAuthenticationHandler + [Authorize] convention should have " +
            "redirected to /login before the authenticated registry client was resolved.")
    {
    }
}
