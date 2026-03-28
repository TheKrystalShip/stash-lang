namespace Stash.Interpreting;

using System;
using System.Collections.Generic;

/// <summary>
/// Wraps a native C# delegate as a Stash-callable function.
/// Used for all built-in functions across every namespace (io, arr, str, etc.).
/// </summary>
public class BuiltInFunction : IStashCallable
{
    private readonly string _name;
    private readonly Func<Interpreter, List<object?>, object?> _body;

    public int Arity { get; }
    public string Name => _name;

    public BuiltInFunction(string name, int arity, Func<Interpreter, List<object?>, object?> body)
    {
        _name = name;
        Arity = arity;
        _body = body;
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        return _body(interpreter, arguments);
    }

    public override string ToString() => $"<built-in fn {_name}>";
}
