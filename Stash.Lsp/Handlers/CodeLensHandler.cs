namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
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

            // Skip built-in symbols (line 0) — they have no source location
            if (sym.Span.StartLine == 0)
            {
                continue;
            }

            var references = result.Symbols.FindReferences(sym.Name, sym.Span.StartLine, sym.Span.StartColumn);

            var lspRange = sym.Span.ToLspRange();
            var refLocations = new JArray();
            foreach (var r in references)
            {
                if (r.Span == sym.Span)
                {
                    continue;
                }

                var refRange = r.Span.ToLspRange();
                refLocations.Add(JObject.FromObject(new
                {
                    uri = request.TextDocument.Uri.ToString(),
                    range = new
                    {
                        start = new { line = refRange.Start.Line, character = refRange.Start.Character },
                        end = new { line = refRange.End.Line, character = refRange.End.Character }
                    }
                }));
            }

            // Add cross-file references from files that import this module
            var crossFileRefs = _analysis.FindCrossFileReferences(request.TextDocument.Uri.ToUri(), sym.Name);
            foreach (var (refUri, refSpan) in crossFileRefs)
            {
                var refRange = refSpan.ToLspRange();
                refLocations.Add(JObject.FromObject(new
                {
                    uri = refUri.ToString(),
                    range = new
                    {
                        start = new { line = refRange.Start.Line, character = refRange.Start.Character },
                        end = new { line = refRange.End.Line, character = refRange.End.Character }
                    }
                }));
            }

            var refCount = references.Count - 1 + crossFileRefs.Count;

            var title = refCount switch
            {
                0 => "no references",
                1 => "1 reference",
                _ => $"{refCount} references"
            };

            lenses.Add(new CodeLens
            {
                Range = lspRange,
                Command = new Command
                {
                    Title = title,
                    Name = "stash.showReferences",
                    Arguments = new JArray
                    {
                        request.TextDocument.Uri.ToString(),
                        JObject.FromObject(new { line = lspRange.Start.Line, character = lspRange.Start.Character }),
                        refLocations
                    }
                }
            });
        }

        return Task.FromResult<CodeLensContainer?>(new CodeLensContainer(lenses));
    }

    public override Task<CodeLens> Handle(CodeLens request, CancellationToken cancellationToken)
        => Task.FromResult(request);
}
