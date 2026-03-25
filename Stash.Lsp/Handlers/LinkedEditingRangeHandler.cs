namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Microsoft.Extensions.Logging;
using Stash.Lsp.Analysis;

/// <summary>
/// Handles LSP <c>textDocument/linkedEditingRange</c> requests to enable simultaneous
/// renaming of all occurrences of a symbol within the current document.
/// </summary>
/// <remarks>
/// <para>
/// When a client that supports linked editing moves the cursor onto a symbol, this handler
/// returns all reference spans for that symbol so the editor can keep them in sync as the
/// user types.  The handler uses the same reference-finding logic as
/// <see cref="DocumentHighlightHandler"/> but requires at least two occurrences before
/// returning a result.
/// </para>
/// <para>
/// A word pattern of <c>\w+</c> is included in the response to guide the editor's selection
/// behaviour.
/// </para>
/// </remarks>
public class LinkedEditingRangeHandler : LinkedEditingRangeHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;
    private readonly ILogger<LinkedEditingRangeHandler> _logger;

    /// <summary>
    /// Initialises the handler with the required analysis engine and document manager.
    /// </summary>
    /// <param name="analysis">The analysis engine used to retrieve cached document results.</param>
    /// <param name="documents">The document manager used to obtain the current document text.</param>
    public LinkedEditingRangeHandler(AnalysisEngine analysis, DocumentManager documents, ILogger<LinkedEditingRangeHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// Processes the linked editing range request and returns all occurrence ranges for the symbol at the cursor.
    /// </summary>
    /// <param name="request">The request containing the document URI and cursor position.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="LinkedEditingRanges"/> instance with all reference spans and a word pattern,
    /// or <see langword="null"/> if the cursor is not over a symbol with two or more occurrences.
    /// </returns>
    public override Task<LinkedEditingRanges> Handle(LinkedEditingRangeParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("LinkedEditingRange request at {Uri}:{Line}:{Col}", request.TextDocument.Uri, request.Position.Line, request.Position.Character);
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var lspLine = (int)request.Position.Line;
        var lspCharacter = (int)request.Position.Character;

        var ctx = _analysis.GetContextAt(uri, text, lspLine, lspCharacter);
        if (ctx == null)
        {
            _logger.LogTrace("LinkedEditingRange: no linked ranges at {Uri}", request.TextDocument.Uri);
            return Task.FromResult<LinkedEditingRanges>(null!);
        }

        var (result, word) = ctx.Value;
        var line = lspLine + 1;
        var col = lspCharacter + 1;

        var references = result.Symbols.FindReferences(word, line, col);
        if (references.Count < 2)
        {
            _logger.LogTrace("LinkedEditingRange: no linked ranges at {Uri}", request.TextDocument.Uri);
            return Task.FromResult<LinkedEditingRanges>(null!);
        }

        var ranges = new List<Range>();
        foreach (var reference in references)
        {
            ranges.Add(reference.Span.ToLspRange());
        }

        _logger.LogDebug("LinkedEditingRange: {Count} ranges for {Uri}", ranges.Count, request.TextDocument.Uri);
        return Task.FromResult<LinkedEditingRanges>(new LinkedEditingRanges
        {
            Ranges = new Container<Range>(ranges),
            WordPattern = @"\w+"
        });
    }

    /// <summary>
    /// Creates the registration options restricting this handler to Stash language documents.
    /// </summary>
    /// <param name="capability">The client's linked editing range capability descriptor.</param>
    /// <param name="clientCapabilities">The full set of client capabilities.</param>
    /// <returns>Registration options scoped to <c>stash</c> language documents.</returns>
    protected override LinkedEditingRangeRegistrationOptions CreateRegistrationOptions(
        LinkedEditingRangeClientCapabilities capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };
}
