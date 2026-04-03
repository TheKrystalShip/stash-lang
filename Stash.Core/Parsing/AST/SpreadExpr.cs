namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents a spread expression: <c>...expr</c>.
/// Used in function call arguments, array literals, and dictionary literals.
/// </summary>
public class SpreadExpr : Expr
{
    /// <summary>Gets the <c>...</c> operator token.</summary>
    public Token Operator { get; }

    /// <summary>Gets the inner expression being spread.</summary>
    public Expr Expression { get; }

    public SpreadExpr(Token op, Expr expression, SourceSpan span) : base(span)
    {
        Operator = op;
        Expression = expression;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitSpreadExpr(this);
}
