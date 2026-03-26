namespace Stash.Interpreting.Types;

using System.Collections.Generic;
using System.Text;
using Stash.Debugging;

/// <summary>
/// Represents a first-class error value in the Stash runtime.
/// </summary>
/// <remarks>
/// <see cref="StashError"/> is a runtime value, not a C# exception. It is produced by
/// <c>throw</c> statements and caught by <c>try</c> expressions. The <see cref="Type"/>
/// distinguishes error categories (e.g., <c>"Error"</c>, <c>"TypeError"</c>), while
/// <see cref="Stack"/> optionally captures the call-stack frames at the throw site.
/// </remarks>
public class StashError
{
    /// <summary>Gets the human-readable error message.</summary>
    public string Message { get; }

    /// <summary>Gets the error type name (e.g., <c>"Error"</c>, <c>"TypeError"</c>).</summary>
    public string Type { get; }

    /// <summary>Gets the optional stack trace frames captured at the throw site.</summary>
    public List<string>? Stack { get; }

    /// <summary>
    /// Initializes a new <see cref="StashError"/> with the given message, type, and optional stack.
    /// </summary>
    /// <param name="message">The human-readable description of the error.</param>
    /// <param name="type">The error type name.</param>
    /// <param name="stack">Optional stack trace frames.</param>
    public StashError(string message, string type, List<string>? stack = null)
    {
        Message = message;
        Type = type;
        Stack = stack;
    }

    /// <summary>
    /// Creates a <see cref="StashError"/> from a caught <see cref="RuntimeError"/> and an optional call stack.
    /// </summary>
    /// <param name="error">The runtime error to wrap.</param>
    /// <param name="callStack">The active call stack at the time the error was thrown, or <c>null</c>.</param>
    /// <returns>A new <see cref="StashError"/> wrapping the runtime error.</returns>
    public static StashError FromRuntimeError(RuntimeError error, List<CallFrame>? callStack)
    {
        List<string>? stack = null;
        if (callStack is { Count: > 0 })
        {
            stack = new List<string>(callStack.Count);
            for (int i = callStack.Count - 1; i >= 0; i--)
            {
                CallFrame frame = callStack[i];
                stack.Add($"  at {frame.FunctionName} ({frame.CallSite})");
            }
        }

        string type = error.ErrorType ?? "RuntimeError";
        return new StashError(error.Message, type, stack);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Type}: {Message}";
}
