namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A throw statement: <c>throw expr;</c>
/// </summary>
/// <remarks>
/// Valid anywhere a statement is permitted. At runtime, evaluated by the interpreter
/// to produce a <see cref="Stash.Interpreting.Types.StashError"/> value and unwind the call stack
/// until a surrounding <c>try</c> expression catches it.
/// </remarks>
public class ThrowStmt : Stmt
{
    /// <summary>Gets the <c>throw</c> keyword token.</summary>
    public Token Keyword { get; }

    /// <summary>Gets the value expression to throw. Always required — bare <c>throw;</c> is a syntax error.</summary>
    public Expr Value { get; }

    /// <summary>Initializes a new instance of <see cref="ThrowStmt"/>.</summary>
    /// <param name="keyword">The <c>throw</c> keyword token.</param>
    /// <param name="value">The value expression to throw.</param>
    /// <param name="span">The source location of this statement.</param>
    public ThrowStmt(Token keyword, Expr value, SourceSpan span) : base(span)
    {
        Keyword = keyword;
        Value = value;
    }

    /// <inheritdoc/>
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitThrowStmt(this);
}
