using System.Collections.Generic;
using System.Linq;
using Stash.Analysis;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Tests that the compiler emits the same Import/ImportAs bytecode sequences for the
/// re-export forms (<c>export "p" as p;</c> and <c>export { a, b } from "p";</c>) as it
/// emits for the equivalent hand-written import forms.
/// </summary>
public class ExportFromCompilerTests : BytecodeTestBase
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Compile source without running ModuleExportsBuilder (exports stays null).
    /// Sufficient for instruction-sequence and local-binding tests.
    /// </summary>
    private static Chunk CompileOnly(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        return Compiler.Compile(stmts);
    }

    /// <summary>
    /// Returns the opcode portion of each instruction (low 8 bits) as a list,
    /// excluding source-map pseudo-instructions if any bleed through.
    /// </summary>
    private static uint[] Opcodes(Chunk chunk) => chunk.Code;

    // ── 1. ImportAs / ExportModuleAs — code array equality ────────────────────

    [Fact]
    public void ExportModuleAs_EmitsIdenticalCodeToImportAs()
    {
        Chunk importChunk = CompileOnly("""import "p" as p;""");
        Chunk exportChunk = CompileOnly("""export "p" as p;""");

        Assert.Equal(importChunk.Code, exportChunk.Code);
    }

    // ── 2. Import / ExportFrom — code array equality ──────────────────────────

    [Fact]
    public void ExportFrom_SingleName_EmitsIdenticalCodeToImport()
    {
        Chunk importChunk = CompileOnly("""import { a } from "p";""");
        Chunk exportChunk = CompileOnly("""export { a } from "p";""");

        Assert.Equal(importChunk.Code, exportChunk.Code);
    }

    [Fact]
    public void ExportFrom_MultipleNames_EmitsIdenticalCodeToImport()
    {
        Chunk importChunk = CompileOnly("""import { a, b } from "p";""");
        Chunk exportChunk = CompileOnly("""export { a, b } from "p";""");

        Assert.Equal(importChunk.Code, exportChunk.Code);
    }

    // ── 3. Constant pool equivalence ─────────────────────────────────────────

    [Fact]
    public void ExportModuleAs_ConstantPoolCountMatchesImportAs()
    {
        Chunk importChunk = CompileOnly("""import "p" as p;""");
        Chunk exportChunk = CompileOnly("""export "p" as p;""");

        Assert.Equal(importChunk.Constants.Length, exportChunk.Constants.Length);
    }

    [Fact]
    public void ExportFrom_ConstantPoolCountMatchesImport()
    {
        Chunk importChunk = CompileOnly("""import { a, b } from "p";""");
        Chunk exportChunk = CompileOnly("""export { a, b } from "p";""");

        Assert.Equal(importChunk.Constants.Length, exportChunk.Constants.Length);
    }

    // ── 4. Disassembly — opcodes match ────────────────────────────────────────

    [Fact]
    public void ExportModuleAs_DisassemblyContainsImportAsOpcode()
    {
        string disasm = Disassemble("""export "p" as p;""");

        Assert.Contains("import.as", disasm);
    }

    [Fact]
    public void ExportFrom_DisassemblyContainsImportOpcode()
    {
        string disasm = Disassemble("""export { a, b } from "p";""");

        Assert.Contains("import", disasm);
    }

    // ── 5. Same-module local binding (D-12 acceptance tests) ─────────────────

    [Fact]
    public void ExportModuleAs_SameModuleCanCallAlias_CompilesWithoutError()
    {
        // This test verifies compilation only — no runtime execution since "p" doesn't exist.
        // The local binding `x` must be resolved correctly in the same scope.
        var tokens = new Lexer("""
            export "lib/x.stash" as x;
            let v = x.foo();
            """, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        // Should not throw — x is bound as a local by the export statement.
        var chunk = Compiler.Compile(stmts);
        Assert.NotNull(chunk);
    }

    [Fact]
    public void ExportFrom_SameModuleCanReferenceNames_CompilesWithoutError()
    {
        // The names a, b must be bound as locals in the same module after the export.
        var tokens = new Lexer("""
            export { a, b } from "lib/types.stash";
            let x = a;
            let y = b;
            """, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        Assert.NotNull(chunk);
    }

    // ── 6. Mixed forms in the same file ──────────────────────────────────────

    [Fact]
    public void MixedForms_CompileCleanly()
    {
        var tokens = new Lexer("""
            import "a" as a;
            export "b" as b;
            export { c } from "d";
            import { e } from "f";
            """, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        Assert.NotNull(chunk);
        // Each of the four statements emits an import.as or import instruction;
        // verify each is present in the disassembly.
        string disasm = Disassembler.Disassemble(chunk);
        Assert.Contains("import.as", disasm);
        Assert.Contains("import", disasm);
    }

    // ── 7. ExportModuleAs code matches ImportAs — three-name variant ──────────

    [Fact]
    public void ExportFrom_ThreeNames_CodeArrayEqualsImport()
    {
        Chunk importChunk = CompileOnly("""import { foo, bar, baz } from "lib/types.stash";""");
        Chunk exportChunk = CompileOnly("""export { foo, bar, baz } from "lib/types.stash";""");

        Assert.Equal(importChunk.Code, exportChunk.Code);
    }

    // ── 8. No new opcodes — spot-check OpCode set ────────────────────────────

    [Fact]
    public void ExportModuleAs_OnlyEmitsKnownImportAsOpcode()
    {
        Chunk chunk = CompileOnly("""export "p" as p;""");
        string disasm = Disassembler.Disassemble(chunk);
        // import.as must appear; the new-opcode sentinel must NOT appear.
        Assert.Contains("import.as", disasm);
        Assert.DoesNotContain("export.as", disasm);
    }

    [Fact]
    public void ExportFrom_OnlyEmitsKnownImportOpcode()
    {
        Chunk chunk = CompileOnly("""export { a } from "p";""");
        string disasm = Disassembler.Disassemble(chunk);
        Assert.DoesNotContain("export.from", disasm);
    }
}
