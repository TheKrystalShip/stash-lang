namespace Stash.Interpreting.Exceptions;

using System;

/// <summary>
/// Control-flow exception used to unwind the call stack when a <c>return</c>
/// statement is executed inside a function body.
/// </summary>
public class ReturnException : Exception
{
    public object? Value { get; }

    public ReturnException(object? value) : base()
    {
        Value = value;
    }
}
