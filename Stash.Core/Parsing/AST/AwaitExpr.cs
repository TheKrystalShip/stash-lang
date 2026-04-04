namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents an await expression: <c>await expr</c>.
/// </summary>
/// <remarks>
/// If the inner expression evaluates to a <c>StashFuture</c>, the await expression
/// blocks until the future resolves and returns its value. If the inner expression
/// evaluates to any other value, the await expression returns that value directly
/// (transparent await).
/// </remarks>
public class AwaitExpr : Expr
{
    /// <summary>
    /// Gets the <c>await</c> keyword token.
    /// </summary>
    public Token Keyword { get; }

    /// <summary>
    /// Gets the inner expression to await.
    /// </summary>
    public Expr Expression { get; }

    /// <summary>
    /// Creates a new await expression node.
    /// </summary>
    /// <param name="keyword">The <c>await</c> keyword token.</param>
    /// <param name="expression">The inner expression to await.</param>
    /// <param name="span">The source span covering the <c>await</c> keyword and the inner expression.</param>
    public AwaitExpr(Token keyword, Expr expression, SourceSpan span) : base(span, ExprType.Await)
    {
        Keyword = keyword;
        Expression = expression;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitAwaitExpr(this);
}
