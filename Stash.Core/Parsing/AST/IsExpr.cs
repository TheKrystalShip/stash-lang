using Stash.Common;
using Stash.Lexing;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a type-checking expression using the <c>is</c> keyword.
/// The left operand is any expression, and the right operand is either a structured
/// <see cref="TypeExpression"/> (most cases) or a runtime <see cref="Expr"/> (when the
/// type comes from an expression like <c>types[0]</c> or <c>getType()</c>).
/// Exactly one of <see cref="Type"/> or <see cref="TypeExpr"/> is non-null.
/// </summary>
public class IsExpr : Expr
{
    /// <summary>
    /// Gets the expression whose type is being checked.
    /// </summary>
    public Expr Left { get; }

    /// <summary>Gets the <c>is</c> keyword token.</summary>
    public Token Keyword { get; }

    /// <summary>
    /// Gets the structured type expression on the RHS (e.g. <c>int</c>, <c>diff.Edit</c>, <c>int[]</c>,
    /// <c>Point</c>, <c>Printable</c>). Null when <see cref="TypeExpr"/> is used instead.
    /// </summary>
    public TypeExpression? Type { get; }

    /// <summary>
    /// Gets the type expression when the RHS is an arbitrary expression evaluating to a type value
    /// (e.g. <c>types[0]</c>, <c>getType()</c>). Null when <see cref="Type"/> is used instead.
    /// </summary>
    public Expr? TypeExpr { get; }

    /// <summary>
    /// Creates a new type-checking expression node with a structured type expression.
    /// </summary>
    /// <param name="left">The expression to check.</param>
    /// <param name="keyword">The <c>is</c> keyword token.</param>
    /// <param name="type">The type expression.</param>
    /// <param name="span">The source span covering the entire expression.</param>
    public IsExpr(Expr left, Token keyword, TypeExpression type, SourceSpan span) : base(span, ExprType.Is)
    {
        Left = left;
        Keyword = keyword;
        Type = type;
    }

    /// <summary>
    /// Creates a new type-checking expression node with a runtime-evaluated type expression.
    /// </summary>
    /// <param name="left">The expression to check.</param>
    /// <param name="keyword">The <c>is</c> keyword token.</param>
    /// <param name="typeExpr">The expression that produces the type to check against.</param>
    /// <param name="span">The source span covering the entire expression.</param>
    public IsExpr(Expr left, Token keyword, Expr typeExpr, SourceSpan span) : base(span, ExprType.Is)
    {
        Left = left;
        Keyword = keyword;
        TypeExpr = typeExpr;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitIsExpr(this);
}
