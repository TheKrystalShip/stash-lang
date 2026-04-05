using System;

namespace Stash.Bytecode;

/// <summary>
/// Represents a captured variable (upvalue) in the bytecode VM.
/// While "open", the upvalue references a stack slot. When the owning function
/// returns, the upvalue is "closed" — the value is copied from the stack into
/// this object.
/// </summary>
internal sealed class Upvalue
{
    private StashValue[] _stack;
    private StashValue _closed;

    /// <summary>The stack index this upvalue references while open.</summary>
    public int StackIndex { get; }

    /// <summary>Whether this upvalue still points to a live stack slot.</summary>
    public bool IsOpen { get; private set; }

    public Upvalue(StashValue[] stack, int stackIndex)
    {
        _stack = stack;
        StackIndex = stackIndex;
        IsOpen = true;
    }

    /// <summary>Gets or sets the captured value.</summary>
    public StashValue Value
    {
        get => IsOpen ? _stack[StackIndex] : _closed;
        set
        {
            if (IsOpen)
            {
                _stack[StackIndex] = value;
            }
            else
            {
                _closed = value;
            }
        }
    }

    /// <summary>
    /// Closes this upvalue: copies the current stack value into <see cref="_closed"/>
    /// and marks the upvalue as closed. Subsequent reads/writes go through <see cref="_closed"/>.
    /// </summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        _closed = _stack[StackIndex];
        IsOpen = false;
    }

    /// <summary>
    /// Updates the stack array reference after the VM resizes the stack.
    /// Only affects open upvalues — closed upvalues store their value internally.
    /// </summary>
    internal void UpdateStack(StashValue[] newStack)
    {
        if (IsOpen)
        {
            _stack = newStack;
        }
    }
}
