namespace Stash.Lsp.Handlers;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

/// <summary>
/// Handles LSP <c>textDocument/documentHighlight</c> requests to highlight all occurrences
/// of the symbol under the cursor within the current document.
/// </summary>
/// <remarks>
/// <para>
/// Uses the <see cref="AnalysisEngine"/> to obtain the analysis context at the cursor position,
/// then queries the symbol table for all references to the resolved symbol. Each reference is
/// mapped to a <see cref="DocumentHighlight"/> with a kind of <c>Read</c> or <c>Write</c>
/// depending on the <see cref="ReferenceKind"/> recorded by the analyser.
/// </para>
/// </remarks>
public class DocumentHighlightHandler : DocumentHighlightHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;
    private readonly ILogger<DocumentHighlightHandler> _logger;

    /// <summary>
    /// Initialises the handler with the dependencies required to resolve symbol references.
    /// </summary>
    /// <param name="analysis">The analysis engine used to retrieve cached document results.</param>
    /// <param name="documents">The document manager used to obtain the current document text.</param>
    public DocumentHighlightHandler(AnalysisEngine analysis, DocumentManager documents, ILogger<DocumentHighlightHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// Processes the document highlight request and returns all occurrence ranges for the symbol at the cursor.
    /// </summary>
    /// <param name="request">The request containing the document URI and cursor position.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="DocumentHighlightContainer"/> with one entry per reference, or
    /// <see langword="null"/> if the cursor is not over a resolvable symbol.
    /// </returns>
    public override Task<DocumentHighlightContainer?> Handle(DocumentHighlightParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("DocumentHighlight request at {Uri}:{Line}:{Col}", request.TextDocument.Uri, request.Position.Line, request.Position.Character);
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            return Task.FromResult<DocumentHighlightContainer?>(null);
        }
        var (result, word) = ctx.Value;

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;
        var references = result.Symbols.FindReferences(word, line, col);

        if (references.Count == 0)
        {
            return Task.FromResult<DocumentHighlightContainer?>(null);
        }

        var highlights = new System.Collections.Generic.List<DocumentHighlight>();
        foreach (var reference in references)
        {
            highlights.Add(new DocumentHighlight
            {
                Range = reference.Span.ToLspRange(),
                Kind = reference.Kind == ReferenceKind.Write
                    ? DocumentHighlightKind.Write
                    : DocumentHighlightKind.Read
            });
        }

        _logger.LogDebug("DocumentHighlight: {Count} highlights for {Uri}", highlights.Count, request.TextDocument.Uri);
        return Task.FromResult<DocumentHighlightContainer?>(new DocumentHighlightContainer(highlights));
    }

    /// <summary>
    /// Creates the registration options restricting this handler to Stash language documents.
    /// </summary>
    /// <param name="capability">The client's document highlight capability descriptor.</param>
    /// <param name="clientCapabilities">The full set of client capabilities.</param>
    /// <returns>Registration options scoped to <c>stash</c> language documents.</returns>
    protected override DocumentHighlightRegistrationOptions CreateRegistrationOptions(
        DocumentHighlightCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };
}
