namespace Stash.Debugging;

using System.Collections.Generic;

/// <summary>
/// Abstraction over an execution backend for debugger inspection.
/// Implemented by <c>Interpreter</c> (tree-walk) and <c>VMDebugAdapter</c> (bytecode).
/// Provides the operations that <c>DebugSession</c> needs from the running executor.
/// </summary>
public interface IDebugExecutor
{
    /// <summary>Current call stack frames (innermost last).</summary>
    IReadOnlyList<CallFrame> CallStack { get; }

    /// <summary>The global scope.</summary>
    IDebugScope GlobalScope { get; }

    /// <summary>
    /// Evaluates an ad-hoc expression string in the given scope.
    /// Returns (value, null) on success or (null, errorMessage) on failure.
    /// Used for watch expressions, conditional breakpoints, and debug console evaluation.
    /// </summary>
    (object? Value, string? Error) EvaluateExpression(string expression, IDebugScope scope);
}
