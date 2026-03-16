namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class ImplementationHandler : ImplementationHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public ImplementationHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    public override Task<LocationOrLocationLinks?> Handle(ImplementationParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var (result, word) = ctx.Value;
        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;

        var symbol = result.Symbols.FindDefinition(word, line, col);
        if (symbol == null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        // Determine the target type name
        string? typeName = null;

        if (symbol.Kind is Analysis.SymbolKind.Struct or Analysis.SymbolKind.Enum)
        {
            typeName = symbol.Name;
        }
        else if (symbol.TypeHint != null)
        {
            // Variable/param/const with a type hint — find usages of that type
            var typeSymbol = result.Symbols.All
                .FirstOrDefault(s => s.Name == symbol.TypeHint && s.Kind is Analysis.SymbolKind.Struct or Analysis.SymbolKind.Enum);
            if (typeSymbol != null)
                typeName = typeSymbol.Name;
        }

        if (typeName == null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var locations = new List<LocationOrLocationLink>();

        // Find all TypeUse references in the current document
        foreach (var reference in result.Symbols.References)
        {
            if (reference.Name == typeName && reference.Kind == ReferenceKind.TypeUse)
            {
                locations.Add(new Location
                {
                    Uri = request.TextDocument.Uri,
                    Range = reference.Span.ToLspRange()
                });
            }
        }

        // Find cross-file references
        var crossFileRefs = _analysis.FindCrossFileReferences(uri, typeName);
        foreach (var (refUri, refSpan) in crossFileRefs)
        {
            locations.Add(new Location
            {
                Uri = DocumentUri.From(refUri),
                Range = refSpan.ToLspRange()
            });
        }

        if (locations.Count == 0)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(locations));
    }

    protected override ImplementationRegistrationOptions CreateRegistrationOptions(
        ImplementationCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };
}
