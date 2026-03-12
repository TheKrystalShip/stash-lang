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
        string? word = FindWordAtPosition(text, request.Position.Line, request.Position.Character);
        if (word == null)
        {
            return Task.FromResult<Hover?>(null);
        }

        // Look up in symbol table
        var span = new Stash.Common.SourceSpan("", line, col, line, col);
        var symbol = result.Symbols.FindDefinition(word, span);
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

    private static string? FindWordAtPosition(string text, int line, int character)
    {
        var lines = text.Split('\n');
        if (line < 0 || line >= lines.Length)
        {
            return null;
        }

        var lineText = lines[line];
        if (character < 0 || character >= lineText.Length)
        {
            return null;
        }

        var c = lineText[character];
        if (!char.IsLetterOrDigit(c) && c != '_')
        {
            return null;
        }

        int start = character;
        while (start > 0 && (char.IsLetterOrDigit(lineText[start - 1]) || lineText[start - 1] == '_'))
        {
            start--;
        }

        int end = character;
        while (end < lineText.Length - 1 && (char.IsLetterOrDigit(lineText[end + 1]) || lineText[end + 1] == '_'))
        {
            end++;
        }

        return lineText[start..(end + 1)];
    }
}
