using System.Collections.Generic;
using Stash.Common;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a command literal expression: <c>$(command text {interpolation})</c>,
/// a streaming command: <c>$&lt;(...)</c> / <c>$!&lt;(...)</c>,
/// a passthrough command: <c>$&gt;(command text {interpolation})</c>, or a strict
/// command: <c>$!(...)</c> / <c>$!&gt;(...)</c>.
/// </summary>
/// <remarks>
/// Command literals execute a shell command. The default <see cref="CommandMode.Capture"/>
/// returns a struct-like result with <c>stdout</c>, <c>stderr</c>, and <c>exitCode</c> fields.
/// The command text is treated as raw shell input, with <c>{expr}</c> interpolation for
/// embedding Stash expression values.
/// <para>
/// When <see cref="Mode"/> is <see cref="CommandMode.Passthrough"/>, the command runs with
/// inherited I/O — stdin, stdout, and stderr flow directly to/from the terminal. The returned
/// <c>CommandResult</c> contains the exit code but empty <c>stdout</c>/<c>stderr</c> fields.
/// </para>
/// <para>
/// When <see cref="Mode"/> is <see cref="CommandMode.Stream"/>, the command yields a
/// <c>StreamingProcess</c> handle for line-by-line iteration. (Phase B implements runtime behavior.)
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
    /// Gets the stream-handling mode for this command literal.
    /// </summary>
    public CommandMode Mode { get; }

    /// <summary>
    /// Gets whether this command should run in passthrough mode with inherited I/O.
    /// Equivalent to <c>Mode == CommandMode.Passthrough</c>.
    /// </summary>
    public bool IsPassthrough => Mode == CommandMode.Passthrough;

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
    /// <param name="mode">The stream-handling mode (capture, stream, or passthrough).</param>
    /// <param name="isStrict">Whether this is a strict command (<c>$!(...)</c>) that throws on non-zero exit code.</param>
    public CommandExpr(List<Expr> parts, SourceSpan span, CommandMode mode = CommandMode.Capture, bool isStrict = false) : base(span, ExprType.Command)
    {
        Parts = parts;
        Mode = mode;
        IsStrict = isStrict;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor)
    {
        return visitor.VisitCommandExpr(this);
    }
}
