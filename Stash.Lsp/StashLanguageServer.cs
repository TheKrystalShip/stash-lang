namespace Stash.Lsp;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;
using Stash.Lsp.Analysis;
using Stash.Lsp.Handlers;

/// <summary>
/// Entry point for the Stash Language Server Protocol server.
/// </summary>
/// <remarks>
/// Configures and starts an <see cref="LanguageServer"/> over stdio, registering all
/// LSP request handlers, services, and lifecycle callbacks.
/// </remarks>
public static class StashLanguageServer
{
    /// <summary>
    /// Starts the LSP server, registers all handlers and services, and waits for the client to exit.
    /// </summary>
    /// <returns>A <see cref="Task"/> that completes when the language server shuts down.</returns>
    public static async Task RunAsync()
    {
        var settings = new LspSettings();

        var server = await LanguageServer.From(options =>
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddFilter((category, level) => level >= settings.LogLevel);
                })
                .WithServices(services =>
                {
                    services.AddSingleton(settings);
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
                .WithHandler<PrepareRenameHandler>()
                .WithHandler<SignatureHelpHandler>()
                .WithHandler<SemanticTokensHandler>()
                .WithHandler<FoldingRangeHandler>()
                .WithHandler<SelectionRangeHandler>()
                .WithHandler<DocumentLinkHandler>()
                .WithHandler<CodeActionHandler>()
                .WithHandler<WorkspaceSymbolHandler>()
                .WithHandler<InlayHintHandler>()
                .WithHandler<CodeLensHandler>()
                .WithHandler<FormattingHandler>()
                .WithHandler<CallHierarchyHandler>()
                .WithHandler<LinkedEditingRangeHandler>()
                .WithHandler<TypeDefinitionHandler>()
                .WithHandler<ImplementationHandler>()
                .WithHandler<ConfigurationHandler>()
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
