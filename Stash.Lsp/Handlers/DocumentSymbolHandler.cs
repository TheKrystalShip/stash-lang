namespace Stash.Lsp.Handlers;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;
using LspSymbolKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind;

public class DocumentSymbolHandler : DocumentSymbolHandlerBase
{
    private readonly AnalysisEngine _analysis;

    public DocumentSymbolHandler(AnalysisEngine analysis)
    {
        _analysis = analysis;
    }

    public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
        DocumentSymbolParams request, CancellationToken cancellationToken)
    {
        var result = _analysis.GetCachedResult(request.TextDocument.Uri.ToUri());
        if (result == null)
        {
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);
        }

        var symbols = result.Symbols.GetTopLevel().Select(sym =>
        {
            var range = SpanToRange(sym.FullSpan ?? sym.Span);
            var selectionRange = SpanToRange(sym.Span);

            return new SymbolInformationOrDocumentSymbol(new DocumentSymbol
            {
                Name = sym.Name,
                Kind = MapSymbolKind(sym.Kind),
                Range = range,
                SelectionRange = selectionRange,
                Detail = sym.Detail
            });
        }).ToList();

        return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
            new SymbolInformationOrDocumentSymbolContainer(symbols));
    }

    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

    private static OmniSharp.Extensions.LanguageServer.Protocol.Models.Range SpanToRange(Stash.Common.SourceSpan span) =>
        new(
            new Position(span.StartLine - 1, span.StartColumn - 1),
            new Position(span.EndLine - 1, span.EndColumn - 1)
        );

    private static LspSymbolKind MapSymbolKind(Analysis.SymbolKind kind) => kind switch
    {
        Analysis.SymbolKind.Function => LspSymbolKind.Function,
        Analysis.SymbolKind.Variable => LspSymbolKind.Variable,
        Analysis.SymbolKind.Constant => LspSymbolKind.Constant,
        Analysis.SymbolKind.Struct => LspSymbolKind.Struct,
        Analysis.SymbolKind.Enum => LspSymbolKind.Enum,
        Analysis.SymbolKind.EnumMember => LspSymbolKind.EnumMember,
        Analysis.SymbolKind.Field => LspSymbolKind.Field,
        Analysis.SymbolKind.Parameter => LspSymbolKind.Variable,
        Analysis.SymbolKind.LoopVariable => LspSymbolKind.Variable,
        _ => LspSymbolKind.Variable
    };
}
