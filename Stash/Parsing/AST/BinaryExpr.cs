using Stash.Common;
using Stash.Lexing;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents an infix binary expression: <c>left op right</c>.
/// </summary>
/// <remarks>
/// Covers all infix operators in Stash:
/// <list type="bullet">
///   <item><description>Arithmetic: <c>+</c>, <c>-</c>, <c>*</c>, <c>/</c>, <c>%</c></description></item>
///   <item><description>Comparison: <c>&lt;</c>, <c>&gt;</c>, <c>&lt;=</c>, <c>&gt;=</c></description></item>
///   <item><description>Equality: <c>==</c>, <c>!=</c></description></item>
///   <item><description>Logical: <c>&amp;&amp;</c>, <c>||</c></description></item>
/// </list>
/// The <see cref="Operator"/> token discriminates which operation to perform.
/// The <c>+</c> operator also supports string concatenation when both operands are strings.
/// </remarks>
/// <example>
/// The Stash expression <c>1 + 2</c> produces:
/// <code>new BinaryExpr(new LiteralExpr(1L, ...), plusToken, new LiteralExpr(2L, ...), span)</code>
/// </example>
public class BinaryExpr : Expr
{
    /// <summary>
    /// Gets the left-hand operand expression.
    /// </summary>
    public Expr Left { get; }

    /// <summary>
    /// Gets the operator token (e.g. <c>+</c>, <c>==</c>, <c>&amp;&amp;</c>).
    /// </summary>
    public Token Operator { get; }

    /// <summary>
    /// Gets the right-hand operand expression.
    /// </summary>
    public Expr Right { get; }

    /// <summary>
    /// Creates a new binary expression node.
    /// </summary>
    /// <param name="left">The left-hand operand.</param>
    /// <param name="op">The infix operator token.</param>
    /// <param name="right">The right-hand operand.</param>
    /// <param name="span">The source span covering the entire binary expression.</param>
    public BinaryExpr(Expr left, Token op, Expr right, SourceSpan span) : base(span)
    {
        Left = left;
        Operator = op;
        Right = right;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitBinaryExpr(this);
}
