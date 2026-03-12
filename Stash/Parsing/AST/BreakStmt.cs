namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// A break statement: <c>break;</c>
/// </summary>
public class BreakStmt : Stmt
{
    public BreakStmt(SourceSpan span) : base(span) { }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitBreakStmt(this);
}
