using System;
using System.Collections.Generic;
using System.Text;
using Stash.Lexing;

namespace Stash.Lsp.Analysis;

public class StashFormatter
{
    private readonly int _indentSize;
    private readonly bool _useTabs;

    public StashFormatter(int indentSize = 2, bool useTabs = false)
    {
        _indentSize = indentSize;
        _useTabs = useTabs;
    }

    private void AppendIndent(StringBuilder sb, int level)
    {
        var indentChar = _useTabs ? '\t' : ' ';
        var count = _useTabs ? level : level * _indentSize;
        sb.Append(new string(indentChar, count));
    }

    private enum FormatterContext { TopLevel, EnumBody, StructBody, Block, Parens, Brackets, StructInit }

    private enum Whitespace { None, Space, NewLine, BlankLine }

    public string Format(string source)
    {
        var lexer = new Lexer(source, "<format>", preserveTrivia: true);
        var tokens = lexer.ScanTokens();

        var sb = new StringBuilder();
        int indentLevel = 0;
        var contextStack = new Stack<FormatterContext>();
        contextStack.Push(FormatterContext.TopLevel);
        TokenType? prevType = null;
        bool prevWasUnaryMinusPlus = false;
        string? pendingKeyword = null;
        bool prevClosedInline = false;
        int ternaryDepth = 0;

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type == TokenType.Eof) break;

            var context = contextStack.Count > 0 ? contextStack.Peek() : FormatterContext.TopLevel;

            // Decrement indent BEFORE determining whitespace for }
            if (token.Type == TokenType.RightBrace)
            {
                if (context != FormatterContext.StructInit)
                {
                    indentLevel = Math.Max(0, indentLevel - 1);
                }
                if (contextStack.Count > 1) contextStack.Pop();
            }

            // Determine and apply whitespace
            var ws = GetWhitespace(prevType, token.Type, context, indentLevel, prevWasUnaryMinusPlus, prevClosedInline, ternaryDepth);

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
                else if (pendingKeyword != null)
                {
                    // fn, if, else, while, for — always a block
                    contextStack.Push(FormatterContext.Block);
                    indentLevel++;
                }
                else if (prevType == TokenType.Import)
                {
                    contextStack.Push(FormatterContext.StructInit);
                }
                else if (prevType == TokenType.Identifier)
                {
                    contextStack.Push(FormatterContext.StructInit);
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
                contextStack.Push(FormatterContext.Brackets);
            }
            else if (token.Type is TokenType.RightParen or TokenType.RightBracket)
            {
                if (contextStack.Count > 1) contextStack.Pop();
            }

            // Track pending keyword for context
            if (token.Type is TokenType.Enum) pendingKeyword = "enum";
            else if (token.Type is TokenType.Struct) pendingKeyword = "struct";
            else if (token.Type is TokenType.Fn) pendingKeyword = "fn";
            else if (token.Type is TokenType.If) pendingKeyword = "if";
            else if (token.Type is TokenType.Else) pendingKeyword = "else";
            else if (token.Type is TokenType.While) pendingKeyword = "while";
            else if (token.Type is TokenType.For) pendingKeyword = "for";
            else if (token.Type is TokenType.Semicolon) pendingKeyword = null;

            // Track ternary ? for colon spacing
            if (token.Type == TokenType.QuestionMark)
                ternaryDepth++;
            else if (token.Type == TokenType.Colon && ternaryDepth > 0)
                ternaryDepth--;
            else if (token.Type is TokenType.Semicolon or TokenType.LeftBrace or TokenType.RightBrace)
                ternaryDepth = 0;

            prevClosedInline = token.Type == TokenType.RightBrace && context == FormatterContext.StructInit;
            prevType = token.Type;
        }

        var result = sb.ToString().TrimEnd();
        return result.Length > 0 ? result + "\n" : "";
    }

    private Whitespace GetWhitespace(TokenType? prev, TokenType cur, FormatterContext context, int indentLevel, bool prevWasUnaryMinusPlus, bool prevClosedInline, int ternaryDepth)
    {
        // Rule 1: No previous token
        if (prev is null)
            return Whitespace.None;

        // Rule 2: prev == Shebang
        if (prev == TokenType.Shebang)
            return Whitespace.BlankLine;

        // Rule 3: prev == SingleLineComment && cur == SingleLineComment
        if (prev == TokenType.SingleLineComment && cur == TokenType.SingleLineComment)
            return Whitespace.NewLine;

        // Rule 4: prev == SingleLineComment
        if (prev == TokenType.SingleLineComment)
            return indentLevel == 0 ? Whitespace.BlankLine : Whitespace.NewLine;

        // Rule 5: prev == BlockComment
        if (prev == TokenType.BlockComment)
            return Whitespace.NewLine;

        // Rule 6: prev == RightBrace && cur == Else
        if (prev == TokenType.RightBrace && cur == TokenType.Else)
            return Whitespace.Space;

        // Rule 6.5: cur == Semicolon — never whitespace before semicolons
        if (cur == TokenType.Semicolon)
            return Whitespace.None;

        // Rule 7: prev == LeftBrace && cur == RightBrace (empty block/struct init)
        if (prev == TokenType.LeftBrace && cur == TokenType.RightBrace)
            return context == FormatterContext.StructInit ? Whitespace.None : Whitespace.NewLine;

        // Rule 8: prev == LeftBrace
        if (prev == TokenType.LeftBrace)
            return context == FormatterContext.StructInit ? Whitespace.None : Whitespace.NewLine;

        // Rule 9: cur == RightBrace
        if (cur == TokenType.RightBrace)
            return context == FormatterContext.StructInit ? Whitespace.None : Whitespace.NewLine;

        // Rule 10: prev == RightBrace && indentLevel == 0
        if (prev == TokenType.RightBrace && indentLevel == 0 && !prevClosedInline)
            return Whitespace.BlankLine;

        // Rule 11: prev == RightBrace && cur == SingleLineComment
        if (prev == TokenType.RightBrace && cur == TokenType.SingleLineComment && !prevClosedInline)
            return Whitespace.BlankLine;

        // Rule 12: prev == RightBrace
        if (prev == TokenType.RightBrace && !prevClosedInline)
            return Whitespace.NewLine;

        // Rule 13: prev == Semicolon && indentLevel == 0 && IsTopLevelStart(cur)
        if (prev == TokenType.Semicolon && indentLevel == 0 && IsTopLevelStart(cur))
            return Whitespace.BlankLine;

        // Rule 14: prev == Semicolon && cur == SingleLineComment
        if (prev == TokenType.Semicolon && cur == TokenType.SingleLineComment)
            return Whitespace.BlankLine;

        // Rule 15: prev == Semicolon
        if (prev == TokenType.Semicolon)
            return Whitespace.NewLine;

        // Rule 16: prev == Comma && context is EnumBody or StructBody
        if (prev == TokenType.Comma && context is FormatterContext.EnumBody or FormatterContext.StructBody)
            return Whitespace.NewLine;

        // Rule 17: prev == Comma
        if (prev == TokenType.Comma)
            return Whitespace.Space;

        // Rule 18: cur is Semicolon, Comma, RightParen, RightBracket
        if (cur is TokenType.Semicolon or TokenType.Comma or TokenType.RightParen or TokenType.RightBracket)
            return Whitespace.None;

        // Rule 19: cur == Dot || prev == Dot
        if (cur == TokenType.Dot || prev == TokenType.Dot)
            return Whitespace.None;

        // Rule 20: cur == Colon — no space before colon (type annotations), but space for ternary
        if (cur == TokenType.Colon)
            return ternaryDepth > 0 ? Whitespace.Space : Whitespace.None;

        // Rule 21: prev == Colon
        if (prev == TokenType.Colon)
            return Whitespace.Space;

        // Rule 22: prev == LeftParen || prev == LeftBracket
        if (prev is TokenType.LeftParen or TokenType.LeftBracket)
            return Whitespace.None;

        // Rule 23: cur == LeftParen && prev == Identifier
        if (cur == TokenType.LeftParen && prev == TokenType.Identifier)
            return Whitespace.None;

        // Rule 24: cur == LeftParen && IsControlKeyword(prev)
        if (cur == TokenType.LeftParen && IsControlKeyword(prev.Value))
            return Whitespace.Space;

        // Rule 25: cur == LeftBracket && prev == Identifier
        if (cur == TokenType.LeftBracket && prev == TokenType.Identifier)
            return Whitespace.None;

        // Rule 26: prev == Bang
        if (prev == TokenType.Bang)
            return Whitespace.None;

        // Rule 27: Postfix ++/--: cur is PlusPlus/MinusMinus AND prev is Identifier/RightParen/RightBracket
        if (cur is TokenType.PlusPlus or TokenType.MinusMinus
            && prev is TokenType.Identifier or TokenType.RightParen or TokenType.RightBracket)
            return Whitespace.None;

        // Rule 28: Prefix ++/--: prev is PlusPlus/MinusMinus AND cur is Identifier/LeftParen
        if (prev is TokenType.PlusPlus or TokenType.MinusMinus
            && cur is TokenType.Identifier or TokenType.LeftParen)
            return Whitespace.None;

        // Rule 29: Unary minus/plus context — cur is Minus or Plus and prev is in unary context
        if ((cur == TokenType.Minus || cur == TokenType.Plus) && IsUnaryContext(prev.Value))
            return Whitespace.Space;

        // Rule 30: After unary minus/plus, no space before operand
        if ((prev is TokenType.Minus or TokenType.Plus) && prevWasUnaryMinusPlus)
            return Whitespace.None;

        // Rule 31: IsBinaryOp(cur) or IsBinaryOp(prev)
        if (IsBinaryOp(cur) || IsBinaryOp(prev.Value))
            return Whitespace.Space;

        // Rule 32: cur == Equal || prev == Equal
        if (cur == TokenType.Equal || prev == TokenType.Equal)
            return Whitespace.Space;

        // Rule 33: cur or prev is Arrow/FatArrow/QuestionQuestion
        if (cur is TokenType.Arrow or TokenType.FatArrow or TokenType.QuestionQuestion
            || prev is TokenType.Arrow or TokenType.FatArrow or TokenType.QuestionQuestion)
            return Whitespace.Space;

        // Rule 34: cur == QuestionMark || prev == QuestionMark
        if (cur == TokenType.QuestionMark || prev == TokenType.QuestionMark)
            return Whitespace.Space;

        // Rule 35: IsKeyword(prev)
        if (IsKeyword(prev.Value))
            return Whitespace.Space;

        // Rule 36: cur == LeftBrace
        if (cur == TokenType.LeftBrace)
            return Whitespace.Space;

        // Rule 37: Default
        return Whitespace.Space;
    }

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

    private static bool IsTopLevelStart(TokenType t) => t is
        TokenType.Fn or TokenType.Struct or TokenType.Enum or TokenType.Const or TokenType.Let or
        TokenType.Import or TokenType.For or TokenType.While or TokenType.If or
        TokenType.SingleLineComment;

    private static bool IsControlKeyword(TokenType t) => t is
        TokenType.If or TokenType.While or TokenType.For;

    private static bool IsKeyword(TokenType t) => t is
        TokenType.Let or TokenType.Const or TokenType.Fn or TokenType.Struct or TokenType.Enum or
        TokenType.If or TokenType.Else or TokenType.For or TokenType.In or TokenType.While or
        TokenType.Return or TokenType.Break or TokenType.Continue or TokenType.True or
        TokenType.False or TokenType.Null or TokenType.Try or TokenType.Import or
        TokenType.From or TokenType.As;

    private static bool IsBinaryOp(TokenType t) => t is
        TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or TokenType.Percent or
        TokenType.EqualEqual or TokenType.BangEqual or TokenType.Less or TokenType.Greater or
        TokenType.LessEqual or TokenType.GreaterEqual or TokenType.AmpersandAmpersand or
        TokenType.PipePipe or TokenType.Pipe;

    private static bool IsValueEnd(TokenType t) => t is
        TokenType.Identifier or TokenType.RightParen or TokenType.RightBracket or
        TokenType.IntegerLiteral or TokenType.FloatLiteral or TokenType.StringLiteral or
        TokenType.InterpolatedString or TokenType.CommandLiteral or
        TokenType.True or TokenType.False or TokenType.Null or TokenType.PlusPlus or TokenType.MinusMinus;

    private static bool IsUnaryContext(TokenType t) => t is
        TokenType.LeftParen or TokenType.LeftBracket or TokenType.Equal or
        TokenType.Comma or TokenType.Semicolon or TokenType.Colon or
        TokenType.FatArrow or TokenType.Arrow or TokenType.Return or
        TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or TokenType.Percent or
        TokenType.EqualEqual or TokenType.BangEqual or TokenType.Less or TokenType.Greater or
        TokenType.LessEqual or TokenType.GreaterEqual or TokenType.AmpersandAmpersand or
        TokenType.PipePipe or TokenType.QuestionQuestion or TokenType.Pipe or
        TokenType.Bang or TokenType.QuestionMark or TokenType.LeftBrace;
}
