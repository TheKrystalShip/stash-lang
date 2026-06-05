using Microsoft.AspNetCore.Mvc.RazorPages;
using Stash.Registry.Web.Auth;

namespace Stash.Registry.Web.Areas.Maintainer;

/// <summary>
/// Encapsulates all page conventions for the Maintainer area.
/// </summary>
/// <remarks>
/// <para>
/// <b>Auth gate (C1 backstop):</b> every page under <c>Areas/Maintainer/Pages/</c>
/// is covered by the named policy <see cref="BffCookieAuthedPolicy"/> via
/// <see cref="Microsoft.AspNetCore.Mvc.RazorPages.PageConventionCollection.AuthorizeAreaFolder"/>.
/// The policy pins <see cref="SessionCookie.AuthScheme"/> explicitly, so the auth gate
/// does not shift if a second scheme is registered in the future.
/// An anonymous request is 302'd to <c>/login?returnUrl=…</c> by
/// <see cref="SessionCookieAuthenticationHandler.HandleChallengeAsync"/> BEFORE the page
/// model is constructed — the <see cref="HttpAuthenticatedRegistryClient"/> DI factory's
/// <see cref="NoActiveSessionException"/> throw is therefore never reached on a normal
/// anonymous request.
/// </para>
/// <para>
/// The policy name is read from <see cref="BffCookieAuthedPolicy"/> and the scheme name
/// from <see cref="SessionCookie.AuthScheme"/> — neither is inlined.
/// </para>
/// </remarks>
public static class MaintainerAreaConventions
{
    /// <summary>
    /// The area name: single source of truth so conventions and routing agree.
    /// </summary>
    public const string AreaName = "Maintainer";

    /// <summary>
    /// Name of the authorization policy that pins the BFF cookie scheme for the Maintainer
    /// area. Register this policy in <c>Program.cs</c> via
    /// <c>services.AddAuthorization(o =&gt; o.AddPolicy(BffCookieAuthedPolicy, …))</c>,
    /// setting <see cref="AuthorizationPolicyBuilder.AuthenticationSchemes"/> to
    /// <see cref="SessionCookie.AuthScheme"/>.
    /// </summary>
    public const string BffCookieAuthedPolicy = "BffCookieAuthed";

    /// <summary>
    /// Registers Maintainer area conventions on <paramref name="options"/>:
    /// <list type="bullet">
    ///   <item>Applies the <see cref="BffCookieAuthedPolicy"/> named policy to every
    ///   page under <c>/Areas/Maintainer/Pages/</c>, pinning the BFF cookie scheme
    ///   explicitly.</item>
    ///   <item>Registers the area route so that <c>Dashboard.cshtml</c> is reachable
    ///   at <c>/dashboard</c> (not the default
    ///   <c>/Maintainer/Dashboard</c> path).</item>
    /// </list>
    /// </summary>
    public static void Apply(RazorPagesOptions options)
    {
        // Require authentication for every page in the Maintainer area using the named
        // BffCookieAuthed policy. The policy explicitly pins SessionCookie.AuthScheme so the
        // auth gate is stable even if a second scheme is added later.
        // This convention 302s anonymous requests to /login?returnUrl=… BEFORE the page model
        // is constructed (the IAuthenticatedRegistryClient DI factory throw is never reached).
        options.Conventions.AuthorizeAreaFolder(AreaName, "/", BffCookieAuthedPolicy);
    }
}
