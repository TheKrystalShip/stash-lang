namespace Stash.Lsp.Handlers;

using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class FormattingHandler : DocumentFormattingHandlerBase
{
    private readonly DocumentManager _documents;

    public FormattingHandler(DocumentManager documents)
    {
        _documents = documents;
    }

    protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentFormattingCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

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
