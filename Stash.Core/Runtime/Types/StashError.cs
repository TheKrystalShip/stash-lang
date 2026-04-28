namespace Stash.Runtime.Types;

using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Protocols;

/// <summary>
/// Represents a first-class error value in the Stash runtime.
/// </summary>
public class StashError : IVMTyped, IVMFieldAccessible, IVMTruthiness, IVMStringifiable
{
    public string Message { get; }
    public string Type { get; }
    public List<string>? Stack { get; }
    public Dictionary<string, object?>? Properties { get; }

    public StashError(string message, string type, List<string>? stack = null, Dictionary<string, object?>? properties = null)
    {
        Message = message;
        Type = type;
        Stack = stack;
        Properties = properties;
    }

    /// <summary>Errors from deferred cleanup that occurred during this error's propagation.</summary>
    public List<StashError>? Suppressed { get; set; }

    /// <summary>
    /// The original <see cref="RuntimeError"/> that produced this StashError.
    /// Set by the VM when catching an exception. Used by bare <c>throw;</c> rethrow
    /// to preserve the original source span and call stack. Not accessible from Stash code.
    /// </summary>
    public RuntimeError? OriginalException { get; set; }

    /// <summary>
    /// Creates a <see cref="StashError"/> from a caught <see cref="RuntimeError"/> and pre-formatted
    /// stack lines and suppressed errors. This overload is used by the VM dispatch loop which builds
    /// its own stack-line strings from <c>StackFrame</c> values.
    /// </summary>
    public static StashError FromRuntimeError(RuntimeError error, List<string>? stackLines, List<StashError>? suppressed = null)
    {
        string type = error.ErrorType ?? "RuntimeError";
        var stashError = new StashError(error.Message, type, stackLines, error.Properties);
        stashError.OriginalException = error;
        if (suppressed is { Count: > 0 })
            stashError.Suppressed = suppressed;
        else if (error.SuppressedErrors is { Count: > 0 })
            stashError.Suppressed = error.SuppressedErrors;
        return stashError;
    }

    /// <summary>
    /// Creates a <see cref="StashError"/> from a caught <see cref="RuntimeError"/>, an optional call stack,
    /// and an optional list of suppressed errors collected during frame unwinding.
    /// When <paramref name="suppressed"/> is provided it takes precedence over
    /// <see cref="RuntimeError.SuppressedErrors"/> (the caller is responsible for merging them).
    /// </summary>
    public static StashError FromRuntimeError(RuntimeError error, IReadOnlyList<(string FunctionName, SourceSpan CallSite)>? callStack, List<StashError>? suppressed = null)
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
        var stashError = new StashError(error.Message, type, stack, error.Properties);
        stashError.OriginalException = error;

        // When the caller provides an explicit suppressed list (already merged with error.SuppressedErrors),
        // use it. Otherwise fall back to the error's own suppressed errors.
        if (suppressed is { Count: > 0 })
            stashError.Suppressed = suppressed;
        else if (error.SuppressedErrors is { Count: > 0 })
            stashError.Suppressed = error.SuppressedErrors;

        return stashError;
    }

    public override string ToString() => $"{Type}: {Message}";

    // --- VM Protocol Implementations ---

    public string VMTypeName => "Error";

    public bool VMTryGetField(string name, out StashValue value, SourceSpan? span)
    {
        switch (name)
        {
            case "message":
                value = StashValue.FromObj(Message);
                return true;
            case "type":
                value = StashValue.FromObj(Type);
                return true;
            case "stack":
                if (Stack is not null)
                {
                    value = StashValue.FromObj(new List<StashValue>(Stack.Select(s => StashValue.FromObj(s))));
                    return true;
                }
                value = StashValue.Null;
                return true;
            case "suppressed":
                if (Suppressed is { Count: > 0 })
                {
                    value = StashValue.FromObj(new List<StashValue>(Suppressed.Select(s => StashValue.FromObj(s))));
                }
                else
                {
                    value = StashValue.FromObj(new List<StashValue>());
                }
                return true;
            default:
                if (Properties?.TryGetValue(name, out object? propVal) == true)
                {
                    value = StashValue.FromObject(propVal);
                    return true;
                }
                value = StashValue.Null;
                return false;
        }
    }

    public bool VMIsFalsy => true;

    public string VMToString() => ToString();
}
