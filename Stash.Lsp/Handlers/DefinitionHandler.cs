namespace Stash.Lsp.Handlers;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using StashSymbolKind = Stash.Analysis.SymbolKind;

/// <summary>
/// Handles LSP <c>textDocument/definition</c> requests to navigate to the declaration
/// of the symbol under the cursor.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="AnalysisEngine.GetContextAt"/> to identify the word at the cursor and
/// resolves it to a <see cref="SymbolInfo"/> via <see cref="ScopeTree.FindDefinition"/>.
/// </para>
/// <para>
/// For symbols imported from another file (<see cref="SymbolInfo.SourceUri"/> is set),
/// the handler navigates directly to the symbol's original declaration in that module
/// using <see cref="AnalysisEngine.ImportResolver"/>. If the exact symbol cannot be found
/// in the imported module, it falls back to the beginning of the imported file.
/// </para>
/// <para>
/// Dot-access context (detected via <see cref="TextUtilities.FindDotPrefix"/>) bypasses
/// local scope resolution entirely, delegating to <see cref="AnalysisResult.ResolveNamespaceMember"/>
/// to resolve the member from the imported module.
/// </para>
/// </remarks>
public class DefinitionHandler : DefinitionHandlerBase
{
    /// <summary>The analysis engine used to obtain context and resolve symbol definitions.</summary>
    private readonly AnalysisEngine _analysis;

    /// <summary>The document manager used to retrieve the current text of open files.</summary>
    private readonly DocumentManager _documents;

    private readonly ILogger<DefinitionHandler> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="DefinitionHandler"/> with the services
    /// needed to resolve go-to-definition locations.
    /// </summary>
    /// <param name="analysis">Analysis engine providing <see cref="AnalysisResult"/> data and import resolution.</param>
    /// <param name="documents">Document manager for reading open file contents.</param>
    public DefinitionHandler(AnalysisEngine analysis, DocumentManager documents, ILogger<DefinitionHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// Processes the go-to-definition request and returns the location of the symbol's declaration,
    /// navigating into imported modules when necessary.
    /// </summary>
    /// <param name="request">The definition request containing the document URI and cursor position.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>
    /// A <see cref="LocationOrLocationLinks"/> pointing to the symbol's definition, or
    /// <see langword="null"/> if no symbol can be resolved at the cursor.
    /// </returns>
    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("GoToDefinition request at {Uri}:{Line}:{Col}", request.TextDocument.Uri, request.Position.Line, request.Position.Character);
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, request.Position.Line, request.Position.Character);
        if (ctx == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var (result, word) = ctx.Value;
        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;

        // Dict literal keys are not symbols — suppress go-to-definition
        if (result.IsDictKey(line, col))
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        // Dot-access context: this word follows a dot (e.g., "build_args" in "cli_utils.build_args").
        // Skip local scope resolution entirely — the member must be resolved from the namespace/module.
        var textLines = text!.Split('\n');
        var dotPrefix = TextUtilities.FindDotPrefix(textLines[request.Position.Line], request.Position.Character);
        bool afterDot = dotPrefix != null;

        var symbol = afterDot ? null : result.Symbols.FindDefinition(word, line, col);

        if (symbol == null)
        {
            var nsMember = result.ResolveNamespaceMember(text!, request.Position.Line, request.Position.Character, word);
            if (nsMember != null)
            {
                var (memberSymbol, moduleInfo) = nsMember.Value;
                var memberLocation = new Location
                {
                    Uri = DocumentUri.From(moduleInfo.Uri),
                    Range = memberSymbol.Span.ToLspRange()
                };
                return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(memberLocation));
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
                        Range = originalSymbol.Span.ToLspRange()
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
            Range = symbol.Span.ToLspRange()
        };

        _logger.LogDebug("GoToDefinition: resolved to {Uri}:{Line}", request.TextDocument.Uri, symbol.Span.StartLine);
        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(location));
    }

    /// <summary>
    /// Creates the registration options specifying that this handler applies to <c>stash</c> language files.
    /// </summary>
    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

}

