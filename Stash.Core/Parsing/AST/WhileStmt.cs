namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// A while loop: <c>while (condition) { ... }</c>
/// </summary>
/// <remarks>
/// The condition is evaluated before each iteration. Supports <c>break</c> and <c>continue</c> inside the body.
/// The condition follows Stash truthiness rules.
/// </remarks>
public class WhileStmt : Stmt
{
    /// <summary>Gets the loop condition expression, evaluated before each iteration.</summary>
    public Expr Condition { get; }
    /// <summary>Gets the block of statements executed on each iteration.</summary>
    public BlockStmt Body { get; }

    /// <summary>Initializes a new instance of <see cref="WhileStmt"/>.</summary>
    /// <param name="condition">The loop condition expression, evaluated before each iteration.</param>
    /// <param name="body">The block of statements executed on each iteration.</param>
    /// <param name="span">The source location of this statement.</param>
    public WhileStmt(Expr condition, BlockStmt body, SourceSpan span) : base(span)
    {
        Condition = condition;
        Body = body;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitWhileStmt(this);
}
