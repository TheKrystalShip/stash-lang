namespace Stash.Bytecode;

/// <summary>
/// A compiled closure: a <see cref="Chunk"/> paired with captured <see cref="Upvalue"/> instances.
/// Created by <c>OP_CLOSURE</c> at runtime.
/// </summary>
internal sealed class VMFunction
{
    /// <summary>The compiled function body.</summary>
    public Chunk Chunk { get; }

    /// <summary>Captured upvalues from the enclosing scope.</summary>
    public Upvalue[] Upvalues { get; }

    public VMFunction(Chunk chunk, Upvalue[] upvalues)
    {
        Chunk = chunk;
        Upvalues = upvalues;
    }

    public override string ToString() =>
        Chunk.Name is not null ? $"<fn {Chunk.Name}>" : "<fn>";
}
