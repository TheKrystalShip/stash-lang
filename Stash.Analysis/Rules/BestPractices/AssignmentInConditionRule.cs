namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA1109 — Warns when an assignment expression is used as the condition of an
/// <c>if</c>, <c>while</c>, <c>do-while</c>, or <c>for</c> statement.
/// </summary>
/// <remarks>
/// Using assignment inside a condition is almost always a typo — the programmer likely meant
/// equality comparison (<c>==</c>) instead of assignment (<c>=</c>). Wrapping the assignment
/// in parentheses is still flagged, since Stash does not use the <c>(x = y)</c> idiom.
/// </remarks>
public sealed class AssignmentInConditionRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1109;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(IfStmt),
        typeof(WhileStmt),
        typeof(DoWhileStmt),
        typeof(ForStmt),
    };

    public void Analyze(RuleContext context)
    {
        Expr? condition = context.Statement switch
        {
            IfStmt ifStmt => ifStmt.Condition,
            WhileStmt whileStmt => whileStmt.Condition,
            DoWhileStmt doWhile => doWhile.Condition,
            ForStmt forStmt => forStmt.Condition,
            _ => null
        };

        if (condition == null) return;

        var unwrapped = StripGrouping(condition);
        if (unwrapped is AssignExpr)
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA1109.CreateDiagnostic(unwrapped.Span));
        }
    }

    private static Expr StripGrouping(Expr expr)
    {
        while (expr is GroupingExpr group)
            expr = group.Expression;
        return expr;
    }
}
