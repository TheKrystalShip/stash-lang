namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// A continue statement: <c>continue;</c>
/// </summary>
/// <remarks>
/// Valid only inside loop bodies (<see cref="WhileStmt"/>, <see cref="DoWhileStmt"/>, <see cref="ForInStmt"/>, <see cref="ForStmt"/>).
/// At runtime, implemented by throwing a <c>ContinueException</c> that is caught by the enclosing loop.
/// </remarks>
public class ContinueStmt : Stmt
{
    /// <summary>Initializes a new instance of <see cref="ContinueStmt"/>.</summary>
    /// <param name="span">The source location of this statement.</param>
    public ContinueStmt(SourceSpan span) : base(span) { }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitContinueStmt(this);
}
