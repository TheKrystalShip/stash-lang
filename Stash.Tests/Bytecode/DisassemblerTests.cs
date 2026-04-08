using Stash.Bytecode;
using Stash.Common;

namespace Stash.Tests.Bytecode;

public class DisassemblerTests
{
    // ---- SourceMap Tests ----

    [Fact]
    public void SourceMap_Empty_ReturnsNull()
    {
        var map = new SourceMap(System.Array.Empty<SourceMapEntry>());
        Assert.Null(map.GetSpan(0));
        Assert.Equal(-1, map.GetLine(0));
    }

    [Fact]
    public void SourceMap_SingleEntry_ExactMatch()
    {
        var span = new SourceSpan("test.stash", 1, 1, 1, 10);
        var map = new SourceMap(new[] { new SourceMapEntry(0, span) });

        SourceSpan? result = map.GetSpan(0);
        Assert.NotNull(result);
        Assert.Equal(1, result!.Value.StartLine);
    }

    [Fact]
    public void SourceMap_SingleEntry_LaterOffset_ReturnsSameSpan()
    {
        var span = new SourceSpan("test.stash", 1, 1, 1, 10);
        var map = new SourceMap(new[] { new SourceMapEntry(0, span) });

        // Offset 5 is after the entry at 0, so it returns the entry at 0
        SourceSpan? result = map.GetSpan(5);
        Assert.NotNull(result);
        Assert.Equal(1, result!.Value.StartLine);
    }

    [Fact]
    public void SourceMap_SingleEntry_BeforeEntry_ReturnsNull()
    {
        var span = new SourceSpan("test.stash", 1, 1, 1, 10);
        var map = new SourceMap(new[] { new SourceMapEntry(5, span) });

        // Offset 3 is before the entry at 5
        Assert.Null(map.GetSpan(3));
    }

    [Fact]
    public void SourceMap_MultipleEntries_ReturnsCorrectSpan()
    {
        var span1 = new SourceSpan("test.stash", 1, 1, 1, 10);
        var span2 = new SourceSpan("test.stash", 2, 1, 2, 10);
        var span3 = new SourceSpan("test.stash", 3, 1, 3, 10);
        var map = new SourceMap(new[]
        {
            new SourceMapEntry(0, span1),
            new SourceMapEntry(3, span2),
            new SourceMapEntry(7, span3),
        });

        Assert.Equal(1, map.GetLine(0));   // Exact match at 0
        Assert.Equal(1, map.GetLine(2));   // Between 0 and 3, floor to 0
        Assert.Equal(2, map.GetLine(3));   // Exact match at 3
        Assert.Equal(2, map.GetLine(5));   // Between 3 and 7, floor to 3
        Assert.Equal(3, map.GetLine(7));   // Exact match at 7
        Assert.Equal(3, map.GetLine(100)); // After all entries, floor to 7
    }

    [Fact]
    public void SourceMap_Count_ReturnsEntryCount()
    {
        var span = new SourceSpan("test.stash", 1, 1, 1, 10);
        var map = new SourceMap(new[]
        {
            new SourceMapEntry(0, span),
            new SourceMapEntry(3, span),
        });

        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void SourceMap_Indexer_ReturnsCorrectEntry()
    {
        var span1 = new SourceSpan("test.stash", 1, 1, 1, 10);
        var span2 = new SourceSpan("test.stash", 5, 1, 5, 10);
        var map = new SourceMap(new[]
        {
            new SourceMapEntry(0, span1),
            new SourceMapEntry(10, span2),
        });

        Assert.Equal(0, map[0].BytecodeOffset);
        Assert.Equal(10, map[1].BytecodeOffset);
        Assert.Equal(5, map[1].Span.StartLine);
    }

    // ---- Disassembler Tests ----

    [Fact]
    public void Disassemble_EmptyChunk_ShowsHeader()
    {
        var builder = new ChunkBuilder();
        builder.Optimize = false;
        Chunk chunk = builder.Build();

        string output = Disassembler.Disassemble(chunk);
        Assert.Contains("<script>", output);
        Assert.Contains("constants: 0", output);
    }

    [Fact]
    public void Disassemble_NamedChunk_ShowsName()
    {
        var builder = new ChunkBuilder { Name = "myFunc", Optimize = false };
        builder.Emit(OpCode.Return);
        Chunk chunk = builder.Build();

        string output = Disassembler.Disassemble(chunk);
        Assert.Contains("myFunc", output);
    }

    [Fact]
    public void Disassemble_SimpleOpcodes_ShowsDotNames()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var span = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span);
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Return);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("null", output);
        Assert.Contains("ret", output);
        Assert.Contains(".code:", output);
    }

    [Fact]
    public void Disassemble_ConstInstruction_ShowsValueInline()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var span = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span);
        ushort idx = builder.AddConstant(42L);
        builder.Emit(OpCode.Const, idx);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("const", output);
        Assert.Contains("42", output);
        Assert.Contains(".const:", output);
    }

    [Fact]
    public void Disassemble_StringConstant_ShowsQuoted()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var span = new SourceSpan("test.stash", 1, 1, 1, 10);
        builder.AddSourceMapping(span);
        ushort idx = builder.AddConstant("hello");
        builder.Emit(OpCode.Const, idx);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("\"hello\"", output);
    }

    [Fact]
    public void Disassemble_HexOffsets_Used()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var span = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span);
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Pop);
        builder.Emit(OpCode.Return);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        // Hex offsets in output
        Assert.Contains("0000", output);
        Assert.Contains("0001", output);
        Assert.Contains("0002", output);
    }

    [Fact]
    public void Disassemble_RawHexBytes_Shown()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var span = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span);
        builder.Emit(OpCode.Null); // byte value 0x01

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        // Should contain hex byte representation
        Assert.Contains("01", output);
    }

    [Fact]
    public void Disassemble_JumpInstruction_ShowsLabelAndTarget()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var span = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span);
        int patch = builder.EmitJump(OpCode.JumpFalse);

        builder.AddSourceMapping(new SourceSpan("test.stash", 2, 1, 2, 5));
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Pop);
        builder.PatchJump(patch);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("jmp.false", output);
        Assert.Contains(".L0", output);
        Assert.Contains("->", output);
    }

    [Fact]
    public void Disassemble_LoopInstruction_ShowsBackwardTarget()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var span = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span);
        int loopStart = builder.CurrentOffset;
        builder.Emit(OpCode.Null);

        builder.AddSourceMapping(new SourceSpan("test.stash", 2, 1, 2, 5));
        builder.Emit(OpCode.Pop);
        builder.EmitLoop(loopStart);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("loop", output);
        Assert.Contains("->", output);
    }

    [Fact]
    public void Disassemble_SourceLineAnnotations_Shown()
    {
        var builder = new ChunkBuilder { Optimize = false };
        builder.AddSourceMapping(new SourceSpan("test.stash", 1, 1, 1, 5));
        builder.Emit(OpCode.Null);
        builder.AddSourceMapping(new SourceSpan("test.stash", 2, 1, 2, 5));
        builder.Emit(OpCode.Return);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("line 1", output);
        Assert.Contains("line 2", output);
    }

    [Fact]
    public void Disassemble_ConstSection_ShowsConstants()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var span = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span);
        builder.AddConstant(42L);
        builder.AddConstant("hello");
        builder.Emit(OpCode.Null);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains(".const:", output);
        Assert.Contains("[0] 42", output);
        Assert.Contains("[1] \"hello\"", output);
    }

    [Fact]
    public void Disassemble_CompleteProgram_ProducesReadableOutput()
    {
        var builder = new ChunkBuilder { Optimize = false, LocalCount = 2 };
        var line1 = new SourceSpan("test.stash", 1, 1, 1, 12);
        builder.AddSourceMapping(line1);
        ushort c42 = builder.AddConstant(42L);
        builder.Emit(OpCode.Const, c42);
        builder.Emit(OpCode.StoreLocal, (byte)0);

        var line2 = new SourceSpan("test.stash", 2, 1, 2, 16);
        builder.AddSourceMapping(line2);
        builder.Emit(OpCode.LoadLocal, (byte)0);
        ushort c10 = builder.AddConstant(10L);
        builder.Emit(OpCode.Const, c10);
        builder.Emit(OpCode.Add);
        builder.Emit(OpCode.Return);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("<script>", output);
        Assert.Contains("42", output);
        Assert.Contains("10", output);
        Assert.Contains("add", output);
        Assert.Contains("ret", output);
        Assert.Contains("line 1", output);
        Assert.Contains("line 2", output);
    }

    [Fact]
    public void DisassembleAll_NestedChunks_ShowsAll()
    {
        // Build inner function chunk
        var innerBuilder = new ChunkBuilder { Name = "inner", Arity = 1, MinArity = 1, Optimize = false };
        innerBuilder.AddSourceMapping(new SourceSpan("test.stash", 5, 1, 5, 10));
        innerBuilder.Emit(OpCode.LoadLocal, (byte)0);
        innerBuilder.Emit(OpCode.Return);
        Chunk innerChunk = innerBuilder.Build();

        // Build outer chunk referencing inner
        var outerBuilder = new ChunkBuilder { Optimize = false };
        outerBuilder.AddSourceMapping(new SourceSpan("test.stash", 1, 1, 1, 10));
        ushort fnIdx = outerBuilder.AddConstant(innerChunk);
        outerBuilder.Emit(OpCode.Closure, fnIdx);
        outerBuilder.Emit(OpCode.Return);
        Chunk outerChunk = outerBuilder.Build();

        string output = Disassembler.DisassembleAll(outerChunk);

        Assert.Contains("<script>", output);
        Assert.Contains("inner", output);
        Assert.Contains("arity: 1", output);
    }

    [Fact]
    public void Disassemble_CompactMode_SimplerFormat()
    {
        var builder = new ChunkBuilder { Optimize = false };
        builder.AddSourceMapping(new SourceSpan("test.stash", 1, 1, 1, 5));
        builder.AddConstant(42L);
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Return);

        Chunk chunk = builder.Build();
        var options = new DisassemblerOptions { Compact = true };
        string output = Disassembler.Disassemble(chunk, options);

        Assert.Contains("== <script> ==", output);
        Assert.Contains("null", output);
        Assert.Contains("ret", output);
        // Compact mode: no .const: section, no hex bytes
        Assert.DoesNotContain(".const:", output);
    }

    [Fact]
    public void Disassemble_ColorMode_ContainsAnsiCodes()
    {
        var builder = new ChunkBuilder { Optimize = false };
        builder.AddSourceMapping(new SourceSpan("test.stash", 1, 1, 1, 5));
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Return);

        Chunk chunk = builder.Build();
        var options = new DisassemblerOptions { Color = true };
        string output = Disassembler.Disassemble(chunk, options);

        Assert.Contains("\x1b[", output); // ANSI escape sequence
    }
}
