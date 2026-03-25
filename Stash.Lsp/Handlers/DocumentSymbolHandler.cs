namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;
using LspSymbolKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind;

/// <summary>
/// Handles LSP <c>textDocument/documentSymbol</c> requests to provide a hierarchical
/// outline of all symbols declared in the current document.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="AnalysisEngine.GetCachedResult"/> to retrieve the latest
/// <see cref="AnalysisResult"/> for the file and builds a hierarchical
/// <see cref="DocumentSymbol"/> tree from <see cref="ScopeTree.GetHierarchicalSymbols"/>.
/// Top-level symbols such as functions, structs, enums, and constants are returned as
/// parent nodes; their nested members (fields, methods, enum members) are attached as
/// child nodes.
/// </para>
/// <para>
/// Synthetic built-in symbols registered at line 0 are excluded from the outline.
/// Each symbol uses the full span (<see cref="SymbolInfo.FullSpan"/>) for its range and
/// the name span for the selection range, so editors can highlight just the identifier
/// when the user navigates to it.
/// </para>
/// </remarks>
public class DocumentSymbolHandler : DocumentSymbolHandlerBase
{
    /// <summary>The analysis engine used to obtain cached symbol hierarchies.</summary>
    private readonly AnalysisEngine _analysis;

    private readonly ILogger<DocumentSymbolHandler> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="DocumentSymbolHandler"/> with the analysis
    /// engine used to retrieve document symbol trees.
    /// </summary>
    /// <param name="analysis">Analysis engine providing hierarchical <see cref="SymbolInfo"/> data.</param>
    public DocumentSymbolHandler(AnalysisEngine analysis, ILogger<DocumentSymbolHandler> logger)
    {
        _analysis = analysis;
        _logger = logger;
    }

    /// <summary>
    /// Processes the document-symbol request and returns a hierarchical list of symbols
    /// declared in the document, suitable for populating an editor outline view.
    /// </summary>
    /// <param name="request">The document-symbol request containing the document URI.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>
    /// A <see cref="SymbolInformationOrDocumentSymbolContainer"/> with the document's symbol hierarchy,
    /// or <see langword="null"/> if no cached analysis result is available.
    /// </returns>
    public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
        DocumentSymbolParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("DocumentSymbol request for {Uri}", request.TextDocument.Uri);
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

            Range range = (sym.FullSpan ?? sym.Span).ToLspRange();
            Range selectionRange = sym.Span.ToLspRange();

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
                            Range = (child.FullSpan ?? child.Span).ToLspRange(),
                            SelectionRange = child.Span.ToLspRange(),
                            Detail = child.Detail
                        }))
                };
            }

            symbols.Add(new SymbolInformationOrDocumentSymbol(docSymbol));
        }

        _logger.LogDebug("DocumentSymbol: {Count} symbols for {Uri}", symbols.Count, request.TextDocument.Uri);
        return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
            new SymbolInformationOrDocumentSymbolContainer(symbols));
    }

    /// <summary>
    /// Creates the registration options specifying that this handler applies to <c>stash</c> language files.
    /// </summary>
    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

    /// <summary>
    /// Maps a Stash <see cref="Analysis.SymbolKind"/> to the corresponding LSP
    /// <see cref="LspSymbolKind"/> for the document outline view.
    /// </summary>
    /// <param name="kind">The Stash symbol kind to map.</param>
    /// <returns>The equivalent LSP symbol kind.</returns>
    private static LspSymbolKind MapSymbolKind(Analysis.SymbolKind kind) => kind switch
    {
        Analysis.SymbolKind.Function => LspSymbolKind.Function,
        Analysis.SymbolKind.Variable => LspSymbolKind.Variable,
        Analysis.SymbolKind.Constant => LspSymbolKind.Constant,
        Analysis.SymbolKind.Struct => LspSymbolKind.Struct,
        Analysis.SymbolKind.Enum => LspSymbolKind.Enum,
        Analysis.SymbolKind.EnumMember => LspSymbolKind.EnumMember,
        Analysis.SymbolKind.Field => LspSymbolKind.Field,
        Analysis.SymbolKind.Method => LspSymbolKind.Method,
        Analysis.SymbolKind.Parameter => LspSymbolKind.Variable,
        Analysis.SymbolKind.LoopVariable => LspSymbolKind.Variable,
        Analysis.SymbolKind.Namespace => LspSymbolKind.Namespace,
        _ => LspSymbolKind.Variable
    };
}
