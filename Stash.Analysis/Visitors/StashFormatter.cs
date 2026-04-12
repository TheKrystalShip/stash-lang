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
/// delegates to <see cref="StatementFormatter"/> or <see cref="ExpressionFormatter"/> which
/// emit tokens via a shared <see cref="FormatterContext"/>.
/// </para>
/// </remarks>
public class StashFormatter : IStmtVisitor<int>, IExprVisitor<int>
{
    private readonly FormatterContext _ctx;
    private readonly int _printWidth;
    private readonly EndOfLineStyle _endOfLine;
    private readonly bool _sortImports;
    private string _source = "";
    private string[] _sourceLines = Array.Empty<string>();
    private HashSet<int> _ignoreLines = new();

    /// <summary>
    /// Initializes a new <see cref="StashFormatter"/> with settings from the given <see cref="FormatConfig"/>.
    /// </summary>
    public StashFormatter(FormatConfig? config = null)
    {
        var cfg = config ?? FormatConfig.Default;
        _ctx = new FormatterContext(cfg);
        _printWidth = cfg.PrintWidth;
        _endOfLine = cfg.EndOfLine;
        _sortImports = cfg.SortImports;
    }

    /// <summary>
    /// Initializes a new <see cref="StashFormatter"/> with the given indentation settings.
    /// </summary>
    /// <param name="indentSize">Spaces per indent level (ignored when <paramref name="useTabs"/> is <see langword="true"/>).</param>
    /// <param name="useTabs">Use a tab per indent level. Defaults to <see langword="false"/>.</param>
    public StashFormatter(int indentSize, bool useTabs = false)
    {
        var cfg = new FormatConfig { IndentSize = indentSize, UseTabs = useTabs };
        _ctx = new FormatterContext(cfg);
        _printWidth = cfg.PrintWidth;
        _endOfLine = cfg.EndOfLine;
        _sortImports = cfg.SortImports;
    }

    // ── Recursion callbacks ───────────────────────────────────────

    private void FormatStmt(Stmt stmt) => stmt.Accept(this);
    private void FormatExpr(Expr expr) => expr.Accept(this);

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
        var codeTokens = code.ToArray();
        var triviaTokens = trivia.ToArray();

        // 2b. Scan trivia for formatter ignore directives
        _source = source;
        _sourceLines = source.Split('\n');
        _ignoreLines = new HashSet<int>();
        foreach (var t in triviaTokens)
        {
            if (t.Type != TokenType.SingleLineComment) continue;
            string text = t.Lexeme.TrimEnd();
            if (text.EndsWith("stash-ignore-all format", StringComparison.Ordinal))
            {
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
        _ctx.Reset(codeTokens, triviaTokens);

        // 5. Walk the AST; blank line around declarations, newline between regular statements
        for (int i = 0; i < statements.Count; i++)
        {
            if (i > 0)
            {
                if (FormatterContext.IsDeclaration(statements[i]) || FormatterContext.IsDeclaration(statements[i - 1]))
                {
                    _ctx.BlankLine();
                }
                else
                {
                    _ctx.NewLine();
                }
            }

            var stmt = statements[i];
            if (_ignoreLines.Contains(stmt.Span.StartLine - 1))
            {
                if (_ctx.HasMoreTokens)
                    _ctx.Trivia.FlushTriviaBefore(_ctx.CurrentToken);
                _ctx.WritePending();
                EmitIgnoredStatement(stmt);
            }
            else
            {
                stmt.Accept(this);
            }
        }

        // 6. Flush end-of-file trivia (trailing comments)
        _ctx.FlushRemainingTrivia();

        // 7. Normalise trailing whitespace and add exactly one trailing newline
        var doc = Doc.Concat(_ctx.Docs.ToArray());
        char indentChar = _ctx.UseTabs ? '\t' : ' ';
        int indentWidth = _ctx.UseTabs ? 1 : _ctx.IndentSize;
        string result = DocPrinter.Print(doc, _printWidth, indentWidth, indentChar).TrimEnd();
        if (result.Length == 0) return "";
        if (_sortImports)
            result = ImportSorter.SortFormattedImports(result);
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

        startLine = Math.Max(1, startLine);
        endLine = Math.Min(originalLines.Length, endLine);
        if (startLine > endLine)
            return source;

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
        int startLine = stmt.Span.StartLine;
        int endLine = stmt.Span.EndLine;
        var verbatim = new StringBuilder();
        for (int lineIdx = startLine; lineIdx <= endLine; lineIdx++)
        {
            if (lineIdx > startLine) verbatim.Append('\n');
            if (lineIdx - 1 < _sourceLines.Length)
                verbatim.Append(_sourceLines[lineIdx - 1]);
        }
        _ctx.AddDoc(Doc.Text(verbatim.ToString()));

        while (_ctx.HasMoreTokens)
        {
            Token t = _ctx.CurrentToken;
            if (t.Span.StartLine > endLine) break;
            if (t.Span.StartLine == endLine && t.Span.StartColumn > stmt.Span.EndColumn) break;
            _ctx.SkipToken();
        }

        while (_ctx.Trivia.CursorPosition < _ctx.Trivia.Tokens.Length)
        {
            Token t = _ctx.Trivia.Tokens[_ctx.Trivia.CursorPosition];
            if (t.Span.StartLine > endLine) break;
            if (t.Span.StartLine == endLine && t.Span.StartColumn > stmt.Span.EndColumn) break;
            _ctx.Trivia.CursorPosition++;
        }

        _ctx.LastCodeToken = null;
        _ctx.ResetPending();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Statement Visitors — thin delegation
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public int VisitVarDeclStmt(VarDeclStmt stmt)
    {
        StatementFormatter.FormatVarDecl(stmt, _ctx, FormatExpr);
        return 0;
    }

    public int VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        StatementFormatter.FormatConstDecl(stmt, _ctx, FormatExpr);
        return 0;
    }

    public int VisitFnDeclStmt(FnDeclStmt stmt)
    {
        StatementFormatter.FormatFnDecl(stmt, _ctx, FormatStmt, FormatExpr);
        return 0;
    }

    public int VisitBlockStmt(BlockStmt stmt)
    {
        StatementFormatter.FormatBlock(stmt, _ctx, FormatStmt);
        return 0;
    }

    public int VisitIfStmt(IfStmt stmt)
    {
        StatementFormatter.FormatIf(stmt, _ctx, FormatStmt, FormatExpr);
        return 0;
    }

    public int VisitWhileStmt(WhileStmt stmt)
    {
        StatementFormatter.FormatWhile(stmt, _ctx, FormatStmt, FormatExpr);
        return 0;
    }

    public int VisitElevateStmt(ElevateStmt stmt)
    {
        StatementFormatter.FormatElevate(stmt, _ctx, FormatStmt, FormatExpr);
        return 0;
    }

    public int VisitDoWhileStmt(DoWhileStmt stmt)
    {
        StatementFormatter.FormatDoWhile(stmt, _ctx, FormatStmt, FormatExpr);
        return 0;
    }

    public int VisitForStmt(ForStmt stmt)
    {
        StatementFormatter.FormatFor(stmt, _ctx, FormatStmt, FormatExpr);
        return 0;
    }

    public int VisitForInStmt(ForInStmt stmt)
    {
        StatementFormatter.FormatForIn(stmt, _ctx, FormatStmt, FormatExpr);
        return 0;
    }

    public int VisitReturnStmt(ReturnStmt stmt)
    {
        StatementFormatter.FormatReturn(stmt, _ctx, FormatExpr);
        return 0;
    }

    public int VisitThrowStmt(ThrowStmt stmt)
    {
        StatementFormatter.FormatThrow(stmt, _ctx, FormatExpr);
        return 0;
    }

    public int VisitTryCatchStmt(TryCatchStmt stmt)
    {
        StatementFormatter.FormatTryCatch(stmt, _ctx, FormatStmt);
        return 0;
    }

    public int VisitSwitchStmt(SwitchStmt stmt)
    {
        StatementFormatter.FormatSwitch(stmt, _ctx, FormatStmt, FormatExpr);
        return 0;
    }

    public int VisitBreakStmt(BreakStmt stmt)
    {
        StatementFormatter.FormatBreak(_ctx);
        return 0;
    }

    public int VisitContinueStmt(ContinueStmt stmt)
    {
        StatementFormatter.FormatContinue(_ctx);
        return 0;
    }

    public int VisitExprStmt(ExprStmt stmt)
    {
        StatementFormatter.FormatExprStmt(stmt, _ctx, FormatExpr);
        return 0;
    }

    public int VisitExtendStmt(ExtendStmt stmt)
    {
        StatementFormatter.FormatExtend(stmt, _ctx, FormatStmt);
        return 0;
    }

    public int VisitStructDeclStmt(StructDeclStmt stmt)
    {
        StatementFormatter.FormatStructDecl(stmt, _ctx, FormatStmt);
        return 0;
    }

    public int VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        StatementFormatter.FormatEnumDecl(stmt, _ctx);
        return 0;
    }

    public int VisitInterfaceDeclStmt(InterfaceDeclStmt stmt)
    {
        StatementFormatter.FormatInterfaceDecl(stmt, _ctx);
        return 0;
    }

    public int VisitImportAsStmt(ImportAsStmt stmt)
    {
        StatementFormatter.FormatImportAs(stmt, _ctx, FormatExpr);
        return 0;
    }

    public int VisitImportStmt(ImportStmt stmt)
    {
        StatementFormatter.FormatImport(stmt, _ctx, FormatExpr);
        return 0;
    }

    public int VisitDestructureStmt(DestructureStmt stmt)
    {
        StatementFormatter.FormatDestructure(stmt, _ctx, FormatExpr);
        return 0;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Expression Visitors — thin delegation
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public int VisitLiteralExpr(LiteralExpr expr)
    {
        ExpressionFormatter.FormatLiteral(_ctx);
        return 0;
    }

    public int VisitIdentifierExpr(IdentifierExpr expr)
    {
        ExpressionFormatter.FormatIdentifier(_ctx);
        return 0;
    }

    public int VisitBinaryExpr(BinaryExpr expr)
    {
        ExpressionFormatter.FormatBinary(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitIsExpr(IsExpr expr)
    {
        ExpressionFormatter.FormatIs(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitUnaryExpr(UnaryExpr expr)
    {
        ExpressionFormatter.FormatUnary(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitGroupingExpr(GroupingExpr expr)
    {
        ExpressionFormatter.FormatGrouping(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitTernaryExpr(TernaryExpr expr)
    {
        ExpressionFormatter.FormatTernary(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitAssignExpr(AssignExpr expr)
    {
        ExpressionFormatter.FormatAssign(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitDotExpr(DotExpr expr)
    {
        ExpressionFormatter.FormatDot(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitDotAssignExpr(DotAssignExpr expr)
    {
        ExpressionFormatter.FormatDotAssign(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitCallExpr(CallExpr expr)
    {
        ExpressionFormatter.FormatCall(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitIndexExpr(IndexExpr expr)
    {
        ExpressionFormatter.FormatIndex(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitIndexAssignExpr(IndexAssignExpr expr)
    {
        ExpressionFormatter.FormatIndexAssign(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitArrayExpr(ArrayExpr expr)
    {
        ExpressionFormatter.FormatArray(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitStructInitExpr(StructInitExpr expr)
    {
        ExpressionFormatter.FormatStructInit(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitSwitchExpr(SwitchExpr expr)
    {
        ExpressionFormatter.FormatSwitchExpr(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        ExpressionFormatter.FormatInterpolatedString(_ctx);
        return 0;
    }

    public int VisitCommandExpr(CommandExpr expr)
    {
        ExpressionFormatter.FormatCommand(_ctx);
        return 0;
    }

    public int VisitLambdaExpr(LambdaExpr expr)
    {
        ExpressionFormatter.FormatLambda(expr, _ctx, FormatStmt, FormatExpr);
        return 0;
    }

    public int VisitUpdateExpr(UpdateExpr expr)
    {
        ExpressionFormatter.FormatUpdate(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitTryExpr(TryExpr expr)
    {
        ExpressionFormatter.FormatTry(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitAwaitExpr(AwaitExpr expr)
    {
        ExpressionFormatter.FormatAwait(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitRetryExpr(RetryExpr expr)
    {
        ExpressionFormatter.FormatRetry(expr, _ctx, FormatStmt, FormatExpr);
        return 0;
    }

    public int VisitTimeoutExpr(TimeoutExpr expr)
    {
        ExpressionFormatter.FormatTimeout(expr, _ctx, FormatStmt, FormatExpr);
        return 0;
    }

    public int VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        ExpressionFormatter.FormatNullCoalesce(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitPipeExpr(PipeExpr expr)
    {
        ExpressionFormatter.FormatPipe(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitRedirectExpr(RedirectExpr expr)
    {
        ExpressionFormatter.FormatRedirect(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitRangeExpr(RangeExpr expr)
    {
        ExpressionFormatter.FormatRange(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitDictLiteralExpr(DictLiteralExpr expr)
    {
        ExpressionFormatter.FormatDictLiteral(expr, _ctx, FormatExpr);
        return 0;
    }

    public int VisitSpreadExpr(SpreadExpr expr)
    {
        ExpressionFormatter.FormatSpread(expr, _ctx, FormatExpr);
        return 0;
    }
}
