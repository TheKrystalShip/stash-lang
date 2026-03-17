namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// Represents an index assignment expression: <c>obj[index] = value</c>.
/// </summary>
/// <remarks>
/// Used for assigning to array elements (<c>arr[0] = x</c>) and dictionary entries
/// (<c>dict["key"] = x</c>). The <see cref="BracketSpan"/> is the source location of the
/// <c>[</c> token, preserved for precise error reporting.
/// </remarks>
public class IndexAssignExpr : Expr
{
    /// <summary>
    /// Gets the expression being indexed (an array or dictionary).
    /// </summary>
    public Expr Object { get; }

    /// <summary>
    /// Gets the index expression (an integer for arrays, any non-null value for dictionaries).
    /// </summary>
    public Expr Index { get; }

    /// <summary>
    /// Gets the expression whose result will be assigned at the given index.
    /// </summary>
    public Expr Value { get; }

    /// <summary>
    /// Gets the source span of the <c>[</c> bracket token, used for precise error reporting.
    /// </summary>
    public SourceSpan BracketSpan { get; }

    /// <summary>
    /// Creates a new index assignment expression node.
    /// </summary>
    /// <param name="obj">The expression being indexed.</param>
    /// <param name="index">The index expression.</param>
    /// <param name="value">The expression to assign at the given index.</param>
    /// <param name="bracketSpan">The source span of the opening bracket.</param>
    /// <param name="span">The source span covering the entire index assignment.</param>
    public IndexAssignExpr(Expr obj, Expr index, Expr value, SourceSpan bracketSpan, SourceSpan span) : base(span)
    {
        Object = obj;
        Index = index;
        Value = value;
        BracketSpan = bracketSpan;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitIndexAssignExpr(this);
}
