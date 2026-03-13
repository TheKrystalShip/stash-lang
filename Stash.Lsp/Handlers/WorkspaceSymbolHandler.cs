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

public class WorkspaceSymbolHandler : WorkspaceSymbolsHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public WorkspaceSymbolHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    protected override WorkspaceSymbolRegistrationOptions CreateRegistrationOptions(
        WorkspaceSymbolCapability capability, ClientCapabilities clientCapabilities) =>
        new();

    public override Task<Container<WorkspaceSymbol>?> Handle(WorkspaceSymbolParams request,
        CancellationToken cancellationToken)
    {
        var query = request.Query ?? "";
        var symbols = new List<WorkspaceSymbol>();

        foreach (var uri in _documents.GetOpenDocumentUris())
        {
            var result = _analysis.GetCachedResult(uri);
            if (result == null) continue;

            var documentUri = DocumentUri.From(uri);

            foreach (var sym in result.Symbols.All)
            {
                // Skip loop variables and parameters for workspace-level search
                if (sym.Kind == Analysis.SymbolKind.LoopVariable ||
                    sym.Kind == Analysis.SymbolKind.Parameter)
                    continue;

                // Filter by query (case-insensitive substring match)
                if (query.Length > 0 &&
                    sym.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                symbols.Add(new WorkspaceSymbol
                {
                    Name = sym.Name,
                    Kind = MapSymbolKind(sym.Kind),
                    ContainerName = sym.ParentName,
                    Location = new Location
                    {
                        Uri = documentUri,
                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                            new Position(sym.Span.StartLine - 1, sym.Span.StartColumn - 1),
                            new Position(sym.Span.EndLine - 1, sym.Span.EndColumn - 1)
                        )
                    }
                });
            }
        }

        return Task.FromResult<Container<WorkspaceSymbol>?>(
            new Container<WorkspaceSymbol>(symbols));
    }

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
