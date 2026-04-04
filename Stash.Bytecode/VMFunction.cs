namespace Stash.Bytecode;

using System.Collections.Generic;
using Stash.Runtime;

/// <summary>
/// A compiled closure: a <see cref="Chunk"/> paired with captured <see cref="Upvalue"/> instances.
/// Created by <c>OP_CLOSURE</c> at runtime. Implements <see cref="IStashCallable"/>
/// so it can be stored in <c>StashStruct.Methods</c>.
/// </summary>
internal sealed class VMFunction : IStashCallable
{
    /// <summary>The compiled function body.</summary>
    public Chunk Chunk { get; }

    /// <summary>Captured upvalues from the enclosing scope.</summary>
    public Upvalue[] Upvalues { get; }

    public int Arity => Chunk.Arity;
    public int MinArity => Chunk.MinArity;

    public VMFunction(Chunk chunk, Upvalue[] upvalues)
    {
        Chunk = chunk;
        Upvalues = upvalues;
    }

    /// <summary>
    /// Not used — the bytecode VM handles VMFunction calls directly in CallValue.
    /// </summary>
    public object? Call(IInterpreterContext context, List<object?> arguments) =>
        throw new System.NotSupportedException("VMFunction must be executed by the bytecode VM.");

    public override string ToString() =>
        Chunk.Name is not null ? $"<fn {Chunk.Name}>" : "<fn>";
}
