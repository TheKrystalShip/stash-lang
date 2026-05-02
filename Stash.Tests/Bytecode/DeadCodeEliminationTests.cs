using Stash.Bytecode;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Tests for the trivial backward-scan dead-code elimination pass in ChunkBuilder.
/// Each test builds a synthetic chunk and asserts on the resulting instruction layout.
/// </summary>
public class DeadCodeEliminationTests
{
    // =========================================================================
    // Basic elimination — dead LoadK
    // =========================================================================

    [Fact]
    public void DeadLoadK_AfterConstHoisting_IsEliminated()
    {
        // Simulate const-hoisting: LoadK r1 was paired with InitConstGlobal that was
        // removed (hoisted). r1 is never read → DCE must remove it.
        //   [0] LoadK r1, k0     ← dead (r1 never read)
        //   [1] Return r0, 1, 0  ← reads r0, not r1
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;

        ushort k0 = builder.AddConstant(42L);
        builder.EmitABx(OpCode.LoadK, 1, k0);          // [0] dead
        builder.EmitABC(OpCode.Return, 0, 1, 0);        // [1] reads r0

        Chunk chunk = builder.Build();

        // LoadK r1 must be gone; only Return survives.
        Assert.Single(chunk.Code);
        Assert.Equal(OpCode.Return, Instruction.GetOp(chunk.Code[0]));
    }

    [Fact]
    public void DeadLoadK_WhenDestIsRead_IsPreserved()
    {
        // LoadK r1 is read by Return → must NOT be eliminated.
        //   [0] LoadK r1, k0
        //   [1] Return r1, 1, 0
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;

        ushort k0 = builder.AddConstant(99L);
        builder.EmitABx(OpCode.LoadK, 1, k0);          // [0]
        builder.EmitABC(OpCode.Return, 1, 1, 0);        // [1] reads r1

        Chunk chunk = builder.Build();

        // Both instructions must survive (or peephole may fuse them — either way,
        // the LoadK is needed and must not be DCE'd independently).
        Assert.True(chunk.Code.Length >= 1);
        // Verify Return is present (peephole may have fused, but LoadK must have been kept).
        bool hasReturn = false;
        foreach (uint w in chunk.Code)
            if (Instruction.GetOp(w) == OpCode.Return) { hasReturn = true; break; }
        Assert.True(hasReturn);
    }

    // =========================================================================
    // Dead overwrite — effectful Call preserved, dead LoadK removed
    // =========================================================================

    [Fact]
    public void DeadOverwrite_CallPreserved_DeadLoadKRemoved()
    {
        // Simulates:  let x = compute(); x = 5;  (x never read after)
        //   [0] GetGlobal r0, g0        ← load function (pure but r0 is read by Call)
        //   [1] Call r0, 0, 0           ← effectful, result in r0
        //   [2] LoadK r0, k_five        ← pure, overwrites r0 (dead if r0 never read)
        //   [3] Return r1, 1, 0         ← reads r1, not r0
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;

        ushort kFive = builder.AddConstant(5L);
        builder.EmitABx(OpCode.GetGlobal, 0, 0);        // [0] load fn into r0
        builder.EmitABC(OpCode.Call, 0, 0, 0);          // [1] call with 0 args
        builder.EmitABx(OpCode.LoadK, 0, kFive);        // [2] dead overwrite of r0
        builder.EmitABC(OpCode.Return, 1, 1, 0);        // [3] reads r1

        Chunk chunk = builder.Build();

        // LoadK at [2] should be eliminated.  GetGlobal [0] and Call [1] must survive.
        bool hasCall = false, hasGetGlobal = false, hasDeadLoadK = false;
        foreach (uint w in chunk.Code)
        {
            switch (Instruction.GetOp(w))
            {
                case OpCode.Call:       hasCall = true; break;
                case OpCode.GetGlobal:  hasGetGlobal = true; break;
                case OpCode.LoadK:      hasDeadLoadK = true; break;
            }
        }
        Assert.True(hasCall, "Call (effectful) must be preserved");
        Assert.True(hasGetGlobal, "GetGlobal (pure, feeds Call) must be preserved");
        Assert.False(hasDeadLoadK, "Dead LoadK overwrite must be eliminated");
    }

    // =========================================================================
    // Effectful instruction guards — never eliminated
    // =========================================================================

    [Fact]
    public void EffectfulCall_UnusedResult_IsPreserved()
    {
        // Call r0 … Return r1 — even though r0 is never read, Call has side effects.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;

        builder.EmitABx(OpCode.GetGlobal, 0, 0);
        builder.EmitABC(OpCode.Call, 0, 0, 0);          // effectful
        builder.EmitABC(OpCode.Return, 1, 1, 0);

        Chunk chunk = builder.Build();

        bool hasCall = chunk.Code.Any(w => Instruction.GetOp(w) == OpCode.Call);
        Assert.True(hasCall, "Call with unused result must be preserved");
    }

    [Fact]
    public void EffectfulNewArray_UnusedResult_IsPreserved()
    {
        // NewArray allocates; must never be removed even if result is unused.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;

        builder.EmitABC(OpCode.NewArray, 0, 0, 0);      // effectful
        builder.EmitABC(OpCode.Return, 1, 1, 0);

        Chunk chunk = builder.Build();

        bool hasNewArray = chunk.Code.Any(w => Instruction.GetOp(w) == OpCode.NewArray);
        Assert.True(hasNewArray, "NewArray with unused result must be preserved");
    }

    [Fact]
    public void EffectfulThrow_IsPreserved()
    {
        // Throw has side effects (raises an exception).
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;

        builder.EmitA(OpCode.LoadNull, 0);
        builder.EmitA(OpCode.Throw, 0);                 // effectful
        builder.EmitABC(OpCode.Return, 1, 1, 0);

        Chunk chunk = builder.Build();

        bool hasThrow = chunk.Code.Any(w => Instruction.GetOp(w) == OpCode.Throw);
        Assert.True(hasThrow, "Throw must be preserved");
    }

    [Fact]
    public void EffectfulDefer_IsPreserved()
    {
        // Defer registers a deferred closure — effectful.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;

        builder.EmitA(OpCode.LoadNull, 0);
        builder.EmitA(OpCode.Defer, 0);                 // effectful
        builder.EmitABC(OpCode.Return, 1, 1, 0);

        Chunk chunk = builder.Build();

        bool hasDefer = chunk.Code.Any(w => Instruction.GetOp(w) == OpCode.Defer);
        Assert.True(hasDefer, "Defer must be preserved");
    }

    [Fact]
    public void EffectfulInterpolate_IsPreserved()
    {
        // Interpolate allocates a string.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 3;

        builder.EmitA(OpCode.LoadNull, 1);               // arg in r1
        builder.EmitABC(OpCode.Interpolate, 0, 1, 0);   // effectful; result in r0
        builder.EmitABC(OpCode.Return, 2, 1, 0);         // reads r2, not r0

        Chunk chunk = builder.Build();

        bool hasInterpolate = chunk.Code.Any(w => Instruction.GetOp(w) == OpCode.Interpolate);
        Assert.True(hasInterpolate, "Interpolate with unused result must be preserved");
    }

    [Fact]
    public void EffectfulClosure_FeedingDefer_BothPreserved()
    {
        // Regression: Closure (effectful) feeds Defer (effectful).
        // Neither should be removed.
        //   [0] Closure r0, proto0    (effectful: allocates closure)
        //   [1] Defer r0              (effectful: registers deferred)
        //   [2] Return r1, 1, 0
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;

        // We use the builder's raw Closure emission (no actual prototype needed for this test).
        builder.EmitABx(OpCode.Closure, 0, 0);          // [0] effectful
        builder.EmitA(OpCode.Defer, 0);                 // [1] effectful
        builder.EmitABC(OpCode.Return, 1, 1, 0);        // [2]

        Chunk chunk = builder.Build();

        bool hasClosure = chunk.Code.Any(w => Instruction.GetOp(w) == OpCode.Closure);
        bool hasDefer = chunk.Code.Any(w => Instruction.GetOp(w) == OpCode.Defer);
        Assert.True(hasClosure, "Closure feeding Defer must be preserved");
        Assert.True(hasDefer, "Defer must be preserved");
    }

    // =========================================================================
    // Block boundary — register live across jump target not eliminated upstream
    // =========================================================================

    [Fact]
    public void BlockBoundary_RegisterLiveAcrossJumpTarget_NotEliminated()
    {
        // r1 is written before a Jmp whose target reads r1.
        // DCE must conservatively keep LoadK r1 even though it looks dead above the jump.
        //
        //   [0] LoadK r1, k0    ← r1 used at [3] (jump target)
        //   [1] Jmp  → [3]      (sBx = 3 - 1 - 1 = 1, target = 1+1+1 = 3)
        //   [2] LoadNull r2     ← unreachable in this path but valid code
        //   [3] Return r1, 1, 0 ← jump target; reads r1
        var builder = new ChunkBuilder();
        builder.MaxRegs = 3;

        ushort k0 = builder.AddConstant(42L);
        builder.EmitABx(OpCode.LoadK, 1, k0);           // [0]
        int jmpIdx = builder.EmitJump(OpCode.Jmp, 0);   // [1] placeholder
        builder.EmitA(OpCode.LoadNull, 2);               // [2]
        builder.PatchJump(jmpIdx);                       // patch to target [3]
        builder.EmitABC(OpCode.Return, 1, 1, 0);        // [3] jump target

        Chunk chunk = builder.Build();

        // LoadK r1 must survive — r1 is live at the jump target.
        bool hasLoadK = chunk.Code.Any(w =>
            Instruction.GetOp(w) == OpCode.LoadK && Instruction.GetA(w) == 1);
        Assert.True(hasLoadK, "LoadK r1 must be preserved (r1 is live at jump target)");
    }

    // =========================================================================
    // Source map — removed instruction's source entry redirected to successor
    // =========================================================================

    [Fact]
    public void SourceMap_RemovedInstruction_RedirectedToSuccessor()
    {
        // Dead LoadK at [0] has a source map entry at offset 0.
        // After DCE removes it, the entry must be remapped to offset 0 (the Return).
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;

        ushort k0 = builder.AddConstant(1L);
        builder.AddSourceMapping(new Stash.Common.SourceSpan("test", 1, 1, 1, 5));   // for LoadK
        builder.EmitABx(OpCode.LoadK, 1, k0);          // [0] dead
        builder.AddSourceMapping(new Stash.Common.SourceSpan("test", 2, 1, 2, 10));  // for Return
        builder.EmitABC(OpCode.Return, 0, 1, 0);        // [1]

        Chunk chunk = builder.Build();

        // After DCE the chunk has 1 instruction.  Both source entries should now map
        // to offset 0 (the surviving Return).
        Assert.Single(chunk.Code);
        for (int ei = 0; ei < chunk.SourceMap.Count; ei++)
            Assert.Equal(0, chunk.SourceMap[ei].BytecodeOffset);
    }

    // =========================================================================
    // Try/catch — handler offset remains valid after DCE
    // =========================================================================

    [Fact]
    public void TryCatch_HandlerOffsetValid_AfterDceRemovesDeadCode()
    {
        // Structure (no DCE inside the try body — conservative allLive at handler boundary):
        //   [0] LoadK r2, k0     ← dead (r2 never read)
        //   [1] TryBegin → [3]   ← sBx = 3-1-1 = 1; handler at old[3]
        //   [2] Return r0, 1, 0  ← try body
        //   [3] LoadNull r0      ← handler (jump target)
        //   [4] Return r0, 1, 0
        //
        // DCE cannot eliminate anything inside the try body (allLive at [3]).
        // BUT the dead LoadK at [0] is before all jump targets, and allLive starts false,
        // so it IS eliminated IF the backward scan reaches it before hitting any jump target.
        // Since [3] is the ONLY jump target in jumpTargets (from TryBegin), and [0] is
        // scanned AFTER [3] in the backward walk (indices 4,3,2,1,0), at index [3] allLive
        // becomes true, making [0] preserved too.  So no DCE fires — but we still verify
        // that the chunk is valid and TryBegin's offset is correctly patched by ApplyRemovals.
        //
        // For a case where DCE fires near a try: put the dead instruction AFTER the handler.
        //   [0] TryBegin → [2]
        //   [1] Return r0, 1, 0
        //   [2] LoadNull r0        ← handler (jump target)
        //   [3] LoadK  r1, k0      ← dead
        //   [4] Return r0, 1, 0
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;

        ushort k0 = builder.AddConstant(77L);

        int tryBeginIdx = builder.EmitJump(OpCode.TryBegin, 0); // [0]
        builder.EmitABC(OpCode.Return, 0, 1, 0);                // [1] try-body return
        builder.PatchJump(tryBeginIdx);                          // patch handler → [2]
        builder.EmitA(OpCode.LoadNull, 0);                      // [2] handler (jump target)
        builder.EmitABx(OpCode.LoadK, 1, k0);                   // [3] dead
        builder.EmitABC(OpCode.Return, 0, 1, 0);                // [4]

        Chunk chunk = builder.Build();

        // Verify TryBegin's handler offset points to a valid instruction (LoadNull).
        uint tryBeginInst = chunk.Code[0];
        Assert.Equal(OpCode.TryBegin, Instruction.GetOp(tryBeginInst));
        int handlerOffset = 0 + 1 + Instruction.GetSBx(tryBeginInst);
        Assert.True(handlerOffset >= 0 && handlerOffset < chunk.Code.Length,
            $"TryBegin handler offset {handlerOffset} out of range [0,{chunk.Code.Length})");
        Assert.Equal(OpCode.LoadNull, Instruction.GetOp(chunk.Code[handlerOffset]));
    }

    // =========================================================================
    // EnableDce=false toggle — dead LoadK must remain in place
    // =========================================================================

    [Fact]
    public void EnableDceFalse_DeadLoadK_Preserved()
    {
        // Same dead-LoadK setup as the first test, but with EnableDce=false.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;
        builder.EnableDce = false;   // ← toggle off

        ushort k0 = builder.AddConstant(42L);
        builder.EmitABx(OpCode.LoadK, 1, k0);          // [0] dead
        builder.EmitABC(OpCode.Return, 0, 1, 0);        // [1]

        Chunk chunk = builder.Build();

        // With DCE disabled the dead LoadK must survive.
        Assert.True(chunk.Code.Length >= 2, "Dead LoadK must survive when EnableDce=false");
        bool hasLoadK = chunk.Code.Any(w =>
            Instruction.GetOp(w) == OpCode.LoadK && Instruction.GetA(w) == 1);
        Assert.True(hasLoadK, "Dead LoadK r1 must be present when EnableDce=false");
    }

    // =========================================================================
    // Multiple dead instructions in sequence
    // =========================================================================

    [Fact]
    public void MultipleDeadInstructions_AllEliminated()
    {
        // Three consecutive dead loads before a Return that reads none of them.
        //   [0] LoadK r1, k0    dead
        //   [1] LoadNull r2     dead
        //   [2] LoadBool r3     dead
        //   [3] Return r0, 1, 0
        var builder = new ChunkBuilder();
        builder.MaxRegs = 4;

        ushort k0 = builder.AddConstant(1L);
        builder.EmitABx(OpCode.LoadK, 1, k0);          // [0] dead
        builder.EmitA(OpCode.LoadNull, 2);              // [1] dead
        builder.EmitABC(OpCode.LoadBool, 3, 1, 0);     // [2] dead
        builder.EmitABC(OpCode.Return, 0, 1, 0);       // [3]

        Chunk chunk = builder.Build();

        Assert.Single(chunk.Code);
        Assert.Equal(OpCode.Return, Instruction.GetOp(chunk.Code[0]));
    }

    // =========================================================================
    // Pure instruction feeding effectful — must be preserved
    // =========================================================================

    [Fact]
    public void PureInstruction_FeedingEffectful_IsPreserved()
    {
        // LoadK r1 feeds SetGlobal → must not be eliminated.
        //   [0] LoadK r1, k0
        //   [1] SetGlobal r1, g0   ← effectful, reads r1
        //   [2] Return r0, 1, 0
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;

        ushort k0 = builder.AddConstant(123L);
        builder.EmitABx(OpCode.LoadK, 1, k0);          // [0]
        builder.EmitABx(OpCode.SetGlobal, 1, 0);       // [1] reads r1
        builder.EmitABC(OpCode.Return, 0, 1, 0);       // [2]

        Chunk chunk = builder.Build();

        bool hasLoadK = chunk.Code.Any(w =>
            Instruction.GetOp(w) == OpCode.LoadK && Instruction.GetA(w) == 1);
        Assert.True(hasLoadK, "LoadK r1 must survive because it feeds SetGlobal");
    }
}
