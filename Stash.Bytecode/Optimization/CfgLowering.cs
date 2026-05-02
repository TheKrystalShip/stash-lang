using System.Collections.Generic;
using Stash.Bytecode;

namespace Stash.Bytecode.Optimization;

/// <summary>
/// Lowers a <see cref="ControlFlowGraph"/> back to a flat instruction stream,
/// re-patching all sBx jump offsets to account for any block reordering or
/// instruction removal that occurred during optimization passes.
/// </summary>
internal static class CfgLowering
{
    /// <summary>
    /// Emit all blocks of <paramref name="cfg"/> (in order) into <paramref name="codeOut"/>,
    /// patch all jump sBx offsets, and remap source-map entries.
    /// </summary>
    /// <param name="cfg">The control-flow graph to lower.</param>
    /// <param name="codeOut">
    /// Destination list; will be cleared and filled with the lowered instruction stream.
    /// </param>
    /// <param name="sourceEntriesOut">
    /// Destination source-map list; will be cleared and filled with remapped entries.
    /// </param>
    /// <param name="sourceEntriesIn">
    /// Original source-map entries whose <c>BytecodeOffset</c> values will be remapped
    /// via the old→new index mapping built during lowering.
    /// </param>
    public static void Lower(
        ControlFlowGraph cfg,
        List<uint> codeOut,
        List<SourceMapEntry> sourceEntriesOut,
        IReadOnlyList<SourceMapEntry> sourceEntriesIn)
    {
        codeOut.Clear();
        sourceEntriesOut.Clear();

        if (cfg.Blocks.Count == 0)
            return;

        // old code position → new code position
        int totalWords = cfg.OriginalWordCount;
        int[] oldToNew = new int[totalWords];

        // Parallel arrays tracking what we emitted: original code position and companion flag.
        var outputOriginalIndices = new List<int>(totalWords);
        var outputIsCompanion = new List<bool>(totalWords);

        // ── Step 1: emit all words from all blocks in order ──────────────
        foreach (BasicBlock block in cfg.Blocks)
        {
            foreach (CfgWord word in block.Words)
            {
                oldToNew[word.OriginalIndex] = codeOut.Count;
                outputOriginalIndices.Add(word.OriginalIndex);
                outputIsCompanion.Add(word.IsCompanion);
                codeOut.Add(word.Raw); // raw word; jumps still have original sBx offsets
            }
        }

        int newCount = codeOut.Count;

        // ── Step 2: patch all sBx jump offsets ────────────────────────────
        for (int newIdx = 0; newIdx < codeOut.Count; newIdx++)
        {
            if (outputIsCompanion[newIdx]) continue; // companion words are not instructions

            uint inst = codeOut[newIdx];
            OpCode op = Instruction.GetOp(inst);

            if (!CfgOpcodeInfo.IsJumpWithSBx(op)) continue;

            int oldIdx = outputOriginalIndices[newIdx];
            int oldSBx = Instruction.GetSBx(inst); // still original sBx (not yet patched)
            int oldTarget = oldIdx + 1 + oldSBx;
            int newTarget = (oldTarget >= 0 && oldTarget < oldToNew.Length)
                ? oldToNew[oldTarget]
                : newCount; // defensive: jump past end
            int newSBx = newTarget - newIdx - 1;
            codeOut[newIdx] = Instruction.PatchSBx(inst, newSBx);
        }

        // ── Step 3: remap source-map entries ─────────────────────────────
        foreach (SourceMapEntry entry in sourceEntriesIn)
        {
            int oldOffset = entry.BytecodeOffset;
            int newOffset = (oldOffset >= 0 && oldOffset < oldToNew.Length)
                ? oldToNew[oldOffset]
                : oldOffset;
            sourceEntriesOut.Add(new SourceMapEntry(newOffset, entry.Span));
        }
    }
}
