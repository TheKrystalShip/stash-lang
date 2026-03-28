namespace Stash.Runtime.Types;

using System.Collections.Generic;
using Stash.Common;

/// <summary>
/// A method bound to a specific struct instance. When called, injects the instance
/// as 'self' into the method's environment before executing the body.
/// </summary>
public class StashBoundMethod : IStashCallable
{
    private readonly StashInstance _instance;
    private readonly IStashCallable _method;

    public StashInstance Instance => _instance;
    public IStashCallable Method => _method;

    public StashBoundMethod(StashInstance instance, IStashCallable method)
    {
        _instance = instance;
        _method = method;
    }

    public int Arity => _method.Arity;
    public int MinArity => _method.MinArity;

    /// <summary>
    /// The source location where this method is defined, if available.
    /// </summary>
    public SourceSpan? DefinitionSpan => _method.DefinitionSpan;

    public object? Call(IInterpreterContext context, List<object?> arguments)
    {
        return _method.CallWithSelf(context, _instance, arguments);
    }

    public override string ToString() => $"<method {_method}>";
}
