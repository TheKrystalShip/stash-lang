namespace Stash.Interpreting.Exceptions;

using System;

/// <summary>
/// Control-flow exception used to skip to the next iteration when a <c>continue</c>
/// statement is executed inside a loop.
/// </summary>
public class ContinueException : Exception
{
    public ContinueException() : base() { }
}
