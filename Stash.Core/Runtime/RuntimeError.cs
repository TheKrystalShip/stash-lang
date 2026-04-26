namespace Stash.Runtime;

using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime.Types;

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
    /// The error type name supplied by user code via <c>throw { type: "...", message: "..." }</c>,
    /// or <c>null</c> for built-in runtime errors (which default to <c>"RuntimeError"</c>).
    /// </summary>
    public string? ErrorType { get; }

    /// <summary>
    /// Optional dictionary of additional typed properties carried by this error.
    /// Used by <c>CommandError</c> to expose <c>exitCode</c>, <c>stderr</c>, <c>stdout</c>,
    /// and <c>command</c> fields, and by dict-based <c>throw</c> to preserve extra fields
    /// beyond <c>type</c> and <c>message</c>.
    /// </summary>
    public Dictionary<string, object?>? Properties { get; init; }

    /// <summary>
    /// Errors from deferred cleanup that occurred during this error's propagation.
    /// </summary>
    public List<StashError>? SuppressedErrors { get; init; }

    /// <summary>
    /// The call stack captured when this error was thrown or caught, or <c>null</c> if not captured.
    /// Set by the VM at the catch boundary or unhandled boundary.
    /// </summary>
    public List<StackFrame>? CallStack { get; set; }

    /// <summary>
    /// Initializes a new <see cref="RuntimeError"/> with a human-readable message and an
    /// optional source location.
    /// </summary>
    public RuntimeError(string message, SourceSpan? span = null, string? errorType = null) : base(message)
    {
        Span = span;
        ErrorType = errorType;
    }
}
