namespace Stash.Lsp.Handlers;

using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class DefinitionHandler : DefinitionHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public DefinitionHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        var result = _analysis.GetCachedResult(request.TextDocument.Uri.ToUri());
        if (result == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var text = _documents.GetText(request.TextDocument.Uri.ToUri());
        if (text == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var word = TextUtilities.FindWordAtPosition(text, request.Position.Line, request.Position.Character);
        if (word == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;
        var symbol = result.Symbols.FindDefinition(word, line, col);

        if (symbol == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var location = new Location
        {
            Uri = request.TextDocument.Uri,
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                new Position(symbol.Span.StartLine - 1, symbol.Span.StartColumn - 1),
                new Position(symbol.Span.EndLine - 1, symbol.Span.EndColumn - 1)
            )
        };

        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(location));
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

}

