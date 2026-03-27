namespace Stash.Lsp.Handlers;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Lsp.Analysis;

/// <summary>
/// Handles LSP <c>textDocument/prepareRename</c> requests to validate that the symbol
/// under the cursor is renameable and to provide the initial placeholder range.
/// </summary>
/// <remarks>
/// <para>
/// This handler is invoked by the client before <see cref="RenameHandler"/> to allow the
/// server to confirm that a rename can proceed. Uses <see cref="AnalysisEngine.GetContextAt"/>
/// and <see cref="ScopeTree.FindReferences"/> to verify that at least one reference to the
/// symbol exists. When confirmed, it returns the span of the reference at the cursor
/// (or the first known reference as a fallback) as a <see cref="PlaceholderRange"/> so
/// the editor can pre-populate the rename input field.
/// </para>
/// <para>
/// Returns <see langword="null"/> (rejecting the rename) when no symbol or references are
/// found at the cursor position.
/// </para>
/// </remarks>
public class PrepareRenameHandler : PrepareRenameHandlerBase
{
    /// <summary>The analysis engine used to obtain context and validate rename eligibility.</summary>
    private readonly AnalysisEngine _analysis;

    /// <summary>The document manager used to retrieve the current text of open files.</summary>
    private readonly DocumentManager _documents;

    private readonly ILogger<PrepareRenameHandler> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="PrepareRenameHandler"/> with the services
    /// needed to validate rename requests.
    /// </summary>
    /// <param name="analysis">Analysis engine providing <see cref="AnalysisResult"/> data and reference lookup.</param>
    /// <param name="documents">Document manager for reading open file contents.</param>
    public PrepareRenameHandler(AnalysisEngine analysis, DocumentManager documents, ILogger<PrepareRenameHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// Validates the rename request and returns a <see cref="PlaceholderRange"/> containing
    /// the range of the symbol at the cursor and its current name as the placeholder text.
    /// </summary>
    /// <param name="request">The prepare-rename request containing the document URI and cursor position.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>
    /// A <see cref="RangeOrPlaceholderRange"/> with the symbol range and placeholder name, or
    /// <see langword="null"/> if the cursor is not on a renameable symbol.
    /// </returns>
    public override Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("PrepareRename request at {Uri}:{Line}:{Col}", request.TextDocument.Uri, request.Position.Line, request.Position.Character);
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            _logger.LogDebug("PrepareRename: not renameable at {Uri}", request.TextDocument.Uri);
            return Task.FromResult<RangeOrPlaceholderRange?>(null);
        }

        var (result, word) = ctx.Value;
        var line = (int)request.Position.Line + 1;
        var col = (int)request.Position.Character + 1;
        var references = result.Symbols.FindReferences(word, line, col);

        if (references.Count == 0)
        {
            _logger.LogDebug("PrepareRename: not renameable at {Uri}", request.TextDocument.Uri);
            return Task.FromResult<RangeOrPlaceholderRange?>(null);
        }

        var referenceAtCursor = references.FirstOrDefault(r =>
            r.Span.StartLine == line &&
            r.Span.EndLine == line &&
            r.Span.StartColumn <= col &&
            col <= r.Span.EndColumn)
            ?? references[0];

        _logger.LogDebug("PrepareRename: symbol renameable at {Uri}", request.TextDocument.Uri);
        return Task.FromResult<RangeOrPlaceholderRange?>(
            new RangeOrPlaceholderRange(new PlaceholderRange
            {
                Range = referenceAtCursor.Span.ToLspRange(),
                Placeholder = word
            }));
    }

    /// <summary>
    /// Creates the registration options specifying that this handler applies to <c>stash</c>
    /// language files and that a prepare-rename provider is available.
    /// </summary>
    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            PrepareProvider = true
        };
}
