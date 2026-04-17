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
        return new StashError(error.Message, type, stack, error.Properties);
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
