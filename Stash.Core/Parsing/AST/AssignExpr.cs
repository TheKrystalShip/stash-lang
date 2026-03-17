namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents a variable assignment expression: <c>x = value</c>.
/// </summary>
/// <remarks>
/// This node is produced when the parser encounters an identifier on the left side of
/// an <c>=</c> operator. Compound assignments (<c>+=</c>, <c>-=</c>, etc.) are desugared
/// by the parser into a <see cref="BinaryExpr"/> wrapped in an <see cref="AssignExpr"/>.
/// For field assignments (<c>obj.field = value</c>), see <see cref="DotAssignExpr"/>.
/// For index assignments (<c>arr[i] = value</c>), see <see cref="IndexAssignExpr"/>.
/// </remarks>
public class AssignExpr : Expr
{
    /// <summary>
    /// Gets the identifier token of the variable being assigned to.
    /// </summary>
    public Token Name { get; }

    /// <summary>
    /// Gets the expression whose result will be assigned to the variable.
    /// </summary>
    public Expr Value { get; }

    /// <summary>
    /// Creates a new assignment expression node.
    /// </summary>
    /// <param name="name">The identifier token of the target variable.</param>
    /// <param name="value">The expression to evaluate and assign.</param>
    /// <param name="span">The source span covering the entire assignment expression.</param>
    public AssignExpr(Token name, Expr value, SourceSpan span) : base(span)
    {
        Name = name;
        Value = value;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitAssignExpr(this);
}
