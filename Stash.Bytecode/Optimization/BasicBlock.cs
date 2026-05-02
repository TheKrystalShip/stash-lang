using System.Collections.Generic;

namespace Stash.Bytecode.Optimization;

/// <summary>
/// A single basic block in the control-flow graph.
/// Contains an ordered list of all 32-bit words (instructions and companion words) from
/// the original instruction stream that belong to this block.
/// </summary>
internal sealed class BasicBlock
{
    public int Id { get; }

    /// <summary>Index of the first word of this block in the original <c>_code</c> array.</summary>
    public int OriginalStart { get; }

    /// <summary>
    /// All words in original order.  Each entry is either a real instruction or a
    /// raw companion uint (e.g., the IC-slot word after GetFieldIC).
    /// Use <see cref="CfgWord.IsCompanion"/> to distinguish them.
    /// </summary>
    public List<CfgWord> Words { get; } = new();

    public List<BlockEdge> Successors { get; } = new();
    public List<BlockEdge> Predecessors { get; } = new();

    public BasicBlock(int id, int originalStart)
    {
        Id = id;
        OriginalStart = originalStart;
    }
}

/// <summary>
/// One word inside a <see cref="BasicBlock"/>, either a real instruction or a companion word.
/// </summary>
internal readonly struct CfgWord
{
    /// <summary>The raw 32-bit word value.</summary>
    public readonly uint Raw;

    /// <summary>True when this word is a companion/descriptor word, not an instruction.</summary>
    public readonly bool IsCompanion;

    /// <summary>
    /// Position of this word in the original <c>_code</c> array (0-based, counting companions).
    /// Used by the lowering pass to build the old→new index mapping.
    /// </summary>
    public readonly int OriginalIndex;

    public CfgWord(uint raw, bool isCompanion, int originalIndex)
    {
        Raw = raw;
        IsCompanion = isCompanion;
        OriginalIndex = originalIndex;
    }
}

/// <summary>Kind of a control-flow edge between basic blocks.</summary>
internal enum EdgeKind
{
    FallThrough,
    Branch,
    Loop,
    ExceptionHandler,
}

/// <summary>A directed edge from one <see cref="BasicBlock"/> to another.</summary>
internal readonly struct BlockEdge
{
    public readonly int TargetBlockId;
    public readonly EdgeKind Kind;

    public BlockEdge(int target, EdgeKind kind)
    {
        TargetBlockId = target;
        Kind = kind;
    }
}
