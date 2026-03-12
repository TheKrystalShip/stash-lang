namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// A return statement: <c>return expr;</c> or <c>return;</c>
/// </summary>
public class ReturnStmt : Stmt
{
    public Expr? Value { get; }

    public ReturnStmt(Expr? value, SourceSpan span) : base(span)
    {
        Value = value;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitReturnStmt(this);
}
