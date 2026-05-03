using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Xunit;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Tests for IC slot compaction (Phase 4A of the basic-block optimizer spec §9.4).
/// When <see cref="LocalValueNumberingPass"/> rewrites a <c>GetFieldIC</c> to <c>Move</c>
/// and removes the orphaned companion word, the post-pipeline compaction step must shrink
/// the IC slot table to remove the now-unreferenced entries.
/// </summary>
public class IcSlotCompactionTests : BytecodeTestBase
{
    private static Chunk Compile(string source, bool enablePipeline)
    {
        var lexer = new Lexer(source, "<test>");
        List<Token> tokens = lexer.ScanTokens();
        List<Stmt> stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        return Compiler.Compile(stmts, enableDce: true, enableOptimizationPipeline: enablePipeline);
    }

    /// Source: the same field `.v` accessed twice in one expression (`b.v + b.v`).
    /// Without optimisation: 2 GetFieldIC instructions → 2 IC slots.
    /// With LVN: the second `b.v` is a VN-hit → rewritten to Move, companion word removed.
    /// After IC compaction: 1 slot survives.
    private const string RepeatedFieldAccess = """
        struct Box { v }
        let b = Box { v: 7 };
        return b.v + b.v;
        """;

    // Expected result: 7 + 7 = 14.

    [Fact]
    public void IcSlotCompaction_DuplicateFieldAccess_SlotCountDrops()
    {
        // Without pipeline: each GetFieldIC gets its own IC slot → 2 total.
        Chunk without = Compile(RepeatedFieldAccess, enablePipeline: false);
        int slotsWithout = without.ICSlots?.Length ?? 0;

        // With pipeline: LVN deduplicates b.v → 1 IC slot orphaned → 1 survives.
        Chunk with = Compile(RepeatedFieldAccess, enablePipeline: true);
        int slotsWith = with.ICSlots?.Length ?? 0;

        Assert.True(slotsWithout > 0, "Expected at least one IC slot without optimisation.");
        Assert.True(slotsWith < slotsWithout,
            $"IC slot count should drop after compaction: before={slotsWithout}, after={slotsWith}.");
    }

    [Fact]
    public void IcSlotCompaction_DuplicateFieldAccess_CorrectResult()
    {
        // Semantics must be preserved: b.v=7 → 7+7=14.
        Chunk chunk = Compile(RepeatedFieldAccess, enablePipeline: true);
        object? result = new VirtualMachine().Execute(chunk);
        Assert.Equal(14L, result);
    }

    [Fact]
    public void IcSlotCompaction_UniqueFieldAccesses_NoCompaction()
    {
        // All field accesses are distinct — HasOrphanedICSlots stays false, no compaction.
        // Slot counts should be equal (pipeline may not eliminate any IC slots here).
        const string src = """
            struct Point { x, y }
            let p = Point { x: 3, y: 4 };
            return p.x + p.y;
            """;
        Chunk without = Compile(src, enablePipeline: false);
        Chunk with    = Compile(src, enablePipeline: true);

        // IC slot count must not grow after optimisation.
        int slotsWithout = without.ICSlots?.Length ?? 0;
        int slotsWith    = with.ICSlots?.Length    ?? 0;
        Assert.True(slotsWith <= slotsWithout,
            $"IC slot count must not grow: before={slotsWithout}, after={slotsWith}.");
    }

    [Fact]
    public void IcSlotCompaction_ConstantIndicesPreserved()
    {
        // After compaction the surviving ICSlot.ConstantIndex must still map to the
        // correct field name.  A wrong mapping would cause the VM to resolve the wrong field.
        // Verify by executing: b.v=7 → 7+7=14.
        Chunk chunk = Compile(RepeatedFieldAccess, enablePipeline: true);
        object? result = new VirtualMachine().Execute(chunk);
        Assert.Equal(14L, result);
    }

    [Fact]
    public void IcSlotCompaction_HighlyRedundantAccess_MaximalReduction()
    {
        // 5 accesses to the same field: 1 GetFieldIC survives, 4 become Move.
        // Without optimisation: 5 IC slots; after LVN + compaction: 1 IC slot.
        const string src = """
            struct Box { v }
            let b = Box { v: 42 };
            let s = b.v + b.v + b.v + b.v + b.v;
            return s;
            """;
        Chunk without = Compile(src, enablePipeline: false);
        Chunk with    = Compile(src, enablePipeline: true);

        int slotsWithout = without.ICSlots?.Length ?? 0;
        int slotsWith    = with.ICSlots?.Length    ?? 0;

        Assert.True(slotsWithout >= 2, $"Expected ≥2 IC slots without optimisation, got {slotsWithout}.");
        Assert.True(slotsWith < slotsWithout,
            $"Maximal dedup: slot count should drop from {slotsWithout} to fewer, got {slotsWith}.");

        // Correctness: 42 * 5 = 210.
        object? result = new VirtualMachine().Execute(with);
        Assert.Equal(210L, result);
    }
}
