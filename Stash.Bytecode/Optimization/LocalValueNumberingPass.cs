using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stash.Runtime;

namespace Stash.Bytecode.Optimization;

/// <summary>
/// Basic-block-local value numbering (LVN) pass — spec §6.
/// <para>
/// Sub-phase A (§6.1 + §6.2): Numbers constants, arithmetic/bitwise/comparison ops, Move.
/// Sub-phase B (§6.4): Numbers GetGlobal with const-global immortality across calls.
/// Sub-phase C (§6.5 + §6.3): Numbers GetField/GetFieldIC/GetTable with conservative invalidation.
/// </para>
/// <para>
/// On a VN HIT the original instruction is rewritten to <c>Move(dest, existingReg)</c>.
/// GetFieldIC hits additionally remove the orphaned companion word from the block.
/// Constant folding is skipped (TODO Phase 4 — spec §6.6).
/// GetUpval/SetUpval are skipped (closure semantics too complex — spec §6.2 note).
/// In operator is skipped (not pure for all collection types — spec §6.2 note).
/// </para>
/// </summary>
internal sealed class LocalValueNumberingPass : IBytecodePass
{
    public string Name => "LocalValueNumberingPass";

    /// <summary>This pass mutates CFG blocks; the pipeline must write back before the next pass.</summary>
    public bool MutatesCfg => true;

    // ── Per-chunk pre-computed state ────────────────────────────────────────────

    /// <summary>
    /// Global slots for which InitConstGlobal appears and NO SetGlobal appears in the
    /// entire chunk. These VNs survive Call/CallBuiltIn/CallSpread within a block.
    /// </summary>
    private HashSet<int> _constImmortalSlots = new();

    // ── Counter for unique VNs ──────────────────────────────────────────────────

    /// <summary>Next fresh value number. 0 is reserved as "no VN assigned".</summary>
    private int _nextVn;

    // ───────────────────────────────────────────────────────────────────────────

    public PassResult Run(ChunkBuilder builder, ControlFlowGraph cfg)
    {
        BuildConstGlobalInfo(builder.RawCode);

        int rewrittenCount = 0;
        _nextVn = 1;

        foreach (BasicBlock block in cfg.Blocks)
            rewrittenCount += ProcessBlock(block, cfg);

        return new PassResult
        {
            InstructionsRewritten = rewrittenCount,
            ChangedAnything = rewrittenCount > 0,
        };
    }

    // ── Pre-pass: const-global immortality analysis ─────────────────────────────

    private void BuildConstGlobalInfo(IReadOnlyList<uint> code)
    {
        _constImmortalSlots.Clear();

        var initConstSlots = new HashSet<int>();
        var writtenSlots = new HashSet<int>();

        foreach (uint word in code)
        {
            OpCode op = Instruction.GetOp(word);

            if (op == OpCode.InitConstGlobal)
            {
                // ABx format: slot in Bx field.
                initConstSlots.Add(Instruction.GetBx(word));
            }
            else if (op == OpCode.SetGlobal)
            {
                // ABx format: slot in Bx field.
                writtenSlots.Add(Instruction.GetBx(word));
            }
            // UnsetGlobal uses Ax format (constant pool index of name string — not slot number).
            // We can't cheaply map it to a slot in the pre-pass, so we handle it conservatively
            // in per-block processing: any UnsetGlobal kills all active GetGlobal VNs.
        }

        // A slot is const-immortal iff it was initialized as const AND never re-assigned.
        foreach (int slot in initConstSlots)
        {
            if (!writtenSlots.Contains(slot))
                _constImmortalSlots.Add(slot);
        }
    }

    // ── Per-block LVN ──────────────────────────────────────────────────────────

    private int ProcessBlock(BasicBlock block, ControlFlowGraph cfg)
    {
        // regToVN[r] = VN assigned to register r  (0 = no VN / untracked)
        var regToVN = new Dictionary<int, int>();
        // vnToReg[vn] = canonical register holding this VN (the register that first produced it)
        var vnToReg = new Dictionary<int, byte>();
        // exprToVN[key] = VN for this expression (VN-hit lookup table)
        var exprToVN = new Dictionary<ExpressionKey, int>();

        // Per-category tracking for targeted invalidation:
        // slot → VN for mutable GetGlobal (killed by calls and SetGlobal for that slot)
        var mutableGlobalBySlot = new Dictionary<int, int>();
        // slot → VN for const-immortal GetGlobal (killed only by UnsetGlobal)
        var constGlobalBySlot = new Dictionary<int, int>();
        // ExpressionKeys for field/table ops (killed en masse by SetField/SetTable/calls)
        var activeFieldKeys = new HashSet<ExpressionKey>();

        int rewritten = 0;

        for (int i = 0; i < block.Words.Count; i++)
        {
            CfgWord word = block.Words[i];
            if (word.IsCompanion) continue;

            uint raw = word.Raw;
            OpCode op = Instruction.GetOp(raw);
            byte a = Instruction.GetA(raw);
            byte b = Instruction.GetB(raw);
            byte c = Instruction.GetC(raw);

            // ── Side-effectful instruction categories ─────────────────────────

            // Call-like: kill mutable globals + all field VNs; const globals survive.
            if (OpcodeOperands.IsCallLike(op))
            {
                KillMutableGlobalVns(regToVN, vnToReg, exprToVN, mutableGlobalBySlot);
                KillFieldVns(regToVN, vnToReg, exprToVN, activeFieldKeys);
                KillWrittenRegs(raw, regToVN, vnToReg);

                // For Call/CallSpread, the callee frame starts at A+1 and extends over
                // A+1..A+MaxRegs-1 (MaxRegs is unknown at this point).  Any register R > A
                // that the caller holds a VN for may be clobbered by the callee at runtime.
                // Kill those VN entries now so later instructions cannot use them as
                // canonical sources.  CallBuiltIn is excluded: it executes without pushing
                // a frame, so caller registers above A are never touched.
                if (op == OpCode.Call || op == OpCode.CallSpread)
                {
                    byte callA = Instruction.GetA(raw);
                    // Collect keys first to avoid mutating the dictionary mid-iteration.
                    var toKill = new List<int>();
                    foreach (int reg in regToVN.Keys)
                    {
                        if (reg > callA)
                            toKill.Add(reg);
                    }
                    foreach (int reg in toKill)
                        KillReg((byte)reg, regToVN, vnToReg);
                }

                continue;
            }

            // SetGlobal / InitConstGlobal: invalidate VN for that specific slot.
            if (op == OpCode.SetGlobal || op == OpCode.InitConstGlobal)
            {
                int slot = Instruction.GetBx(raw);
                KillGlobalSlotVn(slot, regToVN, vnToReg, exprToVN, mutableGlobalBySlot, constGlobalBySlot);
                continue;
            }

            // UnsetGlobal: kill ALL GetGlobal VNs (can't determine slot from Ax constant index).
            if (op == OpCode.UnsetGlobal)
            {
                KillMutableGlobalVns(regToVN, vnToReg, exprToVN, mutableGlobalBySlot);
                KillConstGlobalVns(regToVN, vnToReg, exprToVN, constGlobalBySlot);
                continue;
            }

            // SetField / SetTable: conservative — kill all field/table VNs.
            if (op == OpCode.SetField || op == OpCode.SetTable)
            {
                KillFieldVns(regToVN, vnToReg, exprToVN, activeFieldKeys);
                continue;
            }

            // ── Pure ops: attempt LVN ─────────────────────────────────────────

            if (!OpcodeOperands.IsPureForLvn(op))
            {
                // Not pure, not call-like, not global/field mutation — kill written regs and skip.
                KillWrittenRegs(raw, regToVN, vnToReg);
                continue;
            }

            // Classify the op.
            bool isGlobalOp = op == OpCode.GetGlobal;
            bool isFieldOp = op is OpCode.GetField or OpCode.GetFieldIC or OpCode.GetTable;

            // ── Build ExpressionKey ──────────────────────────────────────────

            ExpressionKey? keyOpt = BuildKey(op, raw, a, b, c, regToVN);
            if (keyOpt is null)
            {
                // Cannot form a stable key — assign a fresh unique VN so downstream
                // VnOf() calls produce correct non-colliding identity numbers.
                KillReg(a, regToVN, vnToReg);
                int identityVn = FreshVn();
                regToVN[a] = identityVn;
                vnToReg[identityVn] = a;
                continue;
            }

            ExpressionKey key = keyOpt.Value;

            // ── VN HIT check ─────────────────────────────────────────────────

            if (exprToVN.TryGetValue(key, out int existingVn) &&
                vnToReg.TryGetValue(existingVn, out byte existingReg))
            {
                // VN HIT: rewrite instruction to Move(a, existingReg).
                if (op == OpCode.GetFieldIC)
                {
                    // Remove the orphaned companion word immediately following GetFieldIC.
                    if (i + 1 < block.Words.Count && block.Words[i + 1].IsCompanion)
                    {
                        block.Words.RemoveAt(i + 1);
                        // Note: after removal, `i` still points at the current (now Move) word;
                        // the next iteration will correctly advance to the word after the removed companion.
                    }
                    // Signal Phase 4 that IC slot compaction may be needed.
                    cfg.HasOrphanedICSlots = true;
                }

                uint moveInstr = Instruction.EncodeABC(OpCode.Move, a, existingReg, 0);
                block.Words[i] = new CfgWord(moveInstr, false, word.OriginalIndex);
                rewritten++;

                // Update regToVN: register a now holds the same VN.
                KillReg(a, regToVN, vnToReg);
                regToVN[a] = existingVn;
                // Keep existingReg as the canonical holder (don't promote a).
                // If a == existingReg, KillReg removed vnToReg[existingVn]; restore it so
                // future VN HIT checks on the same VN can still find the canonical register.
                if (!vnToReg.ContainsKey(existingVn))
                    vnToReg[existingVn] = existingReg;

                // Track per-category VNs (already tracked from the miss that created them).
                continue;
            }

            // ── VN MISS: assign new VN ───────────────────────────────────────

            int newVn = FreshVn();
            KillReg(a, regToVN, vnToReg);
            regToVN[a] = newVn;
            vnToReg[newVn] = a;
            exprToVN[key] = newVn;

            // Track per-category for targeted invalidation.
            if (isGlobalOp)
            {
                int slot = Instruction.GetBx(raw);
                if (_constImmortalSlots.Contains(slot))
                    constGlobalBySlot[slot] = newVn;
                else
                    mutableGlobalBySlot[slot] = newVn;
            }
            if (isFieldOp)
                activeFieldKeys.Add(key);
        }

        return rewritten;
    }

    // ── ExpressionKey construction ──────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ExpressionKey? BuildKey(OpCode op, uint raw, byte a, byte b, byte c,
        Dictionary<int, int> regToVN)
    {
        return op switch
        {
            // Constants — keyed by opcode + literal value(s).
            OpCode.LoadNull => new ExpressionKey(op, 0, 0, 0),
            OpCode.LoadBool => new ExpressionKey(op, b, c, 0),  // b=0|1, c=skip flag
            OpCode.LoadK    => new ExpressionKey(op, Instruction.GetBx(raw), 0, 0),

            // Register copy — key is VN of source.
            OpCode.Move => new ExpressionKey(op, VnOf(b, regToVN), 0, 0),

            // Global read — key is slot number (ABx format, Bx = slot).
            OpCode.GetGlobal => new ExpressionKey(op, Instruction.GetBx(raw), 0, 0),

            // Field read: B=objReg, C=name-constant-index.
            OpCode.GetField or OpCode.GetFieldIC =>
                new ExpressionKey(op, VnOf(b, regToVN), c, 0),

            // Table read: B=objReg, C=keyReg.
            OpCode.GetTable => new ExpressionKey(op, VnOf(b, regToVN), VnOf(c, regToVN), 0),

            // Binary arithmetic/bitwise/comparison: B=left, C=right.
            OpCode.Add or OpCode.Sub or OpCode.Mul or OpCode.Div or OpCode.Mod or OpCode.Pow
            or OpCode.BAnd or OpCode.BOr or OpCode.BXor or OpCode.Shl or OpCode.Shr
            or OpCode.Lt  or OpCode.Le  or OpCode.Gt  or OpCode.Ge
            or OpCode.Eq  or OpCode.Ne =>
                new ExpressionKey(op, VnOf(b, regToVN), VnOf(c, regToVN), 0),

            // Fused-K binary: B=srcReg, C=constant-pool-index.
            OpCode.AddK or OpCode.SubK
            or OpCode.LtK or OpCode.LeK or OpCode.GtK or OpCode.GeK
            or OpCode.EqK or OpCode.NeK =>
                new ExpressionKey(op, VnOf(b, regToVN), c, 0),

            // Unary ops: B=srcReg.
            OpCode.Neg or OpCode.Not or OpCode.BNot or OpCode.TypeOf =>
                new ExpressionKey(op, VnOf(b, regToVN), 0, 0),

            // Is: B=valueReg, C encodes typeReg in low 7 bits + a flag in high bit.
            OpCode.Is =>
                new ExpressionKey(op, VnOf(b, regToVN), VnOf((byte)(c & 0x7F), regToVN), c & 0x80),

            _ => null,
        };
    }

    // ── VN helpers ──────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FreshVn() => _nextVn++;

    /// <summary>
    /// Returns the VN for <paramref name="reg"/>.  If the register is untracked, assigns a
    /// fresh unique "identity" VN so no two untracked registers collide in the key table.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int VnOf(byte reg, Dictionary<int, int> regToVN)
    {
        if (regToVN.TryGetValue(reg, out int vn)) return vn;
        int fresh = FreshVn();
        regToVN[reg] = fresh;
        return fresh;
    }

    // ── Register kill helpers ───────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void KillReg(byte reg, Dictionary<int, int> regToVN, Dictionary<int, byte> vnToReg)
    {
        if (regToVN.TryGetValue(reg, out int oldVn))
        {
            regToVN.Remove(reg);
            // Only remove the reverse mapping if this register was the canonical holder.
            if (vnToReg.TryGetValue(oldVn, out byte canonical) && canonical == reg)
                vnToReg.Remove(oldVn);
        }
    }

    private static void KillWrittenRegs(uint raw, Dictionary<int, int> regToVN, Dictionary<int, byte> vnToReg)
    {
        OpCode op = Instruction.GetOp(raw);
        byte a = Instruction.GetA(raw);

        // For most instructions the primary destination is R(A).
        // Multi-register writers need careful handling — for LVN we only track R(A) anyway
        // (we never number call results, allocating ops, etc.), so a simple R(A) kill is safe.
        int written = OpcodeOperands.GetWrittenReg(raw);
        if (written >= 0)
            KillReg((byte)written, regToVN, vnToReg);
    }

    // ── Targeted invalidation ───────────────────────────────────────────────────

    private static void KillMutableGlobalVns(
        Dictionary<int, int> regToVN,
        Dictionary<int, byte> vnToReg,
        Dictionary<ExpressionKey, int> exprToVN,
        Dictionary<int, int> mutableGlobalBySlot)
    {
        foreach (var (slot, vn) in mutableGlobalBySlot)
        {
            var key = new ExpressionKey(OpCode.GetGlobal, slot, 0, 0);
            exprToVN.Remove(key);
            RemoveVnMapping(vn, regToVN, vnToReg);
        }
        mutableGlobalBySlot.Clear();
    }

    private static void KillConstGlobalVns(
        Dictionary<int, int> regToVN,
        Dictionary<int, byte> vnToReg,
        Dictionary<ExpressionKey, int> exprToVN,
        Dictionary<int, int> constGlobalBySlot)
    {
        foreach (var (slot, vn) in constGlobalBySlot)
        {
            var key = new ExpressionKey(OpCode.GetGlobal, slot, 0, 0);
            exprToVN.Remove(key);
            RemoveVnMapping(vn, regToVN, vnToReg);
        }
        constGlobalBySlot.Clear();
    }

    private static void KillGlobalSlotVn(
        int slot,
        Dictionary<int, int> regToVN,
        Dictionary<int, byte> vnToReg,
        Dictionary<ExpressionKey, int> exprToVN,
        Dictionary<int, int> mutableGlobalBySlot,
        Dictionary<int, int> constGlobalBySlot)
    {
        var key = new ExpressionKey(OpCode.GetGlobal, slot, 0, 0);
        if (exprToVN.TryGetValue(key, out int vn))
        {
            exprToVN.Remove(key);
            RemoveVnMapping(vn, regToVN, vnToReg);
        }
        mutableGlobalBySlot.Remove(slot);
        constGlobalBySlot.Remove(slot);
    }

    private static void KillFieldVns(
        Dictionary<int, int> regToVN,
        Dictionary<int, byte> vnToReg,
        Dictionary<ExpressionKey, int> exprToVN,
        HashSet<ExpressionKey> activeFieldKeys)
    {
        foreach (ExpressionKey key in activeFieldKeys)
        {
            if (exprToVN.TryGetValue(key, out int vn))
            {
                exprToVN.Remove(key);
                RemoveVnMapping(vn, regToVN, vnToReg);
            }
        }
        activeFieldKeys.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RemoveVnMapping(int vn, Dictionary<int, int> regToVN, Dictionary<int, byte> vnToReg)
    {
        if (vnToReg.TryGetValue(vn, out byte reg))
        {
            vnToReg.Remove(vn);
            if (regToVN.TryGetValue(reg, out int storedVn) && storedVn == vn)
                regToVN.Remove(reg);
        }
    }
}

/// <summary>
/// Value-number expression key — uniquely identifies a pure expression by its opcode
/// and up to three integer operands (value numbers of source registers, or literal indices).
/// </summary>
internal readonly record struct ExpressionKey(
    OpCode Op,
    int Operand1,
    int Operand2,
    int Operand3);
