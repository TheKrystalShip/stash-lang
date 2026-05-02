using System;
using System.Collections.Generic;
using Stash.Runtime;

namespace Stash.Bytecode.Optimization;

/// <summary>
/// Register-operand classification helpers for the copy-propagation pass.
/// Mirrors ChunkBuilder's private DceGetWrittenReg / DceAddReads but exposed as a
/// static class usable from optimisation-pass implementations.
/// </summary>
internal static class OpcodeOperands
{
    // ── Bit-manipulation patch helpers ────────────────────────────────────────
    // ABC encoding: op = bits 0-7, A = bits 8-15, B = bits 16-23, C = bits 24-31

    private static uint PatchA(uint instr, byte a) => (instr & 0xFFFF00FFu) | ((uint)a << 8);
    private static uint PatchB(uint instr, byte b) => (instr & 0xFF00FFFFu) | ((uint)b << 16);
    private static uint PatchC(uint instr, byte c) => (instr & 0x00FFFFFFu) | ((uint)c << 24);

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the primary destination register written by <paramref name="instr"/>, or -1 if
    /// the instruction writes no register or writes multiple registers in a non-standard layout.
    /// Mirrors ChunkBuilder.DceGetWrittenReg.
    /// </summary>
    public static int GetWrittenReg(uint instr)
    {
        OpCode op = Instruction.GetOp(instr);
        return op switch
        {
            // Pure (DCE-safe) opcodes that write R(A)
            OpCode.LoadK or OpCode.LoadNull or OpCode.LoadBool or OpCode.Move
                or OpCode.Eq or OpCode.Ne or OpCode.EqK or OpCode.NeK
                or OpCode.Not or OpCode.TypeOf
            // Effectful opcodes that write R(A)
                or OpCode.Call or OpCode.CallSpread or OpCode.CallBuiltIn
                or OpCode.GetTable or OpCode.GetField or OpCode.GetFieldIC
                or OpCode.GetUpval or OpCode.GetGlobal
                or OpCode.TryExpr
                or OpCode.NewArray or OpCode.NewDict or OpCode.NewRange
                or OpCode.Closure or OpCode.NewStruct
                or OpCode.Command
                or OpCode.Import or OpCode.ImportAs
                or OpCode.StructDecl or OpCode.EnumDecl or OpCode.IfaceDecl
                or OpCode.Await
                or OpCode.TryBegin
                or OpCode.Timeout or OpCode.Retry or OpCode.ElevateBegin
                or OpCode.Add or OpCode.Sub or OpCode.Mul or OpCode.Div or OpCode.Mod
                or OpCode.Pow or OpCode.Neg or OpCode.AddI
                or OpCode.BAnd or OpCode.BOr or OpCode.BXor or OpCode.BNot
                or OpCode.Shl or OpCode.Shr
                or OpCode.Lt or OpCode.Le or OpCode.Gt or OpCode.Ge
                or OpCode.AddK or OpCode.SubK
                or OpCode.LtK or OpCode.LeK or OpCode.GtK or OpCode.GeK
                or OpCode.Is or OpCode.In
                or OpCode.TestSet  // conditionally writes R(A)
                or OpCode.Interpolate or OpCode.Spread  // write R(A) = result
                => Instruction.GetA(instr),
            _ => -1,
        };
    }

    /// <summary>
    /// Invokes <paramref name="onWrite"/> for every register written by <paramref name="instr"/>.
    /// Handles multi-register writers conservatively.
    /// <paramref name="constants"/> is used to determine sub-chunk upvalue counts for
    /// Closure companion-word skipping (not needed for writes, but kept for API symmetry).
    /// </summary>
    public static void ForEachWrittenReg(uint instr, IReadOnlyList<StashValue> constants, Action<byte> onWrite)
    {
        OpCode op = Instruction.GetOp(instr);
        byte a = Instruction.GetA(instr);
        byte c = Instruction.GetC(instr);

        switch (op)
        {
            // Call: conservatively A..A+max(1,C)-1 (always at least R(A)=return value)
            case OpCode.Call:
            {
                int count = Math.Max(1, (int)c);
                for (int i = 0; i < count; i++)
                    onWrite((byte)(a + i));
                break;
            }

            // ForPrep/ForLoop (numeric): conservatively A, A+1, A+2, A+3
            case OpCode.ForPrep:
            case OpCode.ForLoop:
            case OpCode.ForPrepII:
            case OpCode.ForLoopII:
                onWrite(a);
                onWrite((byte)(a + 1));
                onWrite((byte)(a + 2));
                onWrite((byte)(a + 3));
                break;

            // IterPrep/IterLoop: A, A+1, A+2
            case OpCode.IterPrep:
            case OpCode.IterLoop:
                onWrite(a);
                onWrite((byte)(a + 1));
                onWrite((byte)(a + 2));
                break;

            // Self writes R(A) (method) and R(A+1) (self-object copy)
            case OpCode.Self:
                onWrite(a);
                onWrite((byte)(a + 1));
                break;

            // Destructure writes R(A)..R(A+N-1) where N comes from the metadata.
            case OpCode.Destructure:
            {
                ushort bx = Instruction.GetBx(instr);
                var meta = (DestructureMetadata)constants[bx].AsObj!;
                int count = meta.Names.Length + (meta.RestName != null ? 1 : 0);
                for (int i = 0; i < count; i++)
                    onWrite((byte)(a + i));
                break;
            }

            // Import writes R(A)..R(A+N-1) where N = number of imported names.
            case OpCode.Import:
            {
                ushort bx = Instruction.GetBx(instr);
                var meta = (ImportMetadata)constants[bx].AsObj!;
                for (int i = 0; i < meta.Names.Length; i++)
                    onWrite((byte)(a + i));
                break;
            }

            // Default: use GetWrittenReg (handles all single-write opcodes)
            default:
            {
                int reg = GetWrittenReg(instr);
                if (reg >= 0)
                    onWrite((byte)reg);
                break;
            }
        }
    }

    /// <summary>
    /// Returns a new instruction word with all register read-operands rewritten via
    /// <paramref name="rewrite"/>.  Only supports "simple" (non-multi-reg-range) forms.
    /// For complex instructions (Call, NewArray, ForPrep, etc.) returns
    /// <paramref name="instr"/> unchanged.
    /// </summary>
    public static uint RewriteReadRegs(uint instr, Func<byte, byte> rewrite)
    {
        OpCode op = Instruction.GetOp(instr);
        byte a = Instruction.GetA(instr);
        byte b = Instruction.GetB(instr);
        byte c = Instruction.GetC(instr);

        switch (op)
        {
            // ── Reads R(B) only ────────────────────────────────────────────────────────
            case OpCode.Move:
            case OpCode.Neg:
            case OpCode.Not:
            case OpCode.BNot:
            case OpCode.TypeOf:
            case OpCode.GetField:
            case OpCode.GetFieldIC:
            case OpCode.Spread:
            case OpCode.Await:
            case OpCode.TryExpr:
            case OpCode.TestSet:
            case OpCode.AddK:
            case OpCode.SubK:
            case OpCode.EqK:
            case OpCode.NeK:
            case OpCode.LtK:
            case OpCode.LeK:
            case OpCode.GtK:
            case OpCode.GeK:
            case OpCode.Self:
            {
                byte nb = rewrite(b);
                return nb == b ? instr : PatchB(instr, nb);
            }

            // ── Reads R(B) and R(C) ───────────────────────────────────────────────────
            case OpCode.Add:
            case OpCode.Sub:
            case OpCode.Mul:
            case OpCode.Div:
            case OpCode.Mod:
            case OpCode.Pow:
            case OpCode.BAnd:
            case OpCode.BOr:
            case OpCode.BXor:
            case OpCode.Shl:
            case OpCode.Shr:
            case OpCode.Eq:
            case OpCode.Ne:
            case OpCode.Lt:
            case OpCode.Le:
            case OpCode.Gt:
            case OpCode.Ge:
            case OpCode.In:
            case OpCode.GetTable:
            {
                byte nb = rewrite(b);
                byte nc = rewrite(c);
                uint r = instr;
                if (nb != b) r = PatchB(r, nb);
                if (nc != c) r = PatchC(r, nc);
                return r;
            }

            // ── AddI: AsBx format, reads AND writes R(A) in-place ─────────────────────
            // Cannot substitute A: the same register is both source and destination, so
            // replacing the read-side would also move the write-destination, producing
            // semantically incorrect code (r(src) = r(src)+k instead of r(A) = r(A)+k).
            // Leave unchanged; the dead-Move will be removed by DCE after AddI kills A.

            // ── Reads R(A) only ───────────────────────────────────────────────────────
            case OpCode.JmpFalse:
            case OpCode.JmpTrue:
            case OpCode.Throw:
            case OpCode.SetGlobal:
            case OpCode.InitConstGlobal:
            case OpCode.SetUpval:
            case OpCode.CloseUpval:
            case OpCode.Switch:
            case OpCode.Test:
            case OpCode.TypedWrap:
            case OpCode.CheckNumeric:
            case OpCode.CatchMatch:
            case OpCode.Defer:
            {
                byte na = rewrite(a);
                return na == a ? instr : PatchA(instr, na);
            }

            // ── Import / ImportAs / Destructure: A is both source and first write slot ─
            // All three read R(A) as input and write results starting at R(A), R(A+1), …
            // Substituting A would redirect both the read AND the write to the copy-source
            // register, leaving the original A register with a stale value while
            // downstream SetGlobal/DeclareLocal instructions still reference A.
            // Leave unchanged; ForEachWrittenReg kills all written slots conservatively.

            // ── Return: reads R(A) only when B != 0 ──────────────────────────────────
            case OpCode.Return:
            {
                if (b == 0) return instr; // returns null, no register read
                byte na = rewrite(a);
                return na == a ? instr : PatchA(instr, na);
            }

            // ── SetField: reads R(A)=object, R(C)=value ──────────────────────────────
            case OpCode.SetField:
            {
                byte na = rewrite(a);
                byte nc = rewrite(c);
                uint r = instr;
                if (na != a) r = PatchA(r, na);
                if (nc != c) r = PatchC(r, nc);
                return r;
            }

            // ── SetTable: reads R(A)=table, R(B)=key, R(C)=value ─────────────────────
            case OpCode.SetTable:
            {
                byte na = rewrite(a);
                byte nb = rewrite(b);
                byte nc = rewrite(c);
                uint r = instr;
                if (na != a) r = PatchA(r, na);
                if (nb != b) r = PatchB(r, nb);
                if (nc != c) r = PatchC(r, nc);
                return r;
            }

            // ── Is: reads R(B)=value, R(C & 0x7F)=type; preserve high bit of C ────────
            case OpCode.Is:
            {
                byte nb = rewrite(b);
                byte cLow = (byte)(c & 0x7F);
                byte cHigh = (byte)(c & 0x80);
                byte ncLow = rewrite(cLow);
                uint r = instr;
                if (nb != b) r = PatchB(r, nb);
                if (ncLow != cLow) r = PatchC(r, (byte)(ncLow | cHigh));
                return r;
            }

            // ── ElevateBegin: reads R(A) and R(B) ────────────────────────────────────
            case OpCode.ElevateBegin:
            {
                byte na = rewrite(a);
                byte nb = rewrite(b);
                uint r = instr;
                if (na != a) r = PatchA(r, na);
                if (nb != b) r = PatchB(r, nb);
                return r;
            }

            // ── Redirect: reads R(A) and R(C) ────────────────────────────────────────
            case OpCode.Redirect:
            {
                byte na = rewrite(a);
                byte nc = rewrite(c);
                uint r = instr;
                if (na != a) r = PatchA(r, na);
                if (nc != c) r = PatchC(r, nc);
                return r;
            }

            // ── Timeout: reads R(A) (duration); R(A+1)=body cannot be expressed as a field ─
            case OpCode.Timeout:
            {
                byte na = rewrite(a);
                return na == a ? instr : PatchA(instr, na);
            }

            // ── NewRange: reads R(B)=start, R(C)=end; R(A+1)=step is implicit ─────────
            case OpCode.NewRange:
            {
                byte nb = rewrite(b);
                byte nc = rewrite(c);
                uint r = instr;
                if (nb != b) r = PatchB(r, nb);
                if (nc != c) r = PatchC(r, nc);
                return r;
            }

            // ── Complex multi-reg forms — return unchanged ────────────────────────────
            case OpCode.AddI:         // in-place read+write of A — see note above
            case OpCode.Import:       // reads A (path), writes A..A+N-1
            case OpCode.ImportAs:     // reads A (path), writes A (namespace object)
            case OpCode.Destructure:  // reads A (source), writes A..A+N-1
            case OpCode.Call:
            case OpCode.CallSpread:
            case OpCode.CallBuiltIn:
            case OpCode.NewArray:
            case OpCode.NewDict:
            case OpCode.NewStruct:
            case OpCode.Interpolate:
            case OpCode.Command:
            case OpCode.ForPrep:
            case OpCode.ForLoop:
            case OpCode.ForPrepII:
            case OpCode.ForLoopII:
            case OpCode.IterPrep:
            case OpCode.IterLoop:
            case OpCode.LockBegin:
            case OpCode.Retry:
            case OpCode.PipeChain:
            case OpCode.StructDecl:
            case OpCode.EnumDecl:
            case OpCode.IfaceDecl:
            case OpCode.Extend:
            case OpCode.Closure:
                return instr;

            // ── No register reads, Ax-format, or otherwise unclassified ──────────────
            default:
                return instr;
        }
    }
}
