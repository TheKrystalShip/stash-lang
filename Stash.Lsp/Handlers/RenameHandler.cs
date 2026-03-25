namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

/// <summary>
/// Handles LSP <c>textDocument/rename</c> requests to rename a symbol and all its
/// references within the current document.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="AnalysisEngine.GetContextAt"/> to identify the symbol under the cursor
/// and <see cref="ScopeTree.FindReferences"/> to collect every reference within the
/// document. Each reference span is rewritten to the new name via a
/// <see cref="WorkspaceEdit"/>.
/// </para>
/// <para>
/// Rename is validated upfront by <see cref="PrepareRenameHandler"/>; if prepare returns
/// <see langword="null"/>, the client will not invoke this handler.
/// </para>
/// </remarks>
public class RenameHandler : RenameHandlerBase
{
    /// <summary>The analysis engine used to obtain context and locate all symbol references.</summary>
    private readonly AnalysisEngine _analysis;

    /// <summary>The document manager used to retrieve the current text of open files.</summary>
    private readonly DocumentManager _documents;

    private readonly ILogger<RenameHandler> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="RenameHandler"/> with the services
    /// needed to produce rename workspace edits.
    /// </summary>
    /// <param name="analysis">Analysis engine providing <see cref="AnalysisResult"/> data and reference lookup.</param>
    /// <param name="documents">Document manager for reading open file contents.</param>
    public RenameHandler(AnalysisEngine analysis, DocumentManager documents, ILogger<RenameHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// Processes the rename request and returns a <see cref="WorkspaceEdit"/> that replaces
    /// every in-document reference to the symbol with the new name.
    /// </summary>
    /// <param name="request">The rename request containing the document URI, cursor position, and new name.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>
    /// A <see cref="WorkspaceEdit"/> with text edits for every reference, or
    /// <see langword="null"/> if no symbol can be resolved at the cursor.
    /// </returns>
    public override Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Rename request at {Uri}:{Line}:{Col} → {NewName}", request.TextDocument.Uri, request.Position.Line, request.Position.Character, request.NewName);
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            return Task.FromResult<WorkspaceEdit?>(null);
        }
        var (result, word) = ctx.Value;

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;
        var references = result.Symbols.FindReferences(word, line, col);

        if (references.Count == 0)
        {
            return Task.FromResult<WorkspaceEdit?>(null);
        }

        var edits = new List<TextEdit>();
        foreach (var reference in references)
        {
            edits.Add(new TextEdit
            {
                Range = reference.Span.ToLspRange(),
                NewText = request.NewName
            });
        }

        var workspaceEdit = new WorkspaceEdit
        {
            Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
            {
                [request.TextDocument.Uri] = edits
            }
        };

        _logger.LogDebug("Rename: {Count} edits for {Uri}", edits.Count, request.TextDocument.Uri);
        return Task.FromResult<WorkspaceEdit?>(workspaceEdit);
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
