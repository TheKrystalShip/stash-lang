namespace Stash.Lsp.Analysis;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
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
    public bool IsUnnecessary { get; }

    public SemanticDiagnostic(string message, DiagnosticLevel level, SourceSpan span, bool isUnnecessary = false)
    {
        Message = message;
        Level = level;
        Span = span;
        IsUnnecessary = isUnnecessary;
    }
}

public class SemanticValidator : IStmtVisitor<object?>, IExprVisitor<object?>
{
    private readonly ScopeTree _scopeTree;
    private readonly List<SemanticDiagnostic> _diagnostics = new();
    private int _loopDepth;
    private int _functionDepth;

    private static readonly HashSet<string> _builtInNames = BuiltInRegistry.KnownNames;

    private static readonly HashSet<string> _validBuiltInTypes = BuiltInRegistry.ValidTypes;

    public SemanticValidator(ScopeTree scopeTree)
    {
        _scopeTree = scopeTree;
    }

    public List<SemanticDiagnostic> Validate(List<Stmt> statements)
    {
        _diagnostics.Clear();
        _loopDepth = 0;
        _functionDepth = 0;

        CheckUnreachableStatements(statements);

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

    private void ValidateTypeHint(Token? typeHint)
    {
        if (typeHint == null)
        {
            return;
        }

        var typeName = typeHint.Lexeme;

        if (_validBuiltInTypes.Contains(typeName))
        {
            return;
        }

        var definition = _scopeTree.FindDefinition(typeName, typeHint.Span.StartLine, typeHint.Span.StartColumn);
        if (definition != null && (definition.Kind == SymbolKind.Struct || definition.Kind == SymbolKind.Enum))
        {
            return;
        }

        _diagnostics.Add(new SemanticDiagnostic(
            $"Unknown type '{typeName}'.",
            DiagnosticLevel.Warning,
            typeHint.Span));
    }

    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        foreach (var paramType in stmt.ParameterTypes)
        {
            ValidateTypeHint(paramType);
        }
        ValidateTypeHint(stmt.ReturnType);

        _functionDepth++;
        CheckUnreachableStatements(stmt.Body.Statements);
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

    public object? VisitDoWhileStmt(DoWhileStmt stmt)
    {
        _loopDepth++;
        stmt.Body.Accept(this);
        _loopDepth--;
        stmt.Condition.Accept(this);
        return null;
    }

    public object? VisitForInStmt(ForInStmt stmt)
    {
        ValidateTypeHint(stmt.TypeHint);
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
        ValidateTypeHint(stmt.TypeHint);
        stmt.Initializer?.Accept(this);
        return null;
    }

    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        ValidateTypeHint(stmt.TypeHint);
        stmt.Initializer.Accept(this);
        return null;
    }

    public object? VisitDestructureStmt(DestructureStmt stmt)
    {
        stmt.Initializer.Accept(this);
        return null;
    }

    public object? VisitBlockStmt(BlockStmt stmt)
    {
        CheckUnreachableStatements(stmt.Statements);
        return null;
    }

    private static bool IsTerminatingStatement(Stmt stmt)
    {
        if (stmt is ReturnStmt || stmt is BreakStmt || stmt is ContinueStmt)
        {
            return true;
        }

        // process.exit(...) call
        if (stmt is ExprStmt exprStmt && exprStmt.Expression is CallExpr call &&
            call.Callee is DotExpr dot &&
            dot.Object is IdentifierExpr obj && obj.Name.Lexeme == "process" &&
            dot.Name.Lexeme == "exit")
        {
            return true;
        }

        return false;
    }

    private void CheckUnreachableStatements(List<Stmt> statements)
    {
        bool reachable = true;
        Stmt? terminatingStmt = null;

        foreach (var stmt in statements)
        {
            if (!reachable)
            {
                _diagnostics.Add(new SemanticDiagnostic(
                    "Unreachable code detected.",
                    DiagnosticLevel.Information,
                    stmt.Span,
                    isUnnecessary: true));
                // Still visit for other diagnostics (e.g., nested errors)
                stmt.Accept(this);
                continue;
            }

            stmt.Accept(this);

            if (IsTerminatingStatement(stmt))
            {
                reachable = false;
                terminatingStmt = stmt;
            }
        }
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

    public object? VisitStructDeclStmt(StructDeclStmt stmt)
    {
        foreach (var fieldType in stmt.FieldTypes)
        {
            ValidateTypeHint(fieldType);
        }
        return null;
    }
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
            if (definition != null && definition.Kind == SymbolKind.Function)
            {
                var paramCount = definition.ParameterNames != null
                    ? definition.ParameterNames.Length
                    : CountParameters(definition.Detail ?? "");
                if (paramCount >= 0)
                {
                    int requiredCount = definition.RequiredParameterCount ?? paramCount;
                    if (expr.Arguments.Count < requiredCount || expr.Arguments.Count > paramCount)
                    {
                        string expected = requiredCount == paramCount
                            ? $"{paramCount}"
                            : $"{requiredCount} to {paramCount}";
                        _diagnostics.Add(new SemanticDiagnostic(
                            $"Expected {expected} arguments but got {expr.Arguments.Count}.",
                            DiagnosticLevel.Error,
                            expr.Paren.Span));
                    }
                }
            }
        }
        else if (expr.Callee is DotExpr dot && dot.Object is IdentifierExpr nsId &&
                 BuiltInRegistry.IsBuiltInNamespace(nsId.Name.Lexeme))
        {
            var qualifiedName = $"{nsId.Name.Lexeme}.{dot.Name.Lexeme}";
            if (BuiltInRegistry.TryGetNamespaceFunction(qualifiedName, out var func) &&
                !func.IsVariadic &&
                expr.Arguments.Count != func.Parameters.Length)
            {
                _diagnostics.Add(new SemanticDiagnostic(
                    $"Expected {func.Parameters.Length} arguments but got {expr.Arguments.Count}.",
                    DiagnosticLevel.Error,
                    expr.Paren.Span));
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

    public object? VisitSwitchExpr(SwitchExpr expr)
    {
        expr.Subject.Accept(this);
        foreach (var arm in expr.Arms)
        {
            arm.Pattern?.Accept(this);
            arm.Body.Accept(this);
        }
        return null;
    }

    public object? VisitArrayExpr(ArrayExpr expr)
    {
        foreach (var el in expr.Elements)
        {
            el.Accept(this);
        }

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
        foreach (var (_, value) in expr.FieldValues)
        {
            value.Accept(this);
        }

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
        foreach (var part in expr.Parts)
        {
            part.Accept(this);
        }

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

    public object? VisitRedirectExpr(RedirectExpr expr)
    {
        expr.Expression.Accept(this);
        expr.Target.Accept(this);
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

    public object? VisitRangeExpr(RangeExpr expr)
    {
        expr.Start.Accept(this);
        expr.End.Accept(this);
        expr.Step?.Accept(this);
        return null;
    }

    public object? VisitLambdaExpr(LambdaExpr expr)
    {
        foreach (var paramType in expr.ParameterTypes)
        {
            if (paramType != null)
            {
                ValidateTypeHint(paramType);
            }
        }

        _functionDepth++;

        if (expr.ExpressionBody != null)
        {
            expr.ExpressionBody.Accept(this);
        }
        else if (expr.BlockBody != null)
        {
            expr.BlockBody.Accept(this);
        }

        _functionDepth--;
        return null;
    }

    // Helper to count parameters from detail string "fn name(a, b)" or "fn name()"
    private static int CountParameters(string detail)
    {
        var openParen = detail.IndexOf('(');
        var closeParen = detail.IndexOf(')');
        if (openParen < 0 || closeParen < 0 || closeParen <= openParen)
        {
            return -1;
        }

        var inside = detail[(openParen + 1)..closeParen].Trim();
        if (string.IsNullOrEmpty(inside))
        {
            return 0;
        }

        return inside.Split(',').Length;
    }
}
