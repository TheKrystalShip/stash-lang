namespace Stash.Interpreting.Types;

using System.Collections.Generic;

/// <summary>
/// A method bound to a specific struct instance. When called, injects the instance
/// as 'self' into the method's environment before executing the body.
/// </summary>
public class StashBoundMethod : IStashCallable
{
    private readonly StashInstance _instance;
    private readonly StashFunction _method;

    public StashBoundMethod(StashInstance instance, StashFunction method)
    {
        _instance = instance;
        _method = method;
    }

    public int Arity => _method.Arity;
    public int MinArity => _method.MinArity;

    /// <summary>
    /// The source location where this method is defined.
    /// </summary>
    public Stash.Common.SourceSpan DefinitionSpan => _method.DefinitionSpan;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        return _method.CallWithSelf(interpreter, _instance, arguments);
    }

    public override string ToString() => $"<method {_method}>";
}
