namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;

/// <summary>
/// A block of statements enclosed in braces: <c>{ ... }</c>
/// </summary>
public class BlockStmt : Stmt
{
    public List<Stmt> Statements { get; }

    public BlockStmt(List<Stmt> statements, SourceSpan span) : base(span)
    {
        Statements = statements;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitBlockStmt(this);
}
