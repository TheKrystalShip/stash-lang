using System;
using System.Collections.Generic;
using System.Text;
using Stash.Analysis.Formatting;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

namespace Stash.Analysis;

/// <summary>
/// Reformats a Stash source file by parsing it into an AST and walking the tree,
/// emitting code tokens with canonical whitespace and interleaving preserved trivia
/// (comments, shebang) at appropriate positions.
/// </summary>
/// <remarks>
/// <para>
/// Architecture: the formatter lexes with <c>preserveTrivia: true</c>, separates code tokens
/// from trivia, parses code tokens into an AST, then walks the AST via the Visitor pattern
/// (<see cref="IStmtVisitor{T}"/> and <see cref="IExprVisitor{T}"/>). Each visitor method
/// emits the next token(s) from a cursor over the code-token array with deterministic
/// whitespace. Trivia is interleaved just before the code token it precedes in source order.
/// </para>
/// </remarks>
public class StashFormatter : IStmtVisitor<int>, IExprVisitor<int>
{
    private readonly int _indentSize;
    private readonly bool _useTabs;
    private readonly TrailingCommaStyle _trailingComma;
    private readonly EndOfLineStyle _endOfLine;
    private readonly bool _bracketSpacing;

    // ── Per-call state (reset in Format) ──────────────────────────────────────

    private List<Doc> _docs = new();
    private readonly int _printWidth;
    private int _indent;
    private Token[] _codeTokens = Array.Empty<Token>();
    private int _cursor;
    private Token[] _triviaTokens = Array.Empty<Token>();
    private int _triviaCursor;
    private Token? _lastCodeToken;
    private PendingWs _pending;
    private string _source = "";
    private string[] _sourceLines = Array.Empty<string>();
    private HashSet<int> _ignoreLines = new();

    private enum PendingWs { None, Space, NewLine, BlankLine }

    /// <summary>
    /// Initializes a new <see cref="StashFormatter"/> with settings from the given <see cref="FormatConfig"/>.
    /// </summary>
    public StashFormatter(FormatConfig? config = null)
    {
        var cfg = config ?? FormatConfig.Default;
        _indentSize = cfg.IndentSize;
        _useTabs = cfg.UseTabs;
        _trailingComma = cfg.TrailingComma;
        _endOfLine = cfg.EndOfLine;
        _bracketSpacing = cfg.BracketSpacing;
        _printWidth = cfg.PrintWidth;
    }

    /// <summary>
    /// Initializes a new <see cref="StashFormatter"/> with the given indentation settings.
    /// </summary>
    /// <param name="indentSize">Spaces per indent level (ignored when <paramref name="useTabs"/> is <see langword="true"/>).</param>
    /// <param name="useTabs">Use a tab per indent level. Defaults to <see langword="false"/>.</param>
    public StashFormatter(int indentSize, bool useTabs = false)
    {
        _indentSize = indentSize;
        _useTabs = useTabs;
        _trailingComma = TrailingCommaStyle.None;
        _endOfLine = EndOfLineStyle.Lf;
        _bracketSpacing = true;
        _printWidth = 80;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Whitespace control — max-upgrade semantics
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Space()
    {
        if (_pending < PendingWs.Space)
        {
            _pending = PendingWs.Space;
        }
    }

    private void NewLine()
    {
        if (_pending < PendingWs.NewLine)
        {
            _pending = PendingWs.NewLine;
        }
    }

    private void BlankLine()
    {
        _pending = PendingWs.BlankLine;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Indentation + pending flush
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private string IndentString()
    {
        char ch = _useTabs ? '\t' : ' ';
        int count = _useTabs ? _indent : _indent * _indentSize;
        return count > 0 ? new string(ch, count) : "";
    }

    private void WritePending()
    {
        switch (_pending)
        {
            case PendingWs.BlankLine:
                _docs.Add(Doc.HardLine);
                _docs.Add(Doc.HardLine);
                break;
            case PendingWs.NewLine:
                _docs.Add(Doc.HardLine);
                break;
            case PendingWs.Space:
                _docs.Add(Doc.Text(" "));
                break;
        }
        _pending = PendingWs.None;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Token cursor
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private bool NextIs(TokenType t) =>
        _cursor < _codeTokens.Length && _codeTokens[_cursor].Type == t;

    private void EmitToken()
    {
        var token = _codeTokens[_cursor];
        FlushTriviaBefore(token);
        WritePending();
        _docs.Add(Doc.Text(token.Lexeme));
        _lastCodeToken = token;
        _cursor++;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Doc IR mark/wrap helpers
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private int Mark() => _docs.Count;

    private void WrapFrom(int mark, Func<Doc, Doc> wrapper)
    {
        int count = _docs.Count - mark;
        if (count == 0) return;
        var slice = new Doc[count];
        for (int i = 0; i < count; i++)
            slice[i] = _docs[mark + i];
        _docs.RemoveRange(mark, count);
        _docs.Add(wrapper(Doc.Concat(slice)));
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Trivia interleaving
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void FlushTriviaBefore(Token upTo)
    {
        while (_triviaCursor < _triviaTokens.Length)
        {
            var trivia = _triviaTokens[_triviaCursor];
            // Stop when trivia is at or after the next code token's position
            if (trivia.Span.StartLine > upTo.Span.StartLine
                || (trivia.Span.StartLine == upTo.Span.StartLine
                    && trivia.Span.StartColumn >= upTo.Span.StartColumn))
            {
                break;
            }

            _triviaCursor++;
            ProcessTrivia(trivia, upTo);
        }
    }

    private void ProcessTrivia(Token trivia, Token? upTo)
    {
        if (trivia.Type == TokenType.Shebang)
        {
            _docs.Add(Doc.Text(trivia.Lexeme));
            _pending = PendingWs.BlankLine;
            return;
        }

        // Inline: trivia sits on the same line as the last emitted code token
        bool isInline = _lastCodeToken != null
            && trivia.Span.StartLine == _lastCodeToken.Span.EndLine;

        if (isInline)
        {
            // Attach comment to end of current line — no WritePending
            _docs.Add(Doc.Text(" "));
            _docs.Add(Doc.Text(trivia.Lexeme));
            // After an inline comment restore at least a newline (blank line at top level)
            if (_indent == 0)
            {
                _pending = PendingWs.BlankLine;
            }
            else if (_pending < PendingWs.NewLine)
            {
                _pending = PendingWs.NewLine;
            }
        }
        else
        {
            // Standalone: position the comment at the current indent level
            WritePending();
            if (trivia.Type == TokenType.BlockComment)
            {
                FormatBlockComment(trivia.Lexeme);
            }
            else
            {
                _docs.Add(Doc.Text(trivia.Lexeme));
            }

            // Determine whitespace after this trivia
            bool nextFollowsImmediately = false;
            if (_triviaCursor < _triviaTokens.Length)
            {
                var next = _triviaTokens[_triviaCursor];
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
                // Part of a consecutive comment block
                if (_pending < PendingWs.NewLine)
                {
                    _pending = PendingWs.NewLine;
                }
            }
            else if (trivia.Type == TokenType.BlockComment)
            {
                // Block comments: single newline before next token (not a blank line)
                if (_pending < PendingWs.NewLine)
                {
                    _pending = PendingWs.NewLine;
                }
            }
            else
            {
                // Single-line / doc comments: blank line at top level, newline inside blocks
                if (_indent == 0)
                {
                    _pending = PendingWs.BlankLine;
                }
                else if (_pending < PendingWs.NewLine)
                {
                    _pending = PendingWs.NewLine;
                }
            }
        }
    }

    private void FlushRemainingTrivia()
    {
        while (_triviaCursor < _triviaTokens.Length)
        {
            ProcessTrivia(_triviaTokens[_triviaCursor++], null);
        }
    }

    private void FormatBlockComment(string lexeme)
    {
        var lines = lexeme.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                _docs.Add(Doc.HardLine);
                _docs.Add(Doc.Text(lines[i].TrimStart()));
            }
            else
            {
                _docs.Add(Doc.Text(lines[i].TrimEnd()));
            }
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Compound-operator detection
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static bool IsCompoundOperator(TokenType t) => t is
        TokenType.PlusEqual or TokenType.MinusEqual or TokenType.StarEqual or
        TokenType.SlashEqual or TokenType.PercentEqual or TokenType.QuestionQuestionEqual or
        TokenType.AmpersandEqual or TokenType.PipeEqual or TokenType.CaretEqual or
        TokenType.LessLessEqual or TokenType.GreaterGreaterEqual;

    private bool NextIsCompoundOperator() =>
        _cursor < _codeTokens.Length && IsCompoundOperator(_codeTokens[_cursor].Type);

    private static bool IsDeclaration(Stmt stmt) =>
        stmt is FnDeclStmt or StructDeclStmt or EnumDeclStmt or InterfaceDeclStmt or ExtendStmt;

    private bool WillExpandToMultiLine(Expr expr)
    {
        return expr switch
        {
            DictLiteralExpr dict => dict.Entries.Count >= 3 || dict.Span.EndLine > dict.Span.StartLine
                || dict.Entries.Exists(e => WillExpandToMultiLine(e.Value)),
            StructInitExpr init => init.FieldValues.Count >= 3 || init.Span.EndLine > init.Span.StartLine
                || init.FieldValues.Exists(fv => WillExpandToMultiLine(fv.Value)),
            ArrayExpr arr => arr.Span.EndLine > arr.Span.StartLine || arr.Elements.Exists(WillExpandToMultiLine),
            _ => expr.Span.EndLine > expr.Span.StartLine
        };
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Public API
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>
    /// Formats the given Stash <paramref name="source"/> string and returns the reformatted
    /// result with a trailing newline. Returns an empty string for whitespace-only input.
    /// </summary>
    public string Format(string source)
    {
        // 1. Lex everything including trivia
        var lexer = new Lexer(source, "<format>", preserveTrivia: true);
        var allTokens = lexer.ScanTokens();

        // 2. Separate code tokens from trivia tokens
        var code = new List<Token>();
        var trivia = new List<Token>();
        foreach (var t in allTokens)
        {
            if (t.Type == TokenType.Eof)
            {
                continue;
            }

            if (t.Type is TokenType.SingleLineComment or TokenType.BlockComment
                or TokenType.DocComment or TokenType.Shebang)
            {
                trivia.Add(t);
            }
            else
            {
                code.Add(t);
            }
        }
        _codeTokens = code.ToArray();
        _triviaTokens = trivia.ToArray();

        // 2b. Scan trivia for formatter ignore directives
        _source = source;
        _sourceLines = source.Split('\n');
        _ignoreLines = new HashSet<int>();
        foreach (var t in _triviaTokens)
        {
            if (t.Type != TokenType.SingleLineComment) continue;
            string text = t.Lexeme.TrimEnd();
            if (text.EndsWith("stash-ignore-all format", StringComparison.Ordinal))
            {
                // Entire file is exempt from formatting — return original source unchanged
                return source;
            }
            if (text.EndsWith("stash-ignore format", StringComparison.Ordinal))
            {
                _ignoreLines.Add(t.Span.StartLine);
            }
        }

        // 3. Parse code tokens into an AST (re-attach the Eof token)
        var parserTokens = new List<Token>(code);
        parserTokens.Add(allTokens[^1]); // Eof
        var parser = new Parser(parserTokens);
        var statements = parser.ParseProgram();
        if (parser.Errors.Count > 0)
            throw new InvalidOperationException(parser.Errors[0]);

        // 4. Reset per-call state
        _docs = new List<Doc>();
        _indent = 0;
        _cursor = 0;
        _triviaCursor = 0;
        _lastCodeToken = null;
        _pending = PendingWs.None;

        // 5. Walk the AST; blank line around declarations, newline between regular statements
        for (int i = 0; i < statements.Count; i++)
        {
            if (i > 0)
            {
                if (IsDeclaration(statements[i]) || IsDeclaration(statements[i - 1]))
                {
                    BlankLine();
                }
                else
                {
                    NewLine();
                }
            }

            var stmt = statements[i];
            if (_ignoreLines.Contains(stmt.Span.StartLine - 1))
            {
                // Flush any trivia before this statement (emits the // stash-ignore format comment)
                if (_cursor < _codeTokens.Length)
                    FlushTriviaBefore(_codeTokens[_cursor]);
                WritePending();
                EmitIgnoredStatement(stmt);
            }
            else
            {
                stmt.Accept(this);
            }
        }

        // 6. Flush end-of-file trivia (trailing comments)
        FlushRemainingTrivia();

        // 7. Normalise trailing whitespace and add exactly one trailing newline
        var doc = Doc.Concat(_docs.ToArray());
        char indentChar = _useTabs ? '\t' : ' ';
        int indentWidth = _useTabs ? 1 : _indentSize;
        string result = DocPrinter.Print(doc, _printWidth, indentWidth, indentChar).TrimEnd();
        if (result.Length == 0) return "";
        return NormalizeEol(result + "\n");
    }

    /// <summary>
    /// Formats the given <paramref name="source"/> but only applies changes within
    /// the 1-based line range [<paramref name="startLine"/>, <paramref name="endLine"/>].
    /// Lines outside the range are preserved unmodified.
    /// </summary>
    public string FormatRange(string source, int startLine, int endLine)
    {
        string fullyFormatted;
        try
        {
            fullyFormatted = Format(source);
        }
        catch
        {
            return source;
        }

        if (fullyFormatted == source)
            return source;

        string[] originalLines = source.TrimEnd('\n').Split('\n');
        string[] formattedLines = fullyFormatted.TrimEnd('\n').Split('\n');

        // Clamp range (1-based inclusive)
        startLine = Math.Max(1, startLine);
        endLine = Math.Min(originalLines.Length, endLine);
        if (startLine > endLine)
            return source;

        // If line counts match, simple line-by-line replacement within the range
        if (originalLines.Length == formattedLines.Length)
        {
            var result = new string[originalLines.Length];
            for (int i = 0; i < originalLines.Length; i++)
            {
                result[i] = (i + 1 >= startLine && i + 1 <= endLine)
                    ? formattedLines[i]
                    : originalLines[i];
            }
            string text = string.Join("\n", result);
            if (source.EndsWith("\n")) text += "\n";
            return text;
        }

        // If line counts differ, fall back to full format
        return fullyFormatted;
    }

    private string NormalizeEol(string text)
    {
        return _endOfLine switch
        {
            EndOfLineStyle.Crlf => text.Replace("\r\n", "\n").Replace("\n", "\r\n"),
            EndOfLineStyle.Auto => DetectSourceEol(_source) == EndOfLineStyle.Crlf
                ? text.Replace("\r\n", "\n").Replace("\n", "\r\n")
                : text,
            _ => text
        };
    }

    private static EndOfLineStyle DetectSourceEol(string source)
    {
        foreach (char c in source)
        {
            if (c == '\r') return EndOfLineStyle.Crlf;
            if (c == '\n') return EndOfLineStyle.Lf;
        }
        return EndOfLineStyle.Lf;
    }

    private void EmitIgnoredStatement(Stmt stmt)
    {
        // Emit the source text verbatim for this statement's span
        int startLine = stmt.Span.StartLine; // 1-based
        int endLine = stmt.Span.EndLine;     // 1-based
        var verbatim = new StringBuilder();
        for (int lineIdx = startLine; lineIdx <= endLine; lineIdx++)
        {
            if (lineIdx > startLine) verbatim.Append('\n');
            if (lineIdx - 1 < _sourceLines.Length)
                verbatim.Append(_sourceLines[lineIdx - 1]);
        }
        _docs.Add(Doc.Text(verbatim.ToString()));

        // Advance code token cursor past all tokens belonging to this statement
        while (_cursor < _codeTokens.Length)
        {
            Token t = _codeTokens[_cursor];
            if (t.Span.StartLine > endLine) break;
            if (t.Span.StartLine == endLine && t.Span.StartColumn > stmt.Span.EndColumn) break;
            _cursor++;
        }

        // Advance trivia cursor past all trivia belonging to this statement
        while (_triviaCursor < _triviaTokens.Length)
        {
            Token t = _triviaTokens[_triviaCursor];
            if (t.Span.StartLine > endLine) break;
            if (t.Span.StartLine == endLine && t.Span.StartColumn > stmt.Span.EndColumn) break;
            _triviaCursor++;
        }

        _lastCodeToken = null;
        _pending = PendingWs.None;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Statement Visitors
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public int VisitVarDeclStmt(VarDeclStmt stmt)
    {
        EmitToken(); // let
        Space();
        EmitToken(); // name
        if (stmt.TypeHint != null)
        {
            EmitToken(); // :
            Space();
            EmitToken(); // type
        }
        if (stmt.Initializer != null)
        {
            Space();
            EmitToken(); // =
            Space();
            stmt.Initializer.Accept(this);
        }
        EmitToken(); // ;
        return 0;
    }

    public int VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        EmitToken(); // const
        Space();
        EmitToken(); // name
        if (stmt.TypeHint != null)
        {
            EmitToken(); // :
            Space();
            EmitToken(); // type
        }
        Space();
        EmitToken(); // =
        Space();
        stmt.Initializer.Accept(this);
        EmitToken(); // ;
        return 0;
    }

    public int VisitFnDeclStmt(FnDeclStmt stmt)
    {
        if (stmt.IsAsync)
        {
            EmitToken(); // async
            Space();
        }
        EmitToken(); // fn
        Space();
        EmitToken(); // name
        EmitToken(); // (
        for (int i = 0; i < stmt.Parameters.Count; i++)
        {
            if (i > 0) { EmitToken(); Space(); } // ,
            if (stmt.HasRestParam && i == stmt.Parameters.Count - 1)
            {
                EmitToken(); // ...
            }
            EmitToken(); // param name
            if (stmt.ParameterTypes[i] != null)
            {
                EmitToken(); // :
                Space();
                EmitToken(); // type
            }
            if (stmt.DefaultValues[i] != null)
            {
                Space();
                EmitToken(); // =
                Space();
                stmt.DefaultValues[i]!.Accept(this);
            }
        }
        EmitToken(); // )
        if (stmt.ReturnType != null)
        {
            Space();
            EmitToken(); // ->
            Space();
            EmitToken(); // return type
        }
        Space();
        stmt.Body.Accept(this);
        return 0;
    }

    public int VisitBlockStmt(BlockStmt stmt)
    {
        EmitToken(); // {
        _indent++;
        int mark = Mark();
        foreach (var s in stmt.Statements)
        {
            NewLine();
            s.Accept(this);
        }
        WrapFrom(mark, Doc.Indent);
        _indent--;
        NewLine();
        EmitToken(); // }
        return 0;
    }

    public int VisitIfStmt(IfStmt stmt)
    {
        EmitToken(); // if
        Space();
        EmitToken(); // (
        stmt.Condition.Accept(this);
        EmitToken(); // )
        Space();
        stmt.ThenBranch.Accept(this);
        if (stmt.ElseBranch != null)
        {
            Space();
            EmitToken(); // else
            Space();
            stmt.ElseBranch.Accept(this); // BlockStmt or chained IfStmt
        }
        return 0;
    }

    public int VisitWhileStmt(WhileStmt stmt)
    {
        EmitToken(); // while
        Space();
        EmitToken(); // (
        stmt.Condition.Accept(this);
        EmitToken(); // )
        Space();
        stmt.Body.Accept(this);
        return 0;
    }

    /// <inheritdoc />
    public int VisitElevateStmt(ElevateStmt stmt)
    {
        EmitToken(); // elevate
        if (stmt.Elevator != null)
        {
            EmitToken(); // (
            stmt.Elevator.Accept(this);
            EmitToken(); // )
        }
        Space();
        stmt.Body.Accept(this);
        return 0;
    }

    public int VisitDoWhileStmt(DoWhileStmt stmt)
    {
        EmitToken(); // do
        Space();
        stmt.Body.Accept(this);
        Space();
        EmitToken(); // while
        Space();
        EmitToken(); // (
        stmt.Condition.Accept(this);
        EmitToken(); // )
        EmitToken(); // ;
        return 0;
    }

    public int VisitForStmt(ForStmt stmt)
    {
        EmitToken(); // for
        Space();
        EmitToken(); // (
        if (stmt.Initializer is not null)
        {
            stmt.Initializer.Accept(this); // emits initializer tokens including trailing ;
        }
        else
        {
            EmitToken(); // ;
        }
        Space();
        if (stmt.Condition is not null)
        {
            stmt.Condition.Accept(this);
        }
        EmitToken(); // ;
        if (stmt.Increment is not null)
        {
            Space();
            stmt.Increment.Accept(this);
        }
        EmitToken(); // )
        Space();
        stmt.Body.Accept(this);
        return 0;
    }

    public int VisitForInStmt(ForInStmt stmt)
    {
        EmitToken(); // for
        Space();
        EmitToken(); // (
        EmitToken(); // let
        Space();
        if (stmt.IndexName != null)
        {
            EmitToken(); // index variable
            EmitToken(); // ,
            Space();
        }
        EmitToken(); // loop variable
        if (stmt.TypeHint != null)
        {
            EmitToken(); // :
            Space();
            EmitToken(); // type
        }
        Space();
        EmitToken(); // in
        Space();
        stmt.Iterable.Accept(this);
        EmitToken(); // )
        Space();
        stmt.Body.Accept(this);
        return 0;
    }

    public int VisitReturnStmt(ReturnStmt stmt)
    {
        EmitToken(); // return
        if (stmt.Value != null)
        {
            Space();
            stmt.Value.Accept(this);
        }
        EmitToken(); // ;
        return 0;
    }

    public int VisitThrowStmt(ThrowStmt stmt)
    {
        EmitToken(); // throw
        Space();
        stmt.Value.Accept(this);
        EmitToken(); // ;
        return 0;
    }

    public int VisitTryCatchStmt(TryCatchStmt stmt)
    {
        EmitToken(); // try
        Space();
        stmt.TryBody.Accept(this);
        if (stmt.CatchBody is not null)
        {
            Space();
            EmitToken(); // catch
            Space();
            EmitToken(); // (
            EmitToken(); // variable
            EmitToken(); // )
            Space();
            stmt.CatchBody.Accept(this);
        }
        if (stmt.FinallyBody is not null)
        {
            Space();
            EmitToken(); // finally
            Space();
            stmt.FinallyBody.Accept(this);
        }
        return 0;
    }

    public int VisitBreakStmt(BreakStmt stmt)
    {
        EmitToken(); // break
        EmitToken(); // ;
        return 0;
    }

    public int VisitContinueStmt(ContinueStmt stmt)
    {
        EmitToken(); // continue
        EmitToken(); // ;
        return 0;
    }

    public int VisitExprStmt(ExprStmt stmt)
    {
        stmt.Expression.Accept(this);
        EmitToken(); // ;
        return 0;
    }

    public int VisitExtendStmt(ExtendStmt stmt)
    {
        EmitToken(); // extend
        Space();
        EmitToken(); // type name
        Space();
        EmitToken(); // {
        _indent++;
        int mark = Mark();
        for (int i = 0; i < stmt.Methods.Count; i++)
        {
            if (i > 0)
            {
                BlankLine();
            }
            else
            {
                NewLine();
            }
            stmt.Methods[i].Accept(this);
        }
        WrapFrom(mark, Doc.Indent);
        _indent--;
        NewLine();
        EmitToken(); // }
        return 0;
    }

    public int VisitStructDeclStmt(StructDeclStmt stmt)
    {
        EmitToken(); // struct
        Space();
        EmitToken(); // name
        if (stmt.Interfaces.Count > 0)
        {
            Space();
            EmitToken(); // :
            Space();
            for (int i = 0; i < stmt.Interfaces.Count; i++)
            {
                if (i > 0)
                {
                    EmitToken(); // ,
                    Space();
                }
                EmitToken(); // interface name
            }
        }
        Space();
        EmitToken(); // {
        _indent++;
        int mark = Mark();
        for (int i = 0; i < stmt.Fields.Count; i++)
        {
            NewLine();
            EmitToken(); // field name
            if (stmt.FieldTypes[i] != null)
            {
                EmitToken(); // :
                Space();
                EmitToken(); // type
            }
            if (NextIs(TokenType.Comma))
            {
                EmitToken(); // , (only if present in source)
            }
        }
        for (int i = 0; i < stmt.Methods.Count; i++)
        {
            if (i > 0 || stmt.Fields.Count > 0)
            {
                BlankLine();
            }
            else
            {
                NewLine();
            }
            stmt.Methods[i].Accept(this);
        }
        WrapFrom(mark, Doc.Indent);
        _indent--;
        NewLine();
        EmitToken(); // }
        return 0;
    }

    public int VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        EmitToken(); // enum
        Space();
        EmitToken(); // name
        Space();
        EmitToken(); // {
        _indent++;
        int mark = Mark();
        for (int i = 0; i < stmt.Members.Count; i++)
        {
            NewLine();
            EmitToken(); // member name
            if (NextIs(TokenType.Comma))
            {
                EmitToken(); // ,
            }
        }
        WrapFrom(mark, Doc.Indent);
        _indent--;
        NewLine();
        EmitToken(); // }
        return 0;
    }

    public int VisitInterfaceDeclStmt(InterfaceDeclStmt stmt)
    {
        EmitToken(); // interface
        Space();
        EmitToken(); // name
        Space();
        EmitToken(); // {
        _indent++;
        int mark = Mark();

        int totalMembers = stmt.Fields.Count + stmt.Methods.Count;
        int methodIndex = 0;
        for (int i = 0; i < totalMembers; i++)
        {
            NewLine();
            EmitToken(); // member name

            if (NextIs(TokenType.LeftParen))
            {
                // Method signature
                var method = stmt.Methods[methodIndex++];
                EmitToken(); // (
                for (int p = 0; p < method.Parameters.Count; p++)
                {
                    if (p > 0)
                    {
                        EmitToken(); // ,
                        Space();
                    }
                    EmitToken(); // param name
                    if (NextIs(TokenType.Colon))
                    {
                        EmitToken(); // :
                        Space();
                        EmitToken(); // type
                    }
                }
                EmitToken(); // )

                if (NextIs(TokenType.Arrow))
                {
                    Space();
                    EmitToken(); // ->
                    Space();
                    EmitToken(); // return type
                }
            }
            else if (NextIs(TokenType.Colon))
            {
                // Field with type hint
                EmitToken(); // :
                Space();
                EmitToken(); // type
            }

            if (NextIs(TokenType.Comma))
            {
                EmitToken(); // ,
            }
        }

        WrapFrom(mark, Doc.Indent);
        _indent--;
        NewLine();
        EmitToken(); // }
        return 0;
    }

    public int VisitImportAsStmt(ImportAsStmt stmt)
    {
        EmitToken(); // import
        Space();
        stmt.Path.Accept(this); // path expression (may be multi-token)
        Space();
        EmitToken(); // as
        Space();
        EmitToken(); // alias
        EmitToken(); // ;
        return 0;
    }

    public int VisitImportStmt(ImportStmt stmt)
    {
        EmitToken(); // import
        Space();
        EmitToken(); // {
        Space();
        for (int i = 0; i < stmt.Names.Count; i++)
        {
            if (i > 0) { EmitToken(); Space(); } // ,
            EmitToken(); // name
        }
        Space();
        EmitToken(); // }
        Space();
        EmitToken(); // from
        Space();
        stmt.Path.Accept(this); // path expression (may be multi-token)
        EmitToken(); // ;
        return 0;
    }

    public int VisitDestructureStmt(DestructureStmt stmt)
    {
        EmitToken(); // let or const
        Space();
        EmitToken(); // [ or {
        for (int i = 0; i < stmt.Names.Count; i++)
        {
            if (i > 0) { EmitToken(); Space(); } // ,
            EmitToken(); // name
        }
        if (stmt.RestName != null)
        {
            if (stmt.Names.Count > 0) { EmitToken(); Space(); } // ,
            EmitToken(); // ...
            EmitToken(); // rest name
        }
        EmitToken(); // ] or }
        Space();
        EmitToken(); // =
        Space();
        stmt.Initializer.Accept(this);
        EmitToken(); // ;
        return 0;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Expression Visitors
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public int VisitLiteralExpr(LiteralExpr expr)
    {
        EmitToken();
        return 0;
    }

    public int VisitIdentifierExpr(IdentifierExpr expr)
    {
        EmitToken();
        return 0;
    }

    public int VisitBinaryExpr(BinaryExpr expr)
    {
        expr.Left.Accept(this);
        Space();
        EmitToken(); // operator
        Space();
        expr.Right.Accept(this);
        return 0;
    }

    public int VisitIsExpr(IsExpr expr)
    {
        expr.Left.Accept(this);
        Space();
        EmitToken(); // is
        Space();
        if (expr.TypeName != null)
        {
            EmitToken(); // type name
        }
        else
        {
            expr.TypeExpr!.Accept(this);
        }
        return 0;
    }

    public int VisitUnaryExpr(UnaryExpr expr)
    {
        EmitToken(); // ! or -
        expr.Right.Accept(this);
        return 0;
    }

    public int VisitGroupingExpr(GroupingExpr expr)
    {
        EmitToken(); // (
        expr.Expression.Accept(this);
        EmitToken(); // )
        return 0;
    }

    public int VisitTernaryExpr(TernaryExpr expr)
    {
        expr.Condition.Accept(this);
        Space();
        EmitToken(); // ?
        Space();
        expr.ThenBranch.Accept(this);
        Space();
        EmitToken(); // :
        Space();
        expr.ElseBranch.Accept(this);
        return 0;
    }

    public int VisitAssignExpr(AssignExpr expr)
    {
        EmitToken(); // identifier
        Space();
        if (NextIsCompoundOperator())
        {
            EmitToken(); // compound operator (+=, -=, etc.)
            Space();
            // The parser desugars compound assignments: skip the repeated LHS, emit only RHS
            if (expr.Value is BinaryExpr binary)
            {
                binary.Right.Accept(this);
            }
            else if (expr.Value is NullCoalesceExpr nc)
            {
                nc.Right.Accept(this);
            }
            else
            {
                throw new InvalidOperationException($"Unexpected compound assignment value type: {expr.Value.GetType().Name}");
            }
        }
        else
        {
            EmitToken(); // =
            Space();
            expr.Value.Accept(this);
        }
        return 0;
    }

    public int VisitDotExpr(DotExpr expr)
    {
        expr.Object.Accept(this);
        EmitToken(); // . or ?.
        EmitToken(); // member name
        return 0;
    }

    public int VisitDotAssignExpr(DotAssignExpr expr)
    {
        expr.Object.Accept(this);
        EmitToken(); // .
        EmitToken(); // field name
        Space();
        if (NextIsCompoundOperator())
        {
            EmitToken(); // compound operator
            Space();
            if (expr.Value is BinaryExpr binary)
            {
                binary.Right.Accept(this);
            }
            else if (expr.Value is NullCoalesceExpr nc)
            {
                nc.Right.Accept(this);
            }
            else
            {
                throw new InvalidOperationException($"Unexpected compound assignment value type: {expr.Value.GetType().Name}");
            }
        }
        else
        {
            EmitToken(); // =
            Space();
            expr.Value.Accept(this);
        }
        return 0;
    }

    public int VisitCallExpr(CallExpr expr)
    {
        expr.Callee.Accept(this);
        EmitToken(); // (
        for (int i = 0; i < expr.Arguments.Count; i++)
        {
            if (i > 0) { EmitToken(); Space(); } // ,
            expr.Arguments[i].Accept(this);
        }
        EmitToken(); // )
        return 0;
    }

    public int VisitIndexExpr(IndexExpr expr)
    {
        expr.Object.Accept(this);
        EmitToken(); // [
        expr.Index.Accept(this);
        EmitToken(); // ]
        return 0;
    }

    public int VisitIndexAssignExpr(IndexAssignExpr expr)
    {
        expr.Object.Accept(this);
        EmitToken(); // [
        expr.Index.Accept(this);
        EmitToken(); // ]
        Space();
        if (NextIsCompoundOperator())
        {
            EmitToken(); // compound operator
            Space();
            if (expr.Value is BinaryExpr binary)
            {
                binary.Right.Accept(this);
            }
            else if (expr.Value is NullCoalesceExpr nc)
            {
                nc.Right.Accept(this);
            }
            else
            {
                throw new InvalidOperationException($"Unexpected compound assignment value type: {expr.Value.GetType().Name}");
            }
        }
        else
        {
            EmitToken(); // =
            Space();
            expr.Value.Accept(this);
        }
        return 0;
    }

    public int VisitArrayExpr(ArrayExpr expr)
    {
        bool multiLine = expr.Span.EndLine > expr.Span.StartLine
            || expr.Elements.Exists(WillExpandToMultiLine);
        EmitToken(); // [
        if (multiLine)
        {
            _indent++;
            int mark = Mark();
            for (int i = 0; i < expr.Elements.Count; i++)
            {
                NewLine();
                expr.Elements[i].Accept(this);
                if (NextIs(TokenType.Comma))
                {
                    EmitToken(); // ,
                }
                else if (_trailingComma == TrailingCommaStyle.All && i == expr.Elements.Count - 1)
                {
                    _docs.Add(Doc.Text(","));
                }
            }
            WrapFrom(mark, Doc.Indent);
            _indent--;
            NewLine();
        }
        else
        {
            for (int i = 0; i < expr.Elements.Count; i++)
            {
                if (i > 0) { EmitToken(); Space(); } // ,
                expr.Elements[i].Accept(this);
            }
        }
        EmitToken(); // ]
        return 0;
    }

    public int VisitStructInitExpr(StructInitExpr expr)
    {
        bool multiLine = expr.FieldValues.Count >= 3 || expr.Span.EndLine > expr.Span.StartLine
            || expr.FieldValues.Exists(fv => WillExpandToMultiLine(fv.Value));

        if (expr.Target != null)
        {
            expr.Target.Accept(this); // e.g. ns.StructName
        }
        else
        {
            EmitToken(); // struct name
        }

        Space();
        EmitToken(); // {

        if (multiLine)
        {
            _indent++;
            int mark = Mark();
            for (int i = 0; i < expr.FieldValues.Count; i++)
            {
                NewLine();
                EmitToken(); // field name
                EmitToken(); // :
                Space();
                expr.FieldValues[i].Value.Accept(this);
                if (NextIs(TokenType.Comma))
                {
                    EmitToken(); // ,
                }
                else if (_trailingComma == TrailingCommaStyle.All && i == expr.FieldValues.Count - 1)
                {
                    _docs.Add(Doc.Text(","));
                }
            }
            WrapFrom(mark, Doc.Indent);
            _indent--;
            NewLine();
        }
        else
        {
            if (_bracketSpacing) Space();
            for (int i = 0; i < expr.FieldValues.Count; i++)
            {
                if (i > 0) { EmitToken(); Space(); } // ,
                EmitToken(); // field name
                EmitToken(); // :
                Space();
                expr.FieldValues[i].Value.Accept(this);
            }
            // Consume any trailing comma silently — not emitted in inline format
            if (NextIs(TokenType.Comma))
            {
                _cursor++;
            }

            if (_bracketSpacing) Space();
        }
        EmitToken(); // }
        return 0;
    }

    public int VisitSwitchExpr(SwitchExpr expr)
    {
        expr.Subject.Accept(this);
        Space();
        EmitToken(); // switch
        Space();
        EmitToken(); // {
        _indent++;
        int mark = Mark();
        for (int i = 0; i < expr.Arms.Count; i++)
        {
            NewLine();
            var arm = expr.Arms[i];
            if (arm.IsDiscard)
            {
                EmitToken(); // _
            }
            else
            {
                arm.Pattern!.Accept(this);
            }

            Space();
            EmitToken(); // =>
            Space();
            arm.Body.Accept(this);
            if (NextIs(TokenType.Comma))
            {
                EmitToken(); // ,
            }
        }
        WrapFrom(mark, Doc.Indent);
        _indent--;
        NewLine();
        EmitToken(); // }
        return 0;
    }

    public int VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        EmitToken(); // single InterpolatedString token
        return 0;
    }

    public int VisitCommandExpr(CommandExpr expr)
    {
        EmitToken(); // single CommandLiteral / PassthroughCommandLiteral / StrictCommandLiteral / StrictPassthroughCommandLiteral token
        return 0;
    }

    public int VisitLambdaExpr(LambdaExpr expr)
    {
        if (expr.IsAsync)
        {
            EmitToken(); // async
            Space();
        }
        EmitToken(); // (
        for (int i = 0; i < expr.Parameters.Count; i++)
        {
            if (i > 0) { EmitToken(); Space(); } // ,
            if (expr.HasRestParam && i == expr.Parameters.Count - 1)
            {
                EmitToken(); // ...
            }
            EmitToken(); // param name
            if (expr.ParameterTypes[i] != null)
            {
                EmitToken(); // :
                Space();
                EmitToken(); // type
            }
            if (expr.DefaultValues[i] != null)
            {
                Space();
                EmitToken(); // =
                Space();
                expr.DefaultValues[i]!.Accept(this);
            }
        }
        EmitToken(); // )
        Space();
        EmitToken(); // =>
        Space();
        if (expr.BlockBody != null)
        {
            expr.BlockBody.Accept(this);
        }
        else
        {
            expr.ExpressionBody!.Accept(this);
        }

        return 0;
    }

    public int VisitUpdateExpr(UpdateExpr expr)
    {
        if (expr.IsPrefix)
        {
            EmitToken(); // ++ or --
            expr.Operand.Accept(this);
        }
        else
        {
            expr.Operand.Accept(this);
            EmitToken(); // ++ or --
        }
        return 0;
    }

    public int VisitTryExpr(TryExpr expr)
    {
        EmitToken(); // try
        Space();
        expr.Expression.Accept(this);
        return 0;
    }

    public int VisitAwaitExpr(AwaitExpr expr)
    {
        EmitToken(); // await
        Space();
        expr.Expression.Accept(this);
        return 0;
    }

    public int VisitRetryExpr(RetryExpr expr)
    {
        EmitToken(); // retry
        Space();
        EmitToken(); // (
        expr.MaxAttempts.Accept(this);

        if (expr.NamedOptions is not null)
        {
            foreach (var (_, value) in expr.NamedOptions)
            {
                EmitToken(); // ,
                Space();
                EmitToken(); // option name identifier
                EmitToken(); // :
                Space();
                value.Accept(this);
            }
        }
        else if (expr.OptionsExpr is not null)
        {
            EmitToken(); // ,
            Space();
            expr.OptionsExpr.Accept(this);
        }

        EmitToken(); // )

        if (expr.OnRetryClause is not null)
        {
            Space();
            EmitToken(); // onRetry (contextual identifier)
            if (expr.OnRetryClause.IsReference)
            {
                Space();
                expr.OnRetryClause.Reference!.Accept(this);
            }
            else
            {
                Space();
                EmitToken(); // (
                if (expr.OnRetryClause.ParamAttempt is not null)
                {
                    EmitToken(); // attempt param identifier
                    if (expr.OnRetryClause.ParamAttemptTypeHint is not null)
                    {
                        EmitToken(); // :
                        Space();
                        EmitToken(); // type hint identifier
                    }
                }
                if (expr.OnRetryClause.ParamError is not null)
                {
                    EmitToken(); // ,
                    Space();
                    EmitToken(); // error param identifier
                    if (expr.OnRetryClause.ParamErrorTypeHint is not null)
                    {
                        EmitToken(); // :
                        Space();
                        EmitToken(); // type hint identifier
                    }
                }
                EmitToken(); // )
                Space();
                expr.OnRetryClause.Body!.Accept(this);
            }
        }

        if (expr.UntilClause is not null)
        {
            Space();
            EmitToken(); // until (contextual identifier)
            Space();
            expr.UntilClause.Accept(this);
        }

        Space();
        expr.Body.Accept(this);
        return 0;
    }

    public int VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        expr.Left.Accept(this);
        Space();
        EmitToken(); // ??
        Space();
        expr.Right.Accept(this);
        return 0;
    }

    public int VisitPipeExpr(PipeExpr expr)
    {
        expr.Left.Accept(this);
        Space();
        EmitToken(); // |
        Space();
        expr.Right.Accept(this);
        return 0;
    }

    public int VisitRedirectExpr(RedirectExpr expr)
    {
        expr.Expression.Accept(this);
        Space();
        EmitToken(); // redirect operator (>, >>, 2>, &>, etc.)
        Space();
        expr.Target.Accept(this);
        return 0;
    }

    public int VisitRangeExpr(RangeExpr expr)
    {
        expr.Start.Accept(this);
        EmitToken(); // ..
        expr.End.Accept(this);
        if (expr.Step != null)
        {
            EmitToken(); // ..
            expr.Step.Accept(this);
        }
        return 0;
    }

    public int VisitDictLiteralExpr(DictLiteralExpr expr)
    {
        bool multiLine = expr.Entries.Count >= 3 || expr.Span.EndLine > expr.Span.StartLine
            || expr.Entries.Exists(e => WillExpandToMultiLine(e.Value));
        EmitToken(); // {
        if (multiLine)
        {
            _indent++;
            int mark = Mark();
            for (int i = 0; i < expr.Entries.Count; i++)
            {
                NewLine();
                if (expr.Entries[i].Key != null)
                {
                    EmitToken(); // key
                    EmitToken(); // :
                    Space();
                }
                expr.Entries[i].Value.Accept(this);
                if (NextIs(TokenType.Comma))
                {
                    EmitToken(); // ,
                }
                else if (_trailingComma == TrailingCommaStyle.All && i == expr.Entries.Count - 1)
                {
                    _docs.Add(Doc.Text(","));
                }
            }
            WrapFrom(mark, Doc.Indent);
            _indent--;
            NewLine();
        }
        else
        {
            if (_bracketSpacing) Space();
            for (int i = 0; i < expr.Entries.Count; i++)
            {
                if (i > 0) { EmitToken(); Space(); } // ,
                if (expr.Entries[i].Key != null)
                {
                    EmitToken(); // key
                    EmitToken(); // :
                    Space();
                }
                expr.Entries[i].Value.Accept(this);
            }
            // Consume any trailing comma silently — not emitted in inline format
            if (NextIs(TokenType.Comma))
            {
                _cursor++;
            }

            if (_bracketSpacing) Space();
        }
        EmitToken(); // }
        return 0;
    }

    /// <inheritdoc />
    public int VisitSpreadExpr(SpreadExpr expr)
    {
        EmitToken(); // ...
        expr.Expression.Accept(this);
        return 0;
    }
}
