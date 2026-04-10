namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// SA1401 — Suggests using optional chaining (<c>a?.b</c>) instead of a verbose null-check
/// ternary that accesses a member only when the receiver is non-null.
/// </summary>
/// <remarks>
/// Detects patterns:
/// <list type="bullet">
///   <item><c>a != null ? a.b : null</c> → suggest <c>a?.b</c></item>
///   <item><c>a == null ? null : a.b</c> → suggest <c>a?.b</c></item>
/// </list>
/// </remarks>
public sealed class UseOptionalChainingRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1401;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(TernaryExpr),
    };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not TernaryExpr ternary)
        {
            return;
        }

        if (ternary.Condition is not BinaryExpr condition)
        {
            return;
        }

        // Extract the null-checked variable and determine which branch is the null branch
        if (!TryGetNullCheck(condition, out string? varName, out bool isNotNullCheck))
        {
            return;
        }

        // For `a != null ? a.b : null`: isNotNullCheck=true, ThenBranch is the dot access, ElseBranch is null
        // For `a == null ? null : a.b`: isNotNullCheck=false, ThenBranch is null, ElseBranch is the dot access
        Expr dotBranch = isNotNullCheck ? ternary.ThenBranch : ternary.ElseBranch;
        Expr nullBranch = isNotNullCheck ? ternary.ElseBranch : ternary.ThenBranch;

        if (!IsNullLiteral(nullBranch))
        {
            return;
        }

        if (dotBranch is not DotExpr dot)
        {
            return;
        }

        if (dot.Object is not IdentifierExpr receiver || receiver.Name.Lexeme != varName)
        {
            return;
        }

        context.ReportDiagnostic(
            DiagnosticDescriptors.SA1401.CreateDiagnostic(ternary.Span, varName!, dot.Name.Lexeme));
    }

    private static bool TryGetNullCheck(BinaryExpr condition, out string? varName, out bool isNotNullCheck)
    {
        varName = null;
        isNotNullCheck = false;

        if (condition.Operator.Type is not (TokenType.EqualEqual or TokenType.BangEqual))
        {
            return false;
        }

        isNotNullCheck = condition.Operator.Type == TokenType.BangEqual;

        if (condition.Left is IdentifierExpr leftId && IsNullLiteral(condition.Right))
        {
            varName = leftId.Name.Lexeme;
            return true;
        }

        if (condition.Right is IdentifierExpr rightId && IsNullLiteral(condition.Left))
        {
            varName = rightId.Name.Lexeme;
            return true;
        }

        return false;
    }

    private static bool IsNullLiteral(Expr expr) =>
        expr is LiteralExpr { Value: null };
}
