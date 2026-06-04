using Microsoft.AspNetCore.Mvc.RazorPages;
using Stash.Registry.Web.Auth;

namespace Stash.Registry.Web.Areas.Maintainer;

/// <summary>
/// Encapsulates all page conventions for the Maintainer area.
/// </summary>
/// <remarks>
/// <para>
/// <b>Auth gate (C1 backstop):</b> every page under <c>Areas/Maintainer/Pages/</c>
/// is covered by <c>[Authorize(AuthenticationSchemes = "BffCookie")]</c> via
/// <see cref="Microsoft.AspNetCore.Mvc.RazorPages.PageConventionCollection.AuthorizeAreaFolder"/>.
/// An anonymous request is 302'd to <c>/login?returnUrl=…</c> by
/// <see cref="SessionCookieAuthenticationHandler.HandleChallengeAsync"/> BEFORE the page
/// model is constructed — the <see cref="HttpAuthenticatedRegistryClient"/> DI factory's
/// <see cref="NoActiveSessionException"/> throw is therefore never reached on a normal
/// anonymous request.
/// </para>
/// <para>
/// The scheme name is read from <see cref="SessionCookie.AuthScheme"/> — never inlined.
/// </para>
/// </remarks>
public static class MaintainerAreaConventions
{
    /// <summary>
    /// The area name: single source of truth so conventions and routing agree.
    /// </summary>
    public const string AreaName = "Maintainer";

    /// <summary>
    /// Registers Maintainer area conventions on <paramref name="options"/>:
    /// <list type="bullet">
    ///   <item>Adds <c>[Authorize(AuthenticationSchemes = "BffCookie")]</c> to every
    ///   page under <c>/Areas/Maintainer/Pages/</c>.</item>
    ///   <item>Registers the area route so that <c>Dashboard.cshtml</c> is reachable
    ///   at <c>/dashboard</c> (not the default
    ///   <c>/Maintainer/Dashboard</c> path).</item>
    /// </list>
    /// </summary>
    public static void Apply(RazorPagesOptions options)
    {
        // Require authentication for every page in the Maintainer area.
        // Uses the default authorization policy (RequireAuthenticatedUser). Since the default
        // authentication scheme is BffCookie (set in AddAuthentication in Program.cs), no
        // explicit scheme parameter is needed — the auth handler resolves via the default scheme.
        // This convention 302s anonymous requests to /login?returnUrl=… BEFORE the page model
        // is constructed (the IAuthenticatedRegistryClient DI factory throw is never reached).
        options.Conventions.AuthorizeAreaFolder(AreaName, "/");
    }
}
