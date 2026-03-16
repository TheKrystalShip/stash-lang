namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class LinkedEditingRangeHandler : LinkedEditingRangeHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public LinkedEditingRangeHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    public override Task<LinkedEditingRanges> Handle(LinkedEditingRangeParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var lspLine = (int)request.Position.Line;
        var lspCharacter = (int)request.Position.Character;

        var ctx = _analysis.GetContextAt(uri, text, lspLine, lspCharacter);
        if (ctx == null)
            return Task.FromResult<LinkedEditingRanges>(null!);

        var (result, word) = ctx.Value;
        var line = lspLine + 1;
        var col = lspCharacter + 1;

        var references = result.Symbols.FindReferences(word, line, col);
        if (references.Count < 2)
            return Task.FromResult<LinkedEditingRanges>(null!);

        var ranges = new List<Range>();
        foreach (var reference in references)
            ranges.Add(reference.Span.ToLspRange());

        return Task.FromResult<LinkedEditingRanges>(new LinkedEditingRanges
        {
            Ranges = new Container<Range>(ranges),
            WordPattern = @"\w+"
        });
    }

    protected override LinkedEditingRangeRegistrationOptions CreateRegistrationOptions(
        LinkedEditingRangeClientCapabilities capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };
}
