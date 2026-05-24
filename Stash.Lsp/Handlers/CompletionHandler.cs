namespace Stash.Lsp.Handlers;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using Stash.Lsp.Completion;
using StashCompletionContext = Stash.Lsp.Completion.CompletionContext;

/// <summary>
/// Handles LSP <c>textDocument/completion</c> requests to provide autocompletion suggestions.
/// </summary>
/// <remarks>
/// <para>
/// Classifies the cursor context via <see cref="CursorContextClassifier"/> into one of five
/// <see cref="CompletionMode"/> values (Default, Dot, ImportString, AfterIs, AfterExtend),
/// then delegates to <see cref="CompletionDispatcher"/> which routes through the appropriate
/// provider pipeline and returns a deduplicated <see cref="CompletionList"/>.
/// </para>
/// </remarks>
public class CompletionHandler : CompletionHandlerBase
{
    /// <summary>The analysis engine used to obtain cached analysis results and symbol trees.</summary>
    private readonly AnalysisEngine _analysis;

    /// <summary>The document manager used to retrieve the current text of open files.</summary>
    private readonly DocumentManager _documents;

    private readonly ILogger<CompletionHandler> _logger;

    /// <summary>The dispatcher that routes completion requests through the appropriate provider pipeline.</summary>
    private readonly CompletionDispatcher _dispatcher;

    /// <summary>
    /// Initialises a new instance of <see cref="CompletionHandler"/> with the services
    /// needed to resolve completion items.
    /// </summary>
    /// <param name="analysis">Analysis engine providing cached <see cref="AnalysisResult"/> data.</param>
    /// <param name="documents">Document manager for reading open file contents.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="dispatcher">The completion dispatcher wired with all mode pipelines.</param>
    public CompletionHandler(
        AnalysisEngine analysis,
        DocumentManager documents,
        ILogger<CompletionHandler> logger,
        CompletionDispatcher dispatcher)
    {
        _analysis = analysis;
        _documents = documents;
        _logger = logger;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Processes the completion request and returns a list of completion items appropriate
    /// for the cursor context — import path strings, dot-access members, or the full symbol list.
    /// </summary>
    /// <param name="request">The completion request containing the document URI and cursor position.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="CompletionList"/> with matching completion items, or an empty list when inside a non-import string.</returns>
    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var line = (int)request.Position.Line;
        var col = (int)request.Position.Character;

        _logger.LogDebug("Completion request at {Uri}:{Line}:{Col}", request.TextDocument.Uri, line, col);

        var text = _documents.GetText(uri);
        string? currentLine = null;
        if (text != null)
        {
            var lines = text.Split('\n');
            if (line < lines.Length)
                currentLine = lines[line];
        }

        var mode = CursorContextClassifier.Classify(currentLine, col, out var dotPrefix);

        var cachedResult = _analysis.GetCachedResult(uri);
        var ctx = new StashCompletionContext(
            Uri: uri,
            LspLine: line,
            LspColumn: col,
            CurrentLine: currentLine,
            Mode: mode,
            DotPrefix: dotPrefix,
            Analysis: cachedResult,
            TriggerCharacter: request.Context?.TriggerCharacter?[0]);

        var result = _dispatcher.Run(ctx);
        _logger.LogDebug("Completion ({Mode}): {Count} items for {Uri}", mode, result.Items?.Count() ?? 0, request.TextDocument.Uri);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Handles <c>completionItem/resolve</c> requests. No additional data is resolved;
    /// the item is returned unchanged.
    /// </summary>
    /// <param name="request">The completion item to resolve.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>The same <paramref name="request"/> item unmodified.</returns>
    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        return Task.FromResult(request);
    }

    /// <summary>
    /// Creates the registration options specifying that this handler applies to <c>stash</c>
    /// language files, triggers on <c>.</c> and <c>(</c>, and does not use a resolve provider.
    /// </summary>
    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            TriggerCharacters = new Container<string>(".", "("),
            ResolveProvider = false
        };
}
