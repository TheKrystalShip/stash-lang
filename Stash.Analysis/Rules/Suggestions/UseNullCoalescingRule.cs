namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// SA1402 — Suggests using null coalescing (<c>a ?? b</c>) instead of a verbose null-check
/// ternary that returns the variable itself or a default.
/// </summary>
/// <remarks>
/// Detects patterns:
/// <list type="bullet">
///   <item><c>a != null ? a : b</c> → suggest <c>a ?? b</c></item>
///   <item><c>a == null ? b : a</c> → suggest <c>a ?? b</c></item>
/// </list>
/// </remarks>
public sealed class UseNullCoalescingRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1402;

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

        if (!TryGetNullCheck(condition, out string? varName, out bool isNotNullCheck))
        {
            return;
        }

        // For `a != null ? a : b`: isNotNullCheck=true, ThenBranch is the var itself, ElseBranch is the default
        // For `a == null ? b : a`: isNotNullCheck=false, ThenBranch is the default, ElseBranch is the var itself
        Expr selfBranch = isNotNullCheck ? ternary.ThenBranch : ternary.ElseBranch;
        Expr defaultBranch = isNotNullCheck ? ternary.ElseBranch : ternary.ThenBranch;

        // The self-branch must be the same identifier as the null-checked variable
        if (selfBranch is not IdentifierExpr selfId || selfId.Name.Lexeme != varName)
        {
            return;
        }

        // The default branch must not itself be null (that would be pointless)
        if (IsNullLiteral(defaultBranch))
        {
            return;
        }

        string defaultText = defaultBranch is IdentifierExpr defId
            ? defId.Name.Lexeme
            : defaultBranch.Span.ToString();

        context.ReportDiagnostic(
            DiagnosticDescriptors.SA1402.CreateDiagnostic(ternary.Span, varName!, defaultText));
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
