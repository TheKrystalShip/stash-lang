namespace Stash.Bytecode.Optimization;

/// <summary>
/// Summary statistics produced by a single optimization pass.
/// </summary>
internal readonly struct PassResult
{
    /// <summary>Number of instructions removed by this pass.</summary>
    public int InstructionsRemoved { get; init; }

    /// <summary>Number of instructions rewritten (not removed) by this pass.</summary>
    public int InstructionsRewritten { get; init; }

    /// <summary>True if this pass made any change to the instruction stream.</summary>
    public bool ChangedAnything { get; init; }
}

/// <summary>
/// An optimization pass that operates on a <see cref="ChunkBuilder"/>'s instruction stream.
/// </summary>
internal interface IBytecodePass
{
    /// <summary>Human-readable name of this pass (used in diagnostics and stats).</summary>
    string Name { get; }

    /// <summary>
    /// When <c>true</c>, this pass mutates <see cref="ControlFlowGraph"/> blocks in place
    /// rather than mutating <see cref="ChunkBuilder.RawCode"/> directly.
    /// The pipeline will call <see cref="ChunkBuilder.WriteBackFromCfg"/> after the pass
    /// returns so that subsequent <c>_code</c>-based passes see the updated stream.
    /// Default: <c>false</c>.
    /// </summary>
    bool MutatesCfg => false;

    /// <summary>
    /// Run the pass.
    /// <para>
    /// Passes that set <see cref="MutatesCfg"/> to <c>false</c> mutate
    /// <see cref="ChunkBuilder.RawCode"/> in place (the existing peephole/DCE approach).
    /// Passes that set <see cref="MutatesCfg"/> to <c>true</c> mutate
    /// <paramref name="cfg"/>'s blocks in place; the pipeline handles write-back.
    /// </para>
    /// </summary>
    PassResult Run(ChunkBuilder builder, ControlFlowGraph cfg);
}
