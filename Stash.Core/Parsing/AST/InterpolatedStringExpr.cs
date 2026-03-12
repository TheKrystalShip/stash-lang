using System.Collections.Generic;
using Stash.Common;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a string interpolation expression containing a mix of literal text segments
/// and embedded expressions.
/// </summary>
/// <remarks>
/// Stash supports two interpolation syntaxes that both produce this node:
/// <list type="bullet">
///   <item><description><c>"Hello ${name}"</c> — embedded interpolation using <c>${...}</c></description></item>
///   <item><description><c>$"Hello {name}"</c> — prefixed interpolation (C#-style) using <c>{...}</c></description></item>
/// </list>
/// <para>
/// The <see cref="Parts"/> list alternates between <see cref="LiteralExpr"/> nodes (for the
/// static text segments) and arbitrary <see cref="Expr"/> nodes (for the interpolated
/// expressions). At runtime, each part is evaluated, stringified, and concatenated.
/// </para>
/// </remarks>
public class InterpolatedStringExpr : Expr
{
    /// <summary>
    /// Gets the ordered list of expression parts that make up this interpolated string.
    /// Each part is either a <see cref="LiteralExpr"/> containing a string segment or an
    /// arbitrary expression to be evaluated and stringified.
    /// </summary>
    public List<Expr> Parts { get; }

    /// <summary>
    /// Creates a new interpolated string expression node.
    /// </summary>
    /// <param name="parts">The ordered list of text and expression parts.</param>
    /// <param name="span">The source span covering the entire interpolated string.</param>
    public InterpolatedStringExpr(List<Expr> parts, SourceSpan span) : base(span)
    {
        Parts = parts;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitInterpolatedStringExpr(this);
}
