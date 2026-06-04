using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Html;

namespace Stash.Registry.Web.Rendering;

/// <summary>
/// Default implementation of <see cref="IReadmeRenderer"/>.
/// Converts package README Markdown to sanitized HTML using a two-layer defense:
/// <list type="number">
///   <item><b>Markdig</b> with <c>DisableHtml()</c> — raw HTML blocks and inline HTML
///   in the Markdown source are escaped to text, never passed through as live markup.
///   This means hostile HTML (e.g. <c>&lt;script&gt;</c>, <c>&lt;iframe&gt;</c>) never
///   reaches the sanitizer as actual tags.</item>
///   <item><b>HtmlSanitizer</b> (Ganss.Xss) — a conservative allow-list strips any
///   remaining unsafe content produced by Markdig's own extensions (e.g. a
///   <c>[click here](javascript:void(0))</c> link whose <c>href</c> has a dangerous scheme).</item>
/// </list>
/// </summary>
/// <remarks>
/// Safe to register as a singleton — both Markdig pipelines and HtmlSanitizer are thread-safe.
/// </remarks>
public sealed class ReadmeRenderer : IReadmeRenderer
{
    // ── Markdig pipeline — raw HTML disabled ──────────────────────────────────

    /// <summary>
    /// Markdig pipeline with raw-HTML pass-through explicitly disabled.
    /// <c>DisableHtml()</c> tells Markdig to escape (not emit) raw HTML blocks and
    /// inline HTML literals in the source, so package-injected HTML never reaches
    /// the sanitizer as executable markup.
    /// Only standard CommonMark extensions are active; no extensions that emit
    /// arbitrary raw HTML (e.g. raw HTML injection via custom renderers) are enabled.
    /// </summary>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()   // tables, task lists, footnotes, etc. — all safe CommonMark-level output
        .DisableHtml()             // FIRST LINE: raw HTML blocks/inlines become escaped text, not live tags
        .Build();

    // ── HtmlSanitizer — conservative allow-list ───────────────────────────────

    /// <summary>
    /// Configured <see cref="HtmlSanitizer"/> instance (thread-safe, singleton-safe).
    /// Conservative allow-list:
    /// <list type="bullet">
    ///   <item>Allowed schemes: <c>http</c>, <c>https</c>, <c>mailto</c> only — blocks <c>javascript:</c> and <c>data:</c>.</item>
    ///   <item>Allowed tags: structural/inline/code/table/list/heading elements only — no <c>&lt;script&gt;</c>, <c>&lt;iframe&gt;</c>, <c>&lt;object&gt;</c>, <c>&lt;embed&gt;</c>, etc.</item>
    ///   <item>Event handler attributes (<c>onclick</c>, <c>onerror</c>, etc.) are stripped by default in Ganss.Xss.</item>
    /// </list>
    /// </summary>
    private static readonly HtmlSanitizer Sanitizer = BuildSanitizer();

    private static HtmlSanitizer BuildSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        // ── Restrict URI schemes (SECOND LINE for javascript:/data: URIs) ─────
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");

        // ── Allow-list: only the tags Markdig legitimately emits ─────────────
        sanitizer.AllowedTags.Clear();

        // Structure
        sanitizer.AllowedTags.Add("p");
        sanitizer.AllowedTags.Add("br");
        sanitizer.AllowedTags.Add("hr");
        sanitizer.AllowedTags.Add("div");
        sanitizer.AllowedTags.Add("span");
        sanitizer.AllowedTags.Add("blockquote");

        // Headings
        sanitizer.AllowedTags.Add("h1");
        sanitizer.AllowedTags.Add("h2");
        sanitizer.AllowedTags.Add("h3");
        sanitizer.AllowedTags.Add("h4");
        sanitizer.AllowedTags.Add("h5");
        sanitizer.AllowedTags.Add("h6");

        // Inline formatting
        sanitizer.AllowedTags.Add("strong");
        sanitizer.AllowedTags.Add("b");
        sanitizer.AllowedTags.Add("em");
        sanitizer.AllowedTags.Add("i");
        sanitizer.AllowedTags.Add("s");
        sanitizer.AllowedTags.Add("del");
        sanitizer.AllowedTags.Add("ins");
        sanitizer.AllowedTags.Add("mark");
        sanitizer.AllowedTags.Add("sup");
        sanitizer.AllowedTags.Add("sub");

        // Code
        sanitizer.AllowedTags.Add("code");
        sanitizer.AllowedTags.Add("pre");
        sanitizer.AllowedTags.Add("kbd");
        sanitizer.AllowedTags.Add("samp");
        sanitizer.AllowedTags.Add("var");

        // Links and media
        sanitizer.AllowedTags.Add("a");
        sanitizer.AllowedTags.Add("img");

        // Lists
        sanitizer.AllowedTags.Add("ul");
        sanitizer.AllowedTags.Add("ol");
        sanitizer.AllowedTags.Add("li");

        // Tables
        sanitizer.AllowedTags.Add("table");
        sanitizer.AllowedTags.Add("thead");
        sanitizer.AllowedTags.Add("tbody");
        sanitizer.AllowedTags.Add("tfoot");
        sanitizer.AllowedTags.Add("tr");
        sanitizer.AllowedTags.Add("th");
        sanitizer.AllowedTags.Add("td");
        sanitizer.AllowedTags.Add("caption");
        sanitizer.AllowedTags.Add("colgroup");
        sanitizer.AllowedTags.Add("col");

        // Details / summary (used by Markdig advanced extensions)
        sanitizer.AllowedTags.Add("details");
        sanitizer.AllowedTags.Add("summary");

        // Definition lists
        sanitizer.AllowedTags.Add("dl");
        sanitizer.AllowedTags.Add("dt");
        sanitizer.AllowedTags.Add("dd");

        // Figure
        sanitizer.AllowedTags.Add("figure");
        sanitizer.AllowedTags.Add("figcaption");

        // ── Allow safe attributes ──────────────────────────────────────────────
        // Ganss.Xss strips all event handler attributes (onclick, onerror, onload, etc.)
        // by default. We explicitly remove 'style' to prevent CSS injection.
        sanitizer.AllowedAttributes.Remove("style");

        return sanitizer;
    }

    // ── IReadmeRenderer implementation ────────────────────────────────────────

    /// <inheritdoc />
    public HtmlString RenderToSafeHtml(string markdown)
    {
        // Step 1 — Markdig: Markdown → HTML with raw-HTML passthrough disabled.
        // Any raw HTML in the source is escaped to &lt;...&gt; text, never emitted as live tags.
        string rawHtml = Markdown.ToHtml(markdown, Pipeline);

        // Step 2 — HtmlSanitizer: strip dangerous schemes/attributes from Markdig's output.
        // javascript:/data: URIs in links (e.g. [x](javascript:void(0))) are removed here.
        string safeHtml = Sanitizer.Sanitize(rawHtml);

        return new HtmlString(safeHtml);
    }
}
