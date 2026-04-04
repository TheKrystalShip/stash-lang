using Stash.Common;
using Stash.Lexing;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a type-checking expression using the <c>is</c> keyword.
/// The left operand is any expression, and the right operand is a type name or expression.
/// Returns <c>true</c> if the value matches the specified type.
/// Exactly one of <see cref="TypeName"/> or <see cref="TypeExpr"/> is set.
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
    /// Gets the type name token (e.g. <c>int</c>, <c>string</c>, <c>array</c>).
    /// Null when <see cref="TypeExpr"/> is used instead.
    /// </summary>
    public Token? TypeName { get; }

    /// <summary>
    /// Gets the type expression when the RHS is a complex expression
    /// (e.g. <c>types[0]</c>, <c>getType()</c>, <c>module.Type</c>).
    /// Null when <see cref="TypeName"/> is used instead.
    /// </summary>
    public Expr? TypeExpr { get; }

    /// <summary>
    /// Creates a new type-checking expression node with a bare type name token.
    /// </summary>
    /// <param name="left">The expression to check.</param>
    /// <param name="keyword">The <c>is</c> keyword token.</param>
    /// <param name="typeName">The type name token.</param>
    /// <param name="span">The source span covering the entire expression.</param>
    public IsExpr(Expr left, Token keyword, Token typeName, SourceSpan span) : base(span, ExprType.Is)
    {
        Left = left;
        Keyword = keyword;
        TypeName = typeName;
    }

    /// <summary>
    /// Creates a new type-checking expression node with a complex type expression.
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
