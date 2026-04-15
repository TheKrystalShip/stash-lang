using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

namespace Stash.Analysis.Formatting;

/// <summary>
/// Encapsulates the emission state and primitives used during formatting.
/// Created per format call; statement and expression formatters operate on this context.
/// </summary>
public sealed class FormatContext
{
    internal readonly int IndentSize;
    internal readonly bool UseTabs;
    internal readonly TrailingCommaStyle TrailingComma;
    internal readonly bool BracketSpacing;
    internal readonly int BlankLinesBetweenBlocks;
    internal readonly bool SingleLineBlocks;
    internal readonly FormatConfig Config;

    internal List<Doc> Docs = new();
    internal int Indent;
    internal Token[] CodeTokens = Array.Empty<Token>();
    internal int Cursor;
    internal Token? LastCodeToken;
    internal PendingWs Pending;

    internal readonly TriviaHandler Trivia;

    private readonly Stack<ScopeKind> _scopes = new();

    internal enum PendingWs { None, Space, SoftLine, NewLine, BlankLine }

    public FormatContext(FormatConfig? config = null)
    {
        var cfg = config ?? FormatConfig.Default;
        Config = cfg;
        IndentSize = cfg.IndentSize;
        UseTabs = cfg.UseTabs;
        TrailingComma = cfg.TrailingComma;
        BracketSpacing = cfg.BracketSpacing;
        BlankLinesBetweenBlocks = cfg.BlankLinesBetweenBlocks;
        SingleLineBlocks = cfg.SingleLineBlocks;
        Trivia = new TriviaHandler(this);
    }

    internal ScopeKind CurrentScope =>
        _scopes.Count > 0 ? _scopes.Peek() : ScopeKind.TopLevel;

    internal void PushScope(ScopeKind kind) => _scopes.Push(kind);

    internal void PopScope() => _scopes.Pop();

    internal int BlankLinesBetween(Stmt prev, Stmt current) =>
        Rules.SpacingRules.BlankLinesBetween(prev, current, CurrentScope, Config);

    internal void Reset(Token[] codeTokens, Token[] triviaTokens)
    {
        Docs = new List<Doc>();
        Indent = 0;
        CodeTokens = codeTokens;
        Cursor = 0;
        LastCodeToken = null;
        Pending = PendingWs.None;
        _scopes.Clear();
        Trivia.Reset(triviaTokens);
    }

    public void Space()
    {
        if (Pending < PendingWs.Space)
            Pending = PendingWs.Space;
    }

    public void SoftNewLine()
    {
        if (Pending < PendingWs.SoftLine)
            Pending = PendingWs.SoftLine;
    }

    public void NewLine()
    {
        if (Pending < PendingWs.NewLine)
            Pending = PendingWs.NewLine;
    }

    public void BlankLine()
    {
        Pending = PendingWs.BlankLine;
    }

    internal string IndentString()
    {
        char ch = UseTabs ? '\t' : ' ';
        int count = UseTabs ? Indent : Indent * IndentSize;
        return count > 0 ? new string(ch, count) : "";
    }

    internal void WritePending()
    {
        switch (Pending)
        {
            case PendingWs.BlankLine:
                for (int bl = 0; bl <= BlankLinesBetweenBlocks; bl++)
                    Docs.Add(Doc.HardLine);
                break;
            case PendingWs.NewLine:
                Docs.Add(Doc.HardLine);
                break;
            case PendingWs.SoftLine:
                Docs.Add(Doc.Line);
                break;
            case PendingWs.Space:
                Docs.Add(Doc.Text(" "));
                break;
        }
        Pending = PendingWs.None;
    }

    public bool NextIs(TokenType t) =>
        Cursor < CodeTokens.Length && CodeTokens[Cursor].Type == t;

    public void EmitToken()
    {
        var token = CodeTokens[Cursor];
        Trivia.FlushTriviaBefore(token);
        WritePending();
        Docs.Add(Doc.Text(token.Lexeme));
        LastCodeToken = token;
        Cursor++;
    }

    public int Mark() => Docs.Count;

    public void WrapFrom(int mark, Func<Doc, Doc> wrapper)
    {
        int count = Docs.Count - mark;
        if (count == 0) return;
        var slice = new Doc[count];
        for (int i = 0; i < count; i++)
            slice[i] = Docs[mark + i];
        Docs.RemoveRange(mark, count);
        Docs.Add(wrapper(Doc.Concat(slice)));
    }

    public void AddDoc(Doc doc) => Docs.Add(doc);

    public bool HasMoreTokens => Cursor < CodeTokens.Length;
    public Token CurrentToken => CodeTokens[Cursor];
    public void SkipToken() => Cursor++;

    public bool HasPendingWhitespace => Pending != PendingWs.None;
    public void ResetPending() => Pending = PendingWs.None;

    public bool NextIsCompoundOperator() =>
        Cursor < CodeTokens.Length && IsCompoundOperator(CodeTokens[Cursor].Type);

    public static bool IsCompoundOperator(TokenType t) => t is
        TokenType.PlusEqual or TokenType.MinusEqual or TokenType.StarEqual or
        TokenType.SlashEqual or TokenType.PercentEqual or TokenType.QuestionQuestionEqual or
        TokenType.AmpersandEqual or TokenType.PipeEqual or TokenType.CaretEqual or
        TokenType.LessLessEqual or TokenType.GreaterGreaterEqual;

    public void FlushTriviaBeforeCurrentToken()
    {
        if (HasMoreTokens) Trivia.FlushTriviaBefore(CurrentToken);
    }

    public void FlushRemainingTrivia() => Trivia.FlushRemainingTrivia();

    internal bool BlockHasComments(int startLine, int endLine)
    {
        for (int i = Trivia.CursorPosition; i < Trivia.Tokens.Length; i++)
        {
            Token t = Trivia.Tokens[i];
            if (t.Span.StartLine > endLine) break;
            if (t.Span.StartLine >= startLine && t.Span.StartLine <= endLine)
                return true;
        }
        return false;
    }
}
