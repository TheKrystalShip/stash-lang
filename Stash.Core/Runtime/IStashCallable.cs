namespace Stash.Runtime;

using System.Collections.Generic;
using Stash.Common;

/// <summary>
/// Interface for callable values in Stash (user-defined functions, built-in functions).
/// </summary>
public interface IStashCallable
{
    /// <summary>
    /// The number of arguments this callable expects.
    /// </summary>
    int Arity { get; }

    /// <summary>
    /// The minimum number of arguments required (for functions with default parameter values).
    /// </summary>
    int MinArity => Arity;

    /// <summary>
    /// Invokes the callable with the given arguments.
    /// </summary>
    object? Call(IInterpreterContext context, List<object?> arguments);

    /// <summary>
    /// Invokes the callable with `self` bound to the given instance.
    /// Used for struct method dispatch. Default implementation ignores the instance.
    /// </summary>
    object? CallWithSelf(IInterpreterContext context, object instance, List<object?> arguments)
        => Call(context, arguments);

    /// <summary>
    /// The name of this callable, or null for anonymous callables.
    /// </summary>
    string? Name => null;

    /// <summary>
    /// The source location where this callable is defined, or null for built-ins.
    /// </summary>
    SourceSpan? DefinitionSpan => null;
}
