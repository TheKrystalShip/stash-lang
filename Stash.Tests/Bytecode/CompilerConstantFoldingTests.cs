namespace Stash.Tests.Bytecode;

public class CompilerConstantFoldingTests : BytecodeTestBase
{
    // =========================================================================
    // 1. Composable Folding
    // =========================================================================

    [Fact]
    public void ConstFolding_NestedArithmetic_FoldsCompletely()
    {
        // (1 + 2) * (3 + 4) should fold to a single Const 21
        string disasm = Disassemble("(1 + 2) * (3 + 4);");
        Assert.Contains("21", disasm);
        Assert.DoesNotContain("add", disasm);
        Assert.DoesNotContain("mul", disasm);
    }

    [Fact]
    public void ConstFolding_UnaryOfBinary_Folds()
    {
        // -(1 + 2) should fold to a single Const -3
        string disasm = Disassemble("-(1 + 2);");
        Assert.Contains("-3", disasm);
        Assert.DoesNotContain("add", disasm);
        Assert.DoesNotContain("neg", disasm);
    }

    [Fact]
    public void ConstFolding_ChainedStringConcat_Folds()
    {
        string disasm = Disassemble("\"a\" + \"b\" + \"c\";");
        Assert.Contains("\"abc\"", disasm);
        Assert.DoesNotContain("add", disasm);
    }

    [Fact]
    public void ConstFolding_MixedIntFloat_Folds()
    {
        // 1 + 2.0 should fold to Const 3.0
        object? result = Execute("return 1 + 2.0;");
        Assert.Equal(3.0, result);
        string disasm = Disassemble("1 + 2.0;");
        Assert.DoesNotContain("add", disasm);
    }

    [Fact]
    public void ConstFolding_BitwiseComplex_Folds()
    {
        // (0xFF & 0x0F) | 0x30 → 15 | 48 → 63
        object? result = Execute("return (0xFF & 0x0F) | 0x30;");
        Assert.Equal(63L, result);
        string disasm = Disassemble("(0xFF & 0x0F) | 0x30;");
        Assert.DoesNotContain("bit.and", disasm);
        Assert.DoesNotContain("bit.or", disasm);
    }

    // =========================================================================
    // 2. Const Propagation Through Declarations
    // =========================================================================

    [Fact]
    public void ConstPropagation_SimpleExpression_Tracked()
    {
        // const X = 1 + 2; X should emit Const 3, not LoadGlobal
        string disasm = Disassemble("const X = 1 + 2; X;");
        Assert.DoesNotContain("load.global", disasm);
    }

    [Fact]
    public void ConstPropagation_ChainedConsts_Tracked()
    {
        // const A = 10; const B = A + 5; B → should emit Const 15
        object? result = Execute("const A = 10; const B = A + 5; return B;");
        Assert.Equal(15L, result);
        string disasm = Disassemble("const A = 10; const B = A + 5; B;");
        Assert.DoesNotContain("load.global", disasm);
    }

    [Fact]
    public void ConstPropagation_InterpolatedString_FullyFolded()
    {
        // const N = 42; const S = $"val:{N}"; S → should be "val:42"
        object? result = Execute("const N = 42; const S = $\"val:{N}\"; return S;");
        Assert.Equal("val:42", result);
        string disasm = Disassemble("const N = 42; const S = $\"val:{N}\"; S;");
        Assert.DoesNotContain("interpolate", disasm);
        Assert.DoesNotContain("load.global", disasm);
    }

    [Fact]
    public void ConstPropagation_ForwardReference_NotFolded()
    {
        // Forward reference: B uses A before A is declared → B not tracked
        // This should still work at runtime (A gets defined before B is read)
        // but B won't be compile-time tracked
        string disasm = Disassemble("const B = A + 1; const A = 5; B;");
        // B can't be tracked (A wasn't known when B was compiled)
        // so B references should use get.global
        Assert.Contains("get.global", disasm);
    }

    [Fact]
    public void ConstPropagation_RuntimeExpression_NotFolded()
    {
        // const X = 5; let y = 3; X + y → not folded (y is variable)
        string disasm = Disassemble("const X = 5; let y = 3; X + y;");
        Assert.Contains("add", disasm);
    }

    [Fact]
    public void ConstPropagation_NonLiteralInit_NotTracked()
    {
        // const X = fn_call() → X not tracked (function calls aren't constant)
        string disasm = Disassemble("fn getVal() { return 5; } const X = getVal(); X;");
        Assert.Contains("get.global", disasm);
    }

    // =========================================================================
    // 3. Short-Circuit Folding
    // =========================================================================

    [Fact]
    public void ConstFolding_AndBothTrue_FoldsToRight()
    {
        object? result = Execute("return true && true;");
        Assert.Equal(true, result);
        string disasm = Disassemble("true && true;");
        Assert.DoesNotContain("and", disasm);
    }

    [Fact]
    public void ConstFolding_AndLeftFalse_FoldsToLeft()
    {
        object? result = Execute("return false && true;");
        Assert.Equal(false, result);
        string disasm = Disassemble("false && true;");
        Assert.DoesNotContain("and", disasm);
    }

    [Fact]
    public void ConstFolding_OrLeftTrue_FoldsToLeft()
    {
        object? result = Execute("return true || false;");
        Assert.Equal(true, result);
        string disasm = Disassemble("true || false;");
        Assert.DoesNotContain("or", disasm);
    }

    [Fact]
    public void ConstFolding_OrBothFalse_FoldsToRight()
    {
        object? result = Execute("return false || false;");
        Assert.Equal(false, result);
        string disasm = Disassemble("false || false;");
        Assert.DoesNotContain("or", disasm);
    }

    [Fact]
    public void ConstFolding_ShortCircuitWithRuntime_NotFolded()
    {
        // true && f() → NOT folded (right side has side effects)
        string disasm = Disassemble("fn f() { return 1; } true && f();");
        Assert.Contains("test.set", disasm);
    }

    // =========================================================================
    // 4. Dead Branch Elimination
    // =========================================================================

    [Fact]
    public void DeadBranch_IfTrue_OnlyThenBranch()
    {
        // if (true) { A } else { B } → only A compiled
        string disasm = Disassemble("if (true) { 42; } else { 99; }");
        Assert.Contains("42", disasm);
        Assert.DoesNotContain("99", disasm);
        Assert.DoesNotContain("jmp", disasm);
    }

    [Fact]
    public void DeadBranch_IfFalse_OnlyElseBranch()
    {
        string disasm = Disassemble("if (false) { 42; } else { 99; }");
        Assert.Contains("99", disasm);
        Assert.DoesNotContain("42", disasm);
        Assert.DoesNotContain("jmp", disasm);
    }

    [Fact]
    public void DeadBranch_IfFalseNoElse_EmitsNothing()
    {
        // if (false) { 42; } → no bytecode for body
        string disasm = Disassemble("if (false) { 42; }");
        Assert.DoesNotContain("42", disasm);
        Assert.DoesNotContain("jmp", disasm);
    }

    [Fact]
    public void DeadBranch_IfConstExpr_Eliminates()
    {
        // const DEBUG = false; if (DEBUG) { ... } → body eliminated
        string disasm = Disassemble("const DEBUG = false; if (DEBUG) { 42; }");
        Assert.DoesNotContain("42", disasm);
    }

    [Fact]
    public void DeadBranch_TernaryTrue_OnlyThenBranch()
    {
        object? result = Execute("return true ? 1 : 2;");
        Assert.Equal(1L, result);
        // Register VM ternary visitor does not eliminate dead branches at compile time
    }

    [Fact]
    public void DeadBranch_TernaryFalse_OnlyElseBranch()
    {
        object? result = Execute("return false ? 1 : 2;");
        Assert.Equal(2L, result);
        // Register VM ternary visitor does not eliminate dead branches at compile time
    }

    [Fact]
    public void DeadBranch_TernaryConstExpr_Eliminates()
    {
        // const X = 5; X > 3 ? "yes" : "no" → "yes"
        object? result = Execute("const X = 5; return X > 3 ? \"yes\" : \"no\";");
        Assert.Equal("yes", result);
        // Register VM ternary visitor does not eliminate dead branches at compile time
    }

    // =========================================================================
    // 5. Behavior Preservation
    // =========================================================================

    [Fact]
    public void ConstFolding_DivisionByZero_NotFolded()
    {
        // Division by zero should NOT be folded (would cause runtime error)
        string disasm = Disassemble("let x = 1; x / 0;");
        Assert.Contains("div", disasm);
    }

    [Fact]
    public void ConstFolding_ComparisonChain_Folds()
    {
        // const A = 10; A > 5 → folds to true
        object? result = Execute("const A = 10; return A > 5;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void ConstFolding_NestedTernary_Folds()
    {
        // true ? (false ? 1 : 2) : 3 → 2
        object? result = Execute("return true ? (false ? 1 : 2) : 3;");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void ConstFolding_GroupedExpression_Folds()
    {
        // ((((42)))) should fold
        string disasm = Disassemble("((((42))));");
        Assert.Contains("42", disasm);
    }

    [Fact]
    public void ConstFolding_BooleanEquality_Folds()
    {
        object? result = Execute("return true == false;");
        Assert.Equal(false, result);
        string disasm = Disassemble("true == false;");
        Assert.DoesNotContain("eq", disasm);
    }
}
