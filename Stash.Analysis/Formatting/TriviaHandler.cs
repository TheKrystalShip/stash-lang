using System;
using Stash.Lexing;

namespace Stash.Analysis.Formatting;

/// <summary>
/// Handles interleaving of trivia (comments, shebang) at appropriate positions
/// during formatting. Owned by <see cref="FormatterContext"/>.
/// </summary>
internal sealed class TriviaHandler
{
    private readonly FormatterContext _ctx;
    internal Token[] Tokens = Array.Empty<Token>();
    internal int CursorPosition;

    internal TriviaHandler(FormatterContext ctx)
    {
        _ctx = ctx;
    }

    internal void Reset(Token[] triviaTokens)
    {
        Tokens = triviaTokens;
        CursorPosition = 0;
    }

    internal void FlushTriviaBefore(Token upTo)
    {
        while (CursorPosition < Tokens.Length)
        {
            var trivia = Tokens[CursorPosition];
            if (trivia.Span.StartLine > upTo.Span.StartLine
                || (trivia.Span.StartLine == upTo.Span.StartLine
                    && trivia.Span.StartColumn >= upTo.Span.StartColumn))
            {
                break;
            }

            CursorPosition++;
            ProcessTrivia(trivia, upTo);
        }
    }

    private void ProcessTrivia(Token trivia, Token? upTo)
    {
        if (trivia.Type == TokenType.Shebang)
        {
            _ctx.Docs.Add(Doc.Text(trivia.Lexeme));
            _ctx.Pending = FormatterContext.PendingWs.BlankLine;
            return;
        }

        bool isInline = _ctx.LastCodeToken != null
            && trivia.Span.StartLine == _ctx.LastCodeToken.Span.EndLine;

        if (isInline)
        {
            _ctx.Docs.Add(Doc.Text(" "));
            _ctx.Docs.Add(Doc.Text(trivia.Lexeme));
            if (_ctx.Indent == 0)
            {
                _ctx.Pending = FormatterContext.PendingWs.BlankLine;
            }
            else if (_ctx.Pending < FormatterContext.PendingWs.NewLine)
            {
                _ctx.Pending = FormatterContext.PendingWs.NewLine;
            }
        }
        else
        {
            _ctx.WritePending();
            if (trivia.Type == TokenType.BlockComment)
            {
                FormatBlockComment(trivia.Lexeme);
            }
            else
            {
                _ctx.Docs.Add(Doc.Text(trivia.Lexeme));
            }

            bool nextFollowsImmediately = false;
            if (CursorPosition < Tokens.Length)
            {
                var next = Tokens[CursorPosition];
                bool nextBeforeUpTo = upTo == null
                    || next.Span.StartLine < upTo.Span.StartLine
                    || (next.Span.StartLine == upTo.Span.StartLine
                        && next.Span.StartColumn < upTo.Span.StartColumn);
                if (nextBeforeUpTo && next.Span.StartLine == trivia.Span.StartLine + 1)
                {
                    nextFollowsImmediately = true;
                }
            }

            if (nextFollowsImmediately)
            {
                if (_ctx.Pending < FormatterContext.PendingWs.NewLine)
                    _ctx.Pending = FormatterContext.PendingWs.NewLine;
            }
            else if (trivia.Type == TokenType.BlockComment)
            {
                if (_ctx.Pending < FormatterContext.PendingWs.NewLine)
                    _ctx.Pending = FormatterContext.PendingWs.NewLine;
            }
            else
            {
                if (_ctx.Indent == 0)
                {
                    _ctx.Pending = FormatterContext.PendingWs.BlankLine;
                }
                else if (_ctx.Pending < FormatterContext.PendingWs.NewLine)
                {
                    _ctx.Pending = FormatterContext.PendingWs.NewLine;
                }
            }
        }
    }

    internal void FlushRemainingTrivia()
    {
        while (CursorPosition < Tokens.Length)
        {
            ProcessTrivia(Tokens[CursorPosition++], null);
        }
    }

    private void FormatBlockComment(string lexeme)
    {
        var lines = lexeme.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                _ctx.Docs.Add(Doc.HardLine);
                _ctx.Docs.Add(Doc.Text(lines[i].TrimStart()));
            }
            else
            {
                _ctx.Docs.Add(Doc.Text(lines[i].TrimEnd()));
            }
        }
    }
}
