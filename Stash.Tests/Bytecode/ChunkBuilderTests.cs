using Stash.Bytecode;
using Stash.Common;
using Stash.Runtime;

namespace Stash.Tests.Bytecode;

public class ChunkBuilderTests
{
    // ---- OpCodeInfo Tests ----

    [Theory]
    [InlineData(OpCode.Add, OpCodeFormat.ABC)]
    [InlineData(OpCode.Return, OpCodeFormat.ABC)]
    [InlineData(OpCode.LoadNull, OpCodeFormat.ABC)]
    [InlineData(OpCode.LoadK, OpCodeFormat.ABx)]
    [InlineData(OpCode.GetGlobal, OpCodeFormat.ABx)]
    [InlineData(OpCode.Jmp, OpCodeFormat.ABx)]
    [InlineData(OpCode.TryEnd, OpCodeFormat.Ax)]
    public void OpCodeInfo_GetFormat_ReturnsExpectedFormat(OpCode opCode, OpCodeFormat expected)
    {
        Assert.Equal(expected, OpCodeInfo.GetFormat(opCode));
    }

    [Fact]
    public void OpCodeInfo_AllOpcodes_HaveDefinedFormat()
    {
        foreach (OpCode op in Enum.GetValues<OpCode>())
        {
            OpCodeFormat fmt = OpCodeInfo.GetFormat(op);
            Assert.True(Enum.IsDefined(fmt));
        }
    }

    // ---- Emit Tests ----

    [Fact]
    public void EmitABC_ProducesSingleInstruction()
    {
        var builder = new ChunkBuilder();
        builder.EmitABC(OpCode.Add, 1, 2, 3);
        Chunk chunk = builder.Build();

        Assert.Single(chunk.Code);
        Assert.Equal(OpCode.Add, Instruction.GetOp(chunk.Code[0]));
    }

    [Fact]
    public void EmitABC_FieldsAreCorrectlyEncoded()
    {
        var builder = new ChunkBuilder();
        builder.EmitABC(OpCode.Add, 7, 15, 42);
        Chunk chunk = builder.Build();

        uint inst = chunk.Code[0];
        Assert.Equal(OpCode.Add, Instruction.GetOp(inst));
        Assert.Equal(7, Instruction.GetA(inst));
        Assert.Equal(15, Instruction.GetB(inst));
        Assert.Equal(42, Instruction.GetC(inst));
    }

    [Fact]
    public void EmitABx_ProducesSingleInstruction()
    {
        var builder = new ChunkBuilder();
        builder.EmitABx(OpCode.LoadK, 2, 5);
        Chunk chunk = builder.Build();

        Assert.Single(chunk.Code);
        Assert.Equal(OpCode.LoadK, Instruction.GetOp(chunk.Code[0]));
    }

    [Fact]
    public void EmitABx_FieldsAreCorrectlyEncoded()
    {
        var builder = new ChunkBuilder();
        builder.EmitABx(OpCode.LoadK, 3, 1000);
        Chunk chunk = builder.Build();

        uint inst = chunk.Code[0];
        Assert.Equal(OpCode.LoadK, Instruction.GetOp(inst));
        Assert.Equal(3, Instruction.GetA(inst));
        Assert.Equal(1000, Instruction.GetBx(inst));
    }

    [Fact]
    public void EmitAsBx_FieldsAreCorrectlyEncoded()
    {
        var builder = new ChunkBuilder();
        builder.EmitAsBx(OpCode.Jmp, 0, 42);
        Chunk chunk = builder.Build();

        uint inst = chunk.Code[0];
        Assert.Equal(OpCode.Jmp, Instruction.GetOp(inst));
        Assert.Equal(42, Instruction.GetSBx(inst));
    }

    [Fact]
    public void EmitAsBx_NegativeOffset_EncodesCorrectly()
    {
        var builder = new ChunkBuilder();
        builder.EmitAsBx(OpCode.Loop, 0, -5);
        Chunk chunk = builder.Build();

        Assert.Equal(-5, Instruction.GetSBx(chunk.Code[0]));
    }

    [Fact]
    public void CurrentOffset_TracksEmittedInstructions()
    {
        var builder = new ChunkBuilder();
        Assert.Equal(0, builder.CurrentOffset);

        builder.EmitABC(OpCode.LoadNull, 0, 0, 0);  // instruction 0
        Assert.Equal(1, builder.CurrentOffset);

        builder.EmitABx(OpCode.LoadK, 1, 0);        // instruction 1
        Assert.Equal(2, builder.CurrentOffset);

        builder.EmitABC(OpCode.Add, 2, 0, 1);       // instruction 2
        Assert.Equal(3, builder.CurrentOffset);
    }

    // ---- Multiple Instructions ----

    [Fact]
    public void Emit_MultipleInstructions_ProducesCorrectSequence()
    {
        var builder = new ChunkBuilder();
        ushort constIdx = builder.AddConstant(42L);
        builder.EmitABx(OpCode.LoadK, 0, constIdx);   // instruction 0
        builder.EmitABC(OpCode.Add, 0, 0, 0);          // instruction 1
        builder.EmitA(OpCode.Return, 0);               // instruction 2

        Chunk chunk = builder.Build();
        Assert.Equal(3, chunk.Code.Length);
        Assert.Equal(OpCode.Return, Instruction.GetOp(chunk.Code[2]));
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
        // JmpFalse at instruction index 0 (placeholder sBx=0)
        int patch = builder.EmitJump(OpCode.JmpFalse);
        // Add at instruction index 1
        builder.EmitABC(OpCode.Add, 0, 1, 2);
        // PatchJump(0) → sBx = _code.Count - 0 - 1 = 2 - 0 - 1 = 1
        builder.PatchJump(patch);

        Chunk chunk = builder.Build();
        int offset = Instruction.GetSBx(chunk.Code[patch]);
        Assert.Equal(1, offset);
    }

    [Fact]
    public void EmitJump_PatchJump_ZeroOffset()
    {
        var builder = new ChunkBuilder();
        // Jmp at index 0, patch immediately → sBx = 1 - 0 - 1 = 0
        int patch = builder.EmitJump(OpCode.Jmp);
        builder.PatchJump(patch);

        Chunk chunk = builder.Build();
        int offset = Instruction.GetSBx(chunk.Code[patch]);
        Assert.Equal(0, offset);
    }

    // ---- Loop Tests ----

    [Fact]
    public void EmitLoop_ProducesCorrectBackwardOffset()
    {
        var builder = new ChunkBuilder();
        int loopStart = builder.CurrentOffset;          // index 0
        builder.EmitABC(OpCode.Add, 0, 1, 2);           // instruction 0
        builder.EmitABC(OpCode.Sub, 0, 0, 1);           // instruction 1
        // EmitLoop(0, 0) → offset = 0 - 2 - 1 = -3
        builder.EmitLoop(0, loopStart);                 // instruction 2

        Chunk chunk = builder.Build();
        Assert.Equal(OpCode.Loop, Instruction.GetOp(chunk.Code[2]));
        int backOffset = Instruction.GetSBx(chunk.Code[2]);
        Assert.Equal(-3, backOffset);
    }

    // ---- Source Map Tests ----

    [Fact]
    public void AddSourceMapping_RecordedInChunk()
    {
        var builder = new ChunkBuilder();
        var span = new SourceSpan("test.stash", 1, 1, 1, 10);
        builder.AddSourceMapping(span);
        builder.EmitABC(OpCode.LoadNull, 0, 0, 0);

        var span2 = new SourceSpan("test.stash", 2, 1, 2, 10);
        builder.AddSourceMapping(span2);
        builder.EmitA(OpCode.Return, 0);

        Chunk chunk = builder.Build();
        Assert.Equal(2, chunk.SourceMap.Count);
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
            MaxRegs = 5,
            IsAsync = true,
            HasRestParam = true
        };
        builder.EmitA(OpCode.Return, 0);

        Chunk chunk = builder.Build();

        Assert.Equal("myFunc", chunk.Name);
        Assert.Equal(3, chunk.Arity);
        Assert.Equal(1, chunk.MinArity);
        Assert.Equal(5, chunk.MaxRegs);
        Assert.True(chunk.IsAsync);
        Assert.True(chunk.HasRestParam);
    }

    [Fact]
    public void Build_DefaultMetadata()
    {
        var builder = new ChunkBuilder();
        builder.EmitA(OpCode.Return, 0);
        Chunk chunk = builder.Build();

        Assert.Null(chunk.Name);
        Assert.Equal(0, chunk.Arity);
        Assert.Equal(0, chunk.MinArity);
        Assert.Equal(0, chunk.MaxRegs);
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
