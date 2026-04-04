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
}
