using Stash.Cli.Completion;

namespace Stash.Tests.Cli.Completion;

/// <summary>
/// Unit tests for <see cref="TokenAtCursor"/> covering token detection, quote tracking,
/// substitution detection, and comment suppression per spec §15.2.
/// Note: glob/brace short-circuiting is NOT done by TokenAtCursor — that is Phase 3
/// in the completion engine. These tests verify token detection and context flags only.
/// </summary>
public class TokenAtCursorTests
{
    // ── Basic shell-mode token detection ─────────────────────────────────────

    [Fact]
    public void ShellMode_TokenAfterSpace_DetectedCorrectly()
    {
        // Buffer="git ch", cursor=6 → token="ch", replace_start=4, replace_end=6
        var ctx = TokenAtCursor.Probe("git ch", 6, CompletionMode.Shell);
        Assert.Equal(CompletionMode.Shell, ctx.Mode);
        Assert.Equal("ch", ctx.TokenText);
        Assert.Equal(4, ctx.ReplaceStart);
        Assert.Equal(6, ctx.ReplaceEnd);
        Assert.False(ctx.InQuote);
        Assert.False(ctx.InSubstitution);
    }

    [Fact]
    public void ShellMode_FirstToken_FullBuffer()
    {
        // Buffer="git", cursor=3 → token="git", replace_start=0
        var ctx = TokenAtCursor.Probe("git", 3, CompletionMode.Shell);
        Assert.Equal("git", ctx.TokenText);
        Assert.Equal(0, ctx.ReplaceStart);
        Assert.Equal(3, ctx.ReplaceEnd);
    }

    [Fact]
    public void ShellMode_TokenWithTilde()
    {
        // Buffer="cd ~/", cursor=5 → token="~/", replace_start=3
        var ctx = TokenAtCursor.Probe("cd ~/", 5, CompletionMode.Shell);
        Assert.Equal(CompletionMode.Shell, ctx.Mode);
        Assert.Equal("~/", ctx.TokenText);
        Assert.Equal(3, ctx.ReplaceStart);
        Assert.Equal(5, ctx.ReplaceEnd);
    }

    [Fact]
    public void ShellMode_TokenWithBrace_DetectedAsTokenContainingBrace()
    {
        // Buffer="cp file.{txt", cursor=12 → token="file.{txt"
        // TokenAtCursor does NOT short-circuit on glob chars — that's Phase 3.
        var ctx = TokenAtCursor.Probe("cp file.{txt", 12, CompletionMode.Shell);
        Assert.Equal(CompletionMode.Shell, ctx.Mode);
        Assert.Equal("file.{txt", ctx.TokenText);
        Assert.Equal(3, ctx.ReplaceStart);
        Assert.Equal(12, ctx.ReplaceEnd);
        Assert.False(ctx.InQuote);
    }

    [Fact]
    public void ShellMode_PipeStage_TokenAfterPipe()
    {
        // Buffer="cat foo | gr", cursor=12 → token="gr", replace_start=10
        var ctx = TokenAtCursor.Probe("cat foo | gr", 12, CompletionMode.Shell);
        Assert.Equal("gr", ctx.TokenText);
        Assert.Equal(10, ctx.ReplaceStart);
    }

    [Fact]
    public void ShellMode_TokenAfterRedirect()
    {
        // Buffer="echo x > fi", cursor=12 → token="fi"
        var ctx = TokenAtCursor.Probe("echo x > fi", 11, CompletionMode.Shell);
        Assert.Equal("fi", ctx.TokenText);
    }

    [Fact]
    public void ShellMode_EscapedSpace_PartOfToken()
    {
        // Buffer="cp My\ ", cursor=7 — the \ escapes the space so it's part of the word.
        // tokenStart stays at 3 (after "cp "), and "My\ " is the token.
        var ctx = TokenAtCursor.Probe(@"cp My\ ", 7, CompletionMode.Shell);
        Assert.Equal("My\\ ", ctx.TokenText);
        Assert.Equal(3, ctx.ReplaceStart);
    }

    // ── Substitution detection ────────────────────────────────────────────────

    [Fact]
    public void Substitution_InsideDollarBrace_DetectedCorrectly()
    {
        // Buffer="echo ${env.HO}", cursor=13 (before the closing })
        // The ${  starts at index 5, inner content starts at 7.
        // At cursor=13, we've seen "echo ${env.HO" — dollarDepth=1.
        var ctx = TokenAtCursor.Probe("echo ${env.HO}", 13, CompletionMode.Shell);
        Assert.True(ctx.InSubstitution);
        Assert.Equal(CompletionMode.Substitution, ctx.Mode);
        Assert.Equal("env.HO", ctx.TokenText);
        Assert.Equal(7, ctx.ReplaceStart);
        Assert.Equal(13, ctx.ReplaceEnd);
        Assert.False(ctx.InQuote);
    }

    [Fact]
    public void Substitution_ClosedBraceBeforeCursor_NotSubstitution()
    {
        // Buffer="echo ${env.HOME} more", cursor=21 — the ${ is closed.
        var ctx = TokenAtCursor.Probe("echo ${env.HOME} more", 21, CompletionMode.Shell);
        Assert.False(ctx.InSubstitution);
        Assert.Equal("more", ctx.TokenText);
    }

    [Fact]
    public void Substitution_DollarBraceInStashMode()
    {
        // Even in Stash mode a ${...} substitution is detected.
        var ctx = TokenAtCursor.Probe("let x = ${foo.ba", 16, CompletionMode.Stash);
        Assert.True(ctx.InSubstitution);
        Assert.Equal(CompletionMode.Substitution, ctx.Mode);
        Assert.Equal("foo.ba", ctx.TokenText);
    }

    // ── Quote detection ───────────────────────────────────────────────────────

    [Fact]
    public void Quote_InsideDoubleQuote_Detected()
    {
        // Buffer=`ls "/usr/lo`, cursor=11 → in_quote=true, quote_char='"', token="/usr/lo"
        var ctx = TokenAtCursor.Probe("ls \"/usr/lo", 11, CompletionMode.Shell);
        Assert.True(ctx.InQuote);
        Assert.Equal('"', ctx.QuoteChar);
        Assert.Equal("/usr/lo", ctx.TokenText);
        Assert.Equal(4, ctx.ReplaceStart);
        Assert.Equal(11, ctx.ReplaceEnd);
    }

    [Fact]
    public void Quote_InsideSingleQuote_Detected()
    {
        // Buffer="echo 'foo/b", cursor=11 → in_quote=true, quote_char='\''
        var ctx = TokenAtCursor.Probe("echo 'foo/b", 11, CompletionMode.Shell);
        Assert.True(ctx.InQuote);
        Assert.Equal('\'', ctx.QuoteChar);
        Assert.Equal("foo/b", ctx.TokenText);
        Assert.Equal(6, ctx.ReplaceStart);
    }

    [Fact]
    public void Quote_ClosedDoubleQuote_NotInQuote()
    {
        // Buffer=`echo "foo" bar`, cursor=14 — quote is closed.
        var ctx = TokenAtCursor.Probe("echo \"foo\" bar", 14, CompletionMode.Shell);
        Assert.False(ctx.InQuote);
        Assert.Equal("bar", ctx.TokenText);
    }

    // ── Comment suppression ───────────────────────────────────────────────────

    [Fact]
    public void Comment_LineComment_ReturnsEmpty()
    {
        // Buffer="// comment xy", cursor=13 → Mode=None, no completion.
        var ctx = TokenAtCursor.Probe("// comment xy", 13, CompletionMode.Stash);
        Assert.Equal(CompletionMode.None, ctx.Mode);
        Assert.Equal(CursorContext.Empty, ctx);
    }

    [Fact]
    public void Comment_LineCommentMidBuffer_ReturnsEmpty()
    {
        // Stash mode: "let x = 5 // comment here", cursor=24
        var ctx = TokenAtCursor.Probe("let x = 5 // comment here", 24, CompletionMode.Stash);
        Assert.Equal(CompletionMode.None, ctx.Mode);
    }

    [Fact]
    public void Comment_BlockComment_CursorInside_ReturnsEmpty()
    {
        // Buffer="/* hello", cursor=8 — inside unclosed block comment.
        var ctx = TokenAtCursor.Probe("/* hello", 8, CompletionMode.Stash);
        Assert.Equal(CompletionMode.None, ctx.Mode);
    }

    [Fact]
    public void Comment_ClosedBlockComment_NotSuppressed()
    {
        // Buffer="/* x */ foo", cursor=11 — block comment is closed; "foo" should complete.
        var ctx = TokenAtCursor.Probe("/* x */ foo", 11, CompletionMode.Stash);
        Assert.NotEqual(CompletionMode.None, ctx.Mode);
        Assert.Equal("foo", ctx.TokenText);
    }

    // ── Stash-mode token detection ────────────────────────────────────────────

    [Fact]
    public void StashMode_IdentifierToken_Detected()
    {
        // Buffer="pri", cursor=3 → token="pri"
        var ctx = TokenAtCursor.Probe("pri", 3, CompletionMode.Stash);
        Assert.Equal(CompletionMode.Stash, ctx.Mode);
        Assert.Equal("pri", ctx.TokenText);
        Assert.Equal(0, ctx.ReplaceStart);
        Assert.Equal(3, ctx.ReplaceEnd);
    }

    [Fact]
    public void StashMode_DottedToken_IncludesDot()
    {
        // Buffer="fs.exi", cursor=6 → token="fs.exi"
        var ctx = TokenAtCursor.Probe("fs.exi", 6, CompletionMode.Stash);
        Assert.Equal("fs.exi", ctx.TokenText);
        Assert.Equal(0, ctx.ReplaceStart);
    }

    [Fact]
    public void StashMode_TokenAfterOperator()
    {
        // Buffer="let x = pri", cursor=11 → token="pri"
        var ctx = TokenAtCursor.Probe("let x = pri", 11, CompletionMode.Stash);
        Assert.Equal("pri", ctx.TokenText);
        Assert.Equal(8, ctx.ReplaceStart);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyBuffer_ReturnsEmptyToken()
    {
        var ctx = TokenAtCursor.Probe(string.Empty, 0, CompletionMode.Stash);
        Assert.Equal(string.Empty, ctx.TokenText);
        Assert.Equal(0, ctx.ReplaceStart);
        Assert.Equal(0, ctx.ReplaceEnd);
    }

    [Fact]
    public void CursorAtZero_ReturnsEmptyToken()
    {
        var ctx = TokenAtCursor.Probe("git status", 0, CompletionMode.Shell);
        Assert.Equal(string.Empty, ctx.TokenText);
        Assert.Equal(0, ctx.ReplaceStart);
        Assert.Equal(0, ctx.ReplaceEnd);
    }

    [Fact]
    public void CursorBeyondBuffer_ClampedToLength()
    {
        // Cursor beyond end should behave as cursor=length.
        var ctx = TokenAtCursor.Probe("foo", 100, CompletionMode.Shell);
        Assert.Equal("foo", ctx.TokenText);
        Assert.Equal(0, ctx.ReplaceStart);
        Assert.Equal(3, ctx.ReplaceEnd);
    }

    [Fact]
    public void PriorArgs_AlwaysEmptyInPhase1()
    {
        // Phase 2 will populate PriorArgs via ArgExpander.
        var ctx = TokenAtCursor.Probe("git commit --me", 15, CompletionMode.Shell);
        Assert.Empty(ctx.PriorArgs);
    }
}
