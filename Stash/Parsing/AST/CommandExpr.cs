using System.Collections.Generic;
using Stash.Common;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a command literal expression: <c>$(command text {interpolation})</c>.
/// </summary>
/// <remarks>
/// Command literals execute a shell command and return a struct-like result with
/// <c>stdout</c>, <c>stderr</c>, and <c>exitCode</c> fields. The command text is
/// treated as raw shell input, with <c>{expr}</c> interpolation for embedding Stash
/// expression values.
/// </remarks>
public class CommandExpr : Expr
{
    /// <summary>
    /// Gets the ordered list of parts that make up this command.
    /// Each part is either a <see cref="LiteralExpr"/> containing raw command text
    /// or an arbitrary expression to be evaluated and interpolated.
    /// </summary>
    public List<Expr> Parts { get; }

    /// <summary>
    /// Creates a new command literal expression node.
    /// </summary>
    /// <param name="parts">The ordered list of text and expression parts.</param>
    /// <param name="span">The source span covering the entire command literal.</param>
    public CommandExpr(List<Expr> parts, SourceSpan span) : base(span)
    {
        Parts = parts;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor)
    {
        return visitor.VisitCommandExpr(this);
    }
}
