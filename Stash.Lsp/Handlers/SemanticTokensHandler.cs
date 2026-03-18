[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Stash.Tests")]
namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    private readonly ConcurrentDictionary<Uri, SemanticTokensDocument> _documents = new();

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
    private const int TokenTypeComment = 10;
    private const int TokenTypeOperator = 11;

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
                    SemanticTokenType.String,
                    SemanticTokenType.Comment,
                    SemanticTokenType.Operator
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

        TokenType prevType = TokenType.Eof;
        TokenType prevPrevType = TokenType.Eof;

        foreach (var token in result.Tokens)
        {
            if (token.Type == TokenType.Eof)
            {
                continue;
            }

            var line = token.Span.StartLine - 1;   // convert to 0-based
            var col = token.Span.StartColumn - 1;
            var length = token.Lexeme.Length;

            bool isTypeHintPosition =
                (prevType == TokenType.Colon && prevPrevType == TokenType.Identifier) ||
                prevType == TokenType.Arrow;

            if (isTypeHintPosition && (token.Type == TokenType.Identifier || IsKeyword(token.Type)))
            {
                ProcessIdentifier(builder, result, token, line, col, length, isTypeHintPosition: true);
            }
            else if (token.Type == TokenType.Identifier)
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
            else if (token.Type == TokenType.CommandLiteral)
            {
                ProcessCommandLiteral(builder, result, token);
            }
            else if (token.Type == TokenType.InterpolatedString)
            {
                ProcessCompoundToken(builder, result, token);
            }
            else if (IsOperator(token.Type))
            {
                builder.Push(line, col, length, TokenTypeOperator, 0);
            }
            else if (token.Type is TokenType.SingleLineComment or TokenType.BlockComment or TokenType.Shebang)
            {
                builder.Push(line, col, length, TokenTypeComment, 0);
            }
            else if (token.Type == TokenType.DocComment)
            {
                EmitDocComment(builder, token, line, col);
            }

            if (token.Type is not (TokenType.DocComment or TokenType.SingleLineComment or TokenType.BlockComment or TokenType.Shebang))
            {
                prevPrevType = prevType;
                prevType = token.Type;
            }
        }

        return Task.CompletedTask;
    }

    private void ProcessIdentifier(SemanticTokensBuilder builder, AnalysisResult result,
        Token token, int line, int col, int length, bool isTypeHintPosition = false)
    {
        if (isTypeHintPosition && token.Type != TokenType.Identifier)
        {
            // Keyword in type hint position — always color as Type
            builder.Push(line, col, length, TokenTypeType, 0);
            return;
        }

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
            else if (isTypeHintPosition)
            {
                // Unresolved identifier in type hint position — likely a type name
                builder.Push(line, col, length, TokenTypeType, 0);
            }
        }
    }

    private void ProcessCommandLiteral(SemanticTokensBuilder builder, AnalysisResult result, Token token)
    {
        if (token.Literal is not List<object> parts)
        {
            return;
        }

        // Highlight the command name (first word of the first text segment)
        bool commandNameFound = false;
        int textOffset = 2; // skip past "$("

        for (int i = 0; i < parts.Count; i++)
        {
            if (!commandNameFound && parts[i] is string text)
            {
                // Find the first word in this text segment
                int wordStart = 0;
                while (wordStart < text.Length && char.IsWhiteSpace(text[wordStart]))
                {
                    wordStart++;
                }

                if (wordStart < text.Length)
                {
                    int wordEnd = wordStart;
                    while (wordEnd < text.Length && !char.IsWhiteSpace(text[wordEnd]))
                    {
                        wordEnd++;
                    }

                    int wordLength = wordEnd - wordStart;
                    if (wordLength > 0)
                    {
                        // Calculate position: token start + "$(" offset + text offset + leading whitespace
                        int cmdLine = token.Span.StartLine - 1;
                        int cmdCol = token.Span.StartColumn - 1 + textOffset + wordStart;
                        builder.Push(cmdLine, cmdCol, wordLength, TokenTypeFunction, 0);
                        commandNameFound = true;
                    }
                }
                textOffset += text.Length;
            }
            else if (parts[i] is string otherText)
            {
                textOffset += otherText.Length;
            }
            else if (parts[i] is List<Token> subTokens)
            {
                // Process embedded expression tokens
                foreach (var subToken in subTokens)
                {
                    if (subToken.Type == TokenType.Eof)
                    {
                        continue;
                    }

                    var subLine = subToken.Span.StartLine - 1;
                    var subCol = subToken.Span.StartColumn - 1;
                    var subLength = subToken.Lexeme.Length;

                    if (subToken.Type == TokenType.Identifier)
                    {
                        ProcessIdentifier(builder, result, subToken, subLine, subCol, subLength);
                    }
                    else if (IsKeyword(subToken.Type))
                    {
                        builder.Push(subLine, subCol, subLength, TokenTypeKeyword, 0);
                    }
                    else if (subToken.Type is TokenType.IntegerLiteral or TokenType.FloatLiteral)
                    {
                        builder.Push(subLine, subCol, subLength, TokenTypeNumber, 0);
                    }
                    else if (subToken.Type == TokenType.StringLiteral)
                    {
                        builder.Push(subLine, subCol, subLength, TokenTypeString, 0);
                    }
                    else if (IsOperator(subToken.Type))
                    {
                        builder.Push(subLine, subCol, subLength, TokenTypeOperator, 0);
                    }
                    else if (subToken.Type == TokenType.CommandLiteral)
                    {
                        ProcessCommandLiteral(builder, result, subToken);
                    }
                    else if (subToken.Type == TokenType.InterpolatedString)
                    {
                        ProcessCompoundToken(builder, result, subToken);
                    }
                }
                // Account for {} delimiters in offset
                // Each interpolation adds at least 2 chars for { and }
                // But we don't need to track textOffset past the first text segment
            }
        }
    }

    private void ProcessCompoundToken(SemanticTokensBuilder builder, AnalysisResult result, Token token)
    {
        if (token.Literal is not List<object> parts)
        {
            return;
        }

        foreach (var part in parts)
        {
            if (part is List<Token> subTokens)
            {
                foreach (var subToken in subTokens)
                {
                    if (subToken.Type == TokenType.Eof)
                    {
                        continue;
                    }

                    var subLine = subToken.Span.StartLine - 1;
                    var subCol = subToken.Span.StartColumn - 1;
                    var subLength = subToken.Lexeme.Length;

                    if (subToken.Type == TokenType.Identifier)
                    {
                        ProcessIdentifier(builder, result, subToken, subLine, subCol, subLength);
                    }
                    else if (IsKeyword(subToken.Type))
                    {
                        builder.Push(subLine, subCol, subLength, TokenTypeKeyword, 0);
                    }
                    else if (subToken.Type is TokenType.IntegerLiteral or TokenType.FloatLiteral)
                    {
                        builder.Push(subLine, subCol, subLength, TokenTypeNumber, 0);
                    }
                    else if (subToken.Type == TokenType.StringLiteral)
                    {
                        builder.Push(subLine, subCol, subLength, TokenTypeString, 0);
                    }
                    else if (IsOperator(subToken.Type))
                    {
                        builder.Push(subLine, subCol, subLength, TokenTypeOperator, 0);
                    }
                    else if (subToken.Type is TokenType.CommandLiteral or TokenType.InterpolatedString)
                    {
                        // Recurse for nested compound tokens
                        ProcessCompoundToken(builder, result, subToken);
                    }
                }
            }
        }
    }

    private static void EmitDocComment(SemanticTokensBuilder builder, Token token, int startLine, int startCol)
    {
        var lexeme = token.Lexeme;

        // Multi-line block doc comment: /** ... */
        if (lexeme.StartsWith("/**"))
        {
            var lines = lexeme.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var lineText = lines[i];
                if (lineText.Length == 0)
                {
                    continue;
                }

                int currentLine = startLine + i;
                int currentCol = i == 0 ? startCol : 0;
                EmitDocLine(builder, lineText, currentLine, currentCol);
            }
        }
        else
        {
            // Single-line /// comment
            EmitDocLine(builder, lexeme, startLine, startCol);
        }
    }

    private static void EmitDocLine(SemanticTokensBuilder builder, string text, int line, int col)
    {
        var segments = FindDocTagSegments(text);
        foreach (var seg in segments)
        {
            builder.Push(line, col + seg.Offset, seg.Length, seg.IsTag ? TokenTypeKeyword : TokenTypeComment, 0);
        }
    }

    internal readonly record struct DocTagSegment(int Offset, int Length, bool IsTag);

    internal static List<DocTagSegment> FindDocTagSegments(string text)
    {
        var segments = new List<DocTagSegment>();
        int pos = 0;

        while (pos < text.Length)
        {
            int tagStart = text.IndexOf('@', pos);
            if (tagStart < 0)
            {
                break;
            }

            int tagLen = 0;
            if (MatchTag(text, tagStart, "@returns"))
            {
                tagLen = 8;
            }
            else if (MatchTag(text, tagStart, "@return"))
            {
                tagLen = 7;
            }
            else if (MatchTag(text, tagStart, "@param"))
            {
                tagLen = 6;
            }

            if (tagLen == 0)
            {
                pos = tagStart + 1;
                continue;
            }

            if (tagStart > pos)
            {
                segments.Add(new DocTagSegment(pos, tagStart - pos, false));
            }

            segments.Add(new DocTagSegment(tagStart, tagLen, true));
            pos = tagStart + tagLen;
        }

        if (pos < text.Length)
        {
            segments.Add(new DocTagSegment(pos, text.Length - pos, false));
        }
        else if (pos == 0 && text.Length > 0)
        {
            segments.Add(new DocTagSegment(0, text.Length, false));
        }

        return segments;
    }

    internal static bool MatchTag(string text, int start, string tag)
    {
        if (start + tag.Length > text.Length)
        {
            return false;
        }

        if (text.AsSpan(start, tag.Length).SequenceEqual(tag.AsSpan()))
        {
            // Must be followed by non-alphanumeric (word boundary) or end of string
            int after = start + tag.Length;
            return after >= text.Length || !char.IsLetterOrDigit(text[after]);
        }

        return false;
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
        TokenType.In or TokenType.While or TokenType.Do or TokenType.Return or TokenType.Break or
        TokenType.Continue or TokenType.True or TokenType.False or TokenType.Null or
        TokenType.Try or TokenType.Import or TokenType.From or TokenType.As or
        TokenType.Switch;

    private static bool IsOperator(TokenType type) => type is
        TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or
        TokenType.Percent or TokenType.Bang or TokenType.Less or TokenType.Greater or
        TokenType.Equal or TokenType.EqualEqual or TokenType.BangEqual or
        TokenType.LessEqual or TokenType.GreaterEqual or TokenType.AmpersandAmpersand or
        TokenType.PipePipe or TokenType.QuestionQuestion or TokenType.PlusPlus or
        TokenType.MinusMinus or TokenType.Arrow or TokenType.FatArrow or
        TokenType.Pipe or TokenType.GreaterGreater or TokenType.AmpersandGreater or
        TokenType.AmpersandGreaterGreater or TokenType.TwoGreater or
        TokenType.TwoGreaterGreater;

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
        ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
    {
        var uri = @params.TextDocument.Uri.ToUri();
        var document = _documents.GetOrAdd(uri,
            _ => new SemanticTokensDocument(CreateRegistrationOptions(null!, null!).Legend));
        return Task.FromResult(document);
    }
}
