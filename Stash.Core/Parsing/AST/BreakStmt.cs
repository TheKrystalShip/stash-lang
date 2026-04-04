namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// A break statement: <c>break;</c>
/// </summary>
/// <remarks>
/// Valid only inside loop bodies (<see cref="WhileStmt"/>, <see cref="DoWhileStmt"/>, <see cref="ForInStmt"/>, <see cref="ForStmt"/>).
/// At runtime, implemented by throwing a <c>BreakException</c> that is caught by the enclosing loop.
/// </remarks>
public class BreakStmt : Stmt
{
    /// <summary>Initializes a new instance of <see cref="BreakStmt"/>.</summary>
    /// <param name="span">The source location of this statement.</param>
    public BreakStmt(SourceSpan span) : base(span, StmtType.Break) { }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitBreakStmt(this);
}
