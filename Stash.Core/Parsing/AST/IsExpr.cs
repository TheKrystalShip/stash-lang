using Stash.Common;
using Stash.Lexing;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a type-checking expression using the <c>is</c> keyword.
/// The left operand is any expression, and the right operand is a type name.
/// Returns <c>true</c> if the value matches the specified type.
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
    /// </summary>
    public Token TypeName { get; }

    /// <summary>
    /// Creates a new type-checking expression node.
    /// </summary>
    /// <param name="left">The expression to check.</param>
    /// <param name="keyword">The <c>is</c> keyword token.</param>
    /// <param name="typeName">The type name token.</param>
    /// <param name="span">The source span covering the entire expression.</param>
    public IsExpr(Expr left, Token keyword, Token typeName, SourceSpan span) : base(span)
    {
        Left = left;
        Keyword = keyword;
        TypeName = typeName;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitIsExpr(this);
}
