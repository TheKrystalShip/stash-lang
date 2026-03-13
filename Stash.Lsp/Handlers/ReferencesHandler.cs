namespace Stash.Lsp.Handlers;

using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class ReferencesHandler : ReferencesHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public ReferencesHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    public override Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            return Task.FromResult<LocationContainer?>(null);
        }
        var (result, word) = ctx.Value;

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;
        var references = result.Symbols.FindReferences(word, line, col);

        if (references.Count == 0)
        {
            return Task.FromResult<LocationContainer?>(null);
        }

        var locations = new System.Collections.Generic.List<Location>();
        foreach (var reference in references)
        {
            locations.Add(new Location
            {
                Uri = request.TextDocument.Uri,
                Range = reference.Span.ToLspRange()
            });
        }

        // Add cross-file references from files that import this module
        var crossFileRefs = _analysis.FindCrossFileReferences(uri, word);
        foreach (var (refUri, refSpan) in crossFileRefs)
        {
            locations.Add(new Location
            {
                Uri = DocumentUri.From(refUri),
                Range = refSpan.ToLspRange()
            });
        }

        return Task.FromResult<LocationContainer?>(new LocationContainer(locations));
    }

    protected override ReferenceRegistrationOptions CreateRegistrationOptions(
        ReferenceCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };
}
