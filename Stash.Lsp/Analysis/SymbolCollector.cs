namespace Stash.Lsp.Analysis;

using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Parsing.AST;

public class SymbolCollector : IStmtVisitor<object?>, IExprVisitor<object?>
{
    private Scope _currentScope = null!;
    private readonly List<ReferenceInfo> _references = new();
    public bool IncludeBuiltIns { get; set; } = true;

    public ScopeTree Collect(List<Stmt> statements)
    {
        _references.Clear();
        var file = statements.Count > 0 ? statements[0].Span.File : "";
        var globalSpan = new SourceSpan(file, 1, 1, int.MaxValue, int.MaxValue);
        var globalScope = new Scope(ScopeKind.Global, null, globalSpan);
        _currentScope = globalScope;

        if (IncludeBuiltIns)
        {
            RegisterBuiltIns(file);
        }

        foreach (var stmt in statements)
        {
            stmt.Accept(this);
        }

        return new ScopeTree(globalScope, _references);
    }

    private void RegisterBuiltIns(string file)
    {
        var span = new SourceSpan(file, 0, 0, 0, 0);

        foreach (var s in BuiltInRegistry.Structs)
        {
            _currentScope.AddSymbol(new SymbolInfo(s.Name, SymbolKind.Struct, span, span, s.Detail));
            foreach (var f in s.Fields)
            {
                var fieldDetail = f.Type != null ? $"field of {s.Name}: {f.Type}" : $"field of {s.Name}";
                _currentScope.AddSymbol(new SymbolInfo(f.Name, SymbolKind.Field, span, detail: fieldDetail, parentName: s.Name, typeHint: f.Type));
            }
        }

        foreach (var fn in BuiltInRegistry.Functions)
        {
            _currentScope.AddSymbol(new SymbolInfo(fn.Name, SymbolKind.Function, span, span, fn.Detail, typeHint: fn.ReturnType, parameterNames: fn.ParamNames));
        }

        foreach (var ns in BuiltInRegistry.NamespaceNames)
        {
            _currentScope.AddSymbol(new SymbolInfo(ns, SymbolKind.Namespace, span, detail: $"namespace {ns}"));
        }
    }

    private void RecordReference(string name, SourceSpan span, ReferenceKind kind)
    {
        var resolved = FindSymbolInScopeChain(name, span.StartLine, span.StartColumn);
        _references.Add(new ReferenceInfo(name, span, kind, resolved));
    }

    private SymbolInfo? FindSymbolInScopeChain(string name, int line, int column)
    {
        var scope = _currentScope;
        while (scope != null)
        {
            for (int i = scope.Symbols.Count - 1; i >= 0; i--)
            {
                var sym = scope.Symbols[i];
                if (sym.Name == name && (sym.Span.StartLine < line || (sym.Span.StartLine == line && sym.Span.StartColumn <= column)))
                {
                    return sym;
                }
            }
            scope = scope.Parent;
        }
        return null;
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
        var paramParts = new List<string>();
        for (int i = 0; i < stmt.Parameters.Count; i++)
        {
            var paramName = stmt.Parameters[i].Lexeme;
            var paramType = i < stmt.ParameterTypes.Count ? stmt.ParameterTypes[i]?.Lexeme : null;
            paramParts.Add(paramType != null ? $"{paramName}: {paramType}" : paramName);
        }

        var detail = $"fn {stmt.Name.Lexeme}({string.Join(", ", paramParts)})";
        if (stmt.ReturnType != null)
        {
            detail += $" -> {stmt.ReturnType.Lexeme}";
        }

        var returnTypeStr = stmt.ReturnType?.Lexeme;
        var paramNames = stmt.Parameters.Select(p => p.Lexeme).ToArray();

        // Function name goes into the parent (current) scope
        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Function, stmt.Name.Span, stmt.Span, detail, typeHint: returnTypeStr, parameterNames: paramNames));

        // Parameters and body statements share the function scope
        PushScope(ScopeKind.Function, stmt.Body.Span);
        for (int i = 0; i < stmt.Parameters.Count; i++)
        {
            var param = stmt.Parameters[i];
            var paramType = i < stmt.ParameterTypes.Count ? stmt.ParameterTypes[i]?.Lexeme : null;
            var paramDetail = paramType != null ? $"parameter of {stmt.Name.Lexeme}: {paramType}" : $"parameter of {stmt.Name.Lexeme}";
            _currentScope.AddSymbol(new SymbolInfo(param.Lexeme, SymbolKind.Parameter, param.Span, detail: paramDetail, parentName: stmt.Name.Lexeme, typeHint: paramType));
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
        var fieldParts = new List<string>();
        for (int i = 0; i < stmt.Fields.Count; i++)
        {
            var fieldName = stmt.Fields[i].Lexeme;
            var fieldType = i < stmt.FieldTypes.Count ? stmt.FieldTypes[i]?.Lexeme : null;
            fieldParts.Add(fieldType != null ? $"{fieldName}: {fieldType}" : fieldName);
        }

        var detail = $"struct {stmt.Name.Lexeme} {{ {string.Join(", ", fieldParts)} }}";

        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Struct, stmt.Name.Span, stmt.Span, detail));

        for (int i = 0; i < stmt.Fields.Count; i++)
        {
            var field = stmt.Fields[i];
            var fieldType = i < stmt.FieldTypes.Count ? stmt.FieldTypes[i]?.Lexeme : null;
            var fieldDetail = fieldType != null ? $"field of {stmt.Name.Lexeme}: {fieldType}" : $"field of {stmt.Name.Lexeme}";
            _currentScope.AddSymbol(new SymbolInfo(field.Lexeme, SymbolKind.Field, field.Span, detail: fieldDetail, parentName: stmt.Name.Lexeme, typeHint: fieldType));
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
        var typeStr = stmt.TypeHint?.Lexeme;
        var detail = typeStr != null ? $"let {stmt.Name.Lexeme}: {typeStr}" : $"let {stmt.Name.Lexeme}";
        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Variable, stmt.Name.Span, stmt.Span, detail, typeHint: typeStr));
        stmt.Initializer?.Accept(this);
        return null;
    }

    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        var typeStr = stmt.TypeHint?.Lexeme;
        var detail = typeStr != null ? $"const {stmt.Name.Lexeme}: {typeStr}" : $"const {stmt.Name.Lexeme}";
        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Constant, stmt.Name.Span, stmt.Span, detail, typeHint: typeStr));
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
        var typeStr = stmt.TypeHint?.Lexeme;
        var detail = typeStr != null ? $"loop variable: {typeStr}" : "loop variable";
        _currentScope.AddSymbol(new SymbolInfo(stmt.VariableName.Lexeme, SymbolKind.LoopVariable, stmt.VariableName.Span, detail: detail, typeHint: typeStr));
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

    public object? VisitIdentifierExpr(IdentifierExpr expr)
    {
        RecordReference(expr.Name.Lexeme, expr.Span, ReferenceKind.Read);
        return null;
    }

    public object? VisitUnaryExpr(UnaryExpr expr)
    {
        expr.Right.Accept(this);
        return null;
    }

    public object? VisitUpdateExpr(UpdateExpr expr)
    {
        if (expr.Operand is IdentifierExpr id)
        {
            RecordReference(id.Name.Lexeme, id.Span, ReferenceKind.Write);
        }
        else
        {
            expr.Operand.Accept(this);
        }
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

    public object? VisitAssignExpr(AssignExpr expr)
    {
        RecordReference(expr.Name.Lexeme, expr.Name.Span, ReferenceKind.Write);
        expr.Value.Accept(this);
        return null;
    }

    public object? VisitCallExpr(CallExpr expr)
    {
        if (expr.Callee is IdentifierExpr id)
        {
            RecordReference(id.Name.Lexeme, id.Span, ReferenceKind.Call);
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
        RecordReference(expr.Name.Lexeme, expr.Name.Span, ReferenceKind.TypeUse);
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

    public object? VisitLambdaExpr(LambdaExpr expr)
    {
        var bodySpan = expr.ExpressionBody?.Span ?? expr.BlockBody!.Span;
        PushScope(ScopeKind.Function, bodySpan);

        for (int i = 0; i < expr.Parameters.Count; i++)
        {
            var param = expr.Parameters[i];
            var paramType = i < expr.ParameterTypes.Count ? expr.ParameterTypes[i]?.Lexeme : null;
            var paramDetail = paramType != null ? $"parameter: {paramType}" : "parameter";
            _currentScope.AddSymbol(new SymbolInfo(param.Lexeme, SymbolKind.Parameter, param.Span, detail: paramDetail, parentName: "<lambda>", typeHint: paramType));
        }

        if (expr.ExpressionBody != null)
        {
            expr.ExpressionBody.Accept(this);
        }
        else if (expr.BlockBody != null)
        {
            foreach (var s in expr.BlockBody.Statements)
            {
                s.Accept(this);
            }
        }

        PopScope();
        return null;
    }
}
