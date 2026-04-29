using System.Linq;
using Stash.Cli.Shell;

namespace Stash.Tests.Cli;

/// <summary>
/// Unit tests for <see cref="ShellLineLexer"/> — shell line AST construction.
/// </summary>
public class ShellLineLexerTests
{
    // ── Single-stage commands ─────────────────────────────────────────────────

    [Fact]
    public void Parse_SimpleCommand_ExtractsProgramAndArgs()
    {
        var ast = ShellLineLexer.Parse("git status --short");
        Assert.Single(ast.Stages);
        Assert.Equal("git", ast.Stages[0].Program);
        Assert.Equal("status --short", ast.Stages[0].RawArgs.Trim());
    }

    [Fact]
    public void Parse_NoArgs_EmptyRawArgs()
    {
        var ast = ShellLineLexer.Parse("ls");
        Assert.Single(ast.Stages);
        Assert.Equal("ls", ast.Stages[0].Program);
        Assert.Equal("", ast.Stages[0].RawArgs.Trim());
    }

    [Fact]
    public void Parse_CommandWithQuotedArg_PreservesQuotes()
    {
        var ast = ShellLineLexer.Parse("echo \"hello world\"");
        Assert.Single(ast.Stages);
        Assert.Equal("echo", ast.Stages[0].Program);
        Assert.Contains("\"hello world\"", ast.Stages[0].RawArgs);
    }

    // ── Pipeline ──────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Pipeline_TwoStages()
    {
        var ast = ShellLineLexer.Parse("ls -la | grep txt");
        Assert.Equal(2, ast.Stages.Count);
        Assert.Equal("ls", ast.Stages[0].Program);
        Assert.Equal("grep", ast.Stages[1].Program);
    }

    [Fact]
    public void Parse_Pipeline_ThreeStages()
    {
        var ast = ShellLineLexer.Parse("cat file.txt | sort | uniq");
        Assert.Equal(3, ast.Stages.Count);
        Assert.Equal("cat", ast.Stages[0].Program);
        Assert.Equal("sort", ast.Stages[1].Program);
        Assert.Equal("uniq", ast.Stages[2].Program);
    }

    [Fact]
    public void Parse_PipeInsideQuotes_NotSplitOnPipe()
    {
        var ast = ShellLineLexer.Parse("echo \"a | b\"");
        Assert.Single(ast.Stages);
        Assert.Equal("echo", ast.Stages[0].Program);
        Assert.Contains("a | b", ast.Stages[0].RawArgs);
    }

    // ── Redirect detection ────────────────────────────────────────────────────

    [Fact]
    public void Parse_StdoutRedirect_Detected()
    {
        var ast = ShellLineLexer.Parse("ls > out.txt");
        Assert.Single(ast.Redirects);
        Assert.Equal(RedirectStream.Stdout, ast.Redirects[0].Stream);
        Assert.False(ast.Redirects[0].Append);
        Assert.Equal("out.txt", ast.Redirects[0].Target);
    }

    [Fact]
    public void Parse_AppendRedirect_Detected()
    {
        var ast = ShellLineLexer.Parse("echo hello >> log.txt");
        Assert.Single(ast.Redirects);
        Assert.Equal(RedirectStream.Stdout, ast.Redirects[0].Stream);
        Assert.True(ast.Redirects[0].Append);
    }

    [Fact]
    public void Parse_StderrRedirect_Detected()
    {
        var ast = ShellLineLexer.Parse("make 2> errors.txt");
        Assert.Single(ast.Redirects);
        Assert.Equal(RedirectStream.Stderr, ast.Redirects[0].Stream);
    }

    [Fact]
    public void Parse_CombinedRedirect_Detected()
    {
        var ast = ShellLineLexer.Parse("build &> output.txt");
        Assert.Single(ast.Redirects);
        Assert.Equal(RedirectStream.Both, ast.Redirects[0].Stream);
    }

    [Fact]
    public void Parse_NoRedirects_EmptyList()
    {
        var ast = ShellLineLexer.Parse("echo hello");
        Assert.Empty(ast.Redirects);
    }

    // ── Phase 5: prefix flags ─────────────────────────────────────────────────

    [Fact]
    public void Lexer_NoPrefix_BothFlagsFalse()
    {
        var ast = ShellLineLexer.Parse("echo hello");
        Assert.False(ast.IsStrict);
        Assert.False(ast.IsForced);
    }

    [Fact]
    public void Lexer_BangPrefix_StripsAndSetsStrict()
    {
        var ast = ShellLineLexer.Parse("!ls -la");
        Assert.True(ast.IsStrict);
        Assert.False(ast.IsForced);
        Assert.Single(ast.Stages);
        Assert.Equal("ls", ast.Stages[0].Program);
    }

    [Fact]
    public void Lexer_BackslashPrefix_StripsAndSetsForced()
    {
        var ast = ShellLineLexer.Parse("\\ls -la");
        Assert.False(ast.IsStrict);
        Assert.True(ast.IsForced);
        Assert.Single(ast.Stages);
        Assert.Equal("ls", ast.Stages[0].Program);
    }

    [Fact]
    public void Lexer_BangBackslashPrefix_StripsBoth()
    {
        var ast = ShellLineLexer.Parse("!\\ls -la");
        Assert.True(ast.IsStrict);
        Assert.True(ast.IsForced);
        Assert.Single(ast.Stages);
        Assert.Equal("ls", ast.Stages[0].Program);
    }

    [Fact]
    public void Lexer_BangPrefix_LeadingWhitespace_StillStripsPrefix()
    {
        var ast = ShellLineLexer.Parse("  !echo hello");
        Assert.True(ast.IsStrict);
        Assert.Equal("echo", ast.Stages[0].Program);
    }

    [Fact]
    public void Lexer_BangPrefix_PipelinePreserved()
    {
        var ast = ShellLineLexer.Parse("!echo hello | cat");
        Assert.True(ast.IsStrict);
        Assert.Equal(2, ast.Stages.Count);
        Assert.Equal("echo", ast.Stages[0].Program);
        Assert.Equal("cat", ast.Stages[1].Program);
    }
}
