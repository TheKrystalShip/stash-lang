namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// A C-style for loop: <c>for (init; condition; update) { ... }</c>
/// </summary>
/// <remarks>
/// The initializer may be a variable declaration or expression statement (or <c>null</c> if omitted).
/// The condition is evaluated before each iteration (or <c>null</c> for an infinite loop).
/// The update expression executes after each iteration (or <c>null</c> if omitted).
/// Supports <c>break</c> and <c>continue</c>.
/// </remarks>
public class ForStmt : Stmt
{
    /// <summary>Gets the optional initializer, executed once before the loop. May be a <see cref="VarDeclStmt"/> or <see cref="ExprStmt"/>.</summary>
    public Stmt? Initializer { get; }
    /// <summary>Gets the optional loop condition expression, evaluated before each iteration. <c>null</c> means infinite loop.</summary>
    public Expr? Condition { get; }
    /// <summary>Gets the optional update expression, executed after each iteration.</summary>
    public Expr? Increment { get; }
    /// <summary>Gets the block of statements executed on each iteration.</summary>
    public BlockStmt Body { get; }

    public ForStmt(Stmt? initializer, Expr? condition, Expr? increment, BlockStmt body, SourceSpan span) : base(span)
    {
        Initializer = initializer;
        Condition = condition;
        Increment = increment;
        Body = body;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitForStmt(this);
}
