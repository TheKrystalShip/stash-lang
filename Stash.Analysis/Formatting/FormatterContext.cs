using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

namespace Stash.Analysis.Formatting;

/// <summary>
/// Encapsulates the emission state and primitives used during formatting.
/// Created per format call; statement and expression formatters operate on this context.
/// </summary>
public sealed class FormatterContext
{
    // ── Configuration (read-only) ─────────────────────────────────
    internal readonly int IndentSize;
    internal readonly bool UseTabs;
    internal readonly TrailingCommaStyle TrailingComma;
    internal readonly bool BracketSpacing;
    internal readonly int BlankLinesBetweenBlocks;
    internal readonly bool SingleLineBlocks;

    // ── Per-call mutable state ────────────────────────────────────
    internal List<Doc> Docs = new();
    internal int Indent;
    internal Token[] CodeTokens = Array.Empty<Token>();
    internal int Cursor;
    internal Token? LastCodeToken;
    internal PendingWs Pending;

    // ── Trivia handler ────────────────────────────────────────────
    internal readonly TriviaHandler Trivia;

    internal enum PendingWs { None, Space, SoftLine, NewLine, BlankLine }

    public FormatterContext(FormatConfig? config = null)
    {
        var cfg = config ?? FormatConfig.Default;
        IndentSize = cfg.IndentSize;
        UseTabs = cfg.UseTabs;
        TrailingComma = cfg.TrailingComma;
        BracketSpacing = cfg.BracketSpacing;
        BlankLinesBetweenBlocks = cfg.BlankLinesBetweenBlocks;
        SingleLineBlocks = cfg.SingleLineBlocks;
        Trivia = new TriviaHandler(this);
    }

    /// <summary>Resets all per-call state for a new format operation.</summary>
    internal void Reset(Token[] codeTokens, Token[] triviaTokens)
    {
        Docs = new List<Doc>();
        Indent = 0;
        CodeTokens = codeTokens;
        Cursor = 0;
        LastCodeToken = null;
        Pending = PendingWs.None;
        Trivia.Reset(triviaTokens);
    }

    // ── Whitespace control — max-upgrade semantics ────────────────

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

    // ── Indentation + pending flush ───────────────────────────────

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

    // ── Token cursor ──────────────────────────────────────────────

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

    // ── Doc IR mark/wrap helpers ──────────────────────────────────

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

    // ── Direct Doc emission ───────────────────────────────────────

    public void AddDoc(Doc doc) => Docs.Add(doc);

    // ── Token access for advanced patterns ────────────────────────

    public bool HasMoreTokens => Cursor < CodeTokens.Length;
    public Token CurrentToken => CodeTokens[Cursor];
    public void SkipToken() => Cursor++;

    // ── Pending whitespace access ─────────────────────────────────

    public bool HasPendingWhitespace => Pending != PendingWs.None;
    public void ResetPending() => Pending = PendingWs.None;

    // ── Compound-operator detection ───────────────────────────────

    public bool NextIsCompoundOperator() =>
        Cursor < CodeTokens.Length && IsCompoundOperator(CodeTokens[Cursor].Type);

    public static bool IsCompoundOperator(TokenType t) => t is
        TokenType.PlusEqual or TokenType.MinusEqual or TokenType.StarEqual or
        TokenType.SlashEqual or TokenType.PercentEqual or TokenType.QuestionQuestionEqual or
        TokenType.AmpersandEqual or TokenType.PipeEqual or TokenType.CaretEqual or
        TokenType.LessLessEqual or TokenType.GreaterGreaterEqual;

    // ── Declaration detection ─────────────────────────────────────

    public static bool IsDeclaration(Stmt stmt) =>
        stmt is FnDeclStmt or StructDeclStmt or EnumDeclStmt or InterfaceDeclStmt or ExtendStmt;

    // ── Trivia delegation ─────────────────────────────────────────

    public void FlushTriviaBeforeCurrentToken()
    {
        if (HasMoreTokens) Trivia.FlushTriviaBefore(CurrentToken);
    }

    public void FlushRemainingTrivia() => Trivia.FlushRemainingTrivia();

    // ── Trivia access (for BlockHasComments) ──────────────────────

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
