namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// An index assignment expression: <c>arr[i] = val</c>
/// </summary>
public class IndexAssignExpr : Expr
{
    public Expr Object { get; }
    public Expr Index { get; }
    public Expr Value { get; }
    public SourceSpan BracketSpan { get; }

    public IndexAssignExpr(Expr obj, Expr index, Expr value, SourceSpan bracketSpan, SourceSpan span) : base(span)
    {
        Object = obj;
        Index = index;
        Value = value;
        BracketSpan = bracketSpan;
    }

    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitIndexAssignExpr(this);
}
