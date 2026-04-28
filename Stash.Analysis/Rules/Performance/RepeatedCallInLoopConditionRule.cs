namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Parsing.AST;

/// <summary>
/// SA1203 — Reports an informational diagnostic when a loop condition calls a method
/// (<c>len</c>, <c>size</c>, or <c>keys</c>) on a variable that is not mutated inside the loop body.
/// </summary>
/// <remarks>
/// These methods recompute their result on every call. Caching the return value before the
/// loop avoids the per-iteration overhead: <c>let n = items.len();</c>.
/// </remarks>
public sealed class RepeatedCallInLoopConditionRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1203;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(WhileStmt),
        typeof(DoWhileStmt),
        typeof(ForStmt),
    };

    private static readonly HashSet<string> CacheableMethods = new(StringComparer.Ordinal)
    {
        "len", "size", "keys"
    };

    public void Analyze(RuleContext context)
    {
        Expr? condition;
        IReadOnlyList<Stmt> bodyStatements;

        switch (context.Statement)
        {
            case WhileStmt w:
                condition = w.Condition;
                bodyStatements = w.Body.Statements;
                break;
            case DoWhileStmt dw:
                condition = dw.Condition;
                bodyStatements = dw.Body.Statements;
                break;
            case ForStmt f:
                condition = f.Condition;
                bodyStatements = f.Body.Statements;
                break;
            default:
                return;
        }

        if (condition == null) return;

        var calls = new List<(string Receiver, string Method, Expr CallExpr)>();
        FindCacheableCalls(condition, calls);

        if (calls.Count == 0) return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (receiver, method, callExpr) in calls)
        {
            string key = $"{receiver}.{method}";
            if (!seen.Add(key)) continue;

            if (!IsReceiverMutatedInBody(receiver, bodyStatements))
            {
                context.ReportDiagnostic(
                    DiagnosticDescriptors.SA1203.CreateDiagnostic(callExpr.Span, receiver, method));
            }
        }
    }

    private static void FindCacheableCalls(Expr expr, List<(string, string, Expr)> result)
    {
        if (expr is CallExpr call && call.Callee is DotExpr dot &&
            dot.Object is IdentifierExpr id && CacheableMethods.Contains(dot.Name.Lexeme))
        {
            result.Add((id.Name.Lexeme, dot.Name.Lexeme, call));
        }

        // Recurse into sub-expressions
        switch (expr)
        {
            case BinaryExpr bin:
                FindCacheableCalls(bin.Left, result);
                FindCacheableCalls(bin.Right, result);
                break;
            case CallExpr c:
                FindCacheableCalls(c.Callee, result);
                foreach (var arg in c.Arguments) FindCacheableCalls(arg, result);
                break;
            case DotExpr d:
                FindCacheableCalls(d.Object, result);
                break;
            case GroupingExpr g:
                FindCacheableCalls(g.Expression, result);
                break;
            case UnaryExpr u:
                FindCacheableCalls(u.Right, result);
                break;
        }
    }

    private static bool IsReceiverMutatedInBody(string receiverName, IReadOnlyList<Stmt> stmts)
    {
        foreach (var stmt in stmts)
        {
            if (IsStmtMutating(stmt, receiverName)) return true;
        }
        return false;
    }

    private static bool IsStmtMutating(Stmt stmt, string name) => stmt switch
    {
        ExprStmt exprStmt => IsExprMutating(exprStmt.Expression, name),
        BlockStmt block => block.Statements.Any(s => IsStmtMutating(s, name)),
        IfStmt ifStmt => IsStmtMutating(ifStmt.ThenBranch, name) ||
                         (ifStmt.ElseBranch != null && IsStmtMutating(ifStmt.ElseBranch, name)),
        _ => false
    };

    private static bool IsExprMutating(Expr expr, string name) => expr switch
    {
        // Direct assignment: name = ...
        AssignExpr assign => assign.Name.Lexeme == name,
        // Any method call on the receiver may mutate it (conservative)
        CallExpr call when call.Callee is DotExpr dot &&
                           dot.Object is IdentifierExpr id &&
                           id.Name.Lexeme == name => true,
        BinaryExpr bin => IsExprMutating(bin.Left, name) || IsExprMutating(bin.Right, name),
        _ => false
    };
}
