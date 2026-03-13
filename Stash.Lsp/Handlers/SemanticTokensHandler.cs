namespace Stash.Lsp.Handlers;

using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lexing;
using Stash.Lsp.Analysis;
using StashSymbolKind = Stash.Lsp.Analysis.SymbolKind;

public class SemanticTokensHandler : SemanticTokensHandlerBase
{
    private readonly AnalysisEngine _analysis;

    // Token type indices (must match legend registration)
    private const int TokenTypeNamespace = 0;
    private const int TokenTypeType = 1;
    private const int TokenTypeFunction = 2;
    private const int TokenTypeParameter = 3;
    private const int TokenTypeVariable = 4;
    private const int TokenTypeProperty = 5;
    private const int TokenTypeEnumMember = 6;
    private const int TokenTypeKeyword = 7;
    private const int TokenTypeNumber = 8;
    private const int TokenTypeString = 9;

    // Token modifier bit flags
    private const int ModifierDeclaration = 1 << 0;
    private const int ModifierReadonly = 1 << 1;

    public SemanticTokensHandler(AnalysisEngine analysis)
    {
        _analysis = analysis;
    }

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            Legend = new SemanticTokensLegend
            {
                TokenTypes = new Container<SemanticTokenType>(
                    SemanticTokenType.Namespace,
                    SemanticTokenType.Type,
                    SemanticTokenType.Function,
                    SemanticTokenType.Parameter,
                    SemanticTokenType.Variable,
                    SemanticTokenType.Property,
                    SemanticTokenType.EnumMember,
                    SemanticTokenType.Keyword,
                    SemanticTokenType.Number,
                    SemanticTokenType.String
                ),
                TokenModifiers = new Container<SemanticTokenModifier>(
                    SemanticTokenModifier.Declaration,
                    SemanticTokenModifier.Readonly
                )
            },
            Full = new SemanticTokensCapabilityRequestFull { Delta = false },
            Range = false
        };

    protected override Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
    {
        var result = _analysis.GetCachedResult(identifier.TextDocument.Uri.ToUri());
        if (result == null)
        {
            return Task.CompletedTask;
        }

        foreach (var token in result.Tokens)
        {
            if (token.Type == TokenType.Eof)
            {
                continue;
            }

            var line = token.Span.StartLine - 1;   // convert to 0-based
            var col = token.Span.StartColumn - 1;
            var length = token.Lexeme.Length;

            if (token.Type == TokenType.Identifier)
            {
                ProcessIdentifier(builder, result, token, line, col, length);
            }
            else if (IsKeyword(token.Type))
            {
                builder.Push(line, col, length, TokenTypeKeyword, 0);
            }
            else if (token.Type is TokenType.IntegerLiteral or TokenType.FloatLiteral)
            {
                builder.Push(line, col, length, TokenTypeNumber, 0);
            }
            else if (token.Type == TokenType.StringLiteral)
            {
                builder.Push(line, col, length, TokenTypeString, 0);
            }
        }

        return Task.CompletedTask;
    }

    private void ProcessIdentifier(SemanticTokensBuilder builder, AnalysisResult result,
        Token token, int line, int col, int length)
    {
        var name = token.Lexeme;
        var spanLine = token.Span.StartLine;
        var spanCol = token.Span.StartColumn;

        var definition = result.Symbols.FindDefinition(name, spanLine, spanCol);

        if (definition != null)
        {
            var (tokenType, modifiers) = MapSymbolKind(definition, token);
            builder.Push(line, col, length, tokenType, modifiers);
        }
        else
        {
            // Unresolved — try built-in names
            if (BuiltInRegistry.IsBuiltInFunction(name))
            {
                builder.Push(line, col, length, TokenTypeFunction, ModifierReadonly);
            }
            else if (BuiltInRegistry.IsBuiltInNamespace(name))
            {
                builder.Push(line, col, length, TokenTypeNamespace, 0);
            }
        }
    }

    private static (int TokenType, int Modifiers) MapSymbolKind(SymbolInfo definition, Token token)
    {
        bool isDeclaration = definition.Span.StartLine == token.Span.StartLine &&
                             definition.Span.StartColumn == token.Span.StartColumn;

        int modifiers = isDeclaration ? ModifierDeclaration : 0;

        int tokenType = definition.Kind switch
        {
            StashSymbolKind.Function => TokenTypeFunction,
            StashSymbolKind.Variable => TokenTypeVariable,
            StashSymbolKind.Constant => TokenTypeVariable,
            StashSymbolKind.Parameter => TokenTypeParameter,
            StashSymbolKind.Struct => TokenTypeType,
            StashSymbolKind.Enum => TokenTypeType,
            StashSymbolKind.Field => TokenTypeProperty,
            StashSymbolKind.EnumMember => TokenTypeEnumMember,
            StashSymbolKind.LoopVariable => TokenTypeVariable,
            StashSymbolKind.Namespace => TokenTypeNamespace,
            _ => TokenTypeVariable
        };

        if (definition.Kind == StashSymbolKind.Constant)
        {
            modifiers |= ModifierReadonly;
        }

        return (tokenType, modifiers);
    }

    private static bool IsKeyword(TokenType type) => type is
        TokenType.Let or TokenType.Const or TokenType.Fn or TokenType.Struct or
        TokenType.Enum or TokenType.If or TokenType.Else or TokenType.For or
        TokenType.In or TokenType.While or TokenType.Return or TokenType.Break or
        TokenType.Continue or TokenType.True or TokenType.False or TokenType.Null or
        TokenType.Try or TokenType.Import or TokenType.From or TokenType.As;

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
        ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SemanticTokensDocument(CreateRegistrationOptions(null!, null!).Legend));
    }
}
