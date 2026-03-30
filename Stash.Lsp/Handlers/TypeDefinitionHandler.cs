namespace Stash.Lsp.Handlers;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Microsoft.Extensions.Logging;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using StashSymbolKind = Stash.Analysis.SymbolKind;

/// <summary>
/// Handles LSP <c>textDocument/typeDefinition</c> requests to navigate to the type
/// declaration of the symbol under the cursor.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="AnalysisEngine.GetContextAt"/> and <see cref="ScopeTree.FindDefinition"/>
/// to resolve the symbol under the cursor. If the symbol is itself a <c>struct</c> or
/// <c>enum</c>, the handler navigates to its own declaration. For variables, constants,
/// parameters, and loop variables, the handler follows the <see cref="SymbolInfo.TypeHint"/>
/// to find the corresponding type declaration in the <see cref="ScopeTree"/>.
/// </para>
/// <para>
/// When the resolved type was imported from another file, the handler looks up the original
/// declaration in that module via <see cref="AnalysisEngine.ImportResolver"/>. Built-in types
/// registered at line 0 are treated as non-navigable and return <see langword="null"/>.
/// </para>
/// </remarks>
public class TypeDefinitionHandler : TypeDefinitionHandlerBase
{
    /// <summary>The analysis engine used to obtain context and resolve type declarations.</summary>
    private readonly AnalysisEngine _analysis;

    /// <summary>The document manager used to retrieve the current text of open files.</summary>
    private readonly DocumentManager _documents;

    private readonly ILogger<TypeDefinitionHandler> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="TypeDefinitionHandler"/> with the services
    /// needed to resolve go-to-type-definition locations.
    /// </summary>
    /// <param name="analysis">Analysis engine providing <see cref="AnalysisResult"/> data and import resolution.</param>
    /// <param name="documents">Document manager for reading open file contents.</param>
    public TypeDefinitionHandler(AnalysisEngine analysis, DocumentManager documents, ILogger<TypeDefinitionHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// Processes the go-to-type-definition request and returns the location of the symbol's
    /// type declaration, following <see cref="SymbolInfo.TypeHint"/> when necessary.
    /// </summary>
    /// <param name="request">The type-definition request containing the document URI and cursor position.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>
    /// A <see cref="LocationOrLocationLinks"/> pointing to the type's declaration, or
    /// <see langword="null"/> if the type cannot be resolved.
    /// </returns>
    public override Task<LocationOrLocationLinks?> Handle(TypeDefinitionParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("TypeDefinition request at {Uri}:{Line}:{Col}", request.TextDocument.Uri, request.Position.Line, request.Position.Character);
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            _logger.LogTrace("TypeDefinition: no type found at {Uri}", request.TextDocument.Uri);
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var (result, word) = ctx.Value;
        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;

        // Dot-access context: skip local scope resolution — resolve from the namespace/module instead.
        var textLines = text!.Split('\n');
        var dotPrefix = TextUtilities.FindDotPrefix(textLines[(int)request.Position.Line], (int)request.Position.Character);
        bool afterDot = dotPrefix != null;

        var symbol = afterDot ? null : result.Symbols.FindDefinition(word, line, col);

        // If cursor is on a namespace member (e.g., alias.member), resolve it
        if (symbol == null)
        {
            var nsMember = result.ResolveNamespaceMember(text!, (int)request.Position.Line, (int)request.Position.Character, word);
            if (nsMember != null)
            {
                symbol = nsMember.Value.Symbol;
            }
        }

        if (symbol == null)
        {
            _logger.LogTrace("TypeDefinition: no type found at {Uri}", request.TextDocument.Uri);
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        // If the symbol IS a struct or enum, return its own location
        if (symbol.Kind is StashSymbolKind.Struct or StashSymbolKind.Enum)
        {
            _logger.LogDebug("TypeDefinition: resolved for {Uri}", request.TextDocument.Uri);
            return MakeLocationAsync(request.TextDocument.Uri, symbol, result);
        }

        // For variables, constants, parameters, loop variables — look up the TypeHint
        var typeName = symbol.TypeHint;
        if (typeName == null)
        {
            _logger.LogTrace("TypeDefinition: no type found at {Uri}", request.TextDocument.Uri);
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        // Search for the type declaration in the scope tree
        var typeSymbol = result.Symbols.All
            .FirstOrDefault(s => s.Name == typeName && s.Kind is StashSymbolKind.Struct or StashSymbolKind.Enum);

        if (typeSymbol != null)
        {
            _logger.LogDebug("TypeDefinition: resolved for {Uri}", request.TextDocument.Uri);
            return MakeLocationAsync(request.TextDocument.Uri, typeSymbol, result);
        }

        _logger.LogTrace("TypeDefinition: no type found at {Uri}", request.TextDocument.Uri);
        return Task.FromResult<LocationOrLocationLinks?>(null);
    }

    /// <summary>
    /// Constructs a <see cref="LocationOrLocationLinks"/> response for the given <paramref name="symbol"/>,
    /// navigating to the original declaration in an imported module when
    /// <see cref="SymbolInfo.SourceUri"/> is set. Returns <see langword="null"/> for built-in
    /// types registered at line 0.
    /// </summary>
    /// <param name="requestUri">The URI of the document containing the cursor.</param>
    /// <param name="symbol">The resolved <see cref="SymbolInfo"/> whose location to return.</param>
    /// <param name="result">The current <see cref="AnalysisResult"/> for the open document.</param>
    /// <returns>
    /// A task containing a <see cref="LocationOrLocationLinks"/> for the symbol, or
    /// <see langword="null"/> if the symbol has no navigable source.
    /// </returns>
    private Task<LocationOrLocationLinks?> MakeLocationAsync(DocumentUri requestUri, SymbolInfo symbol, AnalysisResult result)
    {
        // If the symbol was imported from another file, navigate there
        if (symbol.SourceUri != null)
        {
            var moduleInfo = _analysis.ImportResolver.GetModule(symbol.SourceUri.LocalPath);
            if (moduleInfo != null)
            {
                var originalSymbol = moduleInfo.Symbols.GetTopLevel()
                    .FirstOrDefault(s => s.Name == symbol.Name && s.Kind is StashSymbolKind.Struct or StashSymbolKind.Enum);
                if (originalSymbol != null)
                {
                    var location = new Location
                    {
                        Uri = DocumentUri.From(symbol.SourceUri),
                        Range = originalSymbol.Span.ToLspRange()
                    };
                    return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(location));
                }
            }
        }

        // Built-in types (line 0) have no navigable source
        if (symbol.Span.StartLine == 0)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var loc = new Location
        {
            Uri = requestUri,
            Range = symbol.Span.ToLspRange()
        };
        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(loc));
    }

    /// <summary>
    /// Creates the registration options specifying that this handler applies to <c>stash</c> language files.
    /// </summary>
    protected override TypeDefinitionRegistrationOptions CreateRegistrationOptions(
        TypeDefinitionCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };
}
