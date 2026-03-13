namespace Stash.Lsp.Handlers;

using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class DocumentHighlightHandler : DocumentHighlightHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public DocumentHighlightHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    public override Task<DocumentHighlightContainer?> Handle(DocumentHighlightParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            return Task.FromResult<DocumentHighlightContainer?>(null);
        }
        var (result, word) = ctx.Value;

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;
        var references = result.Symbols.FindReferences(word, line, col);

        if (references.Count == 0)
        {
            return Task.FromResult<DocumentHighlightContainer?>(null);
        }

        var highlights = new System.Collections.Generic.List<DocumentHighlight>();
        foreach (var reference in references)
        {
            highlights.Add(new DocumentHighlight
            {
                Range = reference.Span.ToLspRange(),
                Kind = reference.Kind == ReferenceKind.Write
                    ? DocumentHighlightKind.Write
                    : DocumentHighlightKind.Read
            });
        }

        return Task.FromResult<DocumentHighlightContainer?>(new DocumentHighlightContainer(highlights));
    }

    protected override DocumentHighlightRegistrationOptions CreateRegistrationOptions(
        DocumentHighlightCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };
}
