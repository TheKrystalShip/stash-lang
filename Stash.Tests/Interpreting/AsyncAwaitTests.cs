using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Interpreting;

public class AsyncAwaitTests
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
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        return vm.Execute(chunk);
    }

    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    private static string RunCapturingOutput(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var sw = new System.IO.StringWriter();
        vm.Output = sw;
        vm.Execute(chunk);
        return sw.ToString();
    }

    // ── Category 1: Basic async fn declaration and await ─────────────────────

    [Fact]
    public void AsyncFn_ReturnsInt_AwaitGetsValue()
    {
        var result = Run(@"
async fn compute() {
    return 42;
}
let result = await compute();
");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void AsyncFn_ReturnsString_AwaitGetsValue()
    {
        var result = Run(@"
async fn greet(name) {
    return $""hello {name}"";
}
let result = await greet(""world"");
");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void AsyncFn_ReturnsNull_AwaitGetsNull()
    {
        var result = Run(@"
async fn doNothing() {
    let x = 1;
}
let result = await doNothing();
");
        Assert.Null(result);
    }

    [Fact]
    public void AsyncFn_NoAwait_ReturnsFuture()
    {
        var result = Run(@"
async fn compute() {
    return 42;
}
let f = compute();
time.sleep(0.1);
let result = typeof(f) == ""Future"";
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void AsyncFn_WithParams_PassesArguments()
    {
        var result = Run(@"
async fn add(a, b) {
    return a + b;
}
let result = await add(10, 20);
");
        Assert.Equal(30L, result);
    }

    [Fact]
    public void AsyncFn_WithDefaultParams_UsesDefaults()
    {
        var result = Run(@"
async fn greet(name = ""world"") {
    return $""hello {name}"";
}
let result = await greet();
");
        Assert.Equal("hello world", result);
    }

    // ── Category 2: Async lambdas ─────────────────────────────────────────────

    [Fact]
    public void AsyncLambda_ExpressionBody_AwaitGetsValue()
    {
        var result = Run(@"
let compute = async (x) => x * 2;
let result = await compute(21);
");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void AsyncLambda_BlockBody_AwaitGetsValue()
    {
        var result = Run(@"
let compute = async (x) => {
    let doubled = x * 2;
    return doubled;
};
let result = await compute(21);
");
        Assert.Equal(42L, result);
    }

    // ── Category 3: Parallel execution ────────────────────────────────────────

    [Fact]
    public void AsyncFn_ParallelExecution_FasterThanSequential()
    {
        var result = Run(@"
async fn slow(val) {
    time.sleep(0.08);
    return val;
}
let start = time.millis();
let f1 = slow(1);
let f2 = slow(2);
let r1 = await f1;
let r2 = await f2;
let elapsed = time.millis() - start;
let result = elapsed < 150;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void AsyncFn_MultipleAwaits_AllResolve()
    {
        var result = Run(@"
async fn compute(n) {
    return n * n;
}
let f1 = compute(3);
let f2 = compute(4);
let f3 = compute(5);
let r1 = await f1;
let r2 = await f2;
let r3 = await f3;
let result = r1 + r2 + r3;
");
        Assert.Equal(50L, result); // 9 + 16 + 25
    }

    // ── Category 4: Error handling ────────────────────────────────────────────

    [Fact]
    public void AsyncFn_Throws_AwaitPropagatesError()
    {
        RunExpectingError(@"
async fn fail() {
    throw ""async error"";
}
let result = await fail();
");
    }

    [Fact]
    public void AsyncFn_ThrowsCaughtByTry_ReturnsError()
    {
        var result = Run(@"
async fn fail() {
    throw ""async error"";
}
let err = try await fail();
let result = err is Error;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void AsyncFn_ThrowsCaughtByTry_ErrorHasMessage()
    {
        var result = Run(@"
async fn fail() {
    throw ""async error"";
}
let err = try await fail();
let result = err.message;
");
        Assert.Equal("async error", result);
    }

    // ── Category 5: Await on non-future (transparent) ─────────────────────────

    [Fact]
    public void Await_NonFuture_ReturnsValueDirectly()
    {
        var result = Run(@"
let result = await 42;
");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Await_Null_ReturnsNull()
    {
        var result = Run(@"
let result = await null;
");
        Assert.Null(result);
    }

    [Fact]
    public void Await_String_ReturnsString()
    {
        var result = Run(@"
let result = await ""hello"";
");
        Assert.Equal("hello", result);
    }

    // ── Category 6: Await on Future from task.run() ────────────────────

    [Fact]
    public void Await_FutureFromTaskRun_ReturnsResult()
    {
        var result = Run(@"
let handle = task.run(() => 42);
let result = await handle;
");
        Assert.Equal(42L, result);
    }

    // ── Category 7: task.all, task.race, task.resolve, task.delay ─────────────

    [Fact]
    public void TaskAll_WithFutures_ReturnsAllResults()
    {
        var result = Run(@"
async fn compute(n) {
    return n * n;
}
let futures = [compute(2), compute(3), compute(4)];
let results = await task.all(futures);
let result = results[0] + results[1] + results[2];
");
        Assert.Equal(29L, result); // 4 + 9 + 16
    }

    [Fact]
    public void TaskAll_EmptyArray_ReturnsEmptyArray()
    {
        var result = Run(@"
let results = await task.all([]);
let result = len(results);
");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void TaskRace_WithFutures_ReturnsFirst()
    {
        var result = Run(@"
async fn fast() {
    return ""fast"";
}
async fn slow() {
    time.sleep(0.5);
    return ""slow"";
}
let f = task.race([slow(), fast()]);
let result = await f;
");
        Assert.Equal("fast", result);
    }

    [Fact]
    public void TaskResolve_ReturnsResolvedFuture()
    {
        var result = Run(@"
let f = task.resolve(42);
let result = await f;
");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void TaskDelay_WaitsAndResolves()
    {
        var result = Run(@"
let start = time.millis();
let f = task.delay(0.05);
await f;
let elapsed = time.millis() - start;
let result = elapsed >= 40;
");
        Assert.Equal(true, result);
    }

    // ── Category 8: Type checking ─────────────────────────────────────────────

    [Fact]
    public void IsFuture_OnFuture_ReturnsTrue()
    {
        var result = Run(@"
async fn compute() { return 1; }
let f = compute();
let result = typeof(f) == ""Future"";
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IsFuture_OnNonFuture_ReturnsFalse()
    {
        var result = Run(@"
let result = typeof(42) == ""Future"";
");
        Assert.Equal(false, result);
    }

    [Fact]
    public void TypeOf_Future_ReturnsFuture()
    {
        var result = Run(@"
async fn compute() { return 1; }
let f = compute();
let result = typeof(f);
");
        Assert.Equal("Future", result);
    }

    // ── Category 9: Async in struct methods ───────────────────────────────────

    [Fact]
    public void AsyncFn_CalledFromStruct_Works()
    {
        var result = Run(@"
async fn buildUrl(base, path) {
    return $""{base}/{path}"";
}
struct Api {
    base_url: string,
    fn fetch(path) {
        return buildUrl(self.base_url, path);
    }
}
let api = Api { base_url: ""https://api.example.com"" };
let result = await api.fetch(""users"");
");
        Assert.Equal("https://api.example.com/users", result);
    }

    // ── Category 10: Nested async/await ───────────────────────────────────────

    [Fact]
    public void AsyncFn_NestedAwait_Works()
    {
        var result = Run(@"
async fn inner() {
    return 21;
}
async fn outer() {
    let val = await inner();
    return val * 2;
}
let result = await outer();
");
        Assert.Equal(42L, result);
    }

    // ── Category 11: Closures in async functions ──────────────────────────────

    [Fact]
    public void AsyncFn_CapturesClosure_Works()
    {
        var result = Run(@"
let multiplier = 10;
async fn compute(x) {
    return x * multiplier;
}
let result = await compute(4);
");
        Assert.Equal(40L, result);
    }

    // ── Category 12: Backward compatibility - task.await still works ──────────

    [Fact]
    public void TaskAwait_StillWorks_BackwardCompatible()
    {
        var result = Run(@"
let handle = task.run(() => 99);
let result = task.await(handle);
");
        Assert.Equal(99L, result);
    }

    // ── Additional edge cases ─────────────────────────────────────────────────

    [Fact]
    public void AsyncFn_ReturnsFloat_AwaitGetsValue()
    {
        var result = Run(@"
async fn pi() {
    return 3.14;
}
let result = await pi();
");
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void AsyncFn_ReturnsBool_AwaitGetsValue()
    {
        var result = Run(@"
async fn check(x) {
    return x > 0;
}
let result = await check(5);
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void AsyncFn_ReturnsArray_AwaitGetsArray()
    {
        var result = Run(@"
async fn makeList() {
    return [1, 2, 3];
}
let arr = await makeList();
let result = arr[1];
");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void AsyncLambda_NoParams_AwaitGetsValue()
    {
        var result = Run(@"
let compute = async () => 100;
let result = await compute();
");
        Assert.Equal(100L, result);
    }

    [Fact]
    public void AsyncFn_ChainedAwaits_Work()
    {
        var result = Run(@"
async fn step1() { return 1; }
async fn step2() { return 2; }
async fn step3() { return 3; }
let a = await step1();
let b = await step2();
let c = await step3();
let result = a + b + c;
");
        Assert.Equal(6L, result);
    }

    [Fact]
    public void TaskResolve_WithString_AwaitGetsString()
    {
        var result = Run(@"
let f = task.resolve(""hello"");
let result = await f;
");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void TaskResolve_WithNull_AwaitGetsNull()
    {
        var result = Run(@"
let f = task.resolve(null);
let result = await f;
");
        Assert.Null(result);
    }

    [Fact]
    public void TaskAll_WithFuturesFromTaskRun_ReturnsAllResults()
    {
        var result = Run(@"
let h1 = task.run(() => 10);
let h2 = task.run(() => 20);
let h3 = task.run(() => 30);
let results = await task.all([h1, h2, h3]);
let result = results[0] + results[1] + results[2];
");
        Assert.Equal(60L, result);
    }

    [Fact]
    public void AsyncFn_WithStringInterpolation_Works()
    {
        var result = Run(@"
async fn format(a, b) {
    return $""{a} + {b} = {a + b}"";
}
let result = await format(3, 4);
");
        Assert.Equal("3 + 4 = 7", result);
    }

    [Fact]
    public void Await_Bool_ReturnsBool()
    {
        var result = Run(@"
let result = await true;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void AsyncFn_OutputSideEffect_Works()
    {
        var output = RunCapturingOutput(@"
async fn greet(name) {
    io.println($""hello {name}"");
}
await greet(""world"");
");
        Assert.Equal("hello world\n", output);
    }
}
