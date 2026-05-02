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
    /// Run the pass.
    /// <para>
    /// In Phase 1, passes mutate <see cref="ChunkBuilder.RawCode"/> in place directly
    /// (the existing peephole/DCE logic) rather than working through the CFG IR.
    /// The <paramref name="cfg"/> argument is provided for future passes; Phase 1
    /// passes may ignore it.
    /// </para>
    /// </summary>
    PassResult Run(ChunkBuilder builder, ControlFlowGraph cfg);
}
