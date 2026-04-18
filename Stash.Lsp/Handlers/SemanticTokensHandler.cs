[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Stash.Tests")]
namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lexing;
using Stash.Analysis;
using static Stash.Analysis.SemanticTokenConstants;

/// <summary>
/// Handles LSP <c>textDocument/semanticTokens</c> requests to provide semantic
/// syntax highlighting beyond TextMate grammar capabilities.
/// </summary>
/// <remarks>
/// <para>
/// Classifies tokens into 11 symbol-identity types (namespace, type, struct, enum, interface,
/// function, method, parameter, variable, property, enumMember) and applies modifiers
/// (declaration, readonly, defaultLibrary, async). Uses the <see cref="AnalysisEngine"/> cached
/// result's token list and the <see cref="SemanticTokenWalker"/> for accurate per-token
/// classification. Lexical constructs (keywords, numbers, strings, comments, operators) are
/// handled by the TextMate/tree-sitter grammar and are not emitted as semantic tokens.
/// </para>
/// </remarks>
public class SemanticTokensHandler : SemanticTokensHandlerBase
{
    private readonly AnalysisEngine _analysis;

    /// <summary>Per-document cached <see cref="SemanticTokensDocument"/> instances keyed by URI.</summary>
    private readonly ConcurrentDictionary<Uri, SemanticTokensDocument> _documents = new();

    private readonly ILogger<SemanticTokensHandler> _logger;

    /// <summary>
    /// Initialises the handler with an <see cref="AnalysisEngine"/> used to retrieve cached analysis results.
    /// </summary>
    /// <param name="analysis">The analysis engine that supplies cached per-document results.</param>
    public SemanticTokensHandler(AnalysisEngine analysis, ILogger<SemanticTokensHandler> logger)
    {
        _analysis = analysis;
        _logger = logger;
    }

    /// <summary>
    /// Creates the registration options advertising the supported token types, modifiers, and capabilities.
    /// </summary>
    /// <param name="capability">The client's semantic tokens capability descriptor.</param>
    /// <param name="clientCapabilities">The full set of client capabilities.</param>
    /// <returns>Registration options including the token legend and full-document token support.</returns>
    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            Legend = new SemanticTokensLegend
            {
                TokenTypes = new Container<SemanticTokenType>(
                    SemanticTokenType.Namespace,     // 0
                    SemanticTokenType.Type,           // 1
                    SemanticTokenType.Struct,         // 2
                    SemanticTokenType.Enum,           // 3
                    SemanticTokenType.Interface,      // 4
                    SemanticTokenType.Function,       // 5
                    SemanticTokenType.Method,         // 6
                    SemanticTokenType.Parameter,      // 7
                    SemanticTokenType.Variable,       // 8
                    SemanticTokenType.Property,       // 9
                    SemanticTokenType.EnumMember      // 10
                ),
                TokenModifiers = new Container<SemanticTokenModifier>(
                    SemanticTokenModifier.Declaration,    // bit 0
                    SemanticTokenModifier.Readonly,       // bit 1
                    SemanticTokenModifier.DefaultLibrary, // bit 2
                    SemanticTokenModifier.Async           // bit 3
                )
            },
            Full = new SemanticTokensCapabilityRequestFull { Delta = false },
            Range = false
        };

    /// <summary>
    /// Tokenizes the document and pushes each token's semantic classification into <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The builder that accumulates encoded token data.</param>
    /// <param name="identifier">Identifies the document to tokenize.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A completed task after all tokens have been classified and pushed.</returns>
    protected override Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
    {
        _logger.LogDebug("SemanticTokens request for {Uri}", identifier.TextDocument.Uri);
        var result = _analysis.GetCachedResult(identifier.TextDocument.Uri.ToUri());
        if (result == null)
        {
            return Task.CompletedTask;
        }

        // Walk AST to classify identifiers
        var walker = new SemanticTokenWalker(result);
        walker.Walk(result.Statements);

        // Push all classified tokens to the builder
        var tokenList = result.Tokens;
        var classified = walker.ClassifiedTokens;
        for (int i = 0; i < tokenList.Count; i++)
        {
            var token = tokenList[i];
            if (token.Type == TokenType.Eof)
            {
                continue;
            }

            var line = token.Span.StartLine - 1;
            var col = token.Span.StartColumn - 1;
            var length = token.Lexeme.Length;

            if (classified.TryGetValue((line, col), out var cls))
            {
                builder.Push(line, col, length, cls.Type, cls.Modifiers);
            }
        }

        _logger.LogDebug("SemanticTokens: tokenized {Uri}", identifier.TextDocument.Uri);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns or creates the <see cref="SemanticTokensDocument"/> for the given document URI,
    /// initialised with the registered token legend.
    /// </summary>
    /// <param name="params">Parameters identifying the document.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The cached <see cref="SemanticTokensDocument"/> for the requested URI.</returns>
    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
        ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
    {
        var uri = @params.TextDocument.Uri.ToUri();
        var document = _documents.GetOrAdd(uri,
            _ => new SemanticTokensDocument(CreateRegistrationOptions(null!, null!).Legend));
        return Task.FromResult(document);
    }
}
