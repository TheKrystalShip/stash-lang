namespace Stash.Bytecode;

using System.Collections.Generic;
using Stash.Runtime;

/// <summary>
/// Represents an active function invocation on the VM's call stack.
/// Stored in a flat array — mutated via <c>ref</c> access.
/// </summary>
internal struct CallFrame
{
    /// <summary>The compiled chunk being executed.</summary>
    public Chunk Chunk;

    /// <summary>Instruction pointer — index into <see cref="Chunk.Code"/>.</summary>
    public int IP;

    /// <summary>
    /// Stack index where this frame's local variables begin.
    /// Local slot N maps to <c>_stack[BaseSlot + N]</c>.
    /// </summary>
    public int BaseSlot;

    /// <summary>Captured upvalues for this closure invocation, or null for non-closures.</summary>
    public Upvalue[]? Upvalues;

    /// <summary>Function name for stack traces and error reporting.</summary>
    public string? FunctionName;

    /// <summary>
    /// The module globals for the function being executed, or <c>null</c> to use the VM's own globals.
    /// Set when calling an imported function that carries module-scoped definitions.
    /// </summary>
    public Dictionary<string, StashValue>? ModuleGlobals;
}
