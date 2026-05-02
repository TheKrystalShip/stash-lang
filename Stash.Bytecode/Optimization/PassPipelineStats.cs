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

    public void RecordPass(string name, PassResult r)
    {
        Passes.Add((name, r));
        TotalInstructionsRemoved += r.InstructionsRemoved;
        TotalInstructionsRewritten += r.InstructionsRewritten;
    }
}
