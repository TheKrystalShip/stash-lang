namespace Stash.Interpreting;

using System;

/// <summary>
/// Control-flow exception used to unwind when a <c>break</c> statement is
/// executed inside a loop.
/// </summary>
public class BreakException : Exception
{
    public BreakException() : base() { }
}
