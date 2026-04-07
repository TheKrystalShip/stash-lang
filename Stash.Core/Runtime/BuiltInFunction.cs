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

    private readonly Func<IInterpreterContext, List<object?>, object?>? _legacyBody;
    private readonly DirectHandler? _directBody;

    public int Arity { get; }
    public string Name { get; }

    public BuiltInFunction(string name, int arity, Func<IInterpreterContext, List<object?>, object?> body)
    {
        Name = name;
        Arity = arity;
        _legacyBody = body;
    }

    public BuiltInFunction(string name, int arity, DirectHandler body)
    {
        Name = name;
        Arity = arity;
        _directBody = body;
    }

    public object? Call(IInterpreterContext context, List<object?> arguments)
    {
        if (_legacyBody is not null)
            return _legacyBody(context, arguments);

        // Direct-only path: convert List<object?> → StashValue[] and call through
        StashValue[] svArgs = new StashValue[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
            svArgs[i] = StashValue.FromObject(arguments[i]);
        return CallDirect(context, svArgs).ToObject();
    }

    public StashValue CallDirect(IInterpreterContext context, ReadOnlySpan<StashValue> arguments)
    {
        if (_directBody is not null)
            return _directBody(context, arguments);

        var list = new List<object?>(arguments.Length);
        foreach (StashValue sv in arguments)
            list.Add(sv.ToObject());
        return StashValue.FromObject(_legacyBody!(context, list));
    }

    public override string ToString() => $"<built-in fn {Name}>";
}
