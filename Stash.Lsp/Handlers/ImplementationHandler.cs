namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

/// <summary>
/// Handles LSP <c>textDocument/implementation</c> requests to find all usages of a type
/// across the workspace.
/// </summary>
/// <remarks>
/// <para>
/// For a cursor on a <c>struct</c> or <c>enum</c> symbol, or on a variable/parameter whose
/// <see cref="SymbolInfo.TypeHint"/> refers to one, this handler collects all
/// <see cref="ReferenceKind.TypeUse"/> references within the current document via
/// <see cref="ScopeTree.References"/>, and then appends cross-file references discovered
/// by <see cref="AnalysisEngine.FindCrossFileReferences"/>.
/// </para>
/// <para>
/// This provides an answer to the question "where is this type used?" rather than the
/// traditional OOP notion of implementations, which does not apply to Stash.
/// </para>
/// </remarks>
public class ImplementationHandler : ImplementationHandlerBase
{
    /// <summary>The analysis engine used to obtain context and find cross-file type references.</summary>
    private readonly AnalysisEngine _analysis;

    /// <summary>The document manager used to retrieve the current text of open files.</summary>
    private readonly DocumentManager _documents;

    /// <summary>
    /// Initialises a new instance of <see cref="ImplementationHandler"/> with the services
    /// needed to find type-use locations.
    /// </summary>
    /// <param name="analysis">Analysis engine providing <see cref="AnalysisResult"/> data and cross-file reference search.</param>
    /// <param name="documents">Document manager for reading open file contents.</param>
    public ImplementationHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    /// <summary>
    /// Processes the go-to-implementation request and returns all locations where the
    /// resolved type is used, both within the current document and across open workspace files.
    /// </summary>
    /// <param name="request">The implementation request containing the document URI and cursor position.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>
    /// A <see cref="LocationOrLocationLinks"/> containing all type-use locations, or
    /// <see langword="null"/> if no type can be resolved or no usages are found.
    /// </returns>
    public override Task<LocationOrLocationLinks?> Handle(ImplementationParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var (result, word) = ctx.Value;
        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;

        var symbol = result.Symbols.FindDefinition(word, line, col);
        if (symbol == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        // Determine the target type name
        string? typeName = null;

        if (symbol.Kind is Analysis.SymbolKind.Struct or Analysis.SymbolKind.Enum)
        {
            typeName = symbol.Name;
        }
        else if (symbol.TypeHint != null)
        {
            // Variable/param/const with a type hint — find usages of that type
            var typeSymbol = result.Symbols.All
                .FirstOrDefault(s => s.Name == symbol.TypeHint && s.Kind is Analysis.SymbolKind.Struct or Analysis.SymbolKind.Enum);
            if (typeSymbol != null)
            {
                typeName = typeSymbol.Name;
            }
        }

        if (typeName == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var locations = new List<LocationOrLocationLink>();

        // Find all TypeUse references in the current document
        foreach (var reference in result.Symbols.References)
        {
            if (reference.Name == typeName && reference.Kind == ReferenceKind.TypeUse)
            {
                locations.Add(new Location
                {
                    Uri = request.TextDocument.Uri,
                    Range = reference.Span.ToLspRange()
                });
            }
        }

        // Find cross-file references
        var crossFileRefs = _analysis.FindCrossFileReferences(uri, typeName);
        foreach (var (refUri, refSpan) in crossFileRefs)
        {
            locations.Add(new Location
            {
                Uri = DocumentUri.From(refUri),
                Range = refSpan.ToLspRange()
            });
        }

        if (locations.Count == 0)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(locations));
    }

    /// <summary>
    /// Creates the registration options specifying that this handler applies to <c>stash</c> language files.
    /// </summary>
    protected override ImplementationRegistrationOptions CreateRegistrationOptions(
        ImplementationCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };
}
