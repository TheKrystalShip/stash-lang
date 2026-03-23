namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using Stash.Lsp.Analysis;
using LspSymbolKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind;

/// <summary>
/// Handles LSP <c>workspace/symbol</c> requests to search for symbols across all
/// currently open workspace documents.
/// </summary>
/// <remarks>
/// <para>
/// Iterates over every open document URI from <see cref="DocumentManager.GetOpenDocumentUris"/>
/// and queries the cached <see cref="AnalysisResult"/> for each file. All symbols whose
/// names contain the query string (case-insensitive substring match) are included in the
/// response as <see cref="WorkspaceSymbol"/> entries.
/// </para>
/// <para>
/// Loop variables, parameters, and synthetic built-in symbols registered at line 0 are
/// excluded from results to keep the workspace index focused on top-level and named
/// declarations.
/// </para>
/// </remarks>
public class WorkspaceSymbolHandler : WorkspaceSymbolsHandlerBase
{
    /// <summary>The analysis engine used to obtain cached analysis results for open documents.</summary>
    private readonly AnalysisEngine _analysis;

    /// <summary>The document manager used to enumerate currently open document URIs.</summary>
    private readonly DocumentManager _documents;

    /// <summary>
    /// Initialises a new instance of <see cref="WorkspaceSymbolHandler"/> with the services
    /// needed to search symbols across open workspace files.
    /// </summary>
    /// <param name="analysis">Analysis engine providing <see cref="AnalysisResult"/> data per document.</param>
    /// <param name="documents">Document manager for enumerating open document URIs.</param>
    public WorkspaceSymbolHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    /// <summary>
    /// Creates the registration options for the workspace symbol handler.
    /// </summary>
    protected override WorkspaceSymbolRegistrationOptions CreateRegistrationOptions(
        WorkspaceSymbolCapability capability, ClientCapabilities clientCapabilities) =>
        new();

    /// <summary>
    /// Processes the workspace-symbol request and returns all matching symbols from
    /// every open document whose name contains the query string.
    /// </summary>
    /// <param name="request">The workspace-symbol request containing the search query string.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>
    /// A <see cref="Container{WorkspaceSymbol}"/> with all matching symbols across open documents.
    /// </returns>
    public override Task<Container<WorkspaceSymbol>?> Handle(WorkspaceSymbolParams request,
        CancellationToken cancellationToken)
    {
        var query = request.Query ?? "";
        var symbols = new List<WorkspaceSymbol>();

        foreach (var uri in _documents.GetOpenDocumentUris())
        {
            var result = _analysis.GetCachedResult(uri);
            if (result == null)
            {
                continue;
            }

            var documentUri = DocumentUri.From(uri);

            foreach (var sym in result.Symbols.All)
            {
                // Skip loop variables and parameters for workspace-level search
                if (sym.Kind == Analysis.SymbolKind.LoopVariable ||
                    sym.Kind == Analysis.SymbolKind.Parameter)
                {
                    continue;
                }

                // Skip synthetic built-in symbols (registered at line 0)
                if (sym.Span.StartLine == 0)
                {
                    continue;
                }

                // Filter by query (case-insensitive substring match)
                if (query.Length > 0 &&
                    sym.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                symbols.Add(new WorkspaceSymbol
                {
                    Name = sym.Name,
                    Kind = MapSymbolKind(sym.Kind),
                    ContainerName = sym.ParentName,
                    Location = new Location
                    {
                        Uri = documentUri,
                        Range = sym.Span.ToLspRange()
                    }
                });
            }
        }

        return Task.FromResult<Container<WorkspaceSymbol>?>(
            new Container<WorkspaceSymbol>(symbols));
    }

    /// <summary>
    /// Maps a Stash <see cref="Analysis.SymbolKind"/> to the corresponding LSP
    /// <see cref="LspSymbolKind"/> for workspace symbol results.
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
        Analysis.SymbolKind.Namespace => LspSymbolKind.Namespace,
        _ => LspSymbolKind.Variable
    };
}
