namespace Stash.Lsp.Handlers;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class TypeDefinitionHandler : TypeDefinitionHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public TypeDefinitionHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    public override Task<LocationOrLocationLinks?> Handle(TypeDefinitionParams request, CancellationToken cancellationToken)
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

        // If cursor is on a namespace member (e.g., alias.member), resolve it
        if (symbol == null)
        {
            var nsMember = result.ResolveNamespaceMember(text!, (int)request.Position.Line, (int)request.Position.Character, word);
            if (nsMember != null)
            {
                symbol = nsMember.Value.Symbol;
            }
        }

        if (symbol == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        // If the symbol IS a struct or enum, return its own location
        if (symbol.Kind is Analysis.SymbolKind.Struct or Analysis.SymbolKind.Enum)
        {
            return MakeLocation(request.TextDocument.Uri, symbol, result);
        }

        // For variables, constants, parameters, loop variables — look up the TypeHint
        var typeName = symbol.TypeHint;
        if (typeName == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        // Search for the type declaration in the scope tree
        var typeSymbol = result.Symbols.All
            .FirstOrDefault(s => s.Name == typeName && s.Kind is Analysis.SymbolKind.Struct or Analysis.SymbolKind.Enum);

        if (typeSymbol != null)
        {
            return MakeLocation(request.TextDocument.Uri, typeSymbol, result);
        }

        return Task.FromResult<LocationOrLocationLinks?>(null);
    }

    private Task<LocationOrLocationLinks?> MakeLocation(DocumentUri requestUri, SymbolInfo symbol, AnalysisResult result)
    {
        // If the symbol was imported from another file, navigate there
        if (symbol.SourceUri != null)
        {
            var moduleInfo = _analysis.ImportResolver.GetModule(symbol.SourceUri.LocalPath);
            if (moduleInfo != null)
            {
                var originalSymbol = moduleInfo.Symbols.GetTopLevel()
                    .FirstOrDefault(s => s.Name == symbol.Name && s.Kind is Analysis.SymbolKind.Struct or Analysis.SymbolKind.Enum);
                if (originalSymbol != null)
                {
                    var location = new Location
                    {
                        Uri = DocumentUri.From(symbol.SourceUri),
                        Range = originalSymbol.Span.ToLspRange()
                    };
                    return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(location));
                }
            }
        }

        // Built-in types (line 0) have no navigable source
        if (symbol.Span.StartLine == 0)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var loc = new Location
        {
            Uri = requestUri,
            Range = symbol.Span.ToLspRange()
        };
        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(loc));
    }

    protected override TypeDefinitionRegistrationOptions CreateRegistrationOptions(
        TypeDefinitionCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };
}
