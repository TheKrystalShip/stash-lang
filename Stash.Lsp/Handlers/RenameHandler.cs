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
        var result = _analysis.GetCachedResult(request.TextDocument.Uri.ToUri());
        if (result == null)
            return Task.FromResult<WorkspaceEdit?>(null);

        var text = _documents.GetText(request.TextDocument.Uri.ToUri());
        if (text == null)
            return Task.FromResult<WorkspaceEdit?>(null);

        var word = TextUtilities.FindWordAtPosition(text, request.Position.Line, request.Position.Character);
        if (word == null)
            return Task.FromResult<WorkspaceEdit?>(null);

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;
        var references = result.Symbols.FindReferences(word, line, col);

        if (references.Count == 0)
            return Task.FromResult<WorkspaceEdit?>(null);

        var edits = new List<TextEdit>();
        foreach (var reference in references)
        {
            edits.Add(new TextEdit
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                    new Position(reference.Span.StartLine - 1, reference.Span.StartColumn - 1),
                    new Position(reference.Span.EndLine - 1, reference.Span.EndColumn - 1)),
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
