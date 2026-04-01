namespace Stash.Interpreting.Types;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;

/// <summary>
/// A bound method for extension methods on built-in types. Binds the receiver value as <c>self</c>
/// in the method's closure environment before execution.
/// </summary>
/// <remarks>
/// Unlike <see cref="Stash.Runtime.Types.StashBoundMethod"/> (which binds <c>self</c> to a
/// <see cref="Stash.Runtime.Types.StashInstance"/>), this class binds <c>self</c> to an arbitrary
/// value — a string, array, int, float, or dict. The method body accesses <c>self</c> as a
/// regular variable in its scope.
/// </remarks>
internal class ExtensionBoundMethod : IStashCallable
{
    private readonly object? _receiver;
    private readonly StashFunction _function;

    public ExtensionBoundMethod(object? receiver, StashFunction function)
    {
        _receiver = receiver;
        _function = function;
    }

    public int Arity => _function.Arity;
    public int MinArity => _function.MinArity;
    public string? Name => _function.Name;
    public SourceSpan? DefinitionSpan => _function.DefinitionSpan;

    public object? Call(IInterpreterContext context, List<object?> arguments)
    {
        return _function.CallWithSelfValue(context, _receiver, arguments);
    }

    public override string ToString() => $"<extension method {_function.Name}>";
}
