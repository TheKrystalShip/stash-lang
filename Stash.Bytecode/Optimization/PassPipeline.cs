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

        // Build initial CFG.
        ControlFlowGraph cfg = CfgBuilder.Build(builder.RawCode, builder.RawConstants);

        for (int i = 0; i < _passes.Count; i++)
        {
            IBytecodePass pass = _passes[i];
            PassResult r = pass.Run(builder, cfg);
            stats.RecordPass(pass.Name, r);

            if (r.ChangedAnything)
            {
                if (pass.MutatesCfg)
                {
                    // Pass mutated cfg.Blocks directly — write them back to builder._code
                    // so downstream _code-based passes (Peephole, DCE) see the updated stream.
                    // TODO Phase 3+: re-home Peephole and DCE to operate on cfg.Blocks;
                    //      remove WriteBackFromCfg.
                    builder.WriteBackFromCfg(cfg);
                }

                // Rebuild CFG for the next pass so it starts from a fresh, consistent graph.
                if (i < _passes.Count - 1)
                    cfg = CfgBuilder.Build(builder.RawCode, builder.RawConstants);
            }
        }

        return stats;
    }
}
