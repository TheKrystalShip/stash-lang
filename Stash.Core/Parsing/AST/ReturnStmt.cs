namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// A return statement: <c>return expr;</c> or <c>return;</c>
/// </summary>
/// <remarks>
/// Valid only inside function or lambda bodies. When <see cref="Value"/> is <c>null</c>, the function
/// returns <c>null</c>. At runtime, implemented by throwing a <c>ReturnException</c> carrying the return value.
/// </remarks>
public class ReturnStmt : Stmt
{
    /// <summary>Gets the optional return value expression. <c>null</c> for bare <c>return;</c> statements.</summary>
    public Expr? Value { get; }

    /// <summary>Initializes a new instance of <see cref="ReturnStmt"/>.</summary>
    /// <param name="value">The optional return value expression, or <c>null</c>.</param>
    /// <param name="span">The source location of this statement.</param>
    public ReturnStmt(Expr? value, SourceSpan span) : base(span)
    {
        Value = value;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitReturnStmt(this);
}
