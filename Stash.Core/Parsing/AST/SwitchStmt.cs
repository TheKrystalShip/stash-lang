using System.Collections.Generic;
using Stash.Common;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a single case arm in a <see cref="SwitchStmt"/>: one or more patterns (or the default) mapped to a block body.
/// </summary>
/// <param name="Patterns">The list of value patterns to match against the subject (e.g. <c>1, 2, 3</c>). Empty for a default arm.</param>
/// <param name="IsDefault"><see langword="true"/> when this arm uses the <c>default</c> keyword (matches anything).</param>
/// <param name="Body">The block statement executed when this arm matches.</param>
/// <param name="Span">The source span covering the entire case arm.</param>
public record SwitchCase(List<Expr> Patterns, bool IsDefault, Stmt Body, SourceSpan Span);

/// <summary>
/// Represents a switch statement: <c>switch (value) { case 1, 2: { ... } default: { ... } }</c>.
/// </summary>
/// <remarks>
/// Cases are evaluated in order; the first matching case wins. There is no fallthrough — each
/// case arm requires a block body. A <c>default</c> arm matches any value not covered by a
/// preceding case and is optional. Multiple patterns per case are separated by commas.
/// </remarks>
public class SwitchStmt : Stmt
{
    /// <summary>Gets the expression whose value is matched against each case's patterns.</summary>
    public Expr Subject { get; }

    /// <summary>Gets the ordered list of cases to evaluate against <see cref="Subject"/>.</summary>
    public List<SwitchCase> Cases { get; }

    /// <summary>
    /// Creates a new switch statement node.
    /// </summary>
    /// <param name="subject">The expression being switched on.</param>
    /// <param name="cases">The ordered list of switch cases.</param>
    /// <param name="span">The source span covering the entire switch statement.</param>
    public SwitchStmt(Expr subject, List<SwitchCase> cases, SourceSpan span) : base(span, StmtType.Switch)
    {
        Subject = subject;
        Cases = cases;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitSwitchStmt(this);
}
