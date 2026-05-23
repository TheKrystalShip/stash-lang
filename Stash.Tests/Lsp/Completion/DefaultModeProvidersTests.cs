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
using Stash.Lsp.Handlers;
using Stash.Stdlib;
using Xunit;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using LspCompletionContext = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionContext;

/// <summary>
/// Unit tests for the four Default-mode completion providers added in Phase 2
/// of the <c>lsp-completion-providers</c> feature.
/// Tests confirm that the new pipeline reproduces the post-fix monolith output
/// for the two regressions: member leakage (bug-1) and namespace deduplication (bug-2).
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

    // ── Bug-1: member leakage ────────────────────────────────────────────────────

    /// <summary>
    /// Bug-1 regression (commit 7c3f098): stdlib struct fields/methods and enum members
    /// must NOT appear in unqualified (Default-mode) completions via the new pipeline.
    /// They are always accessed via dot notation and are never valid bare identifiers.
    /// </summary>
    [Fact]
    public void NewPipeline_DefaultMode_ExcludesStdlibStructFieldsAndEnumMembers()
    {
        var items = InvokeNewPipelineCompletion("\n").ToList();

        // No stdlib struct field should leak.
        Assert.DoesNotContain(items, i => i.Kind == LspCompletionItemKind.Field);
        // No struct method.
        Assert.DoesNotContain(items, i => i.Kind == LspCompletionItemKind.Method);
        // No enum member (e.g. WatchEventType.Deleted).
        Assert.DoesNotContain(items, i => i.Kind == LspCompletionItemKind.EnumMember);
    }

    /// <summary>
    /// Bug-1 regression: user-defined struct fields and enum members must also be excluded.
    /// </summary>
    [Fact]
    public void NewPipeline_DefaultMode_ExcludesUserStructFieldsAndEnumMembers()
    {
        const string src = "struct Point { x, y }\nenum Color { Red, Green }\n\n";
        var items = InvokeNewPipelineCompletion(src).ToList();

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
    public void NewPipeline_DefaultMode_IncludesFunctionParameters()
    {
        const string src = "fn greet(name) {\n\n}\n";
        // Cursor sits on line 2 (0-based: 1), col 0 — inside the function body.
        var items = InvokeNewPipelineCompletionAt(src, line: 1, col: 0).ToList();

        Assert.Contains(items, i => i.Label == "name");
    }

    // ── Bug-2: namespace deduplication ──────────────────────────────────────────

    /// <summary>
    /// Bug-2 regression: each stdlib namespace must appear exactly once.
    /// The scoped-symbol pass also returns Kind.Namespace entries that SymbolCollector
    /// injected for hover/goto-def — ScopedSymbolCompletionProvider must exclude them
    /// via the Origin == BuiltinStdlib filter so the sink's first-wins dedup is
    /// not the sole guard.
    /// </summary>
    [Fact]
    public void NewPipeline_DefaultMode_StdlibNamespacesAreNotDuplicated()
    {
        var items = InvokeNewPipelineCompletion("\n").ToList();

        foreach (var ns in StdlibRegistry.NamespaceNames)
        {
            int count = items.Count(i => i.Label == ns);
            Assert.True(count <= 1, $"namespace '{ns}' appeared {count} times in new-pipeline completion list");
        }
    }

    [Fact]
    public void NewPipeline_DefaultMode_NoLabelAppearsMoreThanOnce()
    {
        var items = InvokeNewPipelineCompletion("\n").ToList();

        var duplicates = items.GroupBy(i => i.Label).Where(g => g.Count() > 1).ToList();
        Assert.Empty(duplicates);
    }

    // ── Content parity with monolith ─────────────────────────────────────────────

    [Fact]
    public void NewPipeline_DefaultMode_ContainsKeywords()
    {
        var items = InvokeNewPipelineCompletion("\n").ToList();

        Assert.Contains(items, i => i.Label == "let" && i.Kind == LspCompletionItemKind.Keyword);
        Assert.Contains(items, i => i.Label == "fn" && i.Kind == LspCompletionItemKind.Keyword);
    }

    [Fact]
    public void NewPipeline_DefaultMode_ContainsStdlibNamespaces()
    {
        var items = InvokeNewPipelineCompletion("\n").ToList();

        Assert.Contains(items, i => i.Label == "fs" && i.Kind == LspCompletionItemKind.Module);
        Assert.Contains(items, i => i.Label == "io" && i.Kind == LspCompletionItemKind.Module);
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

    // ── CompletionHandler test-seam ──────────────────────────────────────────────

    [Fact]
    public void CompletionHandler_UseNewPipeline_ProducesNonEmptyDefaultList()
    {
        var items = InvokeNewPipelineCompletion("\n").ToList();
        Assert.NotEmpty(items);
    }

    [Fact]
    public void CompletionHandler_DefaultPipeline_LivePathUnchanged()
    {
        // The live path (useNewPipeline = false) must still return the same shape.
        var (liveItems, newItems) = InvokeBoths("\n");

        // Both paths must include keywords and namespaces.
        Assert.Contains(liveItems, i => i.Label == "let");
        Assert.Contains(newItems, i => i.Label == "let");
        Assert.Contains(liveItems, i => i.Label == "io");
        Assert.Contains(newItems, i => i.Label == "io");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Invokes the new pipeline via the test-only CompletionHandler constructor.
    /// </summary>
    private static IEnumerable<CompletionItem> InvokeNewPipelineCompletion(string source)
    {
        var lines = source.Split('\n');
        int line = lines.Length - 1;
        return InvokeNewPipelineCompletionAt(source + "\n", line + 1, 0);
    }

    private static IEnumerable<CompletionItem> InvokeNewPipelineCompletionAt(
        string source, int line, int col)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var docs = new DocumentManager(NullLogger<DocumentManager>.Instance);
        var logger = NullLogger<CompletionHandler>.Instance;

        var uri = new Uri($"file:///test/np_{Guid.NewGuid():N}.stash");
        docs.Open(uri, source, 1);
        engine.Analyze(uri, source);

        var handler = new CompletionHandler(engine, docs, logger, useNewPipeline: true);

        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = line, Character = col },
            Context = new OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionContext
            {
                TriggerKind = CompletionTriggerKind.Invoked
            }
        };

        var result = handler.Handle(request, default).Result;
        return result.Items ?? Enumerable.Empty<CompletionItem>();
    }

    /// <summary>Returns (liveItems, newItems) for the same source to compare the two paths.</summary>
    private static (List<CompletionItem> Live, List<CompletionItem> New) InvokeBoths(string source)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var docs = new DocumentManager(NullLogger<DocumentManager>.Instance);
        var logger = NullLogger<CompletionHandler>.Instance;

        var lines = source.Split('\n');
        int line = lines.Length;
        string fullSource = source + "\n";
        var uri = new Uri($"file:///test/both_{Guid.NewGuid():N}.stash");
        docs.Open(uri, fullSource, 1);
        engine.Analyze(uri, fullSource);

        var liveHandler = new CompletionHandler(engine, docs, logger);
        var newHandler  = new CompletionHandler(engine, docs, logger, useNewPipeline: true);

        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = line, Character = 0 },
            Context = new OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionContext
            {
                TriggerKind = CompletionTriggerKind.Invoked
            }
        };

        var liveResult = liveHandler.Handle(request, default).Result;
        var newResult  = newHandler.Handle(request, default).Result;

        return (
            (liveResult.Items ?? Enumerable.Empty<CompletionItem>()).ToList(),
            (newResult.Items  ?? Enumerable.Empty<CompletionItem>()).ToList()
        );
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
}
