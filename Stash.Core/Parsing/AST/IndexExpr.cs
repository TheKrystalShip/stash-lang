namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// Represents an index access expression: <c>obj[index]</c>.
/// </summary>
/// <remarks>
/// Used for arrays (<c>arr[0]</c>), strings (<c>str[i]</c>), and dictionaries (<c>dict["key"]</c>).
/// The <see cref="BracketSpan"/> is the source location of the <c>[</c> token, preserved
/// separately for precise error reporting on index-related errors (out of bounds, type mismatch).
/// </remarks>
public class IndexExpr : Expr
{
    /// <summary>
    /// Gets the expression being indexed (an array, string, or dictionary).
    /// </summary>
    public Expr Object { get; }

    /// <summary>
    /// Gets the index expression (an integer for arrays/strings, any non-null value for dictionaries).
    /// </summary>
    public Expr Index { get; }

    /// <summary>
    /// Gets the source span of the <c>[</c> bracket token, used for precise error reporting.
    /// </summary>
    public SourceSpan BracketSpan { get; }

    /// <summary>
    /// Creates a new index access expression node.
    /// </summary>
    /// <param name="obj">The expression being indexed.</param>
    /// <param name="index">The index expression.</param>
    /// <param name="bracketSpan">The source span of the opening bracket.</param>
    /// <param name="span">The source span covering the entire index expression.</param>
    public IndexExpr(Expr obj, Expr index, SourceSpan bracketSpan, SourceSpan span) : base(span, ExprType.Index)
    {
        Object = obj;
        Index = index;
        BracketSpan = bracketSpan;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitIndexExpr(this);
}
