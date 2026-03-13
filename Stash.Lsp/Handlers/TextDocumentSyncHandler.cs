namespace Stash.Lsp.Handlers;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using Stash.Common;
using Stash.Lsp.Analysis;

public class TextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly DocumentManager _documents;
    private readonly AnalysisEngine _analysis;
    private readonly ILanguageServerFacade _server;

    public TextDocumentSyncHandler(DocumentManager documents, AnalysisEngine analysis, ILanguageServerFacade server)
    {
        _documents = documents;
        _analysis = analysis;
        _server = server;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) =>
        new(uri, "stash");

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var text = request.TextDocument.Text;
        var version = request.TextDocument.Version ?? 0;

        _documents.Open(uri, text, version);
        AnalyzeAndPublishDiagnostics(uri, text);

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var text = request.ContentChanges.LastOrDefault()?.Text;
        if (text == null)
        {
            return Unit.Task;
        }

        var version = request.TextDocument.Version ?? 0;
        _documents.Update(uri, text, version);
        AnalyzeAndPublishDiagnostics(uri, text);

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        _documents.Close(uri);

        // Clear diagnostics for closed document
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = request.TextDocument.Uri,
            Diagnostics = new Container<Diagnostic>()
        });

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            Change = TextDocumentSyncKind.Full,
            Save = new BooleanOr<SaveOptions>(new SaveOptions { IncludeText = false })
        };

    private void AnalyzeAndPublishDiagnostics(Uri uri, string text)
    {
        var result = _analysis.Analyze(uri, text);
        var diagnostics = new System.Collections.Generic.List<Diagnostic>();

        foreach (var error in result.StructuredLexErrors)
        {
            diagnostics.Add(new Diagnostic
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                    new Position(error.Span.StartLine - 1, error.Span.StartColumn - 1),
                    new Position(error.Span.EndLine - 1, error.Span.EndColumn - 1)),
                Severity = DiagnosticSeverity.Error,
                Source = "stash",
                Message = error.Message
            });
        }

        foreach (var error in result.StructuredParseErrors)
        {
            diagnostics.Add(new Diagnostic
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                    new Position(error.Span.StartLine - 1, error.Span.StartColumn - 1),
                    new Position(error.Span.EndLine - 1, error.Span.EndColumn - 1)),
                Severity = DiagnosticSeverity.Error,
                Source = "stash",
                Message = error.Message
            });
        }

        foreach (var semantic in result.SemanticDiagnostics)
        {
            diagnostics.Add(new Diagnostic
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                    new Position(semantic.Span.StartLine - 1, semantic.Span.StartColumn - 1),
                    new Position(semantic.Span.EndLine - 1, semantic.Span.EndColumn - 1)),
                Severity = semantic.Level switch
                {
                    Analysis.DiagnosticLevel.Error => DiagnosticSeverity.Error,
                    Analysis.DiagnosticLevel.Warning => DiagnosticSeverity.Warning,
                    Analysis.DiagnosticLevel.Information => DiagnosticSeverity.Information,
                    _ => DiagnosticSeverity.Warning
                },
                Source = "stash",
                Message = semantic.Message
            });
        }

        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.From(uri),
            Diagnostics = new Container<Diagnostic>(diagnostics)
        });
    }
}
