using System;
using System.Collections.Generic;

namespace Stash.Bytecode;

/// <summary>
/// Validates bytecode chunks for structural correctness before execution.
/// Use for externally-produced bytecode to catch malformed instructions
/// that could cause crashes or undefined behavior in the VM.
/// </summary>
public sealed class BytecodeVerifier
{
    /// <summary>
    /// Verifies a chunk and all its nested sub-chunks (closures).
    /// Returns a result indicating whether the bytecode is valid.
    /// </summary>
    public static BytecodeVerificationResult Verify(Chunk chunk)
    {
        var errors = new List<BytecodeVerificationError>();
        VerifyChunk(chunk, errors, prefix: null);
        return new BytecodeVerificationResult(errors);
    }

    private static void VerifyChunk(Chunk chunk, List<BytecodeVerificationError> errors, string? prefix)
    {
        if (chunk.Code.Length == 0)
        {
            AddError(errors, -1, prefix, "Code array is empty.");
            return;
        }

        int len = chunk.Code.Length;
        int lastInstrIdx = -1;

        for (int i = 0; i < len; i++)
        {
            int instrIdx = i;
            uint word = chunk.Code[i];
            var op = Instruction.GetOp(word);

            // 1. Validate opcode range (0–97); keep upper bound in sync with OpCode enum (LockEnd = 97)
            if ((byte)op > 97)
            {
                AddError(errors, instrIdx, prefix, $"Invalid opcode {(byte)op}.");
                lastInstrIdx = instrIdx;
                continue;
            }

            var fmt = OpCodeInfo.GetFormat(op);
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            byte c = Instruction.GetC(word);
            ushort bx = Instruction.GetBx(word);

            // 2. A is always a register except for Ax-format opcodes (TryEnd, ElevateEnd, LockEnd)
            if (fmt != OpCodeFormat.Ax && a >= chunk.MaxRegs)
                AddError(errors, instrIdx, prefix, $"Opcode {op}: register A={a} out of bounds (MaxRegs={chunk.MaxRegs}).");

            switch (op)
            {
                // ── B and C are both registers ───────────────────────────────────────

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
                case OpCode.GetTable:
                case OpCode.SetTable:
                case OpCode.NewRange:
                case OpCode.In:
                    CheckReg(errors, instrIdx, prefix, chunk, "B", b);
                    CheckReg(errors, instrIdx, prefix, chunk, "C", c);
                    break;

                // ── PipeChain: B=stageCount (not a register), C=partsBase; followed by B companion words ─

                case OpCode.PipeChain:
                {
                    // C is partsBase — a register that must be in bounds
                    CheckReg(errors, instrIdx, prefix, chunk, "C", c);
                    // B is the stage count — not a register, but must be >= 2
                    if (b < 2)
                        AddError(errors, instrIdx, prefix, $"Opcode {op}: stage count B={b} is invalid (must be >= 2).");
                    // Skip B companion words
                    int stageCount = b;
                    for (int s = 0; s < stageCount; s++)
                    {
                        i++;
                        if (i >= len)
                        {
                            AddError(errors, instrIdx, prefix, $"Opcode {op}: missing companion word {s} (expected {stageCount} total).");
                            break;
                        }
                        // Companion word: bits 15-8 = partCount, bits 7-0 = flags. No further validation needed.
                    }
                    break;
                }

                // ── B is a register, C is an immediate ──────────────────────────────

                case OpCode.Move:
                case OpCode.Neg:
                case OpCode.BNot:
                case OpCode.Not:
                case OpCode.TypeOf:
                case OpCode.TryExpr:
                case OpCode.Await:
                case OpCode.CheckNumeric:
                case OpCode.TestSet:     // C = expected truthiness (0 or 1)
                case OpCode.ElevateBegin: // C unused
                case OpCode.Spread:      // expand R(B); C unused
                    CheckReg(errors, instrIdx, prefix, chunk, "B", b);
                    break;

                // ── B is a register, C is a constant index ──────────────────────────

                case OpCode.GetField:
                    CheckReg(errors, instrIdx, prefix, chunk, "B", b);
                    CheckConst(errors, instrIdx, prefix, chunk, "C", c);
                    break;

                case OpCode.Self:
                    CheckReg(errors, instrIdx, prefix, chunk, "B", b);
                    CheckConst(errors, instrIdx, prefix, chunk, "C", c);
                    break;

                case OpCode.Is:
                    CheckReg(errors, instrIdx, prefix, chunk, "B", b);
                    CheckConst(errors, instrIdx, prefix, chunk, "C", c);
                    break;

                // ── B is a constant index, C is a register ──────────────────────────

                case OpCode.SetField:    // R(A).K(B) = R(C)
                    CheckConst(errors, instrIdx, prefix, chunk, "B", b);
                    CheckReg(errors, instrIdx, prefix, chunk, "C", c);
                    break;

                // ── B is flags (immediate), C is a register ──────────────────────────

                case OpCode.Redirect:    // R(A) stream (B flags) → file R(C)
                    CheckReg(errors, instrIdx, prefix, chunk, "C", c);
                    break;

                // ── B is a register, C is a constant index (constant-fusion opcodes) ─

                case OpCode.AddK:
                case OpCode.SubK:
                case OpCode.EqK:
                case OpCode.NeK:
                case OpCode.LtK:
                case OpCode.LeK:
                case OpCode.GtK:
                case OpCode.GeK:
                    CheckReg(errors, instrIdx, prefix, chunk, "B", b);
                    CheckConst(errors, instrIdx, prefix, chunk, "C", c);
                    break;

                // ── LoadK: Bx is a constant index ───────────────────────────────────

                case OpCode.LoadK:
                    CheckConstBx(errors, instrIdx, prefix, chunk, bx);
                    break;

                // ── Global slot bounds ───────────────────────────────────────────────

                case OpCode.GetGlobal:
                case OpCode.SetGlobal:
                case OpCode.InitConstGlobal:
                    if (chunk.GlobalSlotCount == 0 || bx >= chunk.GlobalSlotCount)
                        AddError(errors, instrIdx, prefix, $"Opcode {op}: global slot {bx} out of bounds (GlobalSlotCount={chunk.GlobalSlotCount}).");
                    break;

                // ── NewStruct: B is a constant index ────────────────────────────────

                case OpCode.NewStruct:   // R(A) = new K(B) with C fields
                    CheckConst(errors, instrIdx, prefix, chunk, "B", b);
                    break;

                // ── ABx opcodes where Bx is a constant index ─────────────────────────

                case OpCode.StructDecl:
                case OpCode.EnumDecl:
                case OpCode.IfaceDecl:
                case OpCode.Extend:
                case OpCode.Import:
                case OpCode.ImportAs:
                case OpCode.Switch:
                case OpCode.Destructure:
                case OpCode.Retry:
                case OpCode.TypedWrap:
                    CheckConstBx(errors, instrIdx, prefix, chunk, bx);
                    break;

                // ── Jump opcodes with signed sBx offset ──────────────────────────────

                case OpCode.Jmp:
                case OpCode.JmpFalse:
                case OpCode.JmpTrue:
                case OpCode.Loop:
                case OpCode.ForPrep:
                case OpCode.ForLoop:
                case OpCode.ForPrepII:
                case OpCode.ForLoopII:
                case OpCode.IterLoop:
                case OpCode.TryBegin:
                {
                    int sbx = Instruction.GetSBx(word);
                    int target = instrIdx + 1 + sbx;
                    if (target < 0 || target > len)
                        AddError(errors, instrIdx, prefix, $"Opcode {op}: jump target {target} out of bounds [0, {len}].");
                    break;
                }

                // ── GetFieldIC: B=object register, C=constant; companion = IC slot index ─

                case OpCode.GetFieldIC:
                    CheckReg(errors, instrIdx, prefix, chunk, "B", b);
                    CheckConst(errors, instrIdx, prefix, chunk, "C", c);
                    if (i + 1 >= len)
                    {
                        AddError(errors, instrIdx, prefix, $"Opcode {op}: missing companion word.");
                    }
                    else
                    {
                        uint icWord = chunk.Code[i + 1];
                        int icSlotCount = chunk.ICSlots?.Length ?? 0;
                        if (icSlotCount == 0 || (int)icWord >= icSlotCount)
                            AddError(errors, instrIdx, prefix, $"Opcode {op}: IC slot index {icWord} out of bounds (ICSlots.Length={icSlotCount}).");
                    }
                    i++; // skip companion word
                    break;

                // ── CallBuiltIn: B=namespace register, C=arg count; companion = IC slot index ─

                case OpCode.CallBuiltIn:
                    CheckReg(errors, instrIdx, prefix, chunk, "B", b);
                    // C is arg count, not a register
                    if (i + 1 >= len)
                    {
                        AddError(errors, instrIdx, prefix, $"Opcode {op}: missing companion word.");
                    }
                    else
                    {
                        uint icWord = chunk.Code[i + 1];
                        int icSlotCount = chunk.ICSlots?.Length ?? 0;
                        if (icSlotCount == 0 || (int)icWord >= icSlotCount)
                            AddError(errors, instrIdx, prefix, $"Opcode {op}: IC slot index {icWord} out of bounds (ICSlots.Length={icSlotCount}).");
                    }
                    i++; // skip companion word
                    break;

                // ── Closure: Bx = constant (must be Chunk); followed by N upvalue descriptors ─

                case OpCode.Closure:
                {
                    CheckConstBx(errors, instrIdx, prefix, chunk, bx);

                    int uvCount = 0;
                    Chunk? subChunk = null;
                    if (bx < chunk.Constants.Length)
                    {
                        if (chunk.Constants[bx].AsObj is Chunk sc)
                        {
                            subChunk = sc;
                            uvCount = sc.Upvalues.Length;
                        }
                        else
                        {
                            AddError(errors, instrIdx, prefix, $"Opcode {op}: constant[{bx}] is not a Chunk.");
                        }
                    }

                    for (int u = 0; u < uvCount; u++)
                    {
                        i++;
                        if (i >= len)
                        {
                            AddError(errors, instrIdx, prefix, $"Opcode {op}: missing upvalue descriptor word {u}.");
                            break;
                        }
                        byte isLocal = (byte)(chunk.Code[i] & 0xFF);
                        if (isLocal != 0 && isLocal != 1)
                            AddError(errors, i, prefix, $"Opcode {op}: upvalue descriptor {u} has invalid isLocal={isLocal} (must be 0 or 1).");
                    }

                    if (subChunk != null)
                    {
                        string subName = subChunk.Name ?? "<anonymous>";
                        string subPrefix = prefix == null
                            ? $"closure '{subName}'"
                            : $"{prefix} > closure '{subName}'";
                        VerifyChunk(subChunk, errors, subPrefix);
                    }
                    break;
                }

                // ── CatchMatch: ABx — A=errReg (register), Bx=constant index (string[]) ──

                case OpCode.CatchMatch:
                    // A (errReg) already validated by the general A-register check above.
                    CheckConstBx(errors, instrIdx, prefix, chunk, bx);
                    break;

                // ── Rethrow: A=catch register — no operand validation beyond A-register check ─

                case OpCode.Rethrow:
                    // A (catch register) already validated by the general A-register check above.
                    break;

                // ── LockBegin: ABC — A=errReg, B=pathReg, C=constIdx; R(B+1), R(B+2) also used ──

                case OpCode.LockBegin:
                {
                    if (b + 2 >= chunk.MaxRegs) AddError(errors, instrIdx, prefix, $"LockBegin: registers {b},{b + 1},{b + 2} out of bounds (max {chunk.MaxRegs}).");
                    if (c >= chunk.Constants.Length) AddError(errors, instrIdx, prefix, $"LockBegin: constant index {c} out of bounds.");
                    break;
                }

                case OpCode.LockEnd:
                    break; // No operands to validate
            }

            lastInstrIdx = instrIdx;
        }

        // 7. Last instruction should be Return to prevent falling off the end
        if (lastInstrIdx >= 0)
        {
            var lastOp = Instruction.GetOp(chunk.Code[lastInstrIdx]);
            if (lastOp != OpCode.Return)
            {
                string opName = Enum.IsDefined(lastOp) ? lastOp.ToString() : $"op_{(byte)lastOp:x2}";
                AddError(errors, lastInstrIdx, prefix,
                    $"Warning: last instruction is {opName} (expected Return); execution may fall off the end.");
            }
        }
    }

    private static void CheckReg(List<BytecodeVerificationError> errors, int idx, string? prefix, Chunk chunk, string name, byte value)
    {
        if (value >= chunk.MaxRegs)
            AddError(errors, idx, prefix, $"Register {name}={value} out of bounds (MaxRegs={chunk.MaxRegs}).");
    }

    private static void CheckConst(List<BytecodeVerificationError> errors, int idx, string? prefix, Chunk chunk, string name, byte value)
    {
        if (value >= chunk.Constants.Length)
            AddError(errors, idx, prefix, $"Constant index {name}={value} out of bounds (Constants.Length={chunk.Constants.Length}).");
    }

    private static void CheckConstBx(List<BytecodeVerificationError> errors, int idx, string? prefix, Chunk chunk, ushort bx)
    {
        if (bx >= chunk.Constants.Length)
            AddError(errors, idx, prefix, $"Constant index Bx={bx} out of bounds (Constants.Length={chunk.Constants.Length}).");
    }

    private static void AddError(List<BytecodeVerificationError> errors, int idx, string? prefix, string message)
    {
        string fullMessage = prefix == null ? message : $"[{prefix}] {message}";
        errors.Add(new BytecodeVerificationError(idx, fullMessage));
    }
}

public sealed class BytecodeVerificationResult
{
    public bool IsValid => _errors.Count == 0;
    public IReadOnlyList<BytecodeVerificationError> Errors => _errors;

    private readonly List<BytecodeVerificationError> _errors;

    internal BytecodeVerificationResult(List<BytecodeVerificationError> errors)
    {
        _errors = errors;
    }
}

public sealed record BytecodeVerificationError(int InstructionIndex, string Message);
