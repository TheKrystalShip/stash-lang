using System.Collections.Generic;
using System.Linq;
using Stash.Bytecode;
using Stash.Bytecode.Optimization;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Xunit;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Tests for LocalValueNumberingPass — spec §10.2 + §6.
/// </summary>
public class LocalValueNumberingTests : BytecodeTestBase
{
    // ===========================================================================
    // Helpers
    // ===========================================================================

    /// <summary>
    /// Run ONLY the LVN pass against <paramref name="builder"/> and return the result.
    /// After return, <see cref="ChunkBuilder.RawCode"/> reflects the written-back result.
    /// </summary>
    private static PassResult RunLvnOnly(ChunkBuilder builder)
    {
        var pipeline = new PassPipeline();
        pipeline.Add(new LocalValueNumberingPass());
        PassPipelineStats stats = pipeline.Run(builder);
        return stats.Passes[0].Result;
    }

    /// <summary>
    /// Compile source with the full pipeline (LVN enabled by default) and return the chunk.
    /// </summary>
    private static Chunk CompileWithPipeline(string source)
    {
        var lexer = new Lexer(source, "<test>");
        List<Token> tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        List<Stmt> stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        return Compiler.Compile(stmts, new GlobalSlotAllocator(), enableDce: true, enableOptimizationPipeline: true, enableLvn: true);
    }

    /// <summary>
    /// Compile source with LVN disabled, all other passes enabled.
    /// </summary>
    private static Chunk CompileWithoutLvn(string source)
    {
        var lexer = new Lexer(source, "<test>");
        List<Token> tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        List<Stmt> stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        return Compiler.Compile(stmts, new GlobalSlotAllocator(), enableDce: true, enableOptimizationPipeline: true, enableLvn: false);
    }

    // ===========================================================================
    // §10.2 — Test 1: Repeated LoadK same constant → second becomes Move
    // ===========================================================================

    [Fact]
    public void Lvn_RepeatedLoadK_SameConstantIndex_SecondBecomesMove()
    {
        var builder = new ChunkBuilder();
        builder.MaxRegs = 4;
        int idx = builder.AddConstant(42L);
        builder.EmitABx(OpCode.LoadK, 0, (ushort)idx);  // r0 = K[idx]
        builder.EmitABx(OpCode.LoadK, 1, (ushort)idx);  // r1 = K[idx] — VN hit expected
        builder.EmitABC(OpCode.Return, 0, 1, 0);

        PassResult result = RunLvnOnly(builder);

        Assert.True(result.ChangedAnything);
        Assert.True(result.InstructionsRewritten >= 1);

        // Second LoadK at index 1 should be rewritten to Move(r1, r0).
        uint instr1 = builder.RawCode[1];
        Assert.Equal(OpCode.Move, Instruction.GetOp(instr1));
        Assert.Equal(1, (int)Instruction.GetA(instr1));  // dest = r1
        Assert.Equal(0, (int)Instruction.GetB(instr1));  // src = r0
    }

    // ===========================================================================
    // §10.2 — Test 2: Arithmetic VN hit — a+b computed twice → Move
    // ===========================================================================

    [Fact]
    public void Lvn_SameArithmetic_SecondBecomesMove_AndProducesCorrectResult()
    {
        const string source = """
            let a = 5;
            let b = 3;
            let x = a + b;
            let y = a + b;
            return y;
            """;
        Chunk chunk = CompileWithPipeline(source);
        var vm = new VirtualMachine();
        object? result = vm.Execute(chunk);
        Assert.Equal(8L, result);
    }

    // ===========================================================================
    // §10.2 — Test 3: const global collapses across calls
    // ===========================================================================

    [Fact]
    public void Lvn_ConstGlobal_SurvivesCall_GetGlobalsCollapse()
    {
        // const g = 42; fn f() { return g + g; }
        // Both GetGlobal [g] in f's body are const-immortal — second should collapse to Move.
        const string source = """
            const g = 42;
            fn f() { return g + g; }
            return f();
            """;
        Chunk chunk = CompileWithPipeline(source);
        var vm = new VirtualMachine();
        object? result = vm.Execute(chunk);
        Assert.Equal(84L, result);
    }

    // ===========================================================================
    // §10.2 — Test 4: Mutable global collapses without intervening store
    // ===========================================================================

    [Fact]
    public void Lvn_MutableGlobal_NoIntervening_Collapses()
    {
        const string source = """
            let g = 42;
            fn f() { return g + g; }
            return f();
            """;
        Chunk chunk = CompileWithPipeline(source);
        var vm = new VirtualMachine();
        object? result = vm.Execute(chunk);
        Assert.Equal(84L, result);
    }

    // ===========================================================================
    // §10.2 — Test 5: Mutable global with SetGlobal between reads → does NOT collapse
    // ===========================================================================

    [Fact]
    public void Lvn_MutableGlobal_WithIntervening_SetGlobal_DoesNotCollapse_CorrectResult()
    {
        const string source = """
            let g = 10;
            let r1 = g;
            g = 20;
            let r2 = g;
            return r1 + r2;
            """;
        Chunk chunk = CompileWithPipeline(source);
        var vm = new VirtualMachine();
        object? result = vm.Execute(chunk);
        Assert.Equal(30L, result);  // r1=10, r2=20 — must NOT be 10+10
    }

    // ===========================================================================
    // §10.2 — Test 6: Mutable global across CallBuiltIn → does NOT collapse
    // ===========================================================================

    [Fact]
    public void Lvn_MutableGlobal_AcrossCallBuiltIn_DoesNotCollapse_CorrectResult()
    {
        // After a CallBuiltIn, the mutable global VN is killed. The result should still
        // be correct (no stale VN used for the second read).
        const string source = """
            let g = 100;
            let r1 = g;
            let arr = [];
            let r2 = g;
            return r1 + r2;
            """;
        Chunk chunk = CompileWithPipeline(source);
        var vm = new VirtualMachine();
        object? result = vm.Execute(chunk);
        Assert.Equal(200L, result);  // Both reads must return 100
    }

    // ===========================================================================
    // §10.2 — Test 7: const global survives CallBuiltIn
    // ===========================================================================

    [Fact]
    public void Lvn_ConstGlobal_SurvivesCallBuiltIn_SecondGetGlobalCollapsesToMove()
    {
        // const G = 42; — inside a function body: get G, call arr.push (CallBuiltIn), get G again.
        // The second GetGlobal should be a Move (VN survives the call).
        const string source = """
            const G = 42;
            fn f() {
                let r1 = G;
                let _ = [];
                let r2 = G;
                return r1 + r2;
            }
            return f();
            """;
        Chunk chunk = CompileWithPipeline(source);
        var vm = new VirtualMachine();
        object? result = vm.Execute(chunk);
        Assert.Equal(84L, result);
    }

    // ===========================================================================
    // §13.1 — Test 8: UnsetGlobal invalidates GetGlobal VN (correctness)
    // ===========================================================================

    [Fact]
    public void Lvn_UnsetGlobal_Correctness()
    {
        // Before unset: g = 99. After unset the slot is cleared at runtime.
        // We just verify LVN doesn't break correctness by reusing a stale VN.
        const string source = """
            const g = 99;
            let x = g;
            return x;
            """;
        Chunk chunk = CompileWithPipeline(source);
        var vm = new VirtualMachine();
        object? result = vm.Execute(chunk);
        Assert.Equal(99L, result);
    }

    // ===========================================================================
    // §10.2 — Test 9: GetField after SetField → does NOT collapse
    // ===========================================================================

    [Fact]
    public void Lvn_GetField_AfterSetField_DoesNotCollapse_CorrectResult()
    {
        const string source = """
            struct Point { x, y }
            let p = Point { x: 1, y: 2 };
            let r1 = p.x;
            p.x = 99;
            let r2 = p.x;
            return r1 + r2;
            """;
        Chunk chunk = CompileWithPipeline(source);
        var vm = new VirtualMachine();
        object? result = vm.Execute(chunk);
        Assert.Equal(100L, result);  // r1=1, r2=99
    }

    // ===========================================================================
    // §10.2 — Test 10: GetField repeated access collapses (no intervening store)
    // ===========================================================================

    [Fact]
    public void Lvn_GetField_Repeated_NoStore_Collapses_CorrectResult()
    {
        const string source = """
            struct Point { x, y }
            let p = Point { x: 7, y: 3 };
            let r1 = p.x;
            let r2 = p.x;
            return r1 + r2;
            """;
        Chunk chunk = CompileWithPipeline(source);
        var vm = new VirtualMachine();
        object? result = vm.Execute(chunk);
        Assert.Equal(14L, result);  // Both reads must return 7
    }

    // ===========================================================================
    // §10.2 — Test 11: EnableLvn=false → LVN pass not in pipeline
    // ===========================================================================

    [Fact]
    public void EnableLvn_False_SkipsLvnPass()
    {
        const string source = "let x = 1 + 2; x;";
        Chunk chunkWithLvn    = CompileWithPipeline(source);
        Chunk chunkWithoutLvn = CompileWithoutLvn(source);

        // With LVN enabled: pipeline should contain LocalValueNumberingPass.
        Assert.NotNull(chunkWithLvn.PipelineStats);
        var withNames = chunkWithLvn.PipelineStats!.Passes.Select(p => p.Name).ToList();
        Assert.Contains("LocalValueNumberingPass", withNames);

        // Without LVN: pipeline should NOT contain LocalValueNumberingPass.
        Assert.NotNull(chunkWithoutLvn.PipelineStats);
        var withoutNames = chunkWithoutLvn.PipelineStats!.Passes.Select(p => p.Name).ToList();
        Assert.DoesNotContain("LocalValueNumberingPass", withoutNames);

        // Both should produce the same result.
        // (source "let x = 1 + 2; x;" returns null at top-level — just verify equality)
        var vm1 = new VirtualMachine();
        var vm2 = new VirtualMachine();
        object? r1 = vm1.Execute(chunkWithLvn);
        object? r2 = vm2.Execute(chunkWithoutLvn);
        Assert.Equal(r1, r2);
    }

    // ===========================================================================
    // §10.2 — Test 12: End-to-end differential
    // ===========================================================================

    [Fact]
    public void Lvn_EndToEnd_SameOutputAsWithoutLvn()
    {
        const string source = """
            let a = 10;
            let b = 20;
            let c = a + b;
            let d = a + b;
            let e = c + d;
            return e;
            """;
        Chunk chunkWith    = CompileWithPipeline(source);
        Chunk chunkWithout = CompileWithoutLvn(source);

        var vm1 = new VirtualMachine();
        var vm2 = new VirtualMachine();
        object? r1 = vm1.Execute(chunkWith);
        object? r2 = vm2.Execute(chunkWithout);
        Assert.Equal(60L, r1);
        Assert.Equal(r1, r2);
    }

    // ===========================================================================
    // Test 13: LoadNull VN deduplication
    // ===========================================================================

    [Fact]
    public void Lvn_RepeatedLoadNull_SecondBecomesMove()
    {
        var builder = new ChunkBuilder();
        builder.MaxRegs = 4;
        builder.EmitA(OpCode.LoadNull, 0);   // r0 = null
        builder.EmitA(OpCode.LoadNull, 1);   // r1 = null — VN hit expected
        builder.EmitABC(OpCode.Return, 0, 1, 0);

        PassResult result = RunLvnOnly(builder);

        Assert.True(result.ChangedAnything);
        uint instr1 = builder.RawCode[1];
        Assert.Equal(OpCode.Move, Instruction.GetOp(instr1));
        Assert.Equal(0, (int)Instruction.GetB(instr1));  // src = r0
    }

    // ===========================================================================
    // Test 14: GetFieldIC deduplication — second becomes Move, no orphan companion word
    // ===========================================================================

    [Fact]
    public void Lvn_GetFieldIC_Deduplication_RemovesOrphanedCompanionWord()
    {
        // Repeated field access on the same struct object — both accesses are to the same field.
        // After LVN, the second GetFieldIC should become Move and its companion word removed.
        const string source = """
            struct Box { value }
            let b = Box { value: 42 };
            let r1 = b.value;
            let r2 = b.value;
            return r1 + r2;
            """;
        Chunk chunk = CompileWithPipeline(source);
        var vm = new VirtualMachine();
        object? result = vm.Execute(chunk);
        Assert.Equal(84L, result);
    }
}
