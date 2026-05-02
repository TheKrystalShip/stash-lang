using System.Collections.Generic;
using Stash.Runtime;

namespace Stash.Bytecode.Optimization;

/// <summary>
/// Runs a sequence of <see cref="IBytecodePass"/> instances against a
/// <see cref="ChunkBuilder"/>'s instruction stream.
/// </summary>
/// <remarks>
/// Phase 1 design: the passes (re-homed Peephole and DCE) still mutate
/// <see cref="ChunkBuilder.RawCode"/> directly — the CFG is built per-pass call
/// as metadata but is not the working IR yet.  Future phases will move passes to
/// mutate the CFG instead, enabling true CFG-based lowering.
/// </remarks>
internal sealed class PassPipeline
{
    private readonly List<IBytecodePass> _passes = new();

    /// <summary>Append a pass to the end of the pipeline. Returns <c>this</c> for chaining.</summary>
    public PassPipeline Add(IBytecodePass pass)
    {
        _passes.Add(pass);
        return this;
    }

    /// <summary>
    /// Execute all registered passes against <paramref name="builder"/>.
    /// The CFG is (re-)built before each pass so that each pass sees an up-to-date
    /// control-flow graph even if a previous pass mutated <c>_code</c>.
    /// </summary>
    public PassPipelineStats Run(ChunkBuilder builder)
    {
        var stats = new PassPipelineStats();

        if (_passes.Count == 0)
            return stats;

        // Build initial CFG (used as metadata by Phase 1 passes; future passes will use it as IR).
        ControlFlowGraph cfg = CfgBuilder.Build(builder.RawCode, builder.RawConstants);

        foreach (IBytecodePass pass in _passes)
        {
            PassResult r = pass.Run(builder, cfg);
            stats.RecordPass(pass.Name, r);

            // Rebuild CFG if the pass changed the instruction stream, so the next pass
            // sees an up-to-date graph.
            if (r.ChangedAnything && _passes.IndexOf(pass) < _passes.Count - 1)
                cfg = CfgBuilder.Build(builder.RawCode, builder.RawConstants);
        }

        return stats;
    }
}
