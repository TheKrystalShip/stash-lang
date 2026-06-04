namespace Stash.Tests.Registry.Web.Fixtures;

/// <summary>
/// Synthetic fixture used by <see cref="ReadmeChokepointMetaTests"/> self-tests.
/// Provides known-bad and known-good <c>.cshtml</c> snippet strings that exercise the
/// <c>@Html.Raw</c> chokepoint scanner, proving it has teeth and does not pass vacuously.
/// </summary>
/// <remarks>
/// These are in-memory snippet strings — NOT actual <c>.cshtml</c> files on disk —
/// fed directly to the scan helper. No real <c>@Html.Raw</c> violations are introduced
/// into the project source tree by this fixture.
/// </remarks>
internal static class ChokepointFailPathFixture
{
    // ── Known-bad snippet: rogue @Html.Raw(someString) ───────────────────────

    /// <summary>
    /// A <c>.cshtml</c> snippet containing a rogue <c>@Html.Raw(someString)</c> —
    /// the argument is a plain string variable, NOT sourced from <c>IReadmeRenderer.RenderToSafeHtml</c>
    /// and NOT typed as <c>HtmlString</c>.
    /// The chokepoint scanner MUST flag this as a violation.
    /// </summary>
    public const string RogueHtmlRawSnippet = """
        @page "/bad"
        @model BadModel
        <div>
            @Html.Raw(someUserControlledString)
        </div>
        """;

    // ── Known-safe snippet: @Html.Raw(Model.ReadmeHtml) via HtmlString ───────

    /// <summary>
    /// A <c>.cshtml</c> snippet whose <c>@Html.Raw</c> argument is a model property
    /// typed as <c>HtmlString</c> — the safe, approved form.
    /// The chokepoint scanner must NOT flag this as a violation.
    /// </summary>
    public const string SafeHtmlStringPropertySnippet = """
        @page "/ok"
        @model OkModel
        @using Microsoft.AspNetCore.Html
        <div>
            @Html.Raw(Model.ReadmeHtml)
        </div>
        """;

    // ── Known-safe snippet: inline RenderToSafeHtml call ─────────────────────

    /// <summary>
    /// A <c>.cshtml</c> snippet whose <c>@Html.Raw</c> argument is a direct call to
    /// <c>renderer.RenderToSafeHtml(content)</c> — the other safe, approved form.
    /// The chokepoint scanner must NOT flag this as a violation.
    /// </summary>
    public const string SafeRenderToSafeHtmlCallSnippet = """
        @page "/also-ok"
        @model AlsoOkModel
        @inject IReadmeRenderer renderer
        <div>
            @Html.Raw(renderer.RenderToSafeHtml(Model.Content))
        </div>
        """;
}
