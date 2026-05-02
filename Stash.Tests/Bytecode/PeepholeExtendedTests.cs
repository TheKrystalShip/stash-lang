using Stash.Bytecode;

namespace Stash.Tests.Bytecode;

public class PeepholeExtendedTests
{
    // =========================================================================
    // Pattern 11 — Move(A, A) self-move elimination
    // =========================================================================

    [Fact]
    public void Pattern11_SelfMove_EliminatedFromChunk()
    {
        // Build a synthetic chunk: LoadNull r0, Move r0 r0, Return r0 0 0
        // The self-move should be dropped unconditionally.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;

        builder.EmitA(OpCode.LoadNull, 0);       // instruction 0 — effectful
        builder.EmitAB(OpCode.Move, 0, 0);       // instruction 1 — self-move, should drop
        builder.EmitABC(OpCode.Return, 0, 0, 0); // instruction 2

        Chunk chunk = builder.Build();

        Assert.Equal(2, chunk.Code.Length);
        Assert.Equal(OpCode.LoadNull, Instruction.GetOp(chunk.Code[0]));
        Assert.Equal(OpCode.Return, Instruction.GetOp(chunk.Code[1]));
    }

    [Fact]
    public void Pattern11_NonSelfMove_NotEliminated()
    {
        // Move r1, r0 is NOT a self-move — must be preserved.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 2;

        builder.EmitA(OpCode.LoadNull, 0);       // instruction 0
        builder.EmitAB(OpCode.Move, 1, 0);       // instruction 1 — real move, keep
        builder.EmitABC(OpCode.Return, 1, 0, 0); // instruction 2

        Chunk chunk = builder.Build();

        // Pattern 2 (Move + Return) fires here, so the Move is still removed —
        // but in this case Return is patched to read r0 directly.
        // Verify the final instruction is Return reading the original source.
        Assert.Equal(2, chunk.Code.Length);
        Assert.Equal(OpCode.Return, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(0, Instruction.GetA(chunk.Code[1]));  // source r0, not r1
    }

    // =========================================================================
    // Pattern 6 — Move(A,B) + InitConstGlobal(A, slotBx) → InitConstGlobal(B, slotBx)
    // =========================================================================

    [Fact]
    public void Pattern6_MoveFollowedByInitConstGlobal_ElidesMove()
    {
        // Synthetic chunk:
        //   LoadK r1, k0     (load value into r1)
        //   Move  r2, r1     (copy r1 → r2)
        //   InitConstGlobal r2, slot0  (store r2 into global slot 0)
        //   Return r0, 0, 0
        //
        // After Pattern 6: Move is dropped, InitConstGlobal reads r1 directly.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 3;

        ushort k0 = builder.AddConstant(42L);
        builder.EmitABx(OpCode.LoadK, 1, k0);           // instruction 0
        builder.EmitAB(OpCode.Move, 2, 1);               // instruction 1 — should be removed
        builder.EmitABx(OpCode.InitConstGlobal, 2, 0);  // instruction 2 — rewritten to read r1
        builder.EmitABC(OpCode.Return, 0, 0, 0);         // instruction 3

        Chunk chunk = builder.Build();

        // Move is gone; 3 instructions remain.
        Assert.Equal(3, chunk.Code.Length);
        Assert.Equal(OpCode.LoadK, Instruction.GetOp(chunk.Code[0]));
        Assert.Equal(OpCode.InitConstGlobal, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(1, Instruction.GetA(chunk.Code[1]));   // reads r1 (original source)
        Assert.Equal(0, Instruction.GetBx(chunk.Code[1]));  // slot index preserved
        Assert.Equal(OpCode.Return, Instruction.GetOp(chunk.Code[2]));
    }

    [Fact]
    public void Pattern6_MoveIsJumpTarget_NotElided()
    {
        // Synthetic chunk:
        //   Jmp  sBx=0       (instruction 0 → target = 0+1+0 = 1, making instruction 1 a jump target)
        //   Move  r2, r1     (instruction 1 — jump target, pattern MUST NOT fire)
        //   InitConstGlobal r2, slot0  (instruction 2)
        //   Return r0, 0, 0  (instruction 3)
        //
        // Because instruction 1 is a jump target, Pattern 6 must be suppressed.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 3;

        builder.EmitAsBx(OpCode.Jmp, 0, 0);             // instruction 0 — jumps to index 1
        builder.EmitAB(OpCode.Move, 2, 1);               // instruction 1 — jump target
        builder.EmitABx(OpCode.InitConstGlobal, 2, 0);  // instruction 2
        builder.EmitABC(OpCode.Return, 0, 0, 0);         // instruction 3

        Chunk chunk = builder.Build();

        // All 4 instructions must remain unchanged.
        Assert.Equal(4, chunk.Code.Length);
        Assert.Equal(OpCode.Move, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(2, Instruction.GetA(chunk.Code[1])); // dest still r2
        Assert.Equal(1, Instruction.GetB(chunk.Code[1])); // source still r1
        Assert.Equal(OpCode.InitConstGlobal, Instruction.GetOp(chunk.Code[2]));
        Assert.Equal(2, Instruction.GetA(chunk.Code[2])); // still reads r2 (not r1)
        Assert.Equal(0, Instruction.GetBx(chunk.Code[2])); // slot preserved
    }

    [Fact]
    public void Pattern6_InitConstGlobalIsJumpTarget_NotElided()
    {
        // Pattern 6 must also not fire when the InitConstGlobal instruction
        // (i+1) is a jump target — the existing jumpTargets.Contains(i+1) guard handles this.
        //
        // Synthetic chunk:
        //   Move  r2, r1     (instruction 0)
        //   InitConstGlobal r2, slot0  (instruction 1 — jump target from instruction 2)
        //   Jmp   sBx=-2     (instruction 2 → target = 2+1-2 = 1)
        //   Return r0, 0, 0  (instruction 3)
        var builder = new ChunkBuilder();
        builder.MaxRegs = 3;

        builder.EmitAB(OpCode.Move, 2, 1);               // instruction 0
        builder.EmitABx(OpCode.InitConstGlobal, 2, 0);  // instruction 1 — will be jump target
        builder.EmitAsBx(OpCode.Jmp, 0, -2);            // instruction 2 → target = 2+1-2 = 1
        builder.EmitABC(OpCode.Return, 0, 0, 0);         // instruction 3

        Chunk chunk = builder.Build();

        // Pattern must not fire because InitConstGlobal (i+1=1) is a jump target.
        Assert.Equal(4, chunk.Code.Length);
        Assert.Equal(OpCode.Move, Instruction.GetOp(chunk.Code[0]));
        Assert.Equal(OpCode.InitConstGlobal, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(2, Instruction.GetA(chunk.Code[1])); // still reads r2
    }
}
