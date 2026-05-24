namespace Stash.Tests.Lsp.Completion;

using System.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Lsp.Completion;
using Xunit;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using LspInsertTextFormat = OmniSharp.Extensions.LanguageServer.Protocol.Models.InsertTextFormat;

/// <summary>
/// Unit tests for <see cref="CompletionItemSink"/> dedup, ordering, and
/// defence-in-depth accessibility filtering.
/// </summary>
public class CompletionItemSinkTests
{
    // -------------------------------------------------------------------------
    // Idempotency — first Add wins
    // -------------------------------------------------------------------------

    [Fact]
    public void Add_SameLabelTwice_OnlyFirstItemKept()
    {
        var sink = new CompletionItemSink(CompletionMode.Default);
        var first  = new CompletionCandidate("foo", LspCompletionItemKind.Keyword, SourceTag: "first");
        var second = new CompletionCandidate("foo", LspCompletionItemKind.Function, SourceTag: "second");

        sink.Add(first);
        sink.Add(second);

        var list = sink.Materialize();
        var items = list.Items.ToList();
        Assert.Single(items);
        Assert.Equal("foo", items[0].Label);
        // SourceTag from the first Add must win.
        Assert.Equal("first", items[0].Data?.ToString());
    }

    [Fact]
    public void Add_DistinctLabels_AllKept()
    {
        var sink = new CompletionItemSink(CompletionMode.Default);
        sink.Add(new CompletionCandidate("alpha", LspCompletionItemKind.Keyword, SourceTag: "t"));
        sink.Add(new CompletionCandidate("beta",  LspCompletionItemKind.Function, SourceTag: "t"));
        sink.Add(new CompletionCandidate("gamma", LspCompletionItemKind.Variable, SourceTag: "t"));

        var list  = sink.Materialize();
        Assert.Equal(3, list.Items.Count());
    }

    // -------------------------------------------------------------------------
    // Insertion-order preservation
    // -------------------------------------------------------------------------

    [Fact]
    public void Materialize_ReturnsItemsInInsertionOrder()
    {
        var sink = new CompletionItemSink(CompletionMode.Default);
        var labels = new[] { "charlie", "alpha", "bravo" };
        foreach (var l in labels)
            sink.Add(new CompletionCandidate(l, LspCompletionItemKind.Keyword, SourceTag: "t"));

        var materialized = sink.Materialize().Items.Select(i => i.Label).ToList();
        Assert.Equal(labels, materialized);
    }

    // -------------------------------------------------------------------------
    // Defence-in-depth: RequiresQualification rejected in Default mode
    // -------------------------------------------------------------------------

    [Fact]
    public void Add_RequiresQualification_InDefaultMode_IsRejected()
    {
        var sink = new CompletionItemSink(CompletionMode.Default);
        var candidate = new CompletionCandidate(
            "fieldName",
            LspCompletionItemKind.Field,
            SourceTag: "t",
            Accessibility: SymbolAccessibility.RequiresQualification);

        sink.Add(candidate);

        var list = sink.Materialize();
        Assert.Empty(list.Items);
    }

    [Fact]
    public void Add_RequiresQualification_InDotMode_IsAccepted()
    {
        var sink = new CompletionItemSink(CompletionMode.Dot);
        var candidate = new CompletionCandidate(
            "fieldName",
            LspCompletionItemKind.Field,
            SourceTag: "t",
            Accessibility: SymbolAccessibility.RequiresQualification);

        sink.Add(candidate);

        var list = sink.Materialize();
        Assert.Single(list.Items);
        Assert.Equal("fieldName", list.Items.First().Label);
    }

    [Fact]
    public void Add_BareIdentifier_InDefaultMode_IsAccepted()
    {
        var sink = new CompletionItemSink(CompletionMode.Default);
        var candidate = new CompletionCandidate(
            "myVar",
            LspCompletionItemKind.Variable,
            SourceTag: "t",
            Accessibility: SymbolAccessibility.BareIdentifier);

        sink.Add(candidate);

        var list = sink.Materialize();
        Assert.Single(list.Items);
    }

    [Fact]
    public void Add_NullAccessibility_InDefaultMode_IsAccepted()
    {
        // Unclassified candidates (no Accessibility set) must not be filtered.
        var sink = new CompletionItemSink(CompletionMode.Default);
        var candidate = new CompletionCandidate("unclassified", LspCompletionItemKind.Keyword, SourceTag: "t");

        sink.Add(candidate);

        var list = sink.Materialize();
        Assert.Single(list.Items);
    }

    // -------------------------------------------------------------------------
    // SourceTag round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void Add_CandidateWithSourceTag_TagRoundTripsViaData()
    {
        var sink = new CompletionItemSink(CompletionMode.Default);
        var candidate = new CompletionCandidate(
            "println",
            LspCompletionItemKind.Function,
            SourceTag: "StdlibFunctionProvider");

        sink.Add(candidate);

        var item = sink.Materialize().Items.Single();
        Assert.Equal("StdlibFunctionProvider", item.Data?.ToString());
    }

    // -------------------------------------------------------------------------
    // InsertText / InsertTextFormat round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void Add_CandidateWithSnippetInsertText_RoundTripsThroughMaterialize()
    {
        // A snippet candidate with tab-stops must survive the sink round-trip intact.
        var sink = new CompletionItemSink(CompletionMode.Default);
        var candidate = new CompletionCandidate(
            "for",
            LspCompletionItemKind.Keyword,
            SourceTag: "SnippetProvider",
            InsertText: "for ${1:item} in ${2:collection} {\n\t$0\n}",
            InsertTextFormat: LspInsertTextFormat.Snippet);

        sink.Add(candidate);

        var item = sink.Materialize().Items.Single();
        Assert.Equal("for ${1:item} in ${2:collection} {\n\t$0\n}", item.InsertText);
        Assert.Equal(LspInsertTextFormat.Snippet, item.InsertTextFormat);
    }

    [Fact]
    public void Add_CandidateWithNullInsertText_MaterializedItemLeavesInsertTextUnset()
    {
        // When InsertText is null the IDE inserts the Label — the sink must NOT set InsertText.
        var sink = new CompletionItemSink(CompletionMode.Default);
        var candidate = new CompletionCandidate(
            "myVar",
            LspCompletionItemKind.Variable,
            SourceTag: "t");

        sink.Add(candidate);

        var item = sink.Materialize().Items.Single();
        Assert.True(
            string.IsNullOrEmpty(item.InsertText),
            "InsertText should be unset when the candidate carries no InsertText.");
    }

}
