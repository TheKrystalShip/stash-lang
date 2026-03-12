namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// An assignment expression: <c>x = value</c>
/// </summary>
public class AssignExpr : Expr
{
    public Token Name { get; }
    public Expr Value { get; }

    public AssignExpr(Token name, Expr value, SourceSpan span) : base(span)
    {
        Name = name;
        Value = value;
    }

    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitAssignExpr(this);
}
