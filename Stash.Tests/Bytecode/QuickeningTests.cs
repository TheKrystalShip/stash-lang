using Stash.Bytecode;
using Stash.Runtime;

namespace Stash.Tests.Bytecode;

public class QuickeningTests : BytecodeTestBase
{
    // ══════════════════════════ Activation ══════════════════════════

    [Fact]
    public void Activation_TopLevelChunk_QuickenedImmediately()
    {
        // Top-level scripts activate quickening on first call
        Chunk chunk = CompileSource("let x = 1 + 2;");
        var vm = new VirtualMachine();
        vm.Execute(chunk);
        Assert.NotNull(chunk.QuickenCounters);
    }

    [Fact]
    public void Activation_NamedFunction_QuickenedOnSecondCall()
    {
        // Named functions quicken on 2nd call
        Chunk chunk = CompileSource("fn add(a, b) { return a + b; } add(1, 2); add(3, 4);");
        var vm = new VirtualMachine();
        vm.Execute(chunk);
        Chunk? fnChunk = null;
        foreach (StashValue c in chunk.Constants)
        {
            if (c.AsObj is Chunk ch && ch.Name == "add")
            {
                fnChunk = ch;
                break;
            }
        }
        Assert.NotNull(fnChunk);
        Assert.NotNull(fnChunk!.QuickenCounters);
    }

    [Fact]
    public void Activation_SingleCallFunction_NotQuickened()
    {
        // Functions called only once don't get quickened
        Chunk chunk = CompileSource("fn add(a, b) { return a + b; } add(1, 2);");
        var vm = new VirtualMachine();
        vm.Execute(chunk);
        Chunk? fnChunk = null;
        foreach (StashValue c in chunk.Constants)
        {
            if (c.AsObj is Chunk ch && ch.Name == "add")
            {
                fnChunk = ch;
                break;
            }
        }
        Assert.NotNull(fnChunk);
        Assert.Null(fnChunk!.QuickenCounters);
    }

    // ══════════════════════════ Correctness — Arithmetic ══════════════════════════

    [Fact]
    public void AddII_IntegerAddition_CorrectResult()
    {
        // Ranges are end-exclusive. 0..9 iterates 0,1,...,8 (9 iters).
        // sum += 1 + i for i in {0..8} = 9*1 + (0+1+...+8) = 9+36 = 45
        Assert.Equal(45L, Execute(@"
            let sum = 0;
            for (let i in 0..9) {
                sum = sum + 1 + i;
            }
            return sum;
        "));
    }

    [Fact]
    public void SubII_IntegerSubtraction_CorrectResult()
    {
        // result -= 1 + i for i in {0..8} = -(9+36) = -45
        Assert.Equal(-45L, Execute(@"
            let result = 0;
            for (let i in 0..9) {
                result = result - 1 - i;
            }
            return result;
        "));
    }

    [Fact]
    public void MulII_IntegerMultiplication_CorrectResult()
    {
        // 1..10 iterates 1,2,...,9 -> 9! = 362880
        Assert.Equal(362880L, Execute(@"
            let result = 1;
            for (let i in 1..10) {
                result = result * i;
            }
            return result;
        "));
    }

    [Fact]
    public void DivII_IntegerDivision_CorrectResult()
    {
        // 362880 / 1 / 2 / ... / 9 = 1
        Assert.Equal(1L, Execute(@"
            let result = 362880;
            for (let i in 1..10) {
                result = result / i;
            }
            return result;
        "));
    }

    [Fact]
    public void DivII_DivisionByZero_Throws()
    {
        // Even when specialized, division by zero should throw
        Assert.Throws<RuntimeError>(() => Execute(@"
            let x = 10;
            for (let i in 0..9) {
                x = x / 0;
            }
        "));
    }

    [Fact]
    public void ModII_IntegerModulo_CorrectResult()
    {
        // 100 % 4 = 0 on first iter (i=0, i+4=4), then 0 % anything = 0
        Assert.Equal(0L, Execute(@"
            let result = 100;
            for (let i in 0..10) {
                result = result % (i + 4);
            }
            return result;
        "));
    }

    [Fact]
    public void ModII_ModuloByZero_Throws()
    {
        Assert.Throws<RuntimeError>(() => Execute(@"
            let x = 10;
            for (let i in 0..9) {
                x = x % 0;
            }
        "));
    }

    // ══════════════════════════ Correctness — Comparison ══════════════════════════

    [Fact]
    public void LtII_IntegerLessThan_CorrectResult()
    {
        // 0..10 iterates 0..9; i < 5 matches {0,1,2,3,4} = 5
        Assert.Equal(5L, Execute(@"
            let count = 0;
            for (let i in 0..10) {
                if (i < 5) { count = count + 1; }
            }
            return count;
        "));
    }

    [Fact]
    public void LeII_IntegerLessEqual_CorrectResult()
    {
        // i <= 5 matches {0,1,2,3,4,5} = 6
        Assert.Equal(6L, Execute(@"
            let count = 0;
            for (let i in 0..10) {
                if (i <= 5) { count = count + 1; }
            }
            return count;
        "));
    }

    [Fact]
    public void GtII_IntegerGreaterThan_CorrectResult()
    {
        // i > 5 matches {6,7,8,9} = 4
        Assert.Equal(4L, Execute(@"
            let count = 0;
            for (let i in 0..10) {
                if (i > 5) { count = count + 1; }
            }
            return count;
        "));
    }

    [Fact]
    public void GeII_IntegerGreaterEqual_CorrectResult()
    {
        // i >= 5 matches {5,6,7,8,9} = 5
        Assert.Equal(5L, Execute(@"
            let count = 0;
            for (let i in 0..10) {
                if (i >= 5) { count = count + 1; }
            }
            return count;
        "));
    }

    [Fact]
    public void EqII_IntegerEquality_CorrectResult()
    {
        // i == 5 matches {5} = 1
        Assert.Equal(1L, Execute(@"
            let count = 0;
            for (let i in 0..10) {
                if (i == 5) { count = count + 1; }
            }
            return count;
        "));
    }

    [Fact]
    public void NeII_IntegerNotEqual_CorrectResult()
    {
        // i != 5 matches 9 of {0,1,...,9}
        Assert.Equal(9L, Execute(@"
            let count = 0;
            for (let i in 0..10) {
                if (i != 5) { count = count + 1; }
            }
            return count;
        "));
    }

    // ══════════════════════════ ForLoopII — Guard-Free ══════════════════════════

    [Fact]
    public void ForLoopII_IntegerForLoop_CorrectResult()
    {
        // 0..100 iterates 0..99 -> sum = 0+1+...+99 = 4950
        Assert.Equal(4950L, Execute(@"
            let sum = 0;
            for (let i in 0..100) {
                sum = sum + i;
            }
            return sum;
        "));
    }

    [Fact]
    public void ForLoopII_NegativeStep_CorrectResult()
    {
        // 9..0 auto-detects negative step: iterates 9,8,...,1 (0 excluded)
        Assert.Equal(45L, Execute(@"
            let sum = 0;
            for (let i in 9..0) {
                sum = sum + i;
            }
            return sum;
        "));
    }

    [Fact]
    public void ForLoopII_ZeroIterations_CorrectResult()
    {
        // start > end with explicit positive step -> zero iterations
        Assert.Equal(0L, Execute(@"
            let sum = 0;
            for (let i in 10..0..1) {
                sum = sum + i;
            }
            return sum;
        "));
    }

    [Fact]
    public void ForLoopII_LargeLoop_CorrectResult()
    {
        // 0..1000 iterates 0..999 -> sum = 499500
        Assert.Equal(499500L, Execute(@"
            let sum = 0;
            for (let i in 0..1000) {
                sum = sum + i;
            }
            return sum;
        "));
    }

    [Fact]
    public void ForLoopII_LoopVariableReassignment_NoEffect()
    {
        // Reassigning the loop variable inside the body shouldn't affect iteration
        Assert.Equal(10L, Execute(@"
            let count = 0;
            for (let i in 0..10) {
                i = 999;
                count = count + 1;
            }
            return count;
        "));
    }

    // ══════════════════════════ De-specialization ══════════════════════════

    [Fact]
    public void DeSpecialize_FloatAfterInt_FallsBackCorrectly()
    {
        // Start with ints (triggers specialization), then switch to floats
        // The de-specialized handler should still produce correct results
        Assert.Equal(3.5, Execute(@"
            fn add(a, b) { return a + b; }
            // Call with ints multiple times to trigger specialization
            add(1, 2); add(1, 2); add(1, 2); add(1, 2);
            add(1, 2); add(1, 2); add(1, 2); add(1, 2);
            add(1, 2); add(1, 2);
            // Now call with floats -- should de-specialize and still work
            return add(1.5, 2.0);
        "));
    }

    [Fact]
    public void DeSpecialize_StringAfterInt_FallsBackCorrectly()
    {
        // Add with strings uses RuntimeOps.Add (string concatenation)
        Assert.Equal("hello world", Execute(@"
            fn add(a, b) { return a + b; }
            add(1, 2); add(1, 2); add(1, 2); add(1, 2);
            add(1, 2); add(1, 2); add(1, 2); add(1, 2);
            add(1, 2); add(1, 2);
            return add(""hello "", ""world"");
        "));
    }

    [Fact]
    public void DeSpecialize_ComparisonWithFloat_FallsBackCorrectly()
    {
        Assert.Equal(true, Execute(@"
            fn lt(a, b) { return a < b; }
            lt(1, 2); lt(1, 2); lt(1, 2); lt(1, 2);
            lt(1, 2); lt(1, 2); lt(1, 2); lt(1, 2);
            lt(1, 2); lt(1, 2);
            return lt(1.5, 2.5);
        "));
    }

    [Fact]
    public void ForPrepII_FloatLimit_DeSpecializesCorrectly()
    {
        // C-style for-loop with float limit AFTER int specialization.
        // ForPrepII must guard on the limit type to prevent ForLoopII
        // from calling .AsInt on a float (which returns garbage bits).
        // For `i < limit`, the compiler stores (limit - 1) internally and uses <= semantics.
        // So test(10.5) stores internal limit = 9.5, loop runs i=0..9 → sum = 45.0.
        Assert.Equal(45.0, Execute(@"
            fn test(limit) {
                let sum = 0;
                for (let i = 0; i < limit; i++) {
                    sum = sum + i;
                }
                return sum;
            }
            // Warmup: int calls to trigger specialization
            test(10); test(10); test(10); test(10);
            test(10); test(10); test(10); test(10);
            test(10); test(10);
            // Float limit: should de-specialize and work correctly
            return test(10.5);
        "));
    }

    // ══════════════════════════ Edge Cases ══════════════════════════

    [Fact]
    public void Quickening_RecursiveFunction_CorrectResult()
    {
        Assert.Equal(55L, Execute(@"
            fn fib(n) {
                if (n <= 1) { return n; }
                return fib(n - 1) + fib(n - 2);
            }
            return fib(10);
        "));
    }

    [Fact]
    public void Quickening_NestedLoops_CorrectResult()
    {
        // 0..10 x 0..10 = 10 x 10 = 100 iterations
        Assert.Equal(100L, Execute(@"
            let count = 0;
            for (let i in 0..10) {
                for (let j in 0..10) {
                    count = count + 1;
                }
            }
            return count;
        "));
    }

    [Fact]
    public void Quickening_MixedTypesInLoop_CorrectResult()
    {
        // 0..20 iterates 0..19 -> sum = 0+1+...+19 = 190
        Assert.Equal(true, Execute(@"
            let sum = 0;
            for (let i in 0..20) {
                sum = sum + i;
            }
            return sum == 190;
        "));
    }

    [Fact]
    public void Quickening_TryCatchInLoop_CorrectResult()
    {
        // 0..10 iterates 0..9; skip i==5, count the rest = 9
        Assert.Equal(9L, Execute(@"
            let sum = 0;
            for (let i in 0..10) {
                try {
                    if (i == 5) {
                        throw ""skip"";
                    }
                    sum = sum + 1;
                } catch (e) {
                    // skip 5
                }
            }
            return sum;
        "));
    }

    // ══════════════════════════ PatchOp Instruction Test ══════════════════════════

    [Fact]
    public void PatchOp_PreservesOperands()
    {
        // Verify PatchOp preserves all operand fields
        uint original = Instruction.EncodeABC(OpCode.Add, 3, 7, 11);
        uint patched = Instruction.PatchOp(original, OpCode.AddII);

        Assert.Equal(OpCode.AddII, Instruction.GetOp(patched));
        Assert.Equal((byte)3, Instruction.GetA(patched));
        Assert.Equal((byte)7, Instruction.GetB(patched));
        Assert.Equal((byte)11, Instruction.GetC(patched));
    }

    [Fact]
    public void PatchOp_AsBxFormat_PreservesOperands()
    {
        uint original = Instruction.EncodeAsBx(OpCode.ForPrep, 5, 42);
        uint patched = Instruction.PatchOp(original, OpCode.ForPrepII);

        Assert.Equal(OpCode.ForPrepII, Instruction.GetOp(patched));
        Assert.Equal((byte)5, Instruction.GetA(patched));
        Assert.Equal(42, Instruction.GetSBx(patched));
    }
}
