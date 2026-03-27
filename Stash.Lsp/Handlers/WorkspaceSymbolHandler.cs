namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using Microsoft.Extensions.Logging;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using StashSymbolKind = Stash.Analysis.SymbolKind;
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

    private readonly ILogger<WorkspaceSymbolHandler> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="WorkspaceSymbolHandler"/> with the services
    /// needed to search symbols across open workspace files.
    /// </summary>
    /// <param name="analysis">Analysis engine providing <see cref="AnalysisResult"/> data per document.</param>
    /// <param name="documents">Document manager for enumerating open document URIs.</param>
    public WorkspaceSymbolHandler(AnalysisEngine analysis, DocumentManager documents, ILogger<WorkspaceSymbolHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _logger = logger;
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
        _logger.LogDebug("WorkspaceSymbol request: {Query}", request.Query);
        var query = request.Query ?? "";
        var symbols = new List<WorkspaceSymbol>();

        // Combine open document URIs with background-indexed URIs
        var uris = new HashSet<Uri>(_documents.GetOpenDocumentUris());
        foreach (var cachedUri in _analysis.GetAllCachedUris())
        {
            uris.Add(cachedUri);
        }

        foreach (var uri in uris)
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
                if (sym.Kind == StashSymbolKind.LoopVariable ||
                    sym.Kind == StashSymbolKind.Parameter)
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

        _logger.LogDebug("WorkspaceSymbol: {Count} symbols matching '{Query}'", symbols.Count, request.Query);
        return Task.FromResult<Container<WorkspaceSymbol>?>(
            new Container<WorkspaceSymbol>(symbols));
    }

    /// <summary>
    /// Maps a Stash <see cref="SymbolKind"/> to the corresponding LSP
    /// <see cref="LspSymbolKind"/> for workspace symbol results.
    /// </summary>
    /// <param name="kind">The Stash symbol kind to map.</param>
    /// <returns>The equivalent LSP symbol kind.</returns>
    private static LspSymbolKind MapSymbolKind(StashSymbolKind kind) => kind switch
    {
        StashSymbolKind.Function => LspSymbolKind.Function,
        StashSymbolKind.Variable => LspSymbolKind.Variable,
        StashSymbolKind.Constant => LspSymbolKind.Constant,
        StashSymbolKind.Struct => LspSymbolKind.Struct,
        StashSymbolKind.Enum => LspSymbolKind.Enum,
        StashSymbolKind.EnumMember => LspSymbolKind.EnumMember,
        StashSymbolKind.Field => LspSymbolKind.Field,
        StashSymbolKind.Namespace => LspSymbolKind.Namespace,
        _ => LspSymbolKind.Variable
    };
}
