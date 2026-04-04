using Stash.Bytecode;
using Stash.Interpreting;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

namespace Stash.Tests.Bytecode;

public class CompilerTests
{
    /// <summary>
    /// Lex → Parse → Resolve → Compile a source string into a Chunk.
    /// </summary>
    private static Chunk CompileSource(string source)
    {
        var lexer = new Lexer(source, "<test>");
        List<Token> tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        List<Stmt> stmts = parser.ParseProgram();

        var interpreter = new Interpreter();
        var resolver = new Resolver(interpreter);
        resolver.Resolve(stmts);

        return Compiler.Compile(stmts);
    }

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
        Assert.Contains("Const", disasm);
        Assert.Contains("42", disasm);
        Assert.Contains("Pop", disasm);
    }

    [Fact]
    public void Literal_Null_EmitsNull()
    {
        string disasm = Disassemble("null;");
        Assert.Contains("Null", disasm);
    }

    [Fact]
    public void Literal_True_EmitsTrue()
    {
        string disasm = Disassemble("true;");
        Assert.Contains("True", disasm);
    }

    [Fact]
    public void Literal_False_EmitsFalse()
    {
        string disasm = Disassemble("false;");
        Assert.Contains("False", disasm);
    }

    [Fact]
    public void Literal_String_EmitsConst()
    {
        string disasm = Disassemble("\"hello\";");
        Assert.Contains("Const", disasm);
        Assert.Contains("\"hello\"", disasm);
    }

    [Fact]
    public void Literal_Float_EmitsConst()
    {
        string disasm = Disassemble("3.14;");
        Assert.Contains("Const", disasm);
        Assert.Contains("3.14", disasm);
    }

    // =========================================================================
    // 2. Unary Expressions
    // =========================================================================

    [Fact]
    public void Unary_Negate_EmitsNegate()
    {
        string disasm = Disassemble("-42;");
        Assert.Contains("Const", disasm);
        Assert.Contains("Negate", disasm);
    }

    [Fact]
    public void Unary_LogicalNot_EmitsNot()
    {
        string disasm = Disassemble("!true;");
        Assert.Contains("True", disasm);
        Assert.Contains("Not", disasm);
    }

    [Fact]
    public void Unary_BitwiseNot_EmitsBitNot()
    {
        string disasm = Disassemble("~42;");
        Assert.Contains("Const", disasm);
        Assert.Contains("BitNot", disasm);
    }

    // =========================================================================
    // 3. Binary Expressions (Arithmetic)
    // =========================================================================

    [Fact]
    public void Binary_Addition_EmitsAdd()
    {
        string disasm = Disassemble("1 + 2;");
        AssertContainsOpcodes(disasm, "Const", "Add", "Pop");
    }

    [Fact]
    public void Binary_Subtraction_EmitsSubtract()
    {
        string disasm = Disassemble("5 - 3;");
        Assert.Contains("Subtract", disasm);
    }

    [Fact]
    public void Binary_Multiplication_EmitsMultiply()
    {
        string disasm = Disassemble("2 * 3;");
        Assert.Contains("Multiply", disasm);
    }

    [Fact]
    public void Binary_Division_EmitsDivide()
    {
        string disasm = Disassemble("10 / 2;");
        Assert.Contains("Divide", disasm);
    }

    [Fact]
    public void Binary_Modulo_EmitsModulo()
    {
        string disasm = Disassemble("10 % 3;");
        Assert.Contains("Modulo", disasm);
    }

    // =========================================================================
    // 4. Comparison and Equality
    // =========================================================================

    [Fact]
    public void Binary_Equal_EmitsEqual()
    {
        string disasm = Disassemble("1 == 2;");
        Assert.Contains("Equal", disasm);
    }

    [Fact]
    public void Binary_NotEqual_EmitsNotEqual()
    {
        string disasm = Disassemble("1 != 2;");
        Assert.Contains("NotEqual", disasm);
    }

    [Fact]
    public void Binary_LessThan_EmitsLessThan()
    {
        string disasm = Disassemble("1 < 2;");
        Assert.Contains("LessThan", disasm);
    }

    [Fact]
    public void Binary_GreaterThan_EmitsGreaterThan()
    {
        string disasm = Disassemble("1 > 2;");
        Assert.Contains("GreaterThan", disasm);
    }

    [Fact]
    public void Binary_LessEqual_EmitsLessEqual()
    {
        string disasm = Disassemble("1 <= 2;");
        Assert.Contains("LessEqual", disasm);
    }

    [Fact]
    public void Binary_GreaterEqual_EmitsGreaterEqual()
    {
        string disasm = Disassemble("1 >= 2;");
        Assert.Contains("GreaterEqual", disasm);
    }

    // =========================================================================
    // 5. Bitwise Operators
    // =========================================================================

    [Fact]
    public void Binary_BitwiseAnd_EmitsBitAnd()
    {
        string disasm = Disassemble("5 & 3;");
        Assert.Contains("BitAnd", disasm);
    }

    [Fact]
    public void Binary_BitwiseOr_EmitsBitOr()
    {
        string disasm = Disassemble("5 | 3;");
        Assert.Contains("BitOr", disasm);
    }

    [Fact]
    public void Binary_BitwiseXor_EmitsBitXor()
    {
        string disasm = Disassemble("5 ^ 3;");
        Assert.Contains("BitXor", disasm);
    }

    [Fact]
    public void Binary_ShiftLeft_EmitsShiftLeft()
    {
        string disasm = Disassemble("1 << 3;");
        Assert.Contains("ShiftLeft", disasm);
    }

    [Fact]
    public void Binary_ShiftRight_EmitsShiftRight()
    {
        string disasm = Disassemble("8 >> 2;");
        Assert.Contains("ShiftRight", disasm);
    }

    // =========================================================================
    // 6. Short-Circuit Logic
    // =========================================================================

    [Fact]
    public void Logic_And_EmitsAndWithJump()
    {
        string disasm = Disassemble("true && false;");
        Assert.Contains("True", disasm);
        Assert.Contains("And", disasm);
        Assert.Contains("False", disasm);
        Assert.Contains("->", disasm);
    }

    [Fact]
    public void Logic_Or_EmitsOrWithJump()
    {
        string disasm = Disassemble("false || true;");
        Assert.Contains("False", disasm);
        Assert.Contains("Or", disasm);
        Assert.Contains("True", disasm);
    }

    // =========================================================================
    // 7. Null Coalescing
    // =========================================================================

    [Fact]
    public void NullCoalesce_EmitsNullCoalesceWithJump()
    {
        string disasm = Disassemble("null ?? 42;");
        Assert.Contains("Null", disasm);
        Assert.Contains("NullCoalesce", disasm);
        Assert.Contains("42", disasm);
    }

    // =========================================================================
    // 8. Ternary
    // =========================================================================

    [Fact]
    public void Ternary_EmitsConditionAndJumps()
    {
        string disasm = Disassemble("true ? 1 : 2;");
        Assert.Contains("True", disasm);
        Assert.Contains("JumpFalse", disasm);
        Assert.Contains("Jump", disasm);
    }

    // =========================================================================
    // 9. Variable Declarations
    // =========================================================================

    [Fact]
    public void VarDecl_WithInitializer_EmitsValue()
    {
        string disasm = Disassemble("let x = 42;");
        Assert.Contains("Const", disasm);
        Assert.Contains("42", disasm);
    }

    [Fact]
    public void VarDecl_WithoutInitializer_EmitsNull()
    {
        string disasm = Disassemble("let x;");
        Assert.Contains("Null", disasm);
    }

    [Fact]
    public void ConstDecl_EmitsValue()
    {
        string disasm = Disassemble("const x = 42;");
        Assert.Contains("Const", disasm);
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
        foreach (object? c in chunk.Constants)
            if (c is Chunk fc) { fnChunk = fc; break; }
        Assert.NotNull(fnChunk);
        Assert.Contains("LoadLocal", Disassembler.Disassemble(fnChunk));
    }

    [Fact]
    public void Assign_Local_EmitsStoreLocal()
    {
        // Top-level variables are global; use a function body to get StoreLocal
        Chunk chunk = CompileSource("fn foo() { let x = 1; x = 2; }");
        Chunk? fnChunk = null;
        foreach (object? c in chunk.Constants)
            if (c is Chunk fc) { fnChunk = fc; break; }
        Assert.NotNull(fnChunk);
        string fnDisasm = Disassembler.Disassemble(fnChunk);
        Assert.Contains("StoreLocal", fnDisasm);
        Assert.Contains("Dup", fnDisasm);
    }

    // =========================================================================
    // 11. Block Scoping
    // =========================================================================

    [Fact]
    public void Block_PopsLocalsOnExit()
    {
        string disasm = Disassemble("{ let x = 1; let y = 2; }");
        int popCount = CountOccurrences(disasm, "Pop");
        Assert.True(popCount >= 2, $"Expected at least 2 Pop instructions, found {popCount}");
    }

    // =========================================================================
    // 12. If/Else
    // =========================================================================

    [Fact]
    public void If_WithoutElse_EmitsJumpFalse()
    {
        string disasm = Disassemble("if (true) { 42; }");
        Assert.Contains("True", disasm);
        Assert.Contains("JumpFalse", disasm);
    }

    [Fact]
    public void If_WithElse_EmitsJumpFalseAndJump()
    {
        string disasm = Disassemble("if (true) { 1; } else { 2; }");
        Assert.Contains("JumpFalse", disasm);
        Assert.Contains("Jump", disasm);
    }

    // =========================================================================
    // 13. While Loop
    // =========================================================================

    [Fact]
    public void While_EmitsLoopAndJumpFalse()
    {
        string disasm = Disassemble("let x = 0; while (x < 10) { x = x + 1; }");
        Assert.Contains("Loop", disasm);
        Assert.Contains("JumpFalse", disasm);
        Assert.Contains("LessThan", disasm);
    }

    // =========================================================================
    // 14. Do-While Loop
    // =========================================================================

    [Fact]
    public void DoWhile_EmitsLoopAfterCondition()
    {
        string disasm = Disassemble("let x = 0; do { x = x + 1; } while (x < 10);");
        Assert.Contains("Loop", disasm);
        Assert.Contains("JumpFalse", disasm);
    }

    // =========================================================================
    // 15. For Loop
    // =========================================================================

    [Fact]
    public void For_EmitsInitConditionBodyIncrementLoop()
    {
        string disasm = Disassemble("for (let i = 0; i < 10; i = i + 1) { 42; }");
        Assert.Contains("Loop", disasm);
        Assert.Contains("JumpFalse", disasm);
        Assert.Contains("LessThan", disasm);
    }

    // =========================================================================
    // 16. Break and Continue
    // =========================================================================

    [Fact]
    public void Break_EmitsJump()
    {
        string disasm = Disassemble("while (true) { break; }");
        Assert.Contains("Jump", disasm);
        Assert.Contains("Loop", disasm);
    }

    [Fact]
    public void Continue_InWhile_EmitsLoop()
    {
        string disasm = Disassemble("let x = 0; while (x < 10) { x = x + 1; continue; }");
        int loopCount = CountOccurrences(disasm, "Loop");
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
        Assert.Contains("Return", disasm);
    }

    [Fact]
    public void Return_WithoutValue_EmitsNullAndReturn()
    {
        string disasm = Disassemble("fn foo() { return; } foo();");
        Assert.Contains("Null", disasm);
        Assert.Contains("Return", disasm);
    }

    // =========================================================================
    // 18. Function Declarations
    // =========================================================================

    [Fact]
    public void FnDecl_EmitsClosure()
    {
        string disasm = Disassemble("fn add(a, b) { return a + b; }");
        Assert.Contains("Closure", disasm);
    }

    [Fact]
    public void FnDecl_NestedChunkHasCorrectArity()
    {
        Chunk chunk = CompileSource("fn add(a, b) { return a + b; }");
        Chunk? fnChunk = null;
        foreach (object? constant in chunk.Constants)
        {
            if (constant is Chunk c)
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
        foreach (object? constant in chunk.Constants)
        {
            if (constant is Chunk c) { fnChunk = c; break; }
        }
        Assert.NotNull(fnChunk);
        string fnDisasm = Disassembler.Disassemble(fnChunk);
        Assert.Contains("LoadLocal", fnDisasm);
        Assert.Contains("Add", fnDisasm);
        Assert.Contains("Return", fnDisasm);
    }

    // =========================================================================
    // 19. Function Calls
    // =========================================================================

    [Fact]
    public void Call_EmitsCallWithArgCount()
    {
        string disasm = Disassemble("fn foo(x) { return x; } foo(42);");
        Assert.Contains("Call", disasm);
    }

    // =========================================================================
    // 20. Lambda Expressions
    // =========================================================================

    [Fact]
    public void Lambda_ExpressionBody_EmitsClosure()
    {
        string disasm = Disassemble("let f = (x) => x + 1;");
        Assert.Contains("Closure", disasm);
    }

    [Fact]
    public void Lambda_BlockBody_EmitsClosure()
    {
        string disasm = Disassemble("let f = (x) => { return x + 1; };");
        Assert.Contains("Closure", disasm);
    }

    [Fact]
    public void Lambda_ExpressionBody_ContainsImplicitReturn()
    {
        Chunk chunk = CompileSource("let f = (x) => x + 1;");
        Chunk? lambdaChunk = null;
        foreach (object? constant in chunk.Constants)
        {
            if (constant is Chunk c) { lambdaChunk = c; break; }
        }
        Assert.NotNull(lambdaChunk);
        string lambdaDisasm = Disassembler.Disassemble(lambdaChunk);
        Assert.Contains("Return", lambdaDisasm);
    }

    // =========================================================================
    // 21. Array Expressions
    // =========================================================================

    [Fact]
    public void Array_EmitsArrayOpcode()
    {
        string disasm = Disassemble("[1, 2, 3];");
        Assert.Contains("Array", disasm);
    }

    // =========================================================================
    // 22. Index Access
    // =========================================================================

    [Fact]
    public void Index_Get_EmitsGetIndex()
    {
        string disasm = Disassemble("let arr = [1, 2, 3]; arr[0];");
        Assert.Contains("GetIndex", disasm);
    }

    [Fact]
    public void Index_Set_EmitsSetIndex()
    {
        string disasm = Disassemble("let arr = [1, 2, 3]; arr[0] = 42;");
        Assert.Contains("SetIndex", disasm);
    }

    // =========================================================================
    // 23. Dot Access
    // =========================================================================

    [Fact]
    public void Dot_Get_EmitsGetField()
    {
        string disasm = Disassemble("math.sqrt(4);");
        Assert.Contains("GetField", disasm);
        Assert.Contains("sqrt", disasm);
    }

    // =========================================================================
    // 24. Interpolated Strings
    // =========================================================================

    [Fact]
    public void InterpolatedString_EmitsInterpolate()
    {
        string disasm = Disassemble("let x = 42; $\"value is {x}\";");
        Assert.Contains("Interpolate", disasm);
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
        foreach (object? c in chunk.Constants)
            if (c is Chunk fc) { fnChunk = fc; break; }
        Assert.NotNull(fnChunk);
        string fnDisasm = Disassembler.Disassemble(fnChunk);
        Assert.Contains("Add", fnDisasm);
        Assert.Contains("StoreLocal", fnDisasm);
    }

    [Fact]
    public void Update_PostfixIncrement_EmitsDupAndAdd()
    {
        string disasm = Disassemble("let x = 0; x++;");
        Assert.Contains("Dup", disasm);
        Assert.Contains("Add", disasm);
    }

    // =========================================================================
    // 26. Switch Expressions
    // =========================================================================

    [Fact]
    public void Switch_EmitsDupEqualJump()
    {
        string disasm = Disassemble("let x = 1; x switch { 1 => \"one\", _ => \"other\" };");
        Assert.Contains("Dup", disasm);
        Assert.Contains("Equal", disasm);
        Assert.Contains("JumpFalse", disasm);
    }

    // =========================================================================
    // 27. Grouping
    // =========================================================================

    [Fact]
    public void Grouping_TransparentToCompiler()
    {
        string disasm = Disassemble("(1 + 2);");
        Assert.Contains("Add", disasm);
    }

    // =========================================================================
    // 28. Throw Statement
    // =========================================================================

    [Fact]
    public void Throw_EmitsValueAndThrow()
    {
        Chunk chunk = CompileSource("fn foo() { throw \"error\"; } foo();");
        Chunk? fnChunk = null;
        foreach (object? c in chunk.Constants)
            if (c is Chunk fn) { fnChunk = fn; break; }
        Assert.NotNull(fnChunk);
        string fnDisasm = Disassembler.Disassemble(fnChunk);
        Assert.Contains("Throw", fnDisasm);
        Assert.Contains("\"error\"", fnDisasm);
    }

    // =========================================================================
    // 29. Try Expression
    // =========================================================================

    [Fact]
    public void TryExpr_EmitsTryBeginAndTryEnd()
    {
        string disasm = Disassemble("let x = try 42;");
        Assert.Contains("TryBegin", disasm);
        Assert.Contains("TryEnd", disasm);
    }

    // =========================================================================
    // 30. Deferred Features Throw
    // =========================================================================

    [Fact]
    public void StructDecl_CompilesSuccessfully()
    {
        // Should compile without throwing — Phase 5 implements struct declarations
        string disasm = Disassemble("struct Point { x, y }");
        Assert.Contains("StructDecl", disasm);
    }

    [Fact]
    public void EnumDecl_CompilesSuccessfully()
    {
        // Should compile without throwing — Phase 5 implements enum declarations
        string disasm = Disassemble("enum Color { Red, Green, Blue }");
        Assert.Contains("EnumDecl", disasm);
    }

    [Fact]
    public void Import_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => CompileSource("import { foo } from \"bar\";"));
    }

    // =========================================================================
    // 31. Script Chunk Properties
    // =========================================================================

    [Fact]
    public void Script_EndsWithNullReturn()
    {
        string disasm = Disassemble("42;");
        string[] lines = disasm.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("Return", lines[^1]);
    }

    [Fact]
    public void Script_EmptyProgram_EmitsNullReturn()
    {
        string disasm = Disassemble("");
        Assert.Contains("Null", disasm);
        Assert.Contains("Return", disasm);
    }

    // =========================================================================
    // 32. Complex Expressions
    // =========================================================================

    [Fact]
    public void Complex_NestedArithmetic_ProducesCorrectOrder()
    {
        // 1 + 2 * 3 parses as 1 + (2 * 3) → Multiply emitted before Add
        string disasm = Disassemble("1 + 2 * 3;");
        int mulIdx = disasm.IndexOf("Multiply", StringComparison.Ordinal);
        int addIdx = disasm.IndexOf("Add", StringComparison.Ordinal);
        Assert.True(mulIdx < addIdx, "Multiply should be emitted before Add for correct precedence");
    }

    // =========================================================================
    // 33. Await
    // =========================================================================

    [Fact]
    public void Await_EmitsAwait()
    {
        string disasm = Disassemble("fn foo() { return 42; } let x = await foo();");
        Assert.Contains("Await", disasm);
    }

    // =========================================================================
    // 34. Range
    // =========================================================================

    [Fact]
    public void Range_TwoOperands_EmitsRange()
    {
        string disasm = Disassemble("0..10;");
        Assert.Contains("Range", disasm);
    }

    // =========================================================================
    // 35. Spread
    // =========================================================================

    [Fact]
    public void Spread_EmitsSpread()
    {
        string disasm = Disassemble("let arr = [1, 2]; [...arr];");
        Assert.Contains("Spread", disasm);
    }

    // =========================================================================
    // 36. For-In Loop
    // =========================================================================

    [Fact]
    public void ForIn_EmitsIteratorAndIterate()
    {
        string disasm = Disassemble("for (let x in [1, 2, 3]) { x; }");
        Assert.Contains("Iterator", disasm);
        Assert.Contains("Iterate", disasm);
        Assert.Contains("Loop", disasm);
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
        Assert.Contains("Pop", disasm);
    }

    // =========================================================================
    // 38. Continue in For Loop (forward patching)
    // =========================================================================

    [Fact]
    public void Continue_InForLoop_JumpsToIncrement()
    {
        string disasm = Disassemble("for (let i = 0; i < 10; i = i + 1) { continue; }");
        Assert.Contains("Loop", disasm);
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
        foreach (object? c in chunk.Constants)
            if (c is Chunk) fnCount++;
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
            """);
        Chunk? fnChunk = null;
        foreach (object? c in chunk.Constants)
            if (c is Chunk fc) { fnChunk = fc; break; }
        Assert.NotNull(fnChunk);
        string fnDisasm = Disassembler.Disassemble(fnChunk);
        // The StoreLocal for the loop variable and the LoadLocal for reading it
        // must reference the SAME slot (not the iterator's slot).
        int storeIdx = fnDisasm.IndexOf("StoreLocal", StringComparison.Ordinal);
        int loadIdx = fnDisasm.IndexOf("LoadLocal", storeIdx + 1, StringComparison.Ordinal);
        Assert.True(storeIdx >= 0 && loadIdx >= 0,
            "Expected StoreLocal and LoadLocal for loop variable");
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
        foreach (object? c in chunk.Constants)
            if (c is Chunk fc) { fnChunk = fc; break; }
        Assert.NotNull(fnChunk);
        string fnDisasm = Disassembler.Disassemble(fnChunk);
        Assert.Contains("Iterator", fnDisasm);
        Assert.Contains("Jump", fnDisasm);
    }

    // =========================================================================
    // 41. Update Expressions — Deferred for Non-Identifier Operands
    // =========================================================================

    [Fact]
    public void Update_DotExpr_ThrowsCompileError()
    {
        var ex = Assert.Throws<CompileError>(() =>
            CompileSource("fn foo() { let o = null; o.field++; }"));
        Assert.Contains("deferred", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Update_IndexExpr_ThrowsCompileError()
    {
        var ex = Assert.Throws<CompileError>(() =>
            CompileSource("fn foo() { let a = [1]; a[0]++; }"));
        Assert.Contains("deferred", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
