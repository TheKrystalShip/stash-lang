using Microsoft.AspNetCore.Html;

namespace Stash.Registry.Web.Rendering;

/// <summary>
/// The single rendering chokepoint for package README content.
/// All Markdown-to-HTML conversion for package-authored content must flow through this interface.
/// </summary>
/// <remarks>
/// <para>
/// Package-authored README content is treated as hostile input. The pipeline:
/// <list type="number">
///   <item>Markdig: parse Markdown to HTML with raw-HTML passthrough disabled (inline HTML is escaped).</item>
///   <item>HtmlSanitizer: strip any remaining unsafe content — <c>&lt;script&gt;</c>, <c>&lt;iframe&gt;</c>,
///   inline event handlers (<c>onerror=</c>, <c>onclick=</c>, etc.), <c>javascript:</c> URIs,
///   <c>data:</c> URIs — and return a sanitized <see cref="HtmlString"/>.</item>
/// </list>
/// </para>
/// <para>
/// <b>This is the ONLY method that may produce an <see cref="HtmlString"/> for use with
/// <c>@Html.Raw(...)</c> in Razor views.</b> Any other <c>@Html.Raw()</c> call site is a
/// violation caught by <c>ReadmeChokepointMetaTests</c>.
/// </para>
/// </remarks>
public interface IReadmeRenderer
{
    /// <summary>
    /// Converts <paramref name="markdown"/> to sanitized HTML safe for direct rendering.
    /// </summary>
    /// <param name="markdown">Raw Markdown text (package-authored, treated as hostile).</param>
    /// <returns>A sanitized <see cref="HtmlString"/> ready for <c>@Html.Raw()</c> in a Razor view.</returns>
    HtmlString RenderToSafeHtml(string markdown);
}
