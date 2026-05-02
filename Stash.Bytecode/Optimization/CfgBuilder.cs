using System.Collections.Generic;
using Stash.Runtime;

namespace Stash.Bytecode.Optimization;

/// <summary>
/// Constructs a <see cref="ControlFlowGraph"/> from a flat instruction stream.
/// </summary>
internal static class CfgBuilder
{
    /// <summary>
    /// Build a CFG from the raw instruction stream of a compiled chunk.
    /// </summary>
    /// <param name="code">The flat instruction/companion-word stream.</param>
    /// <param name="constants">
    /// The chunk's constant pool.  Required to determine the upvalue count of
    /// <c>Closure</c> instructions (which determines how many companion words follow).
    /// </param>
    public static ControlFlowGraph Build(IReadOnlyList<uint> code, IReadOnlyList<StashValue> constants)
    {
        if (code.Count == 0)
        {
            return new ControlFlowGraph { OriginalWordCount = 0 };
        }

        // ── Pass 1: identify all companion-word positions ──────────────────
        var companionWordSet = BuildCompanionWordSet(code, constants);

        // ── Pass 2: identify basic-block leaders ──────────────────────────
        // A leader is a code position that is the start of a basic block.
        var leaderSet = new SortedSet<int>();
        leaderSet.Add(0); // entry is always a leader

        for (int i = 0; i < code.Count; i++)
        {
            if (companionWordSet.Contains(i)) continue; // skip companion words

            uint inst = code[i];
            OpCode op = Instruction.GetOp(inst);

            // Every jump target is a leader.
            if (CfgOpcodeInfo.IsJumpWithSBx(op))
            {
                int target = i + 1 + Instruction.GetSBx(inst);
                if (target >= 0 && target < code.Count && !companionWordSet.Contains(target))
                    leaderSet.Add(target);
            }

            // The instruction after a terminator (if any) is a leader.
            // Terminators have no companion words, so i+1 is always a valid instruction position.
            if (CfgOpcodeInfo.IsBlockTerminator(op) && i + 1 < code.Count)
                leaderSet.Add(i + 1);
        }

        // ── Pass 3: partition code into blocks ────────────────────────────
        var cfg = new ControlFlowGraph { OriginalWordCount = code.Count };
        var leaderToBlockId = new Dictionary<int, int>(leaderSet.Count);

        // Pre-assign block IDs so we can build edges in pass 4 without a second sort.
        int blockId = 0;
        foreach (int leader in leaderSet)
            leaderToBlockId[leader] = blockId++;

        BasicBlock? currentBlock = null;
        for (int pos = 0; pos < code.Count; pos++)
        {
            if (leaderSet.Contains(pos))
            {
                int id = leaderToBlockId[pos];
                currentBlock = new BasicBlock(id, pos);
                cfg.Blocks.Add(currentBlock);
            }

            // Safety: if we somehow have words before the first leader (shouldn't happen
            // since leader 0 is always added), skip them.
            if (currentBlock is null) continue;

            bool isCompanion = companionWordSet.Contains(pos);
            currentBlock.Words.Add(new CfgWord(code[pos], isCompanion, pos));
        }

        // ── Pass 4: build successor/predecessor edges ──────────────────────
        for (int b = 0; b < cfg.Blocks.Count; b++)
        {
            BasicBlock block = cfg.Blocks[b];

            // Find the last non-companion word (the terminating instruction of the block).
            CfgWord? lastInstrWord = null;
            for (int w = block.Words.Count - 1; w >= 0; w--)
            {
                if (!block.Words[w].IsCompanion)
                {
                    lastInstrWord = block.Words[w];
                    break;
                }
            }

            if (lastInstrWord is null) continue; // empty block (shouldn't happen)

            uint lastInst = lastInstrWord.Value.Raw;
            int lastOrigIdx = lastInstrWord.Value.OriginalIndex;
            OpCode lastOp = Instruction.GetOp(lastInst);

            int nextBlockId = b + 1; // index of the fall-through block (may be out of range)
            bool hasNextBlock = nextBlockId < cfg.Blocks.Count;

            switch (lastOp)
            {
                case OpCode.Jmp:
                {
                    int target = lastOrigIdx + 1 + Instruction.GetSBx(lastInst);
                    AddEdge(cfg, block.Id, target, EdgeKind.Branch, leaderToBlockId);
                    break;
                }

                case OpCode.JmpFalse:
                case OpCode.JmpTrue:
                {
                    int target = lastOrigIdx + 1 + Instruction.GetSBx(lastInst);
                    if (hasNextBlock)
                        AddUncheckedEdge(cfg, block.Id, nextBlockId, EdgeKind.FallThrough);
                    AddEdge(cfg, block.Id, target, EdgeKind.Branch, leaderToBlockId);
                    break;
                }

                case OpCode.Loop:
                {
                    int target = lastOrigIdx + 1 + Instruction.GetSBx(lastInst);
                    AddEdge(cfg, block.Id, target, EdgeKind.Loop, leaderToBlockId);
                    break;
                }

                case OpCode.ForPrep:
                case OpCode.ForPrepII:
                case OpCode.ForLoop:
                case OpCode.ForLoopII:
                case OpCode.IterLoop:
                {
                    // Conservative: both fall-through (body) and loop-edge (exit/back) edges.
                    int target = lastOrigIdx + 1 + Instruction.GetSBx(lastInst);
                    if (hasNextBlock)
                        AddUncheckedEdge(cfg, block.Id, nextBlockId, EdgeKind.FallThrough);
                    AddEdge(cfg, block.Id, target, EdgeKind.Loop, leaderToBlockId);
                    break;
                }

                case OpCode.Return:
                case OpCode.Throw:
                case OpCode.Rethrow:
                    // No successors — terminal blocks.
                    break;

                case OpCode.TryBegin:
                {
                    // Fall-through enters the try body; sBx points to the catch handler.
                    int handler = lastOrigIdx + 1 + Instruction.GetSBx(lastInst);
                    if (hasNextBlock)
                        AddUncheckedEdge(cfg, block.Id, nextBlockId, EdgeKind.FallThrough);
                    AddEdge(cfg, block.Id, handler, EdgeKind.ExceptionHandler, leaderToBlockId);
                    break;
                }

                case OpCode.TryEnd:
                    if (hasNextBlock)
                        AddUncheckedEdge(cfg, block.Id, nextBlockId, EdgeKind.FallThrough);
                    break;

                default:
                    // Non-terminator: falls through to the next block.
                    if (hasNextBlock)
                        AddUncheckedEdge(cfg, block.Id, nextBlockId, EdgeKind.FallThrough);
                    break;
            }
        }

        return cfg;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Pre-scan the code stream to collect the positions of all companion words.
    /// Handles GetFieldIC (1 word), CallBuiltIn (1 word), PipeChain (B words),
    /// and Closure (subchunk.Upvalues.Length words).
    /// </summary>
    private static HashSet<int> BuildCompanionWordSet(IReadOnlyList<uint> code, IReadOnlyList<StashValue> constants)
    {
        var set = new HashSet<int>();
        for (int i = 0; i < code.Count; i++)
        {
            if (set.Contains(i)) continue; // skip companion words we already marked

            uint inst = code[i];
            OpCode op = Instruction.GetOp(inst);

            switch (op)
            {
                case OpCode.GetFieldIC:
                case OpCode.CallBuiltIn:
                    if (i + 1 < code.Count)
                        set.Add(i + 1);
                    break;

                case OpCode.PipeChain:
                {
                    int stages = Instruction.GetB(inst);
                    for (int s = 1; s <= stages && i + s < code.Count; s++)
                        set.Add(i + s);
                    break;
                }

                case OpCode.Closure:
                {
                    ushort bx = Instruction.GetBx(inst);
                    if (bx < constants.Count && constants[bx].AsObj is Chunk subChunk)
                    {
                        int uvCount = subChunk.Upvalues.Length;
                        for (int uv = 1; uv <= uvCount && i + uv < code.Count; uv++)
                            set.Add(i + uv);
                    }
                    break;
                }
            }
        }
        return set;
    }

    private static void AddEdge(
        ControlFlowGraph cfg,
        int fromId,
        int targetPos,
        EdgeKind kind,
        Dictionary<int, int> leaderToBlockId)
    {
        if (leaderToBlockId.TryGetValue(targetPos, out int targetId))
            AddUncheckedEdge(cfg, fromId, targetId, kind);
    }

    private static void AddUncheckedEdge(ControlFlowGraph cfg, int fromId, int toId, EdgeKind kind)
    {
        cfg.Blocks[fromId].Successors.Add(new BlockEdge(toId, kind));
        cfg.Blocks[toId].Predecessors.Add(new BlockEdge(fromId, kind));
    }
}
