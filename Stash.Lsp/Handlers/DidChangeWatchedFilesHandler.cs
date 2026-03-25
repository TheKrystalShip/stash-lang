namespace Stash.Lsp.Handlers;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using Stash.Lsp.Analysis;

/// <summary>
/// Handles <c>workspace/didChangeWatchedFiles</c> notifications to keep the background
/// workspace index up to date when <c>.stash</c> files are created, changed, or deleted
/// outside of the editor.
/// </summary>
public class DidChangeWatchedFilesHandler : DidChangeWatchedFilesHandlerBase
{
    private readonly WorkspaceScanner _scanner;
    private readonly ILogger<DidChangeWatchedFilesHandler> _logger;

    public DidChangeWatchedFilesHandler(WorkspaceScanner scanner, ILogger<DidChangeWatchedFilesHandler> logger)
    {
        _scanner = scanner;
        _logger = logger;
    }

    public override Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken cancellationToken)
    {
        foreach (var change in request.Changes)
        {
            try
            {
                var uri = change.Uri.ToUri();
                if (!uri.IsFile) continue;

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
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error processing file watch event: {Error}", ex.Message);
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
                })
        };
}
