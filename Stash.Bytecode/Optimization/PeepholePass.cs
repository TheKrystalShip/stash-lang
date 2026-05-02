namespace Stash.Bytecode.Optimization;

/// <summary>
/// Bytecode pass that wraps the existing <see cref="ChunkBuilder"/> peephole optimizer.
/// </summary>
internal sealed class PeepholePass : IBytecodePass
{
    public string Name => "PeepholePass";

    public PassResult Run(ChunkBuilder builder, ControlFlowGraph cfg)
    {
        int before = builder.RawCode.Count;
        builder.Peephole();
        int after = builder.RawCode.Count;
        int removed = before - after;
        return new PassResult
        {
            InstructionsRemoved = removed,
            ChangedAnything = removed > 0,
        };
    }
}
