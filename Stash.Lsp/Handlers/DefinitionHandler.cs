namespace Stash.Lsp.Handlers;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class DefinitionHandler : DefinitionHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public DefinitionHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        var result = _analysis.GetCachedResult(request.TextDocument.Uri.ToUri());
        if (result == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var text = _documents.GetText(request.TextDocument.Uri.ToUri());
        if (text == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var word = TextUtilities.FindWordAtPosition(text, request.Position.Line, request.Position.Character);
        if (word == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;
        var symbol = result.Symbols.FindDefinition(word, line, col);

        if (symbol == null)
        {
            // Check if this might be a member of a namespace import (e.g., utils.log → look for "log" in utils module)
            var currentLine = text.Split('\n')[request.Position.Line];
            var dotPrefix = TextUtilities.FindDotPrefix(currentLine, (int)request.Position.Character);
            if (dotPrefix != null && result.NamespaceImports.TryGetValue(dotPrefix, out var moduleInfo))
            {
                var memberSymbol = moduleInfo.Symbols.GetTopLevel().FirstOrDefault(s => s.Name == word);
                if (memberSymbol != null)
                {
                    var memberLocation = new Location
                    {
                        Uri = DocumentUri.From(moduleInfo.Uri),
                        Range = new Range(
                            new Position(memberSymbol.Span.StartLine - 1, memberSymbol.Span.StartColumn - 1),
                            new Position(memberSymbol.Span.EndLine - 1, memberSymbol.Span.EndColumn - 1)
                        )
                    };
                    return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(memberLocation));
                }
            }

            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        // If symbol was imported from another file, look up its original definition in that module
        if (symbol.SourceUri != null)
        {
            var moduleInfo2 = _analysis.ImportResolver.GetModule(symbol.SourceUri.LocalPath);
            if (moduleInfo2 != null)
            {
                var originalSymbol = moduleInfo2.Symbols.GetTopLevel().FirstOrDefault(s => s.Name == word);
                if (originalSymbol != null)
                {
                    var importedLocation = new Location
                    {
                        Uri = DocumentUri.From(symbol.SourceUri),
                        Range = new Range(
                            new Position(originalSymbol.Span.StartLine - 1, originalSymbol.Span.StartColumn - 1),
                            new Position(originalSymbol.Span.EndLine - 1, originalSymbol.Span.EndColumn - 1)
                        )
                    };
                    return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(importedLocation));
                }
            }

            // Fallback: jump to start of the imported file
            var fallbackLocation = new Location
            {
                Uri = DocumentUri.From(symbol.SourceUri),
                Range = new Range(new Position(0, 0), new Position(0, 0))
            };
            return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(fallbackLocation));
        }

        var location = new Location
        {
            Uri = request.TextDocument.Uri,
            Range = new Range(
                new Position(symbol.Span.StartLine - 1, symbol.Span.StartColumn - 1),
                new Position(symbol.Span.EndLine - 1, symbol.Span.EndColumn - 1)
            )
        };

        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(location));
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

}

