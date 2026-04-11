namespace Stash.Lsp.Analysis;

using Stash.Analysis;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using Stash.Common;

/// <summary>
/// Scans workspace folders for <c>.stash</c> files in the background and feeds them
/// through the analysis engine to build a complete cross-file reference index.
/// </summary>
public sealed class WorkspaceScanner : IDisposable
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;
    private readonly LspSettings _settings;
    private readonly ILanguageServerFacade _server;
    private readonly ILogger<WorkspaceScanner> _logger;

    private Channel<string> _queue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });

    private readonly List<string> _workspaceRoots = [];
    private readonly object _rootsLock = new();
    private CancellationTokenSource? _cts;
    private Task? _processingTask;

    public WorkspaceScanner(
        AnalysisEngine analysis,
        DocumentManager documents,
        LspSettings settings,
        ILanguageServerFacade server,
        ILogger<WorkspaceScanner> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _settings = settings;
        _server = server;
        _logger = logger;
    }

    /// <summary>
    /// Returns a snapshot of the current workspace root paths.
    /// </summary>
    public IReadOnlyList<string> GetRoots()
    {
        lock (_rootsLock)
        {
            return new List<string>(_workspaceRoots);
        }
    }

    /// <summary>
    /// Sets workspace root paths and starts the background scan if indexing is enabled.
    /// </summary>
    public void SetRoots(IEnumerable<string> roots)
    {
        lock (_rootsLock)
        {
            _workspaceRoots.Clear();
            _workspaceRoots.AddRange(roots);
        }

        if (_settings.WorkspaceIndexingEnabled && _workspaceRoots.Count > 0)
        {
            StartScan();
        }
    }

    /// <summary>
    /// Starts or restarts the background scanning process.
    /// </summary>
    public void StartScan()
    {
        StopScan();

        _queue = Channel.CreateBounded<string>(
            new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });

        if (!_settings.WorkspaceIndexingEnabled || _workspaceRoots.Count == 0)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // Enumerate files and queue them
        _ = Task.Run(() => EnqueueAllFilesAsync(token), token);

        // Start the consumer
        _processingTask = Task.Run(() => ProcessQueueAsync(token), token);
    }

    /// <summary>
    /// Stops the background scanning process.
    /// </summary>
    public void StopScan()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        _queue.Writer.TryComplete();
        _processingTask = null;
    }

    /// <summary>
    /// Queues a file for re-analysis after it was changed on disk.
    /// </summary>
    public void OnFileChanged(string absolutePath)
    {
        if (!_settings.WorkspaceIndexingEnabled)
        {
            return;
        }

        _analysis.InvalidateModule(absolutePath);
        _queue.Writer.TryWrite(absolutePath);
    }

    /// <summary>
    /// Queues a newly created file for analysis.
    /// </summary>
    public void OnFileCreated(string absolutePath)
    {
        if (!_settings.WorkspaceIndexingEnabled)
        {
            return;
        }

        _queue.Writer.TryWrite(absolutePath);
    }

    /// <summary>
    /// Removes a deleted file from the analysis cache.
    /// </summary>
    public void OnFileDeleted(string absolutePath)
    {
        _analysis.InvalidateModule(absolutePath);
    }

    public void Dispose()
    {
        StopScan();
    }

    private async Task EnqueueAllFilesAsync(CancellationToken ct)
    {
        List<string> roots;
        lock (_rootsLock)
        {
            roots = new List<string>(_workspaceRoots);
        }

        int totalQueued = 0;

        foreach (string root in roots)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            if (!Directory.Exists(root))
            {
                _logger.LogWarning("Workspace root does not exist: {Root}", root);
                continue;
            }

            var ignore = StashIgnore.Load(root);

            _logger.LogInformation("Workspace indexing: scanning {Root}", root);

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.stash", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("Failed to enumerate files in {Root}: {Error}", root, ex.Message);
                continue;
            }

            foreach (string file in files)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                string relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (ignore.IsExcluded(relativePath))
                {
                    continue;
                }

                await _queue.Writer.WriteAsync(file, ct);
                totalQueued++;
            }
        }

        _logger.LogInformation("Workspace indexing: queued {Count} files for analysis", totalQueued);
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        int processed = 0;

        try
        {
            await foreach (string filePath in _queue.Reader.ReadAllAsync(ct))
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    var uri = new Uri(filePath);

                    // Skip files that are currently open — they have fresher analysis
                    if (_documents.GetText(uri) != null)
                    {
                        continue;
                    }

                    string source = await File.ReadAllTextAsync(filePath, ct);
                    _analysis.Analyze(uri, source);
                    processed++;

                    // After every 10 files, send refresh notifications so
                    // reference counts tick up progressively
                    if (processed % 10 == 0)
                    {
                        SendRefreshNotifications();
                        _logger.LogDebug("Workspace indexing: analyzed {Count} files so far", processed);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
                {
                    _logger.LogDebug("Skipping file {Path}: {Error}", filePath, ex.Message);
                }

                // Yield to let request handlers run
                await Task.Yield();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }

        if (processed > 0)
        {
            SendRefreshNotifications();
            _logger.LogInformation("Workspace indexing complete: analyzed {Count} files", processed);
        }
    }

    private void SendRefreshNotifications()
    {
        try
        {
            _server.Workspace.SendCodeLensRefresh(new CodeLensRefreshParams());
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to send codeLens refresh: {Error}", ex.Message);
        }

        try
        {
            _server.Workspace.SendSemanticTokensRefresh(new SemanticTokensRefreshParams());
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to send semanticTokens refresh: {Error}", ex.Message);
        }
    }
}
