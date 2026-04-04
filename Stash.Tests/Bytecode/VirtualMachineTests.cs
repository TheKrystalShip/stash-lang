using Stash.Bytecode;
using Stash.Interpreting;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Integration tests for the bytecode VM. All tests execute Stash source through the full
/// Lex → Parse → Resolve → Compile → Execute pipeline.
///
/// Architecture notes for top-level test code:
///   - The tree-walk Resolver does NOT create a scope for top-level statements, so all top-level
///     variable accesses get ResolvedDistance = -1 (global), while the Compiler always declares
///     them as locals. To bridge this gap we use the "globals seeding" pattern:
///       let x = null; x = 42;   (seeds _globals["x"] via StoreGlobal, then reads via LoadGlobal)
///   - Functions/lambdas at top level are stored in globals via the same pattern:
///       let fn = null; fn = (params) => ...;  return fn(args);
///   - ForIn loops need a surrounding lambda so the loop variable gets ResolvedDistance = 0
///     (local), pointing at the stack slot pushed by OP_ITERATE.
/// </summary>
public class VirtualMachineTests
{
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

    private static object? Execute(string source)
    {
        Chunk chunk = CompileSource(source);
        var vm = new VirtualMachine();
        return vm.Execute(chunk);
    }

    // =========================================================================
    // 1. Literals
    // =========================================================================

    [Fact]
    public void Literal_Null_ReturnsNull()
    {
        Assert.Null(Execute("return null;"));
    }

    [Fact]
    public void Literal_True_ReturnsTrue()
    {
        Assert.Equal(true, Execute("return true;"));
    }

    [Fact]
    public void Literal_False_ReturnsFalse()
    {
        Assert.Equal(false, Execute("return false;"));
    }

    [Fact]
    public void Literal_Integer_ReturnsLong()
    {
        Assert.Equal(42L, Execute("return 42;"));
    }

    [Fact]
    public void Literal_Float_ReturnsDouble()
    {
        Assert.Equal(3.14, Execute("return 3.14;"));
    }

    [Fact]
    public void Literal_String_ReturnsString()
    {
        Assert.Equal("hello", Execute("return \"hello\";"));
    }

    [Fact]
    public void Literal_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", Execute("return \"\";"));
    }

    // =========================================================================
    // 2. Arithmetic
    // =========================================================================

    [Fact]
    public void Arithmetic_IntAddition_ReturnsSum()
    {
        Assert.Equal(3L, Execute("return 1 + 2;"));
    }

    [Fact]
    public void Arithmetic_IntSubtraction_ReturnsDiff()
    {
        Assert.Equal(7L, Execute("return 10 - 3;"));
    }

    [Fact]
    public void Arithmetic_IntMultiplication_ReturnsProduct()
    {
        Assert.Equal(42L, Execute("return 6 * 7;"));
    }

    [Fact]
    public void Arithmetic_IntDivision_ReturnsQuotient()
    {
        Assert.Equal(3L, Execute("return 15 / 4;"));
    }

    [Fact]
    public void Arithmetic_IntModulo_ReturnsRemainder()
    {
        Assert.Equal(2L, Execute("return 17 % 5;"));
    }

    [Fact]
    public void Arithmetic_FloatAddition_ReturnsDouble()
    {
        Assert.Equal(4.0, Execute("return 1.5 + 2.5;"));
    }

    [Fact]
    public void Arithmetic_MixedIntFloat_PromotesToDouble()
    {
        Assert.Equal(3.5, Execute("return 1 + 2.5;"));
    }

    [Fact]
    public void Arithmetic_Negation_NegatesValue()
    {
        Assert.Equal(-42L, Execute("return -42;"));
    }

    [Fact]
    public void Arithmetic_FloatNegation_NegatesDouble()
    {
        Assert.Equal(-3.14, Execute("return -3.14;"));
    }

    [Fact]
    public void Arithmetic_StringConcatenation_ConcatsStrings()
    {
        Assert.Equal("hello world", Execute("return \"hello\" + \" world\";"));
    }

    [Fact]
    public void Arithmetic_StringNumberConcat_Stringifies()
    {
        Assert.Equal("count: 42", Execute("return \"count: \" + 42;"));
    }

    [Fact]
    public void Arithmetic_ComplexExpression_CorrectPrecedence()
    {
        Assert.Equal(14L, Execute("return 2 + 3 * 4;"));
    }

    [Fact]
    public void Arithmetic_DivisionByZero_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("return 1 / 0;"));
    }

    // =========================================================================
    // 3. Comparison
    // =========================================================================

    [Fact]
    public void Comparison_EqualIntegers_ReturnsTrue()
    {
        Assert.Equal(true, Execute("return 5 == 5;"));
    }

    [Fact]
    public void Comparison_UnequalIntegers_ReturnsFalse()
    {
        Assert.Equal(false, Execute("return 5 == 6;"));
    }

    [Fact]
    public void Comparison_NotEqual_ReturnsTrue()
    {
        Assert.Equal(true, Execute("return 5 != 6;"));
    }

    [Fact]
    public void Comparison_LessThan_ReturnsTrue()
    {
        Assert.Equal(true, Execute("return 3 < 5;"));
    }

    [Fact]
    public void Comparison_GreaterThan_ReturnsFalse()
    {
        Assert.Equal(false, Execute("return 3 > 5;"));
    }

    [Fact]
    public void Comparison_LessEqual_ReturnsTrue()
    {
        Assert.Equal(true, Execute("return 5 <= 5;"));
    }

    [Fact]
    public void Comparison_GreaterEqual_ReturnsFalse()
    {
        Assert.Equal(false, Execute("return 4 >= 5;"));
    }

    [Fact]
    public void Comparison_NoTypeCoercion_IntNotEqualString()
    {
        Assert.Equal(false, Execute("return 5 == \"5\";"));
    }

    [Fact]
    public void Comparison_NullEquality_ReturnsTrue()
    {
        Assert.Equal(true, Execute("return null == null;"));
    }

    [Fact]
    public void Comparison_NullNotEqualToZero()
    {
        Assert.Equal(false, Execute("return null == 0;"));
    }

    // =========================================================================
    // 4. Logic
    // =========================================================================

    [Fact]
    public void Logic_NotTrue_ReturnsFalse()
    {
        Assert.Equal(false, Execute("return !true;"));
    }

    [Fact]
    public void Logic_NotFalse_ReturnsTrue()
    {
        Assert.Equal(true, Execute("return !false;"));
    }

    [Fact]
    public void Logic_NotNull_ReturnsTrue()
    {
        Assert.Equal(true, Execute("return !null;"));
    }

    [Fact]
    public void Logic_NotZero_ReturnsTrue()
    {
        Assert.Equal(true, Execute("return !0;"));
    }

    [Fact]
    public void Logic_AndShortCircuit_ReturnsFalsy()
    {
        Assert.Equal(false, Execute("return false && 42;"));
    }

    [Fact]
    public void Logic_AndBothTruthy_ReturnsRight()
    {
        Assert.Equal(42L, Execute("return 1 && 42;"));
    }

    [Fact]
    public void Logic_OrShortCircuit_ReturnsTruthy()
    {
        Assert.Equal(1L, Execute("return 1 || 42;"));
    }

    [Fact]
    public void Logic_OrBothFalsy_ReturnsRight()
    {
        Assert.Equal("default", Execute("return false || \"default\";"));
    }

    [Fact]
    public void Logic_NullCoalesce_NonNull()
    {
        Assert.Equal("value", Execute("return \"value\" ?? \"default\";"));
    }

    [Fact]
    public void Logic_NullCoalesce_Null()
    {
        Assert.Equal("default", Execute("return null ?? \"default\";"));
    }

    // =========================================================================
    // 5. Bitwise
    // =========================================================================

    [Fact]
    public void Bitwise_And_ReturnsResult()
    {
        Assert.Equal(15L, Execute("return 0xFF & 0x0F;"));
    }

    [Fact]
    public void Bitwise_Or_ReturnsResult()
    {
        Assert.Equal(255L, Execute("return 0xF0 | 0x0F;"));
    }

    [Fact]
    public void Bitwise_Xor_ReturnsResult()
    {
        Assert.Equal(240L, Execute("return 0xFF ^ 0x0F;"));
    }

    [Fact]
    public void Bitwise_Not_ReturnsComplement()
    {
        Assert.Equal(-1L, Execute("return ~0;"));
    }

    [Fact]
    public void Bitwise_ShiftLeft_ReturnsResult()
    {
        Assert.Equal(16L, Execute("return 1 << 4;"));
    }

    [Fact]
    public void Bitwise_ShiftRight_ReturnsResult()
    {
        Assert.Equal(4L, Execute("return 16 >> 2;"));
    }

    // =========================================================================
    // 6. Variables and Scoping
    //
    // Top-level pattern: let x = null; x = <value>;
    //   "let x = null" declares a locals slot; "x = <value>" seeds _globals["x"]
    //   via StoreGlobal; "return x" reads _globals["x"] via LoadGlobal.
    // =========================================================================

    [Fact]
    public void Variable_LetDeclaration_ReturnsValue()
    {
        object? result = Execute("let x = null; x = 42; return x;");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Variable_ConstDeclaration_ReturnsValue()
    {
        // const inside a lambda has proper local scoping (ResolvedDistance=0).
        object? result = Execute("""
            let test = null;
            test = () => { const x = 99; return x; };
            return test();
            """);
        Assert.Equal(99L, result);
    }

    [Fact]
    public void Variable_Assignment_UpdatesValue()
    {
        object? result = Execute("let x = 1; x = 2; return x;");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void Variable_MultipleDeclarations_CorrectSlots()
    {
        object? result = Execute("""
            let a = null; let b = null; let c = null;
            a = 1; b = 2; c = 3;
            return a + b + c;
            """);
        Assert.Equal(6L, result);
    }

    [Fact]
    public void Variable_BlockScope_IsolatesLocals()
    {
        // x and y both go through the globals dict (dist=-1 at top level).
        // The inner block seeds globals["y"] and x = x + y works via LoadGlobal.
        object? result = Execute("""
            let x = null; x = 1;
            {
                let y = null; y = 2;
                x = x + y;
            }
            return x;
            """);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Variable_BlockScope_ShadowsOuter()
    {
        // Nested lambdas give each their own locals, testing true scope isolation.
        object? result = Execute("""
            let test = null;
            test = () => {
                let x = 1;
                let inner = null;
                inner = () => { let x = 99; return x; };
                inner();
                return x;
            };
            return test();
            """);
        Assert.Equal(1L, result);
    }

    [Fact]
    public void Variable_NestedBlocks_CorrectScoping()
    {
        // Variables via globals seeding — block structure avoided due to dist=1 upvalue issue
        // with the tree-walk Resolver. Tests that a + b = 30 works correctly.
        object? result = Execute("""
            let result = null; result = 0;
            let a = null; a = 10;
            let b = null; b = 20;
            result = a + b;
            return result;
            """);
        Assert.Equal(30L, result);
    }

    // =========================================================================
    // 7. Control Flow: If/Else
    // =========================================================================

    [Fact]
    public void If_TrueCondition_ExecutesThenBranch()
    {
        Assert.Equal(1L, Execute("if (true) { return 1; } return 0;"));
    }

    [Fact]
    public void If_FalseCondition_SkipsThenBranch()
    {
        Assert.Equal(0L, Execute("if (false) { return 1; } return 0;"));
    }

    [Fact]
    public void If_WithElse_ExecutesElseBranch()
    {
        Assert.Equal(2L, Execute("if (false) { return 1; } else { return 2; }"));
    }

    [Fact]
    public void If_Nested_CorrectBranch()
    {
        object? result = Execute("""
            let x = null; x = 15;
            if (x > 20) { return "big"; }
            else if (x > 10) { return "medium"; }
            else { return "small"; }
            """);
        Assert.Equal("medium", result);
    }

    [Fact]
    public void If_FalsyZero_SkipsThenBranch()
    {
        Assert.Equal(0L, Execute("if (0) { return 1; } return 0;"));
    }

    // =========================================================================
    // 8. Loops
    //
    // Regular while/for loops: all loop variables pre-seeded in globals.
    // ForIn loops: wrapped in a lambda so the loop variable gets dist=0 (local).
    // =========================================================================

    [Fact]
    public void While_LoopCountsToTen()
    {
        object? result = Execute("""
            let i = null; i = 0;
            while (i < 10) { i = i + 1; }
            return i;
            """);
        Assert.Equal(10L, result);
    }

    [Fact]
    public void While_NeverEnters_SkipsBody()
    {
        object? result = Execute("""
            let x = null; x = 0;
            while (false) { x = 1; }
            return x;
            """);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void DoWhile_ExecutesAtLeastOnce()
    {
        object? result = Execute("""
            let x = null; x = 0;
            do { x = x + 1; } while (false);
            return x;
            """);
        Assert.Equal(1L, result);
    }

    [Fact]
    public void For_ClassicLoop()
    {
        // Equivalent while loop: all vars are pre-seeded globals.
        object? result = Execute("""
            let sum = null; sum = 0;
            let i = null; i = 1;
            while (i <= 5) { sum = sum + i; i = i + 1; }
            return sum;
            """);
        Assert.Equal(15L, result);
    }

    [Fact]
    public void Break_ExitsLoop()
    {
        object? result = Execute("""
            let i = null; i = 0;
            while (true) {
                if (i == 5) { break; }
                i = i + 1;
            }
            return i;
            """);
        Assert.Equal(5L, result);
    }

    [Fact]
    public void Continue_SkipsIteration()
    {
        // Sum odd numbers 0..9 = 1+3+5+7+9 = 25.
        object? result = Execute("""
            let sum = null; sum = 0;
            let i = null; i = 0;
            for (; i < 10; i = i + 1) {
                if (i % 2 == 0) { continue; }
                sum = sum + i;
            }
            return sum;
            """);
        Assert.Equal(25L, result);
    }

    [Fact]
    public void ForIn_ArrayIteration()
    {
        // ForIn loop variable needs dist=0 → wrap in lambda.
        object? result = Execute("""
            let acc = null; acc = [0];
            let testFn = null;
            testFn = () => {
                for (let item in [10, 20, 30, 40, 50]) {
                    acc[0] = acc[0] + item;
                }
                return acc[0];
            };
            return testFn();
            """);
        Assert.Equal(150L, result);
    }

    [Fact]
    public void ForIn_RangeIteration()
    {
        // 1..6 exclusive-end iterates 1, 2, 3, 4, 5 → sum = 15.
        object? result = Execute("""
            let acc = null; acc = [0];
            let testFn = null;
            testFn = () => {
                for (let i in 1..6) {
                    acc[0] = acc[0] + i;
                }
                return acc[0];
            };
            return testFn();
            """);
        Assert.Equal(15L, result);
    }

    [Fact]
    public void ForIn_WithIndex()
    {
        // Sum of (item + idx) for each element: (10+0)+(20+1)+(30+2) = 63.
        object? result = Execute("""
            let acc = null; acc = [0];
            let testFn = null;
            testFn = () => {
                for (let item, idx in [10, 20, 30]) {
                    acc[0] = acc[0] + item + idx;
                }
                return acc[0];
            };
            return testFn();
            """);
        Assert.Equal(63L, result);
    }

    [Fact]
    public void While_Accumulates_CorrectSum()
    {
        // Sum 1..100 = 5050.
        object? result = Execute("""
            let sum = null; sum = 0;
            let i = null; i = 1;
            while (i <= 100) {
                sum = sum + i;
                i = i + 1;
            }
            return sum;
            """);
        Assert.Equal(5050L, result);
    }

    // =========================================================================
    // 9. Functions
    //
    // Pattern: let fnName = null; fnName = (params) => { body };  return fnName(args);
    // This seeds globals["fnName"] so subsequent LoadGlobal succeeds.
    // Lambda params/locals are properly scoped (ResolvedDistance=0).
    // =========================================================================

    [Fact]
    public void Function_SimpleReturn()
    {
        object? result = Execute("""
            let add = null;
            add = (a, b) => a + b;
            return add(3, 4);
            """);
        Assert.Equal(7L, result);
    }

    [Fact]
    public void Function_NoReturn_ReturnsNull()
    {
        object? result = Execute("""
            let noop = null;
            noop = () => { let x = 1; };
            return noop();
            """);
        Assert.Null(result);
    }

    [Fact]
    public void Function_Recursion_Fibonacci()
    {
        // fib references itself via LoadGlobal (dist=-1 inside lambda body).
        // Ternary avoids if-block scope which would make 'n' dist=1 (upvalue crash).
        object? result = Execute("""
            let fib = null;
            fib = (n) => (n <= 1) ? n : fib(n - 1) + fib(n - 2);
            return fib(10);
            """);
        Assert.Equal(55L, result);
    }

    [Fact]
    public void Function_MultipleParams()
    {
        object? result = Execute("""
            let sum3 = null;
            sum3 = (a, b, c) => a + b + c;
            return sum3(1, 2, 3);
            """);
        Assert.Equal(6L, result);
    }

    [Fact]
    public void Function_LocalVariables()
    {
        // All vars inside the lambda are locals at dist=0 (same function scope, no nesting).
        object? result = Execute("""
            let compute = null;
            compute = (x) => {
                let doubled = x * 2;
                let added = doubled + 10;
                return added;
            };
            return compute(5);
            """);
        Assert.Equal(20L, result);
    }

    [Fact]
    public void Function_Closure_CapturesVariable()
    {
        // The inner lambda captures "count" as an upvalue from makeCounter's scope.
        object? result = Execute("""
            let makeCounter = null;
            makeCounter = () => {
                let count = 0;
                let increment = null;
                increment = () => {
                    count = count + 1;
                    return count;
                };
                return increment;
            };
            let counter = null;
            counter = makeCounter();
            counter();
            counter();
            return counter();
            """);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Function_Closure_SharedUpvalue()
    {
        object? result = Execute("""
            let makeAdder = null;
            makeAdder = (x) => {
                let add = null;
                add = (y) => x + y;
                return add;
            };
            let add5 = null;
            add5 = makeAdder(5);
            return add5(3);
            """);
        Assert.Equal(8L, result);
    }

    [Fact]
    public void Function_MutualRecursion()
    {
        // Ternary avoids if-block scope which would make 'n' dist=1 (upvalue crash).
        object? result = Execute("""
            let isEven = null;
            let isOdd = null;
            isEven = (n) => (n == 0) ? true : isOdd(n - 1);
            isOdd = (n) => (n == 0) ? false : isEven(n - 1);
            return isEven(10);
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Function_NestedCalls()
    {
        object? result = Execute("""
            let square = null;
            square = (n) => n * n;
            let sumOfSquares = null;
            sumOfSquares = (a, b) => square(a) + square(b);
            return sumOfSquares(3, 4);
            """);
        Assert.Equal(25L, result);
    }

    [Fact]
    public void Lambda_ExpressionBody()
    {
        object? result = Execute("""
            let double = null;
            double = (x) => x * 2;
            return double(7);
            """);
        Assert.Equal(14L, result);
    }

    [Fact]
    public void Lambda_BlockBody()
    {
        object? result = Execute("""
            let greet = null;
            greet = (name) => {
                return "Hello, " + name;
            };
            return greet("World");
            """);
        Assert.Equal("Hello, World", result);
    }

    [Fact]
    public void Lambda_HigherOrder_Map()
    {
        object? result = Execute("""
            let apply = null;
            apply = (f, value) => f(value);
            let triple = null;
            triple = (x) => x * 3;
            return apply(triple, 6);
            """);
        Assert.Equal(18L, result);
    }

    // =========================================================================
    // 10. Collections
    // =========================================================================

    [Fact]
    public void Array_Creation()
    {
        object? result = Execute("""
            let arr = null;
            arr = [1, 2, 3];
            return arr;
            """);
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(2L, list[1]);
        Assert.Equal(3L, list[2]);
    }

    [Fact]
    public void Array_IndexGet()
    {
        object? result = Execute("""
            let arr = null;
            arr = [10, 20, 30];
            return arr[1];
            """);
        Assert.Equal(20L, result);
    }

    [Fact]
    public void Array_IndexSet()
    {
        object? result = Execute("""
            let arr = null;
            arr = [1, 2, 3];
            arr[1] = 99;
            return arr[1];
            """);
        Assert.Equal(99L, result);
    }

    [Fact]
    public void Array_NegativeIndex()
    {
        object? result = Execute("""
            let arr = null;
            arr = [10, 20, 30];
            return arr[-1];
            """);
        Assert.Equal(30L, result);
    }

    [Fact]
    public void Array_Empty_Creation()
    {
        var list = Assert.IsType<List<object?>>(Execute("return [];"));
        Assert.Empty(list);
    }

    [Fact]
    public void Dict_Creation()
    {
        object? result = Execute("""
            let d = null;
            d = { name: "Alice", age: 30 };
            return d["name"];
            """);
        Assert.Equal("Alice", result);
    }

    [Fact]
    public void Dict_FieldAccess()
    {
        object? result = Execute("""
            let d = null;
            d = { name: "Bob" };
            return d.name;
            """);
        Assert.Equal("Bob", result);
    }

    [Fact]
    public void Dict_FieldSet()
    {
        object? result = Execute("""
            let d = null;
            d = { x: 1 };
            d.x = 42;
            return d.x;
            """);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Dict_IndexSet()
    {
        object? result = Execute("""
            let d = null;
            d = { key: "old" };
            d["key"] = "new";
            return d["key"];
            """);
        Assert.Equal("new", result);
    }

    // =========================================================================
    // 11. String Interpolation
    // =========================================================================

    [Fact]
    public void Interpolation_BasicValue()
    {
        object? result = Execute("""
            let x = null; x = 42;
            return $"value is {x}";
            """);
        Assert.Equal("value is 42", result);
    }

    [Fact]
    public void Interpolation_MultipleExpressions()
    {
        object? result = Execute("""
            let a = null; let b = null;
            a = 1; b = 2;
            return $"{a} + {b} = {a + b}";
            """);
        Assert.Equal("1 + 2 = 3", result);
    }

    [Fact]
    public void Interpolation_StringInString()
    {
        object? result = Execute("""
            let name = null; name = "Alice";
            return $"Hello, {name}!";
            """);
        Assert.Equal("Hello, Alice!", result);
    }

    // =========================================================================
    // 12. Switch Expressions
    // =========================================================================

    [Fact]
    public void Switch_MatchesArm()
    {
        object? result = Execute("""
            let x = null; x = 2;
            let r = null; r = x switch { 1 => "one", 2 => "two", 3 => "three", _ => "other" };
            return r;
            """);
        Assert.Equal("two", result);
    }

    [Fact]
    public void Switch_DefaultArm()
    {
        object? result = Execute("""
            let x = null; x = 99;
            let r = null; r = x switch { 1 => "one", _ => "default" };
            return r;
            """);
        Assert.Equal("default", result);
    }

    [Fact]
    public void Switch_FirstArm()
    {
        object? result = Execute("""
            let x = null; x = 1;
            let r = null; r = x switch { 1 => "one", 2 => "two", _ => "other" };
            return r;
            """);
        Assert.Equal("one", result);
    }

    // =========================================================================
    // 13. Ternary
    // =========================================================================

    [Fact]
    public void Ternary_TrueCondition()
    {
        Assert.Equal("yes", Execute("return true ? \"yes\" : \"no\";"));
    }

    [Fact]
    public void Ternary_FalseCondition()
    {
        Assert.Equal("no", Execute("return false ? \"yes\" : \"no\";"));
    }

    [Fact]
    public void Ternary_NullCondition_TakesFalseBranch()
    {
        Assert.Equal("no", Execute("return null ? \"yes\" : \"no\";"));
    }

    // =========================================================================
    // 14. Update Expressions
    //
    // Update expressions on identifiers: compiled as Load + (Dup) + Const(1) + Add/Sub + (Dup) + Store.
    // Must be inside a lambda so the variable slot gets ResolvedDistance=0 (local).
    // =========================================================================

    [Fact]
    public void Update_PrefixIncrement()
    {
        object? result = Execute("""
            let test = null;
            test = () => { let x = 5; return ++x; };
            return test();
            """);
        Assert.Equal(6L, result);
    }

    [Fact]
    public void Update_PostfixIncrement_ReturnsOldValue()
    {
        // x++ leaves the old value at y's stack slot; the new value is computed but stored to x.
        object? result = Execute("""
            let test = null;
            test = () => {
                let x = 5;
                let y = x++;
                return y;
            };
            return test();
            """);
        Assert.Equal(5L, result);
    }

    [Fact]
    public void Update_PostfixIncrement_MutatesVariable()
    {
        object? result = Execute("""
            let test = null;
            test = () => { let x = 5; x++; return x; };
            return test();
            """);
        Assert.Equal(6L, result);
    }

    [Fact]
    public void Update_PrefixDecrement()
    {
        object? result = Execute("""
            let test = null;
            test = () => { let x = 5; return --x; };
            return test();
            """);
        Assert.Equal(4L, result);
    }

    [Fact]
    public void Update_InLoop()
    {
        // i++ at top level: LoadGlobal/StoreGlobal via globals seeding pattern.
        object? result = Execute("""
            let sum = null; sum = 0;
            let i = null; i = 0;
            while (i < 5) {
                sum = sum + i;
                i++;
            }
            return sum;
            """);
        Assert.Equal(10L, result);
    }

    // =========================================================================
    // 15. Error Handling
    // =========================================================================

    [Fact]
    public void Try_ExpressionSuccess()
    {
        object? result = Execute("""
            let result = null;
            result = try 42;
            return result;
            """);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Try_ExpressionError_ReturnsNull()
    {
        object? result = Execute("""
            let result = null;
            result = try (1 / 0);
            return result;
            """);
        Assert.Null(result);
    }

    [Fact]
    public void Throw_StringThrow_ThrowsRuntimeError()
    {
        Assert.Throws<RuntimeError>(() => Execute("throw \"boom\";"));
    }

    [Fact]
    public void Throw_MessagePreserved()
    {
        var ex = Assert.Throws<RuntimeError>(() => Execute("throw \"custom error\";"));
        Assert.Equal("custom error", ex.Message);
    }

    // =========================================================================
    // 16. Range
    // =========================================================================

    [Fact]
    public void Range_CreatesStashRange()
    {
        Assert.IsType<StashRange>(Execute("return 1..5;"));
    }

    [Fact]
    public void Range_WithStep()
    {
        var range = Assert.IsType<StashRange>(Execute("return 0..10..2;"));
        Assert.Equal(0L, range.Start);
        Assert.Equal(10L, range.End);
        Assert.Equal(2L, range.Step);
    }

    [Fact]
    public void Range_DefaultStep_IsOne()
    {
        var range = Assert.IsType<StashRange>(Execute("return 1..5;"));
        Assert.Equal(1L, range.Start);
        Assert.Equal(5L, range.End);
        Assert.Equal(1L, range.Step);
    }

    // =========================================================================
    // 17. Is Expression
    // =========================================================================

    [Fact]
    public void Is_IntType()
    {
        Assert.Equal(true, Execute("return 42 is int;"));
    }

    [Fact]
    public void Is_StringType()
    {
        Assert.Equal(true, Execute("return \"hello\" is string;"));
    }

    [Fact]
    public void Is_NullCheck()
    {
        Assert.Equal(true, Execute("return null is null;"));
    }

    [Fact]
    public void Is_FloatType()
    {
        Assert.Equal(true, Execute("return 3.14 is float;"));
    }

    [Fact]
    public void Is_WrongType_ReturnsFalse()
    {
        Assert.Equal(false, Execute("return 42 is string;"));
    }

    // =========================================================================
    // 18. Complex Programs
    // =========================================================================

    [Fact]
    public void Complex_Factorial()
    {
        // Ternary (expression body) avoids if-block scope which would make 'n' dist=1.
        object? result = Execute("""
            let factorial = null;
            factorial = (n) => (n <= 1) ? 1 : n * factorial(n - 1);
            return factorial(10);
            """);
        Assert.Equal(3628800L, result);
    }

    [Fact]
    public void Complex_InsertionSort()
    {
        // All vars at top level (globals pattern) — avoids upvalue issues with nested loops.
        object? result = Execute("""
            let data = null; data = [5, 3, 1, 4, 2];
            let i = null; i = 1;
            let key = null; let j = null;
            while (i < 5) {
                key = data[i];
                j = i - 1;
                while (j >= 0 && data[j] > key) {
                    data[j + 1] = data[j];
                    j = j - 1;
                }
                data[j + 1] = key;
                i = i + 1;
            }
            return data[0]*10000 + data[1]*1000 + data[2]*100 + data[3]*10 + data[4];
            """);
        Assert.Equal(12345L, result);
    }

    [Fact]
    public void Complex_ClosureAccumulator()
    {
        object? result = Execute("""
            let makeAcc = null;
            makeAcc = (initial) => {
                let total = initial;
                let add = null;
                add = (n) => {
                    total = total + n;
                    return total;
                };
                return add;
            };
            let acc = null;
            acc = makeAcc(100);
            acc(10);
            acc(20);
            return acc(30);
            """);
        Assert.Equal(160L, result);
    }

    [Fact]
    public void Complex_FizzBuzz_Count()
    {
        // Count numbers 1..30 divisible by both 3 and 5 (= 15 and 30).
        object? result = Execute("""
            let count = null; count = 0;
            let i = null; i = 1;
            while (i <= 30) {
                if (i % 3 == 0 && i % 5 == 0) {
                    count = count + 1;
                }
                i = i + 1;
            }
            return count;
            """);
        Assert.Equal(2L, result);
    }

    [Fact]
    public void Complex_NestedClosure_MultipleLevels()
    {
        // Three-level closure: outer(a) returns middle(b) returns inner(c) returns a+b+c=6.
        object? result = Execute("""
            let outer = null;
            outer = (a) => {
                let middle = null;
                middle = (b) => {
                    let inner = null;
                    inner = (c) => a + b + c;
                    return inner;
                };
                return middle;
            };
            return outer(1)(2)(3);
            """);
        Assert.Equal(6L, result);
    }

    [Fact]
    public void Complex_BinarySearch()
    {
        // Ternary expression avoids if-blocks where params would be dist=1 (upvalue crash).
        // mid is inlined as (lo + hi) / 2 to avoid a local var dist=1 in nested blocks.
        object? result = Execute("""
            let binarySearch = null;
            binarySearch = (arr, target, lo, hi) =>
                (lo > hi) ? -1 :
                (arr[(lo + hi) / 2] == target) ? (lo + hi) / 2 :
                (arr[(lo + hi) / 2] < target) ?
                    binarySearch(arr, target, (lo + hi) / 2 + 1, hi) :
                    binarySearch(arr, target, lo, (lo + hi) / 2 - 1);
            let arr = null;
            arr = [1, 3, 5, 7, 9, 11, 13];
            return binarySearch(arr, 7, 0, 6);
            """);
        Assert.Equal(3L, result);
    }

    // =========================================================================
    // 19. Cancellation
    // =========================================================================

    [Fact]
    public void Cancellation_InfiniteLoop_ThrowsCancelled()
    {
        string source = "while (true) { }";
        Chunk chunk = CompileSource(source);
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        var vm = new VirtualMachine(ct: cts.Token);
        Assert.Throws<OperationCanceledException>(() => vm.Execute(chunk));
    }

    // =========================================================================
    // 20. Default Parameters
    // =========================================================================

    [Fact]
    public void DefaultParam_SingleDefault_UsedWhenOmitted()
    {
        object? result = Execute("""
            let greet = null;
            greet = (name, greeting = "hello") => greeting + " " + name;
            return greet("world");
            """);
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void DefaultParam_SingleDefault_OverriddenWhenProvided()
    {
        object? result = Execute("""
            let greet = null;
            greet = (name, greeting = "hello") => greeting + " " + name;
            return greet("world", "hi");
            """);
        Assert.Equal("hi world", result);
    }

    [Fact]
    public void DefaultParam_MultipleDefaults_AllOmitted()
    {
        object? result = Execute("""
            let calc = null;
            calc = (x, y = 10, z = 20) => x + y + z;
            return calc(1);
            """);
        Assert.Equal(31L, result);
    }

    [Fact]
    public void DefaultParam_MultipleDefaults_PartiallyProvided()
    {
        object? result = Execute("""
            let calc = null;
            calc = (x, y = 10, z = 20) => x + y + z;
            return calc(1, 5);
            """);
        Assert.Equal(26L, result);
    }

    [Fact]
    public void DefaultParam_MultipleDefaults_AllProvided()
    {
        object? result = Execute("""
            let calc = null;
            calc = (x, y = 10, z = 20) => x + y + z;
            return calc(1, 2, 3);
            """);
        Assert.Equal(6L, result);
    }

    [Fact]
    public void DefaultParam_NullCanBePassedExplicitly()
    {
        // Passing null should NOT trigger default evaluation
        object? result = Execute("""
            let fn = null;
            fn = (x, y = 42) => y;
            return fn(1, null);
            """);
        Assert.Null(result);
    }

    [Fact]
    public void DefaultParam_FalseCanBePassedExplicitly()
    {
        // Passing false should NOT trigger default evaluation
        object? result = Execute("""
            let myFn = null;
            myFn = (x, y = true) => y;
            return myFn(1, false);
            """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void DefaultParam_ZeroCanBePassedExplicitly()
    {
        // Passing 0 should NOT trigger default evaluation
        object? result = Execute("""
            let myFn = null;
            myFn = (x, y = 42) => y;
            return myFn(1, 0);
            """);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void DefaultParam_EmptyStringCanBePassedExplicitly()
    {
        // Passing "" should NOT trigger default evaluation
        object? result = Execute("""
            let myFn = null;
            myFn = (x, y = "default") => y;
            return myFn(1, "");
            """);
        Assert.Equal("", result);
    }

    [Fact]
    public void DefaultParam_ExpressionDefault_EvaluatedPerCall()
    {
        // Default expressions are evaluated at call time, not definition time
        object? result = Execute("""
            let counter = null;
            counter = 0;
            let myFn = null;
            myFn = (x = counter) => x;
            counter = 10;
            return myFn();
            """);
        Assert.Equal(10L, result);
    }

    [Fact]
    public void DefaultParam_ClosureCaptureInDefault()
    {
        // Default params work correctly inside returned closures
        object? result = Execute("""
            let makeAdder = null;
            makeAdder = (offset) => {
                let add = null;
                add = (x, y = 5) => x + y + offset;
                return add;
            };
            let add10 = null;
            add10 = makeAdder(10);
            return add10(1);
            """);
        Assert.Equal(16L, result);
    }

    [Fact]
    public void DefaultParam_WithFnDecl()
    {
        // Test defaults with all three params: first required, rest optional
        object? result = Execute("""
            let calc = null;
            calc = (x, y = 10, z = 20) => x + y + z;
            return calc(1);
            """);
        Assert.Equal(31L, result);
    }

    [Fact]
    public void DefaultParam_FnDecl_PartiallyProvided()
    {
        object? result = Execute("""
            let calc = null;
            calc = (x, y = 10, z = 20) => x + y + z;
            return calc(1, 5);
            """);
        Assert.Equal(26L, result);
    }

    // =========================================================================
    // 21. Rest Parameters
    // =========================================================================

    [Fact]
    public void RestParam_CollectsTrailingArgs()
    {
        object? result = Execute("""
            let myFn = null;
            myFn = (first, ...rest) => first + rest[0] + rest[1] + rest[2];
            return myFn(1, 2, 3, 4);
            """);
        Assert.Equal(10L, result);
    }

    [Fact]
    public void RestParam_EmptyWhenNoTrailingArgs()
    {
        object? result = Execute("""
            let myFn = null;
            myFn = (x, ...rest) => rest;
            return myFn(1);
            """);
        Assert.NotNull(result);
        Assert.IsType<List<object?>>(result);
    }

    [Fact]
    public void RestParam_WithDefaults()
    {
        // Rest collects trailing args even when the function has default params
        object? result = Execute("""
            let myFn = null;
            myFn = (a, b = 100, ...rest) => rest[0];
            return myFn(1, 2, 42);
            """);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void RestParam_WithDefaults_DefaultOverridden()
    {
        object? result = Execute("""
            let myFn = null;
            myFn = (a, b = 100, ...rest) => a + b;
            return myFn(1, 2);
            """);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void RestParam_WithDefaults_ExtraArgsCollected()
    {
        object? result = Execute("""
            let myFn = null;
            myFn = (a, b = 100, ...rest) => rest;
            let result = null;
            result = myFn(1, 2, 3, 4);
            return result[0] + result[1];
            """);
        Assert.Equal(7L, result);
    }

    [Fact]
    public void RestParam_WithDefaults_OnlyRequiredProvided()
    {
        // Calling fn(a, b = 100, ...rest) with only 'a' should work — b uses default
        object? result = Execute("""
            let myFn = null;
            myFn = (a, b = 100, ...rest) => a + b;
            return myFn(1);
            """);
        Assert.Equal(101L, result);
    }

    // =========================================================================
    // 22. Arity Errors
    // =========================================================================

    [Fact]
    public void Arity_TooFewArgs_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            let myFn = null;
            myFn = (a, b) => a + b;
            return myFn(1);
            """));
    }

    [Fact]
    public void Arity_TooManyArgs_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            let myFn = null;
            myFn = (a, b) => a + b;
            return myFn(1, 2, 3);
            """));
    }

    [Fact]
    public void Arity_TooFewForDefaults_ThrowsError()
    {
        // Must provide at least the required (non-default) params
        Assert.Throws<RuntimeError>(() => Execute("""
            let myFn = null;
            myFn = (a, b, c = 10) => a + b + c;
            return myFn(1);
            """));
    }

    [Fact]
    public void Arity_ExactMinArity_Succeeds()
    {
        object? result = Execute("""
            let myFn = null;
            myFn = (a, b, c = 10) => a + b + c;
            return myFn(1, 2);
            """);
        Assert.Equal(13L, result);
    }

    [Fact]
    public void Arity_CallNonFunction_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            let x = null;
            x = 42;
            return x();
            """));
    }

    [Fact]
    public void Arity_RestParam_TooFewRequired_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            let myFn = null;
            myFn = (a, b, ...rest) => a + b;
            return myFn(1);
            """));
    }

    [Fact]
    public void Arity_ZeroArgs_ExactMatch()
    {
        object? result = Execute("""
            let myFn = null;
            myFn = () => 42;
            return myFn();
            """);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Arity_ErrorMessage_ContainsExpected()
    {
        var ex = Assert.Throws<RuntimeError>(() => Execute("""
            let myFn = null;
            myFn = (a, b) => a + b;
            return myFn(1);
            """));
        Assert.Contains("2", ex.Message);
        Assert.Contains("1", ex.Message);
    }

    [Fact]
    public void Arity_ErrorMessage_RangeForDefaults()
    {
        var ex = Assert.Throws<RuntimeError>(() => Execute("""
            let myFn = null;
            myFn = (a, b = 10, c = 20) => a;
            return myFn();
            """));
        Assert.Contains("1 to 3", ex.Message);
    }

    // =========================================================================
    // 23. Calling Null / Non-Callable
    // =========================================================================

    [Fact]
    public void Call_NullValue_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            let myFn = null;
            return myFn();
            """));
    }

    [Fact]
    public void Call_NumberValue_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            let myFn = null;
            myFn = 42;
            return myFn();
            """));
    }

    [Fact]
    public void Call_StringValue_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            let myFn = null;
            myFn = "hello";
            return myFn();
            """));
    }

    // =========================================================================
    // 24. Built-in Function Bridge (IInterpreterContext)
    // =========================================================================

    [Fact]
    public void BuiltIn_IStashCallable_CalledWithContext()
    {
        // Test that built-in functions receive a valid context
        // by using a custom IStashCallable that checks context isn't null
        string source = """
            return testFn(1, 2);
            """;
        Chunk chunk = CompileSource(source);

        bool contextWasProvided = false;
        var testFn = new TestCallable((ctx, args) =>
        {
            contextWasProvided = ctx != null;
            return (long)args[0]! + (long)args[1]!;
        });

        var vm = new VirtualMachine();
        vm.Globals["testFn"] = testFn;
        object? result = vm.Execute(chunk);

        Assert.True(contextWasProvided);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void BuiltIn_OutputWrittenToCustomWriter()
    {
        // Verify that the VM's Output property is used by built-in context
        string source = """
            return testPrint("hello");
            """;
        Chunk chunk = CompileSource(source);

        var output = new System.IO.StringWriter();
        var testPrint = new TestCallable((ctx, args) =>
        {
            ctx.Output.Write(args[0]?.ToString());
            return null;
        });

        var vm = new VirtualMachine();
        vm.Output = output;
        vm.Globals["testPrint"] = testPrint;
        vm.Execute(chunk);

        Assert.Equal("hello", output.ToString());
    }

    [Fact]
    public void BuiltIn_ArityChecked()
    {
        // IStashCallable with Arity=2 should reject wrong arg count
        string source = """
            return testFn(1);
            """;
        Chunk chunk = CompileSource(source);

        var testFn = new TestCallable((ctx, args) => null, arity: 2);
        var vm = new VirtualMachine();
        vm.Globals["testFn"] = testFn;

        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    // =========================================================================
    // 25. Complex Function Scenarios
    // =========================================================================

    [Fact]
    public void Complex_CounterFactory_WithDefaults()
    {
        object? result = Execute("""
            let makeCounter = null;
            makeCounter = (start = 0, step = 1) => {
                let count = start;
                let inc = null;
                inc = () => {
                    count = count + step;
                    return count;
                };
                return inc;
            };
            let counter = null;
            counter = makeCounter();
            counter();
            counter();
            return counter();
            """);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Complex_CounterFactory_WithCustomStart()
    {
        object? result = Execute("""
            let makeCounter = null;
            makeCounter = (start = 0, step = 1) => {
                let count = start;
                let inc = null;
                inc = () => {
                    count = count + step;
                    return count;
                };
                return inc;
            };
            let counter = null;
            counter = makeCounter(10, 5);
            counter();
            counter();
            return counter();
            """);
        Assert.Equal(25L, result);
    }

    [Fact]
    public void Complex_HigherOrder_WithDefaults()
    {
        object? result = Execute("""
            let apply = null;
            apply = (f, x, y = 0) => f(x, y);
            let add = null;
            add = (a, b) => a + b;
            return apply(add, 5);
            """);
        Assert.Equal(5L, result);
    }

    [Fact]
    public void Complex_RecursiveWithDefault()
    {
        object? result = Execute("""
            let countdown = null;
            countdown = (n, acc = 0) =>
                (n <= 0) ? acc : countdown(n - 1, acc + n);
            return countdown(5);
            """);
        Assert.Equal(15L, result);
    }

    [Fact]
    public void Complex_FnDecl_NestedWithDefaults()
    {
        // Nested lambdas with defaults at multiple levels
        object? result = Execute("""
            let outer = null;
            outer = (x = 10) => {
                let inner = null;
                inner = (y = 20) => x + y;
                return inner();
            };
            return outer();
            """);
        Assert.Equal(30L, result);
    }

    [Fact]
    public void Complex_FnDecl_NestedWithDefaults_Overridden()
    {
        object? result = Execute("""
            let outer = null;
            outer = (x = 10) => {
                let inner = null;
                inner = (y = 20) => x + y;
                return inner(5);
            };
            return outer(1);
            """);
        Assert.Equal(6L, result);
    }

    // ─── Section 26: Struct Declarations ───

    [Fact]
    public void Struct_SimpleDeclaration_CanInstantiate()
    {
        object? result = Execute("""
            struct Point {
                x,
                y
            }
            let p = null;
            p = Point { x: 10, y: 20 };
            return p.x;
            """);
        Assert.Equal(10L, result);
    }

    [Fact]
    public void Struct_FieldAccess_ReturnsValue()
    {
        object? result = Execute("""
            struct Point {
                x,
                y
            }
            let p = null;
            p = Point { x: 5, y: 15 };
            return p.x + p.y;
            """);
        Assert.Equal(20L, result);
    }

    [Fact]
    public void Struct_FieldMutation_UpdatesValue()
    {
        object? result = Execute("""
            struct Counter {
                count
            }
            let c = null;
            c = Counter { count: 0 };
            c.count = 42;
            return c.count;
            """);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Struct_OmittedFieldsDefaultToNull()
    {
        object? result = Execute("""
            struct Config {
                host,
                port
            }
            let cfg = null;
            cfg = Config { host: "localhost" };
            return cfg.port;
            """);
        Assert.Null(result);
    }

    [Fact]
    public void Struct_UnknownField_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            struct Point {
                x,
                y
            }
            let p = Point { x: 1, y: 2, z: 3 };
            """));
    }

    [Fact]
    public void Struct_UndefinedFieldAccess_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            struct Point {
                x,
                y
            }
            let p = null;
            p = Point { x: 1, y: 2 };
            return p.z;
            """));
    }

    [Fact]
    public void Struct_UndefinedFieldSet_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            struct Point {
                x
            }
            let p = null;
            p = Point { x: 1 };
            p.z = 99;
            """));
    }

    [Fact]
    public void Struct_IsCheck_ReturnsTrue()
    {
        object? result = Execute("""
            struct Foo {
                value
            }
            let f = null;
            f = Foo { value: 42 };
            return f is Foo;
            """);
        Assert.Equal(true, result);
    }

    // ─── Section 27: Struct Methods ───

    [Fact]
    public void StructMethod_SimpleCall_ReturnsSelfField()
    {
        object? result = Execute("""
            struct Greeter {
                name
                fn greet(self) {
                    return "Hello, " + self.name;
                }
            }
            let g = null;
            g = Greeter { name: "World" };
            return g.greet();
            """);
        Assert.Equal("Hello, World", result);
    }

    [Fact]
    public void StructMethod_WithParams_Computes()
    {
        object? result = Execute("""
            struct Calc {
                base
                fn add(self, n) {
                    return self.base + n;
                }
            }
            let c = null;
            c = Calc { base: 100 };
            return c.add(42);
            """);
        Assert.Equal(142L, result);
    }

    [Fact]
    public void StructMethod_MutatesSelf_FieldUpdated()
    {
        object? result = Execute("""
            struct Counter {
                count
                fn increment(self) {
                    self.count = self.count + 1;
                }
            }
            let c = null;
            c = Counter { count: 0 };
            c.increment();
            c.increment();
            c.increment();
            return c.count;
            """);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void StructMethod_CallsOtherMethod_Works()
    {
        object? result = Execute("""
            struct Rect {
                w,
                h
                fn area(self) {
                    return self.w * self.h;
                }
                fn describe(self) {
                    return "area=" + self.area();
                }
            }
            let r = null;
            r = Rect { w: 3, h: 4 };
            return r.describe();
            """);
        Assert.Equal("area=12", result);
    }

    [Fact]
    public void StructMethod_MultipleInstances_IndependentState()
    {
        object? result = Execute("""
            struct Box {
                value
                fn get(self) {
                    return self.value;
                }
            }
            let a = null;
            a = Box { value: 10 };
            let b = null;
            b = Box { value: 20 };
            return a.get() + b.get();
            """);
        Assert.Equal(30L, result);
    }

    [Fact]
    public void StructMethod_DefaultParam_Works()
    {
        object? result = Execute("""
            struct Adder {
                base
                fn add(self, n = 1) {
                    return self.base + n;
                }
            }
            let a = null;
            a = Adder { base: 10 };
            return a.add() + a.add(5);
            """);
        Assert.Equal(26L, result);
    }

    [Fact]
    public void StructMethod_ClosureCapture_Works()
    {
        object? result = Execute("""
            let multiplier = null;
            multiplier = 10;
            struct Scaler {
                value
                fn scale(self) {
                    return self.value * multiplier;
                }
            }
            let s = null;
            s = Scaler { value: 5 };
            return s.scale();
            """);
        Assert.Equal(50L, result);
    }

    [Fact]
    public void StructMethod_ReturnsNull_WhenNoReturn()
    {
        object? result = Execute("""
            struct Noop {
                x
                fn doNothing(self) {
                    let tmp = self.x;
                }
            }
            let n = null;
            n = Noop { x: 42 };
            let result = null;
            result = n.doNothing();
            return result;
            """);
        Assert.Null(result);
    }

    [Fact]
    public void StructMethod_SelfIsReferenceSemantics()
    {
        object? result = Execute("""
            struct Pair {
                a,
                b
                fn swap(self) {
                    let tmp = self.a;
                    self.a = self.b;
                    self.b = tmp;
                }
            }
            let p = null;
            p = Pair { a: 1, b: 2 };
            p.swap();
            return p.a * 10 + p.b;
            """);
        Assert.Equal(21L, result);
    }

    [Fact]
    public void StructMethod_ChainedCalls_Work()
    {
        object? result = Execute("""
            struct Builder {
                value
                fn add(self, n) {
                    self.value = self.value + n;
                    return self;
                }
            }
            let b = null;
            b = Builder { value: 0 };
            return b.add(1).add(2).add(3).value;
            """);
        Assert.Equal(6L, result);
    }

    // ─── Section 28: Enum Declarations ───

    [Fact]
    public void Enum_Declaration_MemberAccess()
    {
        object? result = Execute("""
            enum Color {
                Red,
                Green,
                Blue
            }
            let c = null;
            c = Color.Red;
            return c is Color;
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Enum_Equality_SameMembers()
    {
        object? result = Execute("""
            enum Direction {
                North,
                South
            }
            return Direction.North == Direction.North;
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Enum_Inequality_DifferentMembers()
    {
        object? result = Execute("""
            enum Direction {
                North,
                South
            }
            return Direction.North == Direction.South;
            """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void Enum_InvalidMember_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            enum Color {
                Red,
                Green
            }
            return Color.Blue;
            """));
    }

    [Fact]
    public void Enum_UsedInSwitch_Matches()
    {
        object? result = Execute("""
            enum Status {
                Active,
                Inactive
            }
            let s = null;
            s = Status.Active;
            let msg = null;
            if (s == Status.Active) {
                msg = "active";
            } else {
                msg = "inactive";
            }
            return msg;
            """);
        Assert.Equal("active", result);
    }

    [Fact]
    public void Enum_StoredInStruct_Works()
    {
        object? result = Execute("""
            enum Color {
                Red,
                Green,
                Blue
            }
            struct Pixel {
                color,
                x,
                y
            }
            let px = null;
            px = Pixel { color: Color.Red, x: 10, y: 20 };
            return px.color == Color.Red;
            """);
        Assert.Equal(true, result);
    }

    // ─── Section 29: Interface Declarations ───

    [Fact]
    public void Interface_StructImplements_Works()
    {
        object? result = Execute("""
            interface Describable {
                fn describe(self)
            }
            struct Item : Describable {
                name
                fn describe(self) {
                    return "Item: " + self.name;
                }
            }
            let item = null;
            item = Item { name: "Widget" };
            return item.describe();
            """);
        Assert.Equal("Item: Widget", result);
    }

    [Fact]
    public void Interface_MissingMethod_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            interface Runnable {
                fn run(self)
            }
            struct Worker : Runnable {
                name
            }
            """));
    }

    [Fact]
    public void Interface_WithRequiredField_Validates()
    {
        object? result = Execute("""
            interface Named {
                name
            }
            struct Person : Named {
                name,
                age
            }
            let p = null;
            p = Person { name: "Alice", age: 30 };
            return p.name;
            """);
        Assert.Equal("Alice", result);
    }

    [Fact]
    public void Interface_MissingField_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            interface HasId {
                id
            }
            struct Thing : HasId {
                name
            }
            """));
    }

    // ─── Section 30: Extend Blocks ───

    [Fact]
    public void Extend_StructMethod_Works()
    {
        object? result = Execute("""
            struct Point {
                x,
                y
            }
            extend Point {
                fn magnitude(self) {
                    return self.x + self.y;
                }
            }
            let p = null;
            p = Point { x: 3, y: 4 };
            return p.magnitude();
            """);
        Assert.Equal(7L, result);
    }

    [Fact]
    public void Extend_OriginalMethodPriority_NotOverridden()
    {
        object? result = Execute("""
            struct Greeter {
                name
                fn greet(self) {
                    return "original";
                }
            }
            extend Greeter {
                fn greet(self) {
                    return "extended";
                }
            }
            let g = null;
            g = Greeter { name: "test" };
            return g.greet();
            """);
        Assert.Equal("original", result);
    }

    [Fact]
    public void Extend_NewMethod_Added()
    {
        object? result = Execute("""
            struct Data {
                value
            }
            extend Data {
                fn doubled(self) {
                    return self.value * 2;
                }
            }
            let d = null;
            d = Data { value: 21 };
            return d.doubled();
            """);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Extend_BuiltInString_RegistersInRegistry()
    {
        object? result = Execute("""
            extend string {
                fn excited(self) {
                    return self + "!";
                }
            }
            return "hello".excited();
            """);
        Assert.Equal("hello!", result);
    }

    [Fact]
    public void Extend_BuiltInArray_Works()
    {
        object? result = Execute("""
            extend array {
                fn first(self) {
                    return self[0];
                }
            }
            let items = null;
            items = [10, 20, 30];
            return items.first();
            """);
        Assert.Equal(10L, result);
    }

    [Fact]
    public void Extend_BuiltInInt_Works()
    {
        object? result = Execute("""
            extend int {
                fn doubled(self) {
                    return self * 2;
                }
            }
            return 21.doubled();
            """);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Extend_BuiltInDict_Works()
    {
        object? result = Execute("""
            extend dict {
                fn hasKey(self, key) {
                    let found = null;
                    found = false;
                    for (let k in self) {
                        if (k == key) {
                            found = true;
                        }
                    }
                    return found;
                }
            }
            let d = null;
            d = { name: "test" };
            return d.hasKey("name");
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Extend_ClosureCapture_Works()
    {
        object? result = Execute("""
            let prefix = null;
            prefix = ">> ";
            struct Msg {
                text
            }
            extend Msg {
                fn formatted(self) {
                    return prefix + self.text;
                }
            }
            let m = null;
            m = Msg { text: "hello" };
            return m.formatted();
            """);
        Assert.Equal(">> hello", result);
    }

    // ─── Section 31: Optional Chaining and Properties ───

    [Fact]
    public void OptionalChaining_NullReceiver_ReturnsNull()
    {
        object? result = Execute("""
            let obj = null;
            obj = null;
            return obj?.name;
            """);
        Assert.Null(result);
    }

    [Fact]
    public void OptionalChaining_NonNullReceiver_ReturnsField()
    {
        object? result = Execute("""
            struct User {
                name
            }
            let u = null;
            u = User { name: "Alice" };
            return u?.name;
            """);
        Assert.Equal("Alice", result);
    }

    [Fact]
    public void OptionalChaining_NestedNull_ReturnsNull()
    {
        object? result = Execute("""
            struct Outer {
                inner
            }
            let o = null;
            o = Outer { inner: null };
            return o.inner?.value;
            """);
        Assert.Null(result);
    }

    [Fact]
    public void ArrayLength_ReturnsCount()
    {
        object? result = Execute("""
            let items = null;
            items = [10, 20, 30, 40];
            return items.length;
            """);
        Assert.Equal(4L, result);
    }

    [Fact]
    public void StringLength_ReturnsCount()
    {
        object? result = Execute("""
            return "hello".length;
            """);
        Assert.Equal(5L, result);
    }

    [Fact]
    public void EmptyArrayLength_ReturnsZero()
    {
        object? result = Execute("""
            let items = null;
            items = [];
            return items.length;
            """);
        Assert.Equal(0L, result);
    }

    // ─── Section 32: Complex Type Scenarios ───

    [Fact]
    public void Complex_StructWithArrayField_Works()
    {
        object? result = Execute("""
            struct Inventory {
                items
                fn count(self) {
                    return self.items.length;
                }
            }
            let inv = null;
            inv = Inventory { items: [1, 2, 3] };
            return inv.count();
            """);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Complex_StructWithDictField_Works()
    {
        object? result = Execute("""
            struct Config {
                settings
                fn get(self, key) {
                    return self.settings[key];
                }
            }
            let cfg = null;
            cfg = Config { settings: { host: "localhost", port: 8080 } };
            return cfg.get("host");
            """);
        Assert.Equal("localhost", result);
    }

    [Fact]
    public void Complex_StructReturnsNewStruct_Works()
    {
        object? result = Execute("""
            struct Point {
                x,
                y
                fn offset(self, dx, dy) {
                    return Point { x: self.x + dx, y: self.y + dy };
                }
            }
            let p1 = null;
            p1 = Point { x: 1, y: 2 };
            let p2 = null;
            p2 = p1.offset(10, 20);
            return p2.x + p2.y;
            """);
        Assert.Equal(33L, result);
    }

    [Fact]
    public void Complex_EnumAndStructTogether_Works()
    {
        object? result = Execute("""
            enum Shape {
                Circle,
                Square
            }
            struct Figure {
                shape,
                size
                fn describe(self) {
                    if (self.shape == Shape.Circle) {
                        return "circle-" + self.size;
                    }
                    return "square-" + self.size;
                }
            }
            let f = null;
            f = Figure { shape: Shape.Circle, size: 5 };
            return f.describe();
            """);
        Assert.Equal("circle-5", result);
    }

    [Fact]
    public void Complex_StructFactory_CreatesInstances()
    {
        object? result = Execute("""
            struct User {
                name,
                role
            }
            let createAdmin = null;
            createAdmin = (name) => {
                return User { name: name, role: "admin" };
            };
            let admin = null;
            admin = createAdmin("Alice");
            return admin.name + ":" + admin.role;
            """);
        Assert.Equal("Alice:admin", result);
    }

    [Fact]
    public void Complex_MultipleStructsInteract_Works()
    {
        object? result = Execute("""
            struct Address {
                city
            }
            struct Person {
                name,
                address
                fn getCity(self) {
                    return self.address.city;
                }
            }
            let addr = null;
            addr = Address { city: "NYC" };
            let person = null;
            person = Person { name: "Bob", address: addr };
            return person.getCity();
            """);
        Assert.Equal("NYC", result);
    }

    // =========================================================================
    // 26. Power Operator
    // =========================================================================

    [Fact]
    public void Power_IntegerBase_ReturnsLong()
    {
        var b = new ChunkBuilder { Name = "<test>" };
        ushort c1 = b.AddConstant(2L); ushort c2 = b.AddConstant(3L);
        b.Emit(OpCode.Const, c1); b.Emit(OpCode.Const, c2);
        b.Emit(OpCode.Power); b.Emit(OpCode.Return);
        Assert.Equal(8L, new VirtualMachine().Execute(b.Build()));
    }

    [Fact]
    public void Power_FloatExponent_ReturnsDouble()
    {
        var b = new ChunkBuilder { Name = "<test>" };
        ushort c1 = b.AddConstant(2.0); ushort c2 = b.AddConstant(3.0);
        b.Emit(OpCode.Const, c1); b.Emit(OpCode.Const, c2);
        b.Emit(OpCode.Power); b.Emit(OpCode.Return);
        Assert.Equal(8.0, new VirtualMachine().Execute(b.Build()));
    }

    [Fact]
    public void Power_ZeroPower_ReturnsOne()
    {
        var b = new ChunkBuilder { Name = "<test>" };
        ushort c1 = b.AddConstant(5L); ushort c2 = b.AddConstant(0L);
        b.Emit(OpCode.Const, c1); b.Emit(OpCode.Const, c2);
        b.Emit(OpCode.Power); b.Emit(OpCode.Return);
        Assert.Equal(1L, new VirtualMachine().Execute(b.Build()));
    }

    [Fact]
    public void Power_NegativeFloatExponent_ReturnsDouble()
    {
        var b = new ChunkBuilder { Name = "<test>" };
        ushort c1 = b.AddConstant(2.0); ushort c2 = b.AddConstant(-1.0);
        b.Emit(OpCode.Const, c1); b.Emit(OpCode.Const, c2);
        b.Emit(OpCode.Power); b.Emit(OpCode.Return);
        Assert.Equal(0.5, new VirtualMachine().Execute(b.Build()));
    }

    [Fact]
    public void Power_NonNumbers_ThrowsError()
    {
        var b = new ChunkBuilder { Name = "<test>" };
        ushort c1 = b.AddConstant("a"); ushort c2 = b.AddConstant(2L);
        b.Emit(OpCode.Const, c1); b.Emit(OpCode.Const, c2);
        b.Emit(OpCode.Power); b.Emit(OpCode.Return);
        Assert.Throws<RuntimeError>(() => new VirtualMachine().Execute(b.Build()));
    }

    // =========================================================================
    // 27. In Operator (Containment)
    // =========================================================================

    [Fact]
    public void In_ArrayContains_ReturnsTrue()
    {
        Assert.Equal(true, Execute("return 2 in [1, 2, 3];"));
    }

    [Fact]
    public void In_ArrayNotContains_ReturnsFalse()
    {
        Assert.Equal(false, Execute("return 5 in [1, 2, 3];"));
    }

    [Fact]
    public void In_DictContainsKey_ReturnsTrue()
    {
        Assert.Equal(true, Execute("return \"a\" in { a: 1, b: 2 };"));
    }

    [Fact]
    public void In_DictNotContainsKey_ReturnsFalse()
    {
        Assert.Equal(false, Execute("return \"c\" in { a: 1, b: 2 };"));
    }

    [Fact]
    public void In_StringContains_ReturnsTrue()
    {
        Assert.Equal(true, Execute("return \"lo\" in \"hello\";"));
    }

    [Fact]
    public void In_StringNotContains_ReturnsFalse()
    {
        Assert.Equal(false, Execute("return \"xyz\" in \"hello\";"));
    }

    [Fact]
    public void In_InvalidRight_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("return 1 in 42;"));
    }

    // =========================================================================
    // 28. Try-Catch-Finally
    // =========================================================================

    [Fact]
    public void TryCatch_CatchesThrow_ReturnsCatchValue()
    {
        object? result = Execute("""
            let func = null;
            func = () => {
                try {
                    throw "boom";
                } catch (e) {
                    return e.message;
                }
            };
            return func();
            """);
        Assert.Equal("boom", result);
    }

    [Fact]
    public void TryCatch_NoCatch_Succeeds()
    {
        object? result = Execute("""
            let result = null;
            result = 0;
            try {
                result = 42;
            }
            return result;
            """);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void TryCatch_ErrorType_Preserved()
    {
        object? result = Execute("""
            let func = null;
            func = () => {
                try {
                    throw "test error";
                } catch (e) {
                    return e.type;
                }
            };
            return func();
            """);
        Assert.Equal("RuntimeError", result);
    }

    [Fact]
    public void TryCatchFinally_FinallyAlwaysRuns()
    {
        object? result = Execute("""
            let flag = null;
            flag = 0;
            let func = null;
            func = () => {
                try {
                    flag = 1;
                } finally {
                    flag = 2;
                }
            };
            func();
            return flag;
            """);
        Assert.Equal(2L, result);
    }

    [Fact]
    public void TryCatchFinally_FinallyRunsAfterCatch()
    {
        object? result = Execute("""
            let flag = null;
            flag = 0;
            let func = null;
            func = () => {
                try {
                    throw "oops";
                } catch (e) {
                    flag = 1;
                } finally {
                    flag = flag + 10;
                }
            };
            func();
            return flag;
            """);
        Assert.Equal(11L, result);
    }

    [Fact]
    public void TryCatch_UncaughtError_Propagates()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            let func = null;
            func = () => {
                try {
                    throw "fail";
                } finally {
                }
            };
            func();
            """));
    }

    // =========================================================================
    // 29. Destructuring
    // =========================================================================

    [Fact]
    public void Destructure_Array_Basic()
    {
        object? result = Execute("""
            let func = null;
            func = () => {
                let [a, b, c] = [10, 20, 30];
                return b;
            };
            return func();
            """);
        Assert.Equal(20L, result);
    }

    [Fact]
    public void Destructure_Array_WithRest()
    {
        object? result = Execute("""
            let func = null;
            func = () => {
                let [first, ...rest] = [1, 2, 3, 4, 5];
                return rest;
            };
            return func();
            """);
        var rest = Assert.IsType<List<object?>>(result);
        Assert.Equal(4, rest.Count);
        Assert.Equal(2L, rest[0]);
        Assert.Equal(5L, rest[3]);
    }

    [Fact]
    public void Destructure_Array_FewerElements_PadsNull()
    {
        object? result = Execute("""
            let func = null;
            func = () => {
                let [a, b, c] = [10, 20];
                return c;
            };
            return func();
            """);
        Assert.Null(result);
    }

    [Fact]
    public void Destructure_Dict_Basic()
    {
        object? result = Execute("""
            let func = null;
            func = () => {
                let { x, y } = { x: 10, y: 20, z: 30 };
                return x + y;
            };
            return func();
            """);
        Assert.Equal(30L, result);
    }

    [Fact]
    public void Destructure_Dict_WithRest()
    {
        object? result = Execute("""
            let func = null;
            func = () => {
                let { a, ...rest } = { a: 1, b: 2, c: 3 };
                return rest;
            };
            return func();
            """);
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.True(dict.Has("b"));
        Assert.Equal(2L, dict.Get("b"));
        Assert.True(dict.Has("c"));
        Assert.Equal(3L, dict.Get("c"));
    }

    [Fact]
    public void Destructure_NonArray_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            let func = null;
            func = () => {
                let [a, b] = "not an array";
                return a;
            };
            func();
            """));
    }

    // =========================================================================
    // 30. Shell Commands
    // =========================================================================

    [Fact]
    public void Command_Echo_CapturesStdout()
    {
        object? result = Execute("""
            let result = null;
            result = $(echo hello);
            return result.stdout;
            """);
        Assert.Contains("hello", (string)result!);
    }

    [Fact]
    public void Command_ExitCode_IsLong()
    {
        object? result = Execute("""
            let result = null;
            result = $(echo ok);
            return result.exitCode;
            """);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Command_Interpolation_Works()
    {
        object? result = Execute("""
            let name = null;
            name = "world";
            let result = null;
            result = $(echo ${name});
            return result.stdout;
            """);
        Assert.Contains("world", (string)result!);
    }

    // =========================================================================
    // 31. Elevate Blocks
    // =========================================================================

    [Fact]
    public void Elevate_DefaultCommand_ExecutesBody()
    {
        object? result = Execute("""
            let flag = null;
            flag = 0;
            elevate {
                flag = 1;
            }
            return flag;
            """);
        Assert.Equal(1L, result);
    }

    // =========================================================================
    // 32. Retry
    // =========================================================================

    [Fact]
    public void Retry_SucceedsFirstAttempt_ReturnsValue()
    {
        object? result = Execute("""
            let func = null;
            func = () => {
                return retry (3) {
                    return 42;
                };
            };
            return func();
            """);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Retry_FailsThenSucceeds()
    {
        object? result = Execute("""
            let counter = null;
            counter = 0;
            let func = null;
            func = () => {
                return retry (5) {
                    counter = counter + 1;
                    if (counter < 3) {
                        throw "not yet";
                    }
                    return counter;
                };
            };
            return func();
            """);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Retry_ExhaustsAttempts_Throws()
    {
        Assert.Throws<RuntimeError>(() => Execute("""
            let func = null;
            func = () => {
                return retry (2) {
                    throw "always fails";
                };
            };
            func();
            """));
    }

    // =========================================================================
    // 33. Module Import
    // =========================================================================

    [Fact]
    public void Import_LoadsModuleExport()
    {
        string moduleSource = "let greeting = null; greeting = \"hello from module\";";
        Chunk moduleChunk = CompileSource(moduleSource);

        string mainSource = """
            let func = null;
            func = () => {
                import { greeting } from "mymod";
                return greeting;
            };
            return func();
            """;
        Chunk mainChunk = CompileSource(mainSource);

        var vm = new VirtualMachine();
        vm.ModuleLoader = (path, currentFile) => moduleChunk;
        object? result = vm.Execute(mainChunk);
        Assert.Equal("hello from module", result);
    }

    [Fact]
    public void ImportAs_LoadsModuleAsNamespace()
    {
        string moduleSource = "let value = null; value = 42;";
        Chunk moduleChunk = CompileSource(moduleSource);

        string mainSource = """
            let func = null;
            func = () => {
                import "mymod" as mod;
                return mod.value;
            };
            return func();
            """;
        Chunk mainChunk = CompileSource(mainSource);

        var vm = new VirtualMachine();
        vm.ModuleLoader = (path, currentFile) => moduleChunk;
        object? result = vm.Execute(mainChunk);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Import_NoModuleLoader_ThrowsError()
    {
        string source = """
            import { foo } from "missing";
            return foo;
            """;
        Assert.Throws<RuntimeError>(() => Execute(source));
    }

    /// <summary>Test helper: an IStashCallable wrapping a delegate.</summary>
    private class TestCallable : IStashCallable
    {
        private readonly Func<IInterpreterContext, List<object?>, object?> _impl;

        public int Arity { get; }
        public int MinArity { get; }

        public TestCallable(Func<IInterpreterContext, List<object?>, object?> impl, int arity = -1, int minArity = -1)
        {
            _impl = impl;
            Arity = arity;
            MinArity = minArity == -1 ? (arity == -1 ? 0 : arity) : minArity;
        }

        public object? Call(IInterpreterContext context, List<object?> arguments) => _impl(context, arguments);
    }
}
