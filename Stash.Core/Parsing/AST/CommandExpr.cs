using System.Collections.Generic;
using Stash.Common;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a command literal expression: <c>$(command text {interpolation})</c>,
/// a passthrough command: <c>$&gt;(command text {interpolation})</c>, or a strict
/// command: <c>$!(...)</c> / <c>$!&gt;(...)</c>.
/// </summary>
/// <remarks>
/// Command literals execute a shell command and return a struct-like result with
/// <c>stdout</c>, <c>stderr</c>, and <c>exitCode</c> fields. The command text is
/// treated as raw shell input, with <c>{expr}</c> interpolation for embedding Stash
/// expression values.
/// <para>
/// When <see cref="IsPassthrough"/> is <c>true</c>, the command runs with inherited I/O —
/// stdin, stdout, and stderr flow directly to/from the terminal. The returned
/// <c>CommandResult</c> contains the exit code but empty <c>stdout</c>/<c>stderr</c> fields.
/// </para>
/// <para>
/// When <see cref="IsStrict"/> is <c>true</c>, a non-zero exit code causes the interpreter
/// to throw a <c>CommandError</c> instead of returning the result normally.
/// </para>
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
    /// Gets whether this command should run in passthrough mode with inherited I/O.
    /// When <c>true</c>, the process's stdin/stdout/stderr are connected directly
    /// to the terminal rather than being captured.
    /// </summary>
    public bool IsPassthrough { get; }

    /// <summary>
    /// Gets whether this command uses strict mode (<c>$!(...)</c>), which throws a
    /// <c>CommandError</c> on non-zero exit codes instead of returning normally.
    /// </summary>
    public bool IsStrict { get; }

    /// <summary>
    /// Creates a new command literal expression node.
    /// </summary>
    /// <param name="parts">The ordered list of text and expression parts.</param>
    /// <param name="span">The source span covering the entire command literal.</param>
    /// <param name="isPassthrough">Whether this is a passthrough command (<c>$&gt;(...)</c>).</param>
    /// <param name="isStrict">Whether this is a strict command (<c>$!(...)</c>) that throws on non-zero exit code.</param>
    public CommandExpr(List<Expr> parts, SourceSpan span, bool isPassthrough = false, bool isStrict = false) : base(span, ExprType.Command)
    {
        Parts = parts;
        IsPassthrough = isPassthrough;
        IsStrict = isStrict;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor)
    {
        return visitor.VisitCommandExpr(this);
    }
}
