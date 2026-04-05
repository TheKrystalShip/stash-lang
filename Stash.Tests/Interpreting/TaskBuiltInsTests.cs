using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class TaskBuiltInsTests
{
    private static object? Run(string source)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        return vm.Execute(chunk);
    }

    private static string RunCapturingOutput(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        var sw = new System.IO.StringWriter();
        vm.Output = sw;
        vm.Execute(chunk);
        return sw.ToString();
    }

    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    private static object? RunWithFile(string source, string filePath)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, filePath);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        return vm.Execute(chunk);
    }

    private static string RunWithFileCapturingOutput(string source, string filePath)
    {
        var lexer = new Lexer(source, filePath);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        var sw = new System.IO.StringWriter();
        vm.Output = sw;
        vm.Execute(chunk);
        return sw.ToString();
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

    // ── Regression: #7 — Synchronized output across parallel tasks ────────────

    [Fact]
    public void Fork_ParallelOutput_NoInterleaving()
    {
        // Multiple parallel tasks writing lines should produce complete lines without corruption.
        // Each task writes a distinct marker line. We verify all lines appear intact.
        string output = RunCapturingOutput(@"
let tasks = [];
for (let n in 0..10) {
    arr.push(tasks, task.run(() => {
        io.println(""TASK_"" + conv.toStr(n) + ""_LINE"");
    }));
}
task.awaitAll(tasks);
");
        string[] lines = output.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(10, lines.Length);
        for (int i = 0; i < 10; i++)
        {
            Assert.Contains($"TASK_{i}_LINE", output);
        }
        // Verify no line contains a fragment of another line (interleaving check)
        foreach (string line in lines)
        {
            Assert.Matches(@"^TASK_\d+_LINE$", line.Trim());
        }
    }

    [Fact]
    public void Fork_ParallelOutput_HighConcurrency_NoExceptions()
    {
        // Stress test: 20 parallel tasks all writing output simultaneously.
        // Before the fix, this could corrupt the StringWriter's StringBuilder.
        string output = RunCapturingOutput(@"
let tasks = [];
for (let n in 0..20) {
    arr.push(tasks, task.run(() => {
        io.println(""output_"" + conv.toStr(n));
    }));
}
task.awaitAll(tasks);
");
        string[] lines = output.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(20, lines.Length);
    }

    [Fact]
    public void ParForEach_CapturedOutput_AllLinesPresent()
    {
        // arr.parForEach also uses Fork — verify output synchronization works there too.
        string output = RunCapturingOutput(@"
arr.parForEach([1, 2, 3, 4, 5], (x) => {
    io.println(""item_"" + conv.toStr(x));
});
");
        string[] lines = output.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);
        for (int i = 1; i <= 5; i++)
        {
            Assert.Contains($"item_{i}", output);
        }
    }

    // ── Regression: #8 — Thread-safe globals across parallel tasks ────────────

    [Fact]
    public void ParallelTasks_ReadGlobals_NoCrash()
    {
        // Multiple parallel tasks reading a shared global variable concurrently.
        // Before the fix (Dictionary), concurrent reads during resize could crash.
        var result = Run(@"
let shared = 42;
let result = arr.parMap([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], (x) => {
    return x + shared;
});
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(10, list.Count);
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal((long)(i + 1 + 42), list[i]);
        }
    }

    [Fact]
    public void ParallelTasks_ReadGlobalFunction_NoCrash()
    {
        // Parallel tasks calling a global function — the function binding lives in global scope.
        var result = Run(@"
fn double(n) { return n * 2; }
let result = arr.parMap([1, 2, 3, 4, 5], (x) => {
    return double(x);
});
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(5, list.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal((long)(i + 1) * 2, list[i]);
        }
    }

    [Fact]
    public void ParallelTasks_ReadGlobalConstants_NoCrash()
    {
        // Parallel tasks reading global constants — constants are stored in the global
        // scope's ConcurrentDictionary. Concurrent reads must not corrupt state.
        var result = Run(@"
const MULTIPLIER = 10;
const OFFSET = 5;
let result = arr.parMap([1, 2, 3, 4, 5, 6, 7, 8], (x) => {
    return x * MULTIPLIER + OFFSET;
});
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(8, list.Count);
        for (int i = 0; i < 8; i++)
        {
            long expected = (long)(i + 1) * 10 + 5;
            Assert.Equal(expected, list[i]);
        }
    }

    // ── Regression: #9 — Module loading during parallel execution ─────────────

    [Fact]
    public void ParallelTasks_ImportSameModule_ExecutesOnce()
    {
        // Two parallel tasks import the same module. The module increments a counter
        // file-side-effect style. With the fix, the module should only execute once
        // (double-checked locking), so both tasks get the same environment.
        string tmpDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "stash_test_" + System.Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "shared.stash");
            System.IO.File.WriteAllText(modulePath, @"
fn getValue() { return 100; }
");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = @"
import { getValue } from ""shared.stash"";
let t1 = task.run(() => {
    import { getValue } from ""shared.stash"";
    return getValue();
});
let t2 = task.run(() => {
    import { getValue } from ""shared.stash"";
    return getValue();
});
let r1 = task.await(t1);
let r2 = task.await(t2);
let result = r1 + r2;
";
            Assert.Equal(200L, RunWithFile(source, mainPath));
        }
        finally
        {
            System.IO.Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ParallelTasks_ImportSameModule_BothGetSameBindings()
    {
        // Both parallel tasks should resolve the same function from the shared module cache.
        string tmpDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "stash_test_" + System.Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "math_utils.stash");
            System.IO.File.WriteAllText(modulePath, @"
fn add(a, b) { return a + b; }
fn mul(a, b) { return a * b; }
");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = @"
let t1 = task.run(() => {
    import { add } from ""math_utils.stash"";
    return add(10, 20);
});
let t2 = task.run(() => {
    import { mul } from ""math_utils.stash"";
    return mul(3, 7);
});
let r1 = task.await(t1);
let r2 = task.await(t2);
let result = r1 + r2;
";
            Assert.Equal(51L, RunWithFile(source, mainPath));
        }
        finally
        {
            System.IO.Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ParallelTasks_ImportDifferentModules_BothWork()
    {
        // Parallel tasks importing different modules should not interfere with each other.
        string tmpDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "stash_test_" + System.Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tmpDir, "mod_a.stash"),
                "fn getA() { return 111; }");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tmpDir, "mod_b.stash"),
                "fn getB() { return 222; }");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = @"
let t1 = task.run(() => {
    import { getA } from ""mod_a.stash"";
    return getA();
});
let t2 = task.run(() => {
    import { getB } from ""mod_b.stash"";
    return getB();
});
let r1 = task.await(t1);
let r2 = task.await(t2);
let result = r1 + r2;
";
            Assert.Equal(333L, RunWithFile(source, mainPath));
        }
        finally
        {
            System.IO.Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ParallelTasks_ModuleWithSideEffect_ExecutedOnce()
    {
        // The module writes a line via io.println when loaded. If the module is executed
        // only once (due to the shared cache + locking), we should see exactly one line.
        string tmpDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "stash_test_" + System.Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "init.stash");
            System.IO.File.WriteAllText(modulePath, @"
io.println(""MODULE_LOADED"");
fn getData() { return 42; }
");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = @"
import { getData } from ""init.stash"";
let t1 = task.run(() => {
    import { getData } from ""init.stash"";
    return getData();
});
let t2 = task.run(() => {
    import { getData } from ""init.stash"";
    return getData();
});
task.await(t1);
task.await(t2);
";
            string output = RunWithFileCapturingOutput(source, mainPath);
            string[] lines = output.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
            // Module should be loaded exactly once — one MODULE_LOADED line
            int loadCount = lines.Count(l => l.Trim() == "MODULE_LOADED");
            Assert.Equal(1, loadCount);
        }
        finally
        {
            System.IO.Directory.Delete(tmpDir, true);
        }
    }
}
