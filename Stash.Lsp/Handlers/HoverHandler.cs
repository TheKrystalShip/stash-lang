namespace Stash.Lsp.Handlers;

using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class HoverHandler : HoverHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public HoverHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        var result = _analysis.GetCachedResult(request.TextDocument.Uri.ToUri());
        if (result == null)
        {
            return Task.FromResult<Hover?>(null);
        }

        var line = request.Position.Line + 1; // Convert to 1-based
        var col = request.Position.Character + 1;

        // Find the token at this position
        var text = _documents.GetText(request.TextDocument.Uri.ToUri());
        if (text == null)
        {
            return Task.FromResult<Hover?>(null);
        }

        // Find the identifier at the cursor position
        string? word = TextUtilities.FindWordAtPosition(text, request.Position.Line, request.Position.Character);
        if (word == null)
        {
            return Task.FromResult<Hover?>(null);
        }

        // Look up in symbol table
        var symbol = result.Symbols.FindDefinition(word, line, col);
        if (symbol == null)
        {
            return Task.FromResult<Hover?>(null);
        }

        var markdown = $"```stash\n{symbol.Detail ?? symbol.Name}\n```\n*{symbol.Kind}*";

        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = markdown
            })
        });
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

}

