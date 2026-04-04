using Stash.Common;
using Stash.Lexing;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a prefix or postfix increment/decrement expression: <c>++x</c>, <c>x++</c>, <c>--x</c>, <c>x--</c>.
/// </summary>
/// <remarks>
/// The <see cref="IsPrefix"/> flag determines evaluation order: prefix form returns the new value,
/// while postfix form returns the original value before mutation.
/// The <see cref="Operand"/> must be an assignable expression — an identifier, dot access, or index access.
/// </remarks>
/// <example>
/// The Stash expression <c>++i</c> produces:
/// <code>new UpdateExpr(plusPlusToken, new IdentifierExpr(iToken, ...), true, span)</code>
/// </example>
public class UpdateExpr : Expr
{
    /// <summary>
    /// Gets the operator token (<c>++</c> or <c>--</c>).
    /// </summary>
    public Token Operator { get; }

    /// <summary>
    /// Gets the operand expression being incremented or decremented.
    /// </summary>
    public Expr Operand { get; }

    /// <summary>
    /// Gets a value indicating whether this is a prefix (<c>++x</c>) rather than postfix (<c>x++</c>) form.
    /// </summary>
    public bool IsPrefix { get; }

    /// <summary>
    /// Creates a new update expression node.
    /// </summary>
    /// <param name="op">The operator token (<c>++</c> or <c>--</c>).</param>
    /// <param name="operand">The expression being updated.</param>
    /// <param name="isPrefix">Whether this is the prefix form.</param>
    /// <param name="span">The source span covering the entire expression.</param>
    public UpdateExpr(Token op, Expr operand, bool isPrefix, SourceSpan span) : base(span, ExprType.Update)
    {
        Operator = op;
        Operand = operand;
        IsPrefix = isPrefix;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitUpdateExpr(this);
}
