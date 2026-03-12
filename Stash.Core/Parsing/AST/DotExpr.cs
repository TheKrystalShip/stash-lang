namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A dot-access expression: <c>obj.field</c>
/// Used for both struct field access and enum member access.
/// </summary>
public class DotExpr : Expr
{
    public Expr Object { get; }
    public Token Name { get; }

    public DotExpr(Expr obj, Token name, SourceSpan span) : base(span)
    {
        Object = obj;
        Name = name;
    }

    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitDotExpr(this);
}
