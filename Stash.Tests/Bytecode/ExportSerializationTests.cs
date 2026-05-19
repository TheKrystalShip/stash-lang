using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Stash.Analysis;
using Stash.Bytecode;
using Stash.Core.Resolution;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Tests for <see cref="Chunk.Exports"/>, <see cref="ModuleExportsBuilder"/>, and the
/// bytecode serializer round-trip of the export set.
/// </summary>
public class ExportSerializationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Chunk CompileWithExports(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        var diagnostics = new List<SemanticDiagnostic>();
        var exports = ModuleExportsBuilder.Build(stmts, diagnostics);
        return Compiler.Compile(stmts, exports: exports);
    }

    private static Chunk RoundTrip(Chunk original, bool includeDebugInfo = true)
    {
        using var ms = new MemoryStream();
        BytecodeWriter.Write(ms, original, includeDebugInfo: includeDebugInfo);
        ms.Position = 0;
        return BytecodeReader.Read(ms);
    }

    // ── Compile_ExportFn_AttachesToTopChunkExports ────────────────────────────

    [Fact]
    public void Compile_ExportFn_AttachesToTopChunkExports()
    {
        var chunk = CompileWithExports("export fn diff(a, b) {}");

        Assert.NotNull(chunk.Exports);
        Assert.Contains("diff", chunk.Exports!.Names);
    }

    [Fact]
    public void Compile_ExportConst_AttachesToTopChunkExports()
    {
        var chunk = CompileWithExports("""export const VERSION = "1.0";""");

        Assert.NotNull(chunk.Exports);
        Assert.Contains("VERSION", chunk.Exports!.Names);
    }

    [Fact]
    public void Compile_ExportBlock_AttachesToTopChunkExports()
    {
        var chunk = CompileWithExports("""
            fn diff(a, b) {}
            export { diff };
            """);

        Assert.NotNull(chunk.Exports);
        Assert.Contains("diff", chunk.Exports!.Names);
    }

    [Fact]
    public void Compile_NoExportAnnotations_ChunkExportsNamesEmpty()
    {
        var chunk = CompileWithExports("""
            fn helper() {}
            const VERSION = "1.0";
            """);

        // When there are no export annotations, ModuleExportsBuilder returns Empty.
        Assert.NotNull(chunk.Exports);
        Assert.Empty(chunk.Exports!.Names);
    }

    [Fact]
    public void Compile_NestedFn_ChunkExportsIsNull()
    {
        // Compile a script with a nested function; find the nested chunk in the constants.
        var chunk = CompileWithExports("export fn outer() { fn inner() {} }");

        // Top-level chunk has exports.
        Assert.NotNull(chunk.Exports);

        // Find the nested VMFunction chunk in the constant pool — it must have null Exports.
        Chunk? innerChunk = null;
        foreach (var constant in chunk.Constants)
        {
            if (constant.AsObj is Chunk c && c.Name is not null)
            {
                innerChunk = c;
                break;
            }
        }

        // The nested chunk (if found) should not have Exports set.
        if (innerChunk is not null)
        {
            Assert.Null(innerChunk.Exports);
        }
    }

    // ── Serialization round-trip ──────────────────────────────────────────────

    [Fact]
    public void Serialization_RoundTripsExportSet_WithExports()
    {
        var chunk = CompileWithExports("export fn diff(a, b) {}");

        var restored = RoundTrip(chunk);

        Assert.NotNull(restored.Exports);
        Assert.Contains("diff", restored.Exports!.Names);
    }

    [Fact]
    public void Serialization_RoundTripsExportSet_MultipleNames()
    {
        var chunk = CompileWithExports("""
            export fn diff(a, b) {}
            export const VERSION = "1.0";
            export struct Point { x: int, y: int }
            """);

        var restored = RoundTrip(chunk);

        Assert.NotNull(restored.Exports);
        Assert.Contains("diff", restored.Exports!.Names);
        Assert.Contains("VERSION", restored.Exports.Names);
        Assert.Contains("Point", restored.Exports.Names);
        Assert.Equal(3, restored.Exports.Names.Count);
    }

    [Fact]
    public void Serialization_RoundTripsExportSet_EmptyExportBlock()
    {
        // export {} produces empty Names — indistinguishable from no annotations
        var chunk = CompileWithExports("export { };");

        var restored = RoundTrip(chunk);

        Assert.NotNull(restored.Exports);
        Assert.Empty(restored.Exports!.Names);
    }

    [Fact]
    public void Serialization_RoundTripsExportSet_NoAnnotations()
    {
        var chunk = CompileWithExports("fn helper() {}");

        var restored = RoundTrip(chunk);

        Assert.NotNull(restored.Exports);
        Assert.Empty(restored.Exports!.Names);
    }

    [Fact]
    public void Serialization_RoundTripsExportSet_WithDebugInfoDisabled()
    {
        var chunk = CompileWithExports("export fn diff(a, b) {}");

        var restored = RoundTrip(chunk, includeDebugInfo: false);

        Assert.NotNull(restored.Exports);
        Assert.Contains("diff", restored.Exports!.Names);
    }

    [Fact]
    public void Serialization_NullExports_RoundTripsAsNull()
    {
        // A chunk compiled without passing exports (Exports = null) round-trips to Exports = null.
        var tokens = new Lexer("fn helper() {}", "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        // Compile WITHOUT exports parameter — Exports stays null
        var chunk = Compiler.Compile(stmts);

        Assert.Null(chunk.Exports);

        var restored = RoundTrip(chunk);

        Assert.Null(restored.Exports);
    }

    // ── ModuleExportsBuilder ──────────────────────────────────────────────────

    [Fact]
    public void ModuleExportsBuilder_NoAnnotations_NamesEmpty()
    {
        var tokens = new Lexer("fn helper() {}", "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        var diagnostics = new List<SemanticDiagnostic>();

        var exports = ModuleExportsBuilder.Build(stmts, diagnostics);

        Assert.Empty(exports.Names);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ModuleExportsBuilder_ExportFn_CollectsName()
    {
        var tokens = new Lexer("export fn diff(a, b) {}", "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        var diagnostics = new List<SemanticDiagnostic>();

        var exports = ModuleExportsBuilder.Build(stmts, diagnostics);

        Assert.Contains("diff", exports.Names);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ModuleExportsBuilder_InvalidExport_ProducesDiagnostics()
    {
        // let binding in export block should produce SA0805
        var tokens = new Lexer("""
            let counter = 0;
            export { counter };
            """, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        var diagnostics = new List<SemanticDiagnostic>();

        ModuleExportsBuilder.Build(stmts, diagnostics);

        Assert.Contains(diagnostics, d => d.Code == "SA0805");
    }

    // ── ModuleExports.Create factory ──────────────────────────────────────────

    [Fact]
    public void ModuleExports_Create_SetsNamesCorrectly()
    {
        var names = ImmutableHashSet.Create("foo", "bar");
        var exports = Stash.Core.Resolution.ModuleExports.Create(names);

        Assert.Contains("foo", exports.Names);
        Assert.Contains("bar", exports.Names);
    }

    [Fact]
    public void ModuleExports_Empty_NamesEmpty()
    {
        Assert.Empty(Stash.Core.Resolution.ModuleExports.Empty.Names);
    }
}
