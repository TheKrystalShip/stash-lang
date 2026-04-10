namespace Stash.Analysis.FlowAnalysis;

using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// A basic block in a control-flow graph: a maximal sequence of statements with no internal
/// branches. Control enters at the first statement and exits at the last.
/// </summary>
public sealed class BasicBlock
{
    /// <summary>Gets the unique identifier for this block within its <see cref="ControlFlowGraph"/>.</summary>
    public int Id { get; }

    /// <summary>The statements contained in this block, in execution order.</summary>
    public List<Stmt> Statements { get; } = new();

    /// <summary>Blocks that this block may transfer control to.</summary>
    public List<BasicBlock> Successors { get; } = new();

    /// <summary>Blocks that may transfer control to this block.</summary>
    public List<BasicBlock> Predecessors { get; } = new();

    /// <summary>Describes how control leaves this block.</summary>
    public BranchKind BranchKind { get; set; } = BranchKind.Unconditional;

    /// <summary>
    /// The condition expression for <see cref="BranchKind.Conditional"/> blocks, or
    /// <see langword="null"/> for other branch kinds.
    /// </summary>
    public Expr? BranchCondition { get; set; }

    internal BasicBlock(int id) { Id = id; }

    /// <summary>
    /// Adds a directed edge from this block to <paramref name="target"/>, also recording the
    /// reverse predecessor edge on <paramref name="target"/>.
    /// </summary>
    internal void AddSuccessor(BasicBlock target)
    {
        if (!Successors.Contains(target))
        {
            Successors.Add(target);
            target.Predecessors.Add(this);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"BB{Id}[{Statements.Count} stmt(s), {BranchKind}]";
}
