using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stash.Common;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Incrementally builds bytecode instructions, constants, and source mappings,
/// then freezes the result into an immutable <see cref="Chunk"/>.
/// </summary>
public class ChunkBuilder
{
    private byte[] _code = ArrayPool<byte>.Shared.Rent(256);
    private int _codeCount;
    private readonly List<StashValue> _constants = new();
    private readonly Dictionary<StashValue, ushort> _constantIndex = new(StashValueComparer.Instance);
    private readonly List<SourceMapEntry> _sourceMapEntries = new();
    private readonly List<UpvalueDescriptor> _upvalues = new();

    // Properties set by the compiler
    public string? Name { get; set; }
    public int Arity { get; set; }
    public int MinArity { get; set; }
    public int LocalCount { get; set; }
    public bool IsAsync { get; set; }
    public bool HasRestParam { get; set; }
    public string[]? LocalNames { get; set; }
    public bool[]? LocalIsConst { get; set; }
    public string[]? UpvalueNames { get; set; }

    /// <summary>When true, the peephole optimizer runs during Build() to fuse instructions.</summary>
    public bool Optimize { get; set; } = true;

    /// <summary>Shared global slot allocator for slot-based global access. Null for legacy compilation.</summary>
    internal GlobalSlotAllocator? GlobalSlotAllocator { get; set; }

    /// <summary>Current bytecode offset (next byte to be written).</summary>
    public int CurrentOffset => _codeCount;

    // ---- Emit Methods ----

    /// <summary>Emit a single opcode with no operands.</summary>
    public void Emit(OpCode opCode)
    {
        EnsureCodeCapacity(1);
        _code[_codeCount++] = (byte)opCode;
    }

    /// <summary>Emit an opcode followed by a single u8 operand.</summary>
    public void Emit(OpCode opCode, byte operand)
    {
        EnsureCodeCapacity(2);
        _code[_codeCount++] = (byte)opCode;
        _code[_codeCount++] = operand;
    }

    /// <summary>Emit an opcode followed by a u16 operand (big-endian).</summary>
    public void Emit(OpCode opCode, ushort operand)
    {
        EnsureCodeCapacity(3);
        _code[_codeCount++] = (byte)opCode;
        _code[_codeCount++] = (byte)(operand >> 8);
        _code[_codeCount++] = (byte)(operand & 0xFF);
    }

    /// <summary>Emit a raw byte (for inline upvalue descriptors after OP_CLOSURE).</summary>
    public void EmitByte(byte value)
    {
        EnsureCodeCapacity(1);
        _code[_codeCount++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCodeCapacity(int needed)
    {
        if (_codeCount + needed > _code.Length)
            GrowCode();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowCode()
    {
        byte[] newBuf = ArrayPool<byte>.Shared.Rent(_code.Length * 2);
        _code.AsSpan(0, _codeCount).CopyTo(newBuf);
        ArrayPool<byte>.Shared.Return(_code);
        _code = newBuf;
    }

    // ---- Jump Patching ----

    /// <summary>
    /// Emit a jump instruction with a placeholder 2-byte offset.
    /// Returns the offset of the first operand byte for later patching via <see cref="PatchJump"/>.
    /// </summary>
    public int EmitJump(OpCode opCode)
    {
        EnsureCodeCapacity(3);
        _code[_codeCount++] = (byte)opCode;
        int patchOffset = _codeCount;
        _code[_codeCount++] = 0xFF; // placeholder high byte
        _code[_codeCount++] = 0xFF; // placeholder low byte
        return patchOffset;
    }

    /// <summary>
    /// Patches a previously emitted jump instruction to jump to the current offset.
    /// The jump offset is relative to the instruction immediately after the jump's operand.
    /// </summary>
    public void PatchJump(int patchOffset)
    {
        // The jump target is _codeCount (where we are now).
        // The offset is relative to the position right after the 2-byte operand.
        int jumpTarget = _codeCount;
        int jumpFrom = patchOffset + 2; // after the 2-byte operand
        int offset = jumpTarget - jumpFrom;

        if (offset > short.MaxValue || offset < short.MinValue)
        {
            throw new InvalidOperationException($"Jump offset {offset} exceeds 16-bit signed range.");
        }

        short signedOffset = (short)offset;
        ushort encoded = (ushort)signedOffset;
        _code[patchOffset] = (byte)(encoded >> 8);
        _code[patchOffset + 1] = (byte)(encoded & 0xFF);
    }

    /// <summary>
    /// Emits a backwards loop jump to the given target offset.
    /// The operand is the unsigned distance to jump back.
    /// </summary>
    public void EmitLoop(int loopStart)
    {
        EnsureCodeCapacity(3);
        _code[_codeCount++] = (byte)OpCode.Loop;
        // The offset is from the position right after this instruction's operand
        // back to loopStart.
        int offset = (_codeCount + 2) - loopStart;
        if (offset > ushort.MaxValue)
        {
            throw new InvalidOperationException($"Loop body too large: {offset} bytes.");
        }

        _code[_codeCount++] = (byte)(offset >> 8);
        _code[_codeCount++] = (byte)(offset & 0xFF);
    }

    // ---- Constant Pool ----

    /// <summary>
    /// Add a StashValue constant to the pool and return its index.
    /// Deduplicates by tag and value.
    /// </summary>
    public ushort AddConstant(StashValue value)
    {
        // Deduplicate primitives and strings via dictionary lookup (O(1) vs O(n))
        if (value.Tag != StashValueTag.Obj || value.AsObj is string)
        {
            if (_constantIndex.TryGetValue(value, out ushort existing))
                return existing;
        }

        if (_constants.Count > ushort.MaxValue)
        {
            throw new InvalidOperationException("Constant pool overflow (max 65536 entries).");
        }

        ushort index = (ushort)_constants.Count;
        _constants.Add(value);

        if (value.Tag != StashValueTag.Obj || value.AsObj is string)
            _constantIndex[value] = index;

        return index;
    }

    /// <summary>Add a long integer constant.</summary>
    public ushort AddConstant(long value) => AddConstant(StashValue.FromInt(value));

    /// <summary>Add a double constant.</summary>
    public ushort AddConstant(double value) => AddConstant(StashValue.FromFloat(value));

    /// <summary>Add a string constant.</summary>
    public ushort AddConstant(string value) => AddConstant(StashValue.FromObj(value));

    /// <summary>Add a compiled chunk (function body) constant.</summary>
    public ushort AddConstant(Chunk value) => AddConstant(StashValue.FromObj(value));

    /// <summary>Add an arbitrary object constant (for metadata, etc.).</summary>
    public ushort AddConstant(object value) => AddConstant(StashValue.FromObject(value));

    // ---- Source Map ----

    /// <summary>Record a source mapping from the current bytecode offset to a source span.</summary>
    public void AddSourceMapping(SourceSpan span)
    {
        _sourceMapEntries.Add(new SourceMapEntry(_codeCount, span));
    }

    /// <summary>Record a source mapping at a specific bytecode offset.</summary>
    public void AddSourceMapping(int offset, SourceSpan span)
    {
        _sourceMapEntries.Add(new SourceMapEntry(offset, span));
    }

    // ---- Upvalues ----

    /// <summary>Add an upvalue descriptor and return its index.</summary>
    public byte AddUpvalue(byte index, bool isLocal)
    {
        // Check for existing upvalue with same descriptor
        for (int i = 0; i < _upvalues.Count; i++)
        {
            UpvalueDescriptor existing = _upvalues[i];
            if (existing.Index == index && existing.IsLocal == isLocal)
            {
                return (byte)i;
            }
        }

        if (_upvalues.Count > byte.MaxValue)
        {
            throw new InvalidOperationException("Upvalue overflow (max 256 per function).");
        }

        int result = _upvalues.Count;
        _upvalues.Add(new UpvalueDescriptor(index, isLocal));
        return (byte)result;
    }

    // ---- Build ----

    /// <summary>
    /// Runs the peephole optimizer on the current bytecode, replacing multi-instruction
    /// sequences with fused superinstructions and specializing common opcodes.
    /// </summary>
    internal void RunPeepholeOptimizer()
    {
        PeepholeOptimizer.Optimize(_code, ref _codeCount, _constants, _sourceMapEntries);
    }

    /// <summary>
    /// Freeze the builder state into an immutable <see cref="Chunk"/>.
    /// The builder should not be used after calling this method.
    /// </summary>
    public Chunk Build()
    {
        if (Optimize)
            RunPeepholeOptimizer();

        byte[] code = _code.AsSpan(0, _codeCount).ToArray();
        ArrayPool<byte>.Shared.Return(_code);
        _code = null!; // prevent reuse after Build

        string[]? globalNameTable = GlobalSlotAllocator?.BuildNameTable();
        int globalSlotCount = GlobalSlotAllocator?.Count ?? 0;

        return new Chunk(
            code: code,
            constants: _constants.ToArray(),
            sourceMap: new SourceMap(_sourceMapEntries.ToArray()),
            arity: Arity,
            minArity: MinArity,
            localCount: LocalCount,
            upvalues: _upvalues.ToArray(),
            name: Name,
            isAsync: IsAsync,
            hasRestParam: HasRestParam,
            localNames: LocalNames,
            localIsConst: LocalIsConst,
            upvalueNames: UpvalueNames,
            globalNameTable: globalNameTable,
            globalSlotCount: globalSlotCount
        );
    }

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
