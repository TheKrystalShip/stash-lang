namespace Stash.Lsp.Handlers;

using System.Threading;
using System.Threading.Tasks;
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
        var result = _analysis.GetCachedResult(request.TextDocument.Uri.ToUri());
        if (result == null)
            return Task.FromResult<LocationContainer?>(null);

        var text = _documents.GetText(request.TextDocument.Uri.ToUri());
        if (text == null)
            return Task.FromResult<LocationContainer?>(null);

        var word = TextUtilities.FindWordAtPosition(text, request.Position.Line, request.Position.Character);
        if (word == null)
            return Task.FromResult<LocationContainer?>(null);

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;
        var references = result.Symbols.FindReferences(word, line, col);

        if (references.Count == 0)
            return Task.FromResult<LocationContainer?>(null);

        var locations = new System.Collections.Generic.List<Location>();
        foreach (var reference in references)
        {
            locations.Add(new Location
            {
                Uri = request.TextDocument.Uri,
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                    new Position(reference.Span.StartLine - 1, reference.Span.StartColumn - 1),
                    new Position(reference.Span.EndLine - 1, reference.Span.EndColumn - 1))
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
