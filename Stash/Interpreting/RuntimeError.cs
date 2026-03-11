namespace Stash.Interpreting;

using System;
using Stash.Common;

/// <summary>
/// Exception thrown when the interpreter encounters an error during expression evaluation.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RuntimeError"/> represents errors that occur during execution of a Stash program,
/// such as type mismatches (e.g., applying arithmetic to non-numeric operands), division by zero,
/// or referencing undefined variables. This is distinct from lex errors and parse errors, which
/// are detected before execution begins — runtime errors can only be discovered when the
/// interpreter actually evaluates the offending expression.
/// </para>
/// <para>
/// Each <see cref="RuntimeError"/> carries an optional <see cref="SourceSpan"/> so the REPL or
/// runner can report the exact source location (line and column) where the error occurred.
/// The span may be <c>null</c> for errors that cannot be attributed to a specific token.
/// </para>
/// </remarks>
public class RuntimeError : Exception
{
    /// <summary>
    /// The source location of the token or expression that caused the runtime error,
    /// or <c>null</c> if no specific location is available.
    /// </summary>
    public SourceSpan? Span { get; }

    /// <summary>
    /// Initializes a new <see cref="RuntimeError"/> with a human-readable message and an
    /// optional source location.
    /// </summary>
    /// <param name="message">A description of what went wrong (e.g., "Division by zero.").</param>
    /// <param name="span">
    /// The <see cref="SourceSpan"/> of the operator or expression that triggered the error.
    /// When provided, the REPL uses this to display <c>[runtime error at line:column]</c>.
    /// </param>
    public RuntimeError(string message, SourceSpan? span = null) : base(message)
    {
        Span = span;
    }
}
