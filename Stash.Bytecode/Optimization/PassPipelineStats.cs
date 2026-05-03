using System.Collections.Generic;

namespace Stash.Bytecode.Optimization;

/// <summary>
/// Accumulated statistics across all passes in one pipeline run.
/// Attached to the resulting <see cref="Chunk"/> as a debug-only field (not serialized).
/// </summary>
internal sealed class PassPipelineStats
{
    public List<(string Name, PassResult Result)> Passes { get; } = new();

    public int TotalInstructionsRemoved { get; private set; }
    public int TotalInstructionsRewritten { get; private set; }

    /// <summary>
    /// True if any pass orphaned an IC companion word (e.g. <c>GetFieldIC</c> → <c>Move</c>
    /// rewrite by <see cref="LocalValueNumberingPass"/>).  Signals <see cref="ChunkBuilder"/>
    /// to run the IC slot compaction step after the pipeline completes.
    /// </summary>
    public bool HasOrphanedICSlots { get; set; }

    public void RecordPass(string name, PassResult r)
    {
        Passes.Add((name, r));
        TotalInstructionsRemoved += r.InstructionsRemoved;
        TotalInstructionsRewritten += r.InstructionsRewritten;
    }
}
