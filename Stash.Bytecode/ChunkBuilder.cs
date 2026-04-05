using System;
using System.Collections.Generic;
using Stash.Common;

namespace Stash.Bytecode;

/// <summary>
/// Incrementally builds bytecode instructions, constants, and source mappings,
/// then freezes the result into an immutable <see cref="Chunk"/>.
/// </summary>
public class ChunkBuilder
{
    private readonly List<byte> _code = new();
    private readonly List<StashValue> _constants = new();
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

    /// <summary>Current bytecode offset (next byte to be written).</summary>
    public int CurrentOffset => _code.Count;

    // ---- Emit Methods ----

    /// <summary>Emit a single opcode with no operands.</summary>
    public void Emit(OpCode opCode)
    {
        _code.Add((byte)opCode);
    }

    /// <summary>Emit an opcode followed by a single u8 operand.</summary>
    public void Emit(OpCode opCode, byte operand)
    {
        _code.Add((byte)opCode);
        _code.Add(operand);
    }

    /// <summary>Emit an opcode followed by a u16 operand (big-endian).</summary>
    public void Emit(OpCode opCode, ushort operand)
    {
        _code.Add((byte)opCode);
        _code.Add((byte)(operand >> 8));
        _code.Add((byte)(operand & 0xFF));
    }

    /// <summary>Emit a raw byte (for inline upvalue descriptors after OP_CLOSURE).</summary>
    public void EmitByte(byte value)
    {
        _code.Add(value);
    }

    // ---- Jump Patching ----

    /// <summary>
    /// Emit a jump instruction with a placeholder 2-byte offset.
    /// Returns the offset of the first operand byte for later patching via <see cref="PatchJump"/>.
    /// </summary>
    public int EmitJump(OpCode opCode)
    {
        _code.Add((byte)opCode);
        int patchOffset = _code.Count;
        _code.Add(0xFF); // placeholder high byte
        _code.Add(0xFF); // placeholder low byte
        return patchOffset;
    }

    /// <summary>
    /// Patches a previously emitted jump instruction to jump to the current offset.
    /// The jump offset is relative to the instruction immediately after the jump's operand.
    /// </summary>
    public void PatchJump(int patchOffset)
    {
        // The jump target is _code.Count (where we are now).
        // The offset is relative to the position right after the 2-byte operand.
        int jumpTarget = _code.Count;
        int jumpFrom = patchOffset + 2; // after the 2-byte operand
        int offset = jumpTarget - jumpFrom;

        if (offset > short.MaxValue || offset < short.MinValue)
            throw new InvalidOperationException($"Jump offset {offset} exceeds 16-bit signed range.");

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
        _code.Add((byte)OpCode.Loop);
        // The offset is from the position right after this instruction's operand
        // back to loopStart.
        int offset = (_code.Count + 2) - loopStart;
        if (offset > ushort.MaxValue)
            throw new InvalidOperationException($"Loop body too large: {offset} bytes.");

        _code.Add((byte)(offset >> 8));
        _code.Add((byte)(offset & 0xFF));
    }

    // ---- Constant Pool ----

    /// <summary>
    /// Add a StashValue constant to the pool and return its index.
    /// Deduplicates by tag and value.
    /// </summary>
    public ushort AddConstant(StashValue value)
    {
        // Deduplicate primitives and strings
        if (value.Tag != StashValueTag.Obj || value.AsObj is string)
        {
            for (int i = 0; i < _constants.Count; i++)
            {
                StashValue existing = _constants[i];
                if (existing.Tag == value.Tag)
                {
                    bool match = value.Tag switch
                    {
                        StashValueTag.Null => true,
                        StashValueTag.Bool => existing.AsBool == value.AsBool,
                        StashValueTag.Int => existing.AsInt == value.AsInt,
                        StashValueTag.Float => existing.AsFloat == value.AsFloat,
                        StashValueTag.Obj => object.Equals(existing.AsObj, value.AsObj),
                        _ => false,
                    };
                    if (match) return (ushort)i;
                }
            }
        }

        if (_constants.Count > ushort.MaxValue)
            throw new InvalidOperationException("Constant pool overflow (max 65536 entries).");

        int index = _constants.Count;
        _constants.Add(value);
        return (ushort)index;
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
        _sourceMapEntries.Add(new SourceMapEntry(_code.Count, span));
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
                return (byte)i;
        }

        if (_upvalues.Count > byte.MaxValue)
            throw new InvalidOperationException("Upvalue overflow (max 256 per function).");

        int result = _upvalues.Count;
        _upvalues.Add(new UpvalueDescriptor(index, isLocal));
        return (byte)result;
    }

    // ---- Build ----

    /// <summary>
    /// Freeze the builder state into an immutable <see cref="Chunk"/>.
    /// The builder should not be used after calling this method.
    /// </summary>
    public Chunk Build()
    {
        return new Chunk(
            code: _code.ToArray(),
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
            upvalueNames: UpvalueNames
        );
    }
}
