namespace Stash.Resolution;

using System.Collections.Generic;
using Stash.Parsing.AST;
using Stash.Lexing;
using Stash.Runtime;

/// <summary>Static analysis pass that resolves variable references to their lexical scope distances, enabling O(1) variable lookup at runtime.</summary>
/// <remarks>Walks the AST before execution, computing the number of scope hops for each variable reference. Results are stored directly on AST nodes via <see cref="Expr.ResolvedDistance"/> and <see cref="Expr.ResolvedSlot"/>.</remarks>
public class SemanticResolver : IExprVisitor<object?>, IStmtVisitor<object?>
{
    private readonly Stack<ScopeInfo> _scopes = new();

    private class ScopeInfo
    {
        public readonly Dictionary<string, (bool Initialized, int Slot)> Variables = new();
        public int NextSlot;
    }

    private enum FunctionType
    {
        None,
        Function,
        Lambda,
        Method
    }

    private FunctionType _currentFunction = FunctionType.None;

    private SemanticResolver() { }

    /// <summary>Resolves all variable references in the given statements, annotating AST nodes with scope distances and slot indices.</summary>
    public static void Resolve(List<Stmt> statements)
    {
        var resolver = new SemanticResolver();
        resolver.ResolveAll(statements);
    }

    private void ResolveAll(List<Stmt> statements)
    {
        foreach (var statement in statements)
        {
            ResolveStmt(statement);
        }
    }

    private void ResolveStmt(Stmt stmt)
    {
        stmt.Accept(this);
    }

    private void ResolveExpr(Expr expr)
    {
        expr.Accept(this);
    }

    private void BeginScope()
    {
        _scopes.Push(new ScopeInfo());
    }

    private void EndScope()
    {
        _scopes.Pop();
    }

    private void Declare(string name)
    {
        if (_scopes.Count == 0)
            return;

        var scope = _scopes.Peek();
        scope.Variables[name] = (false, scope.NextSlot++);
    }

    private void Define(string name)
    {
        if (_scopes.Count == 0)
            return;

        var scope = _scopes.Peek();
        if (scope.Variables.TryGetValue(name, out var info))
        {
            scope.Variables[name] = (true, info.Slot);
        }
    }

    private void ResolveLocal(Expr expr, string name)
    {
        int i = 0;
        foreach (var scope in _scopes)
        {
            if (scope.Variables.TryGetValue(name, out var info))
            {
                expr.ResolvedDistance = i;
                expr.ResolvedSlot = info.Slot;
                return;
            }
            i++;
        }
        // Not found in any local scope — must be global.
    }

    private int ResolveFunction(List<Token> parameters, List<Expr?> defaultValues, BlockStmt body, FunctionType type)
    {
        FunctionType enclosingFunction = _currentFunction;
        _currentFunction = type;

        foreach (var defaultVal in defaultValues)
        {
            if (defaultVal != null)
            {
                ResolveExpr(defaultVal);
            }
        }

        BeginScope();
        foreach (var param in parameters)
        {
            Declare(param.Lexeme);
            Define(param.Lexeme);
        }
        ResolveAll(body.Statements);
        int localCount = _scopes.Peek().NextSlot;
        EndScope();

        _currentFunction = enclosingFunction;
        return localCount;
    }

    // --- Statement visitors ---

    public object? VisitBlockStmt(BlockStmt stmt)
    {
        BeginScope();
        ResolveAll(stmt.Statements);
        EndScope();
        return null;
    }

    public object? VisitVarDeclStmt(VarDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        if (stmt.Initializer is not null)
        {
            ResolveExpr(stmt.Initializer);
        }
        Define(stmt.Name.Lexeme);
        return null;
    }

    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        ResolveExpr(stmt.Initializer);
        Define(stmt.Name.Lexeme);
        return null;
    }

    public object? VisitDestructureStmt(DestructureStmt stmt)
    {
        foreach (Token name in stmt.Names)
        {
            Declare(name.Lexeme);
        }
        if (stmt.RestName is Token restName)
        {
            Declare(restName.Lexeme);
        }
        ResolveExpr(stmt.Initializer);
        foreach (Token name in stmt.Names)
        {
            Define(name.Lexeme);
        }
        if (stmt.RestName is Token restDef)
        {
            Define(restDef.Lexeme);
        }
        return null;
    }

    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);
        stmt.ResolvedLocalCount = ResolveFunction(stmt.Parameters, stmt.DefaultValues, stmt.Body, FunctionType.Function);
        return null;
    }

    public object? VisitIfStmt(IfStmt stmt)
    {
        ResolveExpr(stmt.Condition);
        ResolveStmt(stmt.ThenBranch);
        if (stmt.ElseBranch is not null)
        {
            ResolveStmt(stmt.ElseBranch);
        }
        return null;
    }

    public object? VisitWhileStmt(WhileStmt stmt)
    {
        ResolveExpr(stmt.Condition);
        ResolveStmt(stmt.Body);
        return null;
    }

    public object? VisitDoWhileStmt(DoWhileStmt stmt)
    {
        ResolveStmt(stmt.Body);
        ResolveExpr(stmt.Condition);
        return null;
    }

    public object? VisitForStmt(ForStmt stmt)
    {
        BeginScope();
        if (stmt.Initializer is not null)
        {
            stmt.Initializer.Accept(this);
        }
        if (stmt.Condition is not null)
        {
            ResolveExpr(stmt.Condition);
        }
        if (stmt.Increment is not null)
        {
            ResolveExpr(stmt.Increment);
        }
        ResolveStmt(stmt.Body);
        EndScope();
        return null;
    }

    public object? VisitForInStmt(ForInStmt stmt)
    {
        ResolveExpr(stmt.Iterable);
        BeginScope();
        if (stmt.IndexName is not null)
        {
            Declare(stmt.IndexName.Lexeme);
            Define(stmt.IndexName.Lexeme);
        }
        Declare(stmt.VariableName.Lexeme);
        Define(stmt.VariableName.Lexeme);
        ResolveAll(stmt.Body.Statements);
        EndScope();
        return null;
    }

    public object? VisitReturnStmt(ReturnStmt stmt)
    {
        if (stmt.Value is not null)
        {
            ResolveExpr(stmt.Value);
        }
        return null;
    }

    public object? VisitThrowStmt(ThrowStmt stmt)
    {
        if (stmt.Value is not null)
            ResolveExpr(stmt.Value);
        return null;
    }

    public object? VisitBreakStmt(BreakStmt stmt) => null;
    public object? VisitContinueStmt(ContinueStmt stmt) => null;

    public object? VisitDeferStmt(DeferStmt stmt)
    {
        stmt.Body.Accept(this);
        return null;
    }

    public object? VisitLockStmt(LockStmt stmt)
    {
        ResolveExpr(stmt.Path);
        if (stmt.WaitOption is not null)
            ResolveExpr(stmt.WaitOption);
        if (stmt.StaleOption is not null)
            ResolveExpr(stmt.StaleOption);
        ResolveStmt(stmt.Body);
        return null;
    }

    public object? VisitExprStmt(ExprStmt stmt)
    {
        ResolveExpr(stmt.Expression);
        return null;
    }

    public object? VisitStructDeclStmt(StructDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);

        foreach (var method in stmt.Methods)
        {
            BeginScope();
            Declare("self");
            Define("self");
            method.ResolvedLocalCount = ResolveFunction(method.Parameters, method.DefaultValues, method.Body, FunctionType.Method);
            EndScope();
        }

        return null;
    }

    public object? VisitExtendStmt(ExtendStmt stmt)
    {
        foreach (var method in stmt.Methods)
        {
            BeginScope();
            Declare("self");
            Define("self");
            method.ResolvedLocalCount = ResolveFunction(method.Parameters, method.DefaultValues, method.Body, FunctionType.Method);
            EndScope();
        }

        return null;
    }

    public object? VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);
        return null;
    }

    public object? VisitInterfaceDeclStmt(InterfaceDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);
        return null;
    }

    public object? VisitElevateStmt(ElevateStmt stmt)
    {
        if (stmt.Elevator is not null)
            ResolveExpr(stmt.Elevator);
        ResolveStmt(stmt.Body);
        return null;
    }

    public object? VisitTryCatchStmt(TryCatchStmt stmt)
    {
        ResolveStmt(stmt.TryBody);
        foreach (CatchClause clause in stmt.CatchClauses)
        {
            BeginScope();
            Declare(clause.Variable.Lexeme);
            Define(clause.Variable.Lexeme);
            ResolveAll(clause.Body.Statements);
            EndScope();
        }
        if (stmt.FinallyBody is not null)
            ResolveStmt(stmt.FinallyBody);
        return null;
    }

    public object? VisitSwitchStmt(SwitchStmt stmt)
    {
        ResolveExpr(stmt.Subject);
        foreach (SwitchCase @case in stmt.Cases)
        {
            foreach (Expr pattern in @case.Patterns)
            {
                ResolveExpr(pattern);
            }
            ResolveStmt(@case.Body);
        }
        return null;
    }

    public object? VisitImportStmt(ImportStmt stmt)
    {
        ResolveExpr(stmt.Path);
        foreach (var name in stmt.Names)
        {
            Declare(name.Lexeme);
            Define(name.Lexeme);
        }
        return null;
    }

    public object? VisitImportAsStmt(ImportAsStmt stmt)
    {
        ResolveExpr(stmt.Path);
        Declare(stmt.Alias.Lexeme);
        Define(stmt.Alias.Lexeme);
        return null;
    }

    // --- Expression visitors ---

    public object? VisitIdentifierExpr(IdentifierExpr expr)
    {
        ResolveLocal(expr, expr.Name.Lexeme);
        return null;
    }

    public object? VisitAssignExpr(AssignExpr expr)
    {
        ResolveExpr(expr.Value);
        ResolveLocal(expr, expr.Name.Lexeme);
        return null;
    }

    public object? VisitLambdaExpr(LambdaExpr expr)
    {
        FunctionType enclosingFunction = _currentFunction;
        _currentFunction = FunctionType.Lambda;

        foreach (var defaultVal in expr.DefaultValues)
        {
            if (defaultVal != null)
            {
                ResolveExpr(defaultVal);
            }
        }

        BeginScope();
        foreach (var param in expr.Parameters)
        {
            Declare(param.Lexeme);
            Define(param.Lexeme);
        }
        if (expr.ExpressionBody is not null)
        {
            ResolveExpr(expr.ExpressionBody);
        }
        else if (expr.BlockBody is not null)
        {
            ResolveAll(expr.BlockBody.Statements);
        }
        expr.ResolvedLocalCount = _scopes.Peek().NextSlot;
        EndScope();

        _currentFunction = enclosingFunction;
        return null;
    }

    public object? VisitBinaryExpr(BinaryExpr expr)
    {
        ResolveExpr(expr.Left);
        ResolveExpr(expr.Right);
        return null;
    }

    public object? VisitIsExpr(IsExpr expr)
    {
        expr.Left.Accept(this);
        if (expr.TypeExpr != null)
        {
            expr.TypeExpr.Accept(this);
        }
        return null;
    }

    public object? VisitUnaryExpr(UnaryExpr expr)
    {
        ResolveExpr(expr.Right);
        return null;
    }

    public object? VisitUpdateExpr(UpdateExpr expr)
    {
        ResolveExpr(expr.Operand);
        if (expr.Operand is IdentifierExpr id)
        {
            ResolveLocal(expr, id.Name.Lexeme);
        }
        return null;
    }

    public object? VisitCallExpr(CallExpr expr)
    {
        ResolveExpr(expr.Callee);
        foreach (var arg in expr.Arguments)
        {
            ResolveExpr(arg);
        }
        return null;
    }

    public object? VisitArrayExpr(ArrayExpr expr)
    {
        foreach (var element in expr.Elements)
        {
            ResolveExpr(element);
        }
        return null;
    }

    public object? VisitDictLiteralExpr(DictLiteralExpr expr)
    {
        foreach (var (_, value) in expr.Entries)
        {
            ResolveExpr(value);
        }
        return null;
    }

    public object? VisitIndexExpr(IndexExpr expr)
    {
        ResolveExpr(expr.Object);
        ResolveExpr(expr.Index);
        return null;
    }

    public object? VisitIndexAssignExpr(IndexAssignExpr expr)
    {
        ResolveExpr(expr.Object);
        ResolveExpr(expr.Index);
        ResolveExpr(expr.Value);
        return null;
    }

    public object? VisitDotExpr(DotExpr expr)
    {
        ResolveExpr(expr.Object);
        return null;
    }

    public object? VisitDotAssignExpr(DotAssignExpr expr)
    {
        ResolveExpr(expr.Object);
        ResolveExpr(expr.Value);
        return null;
    }

    public object? VisitStructInitExpr(StructInitExpr expr)
    {
        if (expr.Target is not null)
        {
            ResolveExpr(expr.Target);
        }
        foreach (var (_, value) in expr.FieldValues)
        {
            ResolveExpr(value);
        }
        return null;
    }

    public object? VisitLiteralExpr(LiteralExpr expr) => null;

    public object? VisitGroupingExpr(GroupingExpr expr)
    {
        ResolveExpr(expr.Expression);
        return null;
    }

    public object? VisitTernaryExpr(TernaryExpr expr)
    {
        ResolveExpr(expr.Condition);
        ResolveExpr(expr.ThenBranch);
        ResolveExpr(expr.ElseBranch);
        return null;
    }

    public object? VisitRangeExpr(RangeExpr expr)
    {
        ResolveExpr(expr.Start);
        ResolveExpr(expr.End);
        if (expr.Step is not null)
        {
            ResolveExpr(expr.Step);
        }
        return null;
    }

    public object? VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        foreach (var part in expr.Parts)
        {
            ResolveExpr(part);
        }
        return null;
    }

    public object? VisitCommandExpr(CommandExpr expr)
    {
        foreach (var part in expr.Parts)
        {
            ResolveExpr(part);
        }
        return null;
    }

    public object? VisitPipeExpr(PipeExpr expr)
    {
        ResolveExpr(expr.Left);
        ResolveExpr(expr.Right);
        return null;
    }

    public object? VisitRedirectExpr(RedirectExpr expr)
    {
        ResolveExpr(expr.Expression);
        ResolveExpr(expr.Target);
        return null;
    }

    public object? VisitTryExpr(TryExpr expr)
    {
        ResolveExpr(expr.Expression);
        return null;
    }

    public object? VisitAwaitExpr(AwaitExpr expr)
    {
        ResolveExpr(expr.Expression);
        return null;
    }

    public object? VisitRetryExpr(RetryExpr expr)
    {
        ResolveExpr(expr.MaxAttempts);
        expr.OptionsExpr?.Accept(this);
        if (expr.NamedOptions is not null)
            foreach (var (_, value) in expr.NamedOptions)
                ResolveExpr(value);
        if (expr.UntilClause is not null)
            ResolveExpr(expr.UntilClause);
        if (expr.OnRetryClause is { IsReference: true, Reference: not null })
            ResolveExpr(expr.OnRetryClause.Reference);
        else if (expr.OnRetryClause is { Body: not null } onRetry)
        {
            BeginScope();
            if (onRetry.ParamAttempt is not null)
            {
                Declare(onRetry.ParamAttempt.Lexeme);
                Define(onRetry.ParamAttempt.Lexeme);
            }
            if (onRetry.ParamError is not null)
            {
                Declare(onRetry.ParamError.Lexeme);
                Define(onRetry.ParamError.Lexeme);
            }
            foreach (var stmt in onRetry.Body.Statements)
                ResolveStmt(stmt);
            EndScope();
        }
        BeginScope();
        Declare("attempt");
        Define("attempt");
        foreach (var stmt in expr.Body.Statements)
            ResolveStmt(stmt);
        EndScope();
        return null;
    }

    public object? VisitTimeoutExpr(TimeoutExpr expr)
    {
        ResolveExpr(expr.Duration);
        BeginScope();
        foreach (var stmt in expr.Body.Statements)
            ResolveStmt(stmt);
        EndScope();
        return null;
    }

    public object? VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        ResolveExpr(expr.Left);
        ResolveExpr(expr.Right);
        return null;
    }

    public object? VisitSwitchExpr(SwitchExpr expr)
    {
        ResolveExpr(expr.Subject);
        foreach (var arm in expr.Arms)
        {
            if (arm.Pattern is not null)
            {
                ResolveExpr(arm.Pattern);
            }
            ResolveExpr(arm.Body);
        }
        return null;
    }

    public object? VisitSpreadExpr(SpreadExpr expr)
    {
        ResolveExpr(expr.Expression);
        return null;
    }
}
