namespace Stash.Lsp.Handlers;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

/// <summary>
/// Handles LSP <c>textDocument/references</c> requests to find all references to the
/// symbol under the cursor across the current document and the open workspace.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="AnalysisEngine.GetContextAt"/> to identify the word at the cursor,
/// then calls <see cref="ScopeTree.FindReferences"/> to locate all in-document references.
/// Cross-file references in open workspace documents are appended via
/// <see cref="AnalysisEngine.FindCrossFileReferences"/>.
/// </para>
/// </remarks>
public class ReferencesHandler : ReferencesHandlerBase
{
    /// <summary>The analysis engine used to obtain context and find cross-file references.</summary>
    private readonly AnalysisEngine _analysis;

    /// <summary>The document manager used to retrieve the current text of open files.</summary>
    private readonly DocumentManager _documents;

    private readonly ILogger<ReferencesHandler> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="ReferencesHandler"/> with the services
    /// needed to locate symbol references.
    /// </summary>
    /// <param name="analysis">Analysis engine providing <see cref="AnalysisResult"/> data and cross-file reference search.</param>
    /// <param name="documents">Document manager for reading open file contents.</param>
    public ReferencesHandler(AnalysisEngine analysis, DocumentManager documents, ILogger<ReferencesHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// Processes the find-all-references request and returns all locations where the
    /// symbol under the cursor is referenced, including cross-file usages.
    /// </summary>
    /// <param name="request">The references request containing the document URI and cursor position.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>
    /// A <see cref="LocationContainer"/> with all reference locations, or
    /// <see langword="null"/> if no symbol can be resolved or no references are found.
    /// </returns>
    public override Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("FindReferences request at {Uri}:{Line}:{Col}", request.TextDocument.Uri, request.Position.Line, request.Position.Character);
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            return Task.FromResult<LocationContainer?>(null);
        }
        var (result, word) = ctx.Value;

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;
        var references = result.Symbols.FindReferences(word, line, col);

        if (references.Count == 0)
        {
            return Task.FromResult<LocationContainer?>(null);
        }

        var locations = new System.Collections.Generic.List<Location>();
        foreach (var reference in references)
        {
            locations.Add(new Location
            {
                Uri = request.TextDocument.Uri,
                Range = reference.Span.ToLspRange()
            });
        }

        // Add cross-file references from files that import this module
        var crossFileRefs = _analysis.FindCrossFileReferences(uri, word);
        foreach (var (refUri, refSpan) in crossFileRefs)
        {
            locations.Add(new Location
            {
                Uri = DocumentUri.From(refUri),
                Range = refSpan.ToLspRange()
            });
        }

        _logger.LogDebug("FindReferences: {Count} locations for {Uri}", locations.Count, request.TextDocument.Uri);
        return Task.FromResult<LocationContainer?>(new LocationContainer(locations));
    }

    /// <summary>
    /// Creates the registration options specifying that this handler applies to <c>stash</c> language files.
    /// </summary>
    protected override ReferenceRegistrationOptions CreateRegistrationOptions(
        ReferenceCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };
}
