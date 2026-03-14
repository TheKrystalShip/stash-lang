using System.Collections.Generic;
using Stash.Common;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a single arm in a <see cref="SwitchExpr"/>: a pattern (or discard) mapped to a result expression.
/// </summary>
/// <param name="Pattern">The value pattern to match against the subject, or <see langword="null"/> for a discard arm.</param>
/// <param name="IsDiscard"><see langword="true"/> when this arm uses the <c>_</c> wildcard (matches anything).</param>
/// <param name="Body">The expression evaluated and returned when this arm matches.</param>
/// <param name="Span">The source span covering the entire arm.</param>
public record SwitchArm(Expr? Pattern, bool IsDiscard, Expr Body, SourceSpan Span);

/// <summary>
/// Represents a switch expression: <c>subject switch { pattern =&gt; result, ... }</c>.
/// </summary>
/// <remarks>
/// Arms are evaluated in order; the first matching arm wins. A discard arm (<c>_</c>) acts as
/// the default case and should appear last. If no arm matches at runtime a <c>RuntimeError</c>
/// is raised.
/// </remarks>
public class SwitchExpr : Expr
{
    /// <summary>Gets the expression whose value is matched against each arm's pattern.</summary>
    public Expr Subject { get; }

    /// <summary>Gets the ordered list of arms to evaluate against <see cref="Subject"/>.</summary>
    public List<SwitchArm> Arms { get; }

    /// <summary>
    /// Creates a new switch expression node.
    /// </summary>
    /// <param name="subject">The expression being switched on.</param>
    /// <param name="arms">The ordered list of switch arms.</param>
    /// <param name="span">The source span covering the entire switch expression.</param>
    public SwitchExpr(Expr subject, List<SwitchArm> arms, SourceSpan span) : base(span)
    {
        Subject = subject;
        Arms = arms;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitSwitchExpr(this);
}
