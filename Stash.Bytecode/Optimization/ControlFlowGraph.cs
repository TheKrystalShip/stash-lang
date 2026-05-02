using System.Collections.Generic;

namespace Stash.Bytecode.Optimization;

/// <summary>
/// Control-flow graph for a single compiled function/chunk.
/// Blocks are stored in original program order (entry block first).
/// </summary>
internal sealed class ControlFlowGraph
{
    /// <summary>All basic blocks, in original instruction order (entry = Blocks[0]).</summary>
    public List<BasicBlock> Blocks { get; } = new();

    /// <summary>The entry (first) block.</summary>
    public BasicBlock Entry => Blocks[0];

    /// <summary>
    /// Total number of words in the original <c>_code</c> array (instructions + companion words).
    /// Used to size the old→new index mapping array in the lowering pass.
    /// </summary>
    public int OriginalWordCount { get; init; }

    /// <summary>Look up a block by its ID.</summary>
    public BasicBlock GetBlock(int id) => Blocks[id];
}
