namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// SA1403 — Suggests using string interpolation instead of string concatenation via <c>+</c>
/// when a chain of <c>+</c> operators contains at least <see cref="Threshold"/> string literal
/// operands and at least one non-literal operand.
/// </summary>
/// <remarks>
/// Not fired inside loop bodies (where SA1202 already provides stronger guidance) or when
/// all operands in the chain are literals (constant folding handles those). Fires exactly
/// once per chain, at the top-level <see cref="BinaryExpr"/> node.
/// Configurable via <c>options.SA1403.threshold</c> in <c>.stashcheck</c> (default: 3).
/// </remarks>
public sealed class PreferStringInterpolationRule : IAnalysisRule, IConfigurableRule
{
    /// <summary>Default minimum string-literal count that triggers the diagnostic.</summary>
    public const int DefaultThreshold = 3;

    /// <summary>Configurable threshold; defaults to <see cref="DefaultThreshold"/>.</summary>
    public int Threshold { get; private set; } = DefaultThreshold;

    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1403;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(BinaryExpr) };

    public void Configure(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("threshold", out string? val) && int.TryParse(val, out int v) && v > 0)
            Threshold = v;
    }

    public void Analyze(RuleContext context)
    {
        if (context.LoopDepth > 0) return; // SA1202 covers the loop case
        if (context.Expression is not BinaryExpr bin) return;
        if (bin.Operator.Type != TokenType.Plus) return;

        // Only process the root of a + chain — skip if our parent is also a + BinaryExpr.
        if (context.ParentBinaryOperator == TokenType.Plus) return;

        int literalCount = CountStringLiterals(bin);
        if (literalCount < Threshold) return;
        if (!HasNonLiteral(bin)) return; // all-literal chain: constant folding handles it

        context.ReportDiagnostic(
            DiagnosticDescriptors.SA1403.CreateDiagnostic(bin.Span, literalCount, Threshold));
    }

    /// <summary>
    /// Counts the number of string-literal operands in the <c>+</c> chain rooted at <paramref name="expr"/>.
    /// Recursion terminates at non-<c>+</c> binary expressions and all non-binary-expression leaves.
    /// </summary>
    private static int CountStringLiterals(Expr expr)
    {
        if (expr is BinaryExpr b && b.Operator.Type == TokenType.Plus)
            return CountStringLiterals(b.Left) + CountStringLiterals(b.Right);
        if (expr is LiteralExpr lit && lit.Value is string)
            return 1;
        return 0;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the <c>+</c> chain rooted at <paramref name="expr"/>
    /// contains at least one operand that is not a <see cref="LiteralExpr"/>.
    /// </summary>
    private static bool HasNonLiteral(Expr expr)
    {
        if (expr is BinaryExpr b && b.Operator.Type == TokenType.Plus)
            return HasNonLiteral(b.Left) || HasNonLiteral(b.Right);
        return expr is not LiteralExpr;
    }
}
