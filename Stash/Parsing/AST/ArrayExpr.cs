namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;

/// <summary>
/// An array literal expression: <c>[expr, expr, ...]</c>
/// </summary>
public class ArrayExpr : Expr
{
    public List<Expr> Elements { get; }

    public ArrayExpr(List<Expr> elements, SourceSpan span) : base(span)
    {
        Elements = elements;
    }

    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitArrayExpr(this);
}
