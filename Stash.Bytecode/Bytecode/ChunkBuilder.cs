using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Stdlib;

namespace Stash.Bytecode;

/// <summary>
/// Builds a <see cref="Chunk"/> incrementally by emitting 32-bit register-based instructions.
/// </summary>
public sealed class ChunkBuilder
{
    private readonly List<uint> _code = new();
    private readonly List<StashValue> _constants = new();
    private readonly Dictionary<StashValue, ushort> _constantMap = new(StashValueComparer.Instance);
    private readonly List<SourceMapEntry> _sourceEntries = new();
    private readonly List<UpvalueDescriptor> _upvalues = new();
    private int _icSlotCount;
    private List<(ushort Slot, ushort ConstIndex)>? _constGlobalInits;
    private StdlibManifest? _stdlibManifest;

    // ---- Metadata (set by Compiler before Build) ----
    public int Arity { get; set; }
    public int MinArity { get; set; }
    public int MaxRegs { get; set; }
    public string? Name { get; set; }
    public bool IsAsync { get; set; }
    public bool HasRestParam { get; set; }
    public bool MayHaveCapturedLocals { get; set; }
    public string[]? LocalNames { get; set; }
    public bool[]? LocalIsConst { get; set; }
    public string[]? UpvalueNames { get; set; }

    // Global slot support (shared across compilation unit)
    private GlobalSlotAllocator? _globalSlots;
    internal void SetGlobalSlots(GlobalSlotAllocator allocator) => _globalSlots = allocator;

    /// <summary>Current instruction count (index of the next instruction to be emitted).</summary>
    public int CurrentOffset => _code.Count;

    // ==================================================================
    // Instruction Emission
    // ==================================================================

    /// <summary>Emit an ABC-format instruction.</summary>
    public void EmitABC(OpCode op, byte a, byte b, byte c)
        => _code.Add(Instruction.EncodeABC(op, a, b, c));

    /// <summary>Emit an ABC instruction with A only (B=0, C=0).</summary>
    public void EmitA(OpCode op, byte a)
        => _code.Add(Instruction.EncodeABC(op, a, 0, 0));

    /// <summary>Emit an ABC instruction with A and B (C=0).</summary>
    public void EmitAB(OpCode op, byte a, byte b)
        => _code.Add(Instruction.EncodeABC(op, a, b, 0));

    /// <summary>Emit an ABx-format instruction.</summary>
    public void EmitABx(OpCode op, byte a, ushort bx)
        => _code.Add(Instruction.EncodeABx(op, a, bx));

    /// <summary>Emit an AsBx-format instruction.</summary>
    public void EmitAsBx(OpCode op, byte a, int sbx)
        => _code.Add(Instruction.EncodeAsBx(op, a, sbx));

    /// <summary>Emit an Ax-format instruction.</summary>
    public void EmitAx(OpCode op, uint ax)
        => _code.Add(Instruction.EncodeAx(op, ax));

    /// <summary>Emit a raw 32-bit instruction word (for upvalue descriptors after Closure).</summary>
    public void EmitRaw(uint word) => _code.Add(word);

    // ==================================================================
    // Jump Support
    // ==================================================================

    /// <summary>
    /// Emit a jump instruction with a placeholder offset (0).
    /// Returns the instruction index for later patching via PatchJump.
    /// </summary>
    public int EmitJump(OpCode op, byte a = 0)
    {
        int index = _code.Count;
        EmitAsBx(op, a, 0); // placeholder sBx = 0
        return index;
    }

    /// <summary>
    /// Patch a previously emitted jump instruction to jump to the current position.
    /// The offset is relative: currentOffset - patchIndex - 1.
    /// </summary>
    public void PatchJump(int patchIndex)
    {
        int offset = _code.Count - patchIndex - 1;
        if (offset > Instruction.SBxMax || offset < Instruction.SBxMin)
            throw new InvalidOperationException($"Jump offset {offset} out of range.");
        _code[patchIndex] = Instruction.PatchSBx(_code[patchIndex], offset);
    }

    /// <summary>
    /// Emit a backward jump (loop) to the given target instruction index.
    /// Uses Loop opcode with cancellation check.
    /// </summary>
    public void EmitLoop(byte a, int loopTarget)
    {
        int offset = loopTarget - _code.Count - 1; // negative offset
        if (offset < Instruction.SBxMin || offset > Instruction.SBxMax)
            throw new InvalidOperationException($"Loop offset {offset} out of range.");
        EmitAsBx(OpCode.Loop, a, offset);
    }

    // ==================================================================
    // Constants
    // ==================================================================

    /// <summary>
    /// Add a constant to the pool, deduplicating matching values.
    /// Returns the constant pool index.
    /// </summary>
    public ushort AddConstant(StashValue value)
    {
        if (_constantMap.TryGetValue(value, out ushort existing))
            return existing;

        if (_constants.Count >= Instruction.BxMax)
            throw new InvalidOperationException("Constant pool overflow (>65535 entries).");

        ushort index = (ushort)_constants.Count;
        _constants.Add(value);
        _constantMap[value] = index;
        return index;
    }

    /// <summary>Add a string constant.</summary>
    public ushort AddConstant(string value) => AddConstant(StashValue.FromObj(value));

    /// <summary>Add a long constant.</summary>
    public ushort AddConstant(long value) => AddConstant(StashValue.FromInt(value));

    /// <summary>Add a double constant.</summary>
    public ushort AddConstant(double value) => AddConstant(StashValue.FromFloat(value));

    /// <summary>Add an object constant (metadata records, etc.).</summary>
    public ushort AddConstant(object value) => AddConstant(StashValue.FromObj(value));

    // ==================================================================
    // Upvalues
    // ==================================================================

    /// <summary>
    /// Register an upvalue descriptor. Returns the upvalue index.
    /// Deduplicates identical descriptors.
    /// </summary>
    public byte AddUpvalue(byte index, bool isLocal)
    {
        for (int i = 0; i < _upvalues.Count; i++)
        {
            if (_upvalues[i].Index == index && _upvalues[i].IsLocal == isLocal)
                return (byte)i;
        }

        if (_upvalues.Count >= 256)
            throw new InvalidOperationException("Upvalue overflow (>256 upvalues per function).");

        byte idx = (byte)_upvalues.Count;
        _upvalues.Add(new UpvalueDescriptor(index, isLocal));
        return idx;
    }

    // ==================================================================
    // Inline Cache
    // ==================================================================

    private readonly List<ushort> _icConstantIndices = new();

    /// <summary>Allocate an inline cache slot. Returns the IC slot index.</summary>
    public ushort AllocateICSlot() => AllocateICSlot(0);

    /// <summary>Allocate an inline cache slot with a known constant index. Returns the IC slot index.</summary>
    public ushort AllocateICSlot(ushort constantIndex)
    {
        ushort slot = (ushort)_icSlotCount++;
        while (_icConstantIndices.Count <= slot)
            _icConstantIndices.Add(0);
        _icConstantIndices[slot] = constantIndex;
        return slot;
    }

    // ==================================================================
    // Const Global Metadata Init
    // ==================================================================

    /// <summary>Record a const global that should be initialized from the constant pool (metadata-based, no bytecode).</summary>
    public void AddConstGlobalInit(ushort slot, ushort constIndex)
    {
        _constGlobalInits ??= new();
        _constGlobalInits.Add((slot, constIndex));
    }

    // ==================================================================
    // Stdlib Manifest
    // ==================================================================

    /// <summary>Set the stdlib manifest for the compiled chunk.</summary>
    public ChunkBuilder SetStdlibManifest(StdlibManifest manifest)
    {
        _stdlibManifest = manifest;
        return this;
    }

    // ==================================================================
    // Source Mapping
    // ==================================================================

    /// <summary>Record a source mapping for the next instruction to be emitted.</summary>
    public void AddSourceMapping(SourceSpan span)
    {
        _sourceEntries.Add(new SourceMapEntry(_code.Count, span));
    }

    // ==================================================================
    // Build
    // ==================================================================

    /// <summary>When true (default), the peephole optimizer runs during Build.</summary>
    public bool EnablePeephole { get; set; } = true;

    /// <summary>When true (default), the trivial dead-code elimination pass runs during Build.</summary>
    public bool EnableDce { get; set; } = true;

    /// <summary>
    /// Freeze the builder into an immutable <see cref="Chunk"/>.
    /// </summary>
    public Chunk Build()
    {
        if (EnablePeephole) Peephole();
        if (EnableDce) DeadCodeEliminate();
        if (EnablePeephole) Peephole();

        string[]? globalNameTable = _globalSlots?.BuildNameTable();
        int globalSlotCount = _globalSlots?.Count ?? 0;
        ICSlot[]? icSlots = _icSlotCount > 0 ? new ICSlot[_icSlotCount] : null;
        if (icSlots is not null)
        {
            for (int i = 0; i < icSlots.Length && i < _icConstantIndices.Count; i++)
                icSlots[i].ConstantIndex = _icConstantIndices[i];
        }

        var chunk = new Chunk(
            code: _code.ToArray(),
            constants: _constants.ToArray(),
            sourceMap: new SourceMap(_sourceEntries.ToArray()),
            arity: Arity,
            minArity: MinArity,
            maxRegs: MaxRegs,
            upvalues: _upvalues.ToArray(),
            name: Name,
            isAsync: IsAsync,
            hasRestParam: HasRestParam,
            mayHaveCapturedLocals: MayHaveCapturedLocals,
            localNames: LocalNames,
            localIsConst: LocalIsConst,
            upvalueNames: UpvalueNames,
            globalNameTable: globalNameTable,
            globalSlotCount: globalSlotCount,
            icSlots: icSlots,
            constGlobalInits: _constGlobalInits?.ToArray());

        chunk.StdlibManifest = _stdlibManifest;
        return chunk;
    }

    // ==================================================================
    // Peephole Optimizer
    // ==================================================================

    private void Peephole()
    {
        if (_code.Count < 2) return;

        // Build the set of jump targets and companion word positions so we don't
        // optimize across basic block boundaries or treat companion words as instructions.
        var jumpTargets = new HashSet<int>();
        var companionWords = new HashSet<int>();
        for (int i = 0; i < _code.Count; i++)
        {
            uint inst = _code[i];
            OpCode op = Instruction.GetOp(inst);
            switch (op)
            {
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
                    int target = i + 1 + Instruction.GetSBx(inst);
                    jumpTargets.Add(target);
                    break;
                }
                case OpCode.GetFieldIC:
                case OpCode.CallBuiltIn:
                    i++; // skip companion word
                    companionWords.Add(i); // track its position
                    break;
            }
        }

        var removals = new List<int>();

        for (int i = 0; i < _code.Count - 1; i++)
        {
            if (jumpTargets.Contains(i)) continue;
            if (jumpTargets.Contains(i + 1)) continue;
            // Never treat a companion word as an instruction or optimize it away.
            if (companionWords.Contains(i)) continue;
            if (companionWords.Contains(i + 1)) continue;

            uint inst0 = _code[i];
            if (Instruction.GetOp(inst0) != OpCode.Move) continue;

            byte moveA = Instruction.GetA(inst0);  // destination
            byte moveB = Instruction.GetB(inst0);  // source

            // Pattern 11: Move(A, A) — self-move is a no-op, drop it.
            if (moveA == moveB)
            {
                removals.Add(i);
                continue;
            }

            uint inst1 = _code[i + 1];
            OpCode op1 = Instruction.GetOp(inst1);

            // Pattern 1: Move(A,B) + JmpFalse/JmpTrue(A, offset) → JmpFalse/JmpTrue(B, offset)
            if ((op1 == OpCode.JmpFalse || op1 == OpCode.JmpTrue) && Instruction.GetA(inst1) == moveA)
            {
                _code[i + 1] = Instruction.EncodeAsBx(op1, moveB, Instruction.GetSBx(inst1));
                removals.Add(i);
                continue;
            }

            // Pattern 2: Move(A,B) + Return(A, C, 0) → Return(B, C, 0)
            if (op1 == OpCode.Return && Instruction.GetA(inst1) == moveA)
            {
                _code[i + 1] = Instruction.EncodeABC(OpCode.Return, moveB, Instruction.GetB(inst1), Instruction.GetC(inst1));
                removals.Add(i);
                continue;
            }

            // Pattern 5: Move(A,B) + SetGlobal(A, slotBx) → SetGlobal(B, slotBx)
            if (op1 == OpCode.SetGlobal && Instruction.GetA(inst1) == moveA)
            {
                _code[i + 1] = Instruction.EncodeABx(OpCode.SetGlobal, moveB, Instruction.GetBx(inst1));
                removals.Add(i);
                continue;
            }

            // Pattern 6: Move(A,B) + InitConstGlobal(A, slotBx) → InitConstGlobal(B, slotBx)
            if (op1 == OpCode.InitConstGlobal && Instruction.GetA(inst1) == moveA)
            {
                _code[i + 1] = Instruction.EncodeABx(OpCode.InitConstGlobal, moveB, Instruction.GetBx(inst1));
                removals.Add(i);
                continue;
            }

            // Pattern 7a: Move(A,B) + SetTable(A, K, V) → SetTable(B, K, V) (table register)
            // Guard: K and V must not also be moveA, or removing the Move would break them.
            if (op1 == OpCode.SetTable && Instruction.GetA(inst1) == moveA
                && Instruction.GetB(inst1) != moveA && Instruction.GetC(inst1) != moveA)
            {
                _code[i + 1] = Instruction.EncodeABC(OpCode.SetTable, moveB, Instruction.GetB(inst1), Instruction.GetC(inst1));
                removals.Add(i);
                continue;
            }

            // Pattern 7b: Move(A,B) + SetTable(T, K, A) → SetTable(T, K, B) (value register)
            if (op1 == OpCode.SetTable && Instruction.GetC(inst1) == moveA
                && Instruction.GetA(inst1) != moveA && Instruction.GetB(inst1) != moveA)
            {
                _code[i + 1] = Instruction.EncodeABC(OpCode.SetTable, Instruction.GetA(inst1), Instruction.GetB(inst1), moveB);
                removals.Add(i);
                continue;
            }

            // Pattern 8a: Move(A,B) + GetTable(X, A, K) → GetTable(X, B, K) (table register)
            if (op1 == OpCode.GetTable && Instruction.GetB(inst1) == moveA
                && Instruction.GetC(inst1) != moveA)
            {
                _code[i + 1] = Instruction.EncodeABC(OpCode.GetTable, Instruction.GetA(inst1), moveB, Instruction.GetC(inst1));
                removals.Add(i);
                continue;
            }

            // Pattern 8b: Move(A,B) + GetTable(X, T, A) → GetTable(X, T, B) (key register)
            if (op1 == OpCode.GetTable && Instruction.GetC(inst1) == moveA
                && Instruction.GetB(inst1) != moveA)
            {
                _code[i + 1] = Instruction.EncodeABC(OpCode.GetTable, Instruction.GetA(inst1), Instruction.GetB(inst1), moveB);
                removals.Add(i);
                continue;
            }

            // Pattern 9a: Move(A,B) + GetField(X, A, K) → GetField(X, B, K)
            // Also handles GetFieldIC: companion word at i+2 is left untouched by the rewrite
            // (it is in companionWords and will be kept adjacent to the patched instruction by
            // ApplyRemovals, which skips companion words during compaction).
            if ((op1 == OpCode.GetField || op1 == OpCode.GetFieldIC) && Instruction.GetB(inst1) == moveA)
            {
                _code[i + 1] = Instruction.EncodeABC(op1, Instruction.GetA(inst1), moveB, Instruction.GetC(inst1));
                removals.Add(i);
                continue;
            }

            // Pattern 9b: Move(A,B) + SetField(A, K, V) → SetField(B, K, V) (object register)
            // Guard: value register C must not also be moveA.
            if (op1 == OpCode.SetField && Instruction.GetA(inst1) == moveA
                && Instruction.GetC(inst1) != moveA)
            {
                _code[i + 1] = Instruction.EncodeABC(OpCode.SetField, moveB, Instruction.GetB(inst1), Instruction.GetC(inst1));
                removals.Add(i);
                continue;
            }

            // Pattern 9c: Move(A,B) + SetField(T, K, A) → SetField(T, K, B) (value register)
            // Guard: object register A must not also be moveA.
            if (op1 == OpCode.SetField && Instruction.GetC(inst1) == moveA
                && Instruction.GetA(inst1) != moveA)
            {
                _code[i + 1] = Instruction.EncodeABC(OpCode.SetField, Instruction.GetA(inst1), Instruction.GetB(inst1), moveB);
                removals.Add(i);
                continue;
            }

            // Pattern 9d: Move(A,B) + Self(X, A, K) → Self(X, B, K) (object register)
            // Guard: X+1 != B — Self writes two consecutive registers R(X) and R(X+1);
            // if R(X+1) == R(B=moveB), removing the Move and substituting moveB would cause
            // Self to overwrite the register it is reading as its object source.
            if (op1 == OpCode.Self && Instruction.GetB(inst1) == moveA
                && (Instruction.GetA(inst1) + 1) != moveB)
            {
                _code[i + 1] = Instruction.EncodeABC(OpCode.Self, Instruction.GetA(inst1), moveB, Instruction.GetC(inst1));
                removals.Add(i);
                continue;
            }

            // Patterns 3 & 4: Move + Move + GetTable/SetTable
            if (i + 2 < _code.Count && op1 == OpCode.Move && !jumpTargets.Contains(i + 2) && !companionWords.Contains(i + 2))
            {
                byte move2A = Instruction.GetA(inst1);
                byte move2B = Instruction.GetB(inst1);
                uint inst2 = _code[i + 2];
                OpCode op2 = Instruction.GetOp(inst2);

                // Pattern 3: Move(A,B) + Move(C,D) + GetTable(X, A, C) → GetTable(X, B, D)
                if (op2 == OpCode.GetTable && Instruction.GetB(inst2) == moveA && Instruction.GetC(inst2) == move2A)
                {
                    _code[i + 2] = Instruction.EncodeABC(OpCode.GetTable, Instruction.GetA(inst2), moveB, move2B);
                    removals.Add(i);
                    removals.Add(i + 1);
                    i++; // skip the second Move
                    continue;
                }

                // Pattern 4: Move(A,B) + Move(C,D) + SetTable(A, C, E) → SetTable(B, D, E)
                if (op2 == OpCode.SetTable && Instruction.GetA(inst2) == moveA && Instruction.GetB(inst2) == move2A)
                {
                    _code[i + 2] = Instruction.EncodeABC(OpCode.SetTable, moveB, move2B, Instruction.GetC(inst2));
                    removals.Add(i);
                    removals.Add(i + 1);
                    i++;
                    continue;
                }
            }
        }

        if (removals.Count == 0) return;

        ApplyRemovals(removals);
    }

    // ==================================================================
    // Dead Code Elimination
    // ==================================================================

    /// <summary>
    /// Trivial backward-scan DCE: removes pure instructions whose destination register
    /// is never read before being overwritten or the function returns.
    /// Conservative: resets liveness to all-live at every jump-target block boundary,
    /// so cross-block opportunities are not exploited.
    /// </summary>
    private void DeadCodeEliminate()
    {
        if (_code.Count < 2) return;

        // Build jump-target set and companion-word set (same as Peephole, plus PipeChain).
        var jumpTargets = new HashSet<int>();
        var companionWords = new HashSet<int>();
        // Any local register captured by a Closure is never eliminated — the closure may
        // read the captured slot at any time, including after a forward assignment that
        // DCE would otherwise remove (because it appears dead in the backward scan).
        var capturedLocals = new HashSet<byte>();
        for (int i = 0; i < _code.Count; i++)
        {
            uint inst = _code[i];
            OpCode op = Instruction.GetOp(inst);
            switch (op)
            {
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
                    int target = i + 1 + Instruction.GetSBx(inst);
                    jumpTargets.Add(target);
                    break;
                }
                case OpCode.GetFieldIC:
                case OpCode.CallBuiltIn:
                    i++;
                    companionWords.Add(i);
                    break;
                case OpCode.PipeChain:
                {
                    // B companion words follow (one per pipeline stage)
                    int stages = Instruction.GetB(inst);
                    for (int s = 0; s < stages; s++)
                    {
                        i++;
                        companionWords.Add(i);
                    }
                    break;
                }
                case OpCode.Closure:
                {
                    // N upvalue descriptor words follow, where N = Upvalues.Length of the
                    // sub-chunk constant.  Without tracking these as companion words, DCE
                    // would misinterpret them as LoadK/LoadNull instructions (their low byte
                    // is 0 or 1 = isLocal flag) and may remove them, corrupting the chunk.
                    ushort bx = Instruction.GetBx(inst);
                    if (bx < _constants.Count && _constants[bx].AsObj is Chunk subChunk)
                    {
                        int uvCount = subChunk.Upvalues.Length;
                        for (int uv = 0; uv < uvCount; uv++)
                        {
                            i++;
                            companionWords.Add(i);
                            // Track which locals are captured for any-position protection.
                            uint desc = _code[i];
                            if ((desc & 0xFF) == 1) // isLocal == 1
                                capturedLocals.Add((byte)((desc >> 8) & 0xFF));
                        }
                    }
                    break;
                }
            }
        }

        var removals = new List<int>();
        var liveRegs = new HashSet<byte>();
        bool allLive = false; // set true at every jump-target block boundary

        for (int i = _code.Count - 1; i >= 0; i--)
        {
            // Raw companion data — never an instruction.
            if (companionWords.Contains(i)) continue;

            // At a jump-target the block edge is conservative: any predecessor may have
            // left any register live, so treat all registers as live.
            if (jumpTargets.Contains(i))
            {
                liveRegs.Clear();
                allLive = true;
            }

            uint inst = _code[i];
            OpCode op = Instruction.GetOp(inst);
            int destReg = DceGetWrittenReg(op, inst);

            // Attempt elimination of a pure instruction with a dead destination.
            if (IsPureForDce(op) && destReg >= 0)
            {
                bool destIsLive = allLive || liveRegs.Contains((byte)destReg)
                                          || capturedLocals.Contains((byte)destReg);
                if (!destIsLive)
                {
                    removals.Add(i);
                    continue; // removed — liveness unchanged
                }
            }

            // Instruction survives: update liveness (only when not in all-live mode,
            // as the set is meaningless when allLive=true).
            if (!allLive)
            {
                if (destReg >= 0)
                    liveRegs.Remove((byte)destReg);

                // PipeChain: reads R(C)..R(C+totalParts-1) where totalParts is derived
                // from companion words at i+1..i+B. Handle here where _code is accessible.
                if (op == OpCode.PipeChain)
                {
                    byte pipc = Instruction.GetC(inst);
                    int stages = Instruction.GetB(inst);
                    int totalParts = 0;
                    for (int cw = i + 1; cw < i + 1 + stages && cw < _code.Count; cw++)
                        totalParts += (int)((_code[cw] >> 8) & 0xFF);
                    for (int p = 0; p < totalParts; p++)
                        liveRegs.Add((byte)(pipc + p));
                }
                else if (op == OpCode.Closure)
                {
                    // Closure reads local registers it captures: for each upvalue descriptor
                    // word where isLocal=1, the Closure captures _stack[base + index], so
                    // R(index) must be live. Read the descriptor companion words directly
                    // from _code (they are always in companionWords and never removed).
                    ushort cbx = Instruction.GetBx(inst);
                    if (cbx < _constants.Count && _constants[cbx].AsObj is Chunk closureSubChunk)
                    {
                        int uvCount = closureSubChunk.Upvalues.Length;
                        for (int uv = 0; uv < uvCount; uv++)
                        {
                            int descIdx = i + 1 + uv;
                            if (descIdx < _code.Count)
                            {
                                uint desc = _code[descIdx];
                                byte isLocal = (byte)(desc & 0xFF);
                                byte uvIndex = (byte)((desc >> 8) & 0xFF);
                                if (isLocal == 1)
                                    liveRegs.Add(uvIndex);
                            }
                        }
                    }
                    DceAddReads(op, inst, liveRegs);
                }
                else
                {
                    DceAddReads(op, inst, liveRegs);
                }
            }
        }

        if (removals.Count == 0) return;

        ApplyRemovals(removals);
    }

    /// <summary>
    /// Returns true if <paramref name="op"/> has no observable side effects and may be
    /// removed when its destination register is provably dead.
    /// </summary>
    private static bool IsPureForDce(OpCode op) => op switch
    {
        OpCode.LoadK or OpCode.LoadNull or OpCode.LoadBool or OpCode.Move
            or OpCode.Add or OpCode.Sub or OpCode.Mul or OpCode.Pow
            or OpCode.Neg or OpCode.AddI
            or OpCode.BAnd or OpCode.BOr or OpCode.BXor or OpCode.BNot
            or OpCode.Eq or OpCode.Ne or OpCode.Lt or OpCode.Le or OpCode.Gt or OpCode.Ge
            or OpCode.Not
            or OpCode.AddK or OpCode.SubK
            or OpCode.EqK or OpCode.NeK or OpCode.LtK or OpCode.LeK or OpCode.GtK or OpCode.GeK
            or OpCode.TypeOf or OpCode.Is or OpCode.In => true,
        _ => false
    };

    /// <summary>
    /// Returns the primary destination register index for <paramref name="op"/>, or -1 if the
    /// instruction writes no register.  Used for both pure and effectful instructions so that
    /// liveness of the def can be tracked going backward.
    /// </summary>
    private static int DceGetWrittenReg(OpCode op, uint instr)
    {
        if (IsPureForDce(op)) return Instruction.GetA(instr);

        return op switch
        {
            OpCode.Call or OpCode.CallSpread or OpCode.CallBuiltIn
                or OpCode.GetTable or OpCode.GetField or OpCode.GetFieldIC
                or OpCode.GetUpval
                or OpCode.GetGlobal
                or OpCode.TryExpr
                or OpCode.NewArray or OpCode.NewDict or OpCode.NewRange
                or OpCode.Closure or OpCode.NewStruct
                or OpCode.Command
                or OpCode.Import or OpCode.ImportAs
                or OpCode.StructDecl or OpCode.EnumDecl or OpCode.IfaceDecl
                or OpCode.Await
                or OpCode.TryBegin   // writes error register on catch entry
                or OpCode.Timeout
                or OpCode.Retry
                or OpCode.ElevateBegin => Instruction.GetA(instr),
            _ => -1
        };
    }

    /// <summary>
    /// Marks all register operands read by <paramref name="op"/> as live in
    /// <paramref name="liveRegs"/>.  Conservative for complex instructions.
    /// </summary>
    private void DceAddReads(OpCode op, uint instr, HashSet<byte> liveRegs)
    {
        byte a = Instruction.GetA(instr);
        byte b = Instruction.GetB(instr);
        byte c = Instruction.GetC(instr);

        switch (op)
        {
            // ── No register reads ──────────────────────────────────────────
            case OpCode.LoadK:
            case OpCode.LoadNull:
            case OpCode.LoadBool:
            case OpCode.GetGlobal:
            case OpCode.GetUpval:
            case OpCode.Jmp:
            case OpCode.Loop:
            case OpCode.TryEnd:
            case OpCode.ElevateEnd:
            case OpCode.Rethrow:
            case OpCode.LockEnd:
            case OpCode.TryBegin:   // writes R(A) on catch entry, reads nothing
            case OpCode.Closure:    // local captures tracked via descriptor words in DeadCodeEliminate()
                break;

            // ── Reads R(A) ────────────────────────────────────────────────
            case OpCode.SetGlobal:
            case OpCode.InitConstGlobal:
            case OpCode.SetUpval:
            case OpCode.CloseUpval:
            case OpCode.JmpFalse:
            case OpCode.JmpTrue:
            case OpCode.Throw:
            case OpCode.Defer:
            case OpCode.UnsetGlobal:
            case OpCode.Switch:
            case OpCode.Test:
            case OpCode.TypedWrap:
            case OpCode.CheckNumeric:
            case OpCode.CatchMatch:
                liveRegs.Add(a);
                break;

            // ── Reads R(B) ────────────────────────────────────────────────
            case OpCode.Move:
            case OpCode.Neg:
            case OpCode.Not:
            case OpCode.BNot:
            case OpCode.TypeOf:
            case OpCode.AddK:
            case OpCode.SubK:
            case OpCode.EqK:
            case OpCode.NeK:
            case OpCode.LtK:
            case OpCode.LeK:
            case OpCode.GtK:
            case OpCode.GeK:
            case OpCode.GetField:
            case OpCode.GetFieldIC:
            case OpCode.Spread:
            case OpCode.Await:
            case OpCode.TryExpr:
                liveRegs.Add(b);
                break;

            // ── Is: reads R(B)=value, R(C & 0x7F)=type (type ALWAYS in a register) ──
            case OpCode.Is:
                liveRegs.Add(b);
                liveRegs.Add((byte)(c & 0x7F));
                break;

            // ── Reads R(A) in-place (AddI: R(A) = R(A) + sBx) ───────────
            case OpCode.AddI:
                liveRegs.Add(a);
                break;

            // ── Reads R(B) and R(C) ───────────────────────────────────────
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
            case OpCode.GetTable:  // reads R(B)=table, R(C)=key
                liveRegs.Add(b);
                liveRegs.Add(c);
                break;

            // ── NewRange: reads R(B)=start, R(C)=end, R(A+1)=step ────────
            case OpCode.NewRange:
                liveRegs.Add(b);
                liveRegs.Add(c);
                liveRegs.Add((byte)(a + 1));
                break;

            // ── TestSet: reads R(B) (writes R(A) conditionally) ──────────
            case OpCode.TestSet:
                liveRegs.Add(b);
                break;

            // ── SetTable: reads R(A)=table, R(B)=key, R(C)=value ─────────
            case OpCode.SetTable:
                liveRegs.Add(a);
                liveRegs.Add(b);
                liveRegs.Add(c);
                break;

            // ── SetField: reads R(A)=object, R(C)=value ──────────────────
            case OpCode.SetField:
                liveRegs.Add(a);
                liveRegs.Add(c);
                break;

            // ── Import/ImportAs: reads R(A)=module path string ────────────
            case OpCode.Import:
            case OpCode.ImportAs:
                liveRegs.Add(a);
                break;

            // ── Self: reads R(B)=object ───────────────────────────────────
            case OpCode.Self:
                liveRegs.Add(b);
                break;

            // ── Return: reads R(A) when B != 0 ───────────────────────────
            case OpCode.Return:
                if (b != 0) liveRegs.Add(a);
                break;

            // ── Call: reads R(A)=callee and R(A+1)..R(A+C)=args ──────────
            case OpCode.Call:
                for (int j = 0; j <= c; j++) liveRegs.Add((byte)(a + j));
                break;

            // ── CallSpread: reads R(A)..R(A+B) conservatively ────────────
            case OpCode.CallSpread:
                for (int j = 0; j <= b; j++) liveRegs.Add((byte)(a + j));
                break;

            // ── CallBuiltIn: companion word describes the call; reads R(A+1)..R(A+C) ──
            case OpCode.CallBuiltIn:
                liveRegs.Add(b); // namespace object
                for (int j = 1; j <= c; j++) liveRegs.Add((byte)(a + j));
                break;

            // ── NewArray: reads R(A+1)..R(A+B) ───────────────────────────
            case OpCode.NewArray:
                for (int j = 1; j <= b; j++) liveRegs.Add((byte)(a + j));
                break;

            // ── NewDict: reads R(A+1)..R(A+2*B) (B key-value pairs) ──────
            case OpCode.NewDict:
                for (int j = 1; j <= 2 * b; j++) liveRegs.Add((byte)(a + j));
                break;

            // ── NewStruct: reads R(A+1)..R(A+C) normally, but R(A+1)..R(A+C+1)
            //    when HasTypeReg=true (type ref at R(A+1), fields at R(A+2)..R(A+1+C)).
            //    Conservative: always mark R(A+1)..R(A+C+1) to handle both cases.
            case OpCode.NewStruct:
                for (int j = 1; j <= c + 1; j++) liveRegs.Add((byte)(a + j));
                break;

            // ── Interpolate: reads R(A+1)..R(A+B) ────────────────────────
            case OpCode.Interpolate:
                for (int j = 1; j <= b; j++) liveRegs.Add((byte)(a + j));
                break;

            // ── Command: reads R(A+1)..R(A+B) ────────────────────────────
            case OpCode.Command:
                for (int j = 1; j <= b; j++) liveRegs.Add((byte)(a + j));
                break;

            // ── ForPrep/ForPrepII: reads R(A), R(A+1), R(A+2) ───────────
            case OpCode.ForPrep:
            case OpCode.ForPrepII:
                liveRegs.Add(a);
                liveRegs.Add((byte)(a + 1));
                liveRegs.Add((byte)(a + 2));
                break;

            // ── ForLoop/ForLoopII: reads R(A), R(A+1), R(A+2) ───────────
            case OpCode.ForLoop:
            case OpCode.ForLoopII:
                liveRegs.Add(a);
                liveRegs.Add((byte)(a + 1));
                liveRegs.Add((byte)(a + 2));
                break;

            // ── IterPrep: reads R(A) ──────────────────────────────────────
            case OpCode.IterPrep:
                liveRegs.Add(a);
                break;

            // ── IterLoop: reads R(A)..R(A+2) ─────────────────────────────
            case OpCode.IterLoop:
                liveRegs.Add(a);
                liveRegs.Add((byte)(a + 1));
                liveRegs.Add((byte)(a + 2));
                break;

            // ── LockBegin: reads R(B), R(B+1), R(B+2) ───────────────────
            case OpCode.LockBegin:
                liveRegs.Add(b);
                liveRegs.Add((byte)(b + 1));
                liveRegs.Add((byte)(b + 2));
                break;

            // ── ElevateBegin: reads R(A), R(B) ───────────────────────────
            case OpCode.ElevateBegin:
                liveRegs.Add(a);
                liveRegs.Add(b);
                break;

            // ── Timeout: reads R(A), R(A+1) ──────────────────────────────
            case OpCode.Timeout:
                liveRegs.Add(a);
                liveRegs.Add((byte)(a + 1));
                break;

            // ── Destructure: reads R(A) ───────────────────────────────────
            case OpCode.Destructure:
                liveRegs.Add(a);
                break;

            // ── Retry: reads R(A)=maxAttempts + consecutive regs for options/body/until/onRetry ─
            case OpCode.Retry:
            {
                liveRegs.Add(a);
                ushort retryBx = Instruction.GetBx(instr);
                if (retryBx < _constants.Count && _constants[retryBx].AsObj is RetryMetadata retryMeta)
                {
                    int next = a + 1;
                    if (retryMeta.OptionCount == -1)
                        liveRegs.Add((byte)(next++));
                    else if (retryMeta.OptionCount > 0)
                    {
                        for (int j = 0; j < retryMeta.OptionCount * 2; j++)
                            liveRegs.Add((byte)(next + j));
                        next += retryMeta.OptionCount * 2;
                    }
                    liveRegs.Add((byte)(next++)); // body
                    if (retryMeta.HasUntilClause) liveRegs.Add((byte)(next++));
                    if (retryMeta.HasOnRetryClause) liveRegs.Add((byte)(next));
                }
                break;
            }

            // ── Redirect: reads R(A), R(C) ───────────────────────────────
            case OpCode.Redirect:
                liveRegs.Add(a);
                liveRegs.Add(c);
                break;

            // ── PipeChain: reads R(C)..R(C+totalParts-1) where totalParts = sum of
            //    companion-word partCounts. Companion words live at instrIdx+1..instrIdx+B
            //    in _code (not removed yet when DeadCodeEliminate calls DceAddReads).
            //    Because DceAddReads is static and caller has no index here, PipeChain reads
            //    are handled directly in DeadCodeEliminate(); this branch is a safe fallback.
            case OpCode.PipeChain:
                liveRegs.Add(c); // partsBase — at minimum
                break;

            // ── StructDecl/EnumDecl/IfaceDecl/Extend: conservative ────────
            case OpCode.StructDecl:
            case OpCode.EnumDecl:
            case OpCode.IfaceDecl:
            case OpCode.Extend:
                liveRegs.Add(a);
                break;

            // ── Unknown/future: conservative — mark A, B, C ──────────────
            default:
                liveRegs.Add(a);
                liveRegs.Add(b);
                liveRegs.Add(c);
                break;
        }
    }

    private void ApplyRemovals(List<int> removals)
    {
        var removeSet = new HashSet<int>(removals);

        // Build mapping: old index → new index
        int[] indexMap = new int[_code.Count];
        int newIdx = 0;
        for (int oldIdx = 0; oldIdx < _code.Count; oldIdx++)
        {
            indexMap[oldIdx] = newIdx;
            if (!removeSet.Contains(oldIdx))
                newIdx++;
        }
        int newCount = newIdx;

        // Patch all jump offsets before compacting (while indices are still original)
        for (int oldIdx = 0; oldIdx < _code.Count; oldIdx++)
        {
            if (removeSet.Contains(oldIdx)) continue;

            uint inst = _code[oldIdx];
            OpCode op = Instruction.GetOp(inst);

            bool isJump = op == OpCode.Jmp || op == OpCode.JmpFalse || op == OpCode.JmpTrue
                       || op == OpCode.Loop || op == OpCode.ForPrep || op == OpCode.ForLoop
                       || op == OpCode.ForPrepII || op == OpCode.ForLoopII
                       || op == OpCode.IterLoop || op == OpCode.TryBegin;

            if (isJump)
            {
                int oldSBx = Instruction.GetSBx(inst);
                int oldTarget = oldIdx + 1 + oldSBx;
                int newTarget = (oldTarget >= 0 && oldTarget < indexMap.Length) ? indexMap[oldTarget] : newCount;
                int newSBx = newTarget - indexMap[oldIdx] - 1;
                _code[oldIdx] = Instruction.PatchSBx(inst, newSBx);
            }

            // Skip companion words — they are raw data, not opcodes
            if (op == OpCode.GetFieldIC || op == OpCode.CallBuiltIn)
                oldIdx++;
        }

        // Compact _code in-place
        newIdx = 0;
        for (int oldIdx = 0; oldIdx < _code.Count; oldIdx++)
        {
            if (!removeSet.Contains(oldIdx))
                _code[newIdx++] = _code[oldIdx];
        }
        _code.RemoveRange(newIdx, _code.Count - newIdx);

        // Patch source map entries
        for (int i = 0; i < _sourceEntries.Count; i++)
        {
            SourceMapEntry entry = _sourceEntries[i];
            int oldOffset = entry.BytecodeOffset;
            int newOffset = (oldOffset < indexMap.Length) ? indexMap[oldOffset] : oldOffset;
            _sourceEntries[i] = new SourceMapEntry(newOffset, entry.Span);
        }
    }

    // ==================================================================
    // Private helpers
    // ==================================================================

    private sealed class StashValueComparer : IEqualityComparer<StashValue>
    {
        public static readonly StashValueComparer Instance = new();

        public bool Equals(StashValue x, StashValue y)
        {
            if (x.Tag != y.Tag) return false;
            return x.Tag switch
            {
                StashValueTag.Null => true,
                StashValueTag.Bool => x.AsBool == y.AsBool,
                StashValueTag.Int => x.AsInt == y.AsInt,
                StashValueTag.Float => x.AsFloat == y.AsFloat,
                StashValueTag.Obj => object.Equals(x.AsObj, y.AsObj),
                _ => false,
            };
        }

        public int GetHashCode(StashValue v) => v.Tag switch
        {
            StashValueTag.Null => 0,
            StashValueTag.Bool => v.AsBool ? 1 : 0,
            StashValueTag.Int => v.AsInt.GetHashCode(),
            StashValueTag.Float => v.AsFloat.GetHashCode(),
            StashValueTag.Obj => v.AsObj?.GetHashCode() ?? 0,
            _ => 0,
        };
    }
}
