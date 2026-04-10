namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using Stash.Analysis;
using Stash.Lsp.Analysis;

public class TextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly DocumentManager _documents;
    private readonly AnalysisEngine _analysis;
    private readonly ILanguageServerFacade _server;
    private readonly LspSettings _settings;
    private readonly ILogger<TextDocumentSyncHandler> _logger;
    private readonly ConcurrentDictionary<Uri, CancellationTokenSource> _pendingAnalysis = new();

    public TextDocumentSyncHandler(DocumentManager documents, AnalysisEngine analysis, ILanguageServerFacade server, LspSettings settings, ILogger<TextDocumentSyncHandler> logger)
    {
        _documents = documents;
        _analysis = analysis;
        _server = server;
        _settings = settings;
        _logger = logger;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) =>
        new(uri, "stash");

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Document opened: {Uri}", request.TextDocument.Uri);
        var uri = request.TextDocument.Uri.ToUri();
        var text = request.TextDocument.Text;
        var version = request.TextDocument.Version ?? 0;

        _documents.Open(uri, text, version);
        AnalyzeAndPublishDiagnostics(uri, text);

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Document changed: {Uri}", request.TextDocument.Uri);
        var uri = request.TextDocument.Uri.ToUri();
        var version = request.TextDocument.Version ?? 0;

        var text = _documents.ApplyIncrementalChanges(uri, version, request.ContentChanges);
        if (text == null)
        {
            return Unit.Task;
        }

        ScheduleDebouncedAnalysis(uri);

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Document closed: {Uri}", request.TextDocument.Uri);
        var uri = request.TextDocument.Uri.ToUri();

        // Cancel any pending debounced analysis
        if (_pendingAnalysis.TryRemove(uri, out var pending))
        {
            _ = pending.CancelAsync();
            pending.Dispose();
        }

        _documents.Close(uri);

        // Clear diagnostics for closed document
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = request.TextDocument.Uri,
            Diagnostics = new Container<Diagnostic>()
        });

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            Change = TextDocumentSyncKind.Incremental,
            Save = new BooleanOr<SaveOptions>(new SaveOptions { IncludeText = false })
        };

    private void ScheduleDebouncedAnalysis(Uri uri)
    {
        // Cancel any previously scheduled analysis for this document
        if (_pendingAnalysis.TryRemove(uri, out var previous))
        {
            _ = previous.CancelAsync();
            previous.Dispose();
        }

        var cts = new CancellationTokenSource();
        _pendingAnalysis[uri] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_settings.DebounceDelayMs, cts.Token);

                // After the delay, get the latest text and analyze
                var text = _documents.GetText(uri);
                if (text != null && !cts.Token.IsCancellationRequested)
                {
                    AnalyzeAndPublishDiagnostics(uri, text);
                }
            }
            catch (OperationCanceledException)
            {
                // Debounce was cancelled by a newer change — expected
            }
            finally
            {
                _pendingAnalysis.TryRemove(uri, out _);
            }
        });
    }

    private void AnalyzeAndPublishDiagnostics(Uri uri, string text)
    {
        _logger.LogDebug("Analysis started: {Uri}", uri);
        // Invalidate this file's module cache so importers get fresh data
        if (uri.IsFile)
        {
            _analysis.InvalidateModule(uri.LocalPath);
        }

        var result = _analysis.Analyze(uri, text);
        var diagnostics = DiagnosticBuilder.Build(result);

        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.From(uri),
            Diagnostics = new Container<Diagnostic>(diagnostics)
        });

        // Re-analyze open files that directly or transitively import this file so they get updated diagnostics
        if (uri.IsFile)
        {
            foreach (var depUri in _analysis.GetTransitiveDependents(uri.LocalPath))
            {
                if (depUri == uri)
                {
                    continue;
                }

                var depText = _documents.GetText(depUri);
                if (depText == null)
                {
                    continue;
                }

                _analysis.InvalidateModule(depUri.IsFile ? depUri.LocalPath : depUri.ToString());
                var depResult = _analysis.Analyze(depUri, depText);
                var depDiagnostics = DiagnosticBuilder.Build(depResult);

                _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
                {
                    Uri = DocumentUri.From(depUri),
                    Diagnostics = new Container<Diagnostic>(depDiagnostics)
                });
            }
        }

        _logger.LogDebug("Analysis completed: {Uri}, {DiagCount} diagnostics", uri, diagnostics.Count);
        _server.Workspace.SendSemanticTokensRefresh(new SemanticTokensRefreshParams());
    }

}
