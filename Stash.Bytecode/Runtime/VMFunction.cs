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
    /// Not supported. VMFunction instances are executed directly by the bytecode VM
    /// via <see cref="VirtualMachine.CallValue"/>. This method exists because 
    /// <see cref="IStashCallable"/> is used for type checks in StashEngine and stdlib functions.
    /// </summary>
    public object? Call(IInterpreterContext context, List<object?> arguments) =>
        throw new System.NotSupportedException("VMFunction must be executed by the bytecode VM.");

    public override string ToString() =>
        Chunk.Name is not null ? $"<fn {Chunk.Name}>" : "<fn>";
}
