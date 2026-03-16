using Stash.Common;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a range expression: <c>start..end</c> or <c>start..end..step</c>.
/// </summary>
public class RangeExpr : Expr
{
    /// <summary>Gets the start of the range.</summary>
    public Expr Start { get; }

    /// <summary>Gets the end of the range (exclusive).</summary>
    public Expr End { get; }

    /// <summary>Gets the optional step increment, or <c>null</c> for a step of 1.</summary>
    public Expr? Step { get; }

    /// <summary>
    /// Creates a new range expression node.
    /// </summary>
    /// <param name="start">The start expression.</param>
    /// <param name="end">The end expression.</param>
    /// <param name="step">The optional step expression.</param>
    /// <param name="span">The source span covering the entire range expression.</param>
    public RangeExpr(Expr start, Expr end, Expr? step, SourceSpan span) : base(span)
    {
        Start = start;
        End = end;
        Step = step;
    }

    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitRangeExpr(this);
}
