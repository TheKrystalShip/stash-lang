namespace Stash.Lsp.Handlers;

using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

/// <summary>
/// Handles LSP <c>textDocument/formatting</c> requests to format an entire Stash document.
/// </summary>
/// <remarks>
/// <para>
/// Delegates all formatting logic to <see cref="StashFormatter"/>, which is constructed from
/// the editor-provided tab size and indentation style from <see cref="FormattingOptions"/>.
/// If the formatted output is identical to the original text, an empty
/// <see cref="TextEditContainer"/> is returned to avoid unnecessary document versions.
/// Otherwise, a single <see cref="TextEdit"/> replacing the entire document is returned.
/// </para>
/// </remarks>
public class FormattingHandler : DocumentFormattingHandlerBase
{
    private readonly DocumentManager _documents;

    /// <summary>
    /// Initialises the handler with the document manager used to retrieve the current document text.
    /// </summary>
    /// <param name="documents">The document manager that holds the in-memory text for open documents.</param>
    public FormattingHandler(DocumentManager documents)
    {
        _documents = documents;
    }

    /// <summary>
    /// Creates the registration options restricting this handler to Stash language documents.
    /// </summary>
    /// <param name="capability">The client's document formatting capability descriptor.</param>
    /// <param name="clientCapabilities">The full set of client capabilities.</param>
    /// <returns>Registration options scoped to <c>stash</c> language documents.</returns>
    protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentFormattingCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

    /// <summary>
    /// Processes the formatting request and returns a single document-replacing text edit.
    /// </summary>
    /// <param name="request">
    /// The request containing the document URI and formatting options (tab size, insert spaces).
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// An empty <see cref="TextEditContainer"/> when no changes are needed, a container with one
    /// full-document replacement edit when the document differs after formatting, or
    /// <see langword="null"/> if the document text is not available.
    /// </returns>
    public override Task<TextEditContainer?> Handle(DocumentFormattingParams request,
        CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        if (text == null)
        {
            return Task.FromResult<TextEditContainer?>(null);
        }

        var tabSize = request.Options.TabSize;
        var useTabs = request.Options.InsertSpaces == false;
        var formatter = new StashFormatter((int)tabSize, useTabs);
        var formatted = formatter.Format(text);

        if (formatted == text)
        {
            return Task.FromResult<TextEditContainer?>(new TextEditContainer());
        }

        // Replace the entire document
        var lines = text.Split('\n');
        var lastLine = lines.Length - 1;
        var lastChar = lines[lastLine].Length;

        var edit = new TextEdit
        {
            Range = new Range(0, 0, lastLine, lastChar),
            NewText = formatted
        };

        return Task.FromResult<TextEditContainer?>(new TextEditContainer(edit));
    }
}
