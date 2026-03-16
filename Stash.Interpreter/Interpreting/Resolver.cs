namespace Stash.Interpreting;

using System.Collections.Generic;
using Stash.Parsing.AST;
using Stash.Lexing;

/// <summary>
/// Static analysis pass that resolves variable references to their lexical scope distance.
/// Run after parsing, before interpretation. Each resolved variable is stored in a
/// side table (Dictionary&lt;Expr, int&gt;) keyed by the AST node, enabling O(1) lookups
/// at runtime instead of walking the scope chain.
/// </summary>
public class Resolver : IExprVisitor<object?>, IStmtVisitor<object?>
{
    private readonly Interpreter _interpreter;
    private readonly Stack<Dictionary<string, bool>> _scopes = new();

    private enum FunctionType { None, Function, Lambda }
    private FunctionType _currentFunction = FunctionType.None;

    public Resolver(Interpreter interpreter)
    {
        _interpreter = interpreter;
    }

    public void Resolve(List<Stmt> statements)
    {
        foreach (var statement in statements)
        {
            Resolve(statement);
        }
    }

    private void Resolve(Stmt stmt)
    {
        stmt.Accept(this);
    }

    private void Resolve(Expr expr)
    {
        expr.Accept(this);
    }

    private void BeginScope()
    {
        _scopes.Push(new Dictionary<string, bool>());
    }

    private void EndScope()
    {
        _scopes.Pop();
    }

    private void Declare(string name)
    {
        if (_scopes.Count == 0)
        {
            return;
        }

        _scopes.Peek()[name] = false;
    }

    private void Define(string name)
    {
        if (_scopes.Count == 0)
        {
            return;
        }

        _scopes.Peek()[name] = true;
    }

    private void ResolveLocal(Expr expr, string name)
    {
        int i = 0;
        foreach (var scope in _scopes)
        {
            if (scope.ContainsKey(name))
            {
                _interpreter.Resolve(expr, i);
                return;
            }
            i++;
        }
        // Not found in any local scope — must be global.
        // Leave unresolved; interpreter will fall back to Environment.Get().
    }

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

    public object? VisitBlockStmt(BlockStmt stmt)
    {
        BeginScope();
        Resolve(stmt.Statements);
        EndScope();
        return null;
    }

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

    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        Resolve(stmt.Initializer);
        Define(stmt.Name.Lexeme);
        return null;
    }

    public object? VisitDestructureStmt(DestructureStmt stmt)
    {
        foreach (Token name in stmt.Names)
        {
            Declare(name.Lexeme);
        }
        Resolve(stmt.Initializer);
        foreach (Token name in stmt.Names)
        {
            Define(name.Lexeme);
        }
        return null;
    }

    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);
        ResolveFunction(stmt.Parameters, stmt.DefaultValues, stmt.Body, FunctionType.Function);
        return null;
    }

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

    public object? VisitWhileStmt(WhileStmt stmt)
    {
        Resolve(stmt.Condition);
        Resolve(stmt.Body);
        return null;
    }

    public object? VisitForInStmt(ForInStmt stmt)
    {
        Resolve(stmt.Iterable);
        BeginScope();
        Declare(stmt.VariableName.Lexeme);
        Define(stmt.VariableName.Lexeme);
        Resolve(stmt.Body.Statements);
        EndScope();
        return null;
    }

    public object? VisitReturnStmt(ReturnStmt stmt)
    {
        if (stmt.Value is not null)
        {
            Resolve(stmt.Value);
        }
        return null;
    }

    public object? VisitBreakStmt(BreakStmt stmt) => null;
    public object? VisitContinueStmt(ContinueStmt stmt) => null;

    public object? VisitExprStmt(ExprStmt stmt)
    {
        Resolve(stmt.Expression);
        return null;
    }

    public object? VisitStructDeclStmt(StructDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);
        return null;
    }

    public object? VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);
        return null;
    }

    public object? VisitImportStmt(ImportStmt stmt)
    {
        foreach (var name in stmt.Names)
        {
            Declare(name.Lexeme);
            Define(name.Lexeme);
        }
        return null;
    }

    public object? VisitImportAsStmt(ImportAsStmt stmt)
    {
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
        Resolve(expr.Value);
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

    public object? VisitBinaryExpr(BinaryExpr expr)
    {
        Resolve(expr.Left);
        Resolve(expr.Right);
        return null;
    }

    public object? VisitUnaryExpr(UnaryExpr expr)
    {
        Resolve(expr.Right);
        return null;
    }

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

    public object? VisitCallExpr(CallExpr expr)
    {
        Resolve(expr.Callee);
        foreach (var arg in expr.Arguments)
        {
            Resolve(arg);
        }
        return null;
    }

    public object? VisitArrayExpr(ArrayExpr expr)
    {
        foreach (var element in expr.Elements)
        {
            Resolve(element);
        }
        return null;
    }

    public object? VisitIndexExpr(IndexExpr expr)
    {
        Resolve(expr.Object);
        Resolve(expr.Index);
        return null;
    }

    public object? VisitIndexAssignExpr(IndexAssignExpr expr)
    {
        Resolve(expr.Object);
        Resolve(expr.Index);
        Resolve(expr.Value);
        return null;
    }

    public object? VisitDotExpr(DotExpr expr)
    {
        Resolve(expr.Object);
        return null;
    }

    public object? VisitDotAssignExpr(DotAssignExpr expr)
    {
        Resolve(expr.Object);
        Resolve(expr.Value);
        return null;
    }

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

    public object? VisitLiteralExpr(LiteralExpr expr) => null;

    public object? VisitGroupingExpr(GroupingExpr expr)
    {
        Resolve(expr.Expression);
        return null;
    }

    public object? VisitTernaryExpr(TernaryExpr expr)
    {
        Resolve(expr.Condition);
        Resolve(expr.ThenBranch);
        Resolve(expr.ElseBranch);
        return null;
    }

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

    public object? VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        foreach (var part in expr.Parts)
        {
            Resolve(part);
        }
        return null;
    }

    public object? VisitCommandExpr(CommandExpr expr)
    {
        foreach (var part in expr.Parts)
        {
            Resolve(part);
        }
        return null;
    }

    public object? VisitPipeExpr(PipeExpr expr)
    {
        Resolve(expr.Left);
        Resolve(expr.Right);
        return null;
    }

    public object? VisitRedirectExpr(RedirectExpr expr)
    {
        Resolve(expr.Expression);
        Resolve(expr.Target);
        return null;
    }

    public object? VisitTryExpr(TryExpr expr)
    {
        Resolve(expr.Expression);
        return null;
    }

    public object? VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        Resolve(expr.Left);
        Resolve(expr.Right);
        return null;
    }

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
}
