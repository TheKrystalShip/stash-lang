namespace Stash.Interpreting;

using System.Collections.Generic;
using Stash.Parsing.AST;
using Stash.Lexing;
using Stash.Runtime;

/// <summary>Static analysis pass that resolves variable references to their lexical scope distances, enabling O(1) variable lookup at runtime.</summary>
/// <remarks>Walks the AST before execution, computing the number of scope hops for each variable reference. Results are stored via <see cref="Interpreter.Resolve"/>.</remarks>
public class Resolver : IExprVisitor<object?>, IStmtVisitor<object?>
{
    /// <summary>The interpreter to register resolved scope distances into.</summary>
    private readonly Interpreter _interpreter;
    /// <summary>Stack of scope dictionaries tracking declared/defined variables at each nesting level.</summary>
    private readonly Stack<ScopeInfo> _scopes = new();

    private class ScopeInfo
    {
        public readonly Dictionary<string, (bool Initialized, int Slot)> Variables = new();
        public int NextSlot;
    }

    /// <summary>Tracks the kind of function currently being resolved.</summary>
    private enum FunctionType
    {
        /// <summary>Not inside any function.</summary>
        None,
        /// <summary>Inside a named function.</summary>
        Function,
        /// <summary>Inside a lambda.</summary>
        Lambda,
        /// <summary>Inside a struct method.</summary>
        Method
    }
    /// <summary>Tracks the current function context for validating return statement placement.</summary>
    private FunctionType _currentFunction = FunctionType.None;

    /// <summary>Creates a resolver that registers results in the given interpreter.</summary>
    /// <param name="interpreter">The interpreter instance to receive resolved scope distances.</param>
    public Resolver(Interpreter interpreter)
    {
        _interpreter = interpreter;
    }

    /// <summary>Resolves all variable references in the given statements.</summary>
    public void Resolve(List<Stmt> statements)
    {
        foreach (var statement in statements)
        {
            Resolve(statement);
        }
    }

    /// <summary>Resolves variable references within a single statement.</summary>
    private void Resolve(Stmt stmt)
    {
        stmt.Accept(this);
    }

    /// <summary>Resolves variable references within a single expression.</summary>
    private void Resolve(Expr expr)
    {
        expr.Accept(this);
    }

    /// <summary>Pushes a new scope onto the scope stack.</summary>
    private void BeginScope()
    {
        _scopes.Push(new ScopeInfo());
    }

    /// <summary>Pops the current scope from the scope stack.</summary>
    private void EndScope()
    {
        _scopes.Pop();
    }

    /// <summary>Declares a variable in the current scope as not yet initialized, assigning it the next slot index.</summary>
    private void Declare(string name)
    {
        if (_scopes.Count == 0)
        {
            return;
        }

        var scope = _scopes.Peek();
        scope.Variables[name] = (false, scope.NextSlot++);
    }

    /// <summary>Marks a variable in the current scope as fully initialized, preserving its slot index.</summary>
    private void Define(string name)
    {
        if (_scopes.Count == 0)
        {
            return;
        }

        var scope = _scopes.Peek();
        if (scope.Variables.TryGetValue(name, out var info))
        {
            scope.Variables[name] = (true, info.Slot);
        }
    }

    /// <summary>Walks the scope stack to find the distance to a variable's declaration and registers it in the interpreter.</summary>
    private void ResolveLocal(Expr expr, string name)
    {
        int i = 0;
        foreach (var scope in _scopes)
        {
            if (scope.Variables.TryGetValue(name, out var info))
            {
                _interpreter.Resolve(expr, i, info.Slot);
                return;
            }
            i++;
        }
        // Not found in any local scope — must be global.
        // Leave unresolved; interpreter will fall back to Environment.Get().
    }

    /// <summary>Resolves a function body: creates a scope, declares parameters, and resolves body statements.</summary>
    private void ResolveFunction(List<Token> parameters, List<Expr?> defaultValues, BlockStmt body, FunctionType type)
    {
        FunctionType enclosingFunction = _currentFunction;
        _currentFunction = type;

        foreach (var defaultVal in defaultValues)
        {
            if (defaultVal != null)
            {
                Resolve(defaultVal);
            }
        }

        BeginScope();
        foreach (var param in parameters)
        {
            Declare(param.Lexeme);
            Define(param.Lexeme);
        }
        Resolve(body.Statements);
        EndScope();

        _currentFunction = enclosingFunction;
    }

    // --- Statement visitors ---

    /// <inheritdoc />
    public object? VisitBlockStmt(BlockStmt stmt)
    {
        BeginScope();
        Resolve(stmt.Statements);
        EndScope();
        return null;
    }

    /// <inheritdoc />
    public object? VisitVarDeclStmt(VarDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        if (stmt.Initializer is not null)
        {
            Resolve(stmt.Initializer);
        }
        Define(stmt.Name.Lexeme);
        return null;
    }

    /// <inheritdoc />
    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        Resolve(stmt.Initializer);
        Define(stmt.Name.Lexeme);
        return null;
    }

    /// <inheritdoc />
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
        Resolve(stmt.Initializer);
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

    /// <inheritdoc />
    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);
        ResolveFunction(stmt.Parameters, stmt.DefaultValues, stmt.Body, FunctionType.Function);
        return null;
    }

    /// <inheritdoc />
    public object? VisitIfStmt(IfStmt stmt)
    {
        Resolve(stmt.Condition);
        Resolve(stmt.ThenBranch);
        if (stmt.ElseBranch is not null)
        {
            Resolve(stmt.ElseBranch);
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitWhileStmt(WhileStmt stmt)
    {
        Resolve(stmt.Condition);
        Resolve(stmt.Body);
        return null;
    }

    /// <inheritdoc />
    public object? VisitDoWhileStmt(DoWhileStmt stmt)
    {
        Resolve(stmt.Body);
        Resolve(stmt.Condition);
        return null;
    }

    /// <inheritdoc />
    public object? VisitForStmt(ForStmt stmt)
    {
        BeginScope();
        if (stmt.Initializer is not null)
        {
            stmt.Initializer.Accept(this);
        }
        if (stmt.Condition is not null)
        {
            Resolve(stmt.Condition);
        }
        if (stmt.Increment is not null)
        {
            Resolve(stmt.Increment);
        }
        Resolve(stmt.Body);
        EndScope();
        return null;
    }

    /// <inheritdoc />
    public object? VisitForInStmt(ForInStmt stmt)
    {
        Resolve(stmt.Iterable);
        BeginScope();
        if (stmt.IndexName is not null)
        {
            Declare(stmt.IndexName.Lexeme);
            Define(stmt.IndexName.Lexeme);
        }
        Declare(stmt.VariableName.Lexeme);
        Define(stmt.VariableName.Lexeme);
        Resolve(stmt.Body.Statements);
        EndScope();
        return null;
    }

    /// <inheritdoc />
    public object? VisitReturnStmt(ReturnStmt stmt)
    {
        if (stmt.Value is not null)
        {
            Resolve(stmt.Value);
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitThrowStmt(ThrowStmt stmt)
    {
        Resolve(stmt.Value);
        return null;
    }

    /// <inheritdoc />
    public object? VisitBreakStmt(BreakStmt stmt) => null;
    /// <inheritdoc />
    public object? VisitContinueStmt(ContinueStmt stmt) => null;

    /// <inheritdoc />
    public object? VisitExprStmt(ExprStmt stmt)
    {
        Resolve(stmt.Expression);
        return null;
    }

    /// <inheritdoc />
    public object? VisitStructDeclStmt(StructDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);

        foreach (var method in stmt.Methods)
        {
            BeginScope();
            Declare("self");
            Define("self");
            ResolveFunction(method.Parameters, method.DefaultValues, method.Body, FunctionType.Method);
            EndScope();
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitExtendStmt(ExtendStmt stmt)
    {
        // extend blocks must be declared at the top level (not inside functions, if-blocks, etc.)
        if (_scopes.Count > 0)
        {
            throw new RuntimeError("'extend' blocks must be declared at the top level.", stmt.Span);
        }

        foreach (var method in stmt.Methods)
        {
            BeginScope();
            Declare("self");
            Define("self");
            ResolveFunction(method.Parameters, method.DefaultValues, method.Body, FunctionType.Method);
            EndScope();
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);
        return null;
    }

    /// <inheritdoc />
    public object? VisitInterfaceDeclStmt(InterfaceDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);
        return null;
    }

    /// <inheritdoc />
    public object? VisitElevateStmt(ElevateStmt stmt)
    {
        if (stmt.Elevator is not null)
            Resolve(stmt.Elevator);
        Resolve(stmt.Body);
        return null;
    }

    /// <inheritdoc />
    public object? VisitTryCatchStmt(TryCatchStmt stmt)
    {
        Resolve(stmt.TryBody);
        if (stmt.CatchBody is not null)
        {
            BeginScope();
            Declare(stmt.CatchVariable!.Lexeme);
            Define(stmt.CatchVariable.Lexeme);
            Resolve(stmt.CatchBody.Statements);
            EndScope();
        }
        if (stmt.FinallyBody is not null)
            Resolve(stmt.FinallyBody);
        return null;
    }

    /// <inheritdoc />
    public object? VisitImportStmt(ImportStmt stmt)
    {
        Resolve(stmt.Path);
        foreach (var name in stmt.Names)
        {
            Declare(name.Lexeme);
            Define(name.Lexeme);
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitImportAsStmt(ImportAsStmt stmt)
    {
        Resolve(stmt.Path);
        Declare(stmt.Alias.Lexeme);
        Define(stmt.Alias.Lexeme);
        return null;
    }

    // --- Expression visitors ---

    /// <inheritdoc />
    public object? VisitIdentifierExpr(IdentifierExpr expr)
    {
        ResolveLocal(expr, expr.Name.Lexeme);
        return null;
    }

    /// <inheritdoc />
    public object? VisitAssignExpr(AssignExpr expr)
    {
        Resolve(expr.Value);
        ResolveLocal(expr, expr.Name.Lexeme);
        return null;
    }

    /// <inheritdoc />
    public object? VisitLambdaExpr(LambdaExpr expr)
    {
        FunctionType enclosingFunction = _currentFunction;
        _currentFunction = FunctionType.Lambda;

        foreach (var defaultVal in expr.DefaultValues)
        {
            if (defaultVal != null)
            {
                Resolve(defaultVal);
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
            Resolve(expr.ExpressionBody);
        }
        else if (expr.BlockBody is not null)
        {
            Resolve(expr.BlockBody.Statements);
        }
        EndScope();

        _currentFunction = enclosingFunction;
        return null;
    }

    /// <inheritdoc />
    public object? VisitBinaryExpr(BinaryExpr expr)
    {
        Resolve(expr.Left);
        Resolve(expr.Right);
        return null;
    }

    /// <inheritdoc />
    public object? VisitIsExpr(IsExpr expr)
    {
        expr.Left.Accept(this);
        if (expr.TypeExpr != null)
        {
            expr.TypeExpr.Accept(this);
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitUnaryExpr(UnaryExpr expr)
    {
        Resolve(expr.Right);
        return null;
    }

    /// <inheritdoc />
    public object? VisitUpdateExpr(UpdateExpr expr)
    {
        Resolve(expr.Operand);
        // If the operand is an identifier, resolve it for the write-back
        if (expr.Operand is IdentifierExpr id)
        {
            ResolveLocal(expr, id.Name.Lexeme);
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitCallExpr(CallExpr expr)
    {
        Resolve(expr.Callee);
        foreach (var arg in expr.Arguments)
        {
            Resolve(arg);
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitArrayExpr(ArrayExpr expr)
    {
        foreach (var element in expr.Elements)
        {
            Resolve(element);
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitDictLiteralExpr(DictLiteralExpr expr)
    {
        foreach (var (_, value) in expr.Entries)
        {
            Resolve(value);
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitIndexExpr(IndexExpr expr)
    {
        Resolve(expr.Object);
        Resolve(expr.Index);
        return null;
    }

    /// <inheritdoc />
    public object? VisitIndexAssignExpr(IndexAssignExpr expr)
    {
        Resolve(expr.Object);
        Resolve(expr.Index);
        Resolve(expr.Value);
        return null;
    }

    /// <inheritdoc />
    public object? VisitDotExpr(DotExpr expr)
    {
        Resolve(expr.Object);
        return null;
    }

    /// <inheritdoc />
    public object? VisitDotAssignExpr(DotAssignExpr expr)
    {
        Resolve(expr.Object);
        Resolve(expr.Value);
        return null;
    }

    /// <inheritdoc />
    public object? VisitStructInitExpr(StructInitExpr expr)
    {
        if (expr.Target is not null)
        {
            Resolve(expr.Target);
        }
        foreach (var (_, value) in expr.FieldValues)
        {
            Resolve(value);
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitLiteralExpr(LiteralExpr expr) => null;

    /// <inheritdoc />
    public object? VisitGroupingExpr(GroupingExpr expr)
    {
        Resolve(expr.Expression);
        return null;
    }

    /// <inheritdoc />
    public object? VisitTernaryExpr(TernaryExpr expr)
    {
        Resolve(expr.Condition);
        Resolve(expr.ThenBranch);
        Resolve(expr.ElseBranch);
        return null;
    }

    /// <inheritdoc />
    public object? VisitRangeExpr(RangeExpr expr)
    {
        Resolve(expr.Start);
        Resolve(expr.End);
        if (expr.Step is not null)
        {
            Resolve(expr.Step);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        foreach (var part in expr.Parts)
        {
            Resolve(part);
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitCommandExpr(CommandExpr expr)
    {
        foreach (var part in expr.Parts)
        {
            Resolve(part);
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitPipeExpr(PipeExpr expr)
    {
        Resolve(expr.Left);
        Resolve(expr.Right);
        return null;
    }

    /// <inheritdoc />
    public object? VisitRedirectExpr(RedirectExpr expr)
    {
        Resolve(expr.Expression);
        Resolve(expr.Target);
        return null;
    }

    /// <inheritdoc />
    public object? VisitTryExpr(TryExpr expr)
    {
        Resolve(expr.Expression);
        return null;
    }

    /// <inheritdoc />
    public object? VisitAwaitExpr(AwaitExpr expr)
    {
        Resolve(expr.Expression);
        return null;
    }

    /// <inheritdoc />
    public object? VisitRetryExpr(RetryExpr expr)
    {
        Resolve(expr.MaxAttempts);
        expr.OptionsExpr?.Accept(this);
        if (expr.NamedOptions is not null)
            foreach (var (_, value) in expr.NamedOptions)
                Resolve(value);
        if (expr.UntilClause is not null)
            Resolve(expr.UntilClause);
        if (expr.OnRetryClause is { IsReference: true, Reference: not null })
            Resolve(expr.OnRetryClause.Reference);
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
                Resolve(stmt);
            EndScope();
        }
        BeginScope();
        Declare("attempt");
        Define("attempt");
        foreach (var stmt in expr.Body.Statements)
            Resolve(stmt);
        EndScope();
        return null;
    }

    /// <inheritdoc />
    public object? VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        Resolve(expr.Left);
        Resolve(expr.Right);
        return null;
    }

    /// <inheritdoc />
    public object? VisitSwitchExpr(SwitchExpr expr)
    {
        Resolve(expr.Subject);
        foreach (var arm in expr.Arms)
        {
            if (arm.Pattern is not null)
            {
                Resolve(arm.Pattern);
            }
            Resolve(arm.Body);
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitSpreadExpr(SpreadExpr expr)
    {
        Resolve(expr.Expression);
        return null;
    }
}
