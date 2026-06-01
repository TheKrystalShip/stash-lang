namespace Stash.Tests.Lsp;

using System;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using Stash.Lsp.Handlers;
using Stash.Tests.Analysis;
using Xunit;

/// <summary>
/// Tests for <see cref="HoverHandler"/> focusing on the <c>readonly</c> modifier
/// keyword hover and the distinction between the modifier and a plain identifier.
/// </summary>
public class HoverHandlerTests : AnalysisTestBase
{
    private static readonly Uri TestUri = new("file:///test.stash");

    private static Hover? GetHoverAt(string source, string word)
    {
        var lines = source.Split('\n');
        int line = -1, col = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            int c = lines[i].IndexOf(word, StringComparison.Ordinal);
            if (c >= 0)
            {
                line = i;
                col = c;
                break;
            }
        }
        if (line < 0)
            throw new InvalidOperationException($"Word '{word}' not found in source.");

        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var docs = new DocumentManager(NullLogger<DocumentManager>.Instance);
        var docUri = DocumentUri.From(TestUri);

        docs.Open(docUri.ToUri(), source, 1);
        engine.Analyze(docUri.ToUri(), source);

        var handler = new HoverHandler(engine, docs, NullLogger<HoverHandler>.Instance);
        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(docUri),
            Position = new Position(line, col)
        };
        return handler.Handle(request, CancellationToken.None).Result;
    }

    [Fact]
    public void HoverOnReadonlyModifier_ReturnsHover_WithDeepTransitiveSemanticsText()
    {
        // `readonly` used as a modifier before `const` — should resolve to keyword hover.
        const string Source = "readonly const Config = { host: \"localhost\" };";

        var hover = GetHoverAt(Source, "readonly");

        Assert.NotNull(hover);
        var md = hover!.Contents.MarkupContent!.Value;

        // Must mention deep / transitive semantics
        Assert.Contains("deep", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("transitive", md, StringComparison.OrdinalIgnoreCase);
        // Must mention ReadOnlyError
        Assert.Contains("ReadOnlyError", md, StringComparison.Ordinal);
        // Must identify it as a modifier / keyword
        Assert.Contains("modifier", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HoverOnReadonlyIdentifier_ReturnsVariableHover_NotKeywordHover()
    {
        // `readonly` used as a plain identifier binding — should resolve to the variable,
        // not the keyword hover.
        const string Source = "let readonly = true;";

        var hover = GetHoverAt(Source, "readonly");

        // Either null (no hover at this position) or a variable-kind hover.
        // It must NOT contain "declaration modifier" phrasing from the keyword hover.
        if (hover != null)
        {
            var md = hover.Contents.MarkupContent?.Value ?? "";
            Assert.DoesNotContain("declaration modifier", md, StringComparison.OrdinalIgnoreCase);
        }
        // If hover is null that's also fine — the identifier hover may not resolve
        // when the variable is unused.  The key invariant is no keyword hover fires.
    }
}
