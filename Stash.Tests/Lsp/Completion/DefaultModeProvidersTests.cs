namespace Stash.Tests.Lsp.Completion;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using Stash.Lsp.Completion;
using Stash.Lsp.Completion.Providers;
using Stash.Lsp.Completion.Providers.Dot;
using Stash.Lsp.Handlers;
using Stash.Stdlib;
using Xunit;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using LspCompletionContext = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionContext;

/// <summary>
/// Unit tests for the four Default-mode completion providers
/// (<see cref="KeywordCompletionProvider"/>, <see cref="StdlibFunctionCompletionProvider"/>,
/// <see cref="StdlibNamespaceCompletionProvider"/>, <see cref="ScopedSymbolCompletionProvider"/>).
/// Covers the two regression scenarios that motivated the
/// <see cref="SymbolAccessibility"/> / <see cref="SymbolOrigin"/> filtering contract:
/// member-only symbols must not leak into the unqualified list, and stdlib namespaces
/// must appear exactly once.
/// </summary>
public class DefaultModeProvidersTests
{
    // ── Source priorities ────────────────────────────────────────────────────────

    [Fact]
    public void KeywordCompletionProvider_SourcePriority_Is10()
    {
        var ctx = BuildDefaultContext("");
        var provider = new KeywordCompletionProvider();

        Assert.True(provider.AppliesTo(ctx));
        var candidate = provider.Provide(ctx).First();
        Assert.Equal(10, candidate.SourcePriority);
    }

    [Fact]
    public void StdlibFunctionCompletionProvider_SourcePriority_Is20()
    {
        var ctx = BuildDefaultContext("");
        var provider = new StdlibFunctionCompletionProvider();

        Assert.True(provider.AppliesTo(ctx));
        var candidate = provider.Provide(ctx).First();
        Assert.Equal(20, candidate.SourcePriority);
    }

    [Fact]
    public void StdlibNamespaceCompletionProvider_SourcePriority_Is30()
    {
        var ctx = BuildDefaultContext("");
        var provider = new StdlibNamespaceCompletionProvider();

        Assert.True(provider.AppliesTo(ctx));
        var candidate = provider.Provide(ctx).First();
        Assert.Equal(30, candidate.SourcePriority);
    }

    [Fact]
    public void ScopedSymbolCompletionProvider_SourcePriority_Is40()
    {
        const string src = "let myVar = 1;\n\n";
        var ctx = BuildDefaultContext(src, line: 1, col: 0);
        var provider = new ScopedSymbolCompletionProvider();

        Assert.True(provider.AppliesTo(ctx));
        var candidates = provider.Provide(ctx).ToList();
        Assert.All(candidates, c => Assert.Equal(40, c.SourcePriority));
    }

    // ── AppliesTo gate ───────────────────────────────────────────────────────────

    [Fact]
    public void AllProviders_DoNotApply_InDotMode()
    {
        var ctx = BuildContextWithMode(CompletionMode.Dot, "");

        Assert.False(new KeywordCompletionProvider().AppliesTo(ctx));
        Assert.False(new StdlibFunctionCompletionProvider().AppliesTo(ctx));
        Assert.False(new StdlibNamespaceCompletionProvider().AppliesTo(ctx));
        Assert.False(new ScopedSymbolCompletionProvider().AppliesTo(ctx));
    }

    // ── Member-leakage invariant ─────────────────────────────────────────────────

    /// <summary>
    /// Stdlib struct fields/methods and enum members must NOT appear in unqualified
    /// completions — they are always reached through dot notation and are never
    /// valid as bare identifiers.
    /// </summary>
    [Fact]
    public void DefaultMode_ExcludesStdlibStructFieldsAndEnumMembers()
    {
        var items = InvokeCompletion("\n").ToList();

        // No stdlib struct field should leak.
        Assert.DoesNotContain(items, i => i.Kind == LspCompletionItemKind.Field);
        // No struct method.
        Assert.DoesNotContain(items, i => i.Kind == LspCompletionItemKind.Method);
        // No enum member (e.g. WatchEventType.Deleted).
        Assert.DoesNotContain(items, i => i.Kind == LspCompletionItemKind.EnumMember);
    }

    /// <summary>
    /// User-defined struct fields and enum members carry the same accessibility
    /// constraint as their stdlib counterparts.
    /// </summary>
    [Fact]
    public void DefaultMode_ExcludesUserStructFieldsAndEnumMembers()
    {
        const string src = "struct Point { x, y }\nenum Color { Red, Green }\n\n";
        var items = InvokeCompletion(src).ToList();

        Assert.DoesNotContain(items, i => i.Label == "x");
        Assert.DoesNotContain(items, i => i.Label == "y");
        Assert.DoesNotContain(items, i => i.Label == "Red");
        Assert.DoesNotContain(items, i => i.Label == "Green");

        // The type names themselves must still appear.
        Assert.Contains(items, i => i.Label == "Point");
        Assert.Contains(items, i => i.Label == "Color");
    }

    /// <summary>
    /// Parameters must appear in unqualified completions inside the function body —
    /// guards against an over-broad "skip all RequiresQualification" filter.
    /// </summary>
    [Fact]
    public void DefaultMode_IncludesFunctionParameters()
    {
        const string src = "fn greet(name) {\n\n}\n";
        // Cursor sits on line 2 (0-based: 1), col 0 — inside the function body.
        var items = InvokeCompletionAt(src, line: 1, col: 0).ToList();

        Assert.Contains(items, i => i.Label == "name");
    }

    // ── Namespace deduplication ──────────────────────────────────────────────────

    /// <summary>
    /// Each stdlib namespace must appear exactly once. The scoped-symbol pass also
    /// returns <c>Kind.Namespace</c> entries that <c>SymbolCollector</c> injects for
    /// hover/goto-def — <see cref="ScopedSymbolCompletionProvider"/> excludes them via
    /// the <c>Origin == BuiltinStdlib</c> filter so the sink's first-wins dedup is
    /// not the sole guard.
    /// </summary>
    [Fact]
    public void DefaultMode_StdlibNamespacesAreNotDuplicated()
    {
        var items = InvokeCompletion("\n").ToList();

        foreach (var ns in StdlibRegistry.NamespaceNames)
        {
            int count = items.Count(i => i.Label == ns);
            Assert.True(count <= 1, $"namespace '{ns}' appeared {count} times in completion list");
        }
    }

    [Fact]
    public void DefaultMode_NoLabelAppearsMoreThanOnce()
    {
        var items = InvokeCompletion("\n").ToList();

        var duplicates = items.GroupBy(i => i.Label).Where(g => g.Count() > 1).ToList();
        Assert.Empty(duplicates);
    }

    // ── Default-mode content surface ─────────────────────────────────────────────

    [Fact]
    public void DefaultMode_ContainsKeywords()
    {
        var items = InvokeCompletion("\n").ToList();

        Assert.Contains(items, i => i.Label == "let" && i.Kind == LspCompletionItemKind.Keyword);
        Assert.Contains(items, i => i.Label == "fn" && i.Kind == LspCompletionItemKind.Keyword);
    }

    [Fact]
    public void DefaultMode_ContainsStdlibNamespaces()
    {
        var items = InvokeCompletion("\n").ToList();

        Assert.Contains(items, i => i.Label == "fs" && i.Kind == LspCompletionItemKind.Module);
        Assert.Contains(items, i => i.Label == "io" && i.Kind == LspCompletionItemKind.Module);
    }

    [Fact]
    public void DefaultMode_ProducesNonEmptyList()
    {
        var items = InvokeCompletion("\n").ToList();
        Assert.NotEmpty(items);
    }

    // ── ScopedSymbolCompletionProvider filter contract ───────────────────────────

    [Fact]
    public void ScopedSymbolProvider_FiltersOut_RequiresQualificationSymbols()
    {
        // A struct definition injects Field symbols (RequiresQualification) into the scope.
        const string src = "struct Box { width, height }\n\n";
        var ctx = BuildDefaultContext(src, line: 1, col: 0);
        var provider = new ScopedSymbolCompletionProvider();

        var candidates = provider.Provide(ctx).ToList();

        Assert.DoesNotContain(candidates, c => c.Label == "width");
        Assert.DoesNotContain(candidates, c => c.Label == "height");
    }

    [Fact]
    public void ScopedSymbolProvider_FiltersOut_BuiltinStdlibOriginSymbols()
    {
        // Even with an empty source, RegisterBuiltIns injects BuiltinStdlib symbols.
        var ctx = BuildDefaultContext("\n", line: 0, col: 0);
        var provider = new ScopedSymbolCompletionProvider();

        var candidates = provider.Provide(ctx).ToList();

        // None of the emitted candidates should be namespace or function symbols
        // that SymbolCollector injected as BuiltinStdlib — those are owned by
        // StdlibFunctionCompletionProvider and StdlibNamespaceCompletionProvider.
        Assert.DoesNotContain(candidates, c => c.Label == "io");
        Assert.DoesNotContain(candidates, c => c.Label == "fs");
        Assert.DoesNotContain(candidates, c => c.Label == "println");
    }

    [Fact]
    public void ScopedSymbolProvider_Includes_UserDefinedVariables()
    {
        const string src = "let counter = 0;\n\n";
        var ctx = BuildDefaultContext(src, line: 1, col: 0);
        var provider = new ScopedSymbolCompletionProvider();

        var candidates = provider.Provide(ctx).ToList();

        Assert.Contains(candidates, c => c.Label == "counter");
    }

    // ── SourceTag contract ───────────────────────────────────────────────────────

    [Fact]
    public void KeywordCompletionProvider_SourceTag_IsSet()
    {
        var ctx = BuildDefaultContext("");
        var candidate = new KeywordCompletionProvider().Provide(ctx).First();
        Assert.Equal("KeywordCompletionProvider", candidate.SourceTag);
    }

    [Fact]
    public void StdlibFunctionCompletionProvider_SourceTag_IsSet()
    {
        var ctx = BuildDefaultContext("");
        var candidate = new StdlibFunctionCompletionProvider().Provide(ctx).First();
        Assert.Equal("StdlibFunctionCompletionProvider", candidate.SourceTag);
    }

    [Fact]
    public void StdlibNamespaceCompletionProvider_SourceTag_IsSet()
    {
        var ctx = BuildDefaultContext("");
        var candidate = new StdlibNamespaceCompletionProvider().Provide(ctx).First();
        Assert.Equal("StdlibNamespaceCompletionProvider", candidate.SourceTag);
    }

    [Fact]
    public void ScopedSymbolProvider_SourceTag_IsSet()
    {
        const string src = "let x = 1;\n\n";
        var ctx = BuildDefaultContext(src, line: 1, col: 0);
        var candidates = new ScopedSymbolCompletionProvider().Provide(ctx).ToList();
        Assert.All(candidates, c => Assert.Equal("ScopedSymbolCompletionProvider", c.SourceTag));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>Invokes the completion handler at the end of <paramref name="source"/>.</summary>
    private static IEnumerable<CompletionItem> InvokeCompletion(string source)
    {
        var lines = source.Split('\n');
        int line = lines.Length - 1;
        return InvokeCompletionAt(source + "\n", line + 1, 0);
    }

    private static IEnumerable<CompletionItem> InvokeCompletionAt(string source, int line, int col)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var docs = new DocumentManager(NullLogger<DocumentManager>.Instance);
        var logger = NullLogger<CompletionHandler>.Instance;

        var uri = new Uri($"file:///test/dmp_{Guid.NewGuid():N}.stash");
        docs.Open(uri, source, 1);
        engine.Analyze(uri, source);

        var handler = new CompletionHandler(engine, docs, logger, BuildDispatcher());

        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = line, Character = col },
            Context = new LspCompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
        };

        var result = handler.Handle(request, default).Result;
        return result.Items ?? Enumerable.Empty<CompletionItem>();
    }

    /// <summary>Builds a Default-mode CompletionContext backed by an analyzed source.</summary>
    private static Stash.Lsp.Completion.CompletionContext BuildDefaultContext(string source, int line = 0, int col = 0)
        => BuildContextWithMode(CompletionMode.Default, source, line, col);

    private static Stash.Lsp.Completion.CompletionContext BuildContextWithMode(
        CompletionMode mode, string source, int line = 0, int col = 0)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var uri = new Uri($"file:///test/ctx_{Guid.NewGuid():N}.stash");
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

    private static CompletionDispatcher BuildDispatcher()
    {
        var pipelines = new Dictionary<CompletionMode, IReadOnlyList<ICompletionProvider>>
        {
            [CompletionMode.Default] = new ICompletionProvider[]
            {
                new KeywordCompletionProvider(),
                new StdlibFunctionCompletionProvider(),
                new StdlibNamespaceCompletionProvider(),
                new ScopedSymbolCompletionProvider(),
            },
            [CompletionMode.Dot] = new ICompletionProvider[] { new DotCompletionProvider() },
            [CompletionMode.ImportString] = new ICompletionProvider[] { new ImportPathCompletionProvider() },
            [CompletionMode.AfterIs] = new ICompletionProvider[] { new IsTypeCompletionProvider() },
            [CompletionMode.AfterExtend] = new ICompletionProvider[] { new ExtendTypeCompletionProvider() },
        };
        return new CompletionDispatcher(pipelines);
    }
}
