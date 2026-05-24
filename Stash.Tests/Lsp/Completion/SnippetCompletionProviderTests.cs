namespace Stash.Tests.Lsp.Completion;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using Stash.Lsp.Completion;
using Stash.Lsp.Completion.Providers;
using Stash.Lsp.Completion.Providers.Dot;
using Stash.Lsp.Completion.Snippets;
using Stash.Lsp.Handlers;
using Xunit;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using LspCompletionContext = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionContext;
using LspInsertTextFormat = OmniSharp.Extensions.LanguageServer.Protocol.Models.InsertTextFormat;

/// <summary>
/// Unit tests for <see cref="SnippetCompletionProvider"/>:
/// <list type="bullet">
///   <item><description>Provider applies only in <see cref="CompletionMode.Default"/> mode.</description></item>
///   <item><description>SourcePriority is strictly greater than <see cref="ScopedSymbolCompletionProvider"/>'s priority (40).</description></item>
///   <item><description>End-to-end: non-keyword snippets (<c>fori</c>, <c>ife</c>) appear in Default-mode completion with correct Kind and InsertTextFormat.</description></item>
///   <item><description>Symbol-vs-snippet collision: user variable named <c>fori</c> suppresses the <c>fori</c> snippet.</description></item>
/// </list>
/// </summary>
public class SnippetCompletionProviderTests
{
    // ── AppliesTo ────────────────────────────────────────────────────────────────

    [Fact]
    public void SnippetCompletionProvider_AppliesTo_DefaultMode()
    {
        var ctx = BuildDefaultContext("");
        var provider = new SnippetCompletionProvider(new StubRegistry());
        Assert.True(provider.AppliesTo(ctx));
    }

    [Fact]
    public void SnippetCompletionProvider_DoesNotApplyTo_DotMode()
    {
        var ctx = BuildContextWithMode(CompletionMode.Dot, "");
        var provider = new SnippetCompletionProvider(new StubRegistry());
        Assert.False(provider.AppliesTo(ctx));
    }

    [Fact]
    public void SnippetCompletionProvider_DoesNotApplyTo_AfterIsMode()
    {
        var ctx = BuildContextWithMode(CompletionMode.AfterIs, "");
        var provider = new SnippetCompletionProvider(new StubRegistry());
        Assert.False(provider.AppliesTo(ctx));
    }

    // ── SourcePriority ───────────────────────────────────────────────────────────

    [Fact]
    public void SnippetCompletionProvider_SourcePriority_IsStrictlyGreaterThan40()
    {
        // ScopedSymbolCompletionProvider has priority 40. Snippet must be > 40.
        Assert.True(SnippetCompletionProvider.SourcePriority > 40,
            $"SnippetCompletionProvider.SourcePriority ({SnippetCompletionProvider.SourcePriority}) " +
            "must be strictly greater than 40 (ScopedSymbolCompletionProvider) so user symbols win dedup.");
    }

    [Fact]
    public void SnippetCompletionProvider_SourcePriority_Is1000()
    {
        Assert.Equal(1000, SnippetCompletionProvider.SourcePriority);
    }

    [Fact]
    public void SnippetCompletionProvider_Candidates_HaveCorrectSourcePriority()
    {
        var ctx = BuildDefaultContext("");
        var snippet = MakeSnippet(prefix: "fori", body: "for (let __snip_1 = __snip_2; __snip_1 < __snip_3; __snip_1++) {\n}");
        var provider = new SnippetCompletionProvider(StubRegistryWith(snippet));
        var candidates = provider.Provide(ctx).ToList();

        Assert.Single(candidates);
        Assert.Equal(1000, candidates[0].SourcePriority);
    }

    // ── Candidate shape ──────────────────────────────────────────────────────────

    [Fact]
    public void SnippetCompletionProvider_Candidate_HasKindSnippet()
    {
        var ctx = BuildDefaultContext("");
        var snippet = MakeSnippet(prefix: "ife", body: "if (__snip_1) {\n} else {\n}");
        var provider = new SnippetCompletionProvider(StubRegistryWith(snippet));
        var candidate = provider.Provide(ctx).Single();

        Assert.Equal(LspCompletionItemKind.Snippet, candidate.Kind);
    }

    [Fact]
    public void SnippetCompletionProvider_Candidate_HasInsertTextFormatSnippet()
    {
        var ctx = BuildDefaultContext("");
        var snippet = MakeSnippet(prefix: "ife", body: "if (${1:condition}) {\n\t$2\n} else {\n\t$0\n}");
        var provider = new SnippetCompletionProvider(StubRegistryWith(snippet));
        var candidate = provider.Provide(ctx).Single();

        Assert.Equal(LspInsertTextFormat.Snippet, candidate.InsertTextFormat);
    }

    [Fact]
    public void SnippetCompletionProvider_Candidate_InsertTextIsSnippetBody()
    {
        const string body = "for (let ${1:i} = 0; ${1:i} < ${3:10}; ${1:i}++) {\n\t$0\n}";
        var ctx = BuildDefaultContext("");
        var snippet = MakeSnippet(prefix: "fori", body: body);
        var provider = new SnippetCompletionProvider(StubRegistryWith(snippet));
        var candidate = provider.Provide(ctx).Single();

        Assert.Equal(body, candidate.InsertText);
    }

    [Fact]
    public void SnippetCompletionProvider_Candidate_LabelIsPrefixAndDetailIsDisplayName()
    {
        var ctx = BuildDefaultContext("");
        var snippet = MakeSnippet(prefix: "fori", displayName: "C-Style For Loop");
        var provider = new SnippetCompletionProvider(StubRegistryWith(snippet));
        var candidate = provider.Provide(ctx).Single();

        Assert.Equal("fori", candidate.Label);
        Assert.Equal("C-Style For Loop", candidate.Detail);
    }

    [Fact]
    public void SnippetCompletionProvider_Candidate_SourceTagIsProviderName()
    {
        var ctx = BuildDefaultContext("");
        var snippet = MakeSnippet(prefix: "ife");
        var provider = new SnippetCompletionProvider(StubRegistryWith(snippet));
        var candidate = provider.Provide(ctx).Single();

        Assert.Equal(nameof(SnippetCompletionProvider), candidate.SourceTag);
    }

    // ── End-to-end: non-keyword snippets surface in Default mode ────────────────

    [Fact]
    public void EndToEnd_DefaultMode_ForI_SnippetAppearsWithSnippetKindAndFormat()
    {
        // "fori" is NOT a Stash keyword, so it won't be blocked by KeywordCompletionProvider.
        // End-to-end: the snippet should appear in the CompletionHandler output.
        var items = InvokeCompletionAt("\n", line: 0, col: 0, includeSnippets: true).ToList();
        var forI = items.FirstOrDefault(i => i.Label == "fori");

        Assert.NotNull(forI);
        Assert.Equal(LspCompletionItemKind.Snippet, forI!.Kind);
        Assert.Equal(LspInsertTextFormat.Snippet, forI.InsertTextFormat);
        Assert.NotNull(forI.InsertText);
    }

    [Fact]
    public void EndToEnd_DefaultMode_IfE_SnippetAppearsWithSnippetKindAndFormat()
    {
        // "ife" is NOT a Stash keyword; should surface.
        var items = InvokeCompletionAt("\n", line: 0, col: 0, includeSnippets: true).ToList();
        var ife = items.FirstOrDefault(i => i.Label == "ife");

        Assert.NotNull(ife);
        Assert.Equal(LspCompletionItemKind.Snippet, ife!.Kind);
        Assert.Equal(LspInsertTextFormat.Snippet, ife.InsertTextFormat);
    }

    [Fact]
    public void EndToEnd_DefaultMode_KeywordsAndSnippetsCoexistWithoutCollision()
    {
        // Snippet prefixes were renamed away from Stash keywords (fn→fnd, let→letv,
        // for→fore, if→ifc, struct→strc) so each label has exactly one owner: the
        // Stash keyword appears with Kind=Keyword; the snippet appears with Kind=Snippet.
        // Validates that the rename eliminates the keyword/snippet collision class.
        var items = InvokeCompletionAt("\n", line: 0, col: 0, includeSnippets: true).ToList();

        foreach (var kw in new[] { "fn", "let", "for", "if", "struct" })
        {
            var item = items.FirstOrDefault(i => i.Label == kw);
            Assert.NotNull(item);
            Assert.Equal(LspCompletionItemKind.Keyword, item!.Kind);
        }

        foreach (var snippet in new[] { "fnd", "letv", "fore", "ifc", "strc" })
        {
            var item = items.FirstOrDefault(i => i.Label == snippet);
            Assert.NotNull(item);
            Assert.Equal(LspCompletionItemKind.Snippet, item!.Kind);
        }
    }

    // ── Symbol-vs-snippet collision ──────────────────────────────────────────────

    [Fact]
    public void Collision_UserVariableNamedFori_WinsOverSnippet()
    {
        // When a user variable shares a snippet's prefix, the variable wins.
        // ScopedSymbolCompletionProvider has priority 40; snippet has priority 50.
        // The pipeline runs lowest-priority-number first, so ScopedSymbol adds "fori" first;
        // the snippet's "fori" is then silently dropped by the sink's first-wins dedup.
        const string src = "let fori = 42;\n\n";
        var items = InvokeCompletionAt(src, line: 1, col: 0, includeSnippets: true).ToList();

        var foriItem = items.FirstOrDefault(i => i.Label == "fori");
        Assert.NotNull(foriItem);

        // The winning item must be the Variable (from ScopedSymbolCompletionProvider),
        // not the snippet (which would have Kind=Snippet and a non-null InsertText).
        Assert.Equal(LspCompletionItemKind.Variable, foriItem!.Kind);

        // The snippet's InsertText must NOT be on the winner — it should be the user var.
        // (CompletionItem.InsertText defaults to null when not set by non-snippet providers.)
        Assert.Null(foriItem.InsertText);
    }

    // ── BundledSnippetRegistry integration ──────────────────────────────────────

    [Fact]
    public void BundledRegistry_LoadsWithoutErrors()
    {
        var registry = new BundledSnippetRegistry();

        // All bundled snippets in bundled.json must pass validation.
        Assert.Empty(registry.LoadErrors);
    }

    [Fact]
    public void BundledRegistry_Snapshot_ContainsExpectedPrefixes()
    {
        var registry = new BundledSnippetRegistry();
        var prefixes = registry.Snapshot().Select(s => s.Prefix).ToHashSet();

        // Required seed: renamed away from Stash keywords so they actually surface
        // (fn/let/for/if/struct were renamed to fnd/letv/fore/ifc/strc).
        foreach (var expected in new[] { "fnd", "letv", "fore", "ifc", "strc", "fori", "ife" })
            Assert.Contains(expected, prefixes);
    }

    [Fact]
    public void BundledRegistry_DeclarationSnippets_HaveScopeTopLevel()
    {
        // P4: declaration snippets (fnd, strc) must be scope=TopLevel.
        var registry = new BundledSnippetRegistry();
        var byPrefix = registry.Snapshot().ToDictionary(s => s.Prefix);

        Assert.Equal(SnippetScope.TopLevel, byPrefix["fnd"].Scope);
        Assert.Equal(SnippetScope.TopLevel, byPrefix["strc"].Scope);
    }

    [Fact]
    public void BundledRegistry_StatementSnippets_HaveScopeAny()
    {
        // P4: statement snippets (letv, ifc, ife, fore, fori) remain scope=Any.
        var registry = new BundledSnippetRegistry();
        var byPrefix = registry.Snapshot().ToDictionary(s => s.Prefix);

        foreach (var prefix in new[] { "letv", "ifc", "ife", "fore", "fori" })
            Assert.Equal(SnippetScope.Any, byPrefix[prefix].Scope);
    }

    // ── Scope-gated provider fixture pair ────────────────────────────────────────

    [Fact]
    public void Provider_TopLevelCursor_EmitsDeclarationSnippets()
    {
        // Cursor at top level (line 0, col 0 of empty document) must see fnd and strc.
        var items = InvokeCompletionAt("\n", line: 0, col: 0, includeSnippets: true).ToList();

        Assert.Contains(items, i => i.Label == "fnd" && i.Kind == LspCompletionItemKind.Snippet);
        Assert.Contains(items, i => i.Label == "strc" && i.Kind == LspCompletionItemKind.Snippet);
    }

    [Fact]
    public void Provider_FnBodyCursor_DoesNotEmitDeclarationSnippets()
    {
        // Cursor inside a function body must NOT see fnd or strc (they are TopLevel-only).
        // Source: a function with a body containing a let statement so the cursor
        // at line 1 is inside the function scope.
        const string src = "fn foo() {\nlet x = 1;\n}\n";
        var items = InvokeCompletionAt(src, line: 1, col: 0, includeSnippets: true).ToList();

        Assert.DoesNotContain(items, i => i.Label == "fnd");
        Assert.DoesNotContain(items, i => i.Label == "strc");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static Stash.Lsp.Completion.CompletionContext BuildDefaultContext(string source, int line = 0, int col = 0)
        => BuildContextWithMode(CompletionMode.Default, source, line, col);

    private static Stash.Lsp.Completion.CompletionContext BuildContextWithMode(
        CompletionMode mode, string source, int line = 0, int col = 0)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var uri = new Uri($"file:///test/snip_ctx_{Guid.NewGuid():N}.stash");
        engine.Analyze(uri, source);
        var result = engine.GetCachedResult(uri);

        var lines = source.Split('\n');
        string? currentLine = line < lines.Length ? lines[line] : null;

        return new Stash.Lsp.Completion.CompletionContext(
            Uri: uri,
            LspLine: line,
            LspColumn: col,
            CurrentLine: currentLine,
            Mode: mode,
            DotPrefix: null,
            Analysis: result,
            TriggerCharacter: null);
    }

    private static IEnumerable<CompletionItem> InvokeCompletionAt(
        string source, int line, int col, bool includeSnippets = false)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var docs = new DocumentManager(NullLogger<DocumentManager>.Instance);
        var logger = NullLogger<CompletionHandler>.Instance;

        var uri = new Uri($"file:///test/snip_e2e_{Guid.NewGuid():N}.stash");
        docs.Open(uri, source, 1);
        engine.Analyze(uri, source);

        var handler = new CompletionHandler(engine, docs, logger, BuildDispatcher(includeSnippets));

        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = line, Character = col },
            Context = new LspCompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
        };

        var result = handler.Handle(request, default).Result;
        return result.Items ?? Enumerable.Empty<CompletionItem>();
    }

    private static CompletionDispatcher BuildDispatcher(bool includeSnippets = false)
    {
        ICompletionProvider[] defaultPipeline = includeSnippets
            ? new ICompletionProvider[]
            {
                new KeywordCompletionProvider(),
                new StdlibFunctionCompletionProvider(),
                new StdlibNamespaceCompletionProvider(),
                new ScopedSymbolCompletionProvider(),
                new SnippetCompletionProvider(new BundledSnippetRegistry()),
            }
            : new ICompletionProvider[]
            {
                new KeywordCompletionProvider(),
                new StdlibFunctionCompletionProvider(),
                new StdlibNamespaceCompletionProvider(),
                new ScopedSymbolCompletionProvider(),
            };

        var pipelines = new Dictionary<CompletionMode, IReadOnlyList<ICompletionProvider>>
        {
            [CompletionMode.Default] = defaultPipeline,
            [CompletionMode.Dot] = new ICompletionProvider[] { new DotCompletionProvider() },
            [CompletionMode.ImportString] = new ICompletionProvider[] { new ImportPathCompletionProvider() },
            [CompletionMode.AfterIs] = new ICompletionProvider[] { new IsTypeCompletionProvider() },
            [CompletionMode.AfterExtend] = new ICompletionProvider[] { new ExtendTypeCompletionProvider() },
        };
        return new CompletionDispatcher(pipelines);
    }

    private static Snippet MakeSnippet(
        string prefix,
        string? body = null,
        string? displayName = null,
        SnippetScope scope = SnippetScope.Any)
    {
        body ??= "let __snip_1 = __snip_2;";
        displayName ??= prefix;
        return new Snippet(
            Id: $"Bundled:{prefix}:{scope}",
            Prefix: prefix,
            DisplayName: displayName,
            Body: body,
            Description: null,
            Scope: scope,
            Source: SnippetSourceKind.Bundled);
    }

    private static ISnippetRegistry StubRegistryWith(params Snippet[] snippets)
        => new StubRegistry(snippets);

    // ── Stub registry ─────────────────────────────────────────────────────────

    private sealed class StubRegistry : ISnippetRegistry
    {
        private readonly IReadOnlyList<Snippet> _snippets;

        public StubRegistry(params Snippet[] snippets)
        {
            _snippets = snippets;
        }

        public SnippetSourceKind Kind => SnippetSourceKind.Bundled;
        public IReadOnlyList<Snippet> Snapshot() => _snippets;
        public IReadOnlyList<SnippetLoadError> LoadErrors => Array.Empty<SnippetLoadError>();
        public void Reload() { }
    }
}
