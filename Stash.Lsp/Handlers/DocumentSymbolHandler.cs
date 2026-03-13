namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
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
        AnalysisResult? result = _analysis.GetCachedResult(request.TextDocument.Uri.ToUri());
        if (result == null)
        {
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);
        }

        var symbols = new List<SymbolInformationOrDocumentSymbol>();

        foreach (var (sym, children) in result.Symbols.GetHierarchicalSymbols())
        {
            // Skip synthetic built-in symbols (registered at line 0)
            if (sym.Span.StartLine == 0)
            {
                continue;
            }

            Range range = SpanToRange(sym.FullSpan ?? sym.Span);
            Range selectionRange = SpanToRange(sym.Span);

            DocumentSymbol docSymbol = new()
            {
                Name = sym.Name,
                Kind = MapSymbolKind(sym.Kind),
                Range = range,
                SelectionRange = selectionRange,
                Detail = sym.Detail
            };

            if (children.Count > 0)
            {
                docSymbol = docSymbol with
                {
                    Children = new Container<DocumentSymbol>(
                        children.Select(child => new DocumentSymbol
                        {
                            Name = child.Name,
                            Kind = MapSymbolKind(child.Kind),
                            Range = SpanToRange(child.FullSpan ?? child.Span),
                            SelectionRange = SpanToRange(child.Span),
                            Detail = child.Detail
                        }))
                };
            }

            symbols.Add(new SymbolInformationOrDocumentSymbol(docSymbol));
        }

        return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
            new SymbolInformationOrDocumentSymbolContainer(symbols));
    }

    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

    private static Range SpanToRange(Common.SourceSpan span) =>
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
        Analysis.SymbolKind.Namespace => LspSymbolKind.Namespace,
        _ => LspSymbolKind.Variable
    };
}
