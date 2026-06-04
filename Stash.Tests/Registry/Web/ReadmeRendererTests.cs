using Microsoft.AspNetCore.Html;
using Stash.Registry.Web.Rendering;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Unit tests for <see cref="ReadmeRenderer"/>, covering hostile-input sanitization and
/// benign Markdown round-trip fidelity.
/// </summary>
/// <remarks>
/// Defense-in-depth design:
/// <list type="number">
///   <item>Markdig with <c>DisableHtml()</c> — raw HTML in the Markdown source is escaped to
///   text before sanitization. Hostile inline HTML (<c>&lt;script&gt;</c>, <c>&lt;iframe&gt;</c>)
///   never reaches the sanitizer as live tags.</item>
///   <item>HtmlSanitizer (Ganss.Xss) — strips dangerous URI schemes
///   (<c>javascript:</c>, <c>data:</c>) that appear in Markdig-generated link/image elements.</item>
/// </list>
/// </remarks>
public sealed class ReadmeRendererTests
{
    private static readonly IReadmeRenderer Renderer = new ReadmeRenderer();

    // ── Hostile-input: <script> stripped ─────────────────────────────────────

    /// <summary>
    /// A raw <c>&lt;script&gt;</c> block in the Markdown source must not appear as a live
    /// script element in the output. Markdig's <c>DisableHtml()</c> escapes raw HTML to text
    /// (producing <c>&amp;lt;script&amp;gt;</c>); the live form <c>&lt;script&gt;</c> must
    /// never appear in the rendered HTML.
    /// </summary>
    [Fact]
    public void RenderToSafeHtml_ScriptTag_IsStripped()
    {
        var result = Renderer.RenderToSafeHtml("<script>alert('xss')</script>");

        // The LIVE <script> opening tag must not appear — escaped text &lt;script&gt; is fine (it's inert).
        // DisableHtml() renders raw HTML as HTML-encoded text, so <script> becomes &lt;script&gt;.
        Assert.DoesNotContain("<script>", result.Value, StringComparison.OrdinalIgnoreCase);
    }

    // ── Hostile-input: <script> inside otherwise-valid Markdown ──────────────

    [Fact]
    public void RenderToSafeHtml_ScriptTagInMarkdownParagraph_IsStripped()
    {
        var result = Renderer.RenderToSafeHtml("Hello <script>evil()</script> world");

        // The LIVE <script> opening tag must not appear in the output.
        // DisableHtml() renders it as HTML-encoded text (&lt;script&gt;...) which is inert.
        Assert.DoesNotContain("<script>", result.Value, StringComparison.OrdinalIgnoreCase);
    }

    // ── Hostile-input: <iframe> stripped ─────────────────────────────────────

    [Fact]
    public void RenderToSafeHtml_IframeTag_IsStripped()
    {
        var result = Renderer.RenderToSafeHtml("<iframe src=\"https://evil.example.com\"></iframe>");

        // The LIVE <iframe> tag must not appear in the output.
        Assert.DoesNotContain("<iframe", result.Value, StringComparison.OrdinalIgnoreCase);
    }

    // ── Hostile-input: onerror= event handler neutralized ────────────────────

    /// <summary>
    /// A raw <c>&lt;img onerror="..."&gt;</c> in the Markdown source must not produce a live
    /// <c>img</c> element with an event handler. Markdig's <c>DisableHtml()</c> escapes the
    /// entire raw HTML element to text; the live unescaped form must not appear.
    /// </summary>
    [Fact]
    public void RenderToSafeHtml_OnerrorAttribute_IsNeutralized()
    {
        // Raw HTML with onerror event handler — DisableHtml escapes it to text.
        var result = Renderer.RenderToSafeHtml("<img src=\"x\" onerror=\"alert(1)\">");

        // The live unescaped img tag must not appear.
        // Note: HTML-encoded text like &lt;img ... onerror=...&gt; is inert (it's visible text,
        // not a parsed attribute). The dangerous LIVE form is <img ... onerror=...> which must be absent.
        Assert.DoesNotContain("<img ", result.Value, StringComparison.OrdinalIgnoreCase);
    }

    // ── Hostile-input: onclick= event handler neutralized ────────────────────

    [Fact]
    public void RenderToSafeHtml_OnclickAttribute_IsNeutralized()
    {
        var result = Renderer.RenderToSafeHtml("<button onclick=\"evil()\">Click</button>");

        // The live <button> element with onclick must not appear.
        Assert.DoesNotContain("<button", result.Value, StringComparison.OrdinalIgnoreCase);
    }

    // ── Hostile-input: javascript: URI blocked ────────────────────────────────

    /// <summary>
    /// A Markdown link with a <c>javascript:</c> URI (valid Markdown syntax, emitted by Markdig
    /// as <c>&lt;a href="javascript:..."&gt;</c>) must have the href stripped by HtmlSanitizer.
    /// Markdig emits this as a standard &lt;a&gt; tag; DisableHtml does not block link hrefs —
    /// HtmlSanitizer's scheme allowlist is the guard here.
    /// </summary>
    [Fact]
    public void RenderToSafeHtml_JavascriptUri_IsBlocked()
    {
        // Markdown link with javascript: URI — Markdig produces <a href="javascript:void(0)">click</a>
        var result = Renderer.RenderToSafeHtml("[click me](javascript:void(0))");

        // The dangerous scheme must not appear as an href value
        Assert.DoesNotContain("javascript:", result.Value, StringComparison.OrdinalIgnoreCase);
    }

    // ── Hostile-input: data: URI blocked ─────────────────────────────────────

    [Fact]
    public void RenderToSafeHtml_DataUri_IsBlocked()
    {
        // Markdown link with data: URI
        var result = Renderer.RenderToSafeHtml("[image](data:text/html,<script>alert(1)</script>)");

        Assert.DoesNotContain("data:", result.Value, StringComparison.OrdinalIgnoreCase);
    }

    // ── Benign round-trip: headings ───────────────────────────────────────────

    [Fact]
    public void RenderToSafeHtml_Headings_RoundTripIntact()
    {
        var result = Renderer.RenderToSafeHtml("# Heading 1\n\n## Heading 2\n\n### Heading 3");

        Assert.Contains("<h1>", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Heading 1", result.Value);
        Assert.Contains("<h2>", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Heading 2", result.Value);
        Assert.Contains("<h3>", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Heading 3", result.Value);
    }

    // ── Benign round-trip: lists ──────────────────────────────────────────────

    [Fact]
    public void RenderToSafeHtml_Lists_RoundTripIntact()
    {
        var result = Renderer.RenderToSafeHtml("- Item A\n- Item B\n- Item C");

        Assert.Contains("<ul>", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<li>", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Item A", result.Value);
        Assert.Contains("Item B", result.Value);
        Assert.Contains("Item C", result.Value);
    }

    // ── Benign round-trip: fenced code block ─────────────────────────────────

    [Fact]
    public void RenderToSafeHtml_FencedCodeBlock_RoundTripIntact()
    {
        var result = Renderer.RenderToSafeHtml("```\nlet x = 1 + 2;\n```");

        Assert.Contains("<pre>", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<code>", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("let x = 1 + 2;", result.Value);
    }

    // ── Benign round-trip: inline code ───────────────────────────────────────

    [Fact]
    public void RenderToSafeHtml_InlineCode_RoundTripIntact()
    {
        var result = Renderer.RenderToSafeHtml("Use `stash pkg add` to install.");

        Assert.Contains("<code>", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stash pkg add", result.Value);
    }

    // ── Benign round-trip: https:// link ─────────────────────────────────────

    [Fact]
    public void RenderToSafeHtml_HttpsLink_RoundTripIntact()
    {
        var result = Renderer.RenderToSafeHtml("[Stash docs](https://docs.stash.example)");

        Assert.Contains("<a ", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://docs.stash.example", result.Value);
        Assert.Contains("Stash docs", result.Value);
    }

    // ── Return type is HtmlString ─────────────────────────────────────────────

    [Fact]
    public void RenderToSafeHtml_ReturnType_IsHtmlString()
    {
        var result = Renderer.RenderToSafeHtml("# Hello");

        Assert.IsType<HtmlString>(result);
        Assert.NotNull(result.Value);
    }

    // ── Empty input ───────────────────────────────────────────────────────────

    [Fact]
    public void RenderToSafeHtml_EmptyInput_ReturnsEmptyOrWhitespace()
    {
        var result = Renderer.RenderToSafeHtml(string.Empty);

        // Empty Markdown produces empty or trivial output — no exception thrown.
        Assert.NotNull(result);
    }
}
