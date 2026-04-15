namespace Stash.Lsp.Handlers;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using Stash.Analysis;
using Stash.Lsp.Analysis;

/// <summary>
/// Handles <c>workspace/didChangeWatchedFiles</c> notifications to keep the background
/// workspace index up to date when <c>.stash</c> files are created, changed, or deleted
/// outside of the editor.
/// </summary>
/// <remarks>
/// When files inside a <c>stashes/</c> directory are created or deleted (e.g. after
/// <c>stash pkg install</c> or <c>stash pkg remove</c>), all open documents are
/// re-analyzed so that import diagnostics update immediately.
/// </remarks>
public class DidChangeWatchedFilesHandler : DidChangeWatchedFilesHandlerBase
{
    private readonly WorkspaceScanner _scanner;
    private readonly DocumentManager _documents;
    private readonly AnalysisEngine _analysis;
    private readonly ILanguageServerFacade _server;
    private readonly ILogger<DidChangeWatchedFilesHandler> _logger;

    public DidChangeWatchedFilesHandler(
        WorkspaceScanner scanner,
        DocumentManager documents,
        AnalysisEngine analysis,
        ILanguageServerFacade server,
        ILogger<DidChangeWatchedFilesHandler> logger)
    {
        _scanner = scanner;
        _documents = documents;
        _analysis = analysis;
        _server = server;
        _logger = logger;
    }

    public override Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken cancellationToken)
    {
        bool packageFilesChanged = false;
        bool configFilesChanged = false;

        foreach (var change in request.Changes)
        {
            try
            {
                var uri = change.Uri.ToUri();
                if (!uri.IsFile)
                {
                    continue;
                }

                string path = uri.LocalPath;
                _logger.LogDebug("File watch event: {Type} {Path}", change.Type, path);

                switch (change.Type)
                {
                    case FileChangeType.Created:
                        _scanner.OnFileCreated(path);
                        break;
                    case FileChangeType.Changed:
                        _scanner.OnFileChanged(path);
                        break;
                    case FileChangeType.Deleted:
                        _scanner.OnFileDeleted(path);
                        break;
                }

                string fileName = Path.GetFileName(path);
                if (fileName is ".stashcheck" or ".stashformat")
                {
                    configFilesChanged = true;
                }

                if (change.Type is FileChangeType.Created or FileChangeType.Deleted
                    && IsInsideStashesDirectory(path))
                {
                    packageFilesChanged = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error processing file watch event: {Error}", ex.Message);
            }
        }

        if (packageFilesChanged)
        {
            try
            {
                ReAnalyzeOpenDocuments();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error re-analyzing open documents after package change: {Error}", ex.Message);
            }
        }

        if (configFilesChanged)
        {
            try
            {
                _logger.LogInformation("Config files changed — invalidating caches and re-analyzing open documents");
                _analysis.InvalidateAllContentCaches();
                ReAnalyzeOpenDocuments();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error re-analyzing open documents after config change: {Error}", ex.Message);
            }
        }

        return Unit.Task;
    }

    protected override DidChangeWatchedFilesRegistrationOptions CreateRegistrationOptions(
        DidChangeWatchedFilesCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            Watchers = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher>(
                new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher
                {
                    GlobPattern = new GlobPattern("**/*.stash"),
                    Kind = WatchKind.Create | WatchKind.Change | WatchKind.Delete
                },
                new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher
                {
                    GlobPattern = new GlobPattern("**/.stashcheck"),
                    Kind = WatchKind.Create | WatchKind.Change | WatchKind.Delete
                },
                new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher
                {
                    GlobPattern = new GlobPattern("**/.stashformat"),
                    Kind = WatchKind.Create | WatchKind.Change | WatchKind.Delete
                })
        };

    private static bool IsInsideStashesDirectory(string path)
    {
        return path.Contains("/stashes/", StringComparison.Ordinal)
            || path.Contains("\\stashes\\", StringComparison.Ordinal);
    }

    /// <summary>
    /// Re-analyzes all currently open documents and publishes updated diagnostics.
    /// Called when package files are created or deleted so that import resolution
    /// picks up the changes on disk.
    /// </summary>
    private void ReAnalyzeOpenDocuments()
    {
        _logger.LogInformation("Package files changed — re-analyzing open documents");

        foreach (var uri in _documents.GetOpenDocumentUris())
        {
            try
            {
                var text = _documents.GetText(uri);
                if (text == null)
                {
                    continue;
                }

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
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to re-analyze {Uri}: {Error}", uri, ex.Message);
            }
        }

        _server.Workspace.SendSemanticTokensRefresh(new SemanticTokensRefreshParams());
    }
}
