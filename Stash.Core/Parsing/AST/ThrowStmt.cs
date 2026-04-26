namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A throw statement: <c>throw expr;</c> or bare <c>throw;</c> (re-throw inside catch).
/// </summary>
/// <remarks>
/// Valid anywhere a statement is permitted. When <see cref="Value"/> is <c>null</c>, this is a
/// bare re-throw that re-raises the original exception from the nearest enclosing catch block.
/// </remarks>
public class ThrowStmt : Stmt
{
    /// <summary>Gets the <c>throw</c> keyword token.</summary>
    public Token Keyword { get; }

    /// <summary>
    /// Gets the value expression to throw, or <c>null</c> for a bare <c>throw;</c> re-throw.
    /// </summary>
    public Expr? Value { get; }

    /// <summary>Initializes a new instance of <see cref="ThrowStmt"/>.</summary>
    /// <param name="keyword">The <c>throw</c> keyword token.</param>
    /// <param name="value">The value expression to throw, or <c>null</c> for bare re-throw.</param>
    /// <param name="span">The source location of this statement.</param>
    public ThrowStmt(Token keyword, Expr? value, SourceSpan span) : base(span, StmtType.Throw)
    {
        Keyword = keyword;
        Value = value;
    }

    /// <inheritdoc/>
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitThrowStmt(this);
}
