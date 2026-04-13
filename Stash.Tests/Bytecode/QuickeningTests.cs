using Stash.Bytecode;
using Stash.Runtime;
using Stash.Runtime.Types;
using System.Collections.Generic;
using Xunit;

namespace Stash.Tests.Bytecode;

public class QuickeningTests : BytecodeTestBase
{
    // =========================================================================
    // 1. Correctness — Arithmetic quickening produces correct results
    // =========================================================================

    [Fact]
    public void Quickening_IntAddition_ProducesCorrectResult()
    {
        string source = """
            fn add_loop() {
                let sum = 0;
                for (let i = 0; i < 20; i++) {
                    sum = sum + i;
                }
                return sum;
            }
            return add_loop() + add_loop();
            """;
        // sum = 0+1+2+...+19 = 190, called twice = 380
        Assert.Equal(380L, Execute(source));
    }

    [Fact]
    public void Quickening_IntSubtraction_ProducesCorrectResult()
    {
        string source = """
            fn sub_loop() {
                let acc = 100;
                for (let i = 1; i <= 10; i++) {
                    acc = acc - i;
                }
                return acc;
            }
            return sub_loop() + sub_loop();
            """;
        // acc = 100 - (1+2+...+10) = 100 - 55 = 45, called twice = 90
        Assert.Equal(90L, Execute(source));
    }

    [Fact]
    public void Quickening_IntMultiplication_ProducesCorrectResult()
    {
        string source = """
            fn mul_loop() {
                let sum = 0;
                for (let i = 0; i < 10; i++) {
                    sum = sum + i * 3;
                }
                return sum;
            }
            return mul_loop() + mul_loop();
            """;
        // sum = 3 * (0+1+...+9) = 3*45 = 135, called twice = 270
        Assert.Equal(270L, Execute(source));
    }

    [Fact]
    public void Quickening_IntDivision_ProducesCorrectResult()
    {
        string source = """
            fn div_loop() {
                let acc = 0;
                for (let i = 0; i < 10; i++) {
                    acc = acc + (i + 1) * 10 / 5;
                }
                return acc;
            }
            return div_loop() + div_loop();
            """;
        // (i+1)*10/5 = (i+1)*2: sum for i=0..9 is 2+4+6+8+10+12+14+16+18+20 = 110, called twice = 220
        Assert.Equal(220L, Execute(source));
    }

    [Fact]
    public void Quickening_IntModulo_ProducesCorrectResult()
    {
        string source = """
            fn mod_loop() {
                let sum = 0;
                for (let i = 0; i < 20; i++) {
                    sum = sum + i % 3;
                }
                return sum;
            }
            return mod_loop() + mod_loop();
            """;
        // i%3 for i=0..19: cycles (0+1+2)*6 + (0+1) = 18+1 = 19, called twice = 38
        Assert.Equal(38L, Execute(source));
    }

    // =========================================================================
    // 2. Correctness — Comparison quickening produces correct results
    // =========================================================================

    [Fact]
    public void Quickening_AllComparisons_ProduceCorrectResults()
    {
        // Verify each comparison op individually after warming it up past the quickening threshold
        static object? Warm(string cmpExpr)
        {
            string src = $$"""
                fn cmp(a, b) { return {{cmpExpr}}; }
                let r = null; r = null;
                for (let i = 0; i < 10; i++) { r = cmp(3, 5); }
                return r;
                """;
            return Execute(src);
        }

        Assert.Equal(true,  Warm("a < b"));   // 3 < 5
        Assert.Equal(true,  Warm("a <= b"));  // 3 <= 5
        Assert.Equal(false, Warm("a > b"));   // 3 > 5
        Assert.Equal(false, Warm("a >= b"));  // 3 >= 5
        Assert.Equal(false, Warm("a == b"));  // 3 == 5
        Assert.Equal(true,  Warm("a != b"));  // 3 != 5
    }

    [Fact]
    public void Quickening_EqNeWithEqualValues_ProducesCorrectResults()
    {
        string source = """
            fn check_eq(a, b) {
                return a == b;
            }
            let r = null; r = null;
            for (let i = 0; i < 10; i++) {
                r = check_eq(7, 7);
            }
            return r;
            """;
        Assert.Equal(true, Execute(source));
    }

    [Fact]
    public void Quickening_LtGtSymmetry_ProducesCorrectResults()
    {
        string source = """
            fn lt_count(limit) {
                let count = 0;
                for (let i = 0; i < 20; i++) {
                    if (i < limit) {
                        count = count + 1;
                    }
                }
                return count;
            }
            return lt_count(10) + lt_count(10);
            """;
        // i < 10 for i=0..19: 10 values per call, called twice = 20
        Assert.Equal(20L, Execute(source));
    }

    // =========================================================================
    // 3. De-specialization — Mixed types fall back correctly
    // =========================================================================

    [Fact]
    public void Quickening_MixedTypes_DespecializesCorrectly()
    {
        string source = """
            fn mixed(x, y) {
                return x + y;
            }
            let result = null; result = 0;
            for (let i = 0; i < 10; i++) {
                result = mixed(i, 1);
            }
            return mixed(1.5, 2.5);
            """;
        Assert.Equal(4.0, Execute(source));
    }

    [Fact]
    public void Quickening_StringAddAfterIntWarmup_StillWorks()
    {
        string source = """
            fn add_things(a, b) {
                return a + b;
            }
            for (let i = 0; i < 10; i++) {
                add_things(i, 1);
            }
            return add_things("hello", " world");
            """;
        Assert.Equal("hello world", Execute(source));
    }

    // =========================================================================
    // 4. ForLoop quickening — Integer for-loops specialize
    // =========================================================================

    [Fact]
    public void Quickening_IntForLoop_ProducesCorrectResult()
    {
        string source = """
            fn sum_range(n) {
                let sum = 0;
                for (let i = 1; i <= n; i++) {
                    sum = sum + i;
                }
                return sum;
            }
            return sum_range(100) + sum_range(100);
            """;
        // sum 1..100 = 5050, called twice = 10100
        Assert.Equal(10100L, Execute(source));
    }

    [Fact]
    public void Quickening_NegativeStepForLoop_WorksCorrectly()
    {
        string source = """
            fn countdown(start) {
                let sum = 0;
                for (let i = start; i > 0; i--) {
                    sum = sum + i;
                }
                return sum;
            }
            return countdown(10) + countdown(10);
            """;
        // sum 10+9+...+1 = 55, called twice = 110
        Assert.Equal(110L, Execute(source));
    }

    [Fact]
    public void Quickening_NestedLoops_ProducesCorrectResult()
    {
        string source = """
            fn matrix_sum(n) {
                let sum = 0;
                for (let i = 0; i < n; i++) {
                    for (let j = 0; j < n; j++) {
                        sum = sum + i * j;
                    }
                }
                return sum;
            }
            return matrix_sum(10) + matrix_sum(10);
            """;
        // sum(i*j for i in 0..9, j in 0..9) = (0+1+...+9)^2 = 45*45 = 2025, called twice = 4050
        Assert.Equal(4050L, Execute(source));
    }

    // =========================================================================
    // 5. Error handling after specialization
    // =========================================================================

    [Fact]
    public void Quickening_DivisionByZero_ThrowsAfterSpecialization()
    {
        string source = """
            fn divide(a, b) {
                return a / b;
            }
            for (let i = 0; i < 10; i++) {
                divide(10, 2);
            }
            return divide(10, 0);
            """;
        Assert.Throws<RuntimeError>(() => Execute(source));
    }

    [Fact]
    public void Quickening_ModuloByZero_ThrowsAfterSpecialization()
    {
        string source = """
            fn modulo(a, b) {
                return a % b;
            }
            for (let i = 0; i < 10; i++) {
                modulo(10, 3);
            }
            return modulo(10, 0);
            """;
        Assert.Throws<RuntimeError>(() => Execute(source));
    }

    // =========================================================================
    // 6. Activation semantics
    // =========================================================================

    [Fact]
    public void Quickening_NamedFunction_ActivatesOnSecondCall()
    {
        string source = """
            fn compute(n) {
                let sum = 0;
                for (let i = 0; i < n; i++) {
                    sum = sum + i * 2;
                }
                return sum;
            }
            let a = null; a = compute(50);
            let b = null; b = compute(50);
            return a + b;
            """;
        // sum(i*2 for i=0..49) = 2*(0+1+...+49) = 2*1225 = 2450, called twice = 4900
        Assert.Equal(4900L, Execute(source));
    }

    // =========================================================================
    // 7. ForPrep compile-time specialization
    // =========================================================================

    [Fact]
    public void ForPrepII_IntegerForLoop_ProducesCorrectResult()
    {
        string source = """
            let sum = 0;
            for (let i = 0; i < 100; i++) {
                sum = sum + i;
            }
            return sum;
            """;
        Assert.Equal(4950L, Execute(source));
    }

    [Fact]
    public void ForPrepII_CompileTimeSpecialization_HandlesFloatFallback()
    {
        string source = """
            let sum = 0.0;
            for (let i = 0; i < 10; i++) {
                sum = sum + i * 1.5;
            }
            return sum;
            """;
        // 0*1.5 + 1*1.5 + ... + 9*1.5 = 1.5 * 45 = 67.5
        Assert.Equal(67.5, Execute(source));
    }

    // =========================================================================
    // 8. Additional — Large counts, multiple ops, modulo patterns
    // =========================================================================

    [Fact]
    public void Quickening_LargeIterationCount_ProducesCorrectResult()
    {
        string source = """
            fn large_loop() {
                let sum = 0;
                for (let i = 0; i < 1000; i++) {
                    sum = sum + i;
                }
                return sum;
            }
            return large_loop();
            """;
        // sum 0..999 = 999*1000/2 = 499500
        Assert.Equal(499500L, Execute(source));
    }

    [Fact]
    public void Quickening_MultipleArithOpsInSameFunction_ProducesCorrectResult()
    {
        string source = """
            fn mixed_ops(n) {
                let a = 0;
                let b = 0;
                let c = 0;
                for (let i = 1; i <= n; i++) {
                    a = a + i;
                    b = b + i * 2;
                    c = c + i - 1;
                }
                return a + b + c;
            }
            return mixed_ops(10) + mixed_ops(10);
            """;
        // a = 1+2+...+10 = 55; b = 2*(1+...+10) = 110; c = (0+1+...+9) = 45
        // per call: 55+110+45 = 210, called twice = 420
        Assert.Equal(420L, Execute(source));
    }

    [Fact]
    public void Quickening_IntModuloByConstant_ProducesCorrectResult()
    {
        string source = """
            fn count_even(n) {
                let count = 0;
                for (let i = 0; i < n; i++) {
                    if (i % 2 == 0) {
                        count = count + 1;
                    }
                }
                return count;
            }
            return count_even(20) + count_even(20);
            """;
        // even numbers in 0..19: 0,2,4,6,8,10,12,14,16,18 = 10, called twice = 20
        Assert.Equal(20L, Execute(source));
    }

    [Fact]
    public void Quickening_ForLoopSumWithLargeBound_ProducesCorrectResult()
    {
        string source = """
            fn gauss(n) {
                let sum = 0;
                for (let i = 0; i <= n; i++) {
                    sum = sum + i;
                }
                return sum;
            }
            return gauss(200);
            """;
        // sum 0..200 = 200*201/2 = 20100
        Assert.Equal(20100L, Execute(source));
    }
}
