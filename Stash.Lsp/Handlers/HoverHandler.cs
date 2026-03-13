namespace Stash.Lsp.Handlers;

using System.Linq;
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
        if (result == null) return Task.FromResult<Hover?>(null);

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;

        var text = _documents.GetText(request.TextDocument.Uri.ToUri());
        if (text == null) return Task.FromResult<Hover?>(null);

        string? word = TextUtilities.FindWordAtPosition(text, request.Position.Line, request.Position.Character);
        if (word == null) return Task.FromResult<Hover?>(null);

        var symbol = result.Symbols.FindDefinition(word, line, col);

        // If not found directly, try namespace member access
        if (symbol == null)
        {
            var lines = text.Split('\n');
            if (request.Position.Line < lines.Length)
            {
                var currentLine = lines[request.Position.Line];
                var dotPrefix = TextUtilities.FindDotPrefix(currentLine, (int)request.Position.Character);
                if (dotPrefix != null && result.NamespaceImports.TryGetValue(dotPrefix, out var moduleInfo))
                {
                    symbol = moduleInfo.Symbols.GetTopLevel().FirstOrDefault(s => s.Name == word);
                    if (symbol != null)
                    {
                        var markdown = $"```stash\n{symbol.Detail ?? symbol.Name}\n```\n*{symbol.Kind}* — from `{dotPrefix}`";
                        return Task.FromResult<Hover?>(new Hover
                        {
                            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                            {
                                Kind = MarkupKind.Markdown,
                                Value = markdown
                            })
                        });
                    }
                }
            }
            return Task.FromResult<Hover?>(null);
        }

        // Normal symbol hover
        var md = $"```stash\n{symbol.Detail ?? symbol.Name}\n```\n*{symbol.Kind}*";
        if (symbol.SourceUri != null)
        {
            var importedPath = System.IO.Path.GetFileName(symbol.SourceUri.LocalPath);
            md += $"\n\n*imported from {importedPath}*";
        }

        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = md
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

