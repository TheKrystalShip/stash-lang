namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A dot-assignment expression: <c>obj.field = value</c>
/// </summary>
public class DotAssignExpr : Expr
{
    public Expr Object { get; }
    public Token Name { get; }
    public Expr Value { get; }

    public DotAssignExpr(Expr obj, Token name, Expr value, SourceSpan span) : base(span)
    {
        Object = obj;
        Name = name;
        Value = value;
    }

    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitDotAssignExpr(this);
}
