namespace Stash.Lsp.Handlers;

using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

/// <summary>
/// Handles LSP <c>textDocument/rangeFormatting</c> requests to format a range within a Stash document.
/// </summary>
/// <remarks>
/// <para>
/// Delegates all formatting logic to <see cref="StashFormatter"/>, which is constructed from
/// the editor-provided tab size and indentation style from <see cref="FormattingOptions"/>.
/// Because <see cref="StashFormatter"/> operates on full documents, the entire document is
/// formatted and a single <see cref="TextEdit"/> replacing the whole document is returned.
/// If the formatted output is identical to the original text, an empty
/// <see cref="TextEditContainer"/> is returned to avoid unnecessary document versions.
/// </para>
/// </remarks>
public class RangeFormattingHandler : DocumentRangeFormattingHandlerBase
{
    private readonly DocumentManager _documents;

    private readonly ILogger<RangeFormattingHandler> _logger;

    /// <summary>
    /// Initialises the handler with the document manager used to retrieve the current document text.
    /// </summary>
    /// <param name="documents">The document manager that holds the in-memory text for open documents.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public RangeFormattingHandler(DocumentManager documents, ILogger<RangeFormattingHandler> logger)
    {
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// Creates the registration options restricting this handler to Stash language documents.
    /// </summary>
    /// <param name="capability">The client's document range formatting capability descriptor.</param>
    /// <param name="clientCapabilities">The full set of client capabilities.</param>
    /// <returns>Registration options scoped to <c>stash</c> language documents.</returns>
    protected override DocumentRangeFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentRangeFormattingCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

    /// <summary>
    /// Processes the range formatting request and returns a single document-replacing text edit.
    /// </summary>
    /// <param name="request">
    /// The request containing the document URI, the requested range, and formatting options
    /// (tab size, insert spaces).
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// An empty <see cref="TextEditContainer"/> when no changes are needed or the document text
    /// is not available, or a container with one full-document replacement edit when the document
    /// differs after formatting.
    /// </returns>
    public override Task<TextEditContainer> Handle(DocumentRangeFormattingParams request,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Range formatting request for {Uri}", request.TextDocument.Uri);
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        if (text == null)
        {
            return Task.FromResult(new TextEditContainer());
        }

        var tabSize = request.Options.TabSize;
        var useTabs = request.Options.InsertSpaces == false;
        var formatter = new StashFormatter((int)tabSize, useTabs);
        string formatted;
        try
        {
            formatted = formatter.Format(text);
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "Formatting failed for {Uri}", uri);
            return Task.FromResult(new TextEditContainer());
        }

        if (formatted == text)
        {
            _logger.LogDebug("Range formatting: no changes for {Uri}", request.TextDocument.Uri);
            return Task.FromResult(new TextEditContainer());
        }

        // Replace the entire document — StashFormatter operates at document level,
        // so returning a full-document edit is the simplest correct approach.
        var lines = text.Split('\n');
        var lastLine = lines.Length - 1;
        var lastChar = lines[lastLine].Length;

        var edit = new TextEdit
        {
            Range = new Range(0, 0, lastLine, lastChar),
            NewText = formatted
        };

        _logger.LogDebug("Range formatting: applied for {Uri}", request.TextDocument.Uri);
        return Task.FromResult(new TextEditContainer(edit));
    }
}
