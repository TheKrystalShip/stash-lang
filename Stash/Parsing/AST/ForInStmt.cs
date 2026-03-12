namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A for-in loop: <c>for (let x in collection) { ... }</c>
/// </summary>
public class ForInStmt : Stmt
{
    public Token VariableName { get; }
    public Expr Iterable { get; }
    public BlockStmt Body { get; }

    public ForInStmt(Token variableName, Expr iterable, BlockStmt body, SourceSpan span) : base(span)
    {
        VariableName = variableName;
        Iterable = iterable;
        Body = body;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitForInStmt(this);
}
