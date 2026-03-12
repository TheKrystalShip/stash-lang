namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// An if statement: <c>if (condition) { ... } else { ... }</c>
/// </summary>
public class IfStmt : Stmt
{
    public Expr Condition { get; }
    public Stmt ThenBranch { get; }
    public Stmt? ElseBranch { get; }

    public IfStmt(Expr condition, Stmt thenBranch, Stmt? elseBranch, SourceSpan span) : base(span)
    {
        Condition = condition;
        ThenBranch = thenBranch;
        ElseBranch = elseBranch;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitIfStmt(this);
}
