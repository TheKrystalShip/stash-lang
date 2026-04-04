using Stash.Common;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a ternary conditional expression: <c>condition ? thenBranch : elseBranch</c>.
/// </summary>
/// <remarks>
/// The ternary operator is right-associative: <c>a ? b : c ? d : e</c> parses as
/// <c>a ? b : (c ? d : e)</c>. This is achieved in the parser by calling
/// <see cref="Parser.Parse"/> (via <c>Expression()</c>) for the branch sub-expressions,
/// which re-enters at the lowest precedence level.
/// </remarks>
/// <example>
/// The Stash expression <c>x ? "yes" : "no"</c> produces:
/// <code>new TernaryExpr(identExpr, yesLiteral, noLiteral, span)</code>
/// </example>
public class TernaryExpr : Expr
{
    /// <summary>
    /// Gets the condition expression evaluated to determine which branch to take.
    /// </summary>
    public Expr Condition { get; }

    /// <summary>
    /// Gets the expression evaluated when <see cref="Condition"/> is truthy.
    /// </summary>
    public Expr ThenBranch { get; }

    /// <summary>
    /// Gets the expression evaluated when <see cref="Condition"/> is falsy.
    /// </summary>
    public Expr ElseBranch { get; }

    /// <summary>
    /// Creates a new ternary conditional expression node.
    /// </summary>
    /// <param name="condition">The condition expression.</param>
    /// <param name="thenBranch">The expression for the truthy branch.</param>
    /// <param name="elseBranch">The expression for the falsy branch.</param>
    /// <param name="span">The source span covering the entire ternary expression.</param>
    public TernaryExpr(Expr condition, Expr thenBranch, Expr elseBranch, SourceSpan span) : base(span, ExprType.Ternary)
    {
        Condition = condition;
        ThenBranch = thenBranch;
        ElseBranch = elseBranch;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitTernaryExpr(this);
}
