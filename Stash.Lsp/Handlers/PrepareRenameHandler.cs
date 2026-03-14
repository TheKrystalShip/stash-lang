namespace Stash.Lsp.Handlers;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class PrepareRenameHandler : PrepareRenameHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public PrepareRenameHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    public override Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            return Task.FromResult<RangeOrPlaceholderRange?>(null);
        }

        var (result, word) = ctx.Value;
        var line = (int)request.Position.Line + 1;
        var col = (int)request.Position.Character + 1;
        var references = result.Symbols.FindReferences(word, line, col);

        if (references.Count == 0)
        {
            return Task.FromResult<RangeOrPlaceholderRange?>(null);
        }

        var referenceAtCursor = references.FirstOrDefault(r =>
            r.Span.StartLine == line &&
            r.Span.EndLine == line &&
            r.Span.StartColumn <= col &&
            col <= r.Span.EndColumn)
            ?? references[0];

        return Task.FromResult<RangeOrPlaceholderRange?>(
            new RangeOrPlaceholderRange(new PlaceholderRange
            {
                Range = referenceAtCursor.Span.ToLspRange(),
                Placeholder = word
            }));
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            PrepareProvider = true
        };
}
