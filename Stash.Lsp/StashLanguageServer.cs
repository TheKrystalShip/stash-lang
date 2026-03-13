namespace Stash.Lsp;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;
using Stash.Lsp.Analysis;
using Stash.Lsp.Handlers;

public static class StashLanguageServer
{
    public static async Task RunAsync()
    {
        var server = await LanguageServer.From(options =>
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Warning);
                })
                .WithServices(services =>
                {
                    services.AddSingleton<DocumentManager>();
                    services.AddSingleton<AnalysisEngine>();
                })
                .WithHandler<TextDocumentSyncHandler>()
                .WithHandler<DocumentSymbolHandler>()
                .WithHandler<HoverHandler>()
                .WithHandler<DefinitionHandler>()
                .WithHandler<CompletionHandler>()
                .WithHandler<ReferencesHandler>()
                .WithHandler<DocumentHighlightHandler>()
                .WithHandler<RenameHandler>()
                .WithHandler<SignatureHelpHandler>()
                .WithHandler<SemanticTokensHandler>()
                .WithHandler<FoldingRangeHandler>()
                .WithHandler<SelectionRangeHandler>()
                .WithHandler<DocumentLinkHandler>()
                .WithHandler<CodeActionHandler>()
                .WithHandler<WorkspaceSymbolHandler>()
                .WithHandler<InlayHintHandler>()
                .WithHandler<CodeLensHandler>()
                .OnInitialize((server, request, cancellationToken) =>
                {
                    return Task.CompletedTask;
                })
                .OnInitialized((server, request, response, cancellationToken) =>
                {
                    return Task.CompletedTask;
                })
        ).ConfigureAwait(false);

        await server.WaitForExit.ConfigureAwait(false);
    }
}
