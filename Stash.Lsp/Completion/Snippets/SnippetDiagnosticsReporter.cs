namespace Stash.Lsp.Completion.Snippets;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Thin seam that surfaces snippet load failures to both the LSP client
/// (via a <c>window/showMessage</c> callback) and the server-side log.
/// </summary>
/// <remarks>
/// <para>
/// Reads exclusively from <see cref="ISnippetRegistry.LoadErrors"/> — the single source
/// of truth for snippet validation failures (per P2's plan correction). When LoadErrors
/// is empty, this reporter is a no-op (silent success).
/// </para>
/// <para>
/// <b>Failure isolation:</b> this reporter never throws. Any exception raised by the
/// callback or logger is caught and swallowed so that snippet reporting failures cannot
/// crash the LSP startup pipeline. The LSP-stays-up guarantee is the single most important
/// behavioural constraint of the snippet feature.
/// </para>
/// <para>
/// <b>Contract per <see cref="Report"/> invocation:</b>
/// <list type="bullet">
///   <item>Exactly one <see cref="ILogger.LogError(string, object[])"/> entry per error
///         (structured payload: <c>SnippetIdOrName</c>, <c>SourceLocation</c>, <c>Reason</c>)
///         so developers see every invalid snippet in the log.</item>
///   <item>Exactly one <c>showMessage</c> callback invocation (when LoadErrors is non-empty)
///         with <see cref="MessageType.Error"/> and a summary message naming the error count
///         and source — keeps the user-facing notification a single popup rather than N
///         popups, while preserving the detail in the log.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class SnippetDiagnosticsReporter
{
    private readonly ILogger<SnippetDiagnosticsReporter> _logger;

    /// <summary>Initialises a new <see cref="SnippetDiagnosticsReporter"/>.</summary>
    public SnippetDiagnosticsReporter(ILogger<SnippetDiagnosticsReporter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Surfaces every error in <paramref name="errors"/> via the log, then fires a single
    /// summary popup via <paramref name="showMessage"/>.
    /// </summary>
    /// <param name="showMessage">
    /// Callback used to surface the summary popup. Typically wraps
    /// <c>server.Window.ShowError(...)</c>; may be <see langword="null"/> in tests or
    /// when no window is available (the log path still fires).
    /// </param>
    /// <param name="errors">Validation errors collected from a snippet registry.</param>
    /// <param name="sourceName">Human-readable source label included in the summary popup.</param>
    public void Report(
        Action<MessageType, string>? showMessage,
        IReadOnlyList<SnippetLoadError> errors,
        string sourceName)
    {
        if (errors is null || errors.Count == 0) return;

        foreach (var err in errors)
        {
            try
            {
                _logger.LogError(
                    "Snippet validation failed: {SnippetIdOrName} ({SourceLocation}): {Reason}",
                    err.SnippetIdOrName,
                    err.SourceLocation,
                    err.Reason);
            }
            catch
            {
                // Never let logger failure crash LSP startup.
            }
        }

        if (showMessage is null) return;

        try
        {
            var message = errors.Count == 1
                ? $"Stash LSP: 1 invalid snippet in {sourceName} — see log for details."
                : $"Stash LSP: {errors.Count} invalid snippets in {sourceName} — see log for details.";
            showMessage(MessageType.Error, message);
        }
        catch
        {
            // Never let the callback failure crash LSP startup — the log entries above
            // already carry the full detail.
        }
    }
}
