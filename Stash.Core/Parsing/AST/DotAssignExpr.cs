namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents a dot-assignment expression: <c>obj.field = value</c>.
/// </summary>
/// <remarks>
/// Used for assigning to struct instance fields and dictionary entries accessed via dot syntax.
/// Assignment to namespace members is a runtime error. For read access, see <see cref="DotExpr"/>.
/// </remarks>
public class DotAssignExpr : Expr
{
    /// <summary>
    /// Gets the receiver expression (the object whose field is being assigned).
    /// </summary>
    public Expr Object { get; }

    /// <summary>
    /// Gets the identifier token of the field being assigned to.
    /// </summary>
    public Token Name { get; }

    /// <summary>
    /// Gets the expression whose result will be assigned to the field.
    /// </summary>
    public Expr Value { get; }

    /// <summary>
    /// Creates a new dot-assignment expression node.
    /// </summary>
    /// <param name="obj">The receiver expression.</param>
    /// <param name="name">The field name token.</param>
    /// <param name="value">The expression to assign to the field.</param>
    /// <param name="span">The source span covering the entire dot assignment.</param>
    public DotAssignExpr(Expr obj, Token name, Expr value, SourceSpan span) : base(span)
    {
        Object = obj;
        Name = name;
        Value = value;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitDotAssignExpr(this);
}
