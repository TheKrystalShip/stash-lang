namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;

/// <summary>
/// Represents an array literal expression: <c>[expr1, expr2, ...]</c>.
/// </summary>
/// <remarks>
/// At runtime, this evaluates to a <c>List&lt;object?&gt;</c>. An empty array literal
/// <c>[]</c> produces a node with an empty <see cref="Elements"/> list.
/// </remarks>
public class ArrayExpr : Expr
{
    /// <summary>
    /// Gets the list of element expressions inside the array literal.
    /// </summary>
    public List<Expr> Elements { get; }

    /// <summary>
    /// Creates a new array literal expression node.
    /// </summary>
    /// <param name="elements">The element expressions (may be empty for <c>[]</c>).</param>
    /// <param name="span">The source span covering the entire array literal.</param>
    public ArrayExpr(List<Expr> elements, SourceSpan span) : base(span)
    {
        Elements = elements;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitArrayExpr(this);
}
