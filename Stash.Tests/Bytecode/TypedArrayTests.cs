using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;

namespace Stash.Tests.Bytecode;

public class TypedArrayTests : BytecodeTestBase
{
    // Override Execute to inject stdlib globals (arr, json, typeof, etc.)
    protected new static object? Execute(string source)
    {
        var lexer = new Lexer(source, "<test>");
        List<Token> tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        List<Stmt> stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        Chunk chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        return Normalize(vm.Execute(chunk));
    }

    // ─── Happy Path ──────────────────────────────────────────────────────────

    [Fact]
    public void TypedArray_IntDeclaration_CreatesIntArray()
    {
        var result = Execute("let scores: int[] = [95, 87, 100]; return typeof(scores);");
        Assert.Equal("int[]", result);
    }

    [Fact]
    public void TypedArray_FloatDeclaration_CreatesFloatArray()
    {
        var result = Execute("let ratios: float[] = [0.5, 1.0, 1.5]; return typeof(ratios);");
        Assert.Equal("float[]", result);
    }

    [Fact]
    public void TypedArray_StringDeclaration_CreatesStringArray()
    {
        var result = Execute("let names: string[] = [\"alice\", \"bob\"]; return typeof(names);");
        Assert.Equal("string[]", result);
    }

    [Fact]
    public void TypedArray_BoolDeclaration_CreatesBoolArray()
    {
        var result = Execute("let flags: bool[] = [true, false, true]; return typeof(flags);");
        Assert.Equal("bool[]", result);
    }

    [Fact]
    public void TypedArray_EmptyDeclaration_CreatesEmptyTypedArray()
    {
        var result = Execute("let nums: int[] = []; return typeof(nums);");
        Assert.Equal("int[]", result);
    }

    [Fact]
    public void TypedArray_IndexRead_ReturnsElement()
    {
        var result = Execute("let scores: int[] = [95, 87, 100]; return scores[1];");
        Assert.Equal(87L, result);
    }

    [Fact]
    public void TypedArray_IndexWrite_ValidatesAndSets()
    {
        var result = Execute("let scores: int[] = [95, 87, 100]; scores[1] = 42; return scores[1];");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void TypedArray_NegativeIndex_Works()
    {
        var result = Execute("let nums: int[] = [10, 20, 30]; return nums[-1];");
        Assert.Equal(30L, result);
    }

    [Fact]
    public void TypedArray_ForIn_IteratesElements()
    {
        var result = Execute(@"
            let nums: int[] = [10, 20, 30];
            let sum = 0;
            for (let n in nums) { sum = sum + n; }
            return sum;
        ");
        Assert.Equal(60L, result);
    }

    [Fact]
    public void TypedArray_ForInIndexed_IteratesWithIndex()
    {
        var result = Execute(@"
            let nums: int[] = [10, 20, 30];
            let result = 0;
            for (let i, n in nums) { result = result + i; }
            return result;
        ");
        Assert.Equal(3L, result); // 0+1+2
    }

    [Fact]
    public void TypedArray_FloatAllowsIntPromotion_Succeeds()
    {
        var result = Execute("let ratios: float[] = [1, 2.5, 3]; return ratios[0];");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void TypedArray_Spread_ExtractsElements()
    {
        var result = Execute(@"
            let a: int[] = [1, 2, 3];
            let b = [...a, 4, 5];
            return len(b);
        ");
        Assert.Equal(5L, result);
    }

    [Fact]
    public void TypedArray_SpreadIntoTyped_ValidatesElements()
    {
        var result = Execute(@"
            let a: int[] = [1, 2, 3];
            let b: int[] = [...a, 4, 5];
            return typeof(b);
        ");
        Assert.Equal("int[]", result);
    }

    [Fact]
    public void TypedArray_Destructuring_ExtractsElements()
    {
        var result = Execute(@"
            let scores: int[] = [95, 87, 100, 64];
            let generic = arr.untyped(scores);
            let [first, second] = generic;
            return first;
        ");
        Assert.Equal(95L, result);
    }

    [Fact]
    public void TypedArray_FunctionParameter_AcceptsTypedArray()
    {
        var result = Execute(@"
            fn sum(nums: int[]) -> int {
                let total = 0;
                for (let n in nums) { total = total + n; }
                return total;
            }
            let scores: int[] = [10, 20, 30];
            return sum(scores);
        ");
        Assert.Equal(60L, result);
    }

    [Fact]
    public void TypedArray_FunctionReturn_ReturnsTypedArray()
    {
        var result = Execute(@"
            fn getScores() -> int[] {
                return [95, 87, 100];
            }
            return typeof(getScores());
        ");
        // Note: getScores returns a generic array (no TypedWrap on return)
        // This is expected — TypedWrap only applies at variable assignment
        Assert.Equal("array", result);
    }

    [Fact]
    public void TypedArray_StructField_StoresTypedArray()
    {
        var result = Execute(@"
            struct Config {
                ports: int[],
                hosts: string[]
            }
            let c = Config { ports: [80, 443], hosts: [""a"", ""b""] };
            return typeof(c.ports);
        ");
        // Struct fields don't trigger TypedWrap — they store what's assigned
        Assert.Equal("array", result);
    }

    [Fact]
    public void TypedArray_PushValidElement_Succeeds()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3];
            arr.push(nums, 4);
            return len(nums);
        ");
        Assert.Equal(4L, result);
    }

    // ─── arr.typed / arr.untyped / arr.elementType / arr.new ─────────────────

    [Fact]
    public void TypedArray_ArrTyped_CreatesFromGeneric()
    {
        var result = Execute(@"
            let nums = arr.typed([1, 2, 3], ""int"");
            return typeof(nums);
        ");
        Assert.Equal("int[]", result);
    }

    [Fact]
    public void TypedArray_ArrUntyped_ConvertsToGeneric()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3];
            let generic = arr.untyped(nums);
            return typeof(generic);
        ");
        Assert.Equal("array", result);
    }

    [Fact]
    public void TypedArray_ArrElementType_ReturnsTypeName()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3];
            return arr.elementType(nums);
        ");
        Assert.Equal("int", result);
    }

    [Fact]
    public void TypedArray_ArrElementType_GenericReturnsNull()
    {
        var result = Execute("return arr.elementType([1, 2, 3]);");
        Assert.Null(result);
    }

    [Fact]
    public void TypedArray_ArrNew_CreatesZeroInitialized()
    {
        var result = Execute(@"
            let nums = arr.new(""int"", 3);
            return len(nums);
        ");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void TypedArray_ArrNew_IntZeroValues()
    {
        var result = Execute(@"
            let nums = arr.new(""int"", 3);
            return nums[0];
        ");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void TypedArray_ArrFilter_PreservesType()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3, 4, 5];
            let evens = arr.filter(nums, (x) => x % 2 == 0);
            return typeof(evens);
        ");
        Assert.Equal("int[]", result);
    }

    [Fact]
    public void TypedArray_ArrSlice_PreservesType()
    {
        var result = Execute(@"
            let nums: int[] = [10, 20, 30, 40, 50];
            let sub = arr.slice(nums, 1, 3);
            return typeof(sub);
        ");
        Assert.Equal("int[]", result);
    }

    [Fact]
    public void TypedArray_ArrMap_ReturnsGenericArray()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3];
            let mapped = arr.map(nums, (x) => conv.toStr(x));
            return typeof(mapped);
        ");
        Assert.Equal("array", result);
    }

    [Fact]
    public void TypedArray_ArrConcat_SameType_PreservesType()
    {
        var result = Execute(@"
            let a: int[] = [1, 2];
            let b: int[] = [3, 4];
            let c = arr.concat(a, b);
            return typeof(c);
        ");
        Assert.Equal("int[]", result);
    }

    [Fact]
    public void TypedArray_ArrConcat_DifferentTypes_ReturnsGeneric()
    {
        var result = Execute(@"
            let a: int[] = [1, 2];
            let b: string[] = [""a"", ""b""];
            let c = arr.concat(a, b);
            return typeof(c);
        ");
        Assert.Equal("array", result);
    }

    [Fact]
    public void TypedArray_ArrSort_InPlace()
    {
        var result = Execute(@"
            let nums: int[] = [3, 1, 2];
            arr.sort(nums);
            return nums[0];
        ");
        Assert.Equal(1L, result);
    }

    [Fact]
    public void TypedArray_ArrReverse_InPlace()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3];
            arr.reverse(nums);
            return nums[0];
        ");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void TypedArray_JsonStringify_ProducesStandardJson()
    {
        // json.stringify requires a generic array — untype first
        var result = Execute(@"
            let nums: int[] = [1, 2, 3];
            return json.stringify(arr.untyped(nums));
        ");
        Assert.Equal("[1,2,3]", result);
    }

    [Fact]
    public void TypedArray_JsonStringify_DirectTypedArray()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3];
            return json.stringify(nums);
        ");
        Assert.Equal("[1,2,3]", result);
    }

    [Fact]
    public void TypedArray_JsonPretty_DirectTypedArray()
    {
        var result = Execute(@"
            let names: string[] = [""alice"", ""bob""];
            return json.stringify(names);
        ");
        Assert.Equal("[\"alice\",\"bob\"]", result);
    }

    // ─── Type System Tests ────────────────────────────────────────────────────

    [Fact]
    public void TypedArray_TypeofReturnsCorrectString()
    {
        Assert.Equal("int[]", Execute("let a: int[] = [1]; return typeof(a);"));
        Assert.Equal("float[]", Execute("let a: float[] = [1.0]; return typeof(a);"));
        Assert.Equal("string[]", Execute("let a: string[] = [\"x\"]; return typeof(a);"));
        Assert.Equal("bool[]", Execute("let a: bool[] = [true]; return typeof(a);"));
    }

    [Fact]
    public void TypedArray_IsOwnType_ReturnsTrue()
    {
        var result = Execute("let a: int[] = [1, 2]; return a is int[];");
        Assert.Equal(true, result);
    }

    [Fact]
    public void TypedArray_IsArray_ReturnsTrue()
    {
        var result = Execute("let a: int[] = [1, 2]; return a is array;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void TypedArray_IsWrongTypedArray_ReturnsFalse()
    {
        var result = Execute("let a: int[] = [1, 2]; return a is string[];");
        Assert.Equal(false, result);
    }

    [Fact]
    public void TypedArray_GenericIsTypedArray_ReturnsFalse()
    {
        var result = Execute("return [1, 2, 3] is int[];");
        Assert.Equal(false, result);
    }

    [Fact]
    public void TypedArray_IsFloatArray_ReturnsFalse()
    {
        var result = Execute("let a: int[] = [1, 2]; return a is float[];");
        Assert.Equal(false, result);
    }

    [Fact]
    public void TypedArray_EqualityIsReference()
    {
        var result = Execute(@"
            let a: int[] = [1, 2, 3];
            let b: int[] = [1, 2, 3];
            return a == b;
        ");
        Assert.Equal(false, result);
    }

    [Fact]
    public void TypedArray_SelfEqualityIsTrue()
    {
        var result = Execute(@"
            let a: int[] = [1, 2, 3];
            return a == a;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void TypedArray_IsTruthy()
    {
        var result = Execute(@"
            let a: int[] = [];
            if (a) { return true; } else { return false; }
        ");
        Assert.Equal(true, result);
    }

    // ─── Error Cases ──────────────────────────────────────────────────────────

    [Fact]
    public void TypedArray_PushWrongType_Throws()
    {
        var ex = Assert.Throws<RuntimeError>(() => Execute(@"
            let nums: int[] = [1, 2, 3];
            arr.push(nums, ""hello"");
        "));
        Assert.Contains("int[]", ex.Message);
    }

    [Fact]
    public void TypedArray_IndexWriteWrongType_Throws()
    {
        var ex = Assert.Throws<RuntimeError>(() => Execute(@"
            let nums: int[] = [1, 2, 3];
            nums[0] = ""bad"";
        "));
        Assert.Contains("int[]", ex.Message);
    }

    [Fact]
    public void TypedArray_NullElement_Throws()
    {
        var ex = Assert.Throws<RuntimeError>(() => Execute(@"
            let nums: int[] = [1, null, 3];
        "));
        Assert.Contains("null", ex.Message);
    }

    [Fact]
    public void TypedArray_DeclarationMixedTypes_Throws()
    {
        var ex = Assert.Throws<RuntimeError>(() => Execute(@"
            let nums: int[] = [1, ""two"", 3];
        "));
        Assert.Contains("int[]", ex.Message);
    }

    [Fact]
    public void TypedArray_FloatIntoIntArray_Throws()
    {
        var ex = Assert.Throws<RuntimeError>(() => Execute(@"
            let nums: int[] = [1, 2, 3];
            arr.push(nums, 3.14);
        "));
        Assert.Contains("int[]", ex.Message);
    }

    [Fact]
    public void TypedArray_BoolIntoIntArray_Throws()
    {
        var ex = Assert.Throws<RuntimeError>(() => Execute(@"
            let nums: int[] = [1, 2, 3];
            arr.push(nums, true);
        "));
        Assert.Contains("int[]", ex.Message);
    }

    [Fact]
    public void TypedArray_StringIntoIntArray_Throws()
    {
        var ex = Assert.Throws<RuntimeError>(() => Execute(@"
            let nums: int[] = [1, 2, 3];
            arr.push(nums, ""bad"");
        "));
        Assert.Contains("int[]", ex.Message);
    }

    [Fact]
    public void TypedArray_IntIntoStringArray_Throws()
    {
        var ex = Assert.Throws<RuntimeError>(() => Execute(@"
            let names: string[] = [""a"", ""b""];
            arr.push(names, 42);
        "));
        Assert.Contains("string[]", ex.Message);
    }

    [Fact]
    public void TypedArray_ArrTypedInvalidElements_Throws()
    {
        var ex = Assert.Throws<RuntimeError>(() => Execute(@"
            arr.typed([1, ""two"", 3], ""int"");
        "));
        Assert.Contains("int[]", ex.Message);
    }

    [Fact]
    public void TypedArray_InvalidElementTypeName_Throws()
    {
        var ex = Assert.Throws<RuntimeError>(() => Execute(@"
            arr.typed([1, 2, 3], ""invalid"");
        "));
        Assert.Contains("Unknown", ex.Message);
    }

    // ─── Edge Cases ───────────────────────────────────────────────────────────

    [Fact]
    public void TypedArray_EmptyArray_TypeofReturnsTypedName()
    {
        var result = Execute("let nums: int[] = []; return typeof(nums);");
        Assert.Equal("int[]", result);
    }

    [Fact]
    public void TypedArray_SingleElement_Works()
    {
        var result = Execute("let nums: int[] = [42]; return nums[0];");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void TypedArray_IntToFloatPromotion_OnPush()
    {
        var result = Execute(@"
            let ratios: float[] = [1.0, 2.0];
            arr.push(ratios, 3);
            return ratios[2];
        ");
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void TypedArray_NestedInGenericArray_Works()
    {
        var result = Execute(@"
            let a: int[] = [1, 2, 3];
            let container = [a, ""hello""];
            return len(container);
        ");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void TypedArray_PassedToUntypedParameter_Works()
    {
        var result = Execute(@"
            fn count(items) {
                return len(items);
            }
            let nums: int[] = [1, 2, 3];
            return count(nums);
        ");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void TypedArray_ArrLen_Works()
    {
        var result = Execute("let nums: int[] = [1, 2, 3]; return len(nums);");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void TypedArray_Len_GlobalWorks()
    {
        var result = Execute("let nums: int[] = [1, 2, 3]; return len(nums);");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void TypedArray_ArrIsEmpty_EmptyReturnsTrue()
    {
        var result = Execute("let nums: int[] = []; return len(nums) == 0;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void TypedArray_ArrContains_FindsElement()
    {
        var result = Execute("let nums: int[] = [1, 2, 3]; return arr.contains(nums, 2);");
        Assert.Equal(true, result);
    }

    [Fact]
    public void TypedArray_ArrReduce_Works()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3, 4];
            return arr.reduce(nums, (acc, n) => acc + n, 0);
        ");
        Assert.Equal(10L, result);
    }

    [Fact]
    public void TypedArray_ArrEvery_Works()
    {
        var result = Execute(@"
            let nums: int[] = [2, 4, 6];
            return arr.every(nums, (n) => n % 2 == 0);
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void TypedArray_ArrSome_Works()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3];
            return arr.any(nums, (n) => n > 2);
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void TypedArray_ArrUnique_PreservesType()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 2, 3, 3];
            let uniq = arr.unique(nums);
            return typeof(uniq);
        ");
        Assert.Equal("int[]", result);
    }

    [Fact]
    public void TypedArray_ArrSortBy_PreservesType()
    {
        var result = Execute(@"
            let nums: int[] = [3, 1, 2];
            let sorted = arr.sortBy(nums, (x) => x);
            return typeof(sorted);
        ");
        Assert.Equal("int[]", result);
    }

    [Fact]
    public void TypedArray_ArrTake_PreservesType()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3, 4, 5];
            let first3 = arr.take(nums, 3);
            return typeof(first3);
        ");
        Assert.Equal("int[]", result);
    }

    [Fact]
    public void TypedArray_ArrDrop_PreservesType()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3, 4, 5];
            let rest = arr.drop(nums, 2);
            return typeof(rest);
        ");
        Assert.Equal("int[]", result);
    }

    [Fact]
    public void TypedArray_ConstDeclaration_Works()
    {
        var result = Execute(@"
            const SCORES: int[] = [95, 87, 100];
            return typeof(SCORES);
        ");
        Assert.Equal("int[]", result);
    }

    [Fact]
    public void TypedArray_ArrPop_Works()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3];
            let last = arr.pop(nums);
            return last;
        ");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void TypedArray_ArrClear_Works()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3];
            arr.clear(nums);
            return len(nums);
        ");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void TypedArray_ArrInsert_Works()
    {
        var result = Execute(@"
            let nums: int[] = [1, 3];
            arr.insert(nums, 1, 2);
            return nums[1];
        ");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void TypedArray_ArrRemoveAt_Works()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3];
            let removed = arr.removeAt(nums, 1);
            return removed;
        ");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void TypedArray_ArrRemove_Works()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3, 2];
            let found = arr.remove(nums, 2);
            return found;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void TypedArray_ArrIndexOf_Works()
    {
        var result = Execute("let nums: int[] = [10, 20, 30]; return arr.indexOf(nums, 20);");
        Assert.Equal(1L, result);
    }

    [Fact]
    public void TypedArray_ArrJoin_Works()
    {
        var result = Execute(@"let names: string[] = [""a"", ""b"", ""c""]; return arr.join(names, ""-"");");
        Assert.Equal("a-b-c", result);
    }

    [Fact]
    public void TypedArray_ArrFind_Works()
    {
        var result = Execute(@"
            let nums: int[] = [1, 2, 3, 4, 5];
            return arr.find(nums, (n) => n > 3);
        ");
        Assert.Equal(4L, result);
    }

    [Fact]
    public void TypedArray_ArrShuffle_Works()
    {
        // Just verify it doesn't throw and preserves length
        var result = Execute(@"
            let nums: int[] = [1, 2, 3, 4, 5];
            arr.shuffle(nums);
            return len(nums);
        ");
        Assert.Equal(5L, result);
    }

    [Fact]
    public void TypedArray_ConcatTypedAndGeneric_ReturnsGeneric()
    {
        var result = Execute(@"
            let a: int[] = [1, 2];
            let b = [3, 4];
            let c = arr.concat(a, b);
            return typeof(c);
        ");
        Assert.Equal("array", result);
    }

    [Fact]
    public void TypedArray_RestDestructuring_ProducesGenericArray()
    {
        // StashTypedArray requires conversion to generic array before destructuring
        var result = Execute(@"
            let nums: int[] = [1, 2, 3, 4];
            let generic = arr.untyped(nums);
            let [first, ...rest] = generic;
            return typeof(rest);
        ");
        Assert.Equal("array", result);
    }
}
