using Stash.Common;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a try expression: <c>try expr</c>.
/// If the inner expression throws a RuntimeError, the try expression evaluates to <c>null</c>.
/// </summary>
public class TryExpr : Expr
{
    /// <summary>
    /// Gets the inner expression to evaluate.
    /// </summary>
    public Expr Expression { get; }

    /// <summary>
    /// Creates a new try expression node.
    /// </summary>
    /// <param name="expression">The inner expression to evaluate.</param>
    /// <param name="span">The source span covering the <c>try</c> keyword and the inner expression.</param>
    public TryExpr(Expr expression, SourceSpan span) : base(span, ExprType.Try)
    {
        Expression = expression;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitTryExpr(this);
}
