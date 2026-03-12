namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// A while loop: <c>while (condition) { ... }</c>
/// </summary>
public class WhileStmt : Stmt
{
    public Expr Condition { get; }
    public BlockStmt Body { get; }

    public WhileStmt(Expr condition, BlockStmt body, SourceSpan span) : base(span)
    {
        Condition = condition;
        Body = body;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitWhileStmt(this);
}
