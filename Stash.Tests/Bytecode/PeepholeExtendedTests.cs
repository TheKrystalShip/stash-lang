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
        builder.EnableDce = false;
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
        builder.EnableDce = false;
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
        builder.EnableCopyProp = false;
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

    // =========================================================================
    // Pattern 7a — Move(A,B) + SetTable(A, K, V) → SetTable(B, K, V)
    // =========================================================================

    [Fact]
    public void Pattern7a_MoveFollowedBySetTableOnTableReg_ElidesMove()
    {
        // LoadNull r0
        // Move r2, r3          (moveA=2, moveB=3)
        // SetTable r2, r1, r0  (table=r2=moveA, key=r1, value=r0)
        // Return r0, 0, 0
        // → SetTable r3, r1, r0  (table register rewritten to moveB=3)
        var builder = new ChunkBuilder();
        builder.MaxRegs = 5;

        builder.EmitA(OpCode.LoadNull, 0);
        builder.EmitAB(OpCode.Move, 2, 3);
        builder.EmitABC(OpCode.SetTable, 2, 1, 0);
        builder.EmitABC(OpCode.Return, 0, 0, 0);

        Chunk chunk = builder.Build();

        Assert.Equal(3, chunk.Code.Length);
        Assert.Equal(OpCode.SetTable, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(3, Instruction.GetA(chunk.Code[1])); // table → r3 (moveB)
        Assert.Equal(1, Instruction.GetB(chunk.Code[1])); // key unchanged
        Assert.Equal(0, Instruction.GetC(chunk.Code[1])); // value unchanged
    }

    // =========================================================================
    // Pattern 7b — Move(A,B) + SetTable(T, K, A) → SetTable(T, K, B)
    // =========================================================================

    [Fact]
    public void Pattern7b_MoveFollowedBySetTableOnValueReg_ElidesMove()
    {
        // LoadNull r0
        // Move r2, r3          (moveA=2, moveB=3)
        // SetTable r0, r1, r2  (table=r0, key=r1, value=r2=moveA)
        // Return r0, 0, 0
        // → SetTable r0, r1, r3  (value register rewritten to moveB=3)
        var builder = new ChunkBuilder();
        builder.MaxRegs = 5;

        builder.EmitA(OpCode.LoadNull, 0);
        builder.EmitAB(OpCode.Move, 2, 3);
        builder.EmitABC(OpCode.SetTable, 0, 1, 2);
        builder.EmitABC(OpCode.Return, 0, 0, 0);

        Chunk chunk = builder.Build();

        Assert.Equal(3, chunk.Code.Length);
        Assert.Equal(OpCode.SetTable, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(0, Instruction.GetA(chunk.Code[1])); // table unchanged
        Assert.Equal(1, Instruction.GetB(chunk.Code[1])); // key unchanged
        Assert.Equal(3, Instruction.GetC(chunk.Code[1])); // value → r3 (moveB)
    }

    // =========================================================================
    // Pattern 8a — Move(A,B) + GetTable(X, A, K) → GetTable(X, B, K)
    // =========================================================================

    [Fact]
    public void Pattern8a_MoveFollowedByGetTableOnTableReg_ElidesMove()
    {
        // LoadNull r0
        // Move r2, r3          (moveA=2, moveB=3)
        // GetTable r4, r2, r1  (result=r4, table=r2=moveA, key=r1)
        // Return r4, 1, 0
        // → GetTable r4, r3, r1
        var builder = new ChunkBuilder();
        builder.EnableDce = false;
        builder.EnableCopyProp = false;
        builder.MaxRegs = 6;

        builder.EmitA(OpCode.LoadNull, 0);
        builder.EmitAB(OpCode.Move, 2, 3);
        builder.EmitABC(OpCode.GetTable, 4, 2, 1);
        builder.EmitABC(OpCode.Return, 4, 1, 0);

        Chunk chunk = builder.Build();

        Assert.Equal(3, chunk.Code.Length);
        Assert.Equal(OpCode.GetTable, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(4, Instruction.GetA(chunk.Code[1])); // result unchanged
        Assert.Equal(3, Instruction.GetB(chunk.Code[1])); // table → r3 (moveB)
        Assert.Equal(1, Instruction.GetC(chunk.Code[1])); // key unchanged
    }

    // =========================================================================
    // Pattern 8b — Move(A,B) + GetTable(X, T, A) → GetTable(X, T, B)
    // =========================================================================

    [Fact]
    public void Pattern8b_MoveFollowedByGetTableOnKeyReg_ElidesMove()
    {
        // LoadNull r0
        // Move r2, r3          (moveA=2, moveB=3)
        // GetTable r4, r1, r2  (result=r4, table=r1, key=r2=moveA)
        // Return r4, 1, 0
        // → GetTable r4, r1, r3
        var builder = new ChunkBuilder();
        builder.EnableDce = false;
        builder.EnableCopyProp = false;
        builder.MaxRegs = 6;

        builder.EmitA(OpCode.LoadNull, 0);
        builder.EmitAB(OpCode.Move, 2, 3);
        builder.EmitABC(OpCode.GetTable, 4, 1, 2);
        builder.EmitABC(OpCode.Return, 4, 1, 0);

        Chunk chunk = builder.Build();

        Assert.Equal(3, chunk.Code.Length);
        Assert.Equal(OpCode.GetTable, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(4, Instruction.GetA(chunk.Code[1])); // result unchanged
        Assert.Equal(1, Instruction.GetB(chunk.Code[1])); // table unchanged
        Assert.Equal(3, Instruction.GetC(chunk.Code[1])); // key → r3 (moveB)
    }

    // =========================================================================
    // Pattern 9a — Move(A,B) + GetField(X, A, K) → GetField(X, B, K)
    // =========================================================================

    [Fact]
    public void Pattern9a_MoveFollowedByGetField_ElidesMove()
    {
        // LoadNull r0
        // Move r2, r3            (moveA=2, moveB=3)
        // GetField r4, r2, k0   (result=r4, obj=r2=moveA, field=k0)
        // Return r4, 1, 0
        // → GetField r4, r3, k0
        var builder = new ChunkBuilder();
        builder.EnableDce = false;
        builder.EnableCopyProp = false;
        builder.MaxRegs = 6;

        ushort k0 = builder.AddConstant("field");

        builder.EmitA(OpCode.LoadNull, 0);
        builder.EmitAB(OpCode.Move, 2, 3);
        builder.EmitABC(OpCode.GetField, 4, 2, (byte)k0);
        builder.EmitABC(OpCode.Return, 4, 1, 0);

        Chunk chunk = builder.Build();

        Assert.Equal(3, chunk.Code.Length);
        Assert.Equal(OpCode.GetField, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(4, Instruction.GetA(chunk.Code[1])); // result unchanged
        Assert.Equal(3, Instruction.GetB(chunk.Code[1])); // obj → r3 (moveB)
        Assert.Equal(k0, Instruction.GetC(chunk.Code[1])); // field constant unchanged
    }

    // =========================================================================
    // Pattern 9a (GetFieldIC) — companion word safety
    // =========================================================================

    [Fact]
    public void Pattern9a_GetFieldIC_CompanionWordUnchanged()
    {
        // Two pairs: Move + GetFieldIC + <companion> so we can verify both IC
        // slot indices are preserved exactly after the Moves are elided.
        //
        // Instruction sequence (positions 0-7):
        //   0: LoadNull r0
        //   1: Move r2, r1       (moveA=2, moveB=1)
        //   2: GetFieldIC r3, r2, k0
        //   3: <companion: icSlot0>
        //   4: Move r5, r4       (moveA=5, moveB=4)
        //   5: GetFieldIC r6, r5, k0
        //   6: <companion: icSlot1>
        //   7: Return r0, 0, 0
        //
        // After peephole (both Moves elided):
        //   0: LoadNull r0
        //   1: GetFieldIC r3, r1, k0
        //   2: <companion: icSlot0>   ← must be untouched
        //   3: GetFieldIC r6, r4, k0
        //   4: <companion: icSlot1>   ← must be untouched
        //   5: Return r0, 0, 0
        var builder = new ChunkBuilder();
        builder.EnableDce = false;
        builder.EnableCopyProp = false;
        builder.MaxRegs = 8;

        ushort k0 = builder.AddConstant("field");
        ushort icSlot0 = builder.AllocateICSlot((ushort)k0);
        ushort icSlot1 = builder.AllocateICSlot((ushort)k0);

        builder.EmitA(OpCode.LoadNull, 0);                       // 0

        builder.EmitAB(OpCode.Move, 2, 1);                       // 1
        builder.EmitABC(OpCode.GetFieldIC, 3, 2, (byte)k0);     // 2
        builder.EmitRaw((uint)icSlot0);                          // 3 — companion word

        builder.EmitAB(OpCode.Move, 5, 4);                       // 4
        builder.EmitABC(OpCode.GetFieldIC, 6, 5, (byte)k0);     // 5
        builder.EmitRaw((uint)icSlot1);                          // 6 — companion word

        builder.EmitABC(OpCode.Return, 0, 0, 0);                 // 7

        Chunk chunk = builder.Build();

        // Both Moves should be elided → 6 code words remain.
        Assert.Equal(6, chunk.Code.Length);

        // First GetFieldIC: obj register rewritten from r2 → r1
        Assert.Equal(OpCode.GetFieldIC, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(3,  Instruction.GetA(chunk.Code[1])); // result unchanged
        Assert.Equal(1,  Instruction.GetB(chunk.Code[1])); // obj → r1 (moveB)
        Assert.Equal(k0, Instruction.GetC(chunk.Code[1])); // field unchanged
        // Companion word must be the original icSlot0 value
        Assert.Equal((uint)icSlot0, chunk.Code[2]);

        // Second GetFieldIC: obj register rewritten from r5 → r4
        Assert.Equal(OpCode.GetFieldIC, Instruction.GetOp(chunk.Code[3]));
        Assert.Equal(6,  Instruction.GetA(chunk.Code[3])); // result unchanged
        Assert.Equal(4,  Instruction.GetB(chunk.Code[3])); // obj → r4 (moveB)
        Assert.Equal(k0, Instruction.GetC(chunk.Code[3])); // field unchanged
        // Companion word must be the original icSlot1 value
        Assert.Equal((uint)icSlot1, chunk.Code[4]);
    }

    // =========================================================================
    // Pattern 9b — Move(A,B) + SetField(A, K, V) → SetField(B, K, V)
    // =========================================================================

    [Fact]
    public void Pattern9b_MoveFollowedBySetFieldOnObjReg_ElidesMove()
    {
        // LoadNull r0
        // Move r2, r3            (moveA=2, moveB=3)
        // SetField r2, k0, r0   (obj=r2=moveA, field=k0, value=r0)
        // Return r0, 0, 0
        // → SetField r3, k0, r0
        var builder = new ChunkBuilder();
        builder.MaxRegs = 5;

        ushort k0 = builder.AddConstant("field");

        builder.EmitA(OpCode.LoadNull, 0);
        builder.EmitAB(OpCode.Move, 2, 3);
        builder.EmitABC(OpCode.SetField, 2, (byte)k0, 0);  // obj=r2, key=k0, value=r0
        builder.EmitABC(OpCode.Return, 0, 0, 0);

        Chunk chunk = builder.Build();

        Assert.Equal(3, chunk.Code.Length);
        Assert.Equal(OpCode.SetField, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(3,  Instruction.GetA(chunk.Code[1])); // obj → r3 (moveB)
        Assert.Equal(k0, Instruction.GetB(chunk.Code[1])); // field constant unchanged
        Assert.Equal(0,  Instruction.GetC(chunk.Code[1])); // value unchanged
    }

    // =========================================================================
    // Pattern 9c — Move(A,B) + SetField(T, K, A) → SetField(T, K, B)
    // =========================================================================

    [Fact]
    public void Pattern9c_MoveFollowedBySetFieldOnValueReg_ElidesMove()
    {
        // LoadNull r0
        // Move r2, r3            (moveA=2, moveB=3)
        // SetField r0, k0, r2   (obj=r0, field=k0, value=r2=moveA)
        // Return r0, 0, 0
        // → SetField r0, k0, r3
        var builder = new ChunkBuilder();
        builder.MaxRegs = 5;

        ushort k0 = builder.AddConstant("field");

        builder.EmitA(OpCode.LoadNull, 0);
        builder.EmitAB(OpCode.Move, 2, 3);
        builder.EmitABC(OpCode.SetField, 0, (byte)k0, 2);  // obj=r0, key=k0, value=r2
        builder.EmitABC(OpCode.Return, 0, 0, 0);

        Chunk chunk = builder.Build();

        Assert.Equal(3, chunk.Code.Length);
        Assert.Equal(OpCode.SetField, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(0,  Instruction.GetA(chunk.Code[1])); // obj unchanged
        Assert.Equal(k0, Instruction.GetB(chunk.Code[1])); // field constant unchanged
        Assert.Equal(3,  Instruction.GetC(chunk.Code[1])); // value → r3 (moveB)
    }

    // =========================================================================
    // Pattern 9d — Move(A,B) + Self(X, A, K) → Self(X, B, K) [positive]
    // =========================================================================

    [Fact]
    public void Pattern9d_MoveFollowedBySelf_ElidesMove()
    {
        // LoadNull r0
        // Move r2, r3            (moveA=2, moveB=3)
        // Self r4, r2, k0       (method=r4, obj=r2=moveA, field=k0)
        //                        X=4, X+1=5 != moveB=3 → guard passes
        // Return r4, 1, 0
        // → Self r4, r3, k0
        var builder = new ChunkBuilder();
        builder.EnableDce = false;
        builder.EnableCopyProp = false;
        builder.MaxRegs = 7;

        ushort k0 = builder.AddConstant("method");

        builder.EmitA(OpCode.LoadNull, 0);
        builder.EmitAB(OpCode.Move, 2, 3);
        builder.EmitABC(OpCode.Self, 4, 2, (byte)k0);
        builder.EmitABC(OpCode.Return, 4, 1, 0);

        Chunk chunk = builder.Build();

        Assert.Equal(3, chunk.Code.Length);
        Assert.Equal(OpCode.Self, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(4,  Instruction.GetA(chunk.Code[1])); // result register unchanged
        Assert.Equal(3,  Instruction.GetB(chunk.Code[1])); // obj → r3 (moveB)
        Assert.Equal(k0, Instruction.GetC(chunk.Code[1])); // method name unchanged
    }

    // =========================================================================
    // Pattern 9d — negative: X+1 == moveB → pattern must NOT fire
    // =========================================================================

    [Fact]
    public void Pattern9d_SelfCollision_NotFused()
    {
        // Move r2, r3            (moveA=2, moveB=3)
        // Self r2, r2, k0       (X=2, X+1=3 == moveB=3 → guard blocks fusion)
        // Return r2, 1, 0
        //
        // Without the guard, Self would write r3 (= X+1) with the receiver,
        // clobbering the source we substituted in.
        var builder = new ChunkBuilder();
        builder.EnableCopyProp = false;
        builder.MaxRegs = 5;

        ushort k0 = builder.AddConstant("method");

        builder.EmitAB(OpCode.Move, 2, 3);
        builder.EmitABC(OpCode.Self, 2, 2, (byte)k0);
        builder.EmitABC(OpCode.Return, 2, 1, 0);

        Chunk chunk = builder.Build();

        // 3 instructions must remain: Move + Self + Return
        Assert.Equal(3, chunk.Code.Length);
        Assert.Equal(OpCode.Move, Instruction.GetOp(chunk.Code[0]));
        Assert.Equal(OpCode.Self, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(2, Instruction.GetB(chunk.Code[1])); // obj still r2 (not rewritten)
    }

    // =========================================================================
    // Jump-target negative test (shared guard for patterns 7-9)
    // =========================================================================

    [Fact]
    public void Patterns7to9_MoveIsJumpTarget_NotFused()
    {
        // Jmp sBx=0        (instruction 0 → target = 1)
        // Move r2, r3      (instruction 1 — jump target; pattern must not fire)
        // SetTable r2, r1, r0  (instruction 2)
        // Return r0, 0, 0  (instruction 3)
        //
        // The jumpTargets.Contains(i) guard blocks the pattern at instruction 1.
        var builder = new ChunkBuilder();
        builder.EnableCopyProp = false;
        builder.MaxRegs = 5;

        builder.EmitAsBx(OpCode.Jmp, 0, 0);             // 0 → target 1
        builder.EmitAB(OpCode.Move, 2, 3);               // 1 — jump target
        builder.EmitABC(OpCode.SetTable, 2, 1, 0);       // 2
        builder.EmitABC(OpCode.Return, 0, 0, 0);         // 3

        Chunk chunk = builder.Build();

        Assert.Equal(4, chunk.Code.Length);
        Assert.Equal(OpCode.Move, Instruction.GetOp(chunk.Code[1]));
        Assert.Equal(OpCode.SetTable, Instruction.GetOp(chunk.Code[2]));
        Assert.Equal(2, Instruction.GetA(chunk.Code[2])); // table still r2
    }
}
