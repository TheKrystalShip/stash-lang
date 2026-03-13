namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class RenameHandler : RenameHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public RenameHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    public override Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            return Task.FromResult<WorkspaceEdit?>(null);
        }
        var (result, word) = ctx.Value;

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;
        var references = result.Symbols.FindReferences(word, line, col);

        if (references.Count == 0)
        {
            return Task.FromResult<WorkspaceEdit?>(null);
        }

        var edits = new List<TextEdit>();
        foreach (var reference in references)
        {
            edits.Add(new TextEdit
            {
                Range = reference.Span.ToLspRange(),
                NewText = request.NewName
            });
        }

        var workspaceEdit = new WorkspaceEdit
        {
            Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
            {
                [request.TextDocument.Uri] = edits
            }
        };

        return Task.FromResult<WorkspaceEdit?>(workspaceEdit);
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            PrepareProvider = true
        };
}
