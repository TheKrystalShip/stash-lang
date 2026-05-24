namespace Stash.Tests.Lsp.Completion;

using System;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using Stash.Lsp.Completion.Snippets;
using Xunit;

/// <summary>
/// Unit tests for <see cref="SnippetContext.Classify"/> and <see cref="SnippetContext.Matches"/>.
/// </summary>
/// <remarks>
/// Scope classification uses the existing <see cref="ScopeTree"/> without a new analysis pass.
/// Tests use <see cref="AnalysisEngine"/> to produce a real scope tree from Stash source.
/// </remarks>
public class SnippetContextTests
{
    // ── Classify — scope vocabulary ─────────────────────────────────────────────

    [Fact]
    public void Classify_TopLevelCursor_ReturnsTopLevel()
    {
        // A cursor at line 0, col 0 of a top-level-only script.
        var tree = GetScopeTree("let x = 1;\n");

        var result = SnippetContext.Classify(tree, line: 0, column: 0);

        Assert.Equal(SnippetScope.TopLevel, result);
    }

    [Fact]
    public void Classify_CursorInsideFunction_ReturnsFnBody()
    {
        // Cursor on line 1 (inside the function body).
        const string src = "fn foo() {\nlet x = 1;\n}\n";
        var tree = GetScopeTree(src);

        var result = SnippetContext.Classify(tree, line: 1, column: 0);

        Assert.Equal(SnippetScope.FnBody, result);
    }

    [Fact]
    public void Classify_CursorInsideLoop_ReturnsLoopBody()
    {
        // A for-in loop at the top level; cursor on line 1 (loop body).
        const string src = "for (let i in [1,2,3]) {\nlet x = i;\n}\n";
        var tree = GetScopeTree(src);

        var result = SnippetContext.Classify(tree, line: 1, column: 0);

        Assert.Equal(SnippetScope.LoopBody, result);
    }

    [Fact]
    public void Classify_CursorInsideLoopNestedInFunction_ReturnsLoopBody()
    {
        // A loop nested inside a function — LoopBody wins because it is the nearest ancestor.
        const string src = "fn foo() {\nfor (let i in [1,2,3]) {\nlet x = i;\n}\n}\n";
        var tree = GetScopeTree(src);

        // Cursor on line 2 (inside the loop body, which is inside the function).
        var result = SnippetContext.Classify(tree, line: 2, column: 0);

        Assert.Equal(SnippetScope.LoopBody, result);
    }

    [Fact]
    public void Classify_CursorAfterLoop_InsideFunction_ReturnsFnBody()
    {
        // After the for loop closes, cursor is still inside the function — FnBody.
        const string src = "fn foo() {\nfor (let i in [1]) {\nlet x = i;\n}\nlet y = 2;\n}\n";
        var tree = GetScopeTree(src);

        // Line 4 is "let y = 2;" — inside fn scope, outside the loop.
        var result = SnippetContext.Classify(tree, line: 4, column: 0);

        Assert.Equal(SnippetScope.FnBody, result);
    }

    [Fact]
    public void Classify_NullTree_ReturnsAny()
    {
        // When no analysis is available, fall back to Any so snippets still appear.
        var result = SnippetContext.Classify(null, line: 0, column: 0);

        Assert.Equal(SnippetScope.Any, result);
    }

    // ── Matches ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Matches_Any_MatchesTopLevel()
        => Assert.True(SnippetContext.Matches(SnippetScope.Any, SnippetScope.TopLevel));

    [Fact]
    public void Matches_Any_MatchesFnBody()
        => Assert.True(SnippetContext.Matches(SnippetScope.Any, SnippetScope.FnBody));

    [Fact]
    public void Matches_Any_MatchesLoopBody()
        => Assert.True(SnippetContext.Matches(SnippetScope.Any, SnippetScope.LoopBody));

    [Fact]
    public void Matches_Any_MatchesAny()
        => Assert.True(SnippetContext.Matches(SnippetScope.Any, SnippetScope.Any));

    [Fact]
    public void Matches_TopLevel_MatchesTopLevel()
        => Assert.True(SnippetContext.Matches(SnippetScope.TopLevel, SnippetScope.TopLevel));

    [Fact]
    public void Matches_TopLevel_DoesNotMatchFnBody()
        => Assert.False(SnippetContext.Matches(SnippetScope.TopLevel, SnippetScope.FnBody));

    [Fact]
    public void Matches_TopLevel_DoesNotMatchLoopBody()
        => Assert.False(SnippetContext.Matches(SnippetScope.TopLevel, SnippetScope.LoopBody));

    [Fact]
    public void Matches_FnBody_MatchesFnBody()
        => Assert.True(SnippetContext.Matches(SnippetScope.FnBody, SnippetScope.FnBody));

    [Fact]
    public void Matches_FnBody_DoesNotMatchTopLevel()
        => Assert.False(SnippetContext.Matches(SnippetScope.FnBody, SnippetScope.TopLevel));

    [Fact]
    public void Matches_LoopBody_MatchesLoopBody()
        => Assert.True(SnippetContext.Matches(SnippetScope.LoopBody, SnippetScope.LoopBody));

    [Fact]
    public void Matches_LoopBody_DoesNotMatchFnBody()
        => Assert.False(SnippetContext.Matches(SnippetScope.LoopBody, SnippetScope.FnBody));

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static ScopeTree GetScopeTree(string source)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var uri = new Uri($"file:///test/snip_ctx_{Guid.NewGuid():N}.stash");
        engine.Analyze(uri, source);
        return engine.GetCachedResult(uri)!.Symbols;
    }
}
