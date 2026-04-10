namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// SA1107 — Emits a warning when the condition of an <c>if</c>, <c>while</c>, or <c>do-while</c>
/// statement is a constant expression (always truthy or always falsy).
/// </summary>
public sealed class NoConstantConditionRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1107;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(IfStmt),
        typeof(WhileStmt),
        typeof(DoWhileStmt),
    };

    public void Analyze(RuleContext context)
    {
        switch (context.Statement)
        {
            case IfStmt ifStmt:
                CheckCondition(ifStmt.Condition, "if", context);
                break;

            // Skip while(true) and do-while(true) — these are intentional infinite loop idioms.
            // ESLint and Biome also exempt loop conditions from constant-condition checks.
            case WhileStmt whileStmt when !IsLiteralTrue(whileStmt.Condition):
                CheckCondition(whileStmt.Condition, "while", context);
                break;

            case DoWhileStmt doWhileStmt when !IsLiteralTrue(doWhileStmt.Condition):
                CheckCondition(doWhileStmt.Condition, "while", context);
                break;
        }
    }

    private static bool IsLiteralTrue(Expr expr) =>
        expr is LiteralExpr lit && lit.Value is true;

    private static void CheckCondition(Expr condition, string stmtKind, RuleContext context)
    {
        bool? isConstantTruthy = EvaluateConstantTruthiness(condition);
        if (isConstantTruthy == null)
        {
            return;
        }

        string truthinessLabel = isConstantTruthy.Value ? "truthy" : "falsy";
        context.ReportDiagnostic(
            DiagnosticDescriptors.SA1107.CreateDiagnostic(condition.Span, stmtKind, truthinessLabel));
    }

    /// <summary>
    /// Returns <c>true</c> if the expression is always truthy, <c>false</c> if always falsy,
    /// or <c>null</c> if not a constant expression.
    /// </summary>
    private static bool? EvaluateConstantTruthiness(Expr expr)
    {
        switch (expr)
        {
            case LiteralExpr lit:
                return IsTruthy(lit.Value);

            case UnaryExpr unary when unary.Operator.Type == TokenType.Bang:
                bool? inner = EvaluateConstantTruthiness(unary.Right);
                return inner.HasValue ? !inner.Value : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Evaluates Stash truthiness: falsy values are <c>null</c>, <c>false</c>, <c>0</c>, <c>0.0</c>, and <c>""</c>.
    /// </summary>
    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        long l => l != 0L,
        double d => d != 0.0,
        string s => s.Length != 0,
        _ => true,
    };
}
