namespace Stash.Runtime.Types;

using System;
using System.Collections.Generic;
using Stash.Common;

public class BuiltInBoundMethod : IStashCallable
{
    private readonly object? _receiver;
    private readonly IStashCallable _function;

    public BuiltInBoundMethod(object? receiver, IStashCallable function)
    {
        _receiver = receiver;
        _function = function;
    }

    public int Arity => _function.Arity == -1 ? -1 : _function.Arity - 1;

    public int MinArity => Math.Max(0, _function.MinArity == -1 ? 0 : _function.MinArity - 1);

    public string? Name => _function.Name;

    public SourceSpan? DefinitionSpan => _function.DefinitionSpan;

    public object? Call(IInterpreterContext context, List<object?> arguments)
    {
        var newArgs = new List<object?>(arguments.Count + 1) { _receiver };
        newArgs.AddRange(arguments);
        return _function.Call(context, newArgs);
    }

    public StashValue CallDirect(IInterpreterContext context, ReadOnlySpan<StashValue> arguments)
    {
        StashValue[] newArgs = new StashValue[arguments.Length + 1];
        newArgs[0] = StashValue.FromObject(_receiver);
        arguments.CopyTo(newArgs.AsSpan(1));
        return _function.CallDirect(context, newArgs);
    }

    public override string ToString() => $"<bound method {_function.Name}>";
}
