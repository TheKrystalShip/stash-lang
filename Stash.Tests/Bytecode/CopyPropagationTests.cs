using System.Collections.Generic;
using System.Linq;
using Stash.Bytecode;
using Stash.Bytecode.Optimization;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Tests for CopyPropagationPass — spec §10.3.
/// </summary>
public class CopyPropagationTests : BytecodeTestBase
{
    // ===========================================================================
    // Helpers
    // ===========================================================================

    /// <summary>
    /// Build a pipeline containing only CopyPropagationPass, run it against
    /// <paramref name="builder"/>, and return the pass result.
    /// After return, <see cref="ChunkBuilder.RawCode"/> reflects the written-back result.
    /// </summary>
    private static PassResult RunCopyPropOnly(ChunkBuilder builder)
    {
        var pipeline = new PassPipeline();
        pipeline.Add(new CopyPropagationPass());
        PassPipelineStats stats = pipeline.Run(builder);
        return stats.Passes[0].Result;
    }

    // ===========================================================================
    // §10.3 — Test 1: Basic copy propagation
    // ===========================================================================

    [Fact]
    public void CopyProp_BasicMove_PropagatesToConsumer()
    {
        // Move r0=r1; Add r2=r0,r3 → after copy-prop: Add r2=r1,r3
        var builder = new ChunkBuilder();
        builder.MaxRegs = 4;
        builder.EmitABC(OpCode.Move, 0, 1, 0);    // Move r0 = r1
        builder.EmitABC(OpCode.Add,  2, 0, 3);    // Add  r2 = r0, r3
        builder.EmitABC(OpCode.Return, 0, 0, 0);  // Return (sentinel)

        PassResult result = RunCopyPropOnly(builder);

        Assert.True(result.ChangedAnything);
        Assert.True(result.InstructionsRewritten >= 1);

        // Add instruction is now at index 1 in the lowered code.
        uint addInstr = builder.RawCode[1];
        Assert.Equal(OpCode.Add, Instruction.GetOp(addInstr));
        Assert.Equal(2, (int)Instruction.GetA(addInstr)); // dest unchanged
        Assert.Equal(1, (int)Instruction.GetB(addInstr)); // r0 → r1
        Assert.Equal(3, (int)Instruction.GetC(addInstr)); // r3 unchanged
    }

    // ===========================================================================
    // §10.3 — Test 2: Chain broken by overwrite of the source register
    // ===========================================================================

    [Fact]
    public void CopyProp_ChainBroken_WhenSourceOverwritten()
    {
        // Move r0=r1; Move r1=r4; Add r2=r0,r3
        // After "Move r1=r4", copyOf[r0]=r1 is invalidated (value r1 was overwritten).
        // So Add should still read r0, not r1.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 5;
        builder.EmitABC(OpCode.Move, 0, 1, 0);    // Move r0 = r1 → copyOf[r0] = r1
        builder.EmitABC(OpCode.Move, 1, 4, 0);    // Move r1 = r4 → kills copyOf[r0] (r1 overwritten)
        builder.EmitABC(OpCode.Add,  2, 0, 3);    // Add  r2 = r0, r3 — r0 has no copy
        builder.EmitABC(OpCode.Return, 0, 0, 0);

        PassResult result = RunCopyPropOnly(builder);

        // Add is at index 2.
        uint addInstr = builder.RawCode[2];
        Assert.Equal(OpCode.Add, Instruction.GetOp(addInstr));
        Assert.Equal(0, (int)Instruction.GetB(addInstr)); // still r0, NOT r1
        Assert.Equal(3, (int)Instruction.GetC(addInstr)); // r3 unchanged
    }

    // ===========================================================================
    // §10.3 — Test 3: Block boundary resets copy map
    // ===========================================================================

    [Fact]
    public void CopyProp_BlockBoundary_CopyDoesNotCrossBlocks()
    {
        // Block 0: Move r0=r1; Jmp +0   (Jmp terminates the block)
        // Block 1: Add r2=r0,r3          (fresh copyOf = {}; r0 is not in the map)
        var builder = new ChunkBuilder();
        builder.MaxRegs = 4;
        builder.EmitABC(OpCode.Move, 0, 1, 0);    // [0] Move r0 = r1
        builder.EmitAsBx(OpCode.Jmp, 0, 0);       // [1] Jmp +0  (block terminator; target = [2])
        builder.EmitABC(OpCode.Add,  2, 0, 3);    // [2] Add r2 = r0, r3  ← block 2
        builder.EmitABC(OpCode.Return, 0, 0, 0);  // [3] Return

        PassResult result = RunCopyPropOnly(builder);

        // Add is still at index 2 in the lowered code (no instructions removed/added).
        uint addInstr = builder.RawCode[2];
        Assert.Equal(OpCode.Add, Instruction.GetOp(addInstr));
        Assert.Equal(0, (int)Instruction.GetB(addInstr)); // NOT rewritten — block boundary
        Assert.Equal(3, (int)Instruction.GetC(addInstr)); // r3 unchanged
    }

    // ===========================================================================
    // §10.3 — Test 4: Companion word preserved; GetFieldIC R(B) is rewritten
    // ===========================================================================

    [Fact]
    public void CopyProp_CompanionWordPreserved_GetFieldICRewritten()
    {
        // Move r1=r5; GetFieldIC r0=r1.fieldConst [companion: icSlot]
        // After copy-prop: GetFieldIC r0=r5.fieldConst (B: r1 → r5)
        // Companion word must be unchanged.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 6;

        ushort fieldConst = builder.AddConstant("myField");
        ushort icSlot = builder.AllocateICSlot(0);

        builder.EmitABC(OpCode.Move, 1, 5, 0);                           // [0] Move r1 = r5
        builder.EmitABC(OpCode.GetFieldIC, 0, 1, (byte)fieldConst);      // [1] GetFieldIC r0 = r1.myField
        builder.EmitRaw(icSlot);                                          // [2] companion word
        builder.EmitABC(OpCode.Return, 0, 0, 0);                         // [3] Return

        PassResult result = RunCopyPropOnly(builder);

        Assert.True(result.ChangedAnything);

        // GetFieldIC at index 1: B should now be r5.
        uint gficInstr = builder.RawCode[1];
        Assert.Equal(OpCode.GetFieldIC, Instruction.GetOp(gficInstr));
        Assert.Equal(5, (int)Instruction.GetB(gficInstr)); // r1 → r5

        // Companion word at index 2 must be unchanged.
        Assert.Equal((uint)icSlot, builder.RawCode[2]);
    }

    // ===========================================================================
    // §10.3 — Test 5: Full pipeline — CopyProp makes Move dead, DCE removes it
    // ===========================================================================

    [Fact]
    public void FullPipeline_CopyPropMakesMoveDead_DceRemovesIt()
    {
        // LoadK r0, 42
        // Move r1=r0          ← will be propagated into Return, then becomes dead
        // Return r1, hasValue  → after copy-prop: Return r0 → Move r1=r0 dead → DCE removes it
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;
        builder.EnableCopyProp = true;
        builder.EnableDce = true;
        builder.EnablePeephole = false; // isolate: only CopyProp + DCE
        builder.EmitABx(OpCode.LoadK, 0, builder.AddConstant(42L));
        builder.EmitABC(OpCode.Move, 1, 0, 0);    // Move r1 = r0
        builder.EmitABC(OpCode.Return, 1, 1, 0);   // Return r1  (B=1 means "has value")

        Chunk chunk = builder.Build();

        // After CopyProp: Return r0 (r1 → r0 via copyOf)
        // After DCE: Move r1=r0 is dead (r1 never read again), removed.
        Assert.Equal(2, chunk.Code.Length);
        Assert.Equal(OpCode.LoadK,  Instruction.GetOp(chunk.Code[0]));
        Assert.Equal(OpCode.Return, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(0, (int)Instruction.GetA(chunk.Code[1])); // Return r0

        // Verify that CopyPropagationPass reported rewrites.
        Assert.NotNull(chunk.PipelineStats);
        (string Name, PassResult Result) cpPass =
            chunk.PipelineStats!.Passes.First(p => p.Name == "CopyPropagationPass");
        Assert.True(cpPass.Result.InstructionsRewritten > 0);
    }

    // ===========================================================================
    // §10.3 — Test 6: EnableCopyProp = false suppresses the pass
    // ===========================================================================

    [Fact]
    public void EnableCopyProp_False_NoRewriting()
    {
        // Same code as Test 1 but with EnableCopyProp = false.
        // Add must still read r0 (the copy was not propagated).
        var builder = new ChunkBuilder();
        builder.MaxRegs = 4;
        builder.EnableCopyProp = false;
        builder.EnableDce = false;
        builder.EnablePeephole = false;
        builder.EnableOptimizationPipeline = true;

        builder.EmitABC(OpCode.Move, 0, 1, 0);    // Move r0 = r1
        builder.EmitABC(OpCode.Add,  2, 0, 3);    // Add r2 = r0, r3
        builder.EmitABC(OpCode.Return, 0, 0, 0);

        Chunk chunk = builder.Build();

        // No CopyPropagationPass in pipeline when disabled.
        Assert.NotNull(chunk.PipelineStats);
        Assert.DoesNotContain(
            "CopyPropagationPass",
            chunk.PipelineStats!.Passes.Select(p => p.Name));

        // Add still reads r0.
        uint addInstr = chunk.Code[1];
        Assert.Equal(OpCode.Add, Instruction.GetOp(addInstr));
        Assert.Equal(0, (int)Instruction.GetB(addInstr)); // r0 unchanged
    }

    // ===========================================================================
    // §10.3 — Test 7: EnableOptimizationPipeline = false uses legacy path (no copy-prop)
    // ===========================================================================

    [Fact]
    public void EnableOptimizationPipeline_False_NoCopyPropRewriting()
    {
        // Legacy path: copy-prop never runs.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 4;
        builder.EnableOptimizationPipeline = false;
        builder.EnableDce = false;
        builder.EnablePeephole = false;

        builder.EmitABC(OpCode.Move, 0, 1, 0);    // Move r0 = r1
        builder.EmitABC(OpCode.Add,  2, 0, 3);    // Add r2 = r0, r3
        builder.EmitABC(OpCode.Return, 0, 0, 0);

        Chunk chunk = builder.Build();

        // PipelineStats is null for legacy path.
        Assert.Null(chunk.PipelineStats);

        // Add still reads r0 (no copy-prop ran).
        uint addInstr = chunk.Code[1];
        Assert.Equal(OpCode.Add, Instruction.GetOp(addInstr));
        Assert.Equal(0, (int)Instruction.GetB(addInstr)); // r0 unchanged
    }

    // ===========================================================================
    // Regression — for-in loop closure capture (kanban: Task Run — Loop Variable
    // Closure Capture). CloseUpval(A) treats A as a stack-slot lower bound, NOT
    // a register read. Copy propagation must NOT rewrite its A operand: doing so
    // would change which slot range gets closed and leave per-iteration upvalues
    // open, so all iterations end up sharing the final loop value.
    // ===========================================================================

    [Fact]
    public void CopyProp_CloseUpval_OperandNotRewritten()
    {
        // Move r1, r3; CloseUpval r1 → must remain CloseUpval r1, NOT r3.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 4;
        builder.EmitABC(OpCode.Move, 1, 3, 0);    // Move r1 = r3
        builder.EmitA(OpCode.CloseUpval, 1);       // CloseUpval r1 (slot lower-bound)
        builder.EmitABC(OpCode.Return, 0, 0, 0);

        RunCopyPropOnly(builder);

        // CloseUpval is at index 1. Its A operand must still be r1.
        uint closeInstr = builder.RawCode[1];
        Assert.Equal(OpCode.CloseUpval, Instruction.GetOp(closeInstr));
        Assert.Equal(1, (int)Instruction.GetA(closeInstr));
    }

    // ===========================================================================
    // Regression — Call/CallSpread invalidate copyOf entries within the callee
    // frame window. The callee's frame spans R(A+1)..R(A+MaxRegs-1), so any
    // copyOf entry whose key or value sits above A may reference a register the
    // callee clobbers at runtime. Subsequent reads must NOT be rewritten through
    // those stale entries. Mirrors the LVN fix for the consecutive-call bug.
    // ===========================================================================

    [Fact]
    public void CopyProp_Call_KillsEntriesAboveA()
    {
        // Move r8, r0;           — r8 is a copy of r0
        // Call  r4, _, 1;        — callee frame is R(5)..R(5+MaxRegs-1) ⊇ {r8}
        // Add   r9, r8, r0;      — read of r8 must NOT be rewritten to r0
        //                          (the runtime r8 has been clobbered by the callee)
        var builder = new ChunkBuilder();
        builder.MaxRegs = 10;
        builder.EmitABC(OpCode.Move, 8, 0, 0);     // Move r8 = r0
        builder.EmitABC(OpCode.Call, 4, 0, 1);     // Call r4 with 1 arg
        builder.EmitABC(OpCode.Add, 9, 8, 0);      // Add r9 = r8 + r0
        builder.EmitABC(OpCode.Return, 0, 0, 0);

        RunCopyPropOnly(builder);

        // Add is at index 2 in the lowered code. Its B operand must still be r8,
        // not rewritten to r0 — the call has invalidated copyOf[r8].
        uint addInstr = builder.RawCode[2];
        Assert.Equal(OpCode.Add, Instruction.GetOp(addInstr));
        Assert.Equal(8, (int)Instruction.GetB(addInstr));
    }

    [Fact]
    public void CopyProp_CallBuiltIn_PreservesEntriesAboveA()
    {
        // CallBuiltIn does NOT push a frame — caller registers above A are safe.
        // The copyOf entry should survive and the post-call read should be rewritten.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 10;
        builder.EmitABC(OpCode.Move, 8, 0, 0);          // Move r8 = r0
        builder.EmitABC(OpCode.CallBuiltIn, 4, 5, 1);   // CallBuiltIn r4, ns=r5, 1 arg
        builder.EmitRaw(0);                              // companion word: IC slot 0
        builder.EmitABC(OpCode.Add, 9, 8, 7);            // Add r9 = r8 + r7
        builder.EmitABC(OpCode.Return, 0, 0, 0);

        RunCopyPropOnly(builder);

        // The Add reads r8; copyOf[r8]=r0 should still apply, so B is rewritten to r0.
        // Add is at index 3 (Move=0, CallBuiltIn=1, companion=2, Add=3).
        uint addInstr = builder.RawCode[3];
        Assert.Equal(OpCode.Add, Instruction.GetOp(addInstr));
        Assert.Equal(0, (int)Instruction.GetB(addInstr));
    }
}
