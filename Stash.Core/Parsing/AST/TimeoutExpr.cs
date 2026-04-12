namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents a <c>timeout</c> expression that bounds execution of a block to a specified duration.
/// If the block does not complete within the duration, a TimeoutError is thrown.
/// </summary>
public class TimeoutExpr : Expr
{
    /// <summary>Gets the <c>timeout</c> keyword token.</summary>
    public Token TimeoutKeyword { get; }

    /// <summary>Gets the expression that evaluates to the timeout duration.</summary>
    public Expr Duration { get; }

    /// <summary>Gets the block body to execute within the time limit.</summary>
    public BlockStmt Body { get; }

    public TimeoutExpr(Token timeoutKeyword, Expr duration, BlockStmt body, SourceSpan span)
        : base(span, ExprType.Timeout)
    {
        TimeoutKeyword = timeoutKeyword;
        Duration = duration;
        Body = body;
    }

    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitTimeoutExpr(this);
}
