namespace Stash.Runtime.Types;

using System.Collections.Generic;
using Stash.Common;

/// <summary>
/// Represents a first-class error value in the Stash runtime.
/// </summary>
public class StashError
{
    public string Message { get; }
    public string Type { get; }
    public List<string>? Stack { get; }

    public StashError(string message, string type, List<string>? stack = null)
    {
        Message = message;
        Type = type;
        Stack = stack;
    }

    /// <summary>
    /// Creates a <see cref="StashError"/> from a caught <see cref="RuntimeError"/> and an optional call stack.
    /// </summary>
    public static StashError FromRuntimeError(RuntimeError error, IReadOnlyList<(string FunctionName, SourceSpan CallSite)>? callStack)
    {
        List<string>? stack = null;
        if (callStack is { Count: > 0 })
        {
            stack = new List<string>(callStack.Count);
            for (int i = callStack.Count - 1; i >= 0; i--)
            {
                var frame = callStack[i];
                stack.Add($"  at {frame.FunctionName} ({frame.CallSite})");
            }
        }

        string type = error.ErrorType ?? "RuntimeError";
        return new StashError(error.Message, type, stack);
    }

    public override string ToString() => $"{Type}: {Message}";
}
