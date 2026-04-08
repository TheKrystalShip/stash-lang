using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Post-compilation pass that scans bytecode for known multi-instruction sequences
/// and replaces them with fused superinstructions, and specializes common single-opcode
/// patterns (LoadLocal0–3, Call0–2).
/// </summary>
internal static class PeepholeOptimizer
{
    /// <summary>
    /// Optimize bytecode in-place. Modifies <paramref name="code"/> and <paramref name="codeLength"/>,
    /// and remaps <paramref name="sourceMapEntries"/> to match the new offsets.
    /// </summary>
    internal static void Optimize(
        byte[] code,
        ref int codeLength,
        IReadOnlyList<StashValue> constants,
        List<SourceMapEntry> sourceMapEntries)
    {
        if (codeLength < 2)
            return;

        // Step 1: Find all jump targets
        HashSet<int> jumpTargets = ComputeJumpTargets(code, codeLength, constants);

        // Step 2: Single-pass fusion + specialization (in-place, wp <= rp always)
        Dictionary<int, int> offsetMap = new();
        List<(int newOffset, int oldOffset)> jumpFixups = new();

        int rp = 0; // read pointer
        int wp = 0; // write pointer

        while (rp < codeLength)
        {
            offsetMap[rp] = wp;

            OpCode op = (OpCode)code[rp];
            int instrSize = GetInstructionSize(code, rp, constants);

            // ---- Tier 1: 3-instruction fusions ----
            if (TryFuse3(code, rp, codeLength, jumpTargets, constants, out OpCode fusedOp, out int oldSize, out int newSize))
            {
                WriteFused3(code, rp, wp, fusedOp);
                rp += oldSize;
                wp += newSize;
                continue;
            }

            // ---- Tier 1: 2-instruction fusions (LoadLocal + Return) ----
            if (TryFuse2(code, rp, codeLength, jumpTargets, constants, out fusedOp, out oldSize, out newSize))
            {
                WriteFused2(code, rp, wp, fusedOp);
                rp += oldSize;
                wp += newSize;
                continue;
            }

            // ---- Tier 0: single-instruction specializations ----
            if (TrySpecialize(code, rp, out OpCode specializedOp))
            {
                code[wp] = (byte)specializedOp;
                rp += 2; // old instruction was 2 bytes (opcode + u8)
                wp += 1; // new instruction is 1 byte (zero-operand)
                continue;
            }

            // ---- No match: copy as-is ----
            if (IsJumpInstruction(op))
            {
                jumpFixups.Add((wp, rp));
            }

            if (wp != rp)
            {
                Buffer.BlockCopy(code, rp, code, wp, instrSize);
            }

            rp += instrSize;
            wp += instrSize;
        }

        // Record end-of-code mapping
        offsetMap[rp] = wp;
        codeLength = wp;

        // Step 3: Fix up jump operands
        foreach ((int newOff, int oldOff) in jumpFixups)
        {
            FixupJump(code, newOff, oldOff, offsetMap);
        }

        // Step 4: Fix up source map entries
        FixupSourceMap(sourceMapEntries, offsetMap);
    }

    // ---- Jump Target Analysis ----

    private static HashSet<int> ComputeJumpTargets(byte[] code, int codeLength, IReadOnlyList<StashValue> constants)
    {
        HashSet<int> targets = new();
        int offset = 0;
        while (offset < codeLength)
        {
            OpCode op = (OpCode)code[offset];
            int instrSize = GetInstructionSize(code, offset, constants);

            if (IsJumpInstruction(op))
            {
                ushort raw = (ushort)((code[offset + 1] << 8) | code[offset + 2]);
                int absTarget;
                if (op == OpCode.Loop)
                    absTarget = offset + 3 - raw;
                else
                    absTarget = offset + 3 + (short)raw;

                if (absTarget >= 0 && absTarget <= codeLength)
                    targets.Add(absTarget);
            }

            offset += instrSize;
        }
        return targets;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsJumpInstruction(OpCode op) => op is
        OpCode.Jump or OpCode.JumpTrue or OpCode.JumpFalse or
        OpCode.Loop or OpCode.And or OpCode.Or or
        OpCode.NullCoalesce or OpCode.Iterate or OpCode.TryBegin;

    // ---- Instruction Size ----

    private static int GetInstructionSize(byte[] code, int offset, IReadOnlyList<StashValue> constants)
    {
        OpCode op = (OpCode)code[offset];
        int baseSize = 1 + OpCodeInfo.OperandSize(op);

        // Closure has inline upvalue descriptors after the opcode + u16
        if (op == OpCode.Closure && offset + 2 < code.Length)
        {
            ushort constIdx = (ushort)((code[offset + 1] << 8) | code[offset + 2]);
            if (constIdx < constants.Count && constants[constIdx].AsObj is Chunk fnChunk)
            {
                baseSize += 2 * fnChunk.Upvalues.Length;
            }
        }

        return baseSize;
    }

    // ---- Tier 1: 3-Instruction Fusion ----

    private static bool TryFuse3(
        byte[] code, int rp, int codeLength,
        HashSet<int> jumpTargets,
        IReadOnlyList<StashValue> constants,
        out OpCode fusedOp, out int oldSize, out int newSize)
    {
        fusedOp = default;
        oldSize = 0;
        newSize = 0;

        OpCode op0 = (OpCode)code[rp];
        int size0 = GetInstructionSize(code, rp, constants);
        int rp1 = rp + size0;
        if (rp1 >= codeLength) return false;

        // Instruction 2 must not be a jump target
        if (jumpTargets.Contains(rp1)) return false;

        OpCode op1 = (OpCode)code[rp1];
        int size1 = GetInstructionSize(code, rp1, constants);
        int rp2 = rp1 + size1;
        if (rp2 >= codeLength) return false;

        // Instruction 3 must not be a jump target
        if (jumpTargets.Contains(rp2)) return false;

        OpCode op2 = (OpCode)code[rp2];
        int size2 = GetInstructionSize(code, rp2, constants);

        // Pattern: LoadLocal + LoadLocal + {Add | LessThan}
        if (op0 == OpCode.LoadLocal && op1 == OpCode.LoadLocal)
        {
            if (op2 == OpCode.Add)
            {
                fusedOp = OpCode.LL_Add;
                oldSize = size0 + size1 + size2; // 2+2+1=5
                newSize = 3; // opcode + u8 + u8
                return true;
            }
            if (op2 == OpCode.LessThan)
            {
                fusedOp = OpCode.LL_LessThan;
                oldSize = size0 + size1 + size2;
                newSize = 3;
                return true;
            }
        }

        // Pattern: LoadLocal + Const + {Add | LessThan | Subtract}
        if (op0 == OpCode.LoadLocal && op1 == OpCode.Const)
        {
            if (op2 == OpCode.Add)
            {
                fusedOp = OpCode.LC_Add;
                oldSize = size0 + size1 + size2; // 2+3+1=6
                newSize = 4; // opcode + u8 + u16
                return true;
            }
            if (op2 == OpCode.LessThan)
            {
                fusedOp = OpCode.LC_LessThan;
                oldSize = size0 + size1 + size2;
                newSize = 4;
                return true;
            }
            if (op2 == OpCode.Subtract)
            {
                fusedOp = OpCode.LC_Subtract;
                oldSize = size0 + size1 + size2;
                newSize = 4;
                return true;
            }
        }

        // Pattern: Dup + StoreLocal + Pop
        if (op0 == OpCode.Dup && op1 == OpCode.StoreLocal && op2 == OpCode.Pop)
        {
            fusedOp = OpCode.DupStoreLocalPop;
            oldSize = size0 + size1 + size2; // 1+2+1=4
            newSize = 2; // opcode + u8
            return true;
        }

        return false;
    }

    private static void WriteFused3(byte[] code, int rp, int wp, OpCode fusedOp)
    {
        switch (fusedOp)
        {
            case OpCode.LL_Add:
            case OpCode.LL_LessThan:
            {
                // LoadLocal slot1 + LoadLocal slot2 + Op
                byte slot1 = code[rp + 1]; // operand of first LoadLocal
                byte slot2 = code[rp + 3]; // operand of second LoadLocal (rp+2 is opcode of LoadLocal2)
                code[wp] = (byte)fusedOp;
                code[wp + 1] = slot1;
                code[wp + 2] = slot2;
                break;
            }

            case OpCode.LC_Add:
            case OpCode.LC_LessThan:
            case OpCode.LC_Subtract:
            {
                // LoadLocal slot + Const constIdx + Op
                byte slot = code[rp + 1]; // operand of LoadLocal
                byte constHi = code[rp + 3]; // high byte of Const operand (rp+2 is Const opcode)
                byte constLo = code[rp + 4]; // low byte of Const operand
                code[wp] = (byte)fusedOp;
                code[wp + 1] = slot;
                code[wp + 2] = constHi;
                code[wp + 3] = constLo;
                break;
            }

            case OpCode.DupStoreLocalPop:
            {
                // Dup + StoreLocal slot + Pop
                byte slot = code[rp + 2]; // operand of StoreLocal (rp+1 is StoreLocal opcode)
                code[wp] = (byte)fusedOp;
                code[wp + 1] = slot;
                break;
            }
        }
    }

    // ---- Tier 1: 2-Instruction Fusion ----

    private static bool TryFuse2(
        byte[] code, int rp, int codeLength,
        HashSet<int> jumpTargets,
        IReadOnlyList<StashValue> constants,
        out OpCode fusedOp, out int oldSize, out int newSize)
    {
        fusedOp = default;
        oldSize = 0;
        newSize = 0;

        OpCode op0 = (OpCode)code[rp];
        int size0 = GetInstructionSize(code, rp, constants);
        int rp1 = rp + size0;
        if (rp1 >= codeLength) return false;

        // Instruction 2 must not be a jump target
        if (jumpTargets.Contains(rp1)) return false;

        OpCode op1 = (OpCode)code[rp1];

        // Pattern: LoadLocal + Return
        if (op0 == OpCode.LoadLocal && op1 == OpCode.Return)
        {
            fusedOp = OpCode.L_Return;
            oldSize = size0 + 1; // 2+1=3
            newSize = 2; // opcode + u8
            return true;
        }

        return false;
    }

    private static void WriteFused2(byte[] code, int rp, int wp, OpCode fusedOp)
    {
        if (fusedOp == OpCode.L_Return)
        {
            byte slot = code[rp + 1]; // operand of LoadLocal
            code[wp] = (byte)fusedOp;
            code[wp + 1] = slot;
        }
    }

    // ---- Tier 0: Single-Instruction Specialization ----

    private static bool TrySpecialize(byte[] code, int rp, out OpCode specializedOp)
    {
        OpCode op = (OpCode)code[rp];

        if (op == OpCode.LoadLocal)
        {
            byte slot = code[rp + 1];
            specializedOp = slot switch
            {
                0 => OpCode.LoadLocal0,
                1 => OpCode.LoadLocal1,
                2 => OpCode.LoadLocal2,
                3 => OpCode.LoadLocal3,
                _ => default
            };
            return specializedOp != default;
        }

        if (op == OpCode.Call)
        {
            byte argc = code[rp + 1];
            specializedOp = argc switch
            {
                0 => OpCode.Call0,
                1 => OpCode.Call1,
                2 => OpCode.Call2,
                _ => default
            };
            return specializedOp != default;
        }

        specializedOp = default;
        return false;
    }

    // ---- Jump Fixup ----

    private static void FixupJump(byte[] code, int newOff, int oldOff, Dictionary<int, int> offsetMap)
    {
        OpCode op = (OpCode)code[newOff];
        ushort raw = (ushort)((code[newOff + 1] << 8) | code[newOff + 2]);

        // Compute old absolute target
        int oldAbsTarget;
        if (op == OpCode.Loop)
            oldAbsTarget = oldOff + 3 - raw;
        else
            oldAbsTarget = oldOff + 3 + (short)raw;

        // Map to new absolute target
        if (!offsetMap.TryGetValue(oldAbsTarget, out int newAbsTarget))
        {
            // Target should always be in the map (instruction boundary or end-of-code).
            // If it's not, leave the operand unchanged as a safety fallback.
            return;
        }

        // Compute new relative offset and patch
        ushort newRaw;
        if (op == OpCode.Loop)
            newRaw = (ushort)((newOff + 3) - newAbsTarget);
        else
            newRaw = (ushort)(short)(newAbsTarget - (newOff + 3));

        code[newOff + 1] = (byte)(newRaw >> 8);
        code[newOff + 2] = (byte)(newRaw & 0xFF);
    }

    // ---- Source Map Fixup ----

    private static void FixupSourceMap(List<SourceMapEntry> entries, Dictionary<int, int> offsetMap)
    {
        var seen = new HashSet<int>();
        int writeIdx = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            SourceMapEntry e = entries[i];
            if (offsetMap.TryGetValue(e.BytecodeOffset, out int newOff))
            {
                if (seen.Add(newOff))
                {
                    entries[writeIdx++] = new SourceMapEntry(newOff, e.Span);
                }
            }
        }
        if (writeIdx < entries.Count)
            entries.RemoveRange(writeIdx, entries.Count - writeIdx);
    }
}
