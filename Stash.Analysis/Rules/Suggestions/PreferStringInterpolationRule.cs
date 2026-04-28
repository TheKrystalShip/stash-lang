namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// SA1403 — Suggests using string interpolation instead of string concatenation via <c>+</c>
/// when at least one operand is a string literal and the other is a non-literal expression.
/// </summary>
/// <remarks>
/// Not fired inside loop bodies (where SA1202 already provides stronger guidance) or when
/// both operands are literals (constant folding handles those).
/// </remarks>
public sealed class PreferStringInterpolationRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1403;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(BinaryExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.LoopDepth > 0) return; // SA1202 covers the loop case
        if (context.Expression is not BinaryExpr bin) return;
        if (bin.Operator.Type != TokenType.Plus) return;

        bool leftIsStringLit = IsStringLiteral(bin.Left);
        bool rightIsStringLit = IsStringLiteral(bin.Right);

        if (leftIsStringLit && !IsLiteral(bin.Right))
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA1403.CreateDiagnostic(bin.Span));
        }
        else if (rightIsStringLit && !IsLiteral(bin.Left))
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA1403.CreateDiagnostic(bin.Span));
        }
    }

    private static bool IsStringLiteral(Expr expr) =>
        expr is LiteralExpr lit && lit.Value is string;

    private static bool IsLiteral(Expr expr) =>
        expr is LiteralExpr;
}
