namespace Stash.Lsp.Analysis;

using System.Collections.Generic;
using Stash.Common;
using Stash.Parsing.AST;

public enum DiagnosticLevel
{
    Error,
    Warning,
    Information
}

public class SemanticDiagnostic
{
    public string Message { get; }
    public DiagnosticLevel Level { get; }
    public SourceSpan Span { get; }

    public SemanticDiagnostic(string message, DiagnosticLevel level, SourceSpan span)
    {
        Message = message;
        Level = level;
        Span = span;
    }
}

public class SemanticValidator : IStmtVisitor<object?>, IExprVisitor<object?>
{
    private readonly ScopeTree _scopeTree;
    private readonly List<SemanticDiagnostic> _diagnostics = new();
    private int _loopDepth;
    private int _functionDepth;

    private static readonly HashSet<string> _builtInNames = new()
    {
        "typeof", "len", "lastError", "parseArgs", "args",
        "io", "fs", "env", "path", "conv", "process",
        "true", "false", "null",
        "println", "print", "readLine"
    };

    public SemanticValidator(ScopeTree scopeTree)
    {
        _scopeTree = scopeTree;
    }

    public List<SemanticDiagnostic> Validate(List<Stmt> statements)
    {
        _diagnostics.Clear();
        _loopDepth = 0;
        _functionDepth = 0;

        foreach (var stmt in statements)
        {
            stmt.Accept(this);
        }

        var unresolved = _scopeTree.GetUnresolvedReferences(_builtInNames);
        foreach (var reference in unresolved)
        {
            _diagnostics.Add(new SemanticDiagnostic(
                $"'{reference.Name}' is not defined.",
                DiagnosticLevel.Warning,
                reference.Span));
        }

        return _diagnostics;
    }

    // Statement visitors

    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        _functionDepth++;
        foreach (var s in stmt.Body.Statements)
        {
            s.Accept(this);
        }
        _functionDepth--;
        return null;
    }

    public object? VisitWhileStmt(WhileStmt stmt)
    {
        stmt.Condition.Accept(this);
        _loopDepth++;
        stmt.Body.Accept(this);
        _loopDepth--;
        return null;
    }

    public object? VisitForInStmt(ForInStmt stmt)
    {
        stmt.Iterable.Accept(this);
        _loopDepth++;
        stmt.Body.Accept(this);
        _loopDepth--;
        return null;
    }

    public object? VisitBreakStmt(BreakStmt stmt)
    {
        if (_loopDepth == 0)
        {
            _diagnostics.Add(new SemanticDiagnostic(
                "'break' used outside of a loop.",
                DiagnosticLevel.Error,
                stmt.Span));
        }
        return null;
    }

    public object? VisitContinueStmt(ContinueStmt stmt)
    {
        if (_loopDepth == 0)
        {
            _diagnostics.Add(new SemanticDiagnostic(
                "'continue' used outside of a loop.",
                DiagnosticLevel.Error,
                stmt.Span));
        }
        return null;
    }

    public object? VisitReturnStmt(ReturnStmt stmt)
    {
        if (_functionDepth == 0)
        {
            _diagnostics.Add(new SemanticDiagnostic(
                "'return' used outside of a function.",
                DiagnosticLevel.Error,
                stmt.Span));
        }
        stmt.Value?.Accept(this);
        return null;
    }

    public object? VisitVarDeclStmt(VarDeclStmt stmt)
    {
        stmt.Initializer?.Accept(this);
        return null;
    }

    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        stmt.Initializer.Accept(this);
        return null;
    }

    public object? VisitBlockStmt(BlockStmt stmt)
    {
        foreach (var s in stmt.Statements)
        {
            s.Accept(this);
        }
        return null;
    }

    public object? VisitIfStmt(IfStmt stmt)
    {
        stmt.Condition.Accept(this);
        stmt.ThenBranch.Accept(this);
        stmt.ElseBranch?.Accept(this);
        return null;
    }

    public object? VisitExprStmt(ExprStmt stmt)
    {
        stmt.Expression.Accept(this);
        return null;
    }

    public object? VisitStructDeclStmt(StructDeclStmt stmt) => null;
    public object? VisitEnumDeclStmt(EnumDeclStmt stmt) => null;
    public object? VisitImportStmt(ImportStmt stmt) => null;
    public object? VisitImportAsStmt(ImportAsStmt stmt) => null;

    // Expression visitors

    public object? VisitAssignExpr(AssignExpr expr)
    {
        var line = expr.Name.Span.StartLine;
        var col = expr.Name.Span.StartColumn;
        var definition = _scopeTree.FindDefinition(expr.Name.Lexeme, line, col);
        if (definition != null && definition.Kind == SymbolKind.Constant)
        {
            _diagnostics.Add(new SemanticDiagnostic(
                $"Cannot reassign constant '{expr.Name.Lexeme}'.",
                DiagnosticLevel.Error,
                expr.Name.Span));
        }
        expr.Value.Accept(this);
        return null;
    }

    public object? VisitCallExpr(CallExpr expr)
    {
        if (expr.Callee is IdentifierExpr id)
        {
            var line = id.Span.StartLine;
            var col = id.Span.StartColumn;
            var definition = _scopeTree.FindDefinition(id.Name.Lexeme, line, col);
            if (definition != null && definition.Kind == SymbolKind.Function && definition.Detail != null)
            {
                var paramCount = CountParameters(definition.Detail);
                if (paramCount >= 0 && expr.Arguments.Count != paramCount)
                {
                    _diagnostics.Add(new SemanticDiagnostic(
                        $"Expected {paramCount} arguments but got {expr.Arguments.Count}.",
                        DiagnosticLevel.Error,
                        expr.Paren.Span));
                }
            }
        }
        else
        {
            expr.Callee.Accept(this);
        }

        foreach (var arg in expr.Arguments)
        {
            arg.Accept(this);
        }
        return null;
    }

    public object? VisitLiteralExpr(LiteralExpr expr) => null;
    public object? VisitIdentifierExpr(IdentifierExpr expr) => null;

    public object? VisitUnaryExpr(UnaryExpr expr)
    {
        expr.Right.Accept(this);
        return null;
    }

    public object? VisitUpdateExpr(UpdateExpr expr)
    {
        expr.Operand.Accept(this);
        return null;
    }

    public object? VisitBinaryExpr(BinaryExpr expr)
    {
        expr.Left.Accept(this);
        expr.Right.Accept(this);
        return null;
    }

    public object? VisitGroupingExpr(GroupingExpr expr)
    {
        expr.Expression.Accept(this);
        return null;
    }

    public object? VisitTernaryExpr(TernaryExpr expr)
    {
        expr.Condition.Accept(this);
        expr.ThenBranch.Accept(this);
        expr.ElseBranch.Accept(this);
        return null;
    }

    public object? VisitArrayExpr(ArrayExpr expr)
    {
        foreach (var el in expr.Elements) el.Accept(this);
        return null;
    }

    public object? VisitIndexExpr(IndexExpr expr)
    {
        expr.Object.Accept(this);
        expr.Index.Accept(this);
        return null;
    }

    public object? VisitIndexAssignExpr(IndexAssignExpr expr)
    {
        expr.Object.Accept(this);
        expr.Index.Accept(this);
        expr.Value.Accept(this);
        return null;
    }

    public object? VisitStructInitExpr(StructInitExpr expr)
    {
        foreach (var (_, value) in expr.FieldValues) value.Accept(this);
        return null;
    }

    public object? VisitDotExpr(DotExpr expr)
    {
        expr.Object.Accept(this);
        return null;
    }

    public object? VisitDotAssignExpr(DotAssignExpr expr)
    {
        expr.Object.Accept(this);
        expr.Value.Accept(this);
        return null;
    }

    public object? VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        foreach (var part in expr.Parts) part.Accept(this);
        return null;
    }

    public object? VisitCommandExpr(CommandExpr expr)
    {
        foreach (var part in expr.Parts)
        {
            part.Accept(this);
        }
        return null;
    }

    public object? VisitPipeExpr(PipeExpr expr)
    {
        expr.Left.Accept(this);
        expr.Right.Accept(this);
        return null;
    }

    public object? VisitTryExpr(TryExpr expr)
    {
        expr.Expression.Accept(this);
        return null;
    }

    public object? VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        expr.Left.Accept(this);
        expr.Right.Accept(this);
        return null;
    }

    // Helper to count parameters from detail string "fn name(a, b)" or "fn name()"
    private static int CountParameters(string detail)
    {
        var openParen = detail.IndexOf('(');
        var closeParen = detail.IndexOf(')');
        if (openParen < 0 || closeParen < 0 || closeParen <= openParen)
            return -1;

        var inside = detail[(openParen + 1)..closeParen].Trim();
        if (string.IsNullOrEmpty(inside))
            return 0;

        return inside.Split(',').Length;
    }
}
