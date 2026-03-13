namespace Stash.Lsp.Analysis;

using System.Collections.Generic;
using Stash.Common;
using Stash.Parsing.AST;

public class SymbolCollector : IStmtVisitor<object?>, IExprVisitor<object?>
{
    private Scope _currentScope = null!;

    public ScopeTree Collect(List<Stmt> statements)
    {
        var file = statements.Count > 0 ? statements[0].Span.File : "";
        var globalSpan = new SourceSpan(file, 1, 1, int.MaxValue, int.MaxValue);
        var globalScope = new Scope(ScopeKind.Global, null, globalSpan);
        _currentScope = globalScope;

        foreach (var stmt in statements)
        {
            stmt.Accept(this);
        }

        return new ScopeTree(globalScope);
    }

    private void PushScope(ScopeKind kind, SourceSpan span)
    {
        var scope = new Scope(kind, _currentScope, span);
        _currentScope = scope;
    }

    private void PopScope()
    {
        _currentScope = _currentScope.Parent!;
    }

    // Statement visitors

    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        var paramNames = new List<string>();
        foreach (var p in stmt.Parameters)
        {
            paramNames.Add(p.Lexeme);
        }

        var detail = $"fn {stmt.Name.Lexeme}({string.Join(", ", paramNames)})";

        // Function name goes into the parent (current) scope
        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Function, stmt.Name.Span, stmt.Span, detail));

        // Parameters and body statements share the function scope
        PushScope(ScopeKind.Function, stmt.Body.Span);
        foreach (var param in stmt.Parameters)
        {
            _currentScope.AddSymbol(new SymbolInfo(param.Lexeme, SymbolKind.Parameter, param.Span, detail: $"parameter of {stmt.Name.Lexeme}", parentName: stmt.Name.Lexeme));
        }

        foreach (var s in stmt.Body.Statements)
        {
            s.Accept(this);
        }

        PopScope();
        return null;
    }

    public object? VisitStructDeclStmt(StructDeclStmt stmt)
    {
        var fieldNames = new List<string>();
        foreach (var f in stmt.Fields)
        {
            fieldNames.Add(f.Lexeme);
        }

        var detail = $"struct {stmt.Name.Lexeme} {{ {string.Join(", ", fieldNames)} }}";

        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Struct, stmt.Name.Span, stmt.Span, detail));

        foreach (var field in stmt.Fields)
        {
            _currentScope.AddSymbol(new SymbolInfo(field.Lexeme, SymbolKind.Field, field.Span, detail: $"field of {stmt.Name.Lexeme}", parentName: stmt.Name.Lexeme));
        }

        return null;
    }

    public object? VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        var memberNames = new List<string>();
        foreach (var m in stmt.Members)
        {
            memberNames.Add(m.Lexeme);
        }

        var detail = $"enum {stmt.Name.Lexeme} {{ {string.Join(", ", memberNames)} }}";

        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Enum, stmt.Name.Span, stmt.Span, detail));

        foreach (var member in stmt.Members)
        {
            _currentScope.AddSymbol(new SymbolInfo(member.Lexeme, SymbolKind.EnumMember, member.Span, detail: $"member of {stmt.Name.Lexeme}", parentName: stmt.Name.Lexeme));
        }

        return null;
    }

    public object? VisitVarDeclStmt(VarDeclStmt stmt)
    {
        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Variable, stmt.Name.Span, stmt.Span, $"let {stmt.Name.Lexeme}"));
        stmt.Initializer?.Accept(this);
        return null;
    }

    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Constant, stmt.Name.Span, stmt.Span, $"const {stmt.Name.Lexeme}"));
        stmt.Initializer.Accept(this);
        return null;
    }

    public object? VisitBlockStmt(BlockStmt stmt)
    {
        PushScope(ScopeKind.Block, stmt.Span);
        foreach (var s in stmt.Statements)
        {
            s.Accept(this);
        }

        PopScope();
        return null;
    }

    public object? VisitIfStmt(IfStmt stmt)
    {
        stmt.Condition.Accept(this);
        stmt.ThenBranch.Accept(this);
        stmt.ElseBranch?.Accept(this);
        return null;
    }

    public object? VisitWhileStmt(WhileStmt stmt)
    {
        stmt.Condition.Accept(this);
        PushScope(ScopeKind.Loop, stmt.Body.Span);
        foreach (var s in stmt.Body.Statements)
        {
            s.Accept(this);
        }

        PopScope();
        return null;
    }

    public object? VisitForInStmt(ForInStmt stmt)
    {
        stmt.Iterable.Accept(this);
        PushScope(ScopeKind.Loop, stmt.Body.Span);
        _currentScope.AddSymbol(new SymbolInfo(stmt.VariableName.Lexeme, SymbolKind.LoopVariable, stmt.VariableName.Span, detail: "loop variable"));
        foreach (var s in stmt.Body.Statements)
        {
            s.Accept(this);
        }

        PopScope();
        return null;
    }

    public object? VisitExprStmt(ExprStmt stmt)
    {
        stmt.Expression.Accept(this);
        return null;
    }

    public object? VisitReturnStmt(ReturnStmt stmt)
    {
        stmt.Value?.Accept(this);
        return null;
    }

    public object? VisitBreakStmt(BreakStmt stmt) => null;
    public object? VisitContinueStmt(ContinueStmt stmt) => null;

    public object? VisitImportStmt(ImportStmt stmt)
    {
        foreach (var name in stmt.Names)
        {
            _currentScope.AddSymbol(new SymbolInfo(name.Lexeme, SymbolKind.Variable, name.Span, detail: $"imported from {stmt.Path.Lexeme}"));
        }
        return null;
    }

    public object? VisitImportAsStmt(ImportAsStmt stmt)
    {
        _currentScope.AddSymbol(new SymbolInfo(stmt.Alias.Lexeme, SymbolKind.Namespace, stmt.Alias.Span, detail: $"namespace from {stmt.Path.Lexeme}"));
        return null;
    }

    // Expression visitors — just recurse, we don't collect declarations from expressions

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

    public object? VisitAssignExpr(AssignExpr expr)
    {
        expr.Value.Accept(this);
        return null;
    }

    public object? VisitCallExpr(CallExpr expr)
    {
        expr.Callee.Accept(this);
        foreach (var arg in expr.Arguments)
        {
            arg.Accept(this);
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

    public object? VisitCommandExpr(CommandExpr expr) => null;

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
}
