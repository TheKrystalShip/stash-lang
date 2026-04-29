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

    /// <summary>
    /// Freeze the builder into an immutable <see cref="Chunk"/>.
    /// </summary>
    public Chunk Build()
    {
        Peephole();

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
