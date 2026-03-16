namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// A do-while loop: <c>do { ... } while (condition);</c>
/// </summary>
public class DoWhileStmt : Stmt
{
    public BlockStmt Body { get; }
    public Expr Condition { get; }

    public DoWhileStmt(BlockStmt body, Expr condition, SourceSpan span) : base(span)
    {
        Body = body;
        Condition = condition;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitDoWhileStmt(this);
}
