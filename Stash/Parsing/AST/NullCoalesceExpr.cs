using Stash.Common;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a null-coalescing expression: <c>left ?? right</c>.
/// Returns <c>left</c> if it is not null, otherwise evaluates and returns <c>right</c>.
/// </summary>
public class NullCoalesceExpr : Expr
{
    /// <summary>
    /// Gets the left-hand operand, evaluated first.
    /// </summary>
    public Expr Left { get; }

    /// <summary>
    /// Gets the right-hand operand, evaluated only when <see cref="Left"/> is null.
    /// </summary>
    public Expr Right { get; }

    /// <summary>
    /// Creates a new null-coalescing expression node.
    /// </summary>
    /// <param name="left">The left-hand operand.</param>
    /// <param name="right">The right-hand operand (fallback value).</param>
    /// <param name="span">The source span covering the full expression.</param>
    public NullCoalesceExpr(Expr left, Expr right, SourceSpan span) : base(span)
    {
        Left = left;
        Right = right;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitNullCoalesceExpr(this);
}
