using Stash.Common;
using Stash.Lexing;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a prefix unary expression: <c>!expr</c> (logical NOT) or <c>-expr</c> (numeric negation).
/// </summary>
/// <remarks>
/// Unary expressions are right-recursive: <c>--x</c> parses as <c>-(-(x))</c>.
/// The <see cref="Operator"/> token discriminates which operation to perform at evaluation time.
/// </remarks>
/// <example>
/// The Stash expression <c>!true</c> produces:
/// <code>new UnaryExpr(bangToken, new LiteralExpr(true, ...), span)</code>
/// </example>
public class UnaryExpr : Expr
{
    /// <summary>
    /// Gets the operator token (<c>!</c> or <c>-</c>).
    /// </summary>
    public Token Operator { get; }

    /// <summary>
    /// Gets the operand expression to the right of the operator.
    /// </summary>
    public Expr Right { get; }

    /// <summary>
    /// Creates a new unary expression node.
    /// </summary>
    /// <param name="op">The operator token (<c>!</c> or <c>-</c>).</param>
    /// <param name="right">The operand expression.</param>
    /// <param name="span">The source span covering the operator and operand.</param>
    public UnaryExpr(Token op, Expr right, SourceSpan span) : base(span)
    {
        Operator = op;
        Right = right;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitUnaryExpr(this);
}
