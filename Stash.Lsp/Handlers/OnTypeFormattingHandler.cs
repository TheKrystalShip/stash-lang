namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

/// <summary>
/// Handles LSP <c>textDocument/onTypeFormatting</c> requests to fix indentation near the cursor
/// when the user types a trigger character (<c>}</c>, <c>;</c>, or <c>\n</c>).
/// </summary>
/// <remarks>
/// <para>
/// Delegates all formatting logic to <see cref="StashFormatter"/> to produce the fully-formatted
/// document, then diffs the original and formatted text line by line. Only lines within a small
/// window around the cursor position (cursor line ± 1) that actually changed are returned as
/// <see cref="TextEdit"/> objects, keeping the edit set minimal and avoiding jarring full-document
/// rewrites on every keystroke.
/// </para>
/// <para>
/// If the line count changes after formatting (rare), the handler falls back to a single
/// full-document replacement edit, matching the behaviour of <see cref="FormattingHandler"/>.
/// </para>
/// </remarks>
public class OnTypeFormattingHandler : DocumentOnTypeFormattingHandlerBase
{
    private readonly DocumentManager _documents;

    private readonly ILogger<OnTypeFormattingHandler> _logger;

    /// <summary>
    /// Initialises the handler with the document manager used to retrieve the current document text.
    /// </summary>
    /// <param name="documents">The document manager that holds the in-memory text for open documents.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public OnTypeFormattingHandler(DocumentManager documents, ILogger<OnTypeFormattingHandler> logger)
    {
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// Creates the registration options for this handler, scoped to Stash documents and the
    /// trigger characters <c>}</c>, <c>;</c>, and <c>\n</c>.
    /// </summary>
    /// <param name="capability">The client's on-type formatting capability descriptor.</param>
    /// <param name="clientCapabilities">The full set of client capabilities.</param>
    /// <returns>Registration options scoped to <c>stash</c> language documents.</returns>
    protected override DocumentOnTypeFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentOnTypeFormattingCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            FirstTriggerCharacter = "}",
            MoreTriggerCharacter = new Container<string>(";", "\n")
        };

    /// <summary>
    /// Processes the on-type formatting request and returns text edits for lines near the cursor
    /// that differ between the original and the fully-formatted document.
    /// </summary>
    /// <param name="request">
    /// The request containing the document URI, cursor position, trigger character, and formatting options.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// An empty <see cref="TextEditContainer"/> when no changes are needed; a container with edits
    /// for the changed lines in the cursor window when the formatted output differs; or a single
    /// full-document replacement edit when the line count changes after formatting.
    /// </returns>
    public override Task<TextEditContainer?> Handle(DocumentOnTypeFormattingParams request,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("OnTypeFormatting request for {Uri} at line {Line}, char '{Char}'",
            request.TextDocument.Uri, request.Position.Line, request.Character);

        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        if (text == null)
        {
            return Task.FromResult<TextEditContainer?>(new TextEditContainer());
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
            return Task.FromResult<TextEditContainer?>(new TextEditContainer());
        }

        if (formatted == text)
        {
            _logger.LogDebug("OnTypeFormatting: no changes for {Uri}", request.TextDocument.Uri);
            return Task.FromResult<TextEditContainer?>(new TextEditContainer());
        }

        var originalLines = text.Split('\n');
        var formattedLines = formatted.Split('\n');

        // If the line count changed, fall back to a full document replacement
        if (originalLines.Length != formattedLines.Length)
        {
            _logger.LogDebug("OnTypeFormatting: line count changed, falling back to full edit for {Uri}",
                request.TextDocument.Uri);

            var lastLine = originalLines.Length - 1;
            var lastChar = originalLines[lastLine].Length;
            var fullEdit = new TextEdit
            {
                Range = new Range(0, 0, lastLine, lastChar),
                NewText = formatted
            };
            return Task.FromResult<TextEditContainer?>(new TextEditContainer(fullEdit));
        }

        // Diff within a one-line window around the cursor
        int cursorLine = request.Position.Line;
        int windowStart = System.Math.Max(0, cursorLine - 1);
        int windowEnd = System.Math.Min(originalLines.Length - 1, cursorLine + 1);

        var edits = new List<TextEdit>();
        for (int i = windowStart; i <= windowEnd; i++)
        {
            if (originalLines[i] != formattedLines[i])
            {
                edits.Add(new TextEdit
                {
                    Range = new Range(i, 0, i, originalLines[i].Length),
                    NewText = formattedLines[i]
                });
            }
        }

        _logger.LogDebug("OnTypeFormatting: returning {Count} edit(s) for {Uri}", edits.Count,
            request.TextDocument.Uri);
        return Task.FromResult<TextEditContainer?>(new TextEditContainer(edits));
    }
}
