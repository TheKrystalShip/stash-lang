namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// A statement that wraps an expression (e.g., a function call used as a statement).
/// </summary>
public class ExprStmt : Stmt
{
    public Expr Expression { get; }

    public ExprStmt(Expr expression, SourceSpan span) : base(span)
    {
        Expression = expression;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitExprStmt(this);
}
