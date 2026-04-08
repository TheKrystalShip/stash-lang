using System.Collections.Generic;
using System.IO;
using System.Text;
using Stash.Bytecode;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Bytecode;

public class BytecodeSerializationTests
{
    private static Chunk RoundTrip(
        Chunk original,
        bool includeDebugInfo = true,
        string? sourceText = null,
        bool embedSource = false)
    {
        using var ms = new MemoryStream();
        BytecodeWriter.Write(ms, original, includeDebugInfo: includeDebugInfo, sourceText: sourceText, embedSource: embedSource);
        ms.Position = 0;
        return BytecodeReader.Read(ms);
    }

    // =========================================================================
    // Round-trip tests
    // =========================================================================

    [Fact]
    public void RoundTrip_EmptyChunk_PreservesDefaults()
    {
        var builder = new ChunkBuilder { Optimize = false };
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.Empty(result.Code);
        Assert.Equal(0, result.Arity);
        Assert.Equal(0, result.MinArity);
        Assert.Null(result.Name);
        Assert.False(result.IsAsync);
        Assert.False(result.HasRestParam);
        Assert.False(result.MayHaveCapturedLocals);
    }

    [Fact]
    public void RoundTrip_SimpleOpcodes_PreservesCode()
    {
        var builder = new ChunkBuilder { Optimize = false };
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Pop);
        builder.Emit(OpCode.Return);
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.Equal(original.Code.Length, result.Code.Length);
        Assert.Equal((byte)OpCode.Null,   result.Code[0]);
        Assert.Equal((byte)OpCode.Pop,    result.Code[1]);
        Assert.Equal((byte)OpCode.Return, result.Code[2]);
    }

    [Fact]
    public void RoundTrip_IntConstant_PreservesValue()
    {
        var builder = new ChunkBuilder { Optimize = false };
        ushort idx = builder.AddConstant(42L);
        builder.Emit(OpCode.Const, idx);
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        var constant = Assert.Single(result.Constants);
        Assert.Equal(StashValueTag.Int, constant.Tag);
        Assert.Equal(42L, constant.AsInt);
    }

    [Fact]
    public void RoundTrip_FloatConstant_PreservesValue()
    {
        var builder = new ChunkBuilder { Optimize = false };
        ushort idx = builder.AddConstant(3.14);
        builder.Emit(OpCode.Const, idx);
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        var constant = Assert.Single(result.Constants);
        Assert.Equal(StashValueTag.Float, constant.Tag);
        Assert.Equal(3.14, constant.AsFloat);
    }

    [Fact]
    public void RoundTrip_StringConstant_PreservesValue()
    {
        var builder = new ChunkBuilder { Optimize = false };
        ushort idx = builder.AddConstant("hello world");
        builder.Emit(OpCode.Const, idx);
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        var constant = Assert.Single(result.Constants);
        Assert.Equal(StashValueTag.Obj, constant.Tag);
        Assert.Equal("hello world", constant.AsObj as string);
    }

    [Fact]
    public void RoundTrip_BoolConstants_PreservesValues()
    {
        var builder = new ChunkBuilder { Optimize = false };
        ushort trueIdx  = builder.AddConstant(StashValue.FromBool(true));
        ushort falseIdx = builder.AddConstant(StashValue.FromBool(false));
        builder.Emit(OpCode.Const, trueIdx);
        builder.Emit(OpCode.Const, falseIdx);
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.Equal(2, result.Constants.Length);
        Assert.Equal(StashValueTag.Bool, result.Constants[trueIdx].Tag);
        Assert.True(result.Constants[trueIdx].AsBool);
        Assert.Equal(StashValueTag.Bool, result.Constants[falseIdx].Tag);
        Assert.False(result.Constants[falseIdx].AsBool);
    }

    [Fact]
    public void RoundTrip_NestedChunk_PreservesFunction()
    {
        var innerBuilder = new ChunkBuilder { Name = "inner", Arity = 2, MinArity = 1, Optimize = false };
        innerBuilder.Emit(OpCode.Return);
        Chunk innerChunk = innerBuilder.Build();

        var outerBuilder = new ChunkBuilder { Optimize = false };
        ushort idx = outerBuilder.AddConstant(innerChunk);
        outerBuilder.Emit(OpCode.Const, idx);
        Chunk original = outerBuilder.Build();

        Chunk result = RoundTrip(original);

        Assert.Single(result.Constants);
        var nested = Assert.IsType<Chunk>(result.Constants[0].AsObj);
        Assert.Equal("inner", nested.Name);
        Assert.Equal(2, nested.Arity);
        Assert.Equal(1, nested.MinArity);
        byte returnByte = Assert.Single(nested.Code);
        Assert.Equal((byte)OpCode.Return, returnByte);
    }

    [Fact]
    public void RoundTrip_Metadata_PreservesAllFields()
    {
        var builder = new ChunkBuilder
        {
            Name                  = "testFunc",
            Arity                 = 2,
            MinArity              = 1,
            LocalCount            = 5,
            IsAsync               = true,
            HasRestParam          = true,
            MayHaveCapturedLocals = true,
            Optimize              = false,
        };
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.Equal("testFunc", result.Name);
        Assert.Equal(2, result.Arity);
        Assert.Equal(1, result.MinArity);
        Assert.Equal(5, result.LocalCount);
        Assert.True(result.IsAsync);
        Assert.True(result.HasRestParam);
        Assert.True(result.MayHaveCapturedLocals);
    }

    [Fact]
    public void RoundTrip_Upvalues_PreservesDescriptors()
    {
        var builder = new ChunkBuilder { Optimize = false };
        builder.AddUpvalue(3, isLocal: true);
        builder.AddUpvalue(1, isLocal: false);
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.Equal(2, result.Upvalues.Length);
        Assert.Equal(3,    result.Upvalues[0].Index);
        Assert.True(result.Upvalues[0].IsLocal);
        Assert.Equal(1,    result.Upvalues[1].Index);
        Assert.False(result.Upvalues[1].IsLocal);
    }

    [Fact]
    public void RoundTrip_SourceMap_PreservesLocations()
    {
        var builder = new ChunkBuilder { Optimize = false };

        // Add mappings at explicit offsets before emitting code
        builder.AddSourceMapping(0, new SourceSpan("test.stash",  1, 1, 1, 5));
        builder.AddSourceMapping(1, new SourceSpan("test.stash",  2, 1, 2, 10));
        builder.AddSourceMapping(2, new SourceSpan("other.stash", 5, 3, 5, 8));
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Pop);
        builder.Emit(OpCode.Return);
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.Equal(3, result.SourceMap.Count);

        Assert.Equal(0,           result.SourceMap[0].BytecodeOffset);
        Assert.Equal("test.stash",  result.SourceMap[0].Span.File);
        Assert.Equal(1,             result.SourceMap[0].Span.StartLine);
        Assert.Equal(1,             result.SourceMap[0].Span.StartColumn);
        Assert.Equal(1,             result.SourceMap[0].Span.EndLine);
        Assert.Equal(5,             result.SourceMap[0].Span.EndColumn);

        Assert.Equal(1,           result.SourceMap[1].BytecodeOffset);
        Assert.Equal("test.stash",  result.SourceMap[1].Span.File);
        Assert.Equal(2,             result.SourceMap[1].Span.StartLine);

        Assert.Equal(2,            result.SourceMap[2].BytecodeOffset);
        Assert.Equal("other.stash", result.SourceMap[2].Span.File);
        Assert.Equal(5,             result.SourceMap[2].Span.StartLine);
        Assert.Equal(3,             result.SourceMap[2].Span.StartColumn);
    }

    [Fact]
    public void RoundTrip_LocalNames_PreservesDebugInfo()
    {
        var builder = new ChunkBuilder
        {
            LocalNames   = ["x", "y", "z"],
            LocalIsConst = [true, false, true],
            LocalCount   = 3,
            Optimize     = false,
        };
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.NotNull(result.LocalNames);
        Assert.Equal(new[] { "x", "y", "z" }, result.LocalNames);
        Assert.NotNull(result.LocalIsConst);
        Assert.Equal(new[] { true, false, true }, result.LocalIsConst);
    }

    [Fact]
    public void RoundTrip_UpvalueNames_Preserved()
    {
        var builder = new ChunkBuilder
        {
            UpvalueNames = ["captured"],
            Optimize     = false,
        };
        builder.AddUpvalue(0, isLocal: true);
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.NotNull(result.UpvalueNames);
        Assert.Equal(new[] { "captured" }, result.UpvalueNames);
    }

    [Fact]
    public void RoundTrip_GlobalNameTable_Preserved()
    {
        var allocator = new GlobalSlotAllocator();
        allocator.GetOrAllocate("x");
        allocator.GetOrAllocate("y");

        var builder = new ChunkBuilder { Optimize = false };
        builder.GlobalSlotAllocator = allocator;
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.Equal(2, result.GlobalSlotCount);
        Assert.NotNull(result.GlobalNameTable);
        Assert.Equal("x", result.GlobalNameTable![0]);
        Assert.Equal("y", result.GlobalNameTable![1]);
    }

    [Fact]
    public void RoundTrip_StripDebugInfo_OmitsDebugData()
    {
        var builder = new ChunkBuilder
        {
            LocalNames   = ["a", "b"],
            LocalIsConst = [false, true],
            UpvalueNames = ["captured"],
            LocalCount   = 2,
            Optimize     = false,
        };
        builder.Emit(OpCode.Null);
        builder.AddSourceMapping(0, new SourceSpan("test.stash", 1, 1, 1, 5));
        builder.AddUpvalue(0, isLocal: true);
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original, includeDebugInfo: false);

        Assert.Null(result.LocalNames);
        Assert.Null(result.LocalIsConst);
        Assert.Null(result.UpvalueNames);
        Assert.Equal(0, result.SourceMap.Count);
    }

    [Fact]
    public void RoundTrip_LargeConstantPool_HandlesMany()
    {
        var builder = new ChunkBuilder { Optimize = false };
        for (int i = 0; i < 500; i++)
            builder.AddConstant($"constant_{i}");
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.Equal(500, result.Constants.Length);
        for (int i = 0; i < 500; i++)
            Assert.Equal($"constant_{i}", result.Constants[i].AsObj as string);
    }

    [Fact]
    public void RoundTrip_EmbeddedSource_Preserved()
    {
        const string sourceText = "let x = 1;";
        var builder = new ChunkBuilder { Optimize = false };
        Chunk original = builder.Build();

        string tempPath = Path.GetTempFileName();
        try
        {
            BytecodeWriter.Write(
                tempPath,
                original,
                includeDebugInfo: true,
                sourceText: sourceText,
                embedSource: true);

            string? embedded = BytecodeReader.ReadEmbeddedSource(tempPath);

            Assert.Equal(sourceText, embedded);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // =========================================================================
    // Validation tests
    // =========================================================================

    [Fact]
    public void Read_InvalidMagic_Throws()
    {
        byte[] garbage = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09];
        using var ms = new MemoryStream(garbage);

        Assert.Throws<InvalidDataException>(() => BytecodeReader.Read(ms));
    }

    [Fact]
    public void Read_WrongFormatVersion_Throws()
    {
        // Valid magic bytes followed by version = 99
        byte[] header = new byte[32];
        header[0] = 0x53; // S
        header[1] = 0x54; // T
        header[2] = 0x42; // B
        header[3] = 0x43; // C
        header[4] = 99;   // version LE u16 = 99
        header[5] = 0;
        using var ms = new MemoryStream(header);

        Assert.Throws<InvalidDataException>(() => BytecodeReader.Read(ms));
    }

    [Fact]
    public void Read_CorruptedStream_Throws()
    {
        // Write valid bytecode then truncate to header + a few extra bytes
        var builder = new ChunkBuilder { Optimize = false };
        builder.Emit(OpCode.Return);
        Chunk original = builder.Build();

        using var full = new MemoryStream();
        BytecodeWriter.Write(full, original);
        byte[] truncated = full.ToArray()[..36]; // 32-byte header + 4 bytes of chunk

        using var ms = new MemoryStream(truncated);
        Assert.ThrowsAny<Exception>(() => BytecodeReader.Read(ms));
    }

    // =========================================================================
    // Utility tests
    // =========================================================================

    [Fact]
    public void IsBytecodeStream_ValidHeader_ReturnsTrue()
    {
        var builder = new ChunkBuilder { Optimize = false };
        Chunk original = builder.Build();

        using var ms = new MemoryStream();
        BytecodeWriter.Write(ms, original);
        ms.Position = 0;

        Assert.True(BytecodeReader.IsBytecodeStream(ms));
    }

    [Fact]
    public void IsBytecodeStream_InvalidHeader_ReturnsFalse()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("let x = 1;"));

        Assert.False(BytecodeReader.IsBytecodeStream(ms));
    }

    [Fact]
    public void IsBytecodeStream_PreservesPosition()
    {
        byte[] data = Encoding.UTF8.GetBytes("some non-bytecode data here");
        using var ms = new MemoryStream(data);
        ms.Position = 5;

        BytecodeReader.IsBytecodeStream(ms);

        Assert.Equal(5, ms.Position);
    }

    [Fact]
    public void OpCodeTableHash_Deterministic()
    {
        uint hash1 = BytecodeWriter.ComputeOpCodeTableHash();
        uint hash2 = BytecodeWriter.ComputeOpCodeTableHash();

        Assert.Equal(hash1, hash2);
    }

    // =========================================================================
    // IC slot reconstruction
    // =========================================================================

    [Fact]
    public void RoundTrip_GetFieldIC_ReconstructsICSlots()
    {
        var builder = new ChunkBuilder { Optimize = false };
        ushort nameIdx = builder.AddConstant("fieldName");
        ushort icSlot1 = builder.AllocateICSlot();
        ushort icSlot2 = builder.AllocateICSlot();
        builder.Emit(OpCode.GetFieldIC, nameIdx, icSlot1);
        builder.Emit(OpCode.GetFieldIC, nameIdx, icSlot2);
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.NotNull(result.ICSlots);
        Assert.Equal(2, result.ICSlots!.Length);
    }

    // =========================================================================
    // Edge cases
    // =========================================================================

    [Fact]
    public void RoundTrip_NullName_PreservesNull()
    {
        var builder = new ChunkBuilder { Optimize = false }; // Name defaults to null
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.Null(result.Name);
    }

    [Fact]
    public void RoundTrip_EmptyStringConstant_Preserved()
    {
        var builder = new ChunkBuilder { Optimize = false };
        ushort idx = builder.AddConstant("");
        builder.Emit(OpCode.Const, idx);
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        var constant = Assert.Single(result.Constants);
        Assert.Equal("", constant.AsObj as string);
    }

    [Fact]
    public void RoundTrip_NegativeNumbers_Preserved()
    {
        var builder = new ChunkBuilder { Optimize = false };
        ushort negIntIdx   = builder.AddConstant(-42L);
        ushort negFloatIdx = builder.AddConstant(-3.14);
        builder.Emit(OpCode.Const, negIntIdx);
        builder.Emit(OpCode.Const, negFloatIdx);
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.Equal(2, result.Constants.Length);
        Assert.Equal(-42L,  result.Constants[negIntIdx].AsInt);
        Assert.Equal(-3.14, result.Constants[negFloatIdx].AsFloat);
    }

    [Fact]
    public void RoundTrip_MaxValues_Preserved()
    {
        var builder = new ChunkBuilder { Optimize = false };
        ushort maxLongIdx = builder.AddConstant(long.MaxValue);
        ushort minLongIdx = builder.AddConstant(long.MinValue);
        ushort maxDblIdx  = builder.AddConstant(double.MaxValue);
        builder.Emit(OpCode.Const, maxLongIdx);
        builder.Emit(OpCode.Const, minLongIdx);
        builder.Emit(OpCode.Const, maxDblIdx);
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.Equal(3, result.Constants.Length);
        Assert.Equal(long.MaxValue,   result.Constants[maxLongIdx].AsInt);
        Assert.Equal(long.MinValue,   result.Constants[minLongIdx].AsInt);
        Assert.Equal(double.MaxValue, result.Constants[maxDblIdx].AsFloat);
    }

    // =========================================================================
    // Metadata constant round-trip tests
    // =========================================================================

    [Fact]
    public void RoundTrip_CommandMetadata_Preserved()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var meta = new CommandMetadata(3, true, false);
        builder.AddConstant(meta);
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Return);
        Chunk original = builder.Build();

        Chunk restored = RoundTrip(original);

        Assert.Single(restored.Constants);
        var result = Assert.IsType<CommandMetadata>(restored.Constants[0].AsObj);
        Assert.Equal(3, result.PartCount);
        Assert.True(result.IsPassthrough);
        Assert.False(result.IsStrict);
    }

    [Fact]
    public void RoundTrip_StructMetadata_Preserved()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var meta = new StructMetadata("Point", new[] { "x", "y" }, new[] { "toString" }, new[] { "ISerializable" });
        builder.AddConstant(meta);
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Return);
        Chunk original = builder.Build();

        Chunk restored = RoundTrip(original);

        Assert.Single(restored.Constants);
        var result = Assert.IsType<StructMetadata>(restored.Constants[0].AsObj);
        Assert.Equal("Point", result.Name);
        Assert.Equal(new[] { "x", "y" }, result.Fields);
        Assert.Equal(new[] { "toString" }, result.MethodNames);
        Assert.Equal(new[] { "ISerializable" }, result.InterfaceNames);
    }

    [Fact]
    public void RoundTrip_EnumMetadata_Preserved()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var meta = new EnumMetadata("Color", new[] { "Red", "Green", "Blue" });
        builder.AddConstant(meta);
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Return);
        Chunk original = builder.Build();

        Chunk restored = RoundTrip(original);

        Assert.Single(restored.Constants);
        var result = Assert.IsType<EnumMetadata>(restored.Constants[0].AsObj);
        Assert.Equal("Color", result.Name);
        Assert.Equal(new[] { "Red", "Green", "Blue" }, result.Members);
    }

    [Fact]
    public void RoundTrip_InterfaceMetadata_Preserved()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var meta = new InterfaceMetadata(
            "IShape",
            new[]
            {
                new InterfaceField("name", "str"),
                new InterfaceField("size", null),
            },
            new[]
            {
                new InterfaceMethod("area",  0, new List<string>(),          new List<string?>(),               "float"),
                new InterfaceMethod("draw",  2, new List<string> { "x", "y" }, new List<string?> { "int", "int" }, null),
            });
        builder.AddConstant(meta);
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Return);
        Chunk original = builder.Build();

        Chunk restored = RoundTrip(original);

        Assert.Single(restored.Constants);
        var result = Assert.IsType<InterfaceMetadata>(restored.Constants[0].AsObj);
        Assert.Equal("IShape", result.Name);

        Assert.Equal(2, result.Fields.Length);
        Assert.Equal("name", result.Fields[0].Name);
        Assert.Equal("str",  result.Fields[0].TypeHint);
        Assert.Equal("size", result.Fields[1].Name);
        Assert.Null(result.Fields[1].TypeHint);

        Assert.Equal(2, result.Methods.Length);
        Assert.Equal("area",  result.Methods[0].Name);
        Assert.Equal(0,       result.Methods[0].Arity);
        Assert.Empty(result.Methods[0].ParameterNames);
        Assert.Empty(result.Methods[0].ParameterTypes);
        Assert.Equal("float", result.Methods[0].ReturnType);

        Assert.Equal("draw",           result.Methods[1].Name);
        Assert.Equal(2,                result.Methods[1].Arity);
        Assert.Equal(new[] { "x", "y" }, result.Methods[1].ParameterNames);
        Assert.Equal(new string?[] { "int", "int" }, result.Methods[1].ParameterTypes);
        Assert.Null(result.Methods[1].ReturnType);
    }

    [Fact]
    public void RoundTrip_ExtendMetadata_Preserved()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var meta = new ExtendMetadata("Point", new[] { "translate", "scale" }, false);
        builder.AddConstant(meta);
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Return);
        Chunk original = builder.Build();

        Chunk restored = RoundTrip(original);

        Assert.Single(restored.Constants);
        var result = Assert.IsType<ExtendMetadata>(restored.Constants[0].AsObj);
        Assert.Equal("Point", result.TypeName);
        Assert.Equal(new[] { "translate", "scale" }, result.MethodNames);
        Assert.False(result.IsBuiltIn);
    }

    [Fact]
    public void RoundTrip_ImportMetadata_Preserved()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var meta = new ImportMetadata(new[] { "math", "utils" });
        builder.AddConstant(meta);
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Return);
        Chunk original = builder.Build();

        Chunk restored = RoundTrip(original);

        Assert.Single(restored.Constants);
        var result = Assert.IsType<ImportMetadata>(restored.Constants[0].AsObj);
        Assert.Equal(new[] { "math", "utils" }, result.Names);
    }

    [Fact]
    public void RoundTrip_ImportAsMetadata_Preserved()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var meta = new ImportAsMetadata("myModule");
        builder.AddConstant(meta);
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Return);
        Chunk original = builder.Build();

        Chunk restored = RoundTrip(original);

        Assert.Single(restored.Constants);
        var result = Assert.IsType<ImportAsMetadata>(restored.Constants[0].AsObj);
        Assert.Equal("myModule", result.AliasName);
    }

    [Fact]
    public void RoundTrip_DestructureMetadata_Preserved()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var withRest    = new DestructureMetadata("dict", new[] { "a", "b" }, "rest", true);
        var withoutRest = new DestructureMetadata("arr",  new[] { "first" },   null,   false);
        builder.AddConstant(withRest);
        builder.AddConstant(withoutRest);
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Return);
        Chunk original = builder.Build();

        Chunk restored = RoundTrip(original);

        Assert.Equal(2, restored.Constants.Length);

        var r1 = Assert.IsType<DestructureMetadata>(restored.Constants[0].AsObj);
        Assert.Equal("dict",   r1.Kind);
        Assert.Equal(new[] { "a", "b" }, r1.Names);
        Assert.Equal("rest",  r1.RestName);
        Assert.True(r1.IsConst);

        var r2 = Assert.IsType<DestructureMetadata>(restored.Constants[1].AsObj);
        Assert.Equal("arr",            r2.Kind);
        Assert.Equal(new[] { "first" }, r2.Names);
        Assert.Null(r2.RestName);
        Assert.False(r2.IsConst);
    }

    [Fact]
    public void RoundTrip_RetryMetadata_Preserved()
    {
        var builder = new ChunkBuilder { Optimize = false };
        var meta = new RetryMetadata(2, true, true, false);
        builder.AddConstant(meta);
        builder.Emit(OpCode.Null);
        builder.Emit(OpCode.Return);
        Chunk original = builder.Build();

        Chunk restored = RoundTrip(original);

        Assert.Single(restored.Constants);
        var result = Assert.IsType<RetryMetadata>(restored.Constants[0].AsObj);
        Assert.Equal(2, result.OptionCount);
        Assert.True(result.HasUntilClause);
        Assert.True(result.HasOnRetryClause);
        Assert.False(result.OnRetryIsReference);
    }

    // =========================================================================
    // Nested chunk debug stripping
    // =========================================================================

    [Fact]
    public void RoundTrip_StripDebugInfo_NestedChunksAlsoStripped()
    {
        // Build inner function with debug info
        var innerBuilder = new ChunkBuilder
        {
            Name       = "inner",
            Arity      = 1,
            MinArity   = 1,
            LocalCount = 2,
            LocalNames = ["a", "b"],
            LocalIsConst = [false, true],
            Optimize   = false,
        };
        innerBuilder.AddSourceMapping(0, new SourceSpan("test.stash", 5, 1, 5, 10));
        innerBuilder.Emit(OpCode.Return);
        Chunk innerChunk = innerBuilder.Build();

        // Build outer chunk containing the inner as a constant
        var outerBuilder = new ChunkBuilder
        {
            LocalCount   = 1,
            LocalNames   = ["x"],
            LocalIsConst = [false],
            Optimize     = false,
        };
        outerBuilder.AddSourceMapping(0, new SourceSpan("test.stash", 1, 1, 1, 10));
        ushort idx = outerBuilder.AddConstant(innerChunk);
        outerBuilder.Emit(OpCode.Const, idx);
        Chunk original = outerBuilder.Build();

        // Round-trip with debug info STRIPPED
        Chunk result = RoundTrip(original, includeDebugInfo: false);

        // Outer chunk should have no debug info
        Assert.Null(result.LocalNames);
        Assert.Null(result.LocalIsConst);
        Assert.Equal(0, result.SourceMap.Count);

        // Nested chunk should also have no debug info
        var nested = Assert.IsType<Chunk>(result.Constants[0].AsObj);
        Assert.Equal("inner", nested.Name);
        Assert.Equal(1, nested.Arity);
        Assert.Null(nested.LocalNames);
        Assert.Null(nested.LocalIsConst);
        Assert.Equal(0, nested.SourceMap.Count);
    }

    // =========================================================================
    // Safety limit tests
    // =========================================================================

    [Fact]
    public void Read_OversizedStringConstant_Throws()
    {
        // Build a valid .stashc header from a real write
        var builder = new ChunkBuilder { Optimize = false };
        builder.Emit(OpCode.Null);
        Chunk headerChunk = builder.Build();

        using var headerStream = new MemoryStream();
        BytecodeWriter.Write(headerStream, headerChunk);
        byte[] validHeader = headerStream.ToArray()[..32]; // 32-byte header only

        // Hand-craft chunk body with an oversized string constant
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        // Write valid header
        writer.Write(validHeader);

        // Chunk: null name
        writer.Write((ushort)0xFFFF);
        // Arity, MinArity, LocalCount, GlobalSlotCount
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        // Flags
        writer.Write((byte)0);
        // Code: 0 bytes
        writer.Write((uint)0);
        // Constants: 1 constant
        writer.Write((ushort)1);
        // Tag 4 = string, with oversized length
        writer.Write((byte)4);
        writer.Write(0x7FFFFFFF); // ~2 GB
        writer.Flush();

        ms.Position = 0;
        Assert.Throws<InvalidDataException>(() => BytecodeReader.Read(ms));
    }
}
