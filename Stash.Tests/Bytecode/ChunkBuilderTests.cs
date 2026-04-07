using Stash.Bytecode;
using Stash.Common;
using Stash.Runtime;

namespace Stash.Tests.Bytecode;

public class ChunkBuilderTests
{
    // ---- OpCodeInfo Tests ----

    [Theory]
    [InlineData(OpCode.Null, 0)]
    [InlineData(OpCode.Add, 0)]
    [InlineData(OpCode.Return, 0)]
    [InlineData(OpCode.LoadLocal, 1)]
    [InlineData(OpCode.Call, 1)]
    [InlineData(OpCode.Const, 2)]
    [InlineData(OpCode.Jump, 2)]
    [InlineData(OpCode.Closure, 2)]
    public void OpCodeInfo_OperandSize_ReturnsExpectedSize(OpCode opCode, int expected)
    {
        Assert.Equal(expected, OpCodeInfo.OperandSize(opCode));
    }

    [Fact]
    public void OpCodeInfo_AllOpcodes_HaveDefinedSize()
    {
        // Verify every defined opcode has a valid operand size (doesn't throw)
        foreach (OpCode op in Enum.GetValues<OpCode>())
        {
            int size = OpCodeInfo.OperandSize(op);
            Assert.InRange(size, 0, 2);
        }
    }

    // ---- Emit Tests ----

    [Fact]
    public void Emit_NoOperand_EmitsSingleByte()
    {
        var builder = new ChunkBuilder();
        builder.Emit(OpCode.Add);
        Chunk chunk = builder.Build();

        Assert.Single(chunk.Code);
        Assert.Equal((byte)OpCode.Add, chunk.Code[0]);
    }

    [Fact]
    public void Emit_ByteOperand_EmitsTwoBytes()
    {
        var builder = new ChunkBuilder();
        builder.Emit(OpCode.LoadLocal, 5);
        Chunk chunk = builder.Build();

        Assert.Equal(2, chunk.Code.Length);
        Assert.Equal((byte)OpCode.LoadLocal, chunk.Code[0]);
        Assert.Equal(5, chunk.Code[1]);
    }

    [Fact]
    public void Emit_UShortOperand_EmitsThreeBytesBigEndian()
    {
        var builder = new ChunkBuilder();
        builder.Emit(OpCode.Const, (ushort)0x0102);
        Chunk chunk = builder.Build();

        Assert.Equal(3, chunk.Code.Length);
        Assert.Equal((byte)OpCode.Const, chunk.Code[0]);
        Assert.Equal(0x01, chunk.Code[1]); // high byte
        Assert.Equal(0x02, chunk.Code[2]); // low byte
    }

    [Fact]
    public void Emit_UShortOperand_Zero_EmitsCorrectly()
    {
        var builder = new ChunkBuilder();
        builder.Emit(OpCode.Const, (ushort)0);
        Chunk chunk = builder.Build();

        Assert.Equal(0x00, chunk.Code[1]);
        Assert.Equal(0x00, chunk.Code[2]);
    }

    [Fact]
    public void EmitByte_EmitsRawByte()
    {
        var builder = new ChunkBuilder();
        builder.EmitByte(0xAB);
        Chunk chunk = builder.Build();

        Assert.Single(chunk.Code);
        Assert.Equal(0xAB, chunk.Code[0]);
    }

    [Fact]
    public void CurrentOffset_TracksEmittedBytes()
    {
        var builder = new ChunkBuilder();
        Assert.Equal(0, builder.CurrentOffset);

        builder.Emit(OpCode.Null);     // 1 byte
        Assert.Equal(1, builder.CurrentOffset);

        builder.Emit(OpCode.LoadLocal, 3);  // 2 bytes
        Assert.Equal(3, builder.CurrentOffset);

        builder.Emit(OpCode.Const, (ushort)42);  // 3 bytes
        Assert.Equal(6, builder.CurrentOffset);
    }

    // ---- Multiple Instructions ----

    [Fact]
    public void Emit_MultipleInstructions_ProducesCorrectSequence()
    {
        var builder = new ChunkBuilder();
        ushort constIdx = builder.AddConstant(42L);    // constant pool index 0
        builder.Emit(OpCode.Const, constIdx);          // offset 0: Const 0
        builder.Emit(OpCode.StoreLocal, (byte)0);      // offset 3: StoreLocal 0
        builder.Emit(OpCode.LoadLocal, (byte)0);       // offset 5: LoadLocal 0
        builder.Emit(OpCode.Add);                      // offset 7: Add
        builder.Emit(OpCode.Return);                   // offset 8: Return

        Chunk chunk = builder.Build();
        Assert.Equal(9, chunk.Code.Length);
        Assert.Equal((byte)OpCode.Return, chunk.Code[8]);
    }

    // ---- Constant Pool Tests ----

    [Fact]
    public void AddConstant_Long_Deduplicates()
    {
        var builder = new ChunkBuilder();
        ushort idx1 = builder.AddConstant(42L);
        ushort idx2 = builder.AddConstant(42L);
        Assert.Equal(idx1, idx2);

        Chunk chunk = builder.Build();
        Assert.Single(chunk.Constants);
    }

    [Fact]
    public void AddConstant_String_Deduplicates()
    {
        var builder = new ChunkBuilder();
        ushort idx1 = builder.AddConstant("hello");
        ushort idx2 = builder.AddConstant("hello");
        Assert.Equal(idx1, idx2);

        Chunk chunk = builder.Build();
        Assert.Single(chunk.Constants);
    }

    [Fact]
    public void AddConstant_DifferentValues_GetDifferentIndices()
    {
        var builder = new ChunkBuilder();
        ushort idx1 = builder.AddConstant(1L);
        ushort idx2 = builder.AddConstant(2L);
        ushort idx3 = builder.AddConstant("hello");

        Assert.NotEqual(idx1, idx2);
        Assert.NotEqual(idx2, idx3);

        Chunk chunk = builder.Build();
        Assert.Equal(3, chunk.Constants.Length);
        Assert.Equal(1L, chunk.Constants[0].AsInt);
        Assert.Equal(2L, chunk.Constants[1].AsInt);
        Assert.Equal("hello", (string)chunk.Constants[2].AsObj!);
    }

    [Fact]
    public void AddConstant_Null_Deduplicates()
    {
        var builder = new ChunkBuilder();
        ushort idx1 = builder.AddConstant(StashValue.Null);
        ushort idx2 = builder.AddConstant(StashValue.Null);
        Assert.Equal(idx1, idx2);
    }

    [Fact]
    public void AddConstant_Bool_Deduplicates()
    {
        var builder = new ChunkBuilder();
        ushort idx1 = builder.AddConstant(StashValue.True);
        ushort idx2 = builder.AddConstant(StashValue.True);
        ushort idx3 = builder.AddConstant(StashValue.False);

        Assert.Equal(idx1, idx2);
        Assert.NotEqual(idx1, idx3);
    }

    [Fact]
    public void AddConstant_Double_Deduplicates()
    {
        var builder = new ChunkBuilder();
        ushort idx1 = builder.AddConstant(3.14);
        ushort idx2 = builder.AddConstant(3.14);
        Assert.Equal(idx1, idx2);
    }

    // ---- Jump Patching Tests ----

    [Fact]
    public void EmitJump_PatchJump_ProducesCorrectForwardOffset()
    {
        var builder = new ChunkBuilder();
        // Emit: JumpFalse (offset 0) -> placeholder -> Add (offset 3) -> Null (offset 4)
        int patch = builder.EmitJump(OpCode.JumpFalse); // 3 bytes: opcode + 2 placeholder
        builder.Emit(OpCode.Add);                        // offset 3
        builder.PatchJump(patch);                        // patch to point to offset 4

        Chunk chunk = builder.Build();
        // The jump operand should encode offset 1 (from offset 3 to offset 4)
        // Actually: target=4, jumpFrom=patch+2=3, offset=4-3=1
        ushort encoded = (ushort)((chunk.Code[patch] << 8) | chunk.Code[patch + 1]);
        short signedOffset = (short)encoded;
        Assert.Equal(1, signedOffset);  // Jump over 1 byte (the Add instruction)
    }

    [Fact]
    public void EmitJump_PatchJump_ZeroOffset()
    {
        var builder = new ChunkBuilder();
        int patch = builder.EmitJump(OpCode.Jump);  // 3 bytes
        builder.PatchJump(patch);                    // patch to point to current = offset 3

        Chunk chunk = builder.Build();
        ushort encoded = (ushort)((chunk.Code[patch] << 8) | chunk.Code[patch + 1]);
        short signedOffset = (short)encoded;
        Assert.Equal(0, signedOffset);
    }

    // ---- Loop Tests ----

    [Fact]
    public void EmitLoop_ProducesCorrectBackwardOffset()
    {
        var builder = new ChunkBuilder();
        int loopStart = builder.CurrentOffset;       // offset 0
        builder.Emit(OpCode.Add);                     // offset 0, 1 byte
        builder.Emit(OpCode.Pop);                     // offset 1, 1 byte
        builder.EmitLoop(loopStart);                  // offset 2: Loop <backward-offset>

        Chunk chunk = builder.Build();
        Assert.Equal((byte)OpCode.Loop, chunk.Code[2]);

        // EmitLoop adds the Loop opcode byte (_code.Count becomes 3),
        // then computes offset = (_code.Count + 2) - loopStart = (3 + 2) - 0 = 5
        ushort backOffset = (ushort)((chunk.Code[3] << 8) | chunk.Code[4]);
        Assert.Equal(5, backOffset);
    }

    // ---- Source Map Tests ----

    [Fact]
    public void AddSourceMapping_RecordedInChunk()
    {
        var builder = new ChunkBuilder();
        var span = new SourceSpan("test.stash", 1, 1, 1, 10);
        builder.AddSourceMapping(span);
        builder.Emit(OpCode.Null);

        var span2 = new SourceSpan("test.stash", 2, 1, 2, 10);
        builder.AddSourceMapping(span2);
        builder.Emit(OpCode.Return);

        Chunk chunk = builder.Build();
        Assert.Equal(2, chunk.SourceMap.Count);
    }

    [Fact]
    public void AddSourceMapping_AtSpecificOffset()
    {
        var builder = new ChunkBuilder();
        builder.Emit(OpCode.Null);  // offset 0
        builder.Emit(OpCode.Pop);   // offset 1

        var span = new SourceSpan("test.stash", 5, 1, 5, 10);
        builder.AddSourceMapping(0, span);

        Chunk chunk = builder.Build();
        Assert.Equal(5, chunk.SourceMap.GetLine(0));
    }

    // ---- Upvalue Tests ----

    [Fact]
    public void AddUpvalue_ReturnsIndex()
    {
        var builder = new ChunkBuilder();
        byte idx0 = builder.AddUpvalue(0, true);
        byte idx1 = builder.AddUpvalue(1, false);

        Assert.Equal(0, idx0);
        Assert.Equal(1, idx1);

        Chunk chunk = builder.Build();
        Assert.Equal(2, chunk.Upvalues.Length);
        Assert.True(chunk.Upvalues[0].IsLocal);
        Assert.Equal(0, chunk.Upvalues[0].Index);
        Assert.False(chunk.Upvalues[1].IsLocal);
        Assert.Equal(1, chunk.Upvalues[1].Index);
    }

    [Fact]
    public void AddUpvalue_Deduplicates()
    {
        var builder = new ChunkBuilder();
        byte idx0 = builder.AddUpvalue(3, true);
        byte idx1 = builder.AddUpvalue(3, true);  // same as idx0
        byte idx2 = builder.AddUpvalue(3, false); // different (isLocal differs)

        Assert.Equal(idx0, idx1);
        Assert.NotEqual(idx0, idx2);
    }

    // ---- Build Tests ----

    [Fact]
    public void Build_PreservesMetadata()
    {
        var builder = new ChunkBuilder
        {
            Name = "myFunc",
            Arity = 3,
            MinArity = 1,
            LocalCount = 5,
            IsAsync = true,
            HasRestParam = true
        };
        builder.Emit(OpCode.Return);

        Chunk chunk = builder.Build();

        Assert.Equal("myFunc", chunk.Name);
        Assert.Equal(3, chunk.Arity);
        Assert.Equal(1, chunk.MinArity);
        Assert.Equal(5, chunk.LocalCount);
        Assert.True(chunk.IsAsync);
        Assert.True(chunk.HasRestParam);
    }

    [Fact]
    public void Build_DefaultMetadata()
    {
        var builder = new ChunkBuilder();
        builder.Emit(OpCode.Return);
        Chunk chunk = builder.Build();

        Assert.Null(chunk.Name);
        Assert.Equal(0, chunk.Arity);
        Assert.Equal(0, chunk.MinArity);
        Assert.Equal(0, chunk.LocalCount);
        Assert.False(chunk.IsAsync);
        Assert.False(chunk.HasRestParam);
        Assert.Empty(chunk.Upvalues);
    }

    [Fact]
    public void Build_EmptyChunk()
    {
        var builder = new ChunkBuilder();
        Chunk chunk = builder.Build();

        Assert.Empty(chunk.Code);
        Assert.Empty(chunk.Constants);
        Assert.Equal(0, chunk.SourceMap.Count);
    }
}
