[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Stash.Tests")]
namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
/// Classifies tokens into semantic types (namespace, type, function, parameter, variable,
/// property, enumMember, keyword, number, string, comment, operator) and applies modifiers
/// (declaration, readonly). Uses the <see cref="AnalysisEngine"/> cached result's token list
/// and symbol table for accurate per-token classification.
/// </para>
/// <para>
/// Special handling is applied for: post-dot member access, dict literal keys vs. struct init
/// fields, embedded expressions inside interpolated strings and command literals, and doc-comment
/// tag highlighting (<c>@param</c>, <c>@return</c>, <c>@returns</c>).
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
                    SemanticTokenType.Operator,
                    SemanticTokenType.Interface
                ),
                TokenModifiers = new Container<SemanticTokenModifier>(
                    SemanticTokenModifier.Declaration,
                    SemanticTokenModifier.Readonly
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

        // Phase 1: Walk AST to classify identifiers
        var walker = new SemanticTokenWalker(result);
        walker.Walk(result.Statements);
        var classified = walker.ClassifiedTokens;

        // Phase 2: Iterate token stream
        var tokenList = result.Tokens;
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

            // Check if the walker classified this position
            if (classified.TryGetValue((line, col), out var cls))
            {
                builder.Push(line, col, length, cls.Type, cls.Modifiers);
            }
            else if (token.Lexeme is "and" or "or" or "in")
            {
                // Must precede IsOperator — and/or share token types with &&/||
            }
            else if (IsKeyword(token.Type))
            {
                builder.Push(line, col, length, TokenTypeKeyword, 0);
            }
            else if (token.Type is TokenType.IntegerLiteral or TokenType.FloatLiteral)
            {
                builder.Push(line, col, length, TokenTypeNumber, 0);
            }
            else if (token.Type is TokenType.DurationLiteral or TokenType.ByteSizeLiteral)
            {
                builder.Push(line, col, length, TokenTypeNumber, 0);
            }
            else if (token.Type == TokenType.StringLiteral)
            {
                builder.Push(line, col, length, TokenTypeString, 0);
            }
            else if (token.Type == TokenType.IpAddressLiteral)
            {
                // Split: `@` → operator, address body → number, `/` → operator, CIDR prefix → number
                builder.Push(line, col, 1, TokenTypeOperator, 0);
                string lexeme = token.Lexeme;
                int slashIndex = lexeme.IndexOf('/', 1);
                if (slashIndex < 0)
                {
                    builder.Push(line, col + 1, length - 1, TokenTypeNumber, 0);
                }
                else
                {
                    builder.Push(line, col + 1, slashIndex - 1, TokenTypeNumber, 0);
                    builder.Push(line, col + slashIndex, 1, TokenTypeOperator, 0);
                    builder.Push(line, col + slashIndex + 1, length - slashIndex - 1, TokenTypeNumber, 0);
                }
            }
            else if (token.Type == TokenType.SemVerLiteral)
            {
                // Split: `@` → operator, `v` + version → number
                builder.Push(line, col, 1, TokenTypeOperator, 0);
                builder.Push(line, col + 1, length - 1, TokenTypeNumber, 0);
            }
            else if (token.Type is TokenType.CommandLiteral or TokenType.PassthroughCommandLiteral)
            {
                ProcessCommandLiteral(builder, classified, token);
            }
            else if (token.Type == TokenType.InterpolatedString)
            {
                ProcessCompoundToken(builder, classified, token);
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
        }

        _logger.LogDebug("SemanticTokens: tokenized {Uri}", identifier.TextDocument.Uri);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Tokenizes a command literal, highlighting the command name and any embedded expression tokens.
    /// </summary>
    /// <param name="builder">The token builder to receive classifications.</param>
    /// <param name="classified">Pre-classified identifier positions from the AST walker.</param>
    /// <param name="token">The command literal token whose <c>Literal</c> contains the parsed parts list.</param>
    private void ProcessCommandLiteral(SemanticTokensBuilder builder,
        IReadOnlyDictionary<(int Line, int Col), (int Type, int Modifiers)> classified, Token token)
    {
        if (token.Literal is not List<object> parts)
        {
            return;
        }

        // Highlight the command name (first word of the first text segment)
        bool commandNameFound = false;

        // The lexeme always includes a synthetic "$(" or "$>(" prefix, but for piped
        // command segments the SourceSpan starts at the command text (after pipe + whitespace),
        // not at the prefix. Detect this by comparing span width to lexeme length.
        int prefixLen = token.Lexeme.StartsWith("$>(") ? 3 :
                        token.Lexeme.StartsWith("$(") ? 2 : 0;
        int textOffset = prefixLen;
        if (prefixLen > 0 && token.Span.StartLine == token.Span.EndLine)
        {
            int spanWidth = token.Span.EndColumn - token.Span.StartColumn + 1;
            if (spanWidth < token.Lexeme.Length)
                textOffset = 0;
        }

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
                for (int j = 0; j < subTokens.Count; j++)
                {
                    var subToken = subTokens[j];
                    if (subToken.Type == TokenType.Eof)
                    {
                        continue;
                    }

                    var subLine = subToken.Span.StartLine - 1;
                    var subCol = subToken.Span.StartColumn - 1;
                    var subLength = subToken.Lexeme.Length;

                    if (subToken.Type == TokenType.Identifier)
                    {
                        var key = (subToken.Span.StartLine - 1, subToken.Span.StartColumn - 1);
                        if (classified.TryGetValue(key, out var identCls))
                        {
                            builder.Push(key.Item1, key.Item2, subToken.Lexeme.Length, identCls.Type, identCls.Modifiers);
                        }
                    }
                    else if (subToken.Lexeme is "and" or "or" or "in")
                    {
                        // Must precede IsOperator — and/or share token types with &&/||
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
                    else if (subToken.Type == TokenType.Dot || IsOperator(subToken.Type) || IsPunctuation(subToken.Type))
                    {
                        builder.Push(subLine, subCol, subLength, TokenTypeOperator, 0);
                    }
                    else if (subToken.Type is TokenType.CommandLiteral or TokenType.PassthroughCommandLiteral)
                    {
                        ProcessCommandLiteral(builder, classified, subToken);
                    }
                    else if (subToken.Type == TokenType.InterpolatedString)
                    {
                        ProcessCompoundToken(builder, classified, subToken);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Tokenizes an interpolated string literal, recursing into each embedded expression segment.
    /// </summary>
    /// <param name="builder">The token builder to receive classifications.</param>
    /// <param name="classified">Pre-classified identifier positions from the AST walker.</param>
    /// <param name="token">The interpolated string token whose <c>Literal</c> contains the parts list.</param>
    private void ProcessCompoundToken(SemanticTokensBuilder builder,
        IReadOnlyDictionary<(int Line, int Col), (int Type, int Modifiers)> classified, Token token)
    {
        if (token.Literal is not List<object> parts)
        {
            return;
        }

        int baseLine = token.Span.StartLine - 1;
        int baseCol = token.Span.StartColumn - 1;
        string lexeme = token.Lexeme;

        // Determine prefix length: handle triple-quoted ($""" or """) and regular ($" or ")
        int prefixLen;
        if (lexeme.StartsWith("$\"\"\""))
        {
            prefixLen = 4; // $"""
            // Skip leading newline after opening triple quote
            if (prefixLen < lexeme.Length && lexeme[prefixLen] == '\n')
            {
                prefixLen++;
            }
        }
        else if (lexeme.StartsWith("\"\"\""))
        {
            prefixLen = 3; // """
            if (prefixLen < lexeme.Length && lexeme[prefixLen] == '\n')
            {
                prefixLen++;
            }
        }
        else if (lexeme.StartsWith("$\""))
        {
            prefixLen = 2; // $"
        }
        else
        {
            prefixLen = 1; // "
        }

        // Cursor tracking for multi-line support
        int curLine = baseLine;
        int curCol = baseCol;
        int offset = 0;

        // Emit opening prefix ($" or ") as string
        builder.Push(curLine, curCol, prefixLen, TokenTypeString, 0);
        AdvanceCursor(lexeme, ref offset, prefixLen, ref curLine, ref curCol);

        // Track whether we're at a line start (true after prefix ends with \n or after text ending with \n)
        bool atLineStart = prefixLen > 0 && offset > 0 && lexeme[offset - 1] == '\n';

        foreach (var part in parts)
        {
            if (part is string textSegment)
            {
                int lexemeLen = CalculateLexemeLength(lexeme, offset, textSegment, atLineStart);
                if (lexemeLen > 0)
                {
                    EmitStringSegment(builder, lexeme, offset, lexemeLen, curLine, curCol);
                }
                AdvanceCursor(lexeme, ref offset, lexemeLen, ref curLine, ref curCol);
                atLineStart = textSegment.Length > 0 && textSegment[^1] == '\n';
            }
            else if (part is List<Token> subTokens)
            {
                // Don't emit semantic token for { — let TextMate handle with punctuation.definition.interpolation scope
                AdvanceCursor(lexeme, ref offset, 1, ref curLine, ref curCol);

                // Classify sub-tokens within the expression
                for (int j = 0; j < subTokens.Count; j++)
                {
                    var subToken = subTokens[j];
                    if (subToken.Type == TokenType.Eof)
                    {
                        continue;
                    }

                    var subLine = subToken.Span.StartLine - 1;
                    var subCol = subToken.Span.StartColumn - 1;
                    var subLength = subToken.Lexeme.Length;

                    if (subToken.Type == TokenType.Identifier)
                    {
                        var key = (subToken.Span.StartLine - 1, subToken.Span.StartColumn - 1);
                        if (classified.TryGetValue(key, out var identCls))
                        {
                            builder.Push(key.Item1, key.Item2, subToken.Lexeme.Length, identCls.Type, identCls.Modifiers);
                        }
                    }
                    else if (subToken.Lexeme is "and" or "or" or "in")
                    {
                        // Must precede IsOperator — and/or share token types with &&/||
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
                    else if (subToken.Type == TokenType.Dot || IsOperator(subToken.Type) || IsPunctuation(subToken.Type))
                    {
                        builder.Push(subLine, subCol, subLength, TokenTypeOperator, 0);
                    }
                    else if (subToken.Type is TokenType.CommandLiteral or TokenType.PassthroughCommandLiteral)
                    {
                        ProcessCommandLiteral(builder, classified, subToken);
                    }
                    else if (subToken.Type == TokenType.InterpolatedString)
                    {
                        ProcessCompoundToken(builder, classified, subToken);
                    }
                }

                // Find the matching } by scanning with brace-depth tracking
                // offset is currently past the opening {, so depth starts at 1
                int depth = 1;
                int scanPos = offset;
                while (scanPos < lexeme.Length && depth > 0)
                {
                    char c = lexeme[scanPos];
                    if (c == '{')
                    {
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                    }

                    if (depth > 0)
                    {
                        scanPos++;
                    }
                }

                // Don't emit semantic token for } — let TextMate handle with punctuation.definition.interpolation scope
                int charsBeforeClosingBrace = scanPos - offset;
                if (charsBeforeClosingBrace > 0)
                {
                    AdvanceCursor(lexeme, ref offset, charsBeforeClosingBrace, ref curLine, ref curCol);
                }
                AdvanceCursor(lexeme, ref offset, 1, ref curLine, ref curCol);
                atLineStart = false;
            }
        }

        // Emit closing quote(s) as string
        int remaining = lexeme.Length - offset;
        if (remaining > 0)
        {
            builder.Push(curLine, curCol, remaining, TokenTypeString, 0);
        }
    }

    private static void AdvanceCursor(string lexeme, ref int offset, int count, ref int line, ref int col)
    {
        for (int i = 0; i < count && offset < lexeme.Length; i++)
        {
            if (lexeme[offset] == '\n')
            {
                line++;
                col = 0;
            }
            else
            {
                col++;
            }
            offset++;
        }
    }

    private void EmitStringSegment(SemanticTokensBuilder builder, string lexeme, int offset, int length, int startLine, int startCol)
    {
        int currentLine = startLine;
        int currentCol = startCol;
        int segStart = offset;

        for (int i = 0; i < length; i++)
        {
            if (offset + i < lexeme.Length && lexeme[offset + i] == '\n')
            {
                int segLen = offset + i - segStart;
                if (segLen > 0)
                {
                    builder.Push(currentLine, currentCol, segLen, TokenTypeString, 0);
                }
                currentLine++;
                currentCol = 0;
                segStart = offset + i + 1;
            }
        }

        int remainingLen = offset + length - segStart;
        if (remainingLen > 0)
        {
            builder.Push(currentLine, currentCol, remainingLen, TokenTypeString, 0);
        }
    }

    /// <summary>
    /// Calculates how many characters in the raw <paramref name="lexeme"/> correspond to a
    /// text segment from the parts list that may have had indentation stripped.
    /// After each newline in the text, skips over leading whitespace in the lexeme that
    /// was removed during indent stripping. The <paramref name="atLineStart"/> flag handles
    /// the first segment which may also have stripped leading whitespace.
    /// </summary>
    private static int CalculateLexemeLength(string lexeme, int offset, string textSegment, bool atLineStart)
    {
        if (textSegment.Length == 0)
        {
            // Even an empty segment may correspond to stripped indent at line start
            if (atLineStart)
            {
                int skip = 0;
                while (offset + skip < lexeme.Length && lexeme[offset + skip] is ' ' or '\t')
                {
                    skip++;
                }

                return skip;
            }
            return 0;
        }

        int lexemePos = offset;
        int textPos = 0;

        // Handle stripped indent at the very start of the first segment
        if (atLineStart)
        {
            int textIndent = 0;
            while (textIndent < textSegment.Length && textSegment[textIndent] is ' ' or '\t')
            {
                textIndent++;
            }

            int lexIndent = 0;
            while (lexemePos + lexIndent < lexeme.Length && lexeme[lexemePos + lexIndent] is ' ' or '\t')
            {
                lexIndent++;
            }

            int stripped = lexIndent - textIndent;
            if (stripped > 0)
            {
                lexemePos += stripped;
            }
        }

        while (textPos < textSegment.Length && lexemePos < lexeme.Length)
        {
            if (textSegment[textPos] == '\n')
            {
                // Match the newline in both
                lexemePos++;
                textPos++;

                // Count whitespace at start of next line in text
                int textIndent = 0;
                while (textPos + textIndent < textSegment.Length && textSegment[textPos + textIndent] is ' ' or '\t')
                {
                    textIndent++;
                }

                // Count whitespace at start of next line in lexeme
                int lexIndent = 0;
                while (lexemePos + lexIndent < lexeme.Length && lexeme[lexemePos + lexIndent] is ' ' or '\t')
                {
                    lexIndent++;
                }

                // Skip the extra (stripped) indent in lexeme
                int stripped = lexIndent - textIndent;
                if (stripped > 0)
                {
                    lexemePos += stripped;
                }
            }
            else
            {
                lexemePos++;
                textPos++;
            }
        }

        return lexemePos - offset;
    }

    /// <summary>
    /// Emits token classifications for a doc-comment token, handling both single-line
    /// (<c>///</c>) and multi-line (<c>/** … */</c>) forms.
    /// </summary>
    /// <param name="builder">The token builder to push segments into.</param>
    /// <param name="token">The doc-comment token.</param>
    /// <param name="startLine">0-based starting line of the token.</param>
    /// <param name="startCol">0-based starting column of the token.</param>
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

    /// <summary>
    /// Emits token classifications for a single line of a doc comment, splitting the text into
    /// comment and keyword segments around any recognised doc tags.
    /// </summary>
    /// <param name="builder">The token builder to push segments into.</param>
    /// <param name="text">The text of a single doc-comment line.</param>
    /// <param name="line">0-based line number.</param>
    /// <param name="col">0-based starting column of the line's first character.</param>
    private static void EmitDocLine(SemanticTokensBuilder builder, string text, int line, int col)
    {
        var segments = FindDocTagSegments(text);
        foreach (var seg in segments)
        {
            builder.Push(line, col + seg.Offset, seg.Length, seg.IsTag ? TokenTypeKeyword : TokenTypeComment, 0);
        }
    }

    /// <summary>Represents a contiguous segment of a doc-comment line, flagged as either a doc tag or plain text.</summary>
    internal readonly record struct DocTagSegment(int Offset, int Length, bool IsTag);

    /// <summary>
    /// Splits <paramref name="text"/> into segments, marking recognised doc tags
    /// (<c>@param</c>, <c>@return</c>, <c>@returns</c>) as keyword segments and surrounding
    /// text as comment segments.
    /// </summary>
    /// <param name="text">A single line of doc-comment text to analyse.</param>
    /// <returns>An ordered list of <see cref="DocTagSegment"/> covering the full line.</returns>
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

    /// <summary>
    /// Tests whether <paramref name="text"/> contains <paramref name="tag"/> at position
    /// <paramref name="start"/> followed by a word boundary or end-of-string.
    /// </summary>
    /// <param name="text">The text to search within.</param>
    /// <param name="start">Position in <paramref name="text"/> at which to check.</param>
    /// <param name="tag">The exact tag string to match (e.g., <c>"@param"</c>).</param>
    /// <returns><see langword="true"/> if the tag matches at the given position; otherwise <see langword="false"/>.</returns>
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

    /// <summary>Returns <see langword="true"/> when <paramref name="type"/> is a Stash keyword token.</summary>
    private static bool IsKeyword(TokenType type) => type is
        TokenType.Let or TokenType.Const or
        TokenType.Enum or TokenType.If or TokenType.Else or TokenType.For or
        TokenType.While or TokenType.Do or TokenType.Break or
        TokenType.Continue or
        TokenType.Try or TokenType.Import or TokenType.As or
        TokenType.Retry or TokenType.Timeout or TokenType.Switch or TokenType.Case or TokenType.Default;

    /// <summary>Returns <see langword="true"/> when <paramref name="type"/> is a punctuation token.</summary>
    private static bool IsPunctuation(TokenType type) => type is
        TokenType.LeftParen or TokenType.RightParen or
        TokenType.LeftBracket or TokenType.RightBracket or
        TokenType.LeftBrace or TokenType.RightBrace or
        TokenType.Comma or TokenType.Colon or
        TokenType.QuestionMark or TokenType.DotDot or
        TokenType.QuestionDot;

    /// <summary>Returns <see langword="true"/> when <paramref name="type"/> is an operator token.</summary>
    private static bool IsOperator(TokenType type) => type is
        TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or
        TokenType.Percent or TokenType.Bang or TokenType.Less or TokenType.Greater or
        TokenType.Equal or TokenType.EqualEqual or TokenType.BangEqual or
        TokenType.LessEqual or TokenType.GreaterEqual or TokenType.AmpersandAmpersand or
        TokenType.PipePipe or TokenType.QuestionQuestion or TokenType.PlusPlus or
        TokenType.MinusMinus or TokenType.Arrow or TokenType.FatArrow or
        TokenType.Pipe or TokenType.GreaterGreater or TokenType.AmpersandGreater or
        TokenType.AmpersandGreaterGreater or TokenType.TwoGreater or
        TokenType.TwoGreaterGreater or TokenType.Ampersand or TokenType.Caret or
        TokenType.Tilde or TokenType.LessLess or TokenType.AmpersandEqual or
        TokenType.PipeEqual or TokenType.CaretEqual or TokenType.LessLessEqual or
        TokenType.GreaterGreaterEqual;

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
