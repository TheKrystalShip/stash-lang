namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// A statement that wraps an expression (e.g., a function call used as a statement).
/// </summary>
/// <remarks>
/// Any expression can appear as a statement. The expression's return value is discarded.
/// Common cases include function/method calls and assignments used at statement level.
/// </remarks>
public class ExprStmt : Stmt
{
    /// <summary>Gets the wrapped expression.</summary>
    public Expr Expression { get; }

    /// <summary>Initializes a new instance of <see cref="ExprStmt"/>.</summary>
    /// <param name="expression">The expression to wrap as a statement.</param>
    /// <param name="span">The source location of this statement.</param>
    public ExprStmt(Expr expression, SourceSpan span) : base(span)
    {
        Expression = expression;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitExprStmt(this);
}
