namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// A continue statement: <c>continue;</c>
/// </summary>
public class ContinueStmt : Stmt
{
    public ContinueStmt(SourceSpan span) : base(span) { }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitContinueStmt(this);
}
