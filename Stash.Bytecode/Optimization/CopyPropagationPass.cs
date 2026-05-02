using System.Collections.Generic;
using Stash.Runtime;

namespace Stash.Bytecode.Optimization;

/// <summary>
/// Basic-block-local copy propagation pass.
/// <para>
/// Walks each basic block forward maintaining a <c>copyOf</c> map (register → original source
/// register).  When a <c>Move(A, B)</c> is seen the map records <c>A = copyOf[B] ?? B</c>
/// (chain-compressed to a single hop).  For every other instruction, all read-register operands
/// are rewritten through the map; then all written registers kill their map entries.
/// </para>
/// <para>
/// The pass mutates <see cref="ControlFlowGraph"/> blocks in place.  The pipeline write-back
/// step (<see cref="ChunkBuilder.WriteBackFromCfg"/>) then propagates the changes to
/// <c>_code</c> so that downstream <c>_code</c>-based passes (DCE, Peephole) see the updated
/// stream.
/// </para>
/// </summary>
internal sealed class CopyPropagationPass : IBytecodePass
{
    public string Name => "CopyPropagationPass";

    /// <summary>
    /// True: this pass mutates <see cref="ControlFlowGraph"/> blocks; the pipeline must
    /// write them back to the builder before running the next pass.
    /// </summary>
    public bool MutatesCfg => true;

    public PassResult Run(ChunkBuilder builder, ControlFlowGraph cfg)
    {
        int rewrittenCount = 0;
        IReadOnlyList<StashValue> constants = builder.RawConstants;

        foreach (BasicBlock block in cfg.Blocks)
        {
            // copyOf[A] = S means register A currently holds the same value as register S.
            // We maintain chain-compressed depth-1 entries: if S was itself a copy it has
            // already been resolved, so every entry points directly to the ultimate source.
            var copyOf = new Dictionary<byte, byte>();

            for (int i = 0; i < block.Words.Count; i++)
            {
                CfgWord word = block.Words[i];
                if (word.IsCompanion) continue; // companion words are never instructions

                uint raw = word.Raw;
                OpCode op = Instruction.GetOp(raw);

                if (op == OpCode.Move)
                {
                    byte a = Instruction.GetA(raw);
                    byte b = Instruction.GetB(raw);

                    if (a == b) continue; // self-move is a no-op; no copy chain changes

                    // 1. Chain-compress: follow one hop to get the ultimate source for B.
                    byte src = copyOf.TryGetValue(b, out byte chain) ? chain : b;

                    // 2. Invalidate: A is being overwritten — kill copyOf[A] and any entry
                    //    whose value equals A (they would now point to a stale register).
                    copyOf.Remove(a);
                    RemoveAliases(copyOf, a);

                    // 3. Record: A is now a copy of src.
                    copyOf[a] = src;

                    // Do not modify the Move itself; DCE will remove it once it is dead.
                }
                else
                {
                    // Rewrite all read-register operands through the copy map.
                    uint newRaw = OpcodeOperands.RewriteReadRegs(
                        raw,
                        src => copyOf.TryGetValue(src, out byte mapped) ? mapped : src);

                    if (newRaw != raw)
                    {
                        block.Words[i] = new CfgWord(newRaw, false, word.OriginalIndex);
                        rewrittenCount++;
                    }

                    // Kill written registers: invalidate any copy entry whose key or value
                    // overlaps with a register the instruction just wrote.
                    OpcodeOperands.ForEachWrittenReg(raw, constants, written =>
                    {
                        copyOf.Remove(written);
                        RemoveAliases(copyOf, written);
                    });
                }
            }
        }

        return new PassResult
        {
            InstructionsRewritten = rewrittenCount,
            ChangedAnything = rewrittenCount > 0,
        };
    }

    /// <summary>
    /// Removes all entries from <paramref name="copyOf"/> whose value equals
    /// <paramref name="killed"/> (i.e., entries that pointed to the register that was
    /// just overwritten and are therefore stale).
    /// </summary>
    private static void RemoveAliases(Dictionary<byte, byte> copyOf, byte killed)
    {
        // Collect keys first; modifying the dictionary during iteration is not allowed.
        List<byte>? toRemove = null;
        foreach (var kvp in copyOf)
        {
            if (kvp.Value == killed)
                (toRemove ??= new List<byte>()).Add(kvp.Key);
        }
        if (toRemove is not null)
        {
            foreach (byte k in toRemove)
                copyOf.Remove(k);
        }
    }
}
