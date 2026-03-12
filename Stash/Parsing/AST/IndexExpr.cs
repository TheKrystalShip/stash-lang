namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// An index access expression: <c>arr[i]</c>
/// </summary>
public class IndexExpr : Expr
{
    public Expr Object { get; }
    public Expr Index { get; }
    public SourceSpan BracketSpan { get; }

    public IndexExpr(Expr obj, Expr index, SourceSpan bracketSpan, SourceSpan span) : base(span)
    {
        Object = obj;
        Index = index;
        BracketSpan = bracketSpan;
    }

    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitIndexExpr(this);
}
