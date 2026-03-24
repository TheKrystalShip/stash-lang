using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Interpreting.Types;

namespace Stash.Tests.Interpreting;

public class TaskBuiltInsTests
{
    private static object? Run(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.Interpret(statements);

        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        return interpreter.Interpret(resultExpr);
    }

    private static string RunCapturingOutput(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        var sw = new System.IO.StringWriter();
        interpreter.Output = sw;
        interpreter.Interpret(statements);
        return sw.ToString();
    }

    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
    }

    // ── task.run + task.await ─────────────────────────────────────────────────

    [Fact]
    public void Run_SimpleFunction_ReturnsResult()
    {
        var result = Run(@"
fn compute() {
    return 42;
}
let handle = task.run(compute);
let result = task.await(handle);
");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Run_Lambda_ReturnsResult()
    {
        var result = Run(@"
let handle = task.run(() => 10 + 20);
let result = task.await(handle);
");
        Assert.Equal(30L, result);
    }

    [Fact]
    public void Run_MultipleTasksParallel_AllComplete()
    {
        var result = Run(@"
let h1 = task.run(() => 1);
let h2 = task.run(() => 2);
let h3 = task.run(() => 3);
let r1 = task.await(h1);
let r2 = task.await(h2);
let r3 = task.await(h3);
let result = r1 + r2 + r3;
");
        Assert.Equal(6L, result);
    }

    [Fact]
    public void Run_TaskThrowsError_AwaitPropagates()
    {
        RunExpectingError(@"
fn failing() {
    let x = arr.push(""not an array"", 1);
}
let handle = task.run(failing);
task.await(handle);
");
    }

    [Fact]
    public void Run_ReturnsNull_AwaitReturnsNull()
    {
        var result = Run(@"
let handle = task.run(() => null);
let result = task.await(handle);
");
        Assert.Null(result);
    }

    [Fact]
    public void Run_ReturnsString_AwaitReturnsString()
    {
        var result = Run(@"
let handle = task.run(() => ""hello"");
let result = task.await(handle);
");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Run_ReturnsBool_AwaitReturnsBool()
    {
        var result = Run(@"
let handle = task.run(() => true);
let result = task.await(handle);
");
        Assert.Equal(true, result);
    }

    // ── task.awaitAll ─────────────────────────────────────────────────────────

    [Fact]
    public void AwaitAll_ReturnsList()
    {
        var result = Run(@"
let h1 = task.run(() => 10);
let h2 = task.run(() => 20);
let h3 = task.run(() => 30);
let result = task.awaitAll([h1, h2, h3]);
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(10L, list[0]);
        Assert.Equal(20L, list[1]);
        Assert.Equal(30L, list[2]);
    }

    [Fact]
    public void AwaitAll_EmptyList_ReturnsEmpty()
    {
        var result = Run(@"let result = task.awaitAll([]);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void AwaitAll_SingleTask_ReturnsSingleElementList()
    {
        var result = Run(@"
let h = task.run(() => 99);
let result = task.awaitAll([h]);
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Single(list);
        Assert.Equal(99L, list[0]);
    }

    // ── task.awaitAny ─────────────────────────────────────────────────────────

    [Fact]
    public void AwaitAny_ReturnsFirst()
    {
        var result = Run(@"
let h1 = task.run(() => 1);
let h2 = task.run(() => 2);
let result = task.awaitAny([h1, h2]);
");
        long val = Assert.IsType<long>(result);
        Assert.True(val == 1L || val == 2L);
    }

    [Fact]
    public void AwaitAny_SingleTask_ReturnsThatResult()
    {
        var result = Run(@"
let h = task.run(() => 77);
let result = task.awaitAny([h]);
");
        Assert.Equal(77L, result);
    }

    [Fact]
    public void AwaitAny_EmptyList_Throws()
    {
        RunExpectingError(@"task.awaitAny([]);");
    }

    // ── task.status ───────────────────────────────────────────────────────────

    [Fact]
    public void Status_AfterRun_IsRunningOrCompleted()
    {
        var result = Run(@"
let handle = task.run(() => 42);
let result = task.status(handle);
");
        var status = Assert.IsType<StashEnumValue>(result);
        Assert.True(status.MemberName == "Running" || status.MemberName == "Completed");
    }

    [Fact]
    public void Status_AfterAwait_ReturnsCompleted()
    {
        // After await, the handle's status field is kept in sync and correctly
        // reflects the completed state.
        var result = Run(@"
let handle = task.run(() => 42);
task.await(handle);
let result = task.status(handle);
");
        var status = Assert.IsType<StashEnumValue>(result);
        Assert.Equal("Completed", status.MemberName);
    }

    // ── task.cancel ───────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_RunningTask_DoesNotThrow()
    {
        // Cancel is fire-and-forget; the task may already be complete by the time
        // cancel is called, which is fine — it should not throw.
        Run(@"
let handle = task.run(() => 42);
task.cancel(handle);
let result = 0;
");
    }

    [Fact]
    public void Cancel_CalledTwice_DoesNotThrow()
    {
        Run(@"
let handle = task.run(() => 42);
task.cancel(handle);
task.cancel(handle);
let result = 0;
");
    }

    // ── arr.parMap ────────────────────────────────────────────────────────────

    [Fact]
    public void ParMap_DoublesEachElement()
    {
        var result = Run(@"
let result = arr.parMap([1, 2, 3, 4], (x) => x * 2);
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(4, list.Count);
        Assert.Equal(2L, list[0]);
        Assert.Equal(4L, list[1]);
        Assert.Equal(6L, list[2]);
        Assert.Equal(8L, list[3]);
    }

    [Fact]
    public void ParMap_EmptyArray_ReturnsEmpty()
    {
        var result = Run(@"let result = arr.parMap([], (x) => x);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void ParMap_PreservesOrder()
    {
        var result = Run(@"
let result = arr.parMap([5, 4, 3, 2, 1], (x) => x * 10);
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(50L, list[0]);
        Assert.Equal(40L, list[1]);
        Assert.Equal(30L, list[2]);
        Assert.Equal(20L, list[3]);
        Assert.Equal(10L, list[4]);
    }

    [Fact]
    public void ParMap_InvalidArgs_Throws()
    {
        RunExpectingError(@"arr.parMap(""not array"", (x) => x);");
    }

    [Fact]
    public void ParMap_SingleElement_ReturnsSingleElement()
    {
        var result = Run(@"let result = arr.parMap([7], (x) => x + 1);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Single(list);
        Assert.Equal(8L, list[0]);
    }

    [Fact]
    public void ParMap_StringElements_MapsCorrectly()
    {
        var result = Run(@"let result = arr.parMap([""a"", ""b"", ""c""], (x) => str.upper(x));");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal("A", list[0]);
        Assert.Equal("B", list[1]);
        Assert.Equal("C", list[2]);
    }

    [Fact]
    public void ParMap_WithMaxConcurrency_ProducesCorrectResults()
    {
        string output = RunCapturingOutput(@"
            let nums = [1, 2, 3, 4, 5];
            let doubled = arr.parMap(nums, (x) => x * 2, 2);
            io.println(doubled);
        ");
        Assert.Equal("[2, 4, 6, 8, 10]", output.Trim());
    }

    [Fact]
    public void ParMap_WithMaxConcurrencyOne_RunsSequentially()
    {
        string output = RunCapturingOutput(@"
            let nums = [1, 2, 3];
            let result = arr.parMap(nums, (x) => x + 10, 1);
            io.println(result);
        ");
        Assert.Equal("[11, 12, 13]", output.Trim());
    }

    [Fact]
    public void ParMap_MaxConcurrencyZero_ThrowsError()
    {
        RunExpectingError(@"arr.parMap([1, 2, 3], (x) => x, 0);");
    }

    [Fact]
    public void ParMap_MaxConcurrencyNegative_ThrowsError()
    {
        RunExpectingError(@"arr.parMap([1, 2, 3], (x) => x, -5);");
    }

    [Fact]
    public void ParMap_MaxConcurrencyNonInteger_ThrowsError()
    {
        RunExpectingError(@"arr.parMap([1, 2, 3], (x) => x, ""two"");");
    }

    // ── arr.parFilter ─────────────────────────────────────────────────────────

    [Fact]
    public void ParFilter_KeepsMatchingElements()
    {
        var result = Run(@"
let result = arr.parFilter([1, 2, 3, 4, 5, 6], (x) => x > 3);
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(4L, list[0]);
        Assert.Equal(5L, list[1]);
        Assert.Equal(6L, list[2]);
    }

    [Fact]
    public void ParFilter_EmptyArray_ReturnsEmpty()
    {
        var result = Run(@"let result = arr.parFilter([], (x) => true);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void ParFilter_NoMatch_ReturnsEmpty()
    {
        var result = Run(@"let result = arr.parFilter([1, 2, 3], (x) => x > 100);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void ParFilter_AllMatch_ReturnsAll()
    {
        var result = Run(@"let result = arr.parFilter([1, 2, 3], (x) => x > 0);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(2L, list[1]);
        Assert.Equal(3L, list[2]);
    }

    [Fact]
    public void ParFilter_InvalidArgs_Throws()
    {
        RunExpectingError(@"arr.parFilter(""not array"", (x) => true);");
    }

    [Fact]
    public void ParFilter_WithMaxConcurrency_FiltersCorrectly()
    {
        string output = RunCapturingOutput(@"
            let nums = [1, 2, 3, 4, 5, 6];
            let evens = arr.parFilter(nums, (x) => x % 2 == 0, 2);
            io.println(evens);
        ");
        Assert.Equal("[2, 4, 6]", output.Trim());
    }

    // ── arr.parForEach ────────────────────────────────────────────────────────

    [Fact]
    public void ParForEach_ExecutesForEachElement()
    {
        // parForEach returns null; just verify it doesn't crash
        var result = Run(@"
arr.parForEach([1, 2, 3], (x) => x * 2);
let result = true;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void ParForEach_EmptyArray_NoOp()
    {
        var result = Run(@"
arr.parForEach([], (x) => x);
let result = true;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void ParForEach_InvalidArgs_Throws()
    {
        RunExpectingError(@"arr.parForEach(""not array"", (x) => x);");
    }

    [Fact]
    public void ParForEach_WithMaxConcurrency_ExecutesAll()
    {
        string output = RunCapturingOutput(@"
            let nums = [1, 2, 3];
            arr.parForEach(nums, (x) => {
                let y = x * 2;
            }, 1);
            io.println(""done"");
        ");
        Assert.Equal("done", output.Trim());
    }

    // ── Isolation tests ───────────────────────────────────────────────────────

    [Fact]
    public void ParMap_TaskIsolation_NoSharedMutation()
    {
        // Each parallel task should have its own isolated environment.
        // Local variables in one task should not affect others.
        var result = Run(@"
let result = arr.parMap([1, 2, 3], (x) => {
    let local = x * 10;
    return local;
});
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(10L, list[0]);
        Assert.Equal(20L, list[1]);
        Assert.Equal(30L, list[2]);
    }

    [Fact]
    public void ParFilter_TaskIsolation_NoSharedMutation()
    {
        var result = Run(@"
let result = arr.parFilter([1, 2, 3, 4, 5], (x) => {
    let local = x * 2;
    return local > 4;
});
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(3L, list[0]);
        Assert.Equal(4L, list[1]);
        Assert.Equal(5L, list[2]);
    }

    [Fact]
    public void ParMap_LargeArray_PreservesOrder()
    {
        // Runs 20 tasks in parallel and verifies result ordering is maintained
        var result = Run(@"
let nums = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20];
let result = arr.parMap(nums, (x) => x * x);
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(20, list.Count);
        for (int i = 0; i < 20; i++)
        {
            long expected = (long)(i + 1) * (i + 1);
            Assert.Equal(expected, list[i]);
        }
    }
}
