namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// An if statement: <c>if (condition) { ... } else { ... }</c>
/// </summary>
/// <remarks>
/// The <see cref="ElseBranch"/> is <c>null</c> when there is no <c>else</c> clause. For <c>else if</c> chains,
/// the else branch is another <see cref="IfStmt"/> node, forming a linked chain. The condition follows
/// Stash truthiness rules: <c>false</c>, <c>null</c>, <c>0</c>, <c>0.0</c>, and <c>""</c> are falsy.
/// </remarks>
public class IfStmt : Stmt
{
    /// <summary>Gets the condition expression to evaluate.</summary>
    public Expr Condition { get; }
    /// <summary>Gets the statement to execute when the condition is truthy.</summary>
    public Stmt ThenBranch { get; }
    /// <summary>Gets the optional else branch. <c>null</c> if there is no <c>else</c> clause. May be another <see cref="IfStmt"/> for <c>else if</c> chains.</summary>
    public Stmt? ElseBranch { get; }

    /// <summary>Initializes a new instance of <see cref="IfStmt"/>.</summary>
    /// <param name="condition">The condition expression to evaluate.</param>
    /// <param name="thenBranch">The statement to execute when the condition is truthy.</param>
    /// <param name="elseBranch">The optional else branch, or <c>null</c>.</param>
    /// <param name="span">The source location of this statement.</param>
    public IfStmt(Expr condition, Stmt thenBranch, Stmt? elseBranch, SourceSpan span) : base(span)
    {
        Condition = condition;
        ThenBranch = thenBranch;
        ElseBranch = elseBranch;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitIfStmt(this);
}
