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
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            return Task.FromResult<Hover?>(null);
        }

        var (result, word) = ctx.Value;
        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;

        var symbol = result.Symbols.FindDefinition(word, line, col);

        // If not found directly, try namespace member access
        if (symbol == null)
        {
            var nsMember = result.ResolveNamespaceMember(text!, (int)request.Position.Line, (int)request.Position.Character, word);
            if (nsMember != null)
            {
                var (nsSym, _) = nsMember.Value;
                var lines2 = text!.Split('\n');
                var dotPrefix = TextUtilities.FindDotPrefix(lines2[(int)request.Position.Line], (int)request.Position.Character);
                var markdown = $"```stash\n{nsSym.Detail ?? nsSym.Name}\n```\n*{nsSym.Kind}* — from `{dotPrefix}`";
                return Task.FromResult<Hover?>(new Hover
                {
                    Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = markdown
                    })
                });
            }
            // Try built-in namespace constants (e.g., process.SIGTERM)
            {
                var lines2 = text!.Split('\n');
                var dotPrefix = TextUtilities.FindDotPrefix(lines2[(int)request.Position.Line], (int)request.Position.Character);
                if (dotPrefix != null)
                {
                    var qualifiedName = $"{dotPrefix}.{word}";
                    if (BuiltInRegistry.TryGetNamespaceConstant(qualifiedName, out var constant))
                    {
                        var markdown = $"```stash\n{constant.Detail}\n```\n*constant* — from `{dotPrefix}`";
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
        var detail = symbol.Detail ?? symbol.Name;

        // Append inferred type if the symbol has a TypeHint not already shown in detail
        if (symbol.TypeHint != null &&
            symbol.Kind is Analysis.SymbolKind.Variable or Analysis.SymbolKind.Constant
                or Analysis.SymbolKind.Parameter or Analysis.SymbolKind.LoopVariable &&
            !detail.Contains($": {symbol.TypeHint}"))
        {
            detail += $": {symbol.TypeHint}";
        }

        var md = $"```stash\n{detail}\n```\n*{symbol.Kind}*";
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

