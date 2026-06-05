namespace Stash.Tests.Registry.Web.Fixtures;

/// <summary>
/// Synthetic fixture used by <see cref="SessionTokenLeakMetaTests"/> self-tests.
/// Provides known-bad and known-good snippet strings for the <c>PublishTokenJwt</c>
/// reference scanner and the JWT-in-body check, proving each has teeth.
/// </summary>
/// <remarks>
/// These are in-memory snippet strings — NOT actual files added to the Web project tree —
/// fed directly to the scan helper. No real JWT leak is introduced by this fixture.
/// </remarks>
internal static class SessionLeakFailPathFixture
{
    // ── Known-bad C# snippet: ViewData set to PublishTokenJwt ────────────────

    /// <summary>
    /// A C# snippet that deliberately references <c>BffSession.PublishTokenJwt</c>
    /// (or a property named <c>PublishTokenJwt</c>) in a page-model context.
    /// The Roslyn scanner MUST flag this as a violation.
    /// </summary>
    public const string RoguePublishTokenJwtReferenceInCSharp = """
        using Stash.Registry.Web.Auth;
        using Microsoft.AspNetCore.Mvc.RazorPages;

        // RoguePageModel leaks the publish JWT into ViewData — the scanner must flag this.
        public sealed class RoguePageModel : PageModel
        {
            private readonly ISessionTokenAccessor _accessor;

            public RoguePageModel(ISessionTokenAccessor accessor) => _accessor = accessor;

            public void OnGet()
            {
                _accessor.TryGetSession(out var session);
                // VIOLATION: PublishTokenJwt referenced in a page model
                ViewData["Token"] = session!.PublishTokenJwt;
            }
        }
        """;

    // ── Known-good C# snippet: no JWT property reference ─────────────────────

    /// <summary>
    /// A C# snippet that does NOT reference the JWT property on the session.
    /// The text scanner must NOT flag this as a violation.
    /// </summary>
    public const string CleanPageModelSnippet = """
        using Stash.Registry.Web.Auth;
        using Microsoft.AspNetCore.Mvc.RazorPages;

        // CleanPageModel only exposes the username — safe.
        public sealed class CleanPageModel : PageModel
        {
            private readonly ISessionTokenAccessor _accessor;

            public CleanPageModel(ISessionTokenAccessor accessor) => _accessor = accessor;

            public void OnGet()
            {
                _accessor.TryGetSession(out var session);
                // Only Username is exposed — safe.
                ViewData["Username"] = session?.Username;
            }
        }
        """;

    // ── Known-bad Razor snippet: PublishTokenJwt in .cshtml ──────────────────

    /// <summary>
    /// A <c>.cshtml</c>-like snippet that references <c>PublishTokenJwt</c>.
    /// The text-based scanner MUST flag this as a violation.
    /// </summary>
    public const string RoguePublishTokenJwtInRazor = """
        @page "/rogue"
        @model RogueModel
        @{
            // VIOLATION: PublishTokenJwt referenced in a Razor view
            var token = Model.Session.PublishTokenJwt;
        }
        <div>Token: @token</div>
        """;

    // ── Known-good Razor snippet: no PublishTokenJwt ──────────────────────────

    /// <summary>
    /// A <c>.cshtml</c>-like snippet that does NOT reference <c>PublishTokenJwt</c>.
    /// The text-based scanner must NOT flag this.
    /// </summary>
    public const string CleanRazorSnippet = """
        @page "/ok"
        @model OkModel
        <div>Welcome, @User.Identity?.Name</div>
        """;
}
