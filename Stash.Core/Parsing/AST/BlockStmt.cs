namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;

/// <summary>
/// A block of statements enclosed in braces: <c>{ ... }</c>
/// </summary>
/// <remarks>
/// A block introduces a new lexical scope. Variables declared inside a block are not visible outside of it.
/// Blocks are used as the body for functions, loops, if-statements, and standalone scoped blocks.
/// </remarks>
public class BlockStmt : Stmt
{
    /// <summary>Gets the list of statements contained in the block.</summary>
    public List<Stmt> Statements { get; }

    /// <summary>Initializes a new instance of <see cref="BlockStmt"/>.</summary>
    /// <param name="statements">The list of statements contained in the block.</param>
    /// <param name="span">The source location of this block.</param>
    public BlockStmt(List<Stmt> statements, SourceSpan span) : base(span, StmtType.Block)
    {
        Statements = statements;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitBlockStmt(this);
}
