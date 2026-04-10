namespace Stash.Analysis.FlowAnalysis;

using System.Collections.Generic;

/// <summary>
/// The control-flow graph for a single function body or the top-level program scope.
/// </summary>
/// <remarks>
/// Every block in the graph represents a maximal straight-line sequence of statements.
/// All terminating paths (return / throw) connect to the synthetic <see cref="Exit"/> block.
/// Non-terminating fall-through paths also connect to the exit block.
/// </remarks>
public sealed class ControlFlowGraph
{
    /// <summary>The entry block — the first block executed when control enters the function.</summary>
    public BasicBlock Entry { get; }

    /// <summary>
    /// The synthetic exit block. Blocks whose last statement is <c>return</c> or <c>throw</c>
    /// connect here, as do any fall-through blocks that reach the end of the function body.
    /// </summary>
    public BasicBlock Exit { get; }

    /// <summary>All blocks in the graph, including <see cref="Entry"/> and <see cref="Exit"/>.</summary>
    public IReadOnlyList<BasicBlock> Blocks { get; }

    internal ControlFlowGraph(BasicBlock entry, BasicBlock exit, List<BasicBlock> blocks)
    {
        Entry = entry;
        Exit = exit;
        Blocks = blocks;
    }

    /// <summary>
    /// Returns <see langword="true"/> when at least one predecessor of the exit block arrives
    /// via a non-terminating edge (i.e., an <see cref="BranchKind.Unconditional"/> or
    /// <see cref="BranchKind.Conditional"/> branch), meaning there is a code path that reaches
    /// the end of the function without a <c>return</c> or <c>throw</c>.
    /// </summary>
    public bool HasNonTerminatingPathToExit()
    {
        foreach (var pred in Exit.Predecessors)
        {
            if (pred.BranchKind is BranchKind.Unconditional or BranchKind.Conditional)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns all blocks that are unreachable from the entry block (i.e., dead code).
    /// The <see cref="Exit"/> block is never returned.
    /// </summary>
    public IEnumerable<BasicBlock> GetUnreachableBlocks()
    {
        var reachable = new HashSet<BasicBlock>();
        var queue = new Queue<BasicBlock>();
        queue.Enqueue(Entry);
        reachable.Add(Entry);

        while (queue.Count > 0)
        {
            var block = queue.Dequeue();
            foreach (var succ in block.Successors)
            {
                if (reachable.Add(succ))
                {
                    queue.Enqueue(succ);
                }
            }
        }

        foreach (var block in Blocks)
        {
            if (!reachable.Contains(block) && block != Exit)
            {
                yield return block;
            }
        }
    }
}
