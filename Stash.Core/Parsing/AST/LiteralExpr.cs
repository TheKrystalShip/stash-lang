using Stash.Common;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a literal value in Stash source code: an integer, float, string, boolean, or <c>null</c>.
/// </summary>
/// <remarks>
/// <see cref="Value"/> is typed as <c>object?</c> because Stash is dynamically typed.
/// At runtime the value will be one of:
/// <list type="bullet">
///   <item><description><see cref="long"/> — integer literals (e.g. <c>42</c>)</description></item>
///   <item><description><see cref="double"/> — floating-point literals (e.g. <c>3.14</c>)</description></item>
///   <item><description><see cref="string"/> — string literals (e.g. <c>"hello"</c>)</description></item>
///   <item><description><see cref="bool"/> — <c>true</c> or <c>false</c></description></item>
///   <item><description><c>null</c> — the <c>null</c> keyword</description></item>
/// </list>
/// </remarks>
/// <example>
/// The Stash expression <c>42</c> produces:
/// <code>new LiteralExpr(42L, span)</code>
/// </example>
public class LiteralExpr : Expr
{
    /// <summary>
    /// Gets the runtime value of this literal. May be <c>null</c> for the Stash <c>null</c> keyword.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Creates a new literal expression node.
    /// </summary>
    /// <param name="value">
    /// The literal value — a <see cref="long"/>, <see cref="double"/>, <see cref="string"/>,
    /// <see cref="bool"/>, or <c>null</c>.
    /// </param>
    /// <param name="span">The source span of the literal token.</param>
    public LiteralExpr(object? value, SourceSpan span) : base(span, ExprType.Literal)
    {
        Value = value;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitLiteralExpr(this);
}
