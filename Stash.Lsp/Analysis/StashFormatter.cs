using System;
using System.Collections.Generic;
using System.Text;
using Stash.Lexing;

namespace Stash.Lsp.Analysis;

/// <summary>
/// Reformats a Stash source file by re-lexing it (with trivia preserved) and emitting
/// tokens with canonical whitespace according to a deterministic set of formatting rules.
/// </summary>
/// <remarks>
/// <para>
/// Formatting is token-stream based: the formatter lexes the source with
/// <c>preserveTrivia: true</c> so that comments are included, then iterates over the token
/// stream and determines what whitespace to insert before each token via <see cref="GetWhitespace"/>.
/// No AST is constructed.
/// </para>
/// <para>
/// Key formatting rules applied by <see cref="GetWhitespace"/>:
/// </para>
/// <list type="number">
///   <item><description>Single blank line between top-level declarations (after <c>}</c> at indent 0, after top-level <c>;</c>).</description></item>
///   <item><description>Newline + indent inside block bodies (<c>{ … }</c>) and after semicolons.</description></item>
///   <item><description>No space before <c>;</c>, <c>,</c>, <c>)</c>, <c>]</c>, or <c>.</c>.</description></item>
///   <item><description>Space around binary operators, assignment (<c>=</c>), arrows (<c>-&gt;</c>, <c>=&gt;</c>), and ternary operators (<c>?</c>, <c>:</c>).</description></item>
///   <item><description>No space between a function name and its argument list (<c>fn(</c>), but space between a control keyword and its parenthesis (<c>if (</c>).</description></item>
///   <item><description>No space after <c>!</c> (logical not) or before/after <c>++</c>/<c>--</c> in postfix or prefix position.</description></item>
///   <item><description>Struct/enum body members are separated by newlines; struct initializer fields use inline spacing.</description></item>
///   <item><description>Multi-line array literals are preserved: elements are placed on separate indented lines.</description></item>
///   <item><description>Inline comments on the same line as the preceding token are kept on that line.</description></item>
///   <item><description>Block comments are re-indented to the current indent level.</description></item>
/// </list>
/// <para>
/// The formatter is used by the LSP <c>textDocument/formatting</c> and
/// <c>textDocument/rangeFormatting</c> handlers in <see cref="AnalysisEngine"/>.
/// </para>
/// </remarks>
public class StashFormatter
{
    /// <summary>Number of spaces per indent level when <see cref="_useTabs"/> is <see langword="false"/>.</summary>
    private readonly int _indentSize;

    /// <summary>When <see langword="true"/>, a single tab character is used per indent level instead of spaces.</summary>
    private readonly bool _useTabs;

    /// <summary>
    /// Initializes a new <see cref="StashFormatter"/> with the given indentation settings.
    /// </summary>
    /// <param name="indentSize">Number of spaces per indent level (ignored when <paramref name="useTabs"/> is <see langword="true"/>). Defaults to 2.</param>
    /// <param name="useTabs">Use tab characters instead of spaces for indentation. Defaults to <see langword="false"/>.</param>
    public StashFormatter(int indentSize = 2, bool useTabs = false)
    {
        _indentSize = indentSize;
        _useTabs = useTabs;
    }

    /// <summary>
    /// Appends the indentation string for the given nesting <paramref name="level"/> to
    /// <paramref name="sb"/>. Uses a tab per level when <see cref="_useTabs"/> is set,
    /// otherwise <see cref="_indentSize"/> spaces per level.
    /// </summary>
    /// <param name="sb">The string builder to append to.</param>
    /// <param name="level">The current nesting level (zero-based).</param>
    private void AppendIndent(StringBuilder sb, int level)
    {
        var indentChar = _useTabs ? '\t' : ' ';
        var count = _useTabs ? level : level * _indentSize;
        sb.Append(new string(indentChar, count));
    }

    /// <summary>
    /// Categorises the syntactic context currently being formatted, used to decide between
    /// inline spacing (struct initializers) and block formatting (bodies, enums).
    /// </summary>
    private enum FormatterContext { TopLevel, EnumBody, StructBody, Block, Parens, Brackets, StructInit, SwitchBody }

    /// <summary>
    /// The type of whitespace to insert before the current token as determined by
    /// <see cref="GetWhitespace"/>.
    /// </summary>
    private enum Whitespace { None, Space, NewLine, BlankLine }

    /// <summary>
    /// Formats the given Stash <paramref name="source"/> string and returns the reformatted
    /// result with a trailing newline. Returns an empty string if the source contains only
    /// whitespace.
    /// </summary>
    /// <param name="source">The raw Stash source text to format.</param>
    /// <returns>The formatted source text.</returns>
    public string Format(string source)
    {
        var lexer = new Lexer(source, "<format>", preserveTrivia: true);
        var tokens = lexer.ScanTokens();

        var sb = new StringBuilder();
        int indentLevel = 0;
        var contextStack = new Stack<FormatterContext>();
        contextStack.Push(FormatterContext.TopLevel);
        TokenType? prevType = null;
        Token? prevToken = null;
        bool prevWasUnaryMinusPlus = false;
        string? pendingKeyword = null;
        bool prevClosedInline = false;
        int ternaryDepth = 0;
        var multiLineBrackets = new Stack<bool>();
        var multiLineBraces = new Stack<bool>();

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type == TokenType.Eof)
            {
                break;
            }

            var context = contextStack.Count > 0 ? contextStack.Peek() : FormatterContext.TopLevel;

            // Decrement indent BEFORE determining whitespace for }
            bool closingMultiLineStructInit = false;
            if (token.Type == TokenType.RightBrace)
            {
                if (context == FormatterContext.StructInit)
                {
                    if (multiLineBraces.Count > 0)
                    {
                        closingMultiLineStructInit = multiLineBraces.Pop();
                    }
                    if (closingMultiLineStructInit)
                    {
                        indentLevel = Math.Max(0, indentLevel - 1);
                    }
                }
                else
                {
                    indentLevel = Math.Max(0, indentLevel - 1);
                }
                if (contextStack.Count > 1)
                {
                    contextStack.Pop();
                }
            }

            // Decrement indent BEFORE determining whitespace for ] in multi-line arrays
            if (token.Type == TokenType.RightBracket && multiLineBrackets.Count > 0 && multiLineBrackets.Peek())
            {
                indentLevel = Math.Max(0, indentLevel - 1);
            }

            // Determine and apply whitespace
            var ws = GetWhitespace(prevType, token.Type, context, indentLevel, prevWasUnaryMinusPlus, prevClosedInline, ternaryDepth);

            // Keep inline comments on the same line
            if (token.Type == TokenType.SingleLineComment && prevToken != null && prevToken.Span.StartLine == token.Span.StartLine)
            {
                ws = Whitespace.Space;
            }

            // Preserve multi-line array formatting
            if (multiLineBrackets.Count > 0 && multiLineBrackets.Peek())
            {
                if (prevType == TokenType.LeftBracket && token.Type != TokenType.RightBracket)
                {
                    ws = Whitespace.NewLine;
                }
                else if (prevType == TokenType.Comma && context == FormatterContext.Brackets)
                {
                    ws = Whitespace.NewLine;
                }
                else if (token.Type == TokenType.RightBracket)
                {
                    ws = Whitespace.NewLine;
                }
            }

            // Preserve multi-line struct initializer formatting
            bool isMultiLineStructInit = context == FormatterContext.StructInit
                && multiLineBraces.Count > 0
                && multiLineBraces.Peek();
            if (isMultiLineStructInit || closingMultiLineStructInit)
            {
                if (prevType == TokenType.LeftBrace)
                {
                    ws = Whitespace.NewLine;
                }
                else if (prevType == TokenType.Comma)
                {
                    ws = Whitespace.NewLine;
                }
                else if (closingMultiLineStructInit)
                {
                    ws = Whitespace.NewLine;
                }
            }

            switch (ws)
            {
                case Whitespace.BlankLine:
                    sb.AppendLine();
                    sb.AppendLine();
                    AppendIndent(sb, indentLevel);
                    break;
                case Whitespace.NewLine:
                    sb.AppendLine();
                    AppendIndent(sb, indentLevel);
                    break;
                case Whitespace.Space:
                    sb.Append(' ');
                    break;
            }

            // Track unary minus/plus
            if ((token.Type == TokenType.Minus || token.Type == TokenType.Plus)
                && prevType != null && IsUnaryContext(prevType.Value))
            {
                prevWasUnaryMinusPlus = true;
            }
            else if (prevType is TokenType.Minus or TokenType.Plus)
            {
                prevWasUnaryMinusPlus = false;
            }

            // Output token
            if (token.Type == TokenType.BlockComment)
            {
                FormatBlockComment(sb, token.Lexeme, indentLevel);
            }
            else
            {
                sb.Append(token.Lexeme);
            }

            // Handle context changes for { ( [
            if (token.Type == TokenType.LeftBrace)
            {
                if (pendingKeyword == "enum")
                {
                    contextStack.Push(FormatterContext.EnumBody);
                    indentLevel++;
                }
                else if (pendingKeyword == "struct")
                {
                    contextStack.Push(FormatterContext.StructBody);
                    indentLevel++;
                }
                else if (pendingKeyword == "switch")
                {
                    contextStack.Push(FormatterContext.SwitchBody);
                    indentLevel++;
                }
                else if (pendingKeyword != null)
                {
                    // fn, if, else, while, for — always a block
                    contextStack.Push(FormatterContext.Block);
                    indentLevel++;
                }
                else if (prevType == TokenType.Import)
                {
                    contextStack.Push(FormatterContext.StructInit);
                    bool isMultiLine = (i + 1 < tokens.Count
                        && tokens[i + 1].Type != TokenType.Eof
                        && tokens[i + 1].Span.StartLine > token.Span.StartLine)
                        || CountStructInitFields(tokens, i) >= 3;
                    multiLineBraces.Push(isMultiLine);
                    if (isMultiLine)
                    {
                        indentLevel++;
                    }
                }
                else if (prevType == TokenType.Identifier)
                {
                    contextStack.Push(FormatterContext.StructInit);
                    bool isMultiLine = (i + 1 < tokens.Count
                        && tokens[i + 1].Type != TokenType.Eof
                        && tokens[i + 1].Span.StartLine > token.Span.StartLine)
                        || CountStructInitFields(tokens, i) >= 3;
                    multiLineBraces.Push(isMultiLine);
                    if (isMultiLine)
                    {
                        indentLevel++;
                    }
                }
                else
                {
                    contextStack.Push(FormatterContext.Block);
                    indentLevel++;
                }
                pendingKeyword = null;
            }
            else if (token.Type == TokenType.LeftParen)
            {
                contextStack.Push(FormatterContext.Parens);
            }
            else if (token.Type == TokenType.LeftBracket)
            {
                bool isMultiLine = i + 1 < tokens.Count
                    && tokens[i + 1].Type != TokenType.Eof
                    && tokens[i + 1].Span.StartLine > token.Span.StartLine;
                multiLineBrackets.Push(isMultiLine);
                if (isMultiLine)
                {
                    indentLevel++;
                }

                contextStack.Push(FormatterContext.Brackets);
            }
            else if (token.Type is TokenType.RightParen or TokenType.RightBracket)
            {
                if (token.Type == TokenType.RightBracket && multiLineBrackets.Count > 0)
                {
                    multiLineBrackets.Pop();
                }
                if (contextStack.Count > 1)
                {
                    contextStack.Pop();
                }
            }

            // Track pending keyword for context
            if (token.Type is TokenType.Enum)
            {
                pendingKeyword = "enum";
            }
            else if (token.Type is TokenType.Struct)
            {
                pendingKeyword = "struct";
            }
            else if (token.Type is TokenType.Fn)
            {
                pendingKeyword = "fn";
            }
            else if (token.Type is TokenType.If)
            {
                pendingKeyword = "if";
            }
            else if (token.Type is TokenType.Else)
            {
                pendingKeyword = "else";
            }
            else if (token.Type is TokenType.While)
            {
                pendingKeyword = "while";
            }
            else if (token.Type is TokenType.Do)
            {
                pendingKeyword = "do";
            }
            else if (token.Type is TokenType.For)
            {
                pendingKeyword = "for";
            }
            else if (token.Type is TokenType.Switch)
            {
                pendingKeyword = "switch";
            }
            else if (token.Type is TokenType.Semicolon)
            {
                pendingKeyword = null;
            }

            // Track ternary ? for colon spacing
            if (token.Type == TokenType.QuestionMark)
            {
                ternaryDepth++;
            }
            else if (token.Type == TokenType.Colon && ternaryDepth > 0)
            {
                ternaryDepth--;
            }
            else if (token.Type is TokenType.Semicolon or TokenType.LeftBrace or TokenType.RightBrace)
            {
                ternaryDepth = 0;
            }

            prevClosedInline = token.Type == TokenType.RightBrace && context == FormatterContext.StructInit;
            prevType = token.Type;
            prevToken = token;
        }

        var result = sb.ToString().TrimEnd();
        return result.Length > 0 ? result + "\n" : "";
    }

    /// <summary>
    /// Applies the formatting rules (numbered 1–37) to determine what whitespace, if any,
    /// should be inserted between the previous token type <paramref name="prev"/> and the
    /// current token type <paramref name="cur"/> given the formatting context.
    /// </summary>
    /// <param name="prev">The token type that was just emitted, or <see langword="null"/> at the start of file.</param>
    /// <param name="cur">The token type about to be emitted.</param>
    /// <param name="context">The current syntactic context (block, enum body, struct init, …).</param>
    /// <param name="indentLevel">The current nesting level, used for blank-line decisions.</param>
    /// <param name="prevWasUnaryMinusPlus"><see langword="true"/> if the previous <c>-</c> or <c>+</c> was in a unary position.</param>
    /// <param name="prevClosedInline"><see langword="true"/> if the previous <c>}</c> closed a struct initializer inline.</param>
    /// <param name="ternaryDepth">Number of open ternary <c>?</c> operators not yet matched by a <c>:</c>.</param>
    /// <returns>The <see cref="Whitespace"/> to insert before the current token.</returns>
    private Whitespace GetWhitespace(TokenType? prev, TokenType cur, FormatterContext context, int indentLevel, bool prevWasUnaryMinusPlus, bool prevClosedInline, int ternaryDepth)
    {
        // Rule 1: No previous token
        if (prev is null)
        {
            return Whitespace.None;
        }

        // Rule 2: prev == Shebang
        if (prev == TokenType.Shebang)
        {
            return Whitespace.BlankLine;
        }

        // Rule 3: prev == SingleLineComment && cur == SingleLineComment
        if (prev == TokenType.SingleLineComment && cur == TokenType.SingleLineComment)
        {
            return Whitespace.NewLine;
        }

        // Rule 4: prev == SingleLineComment
        if (prev == TokenType.SingleLineComment)
        {
            return indentLevel == 0 ? Whitespace.BlankLine : Whitespace.NewLine;
        }

        // Rule 5: prev == BlockComment
        if (prev == TokenType.BlockComment)
        {
            return Whitespace.NewLine;
        }

        // Rule 6: prev == RightBrace && cur == Else
        if (prev == TokenType.RightBrace && cur == TokenType.Else)
        {
            return Whitespace.Space;
        }

        // Rule 6.5: cur == Semicolon — never whitespace before semicolons
        if (cur == TokenType.Semicolon)
        {
            return Whitespace.None;
        }

        // Rule 7: prev == LeftBrace && cur == RightBrace (empty block/struct init)
        if (prev == TokenType.LeftBrace && cur == TokenType.RightBrace)
        {
            return context == FormatterContext.StructInit ? Whitespace.None : Whitespace.NewLine;
        }

        // Rule 8: prev == LeftBrace
        if (prev == TokenType.LeftBrace)
        {
            return context == FormatterContext.StructInit ? Whitespace.Space : Whitespace.NewLine;
        }

        // Rule 9: cur == RightBrace
        if (cur == TokenType.RightBrace)
        {
            return context == FormatterContext.StructInit ? Whitespace.Space : Whitespace.NewLine;
        }

        // Rule 9.5: prev == RightBrace && cur is RightParen/RightBracket — callback-style closings
        if (prev == TokenType.RightBrace && cur is TokenType.RightParen or TokenType.RightBracket)
        {
            return Whitespace.None;
        }

        // Rule 10: prev == RightBrace && indentLevel == 0
        if (prev == TokenType.RightBrace && indentLevel == 0 && !prevClosedInline)
        {
            return Whitespace.BlankLine;
        }

        // Rule 11: prev == RightBrace && cur == SingleLineComment
        if (prev == TokenType.RightBrace && cur == TokenType.SingleLineComment && !prevClosedInline)
        {
            return Whitespace.BlankLine;
        }

        // Rule 12: prev == RightBrace
        if (prev == TokenType.RightBrace && !prevClosedInline)
        {
            return Whitespace.NewLine;
        }

        // Rule 13: prev == Semicolon && indentLevel == 0 && IsTopLevelStart(cur)
        if (prev == TokenType.Semicolon && indentLevel == 0 && IsTopLevelStart(cur))
        {
            return Whitespace.BlankLine;
        }

        // Rule 14: prev == Semicolon && cur == SingleLineComment
        if (prev == TokenType.Semicolon && cur == TokenType.SingleLineComment)
        {
            return Whitespace.BlankLine;
        }

        // Rule 15: prev == Semicolon
        if (prev == TokenType.Semicolon)
        {
            return Whitespace.NewLine;
        }

        // Rule 16: prev == Comma && context is EnumBody, StructBody, or SwitchBody
        if (prev == TokenType.Comma && context is FormatterContext.EnumBody or FormatterContext.StructBody or FormatterContext.SwitchBody)
        {
            return Whitespace.NewLine;
        }

        // Rule 17: prev == Comma
        if (prev == TokenType.Comma)
        {
            return Whitespace.Space;
        }

        // Rule 18: cur is Semicolon, Comma, RightParen, RightBracket
        if (cur is TokenType.Semicolon or TokenType.Comma or TokenType.RightParen or TokenType.RightBracket)
        {
            return Whitespace.None;
        }

        // Rule 19: cur == Dot/QuestionDot || prev == Dot/QuestionDot
        if (cur == TokenType.Dot || prev == TokenType.Dot ||
            cur == TokenType.QuestionDot || prev == TokenType.QuestionDot)
        {
            return Whitespace.None;
        }

        // Rule 20: cur == Colon — no space before colon (type annotations), but space for ternary
        if (cur == TokenType.Colon)
        {
            return ternaryDepth > 0 ? Whitespace.Space : Whitespace.None;
        }

        // Rule 21: prev == Colon
        if (prev == TokenType.Colon)
        {
            return Whitespace.Space;
        }

        // Rule 22: prev == LeftParen || prev == LeftBracket
        if (prev is TokenType.LeftParen or TokenType.LeftBracket)
        {
            return Whitespace.None;
        }

        // Rule 23: cur == LeftParen && prev == Identifier
        if (cur == TokenType.LeftParen && prev == TokenType.Identifier)
        {
            return Whitespace.None;
        }

        // Rule 24: cur == LeftParen && IsControlKeyword(prev)
        if (cur == TokenType.LeftParen && IsControlKeyword(prev.Value))
        {
            return Whitespace.Space;
        }

        // Rule 25: cur == LeftBracket && prev == Identifier
        if (cur == TokenType.LeftBracket && prev == TokenType.Identifier)
        {
            return Whitespace.None;
        }

        // Rule 26: prev == Bang
        if (prev == TokenType.Bang)
        {
            return Whitespace.None;
        }

        // Rule 27: Postfix ++/--: cur is PlusPlus/MinusMinus AND prev is Identifier/RightParen/RightBracket
        if (cur is TokenType.PlusPlus or TokenType.MinusMinus
            && prev is TokenType.Identifier or TokenType.RightParen or TokenType.RightBracket)
        {
            return Whitespace.None;
        }

        // Rule 28: Prefix ++/--: prev is PlusPlus/MinusMinus AND cur is Identifier/LeftParen
        if (prev is TokenType.PlusPlus or TokenType.MinusMinus
            && cur is TokenType.Identifier or TokenType.LeftParen)
        {
            return Whitespace.None;
        }

        // Rule 29: Unary minus/plus context — cur is Minus or Plus and prev is in unary context
        if ((cur == TokenType.Minus || cur == TokenType.Plus) && IsUnaryContext(prev.Value))
        {
            return Whitespace.Space;
        }

        // Rule 30: After unary minus/plus, no space before operand
        if ((prev is TokenType.Minus or TokenType.Plus) && prevWasUnaryMinusPlus)
        {
            return Whitespace.None;
        }

        // Rule 31: IsBinaryOp(cur) or IsBinaryOp(prev)
        if (IsBinaryOp(cur) || IsBinaryOp(prev.Value))
        {
            return Whitespace.Space;
        }

        // Rule 32: cur == Equal || prev == Equal
        if (cur == TokenType.Equal || prev == TokenType.Equal)
        {
            return Whitespace.Space;
        }

        // Rule 33: cur or prev is Arrow/FatArrow/QuestionQuestion
        if (cur is TokenType.Arrow or TokenType.FatArrow or TokenType.QuestionQuestion
            || prev is TokenType.Arrow or TokenType.FatArrow or TokenType.QuestionQuestion)
        {
            return Whitespace.Space;
        }

        // Rule 34: cur == QuestionMark || prev == QuestionMark
        if (cur == TokenType.QuestionMark || prev == TokenType.QuestionMark)
        {
            return Whitespace.Space;
        }

        // Rule 35: IsKeyword(prev)
        if (IsKeyword(prev.Value))
        {
            return Whitespace.Space;
        }

        // Rule 36: cur == LeftBrace
        if (cur == TokenType.LeftBrace)
        {
            return Whitespace.Space;
        }

        // Rule 37: Default
        return Whitespace.Space;
    }

    /// <summary>
    /// Appends a multi-line block comment to <paramref name="sb"/>, re-indenting each line
    /// after the first to the current <paramref name="indentLevel"/> and stripping leading
    /// whitespace from continuation lines.
    /// </summary>
    /// <param name="sb">The output string builder.</param>
    /// <param name="lexeme">The raw block-comment lexeme including <c>/*</c> and <c>*/</c> delimiters.</param>
    /// <param name="indentLevel">The current nesting level used for indenting continuation lines.</param>
    private void FormatBlockComment(StringBuilder sb, string lexeme, int indentLevel)
    {
        var lines = lexeme.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
                AppendIndent(sb, indentLevel);
                sb.Append(lines[i].TrimStart());
            }
            else
            {
                sb.Append(lines[i].TrimEnd());
            }
        }
    }

    private static int CountStructInitFields(List<Token> tokens, int openBraceIndex)
    {
        int depth = 0;
        int commas = 0;
        for (int j = openBraceIndex + 1; j < tokens.Count; j++)
        {
            var t = tokens[j];
            if (t.Type is TokenType.LeftBrace or TokenType.LeftParen or TokenType.LeftBracket)
            {
                depth++;
            }
            else if (t.Type is TokenType.RightBrace or TokenType.RightParen or TokenType.RightBracket)
            {
                if (depth == 0)
                {
                    break;
                }

                depth--;
            }
            else if (t.Type == TokenType.Comma && depth == 0)
            {
                commas++;
            }
        }
        return commas + 1;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="t"/> begins a top-level declaration or
    /// statement, used to insert a blank line after a top-level semicolon.
    /// </summary>
    private static bool IsTopLevelStart(TokenType t) => t is
        TokenType.Fn or TokenType.Struct or TokenType.Enum or TokenType.Const or TokenType.Let or
        TokenType.Import or TokenType.For or TokenType.While or TokenType.Do or TokenType.If or
        TokenType.SingleLineComment;

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="t"/> is a control-flow keyword
    /// that takes a parenthesised condition, used to insert a space before the opening
    /// parenthesis (e.g. <c>if (</c>, <c>while (</c>).
    /// </summary>
    private static bool IsControlKeyword(TokenType t) => t is
        TokenType.If or TokenType.While or TokenType.Do or TokenType.For;

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="t"/> is a keyword that must be
    /// followed by a space before the next token (e.g. <c>let</c>, <c>return</c>, <c>import</c>).
    /// </summary>
    private static bool IsKeyword(TokenType t) => t is
        TokenType.Let or TokenType.Const or TokenType.Fn or TokenType.Struct or TokenType.Enum or
        TokenType.If or TokenType.Else or TokenType.For or TokenType.In or TokenType.While or TokenType.Do or
        TokenType.Return or TokenType.Break or TokenType.Continue or TokenType.True or
        TokenType.False or TokenType.Null or TokenType.Try or TokenType.Import or
        TokenType.From or TokenType.As;

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="t"/> is a binary operator that
    /// requires spaces on both sides (arithmetic, comparison, logical, pipe operators).
    /// </summary>
    private static bool IsBinaryOp(TokenType t) => t is
        TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or TokenType.Percent or
        TokenType.EqualEqual or TokenType.BangEqual or TokenType.Less or TokenType.Greater or
        TokenType.LessEqual or TokenType.GreaterEqual or TokenType.AmpersandAmpersand or
        TokenType.PipePipe or TokenType.Pipe or TokenType.GreaterGreater or
        TokenType.AmpersandGreater or TokenType.AmpersandGreaterGreater;

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="t"/> ends a value expression,
    /// used to distinguish postfix <c>++</c>/<c>--</c> (no space) from prefix (no space on right).
    /// </summary>
    private static bool IsValueEnd(TokenType t) => t is
        TokenType.Identifier or TokenType.RightParen or TokenType.RightBracket or
        TokenType.IntegerLiteral or TokenType.FloatLiteral or TokenType.StringLiteral or
        TokenType.InterpolatedString or TokenType.CommandLiteral or TokenType.PassthroughCommandLiteral or
        TokenType.True or TokenType.False or TokenType.Null or TokenType.PlusPlus or TokenType.MinusMinus;

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="t"/> is a token type that places a
    /// following <c>-</c> or <c>+</c> in a unary (prefix) position rather than a binary position,
    /// suppressing the space between the operator and its operand.
    /// </summary>
    private static bool IsUnaryContext(TokenType t) => t is
        TokenType.LeftParen or TokenType.LeftBracket or TokenType.Equal or
        TokenType.Comma or TokenType.Semicolon or TokenType.Colon or
        TokenType.FatArrow or TokenType.Arrow or TokenType.Return or
        TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or TokenType.Percent or
        TokenType.EqualEqual or TokenType.BangEqual or TokenType.Less or TokenType.Greater or
        TokenType.LessEqual or TokenType.GreaterEqual or TokenType.AmpersandAmpersand or
        TokenType.PipePipe or TokenType.QuestionQuestion or TokenType.Pipe or
        TokenType.GreaterGreater or TokenType.AmpersandGreater or TokenType.AmpersandGreaterGreater or
        TokenType.Bang or TokenType.QuestionMark or TokenType.LeftBrace;
}
