namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class CodeLensHandler : CodeLensHandlerBase
{
    private readonly AnalysisEngine _analysis;

    public CodeLensHandler(AnalysisEngine analysis)
    {
        _analysis = analysis;
    }

    protected override CodeLensRegistrationOptions CreateRegistrationOptions(
        CodeLensCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            ResolveProvider = false
        };

    public override Task<CodeLensContainer?> Handle(CodeLensParams request,
        CancellationToken cancellationToken)
    {
        var result = _analysis.GetCachedResult(request.TextDocument.Uri.ToUri());
        if (result == null)
        {
            return Task.FromResult<CodeLensContainer?>(null);
        }

        var lenses = new List<CodeLens>();

        foreach (var sym in result.Symbols.GetTopLevel())
        {
            if (sym.Kind is not (Analysis.SymbolKind.Function or Analysis.SymbolKind.Struct or Analysis.SymbolKind.Enum))
            {
                continue;
            }

            var references = result.Symbols.FindReferences(sym.Name, sym.Span.StartLine, sym.Span.StartColumn);
            var refCount = references.Count - 1;

            var title = refCount switch
            {
                0 => "no references",
                1 => "1 reference",
                _ => $"{refCount} references"
            };

            lenses.Add(new CodeLens
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                    new Position(sym.Span.StartLine - 1, sym.Span.StartColumn - 1),
                    new Position(sym.Span.EndLine - 1, sym.Span.EndColumn - 1)),
                Command = new Command
                {
                    Title = title,
                    Name = ""
                }
            });
        }

        return Task.FromResult<CodeLensContainer?>(new CodeLensContainer(lenses));
    }

    public override Task<CodeLens> Handle(CodeLens request, CancellationToken cancellationToken)
        => Task.FromResult(request);
}
