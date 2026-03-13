namespace Stash.Lsp.Handlers;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
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
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var (result, word) = ctx.Value;
        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;
        var symbol = result.Symbols.FindDefinition(word, line, col);

        if (symbol == null)
        {
            var nsMember = result.ResolveNamespaceMember(text!, (int)request.Position.Line, (int)request.Position.Character, word);
            if (nsMember != null)
            {
                var (memberSymbol, moduleInfo) = nsMember.Value;
                var memberLocation = new Location
                {
                    Uri = DocumentUri.From(moduleInfo.Uri),
                    Range = memberSymbol.Span.ToLspRange()
                };
                return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(memberLocation));
            }

            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        // If symbol was imported from another file, look up its original definition in that module
        if (symbol.SourceUri != null)
        {
            var moduleInfo2 = _analysis.ImportResolver.GetModule(symbol.SourceUri.LocalPath);
            if (moduleInfo2 != null)
            {
                var originalSymbol = moduleInfo2.Symbols.GetTopLevel().FirstOrDefault(s => s.Name == word);
                if (originalSymbol != null)
                {
                    var importedLocation = new Location
                    {
                        Uri = DocumentUri.From(symbol.SourceUri),
                        Range = originalSymbol.Span.ToLspRange()
                    };
                    return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(importedLocation));
                }
            }

            // Fallback: jump to start of the imported file
            var fallbackLocation = new Location
            {
                Uri = DocumentUri.From(symbol.SourceUri),
                Range = new Range(new Position(0, 0), new Position(0, 0))
            };
            return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(fallbackLocation));
        }

        var location = new Location
        {
            Uri = request.TextDocument.Uri,
            Range = symbol.Span.ToLspRange()
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

