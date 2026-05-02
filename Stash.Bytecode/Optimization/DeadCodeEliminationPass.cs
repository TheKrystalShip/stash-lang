namespace Stash.Bytecode.Optimization;

/// <summary>
/// Bytecode pass that wraps the existing <see cref="ChunkBuilder"/> dead-code elimination pass.
/// </summary>
internal sealed class DeadCodeEliminationPass : IBytecodePass
{
    public string Name => "DeadCodeEliminationPass";

    public PassResult Run(ChunkBuilder builder, ControlFlowGraph cfg)
    {
        int before = builder.RawCode.Count;
        builder.DeadCodeEliminate();
        int after = builder.RawCode.Count;
        int removed = before - after;
        return new PassResult
        {
            InstructionsRemoved = removed,
            ChangedAnything = removed > 0,
        };
    }
}
