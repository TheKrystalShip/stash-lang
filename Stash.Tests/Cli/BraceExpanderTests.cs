using System.Collections.Generic;
using Stash.Cli.Shell;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// Unit tests for <see cref="BraceExpander.Expand"/> covering the §6 step 2
/// brace-expansion rules.
/// </summary>
public class BraceExpanderTests
{
    // ── No-op cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Expand_EmptyInput_ReturnsEmpty()
    {
        var result = BraceExpander.Expand("");
        Assert.Equal(new[] { "" }, result);
    }

    [Fact]
    public void Expand_NoBraces_ReturnsInputUnchanged()
    {
        var result = BraceExpander.Expand("hello");
        Assert.Equal(new[] { "hello" }, result);
    }

    [Fact]
    public void Expand_NoBraces_WithHyphen_ReturnsInputUnchanged()
    {
        var result = BraceExpander.Expand("a-b-c");
        Assert.Equal(new[] { "a-b-c" }, result);
    }

    // ── Single-element and empty braces (literal passthrough) ─────────────────

    [Fact]
    public void Expand_SingleElementBrace_Literal()
    {
        // {a} — no comma — stays literal.
        var result = BraceExpander.Expand("{a}");
        Assert.Equal(new[] { "{a}" }, result);
    }

    [Fact]
    public void Expand_EmptyBrace_Literal()
    {
        var result = BraceExpander.Expand("{}");
        Assert.Equal(new[] { "{}" }, result);
    }

    [Fact]
    public void Expand_NoCommaInBrace_Literal()
    {
        var result = BraceExpander.Expand("file{txt}");
        Assert.Equal(new[] { "file{txt}" }, result);
    }

    [Fact]
    public void Expand_BraceRangeNotSupported_Literal()
    {
        // {1..5} — range syntax is not supported in v1.
        var result = BraceExpander.Expand("{1..5}");
        Assert.Equal(new[] { "{1..5}" }, result);
    }

    // ── Basic expansion ───────────────────────────────────────────────────────

    [Fact]
    public void Expand_SimpleBrace_ProducesAllAlternatives()
    {
        var result = BraceExpander.Expand("a{1,2,3}b");
        Assert.Equal(new[] { "a1b", "a2b", "a3b" }, result);
    }

    [Fact]
    public void Expand_NoPrefixOrSuffix_ProducesAlternatives()
    {
        var result = BraceExpander.Expand("{a,b,c}");
        Assert.Equal(new[] { "a", "b", "c" }, result);
    }

    [Fact]
    public void Expand_PrefixOnly()
    {
        var result = BraceExpander.Expand("file.{txt,bak}");
        Assert.Equal(new[] { "file.txt", "file.bak" }, result);
    }

    [Fact]
    public void Expand_SuffixOnly()
    {
        var result = BraceExpander.Expand("{pre,post}fix");
        Assert.Equal(new[] { "prefix", "postfix" }, result);
    }

    // ── Empty alternatives ────────────────────────────────────────────────────

    [Fact]
    public void Expand_TrailingEmptyAlternative()
    {
        // {a,} → "a" and ""
        var result = BraceExpander.Expand("{a,}");
        Assert.Equal(new[] { "a", "" }, result);
    }

    [Fact]
    public void Expand_LeadingEmptyAlternative()
    {
        // {,a} → "" and "a"
        var result = BraceExpander.Expand("{,a}");
        Assert.Equal(new[] { "", "a" }, result);
    }

    [Fact]
    public void Expand_BothEmpty()
    {
        // {,} → "" and ""
        var result = BraceExpander.Expand("{,}");
        Assert.Equal(new[] { "", "" }, result);
    }

    // ── Unbalanced braces (literal passthrough) ───────────────────────────────

    [Fact]
    public void Expand_UnbalancedOpenBrace_Literal()
    {
        // No closing '}' — the whole string is returned as-is.
        var result = BraceExpander.Expand("{a,b");
        Assert.Equal(new[] { "{a,b" }, result);
    }

    [Fact]
    public void Expand_UnbalancedCloseBrace_Literal()
    {
        // No '{' at all.
        var result = BraceExpander.Expand("a,b}");
        Assert.Equal(new[] { "a,b}" }, result);
    }

    // ── Cross-product (multiple brace groups) ─────────────────────────────────

    [Fact]
    public void Expand_MultipleBraces_CrossProduct()
    {
        var result = BraceExpander.Expand("{a,b}-{1,2}");
        Assert.Equal(new[] { "a-1", "a-2", "b-1", "b-2" }, result);
    }

    [Fact]
    public void Expand_AdjacentBraces_NoSeparator()
    {
        var result = BraceExpander.Expand("{a,b}{1,2}");
        Assert.Equal(new[] { "a1", "a2", "b1", "b2" }, result);
    }

    [Fact]
    public void Expand_PrefixSuffix_CrossProduct()
    {
        // pre{a,b}suf{1,2}end → 4 outputs
        var result = BraceExpander.Expand("pre{a,b}suf{1,2}end");
        Assert.Equal(new[] { "preasuf1end", "preasuf2end", "prebsuf1end", "prebsuf2end" }, result);
    }

    // ── Nested braces ─────────────────────────────────────────────────────────

    [Fact]
    public void Expand_NestedBraces_FlattensCorrectly()
    {
        var result = BraceExpander.Expand("{a,{b,c}}");
        Assert.Equal(new[] { "a", "b", "c" }, result);
    }

    [Fact]
    public void Expand_NestedWithSurroundingText()
    {
        var result = BraceExpander.Expand("x{a,{b,c}}y");
        Assert.Equal(new[] { "xay", "xby", "xcy" }, result);
    }

    [Fact]
    public void Expand_OnlyTopLevelCommasSplit()
    {
        // {a,{b,c},d} → outer splits: "a", "{b,c}", "d"
        // then "{b,c}" recursively → "b", "c"
        // result: ["a", "b", "c", "d"]
        var result = BraceExpander.Expand("{a,{b,c},d}");
        Assert.Equal(new[] { "a", "b", "c", "d" }, result);
    }
}
