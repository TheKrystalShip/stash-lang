namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// A do-while loop: <c>do { ... } while (condition);</c>
/// </summary>
/// <remarks>
/// Unlike <see cref="WhileStmt"/>, the body executes at least once before the condition is checked.
/// Supports <c>break</c> and <c>continue</c>.
/// </remarks>
public class DoWhileStmt : Stmt
{
    /// <summary>Gets the block of statements executed on each iteration (at least once).</summary>
    public BlockStmt Body { get; }
    /// <summary>Gets the loop condition expression, evaluated after each iteration.</summary>
    public Expr Condition { get; }

    /// <summary>Initializes a new instance of <see cref="DoWhileStmt"/>.</summary>
    /// <param name="body">The block of statements executed on each iteration (at least once).</param>
    /// <param name="condition">The loop condition expression, evaluated after each iteration.</param>
    /// <param name="span">The source location of this statement.</param>
    public DoWhileStmt(BlockStmt body, Expr condition, SourceSpan span) : base(span, StmtType.DoWhile)
    {
        Body = body;
        Condition = condition;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitDoWhileStmt(this);
}
