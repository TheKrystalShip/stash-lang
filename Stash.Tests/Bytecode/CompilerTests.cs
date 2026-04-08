using Stash.Bytecode;
using Stash.Runtime;

namespace Stash.Tests.Bytecode;

public class CompilerTests : BytecodeTestBase
{
    /// <summary>
    /// Compile source and return the disassembly string.
    /// </summary>
    private static string Disassemble(string source)
    {
        Chunk chunk = CompileSource(source);
        return Disassembler.Disassemble(chunk);
    }

    /// <summary>
    /// Helper to check that specific opcode sequences appear in the disassembly.
    /// </summary>
    private static void AssertContainsOpcodes(string disasm, params string[] expectedOpcodes)
    {
        foreach (string op in expectedOpcodes)
            Assert.Contains(op, disasm);
    }

    private static int CountOccurrences(string text, string searchTerm)
    {
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf(searchTerm, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += searchTerm.Length;
        }
        return count;
    }

    // =========================================================================
    // 1. Literal Expressions
    // =========================================================================

    [Fact]
    public void Literal_IntegerConstant_EmitsConst()
    {
        string disasm = Disassemble("42;");
        Assert.Contains("const", disasm);
        Assert.Contains("42", disasm);
        Assert.Contains("pop", disasm);
    }

    [Fact]
    public void Literal_Null_EmitsNull()
    {
        string disasm = Disassemble("null;");
        Assert.Contains("null", disasm);
    }

    [Fact]
    public void Literal_True_EmitsTrue()
    {
        string disasm = Disassemble("true;");
        Assert.Contains("true", disasm);
    }

    [Fact]
    public void Literal_False_EmitsFalse()
    {
        string disasm = Disassemble("false;");
        Assert.Contains("false", disasm);
    }

    [Fact]
    public void Literal_String_EmitsConst()
    {
        string disasm = Disassemble("\"hello\";");
        Assert.Contains("const", disasm);
        Assert.Contains("\"hello\"", disasm);
    }

    [Fact]
    public void Literal_Float_EmitsConst()
    {
        string disasm = Disassemble("3.14;");
        Assert.Contains("const", disasm);
        Assert.Contains("3.14", disasm);
    }

    // =========================================================================
    // 2. Unary Expressions
    // =========================================================================

    [Fact]
    public void Unary_Negate_EmitsNegate()
    {
        string disasm = Disassemble("-42;");
        Assert.Contains("const", disasm);
        Assert.Contains("neg", disasm);
    }

    [Fact]
    public void Unary_LogicalNot_EmitsNot()
    {
        string disasm = Disassemble("!true;");
        Assert.Contains("true", disasm);
        Assert.Contains("not", disasm);
    }

    [Fact]
    public void Unary_BitwiseNot_EmitsBitNot()
    {
        string disasm = Disassemble("~42;");
        Assert.Contains("const", disasm);
        Assert.Contains("bit.not", disasm);
    }

    // =========================================================================
    // 3. Binary Expressions (Arithmetic)
    // =========================================================================

    [Fact]
    public void Binary_Addition_EmitsAdd()
    {
        string disasm = Disassemble("1 + 2;");
        AssertContainsOpcodes(disasm, "const", "add", "pop");
    }

    [Fact]
    public void Binary_Subtraction_EmitsSubtract()
    {
        string disasm = Disassemble("5 - 3;");
        Assert.Contains("sub", disasm);
    }

    [Fact]
    public void Binary_Multiplication_EmitsMultiply()
    {
        string disasm = Disassemble("2 * 3;");
        Assert.Contains("mul", disasm);
    }

    [Fact]
    public void Binary_Division_EmitsDivide()
    {
        string disasm = Disassemble("10 / 2;");
        Assert.Contains("div", disasm);
    }

    [Fact]
    public void Binary_Modulo_EmitsModulo()
    {
        string disasm = Disassemble("10 % 3;");
        Assert.Contains("mod", disasm);
    }

    // =========================================================================
    // 4. Comparison and Equality
    // =========================================================================

    [Fact]
    public void Binary_Equal_EmitsEqual()
    {
        string disasm = Disassemble("1 == 2;");
        Assert.Contains("eq", disasm);
    }

    [Fact]
    public void Binary_NotEqual_EmitsNotEqual()
    {
        string disasm = Disassemble("1 != 2;");
        Assert.Contains("neq", disasm);
    }

    [Fact]
    public void Binary_LessThan_EmitsLessThan()
    {
        string disasm = Disassemble("1 < 2;");
        Assert.Contains("lt", disasm);
    }

    [Fact]
    public void Binary_GreaterThan_EmitsGreaterThan()
    {
        string disasm = Disassemble("1 > 2;");
        Assert.Contains("gt", disasm);
    }

    [Fact]
    public void Binary_LessEqual_EmitsLessEqual()
    {
        string disasm = Disassemble("1 <= 2;");
        Assert.Contains("le", disasm);
    }

    [Fact]
    public void Binary_GreaterEqual_EmitsGreaterEqual()
    {
        string disasm = Disassemble("1 >= 2;");
        Assert.Contains("ge", disasm);
    }

    // =========================================================================
    // 5. Bitwise Operators
    // =========================================================================

    [Fact]
    public void Binary_BitwiseAnd_EmitsBitAnd()
    {
        string disasm = Disassemble("5 & 3;");
        Assert.Contains("bit.and", disasm);
    }

    [Fact]
    public void Binary_BitwiseOr_EmitsBitOr()
    {
        string disasm = Disassemble("5 | 3;");
        Assert.Contains("bit.or", disasm);
    }

    [Fact]
    public void Binary_BitwiseXor_EmitsBitXor()
    {
        string disasm = Disassemble("5 ^ 3;");
        Assert.Contains("bit.xor", disasm);
    }

    [Fact]
    public void Binary_ShiftLeft_EmitsShiftLeft()
    {
        string disasm = Disassemble("1 << 3;");
        Assert.Contains("shl", disasm);
    }

    [Fact]
    public void Binary_ShiftRight_EmitsShiftRight()
    {
        string disasm = Disassemble("8 >> 2;");
        Assert.Contains("shr", disasm);
    }

    // =========================================================================
    // 6. Short-Circuit Logic
    // =========================================================================

    [Fact]
    public void Logic_And_EmitsAndWithJump()
    {
        string disasm = Disassemble("true && false;");
        Assert.Contains("true", disasm);
        Assert.Contains("and", disasm);
        Assert.Contains("false", disasm);
        Assert.Contains("->", disasm);
    }

    [Fact]
    public void Logic_Or_EmitsOrWithJump()
    {
        string disasm = Disassemble("false || true;");
        Assert.Contains("false", disasm);
        Assert.Contains("or", disasm);
        Assert.Contains("true", disasm);
    }

    // =========================================================================
    // 7. Null Coalescing
    // =========================================================================

    [Fact]
    public void NullCoalesce_EmitsNullCoalesceWithJump()
    {
        string disasm = Disassemble("null ?? 42;");
        Assert.Contains("null", disasm);
        Assert.Contains("null.coal", disasm);
        Assert.Contains("42", disasm);
    }

    // =========================================================================
    // 8. Ternary
    // =========================================================================

    [Fact]
    public void Ternary_EmitsConditionAndJumps()
    {
        string disasm = Disassemble("true ? 1 : 2;");
        Assert.Contains("true", disasm);
        Assert.Contains("jmp.false", disasm);
        Assert.Contains("jmp", disasm);
    }

    // =========================================================================
    // 9. Variable Declarations
    // =========================================================================

    [Fact]
    public void VarDecl_WithInitializer_EmitsValue()
    {
        string disasm = Disassemble("let x = 42;");
        Assert.Contains("const", disasm);
        Assert.Contains("42", disasm);
    }

    [Fact]
    public void VarDecl_WithoutInitializer_EmitsNull()
    {
        string disasm = Disassemble("let x;");
        Assert.Contains("null", disasm);
    }

    [Fact]
    public void ConstDecl_EmitsValue()
    {
        string disasm = Disassemble("const x = 42;");
        Assert.Contains("const", disasm);
        Assert.Contains("42", disasm);
    }

    [Fact]
    public void VarDecl_LocalCount_IsTracked()
    {
        Chunk chunk = CompileSource("let x = 1; let y = 2;");
        Assert.Equal(2, chunk.LocalCount);
    }

    // =========================================================================
    // 10. Variable Access
    // =========================================================================

    [Fact]
    public void Identifier_Local_EmitsLoadLocal()
    {
        // Top-level variables are global; use a function body to get LoadLocal
        Chunk chunk = CompileSource("fn foo() { let x = 42; x; }");
        Chunk? fnChunk = null;
        foreach (StashValue c in chunk.Constants)
            if (c.AsObj is Chunk fc) { fnChunk = fc; break; }
        Assert.NotNull(fnChunk);
        Assert.Contains("load.local", Disassembler.Disassemble(fnChunk));
    }

    [Fact]
    public void Assign_Local_EmitsStoreLocal()
    {
        // Top-level variables are global; use a function body to get StoreLocal
        Chunk chunk = CompileSource("fn foo() { let x = 1; x = 2; }");
        Chunk? fnChunk = null;
        foreach (StashValue c in chunk.Constants)
            if (c.AsObj is Chunk fc) { fnChunk = fc; break; }
        Assert.NotNull(fnChunk);
        string fnDisasm = Disassembler.Disassemble(fnChunk);
        Assert.True(fnDisasm.Contains("store.local") || fnDisasm.Contains("dup.store.pop"), "Expected store.local or dup.store.pop");
        Assert.Contains("dup", fnDisasm);
    }

    // =========================================================================
    // 11. Block Scoping
    // =========================================================================

    [Fact]
    public void Block_PopsLocalsOnExit()
    {
        string disasm = Disassemble("{ let x = 1; let y = 2; }");
        int popCount = CountOccurrences(disasm, "pop");
        Assert.True(popCount >= 2, $"Expected at least 2 Pop instructions, found {popCount}");
    }

    // =========================================================================
    // 12. If/Else
    // =========================================================================

    [Fact]
    public void If_WithoutElse_EmitsJumpFalse()
    {
        string disasm = Disassemble("if (true) { 42; }");
        Assert.Contains("true", disasm);
        Assert.Contains("jmp.false", disasm);
    }

    [Fact]
    public void If_WithElse_EmitsJumpFalseAndJump()
    {
        string disasm = Disassemble("if (true) { 1; } else { 2; }");
        Assert.Contains("jmp.false", disasm);
        Assert.Contains("jmp", disasm);
    }

    // =========================================================================
    // 13. While Loop
    // =========================================================================

    [Fact]
    public void While_EmitsLoopAndJumpFalse()
    {
        string disasm = Disassemble("let x = 0; while (x < 10) { x = x + 1; }");
        Assert.Contains("loop", disasm);
        Assert.True(disasm.Contains("jmp.false") || disasm.Contains("jmp.lt.false"), "Expected jmp.false or jmp.lt.false");
        Assert.Contains("lt", disasm);
    }

    // =========================================================================
    // 14. Do-While Loop
    // =========================================================================

    [Fact]
    public void DoWhile_EmitsLoopAfterCondition()
    {
        string disasm = Disassemble("let x = 0; do { x = x + 1; } while (x < 10);");
        Assert.Contains("loop", disasm);
        Assert.True(disasm.Contains("jmp.false") || disasm.Contains("jmp.lt.false"), "Expected jmp.false or jmp.lt.false");
    }

    // =========================================================================
    // 15. For Loop
    // =========================================================================

    [Fact]
    public void For_EmitsInitConditionBodyIncrementLoop()
    {
        string disasm = Disassemble("for (let i = 0; i < 10; i = i + 1) { 42; }");
        Assert.Contains("loop", disasm);
        Assert.Contains("jmp.false", disasm);
        Assert.Contains("lt", disasm);
    }

    // =========================================================================
    // 16. Break and Continue
    // =========================================================================

    [Fact]
    public void Break_EmitsJump()
    {
        string disasm = Disassemble("while (true) { break; }");
        Assert.Contains("jmp", disasm);
        Assert.Contains("loop", disasm);
    }

    [Fact]
    public void Continue_InWhile_EmitsLoop()
    {
        string disasm = Disassemble("let x = 0; while (x < 10) { x = x + 1; continue; }");
        int loopCount = CountOccurrences(disasm, "loop");
        Assert.True(loopCount >= 2, $"Expected >= 2 Loop instructions, found {loopCount}");
    }

    [Fact]
    public void Break_OutsideLoop_ThrowsCompileError()
    {
        Assert.Throws<CompileError>(() => CompileSource("break;"));
    }

    [Fact]
    public void Continue_OutsideLoop_ThrowsCompileError()
    {
        Assert.Throws<CompileError>(() => CompileSource("continue;"));
    }

    // =========================================================================
    // 17. Return
    // =========================================================================

    [Fact]
    public void Return_WithValue_EmitsValueAndReturn()
    {
        string disasm = Disassemble("fn foo() { return 42; } foo();");
        Assert.Contains("ret", disasm);
    }

    [Fact]
    public void Return_WithoutValue_EmitsNullAndReturn()
    {
        string disasm = Disassemble("fn foo() { return; } foo();");
        Assert.Contains("null", disasm);
        Assert.Contains("ret", disasm);
    }

    // =========================================================================
    // 18. Function Declarations
    // =========================================================================

    [Fact]
    public void FnDecl_EmitsClosure()
    {
        string disasm = Disassemble("fn add(a, b) { return a + b; }");
        Assert.Contains("closure", disasm);
    }

    [Fact]
    public void FnDecl_NestedChunkHasCorrectArity()
    {
        Chunk chunk = CompileSource("fn add(a, b) { return a + b; }");
        Chunk? fnChunk = null;
        foreach (StashValue constant in chunk.Constants)
        {
            if (constant.AsObj is Chunk c)
            {
                fnChunk = c;
                break;
            }
        }
        Assert.NotNull(fnChunk);
        Assert.Equal(2, fnChunk.Arity);
        Assert.Equal("add", fnChunk.Name);
    }

    [Fact]
    public void FnDecl_BodyContainsLoadLocalForParams()
    {
        Chunk chunk = CompileSource("fn add(a, b) { return a + b; }");
        Chunk? fnChunk = null;
        foreach (StashValue constant in chunk.Constants)
        {
            if (constant.AsObj is Chunk c) { fnChunk = c; break; }
        }
        Assert.NotNull(fnChunk);
        string fnDisasm = Disassembler.Disassemble(fnChunk);
        // Optimizer may fuse LoadLocal+LoadLocal+Add → ll.add and LoadLocal+Return → ret.local
        Assert.True(fnDisasm.Contains("load.local") || fnDisasm.Contains("ll.add"), "Expected load.local or ll.add in disassembly");
        Assert.True(fnDisasm.Contains("add") || fnDisasm.Contains("ll.add"), "Expected add (standalone or in ll.add) in disassembly");
        Assert.True(fnDisasm.Contains("ret"), "Expected ret (standalone or in ret.local) in disassembly");
    }

    // =========================================================================
    // 19. Function Calls
    // =========================================================================

    [Fact]
    public void Call_EmitsCallWithArgCount()
    {
        string disasm = Disassemble("fn foo(x) { return x; } foo(42);");
        Assert.Contains("call", disasm);
    }

    // =========================================================================
    // 20. Lambda Expressions
    // =========================================================================

    [Fact]
    public void Lambda_ExpressionBody_EmitsClosure()
    {
        string disasm = Disassemble("let f = (x) => x + 1;");
        Assert.Contains("closure", disasm);
    }

    [Fact]
    public void Lambda_BlockBody_EmitsClosure()
    {
        string disasm = Disassemble("let f = (x) => { return x + 1; };");
        Assert.Contains("closure", disasm);
    }

    [Fact]
    public void Lambda_ExpressionBody_ContainsImplicitReturn()
    {
        Chunk chunk = CompileSource("let f = (x) => x + 1;");
        Chunk? lambdaChunk = null;
        foreach (StashValue constant in chunk.Constants)
        {
            if (constant.AsObj is Chunk c) { lambdaChunk = c; break; }
        }
        Assert.NotNull(lambdaChunk);
        string lambdaDisasm = Disassembler.Disassemble(lambdaChunk);
        Assert.Contains("ret", lambdaDisasm);
    }

    // =========================================================================
    // 21. Array Expressions
    // =========================================================================

    [Fact]
    public void Array_EmitsArrayOpcode()
    {
        string disasm = Disassemble("[1, 2, 3];");
        Assert.Contains("array", disasm);
    }

    // =========================================================================
    // 22. Index Access
    // =========================================================================

    [Fact]
    public void Index_Get_EmitsGetIndex()
    {
        string disasm = Disassemble("let arr = [1, 2, 3]; arr[0];");
        Assert.Contains("get.index", disasm);
    }

    [Fact]
    public void Index_Set_EmitsSetIndex()
    {
        string disasm = Disassemble("let arr = [1, 2, 3]; arr[0] = 42;");
        Assert.Contains("set.index", disasm);
    }

    // =========================================================================
    // 23. Dot Access
    // =========================================================================

    [Fact]
    public void Dot_Get_EmitsGetField()
    {
        string disasm = Disassemble("math.sqrt(4);");
        Assert.Contains("get.field", disasm);
        Assert.Contains("sqrt", disasm);
    }

    // =========================================================================
    // 24. Interpolated Strings
    // =========================================================================

    [Fact]
    public void InterpolatedString_EmitsInterpolate()
    {
        string disasm = Disassemble("let x = 42; $\"value is {x}\";");
        Assert.Contains("interpolate", disasm);
    }

    // =========================================================================
    // 25. Update Expressions
    // =========================================================================

    [Fact]
    public void Update_PrefixIncrement_EmitsAddAndStore()
    {
        // Top-level variables are global; use a function body to get StoreLocal
        Chunk chunk = CompileSource("fn foo() { let x = 0; ++x; }");
        Chunk? fnChunk = null;
        foreach (StashValue c in chunk.Constants)
            if (c.AsObj is Chunk fc) { fnChunk = fc; break; }
        Assert.NotNull(fnChunk);
        string fnDisasm = Disassembler.Disassemble(fnChunk);
        Assert.Contains("add", fnDisasm);
        Assert.True(fnDisasm.Contains("store.local") || fnDisasm.Contains("dup.store.pop"), "Expected store.local or dup.store.pop");
    }

    [Fact]
    public void Update_PostfixIncrement_EmitsDupAndAdd()
    {
        string disasm = Disassemble("let x = 0; x++;");
        Assert.Contains("dup", disasm);
        Assert.Contains("add", disasm);
    }

    // =========================================================================
    // 26. Switch Expressions
    // =========================================================================

    [Fact]
    public void Switch_EmitsDupEqualJump()
    {
        string disasm = Disassemble("let x = 1; x switch { 1 => \"one\", _ => \"other\" };");
        Assert.Contains("dup", disasm);
        Assert.Contains("eq", disasm);
        Assert.True(disasm.Contains("jmp.false") || disasm.Contains("jmp.eq.false"), "Expected jmp.false or jmp.eq.false");
    }

    // =========================================================================
    // 27. Grouping
    // =========================================================================

    [Fact]
    public void Grouping_TransparentToCompiler()
    {
        string disasm = Disassemble("(1 + 2);");
        Assert.Contains("add", disasm);
    }

    // =========================================================================
    // 28. Throw Statement
    // =========================================================================

    [Fact]
    public void Throw_EmitsValueAndThrow()
    {
        Chunk chunk = CompileSource("fn foo() { throw \"error\"; } foo();");
        Chunk? fnChunk = null;
        foreach (StashValue c in chunk.Constants)
            if (c.AsObj is Chunk fn) { fnChunk = fn; break; }
        Assert.NotNull(fnChunk);
        string fnDisasm = Disassembler.Disassemble(fnChunk);
        Assert.Contains("throw", fnDisasm);
        Assert.Contains("\"error\"", fnDisasm);
    }

    // =========================================================================
    // 29. Try Expression
    // =========================================================================

    [Fact]
    public void TryExpr_EmitsTryBeginAndTryEnd()
    {
        string disasm = Disassemble("let x = try 42;");
        Assert.Contains("try.begin", disasm);
        Assert.Contains("try.end", disasm);
    }

    // =========================================================================
    // 30. Deferred Features Throw
    // =========================================================================

    [Fact]
    public void StructDecl_CompilesSuccessfully()
    {
        // Should compile without throwing — Phase 5 implements struct declarations
        string disasm = Disassemble("struct Point { x, y }");
        Assert.Contains("struct.decl", disasm);
    }

    [Fact]
    public void EnumDecl_CompilesSuccessfully()
    {
        // Should compile without throwing — Phase 5 implements enum declarations
        string disasm = Disassemble("enum Color { Red, Green, Blue }");
        Assert.Contains("enum.decl", disasm);
    }

    [Fact]
    public void Import_EmitsImportOpcode()
    {
        string disasm = Disassemble("import { foo } from \"bar\";");
        Assert.Contains("import", disasm);
    }

    // =========================================================================
    // 31. Script Chunk Properties
    // =========================================================================

    [Fact]
    public void Script_EndsWithNullReturn()
    {
        string disasm = Disassemble("42;");
        string[] lines = disasm.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("ret", lines[^1]);
    }

    [Fact]
    public void Script_EmptyProgram_EmitsNullReturn()
    {
        string disasm = Disassemble("");
        Assert.Contains("null", disasm);
        Assert.Contains("ret", disasm);
    }

    // =========================================================================
    // 32. Complex Expressions
    // =========================================================================

    [Fact]
    public void Complex_NestedArithmetic_ProducesCorrectOrder()
    {
        // 1 + 2 * 3 parses as 1 + (2 * 3) → Multiply emitted before Add
        string disasm = Disassemble("1 + 2 * 3;");
        int mulIdx = disasm.IndexOf("mul", StringComparison.Ordinal);
        int addIdx = disasm.IndexOf("add", StringComparison.Ordinal);
        Assert.True(mulIdx < addIdx, "mul should be emitted before add for correct precedence");
    }

    // =========================================================================
    // 33. Await
    // =========================================================================

    [Fact]
    public void Await_EmitsAwait()
    {
        string disasm = Disassemble("fn foo() { return 42; } let x = await foo();");
        Assert.Contains("await", disasm);
    }

    // =========================================================================
    // 34. Range
    // =========================================================================

    [Fact]
    public void Range_TwoOperands_EmitsRange()
    {
        string disasm = Disassemble("0..10;");
        Assert.Contains("range", disasm);
    }

    // =========================================================================
    // 35. Spread
    // =========================================================================

    [Fact]
    public void Spread_EmitsSpread()
    {
        string disasm = Disassemble("let arr = [1, 2]; [...arr];");
        Assert.Contains("spread", disasm);
    }

    // =========================================================================
    // 36. For-In Loop
    // =========================================================================

    [Fact]
    public void ForIn_EmitsIteratorAndIterate()
    {
        string disasm = Disassemble("for (let x in [1, 2, 3]) { x; }");
        Assert.Contains("iterator", disasm);
        Assert.Contains("iterate", disasm);
        Assert.Contains("loop", disasm);
    }

    // =========================================================================
    // 37. Nested Scoping
    // =========================================================================

    [Fact]
    public void NestedBlocks_CleanupLocalsCorrectly()
    {
        Chunk chunk = CompileSource("""
            let a = 1;
            {
                let b = 2;
                {
                    let c = 3;
                }
            }
            """);
        string disasm = Disassembler.Disassemble(chunk);
        Assert.Contains("pop", disasm);
    }

    // =========================================================================
    // 38. Continue in For Loop (forward patching)
    // =========================================================================

    [Fact]
    public void Continue_InForLoop_JumpsToIncrement()
    {
        string disasm = Disassemble("for (let i = 0; i < 10; i = i + 1) { continue; }");
        Assert.Contains("loop", disasm);
    }

    // =========================================================================
    // 39. Multiple Functions
    // =========================================================================

    [Fact]
    public void MultipleFunctions_EachHasOwnChunk()
    {
        Chunk chunk = CompileSource("""
            fn add(a, b) { return a + b; }
            fn sub(a, b) { return a - b; }
            """);
        int fnCount = 0;
        foreach (StashValue c in chunk.Constants)
            if (c.AsObj is Chunk) fnCount++;
        Assert.Equal(2, fnCount);
    }

    // =========================================================================
    // 40. ForIn — Iterator Slot Alignment
    // =========================================================================

    [Fact]
    public void ForIn_LoopVarSlot_IsNotIteratorSlot()
    {
        // The iterator must occupy its own local slot so that the loop variable
        // gets the next slot. Without this, LoadLocal for the loop variable
        // would load the iterator instead.
        Chunk chunk = CompileSource("""
            fn foo() {
                for (let x in [1, 2, 3]) { x; }
            }
            """, optimize: false);
        Chunk? fnChunk = null;
        foreach (StashValue c in chunk.Constants)
            if (c.AsObj is Chunk fc) { fnChunk = fc; break; }
        Assert.NotNull(fnChunk);
        string fnDisasm = Disassembler.Disassemble(fnChunk);
        // The StoreLocal for the loop variable and the LoadLocal for reading it
        // must reference the SAME slot (not the iterator's slot).
        int storeIdx = fnDisasm.IndexOf("store.local", StringComparison.Ordinal);
        int loadIdx = fnDisasm.IndexOf("load.local", storeIdx + 1, StringComparison.Ordinal);
        Assert.True(storeIdx >= 0 && loadIdx >= 0,
            "Expected store.local and load.local for loop variable");
        // Extract the slot numbers from both instructions — they must match
        string storeLine = fnDisasm[storeIdx..fnDisasm.IndexOf('\n', storeIdx)];
        string loadLine = fnDisasm[loadIdx..fnDisasm.IndexOf('\n', loadIdx)];
        // Both should reference the same slot index
        string storeSlot = storeLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Last();
        string loadSlot = loadLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Last();
        Assert.Equal(storeSlot, loadSlot);
    }

    [Fact]
    public void ForIn_BreakCleansUpIteratorSlot()
    {
        // break inside for-in must clean up the iterator, index, and loop locals
        Chunk chunk = CompileSource("fn foo() { for (let x in [1, 2, 3]) { break; } }");
        Chunk? fnChunk = null;
        foreach (StashValue c in chunk.Constants)
            if (c.AsObj is Chunk fc) { fnChunk = fc; break; }
        Assert.NotNull(fnChunk);
        string fnDisasm = Disassembler.Disassemble(fnChunk);
        Assert.Contains("iterator", fnDisasm);
        Assert.Contains("jmp", fnDisasm);
    }

    // =========================================================================
    // 41. Update Expressions — Deferred for Non-Identifier Operands
    // =========================================================================

    [Fact]
    public void Update_DotExpr_CompilesSuccessfully()
    {
        // Should compile without throwing now that Phase 6 is implemented
        var ex = Record.Exception(() => CompileSource("fn foo() { let o = null; o.field++; }"));
        Assert.Null(ex);
    }

    [Fact]
    public void Update_IndexExpr_CompilesSuccessfully()
    {
        // Should compile without throwing now that Phase 6 is implemented
        var ex = Record.Exception(() => CompileSource("fn foo() { let a = [1]; a[0]++; }"));
        Assert.Null(ex);
    }
}
