namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents a dot-access expression: <c>obj.field</c> or optional chaining <c>obj?.field</c>.
/// </summary>
/// <remarks>
/// Used for accessing fields on struct instances, entries in dictionaries, members of enums,
/// and members of namespaces. When <see cref="IsOptional"/> is <c>true</c>, the expression
/// uses optional chaining semantics (<c>?.</c>): if the receiver is <c>null</c>, the entire
/// expression evaluates to <c>null</c> instead of throwing an error.
/// For field assignment, see <see cref="DotAssignExpr"/>.
/// </remarks>
public class DotExpr : Expr
{
    /// <summary>
    /// Gets the receiver expression (the object being accessed).
    /// </summary>
    public Expr Object { get; }

    /// <summary>
    /// Gets the identifier token of the field or member being accessed.
    /// </summary>
    public Token Name { get; }

    /// <summary>
    /// Gets whether this is an optional chaining access (<c>?.</c>).
    /// When <c>true</c> and the receiver is <c>null</c>, the expression evaluates to <c>null</c>.
    /// </summary>
    public bool IsOptional { get; }

    /// <summary>
    /// Creates a new dot-access expression node.
    /// </summary>
    /// <param name="obj">The receiver expression.</param>
    /// <param name="name">The field or member name token.</param>
    /// <param name="span">The source span covering the entire dot expression.</param>
    /// <param name="isOptional"><c>true</c> for optional chaining (<c>?.</c>); <c>false</c> for regular access.</param>
    public DotExpr(Expr obj, Token name, SourceSpan span, bool isOptional = false) : base(span)
    {
        Object = obj;
        Name = name;
        IsOptional = isOptional;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitDotExpr(this);
}
