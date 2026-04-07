namespace Stash.Runtime;

using System;
using System.Collections.Generic;

/// <summary>
/// Wraps a native C# delegate as a Stash-callable function.
/// Used for all built-in functions across every namespace (io, arr, str, etc.).
/// </summary>
public sealed class BuiltInFunction : IStashCallable
{
    public delegate StashValue DirectHandler(IInterpreterContext context, ReadOnlySpan<StashValue> args);

    private readonly DirectHandler _directBody;

    public int Arity { get; }
    public string Name { get; }

    public BuiltInFunction(string name, int arity, DirectHandler body)
    {
        Name = name;
        Arity = arity;
        _directBody = body;
    }

    public object? Call(IInterpreterContext context, List<object?> arguments)
    {
        // Bridge legacy callers through the direct path
        StashValue[] svArgs = new StashValue[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
            svArgs[i] = StashValue.FromObject(arguments[i]);
        return CallDirect(context, svArgs).ToObject();
    }

    public StashValue CallDirect(IInterpreterContext context, ReadOnlySpan<StashValue> arguments)
    {
        return _directBody(context, arguments);
    }

    public override string ToString() => $"<built-in fn {Name}>";
}
