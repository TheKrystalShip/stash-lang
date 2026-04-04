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
        Assert.Equal(1, result!.StartLine);
    }

    [Fact]
    public void SourceMap_SingleEntry_LaterOffset_ReturnsSameSpan()
    {
        var span = new SourceSpan("test.stash", 1, 1, 1, 10);
        var map = new SourceMap(new[] { new SourceMapEntry(0, span) });

        // Offset 5 is after the entry at 0, so it returns the entry at 0
        SourceSpan? result = map.GetSpan(5);
        Assert.NotNull(result);
        Assert.Equal(1, result!.StartLine);
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
    public void Disassemble_EmptyChunk_ShowsHeaderOnly()
    {
        var builder = new ChunkBuilder();
        Chunk chunk = builder.Build();

        string output = Disassembler.Disassemble(chunk);
        Assert.Contains("== <script> ==", output);
    }

    [Fact]
    public void Disassemble_NamedChunk_ShowsName()
    {
        var builder = new ChunkBuilder { Name = "myFunc" };
        builder.Emit(OpCode.Return);
        Chunk chunk = builder.Build();

        string output = Disassembler.Disassemble(chunk);
        Assert.Contains("== myFunc ==", output);
    }

    [Fact]
    public void Disassemble_SimpleOpcodes_ShowsCorrectFormat()
    {
        var builder = new ChunkBuilder();
        var span = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span);
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Return);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("0000", output);
        Assert.Contains("Null", output);
        Assert.Contains("Return", output);
    }

    [Fact]
    public void Disassemble_ConstInstruction_ShowsValue()
    {
        var builder = new ChunkBuilder();
        var span = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span);
        ushort idx = builder.AddConstant(42L);
        builder.Emit(OpCode.Const, idx);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("Const", output);
        Assert.Contains("; 42", output);
    }

    [Fact]
    public void Disassemble_StringConstant_ShowsQuoted()
    {
        var builder = new ChunkBuilder();
        var span = new SourceSpan("test.stash", 1, 1, 1, 10);
        builder.AddSourceMapping(span);
        ushort idx = builder.AddConstant("hello");
        builder.Emit(OpCode.Const, idx);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("\"hello\"", output);
    }

    [Fact]
    public void Disassemble_LocalAccess_ShowsSlot()
    {
        var builder = new ChunkBuilder();
        var span = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span);
        builder.Emit(OpCode.LoadLocal, (byte)3);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("LoadLocal", output);
        Assert.Contains("3", output);
    }

    [Fact]
    public void Disassemble_JumpInstruction_ShowsTarget()
    {
        var builder = new ChunkBuilder();
        var span = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span);
        int patch = builder.EmitJump(OpCode.JumpFalse);

        builder.AddSourceMapping(new SourceSpan("test.stash", 2, 1, 2, 5));
        builder.Emit(OpCode.Null);   // offset 3
        builder.Emit(OpCode.Pop);    // offset 4
        builder.PatchJump(patch);    // patch to offset 5

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("JumpFalse", output);
        Assert.Contains("-> 0005", output);
    }

    [Fact]
    public void Disassemble_LoopInstruction_ShowsBackwardTarget()
    {
        var builder = new ChunkBuilder();
        var span = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span);
        int loopStart = builder.CurrentOffset;  // offset 0
        builder.Emit(OpCode.Null);               // offset 0

        builder.AddSourceMapping(new SourceSpan("test.stash", 2, 1, 2, 5));
        builder.Emit(OpCode.Pop);                // offset 1
        builder.EmitLoop(loopStart);             // offset 2: Loop -> backward to 0

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("Loop", output);
        Assert.Contains("-> 0000", output);
    }

    [Fact]
    public void Disassemble_LineNumbers_ShowContinuationMarker()
    {
        var builder = new ChunkBuilder();
        // Two instructions on line 1
        var span1 = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span1);
        builder.Emit(OpCode.Null);

        // Same line mapping for second instruction
        builder.AddSourceMapping(span1);
        builder.Emit(OpCode.Pop);

        // New line for third instruction
        var span2 = new SourceSpan("test.stash", 2, 1, 2, 5);
        builder.AddSourceMapping(span2);
        builder.Emit(OpCode.Return);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        // First instruction shows line 1, second shows |, third shows line 2
        string[] lines = output.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        // lines[0] is header, lines[1] is first instruction, etc.
        Assert.True(lines.Length >= 4);
        Assert.Contains("   1 ", lines[1]);   // line 1
        Assert.Contains("   | ", lines[2]);   // continuation
        Assert.Contains("   2 ", lines[3]);   // line 2
    }

    [Fact]
    public void Disassemble_GlobalAccess_ShowsName()
    {
        var builder = new ChunkBuilder();
        var span = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span);
        ushort nameIdx = builder.AddConstant("myVar");
        builder.Emit(OpCode.LoadGlobal, nameIdx);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("LoadGlobal", output);
        Assert.Contains("; myVar", output);
    }

    [Fact]
    public void Disassemble_FieldAccess_ShowsFieldName()
    {
        var builder = new ChunkBuilder();
        var span = new SourceSpan("test.stash", 1, 1, 1, 5);
        builder.AddSourceMapping(span);
        ushort nameIdx = builder.AddConstant("name");
        builder.Emit(OpCode.GetField, nameIdx);

        Chunk chunk = builder.Build();
        string output = Disassembler.Disassemble(chunk);

        Assert.Contains("GetField", output);
        Assert.Contains("; name", output);
    }

    // ---- Roundtrip: Build → Disassemble ----

    [Fact]
    public void Disassemble_CompleteProgram_ProducesReadableOutput()
    {
        // Simulates: var x = 42; return x + 10;
        var builder = new ChunkBuilder();
        builder.LocalCount = 2;

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

        // Verify it looks reasonable
        Assert.Contains("== <script> ==", output);
        Assert.Contains("; 42", output);
        Assert.Contains("; 10", output);
        Assert.Contains("Add", output);
        Assert.Contains("Return", output);

        // Verify line numbers appear
        Assert.Contains("   1 ", output);
        Assert.Contains("   2 ", output);
    }
}
