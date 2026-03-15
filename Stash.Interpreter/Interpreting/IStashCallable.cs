namespace Stash.Interpreting;

using System.Collections.Generic;

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
    object? Call(Interpreter interpreter, List<object?> arguments);
}
