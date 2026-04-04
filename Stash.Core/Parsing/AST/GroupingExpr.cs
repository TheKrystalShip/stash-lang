using Stash.Common;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a parenthesized expression <c>(expr)</c> in the AST.
/// </summary>
/// <remarks>
/// <see cref="GroupingExpr"/> exists as a distinct node (rather than being discarded during parsing)
/// to preserve the syntactic structure of the original source. This is important for
/// debugging and for potential future pretty-printing or source-to-source transformations.
/// At evaluation time the grouping simply delegates to its inner expression.
/// </remarks>
/// <example>
/// The Stash expression <c>(1 + 2)</c> produces:
/// <code>new GroupingExpr(new BinaryExpr(...), span)</code>
/// </example>
public class GroupingExpr : Expr
{
    /// <summary>
    /// Gets the inner expression wrapped by the parentheses.
    /// </summary>
    public Expr Expression { get; }

    /// <summary>
    /// Creates a new grouping expression node.
    /// </summary>
    /// <param name="expression">The expression inside the parentheses.</param>
    /// <param name="span">The source span from the opening <c>(</c> to the closing <c>)</c>.</param>
    public GroupingExpr(Expr expression, SourceSpan span) : base(span, ExprType.Grouping)
    {
        Expression = expression;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitGroupingExpr(this);
}
