# Test Infrastructure — Base Class Extraction and Deduplication

**Status:** Backlog — Design
**Created:** 2026-04-06
**Scope:** `Stash.Tests/` project only — no runtime changes

## 1. Problem Statement

The `Stash.Tests` project (~46 test files, ~2,000 tests) suffers from pervasive copy-paste duplication of test infrastructure code. The same lexer→parser→resolver→compiler→VM pipeline is manually reconstructed in **25+ files** across `Interpreting/` and `Bytecode/`. The `Analysis/` directory has a parallel problem with its own analysis pipeline duplicated across **20 files**.

This makes:

- **Refactoring the pipeline nearly impossible** — any change to the compilation pipeline (e.g., adding a new resolver pass, changing VM initialization) requires updating every file individually.
- **Adding new test helpers expensive** — output capture, error capture, file-path overrides, and argument injection are each re-implemented from scratch wherever needed.
- **Consistency fragile** — subtle divergences between "identical" helpers are invisible and easy to introduce.

## 2. Duplication Inventory

### 2.1 Interpreting Tests (25+ files) — Bytecode Execution Pipeline

**Core pattern** (duplicated verbatim in 18+ files):

```csharp
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
```

**Variant helpers** (each independently re-implemented in 2-5 files):

| Helper                                        | Files                                                                    | What it adds                                                         |
| --------------------------------------------- | ------------------------------------------------------------------------ | -------------------------------------------------------------------- |
| `Eval(string source)`                         | InterpreterTests, BitwiseOperatorTests                                   | Expression evaluation via `parser.Parse()` + `CompileExpression()`   |
| `RunStatements(string source)`                | TestBuiltInsTests, FsBuiltInsTests                                       | Statement execution, no return value                                 |
| `RunCapturingError(string source)`            | SftpBuiltInsTests, SshBuiltInsTests                                      | Returns the `RuntimeError` instead of just asserting                 |
| `RunCapturingOutput(string source)`           | IoBuiltInsTests, RetryExprTests, TaskBuiltInsTests, CliExecutionTests    | Redirects `vm.Output` to `StringWriter`                              |
| `CaptureStderr(string source)`                | IoBuiltInsTests, TaskBuiltInsTests                                       | Redirects `vm.ErrorOutput`                                           |
| `RunWithArgs(string source, string[] args)`   | DictArgParseTests, CliExecutionTests, ArgsNamespaceTests, ArgsBuildTests | Sets `vm.ScriptArgs`                                                 |
| `RunWithFile(string source, string filePath)` | TaskBuiltInsTests, PkgBuiltInsTests, TestDiscoveryTests, TestFilterTests | Passes `filePath` to Lexer instead of `"<test>"`                     |
| `RunWithHarness(string source)`               | TestBuiltInsTests, TestDiscoveryTests, TestFilterTests                   | Sets `vm.TestHarness` to `TapReporter`, returns `(reporter, output)` |

### 2.2 Analysis Tests (20 files) — Analysis Pipeline

**Core pattern** (duplicated with minor variations across 15+ files):

```csharp
private static AnalysisResult Analyze(string source)
{
    var lexer = new Lexer(source, "<test>");
    var tokens = lexer.ScanTokens();
    var parser = new Parser(tokens);
    var stmts = parser.ParseProgram();
    var collector = new SymbolCollector();
    var tree = collector.Collect(stmts);
    // ... optional validator, resolver, inference steps
}
```

Variants: `Validate()`, `FullAnalyze()`, `AnalyzeWithDocs()`, `AnalyzeWithStatements()`, `AnalyzeWithInference()` — each adding different analysis passes.

### 2.3 Bytecode Tests (3 files)

`CompilerTests.cs` and `VirtualMachineTests.cs` both define their own `CompileSource()`. `VMDebugTests.cs` adds `ExecuteWithDebugger()`.

### 2.4 IDisposable Temp-Directory Pattern (3+ files)

`FsBuiltInsTests`, `FsWatchBuiltInsTests`, and `CliPackageCommandsTests` each independently implement the same temp-directory setup/teardown:

```csharp
private readonly string _testDir;
public ClassName()
{
    _testDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(_testDir);
}
public void Dispose()
{
    try { Directory.Delete(_testDir, true); } catch { }
}
```

### 2.5 Registry Tests — DB Context Setup (5+ files)

Multiple registry test files independently create in-memory SQLite `RegistryDbContext` instances with identical `DbContextOptionsBuilder` boilerplate plus `CreateTestDb()` factory methods.

## 3. Proposed Design

### 3.1 Execution Test Base Class: `StashTestBase`

**Location:** `Stash.Tests/Interpreting/StashTestBase.cs`

A `protected static` utility base class that every execution-oriented test class inherits from. All helpers are `protected static` so they behave identically to the current `private static` methods — no instance state required for the common case.

```csharp
namespace Stash.Tests.Interpreting;

public abstract class StashTestBase
{
    // ── Core pipeline ────────────────────────────────────────────

    /// Compile and execute statements. Appends "return result;" automatically.
    protected static object? Run(string source, string sourceName = "<test>")
    {
        string full = source + "\nreturn result;";
        var vm = CompileAndCreateVM(full, sourceName);
        return vm.Execute(vm.Chunk);
    }

    /// Compile and execute statements without returning a value.
    protected static void RunStatements(string source, string sourceName = "<test>")
    {
        var vm = CompileAndCreateVM(source, sourceName);
        vm.Execute(vm.Chunk);
    }

    /// Evaluate a single expression (no statements, no "return result;").
    protected static object? Eval(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var expr = parser.Parse();
        var chunk = Compiler.CompileExpression(expr);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        return vm.Execute(chunk);
    }

    // ── Error assertion ──────────────────────────────────────────

    /// Assert that execution throws RuntimeError.
    protected static void RunExpectingError(string source)
    {
        var (chunk, globals) = Compile(source);
        var vm = new VirtualMachine(globals);
        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    /// Execute and return the RuntimeError (for message inspection).
    protected static RuntimeError RunCapturingError(string source)
    {
        var (chunk, globals) = Compile(source);
        var vm = new VirtualMachine(globals);
        return Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    // ── Output capture ───────────────────────────────────────────

    /// Execute and capture stdout.
    protected static (object? Result, string Output) RunCapturingOutput(string source)
    {
        string full = source + "\nreturn result;";
        var (chunk, globals) = CompileRaw(full);
        var vm = new VirtualMachine(globals);
        var sw = new StringWriter();
        vm.Output = sw;
        object? result = vm.Execute(chunk);
        return (result, sw.ToString());
    }

    /// Execute and capture stderr.
    protected static string RunCapturingStderr(string source)
    {
        var (chunk, globals) = Compile(source);
        var vm = new VirtualMachine(globals);
        var sw = new StringWriter();
        vm.ErrorOutput = sw;
        vm.Execute(chunk);
        return sw.ToString();
    }

    // ── Parameters ───────────────────────────────────────────────

    /// Execute with script arguments.
    protected static object? RunWithArgs(string source, string[] scriptArgs)
    {
        string full = source + "\nreturn result;";
        var (chunk, globals) = CompileRaw(full);
        var vm = new VirtualMachine(globals);
        vm.ScriptArgs = scriptArgs;
        return vm.Execute(chunk);
    }

    /// Execute with a specific source file path.
    protected static object? RunWithFile(string source, string filePath)
    {
        string full = source + "\nreturn result;";
        var (chunk, globals) = CompileRaw(full, filePath);
        var vm = new VirtualMachine(globals);
        return vm.Execute(chunk);
    }

    // ── TAP test harness ─────────────────────────────────────────

    /// Execute with TAP test harness, return reporter and captured output.
    protected static (TapReporter Reporter, string Output) RunWithHarness(string source)
    {
        var (chunk, globals) = Compile(source);
        var vm = new VirtualMachine(globals);
        var sw = new StringWriter();
        var reporter = new TapReporter(sw);
        vm.TestHarness = reporter;
        vm.Execute(chunk);
        reporter.OnRunComplete(reporter.PassedCount, reporter.FailedCount, reporter.SkippedCount);
        return (reporter, sw.ToString());
    }

    // ── Internal pipeline ────────────────────────────────────────

    private static (Chunk Chunk, Dictionary<string, object?> Globals) Compile(
        string source, string sourceName = "<test>")
    {
        var lexer = new Lexer(source, sourceName);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        return (chunk, StdlibDefinitions.CreateVMGlobals());
    }

    private static (Chunk Chunk, Dictionary<string, object?> Globals) CompileRaw(
        string source, string sourceName = "<test>")
    {
        return Compile(source, sourceName);
    }

    private static PreparedVM CompileAndCreateVM(string source, string sourceName)
    {
        var (chunk, globals) = Compile(source, sourceName);
        var vm = new VirtualMachine(globals);
        return new PreparedVM(vm, chunk);
    }

    private readonly record struct PreparedVM(VirtualMachine VM, Chunk Chunk)
    {
        public object? Execute(Chunk chunk) => VM.Execute(chunk);
    }
}
```

> **Decision:** `protected static` methods rather than `protected` instance methods.
> **Rationale:** The current helpers are all `private static`. Making them `protected static` preserves the same no-side-effect semantics. Test classes that need instance state (temp directories) add it via their own fields — the base class stays stateless.
> **Alternative rejected:** A static utility class (`TestHelpers.Run(...)`) — this would work but forces every call site to prefix with `TestHelpers.`, which is noisier than inheritance. Base class inheritance reads naturally as `Run(source)` in test methods.

> **Decision:** Keep `Eval()` separate from `Run()` rather than a boolean flag.
> **Rationale:** Expression parsing and statement parsing are fundamentally different code paths (`parser.Parse()` vs `parser.ParseProgram()`, `Compiler.CompileExpression()` vs `Compiler.Compile()`). A flag would be misleading — these are two distinct pipelines.

### 3.2 Analysis Test Base Class: `AnalysisTestBase`

**Location:** `Stash.Tests/Analysis/AnalysisTestBase.cs`

```csharp
namespace Stash.Tests.Analysis;

public abstract class AnalysisTestBase
{
    protected static (List<Stmt> Stmts, ScopeTree Tree) Analyze(
        string source, bool includeBuiltIns = false) { ... }

    protected static AnalysisResult FullAnalyze(string source) { ... }

    protected static List<SemanticDiagnostic> Validate(string source) { ... }

    protected static List<SemanticDiagnostic> ValidateWithSuppression(string source) { ... }
}
```

This captures the repeated `Lexer→Parser→SymbolCollector→(Validator)` pipeline that 15+ analysis test files duplicate. Specialized variants (`AnalyzeWithDocs`, `AnalyzeWithInference`) are left in the files that need them — they're used by only 1-2 files each and contain genuinely different logic.

### 3.3 Temp Directory Fixture: `TempDirectoryFixture`

**Location:** `Stash.Tests/Interpreting/TempDirectoryFixture.cs`

```csharp
namespace Stash.Tests.Interpreting;

public abstract class TempDirectoryFixture : StashTestBase, IDisposable
{
    protected readonly string TestDir;

    protected TempDirectoryFixture(string prefix = "stash_test")
    {
        TestDir = Path.Combine(Path.GetTempPath(), prefix + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(TestDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(TestDir, true); } catch { }
        GC.SuppressFinalize(this);
    }
}
```

Consumed by `FsBuiltInsTests`, `FsWatchBuiltInsTests`, and `CliPackageCommandsTests`.

> **Decision:** Inherits from `StashTestBase` so file-system tests still get `Run()` etc.
> **Alternative considered:** xUnit `IClassFixture<T>` — rejected because the temp directory is per-test-class, not shared across classes. `IDisposable` on the test class itself is the right xUnit pattern for per-class cleanup.

## 4. Migration Plan

### Phase 1: Create base classes (non-breaking)

1. Create `StashTestBase.cs` with all shared helpers.
2. Create `TempDirectoryFixture.cs` inheriting from `StashTestBase`.
3. Create `AnalysisTestBase.cs` with shared analysis helpers.
4. Verify everything compiles — no existing tests change yet.

### Phase 2: Migrate Interpreting tests (bulk)

For each of the ~25 Interpreting test files:

1. Change class declaration to inherit from `StashTestBase`.
2. Delete the local `Run()`, `RunExpectingError()`, and any other helpers that now exist in the base class.
3. Keep file-specific helpers that are genuinely unique.
4. Run `dotnet test --filter "FullyQualifiedName~{TestClass}"` after each file to verify.

**Files inheriting `StashTestBase` directly (no constructor, no state):**
ArrBuiltInsTests, DictBuiltInsTests, StrBuiltInsTests, JsonBuiltInsTests, TimeBuiltInsTests, CryptoTests, PathBuiltInsTests, SysBuiltInsTests, EncodingTests, ConfigTests, TermBuiltInsTests, NetBuiltInsTests, UfcsTests, SpreadRestTests, AsyncAwaitTests, ExtendBlockTests, DurationByteSizeTests, IpAddressTests, SemVerTests (Interpreting), InterpreterTests, BitwiseOperatorTests, IoBuiltInsTests, DictLiteralTests, DictArgParseTests, RetryExprTests, ElevateTests, SftpBuiltInsTests, SshBuiltInsTests, ArgsNamespaceTests, ArgsBuildTests, TemplateTests, TomlBuiltInsTests, YamlBuiltInsTests, PkgBuiltInsTests, TaskBuiltInsTests

**Files inheriting `TempDirectoryFixture`:**
FsBuiltInsTests, FsWatchBuiltInsTests, CliPackageCommandsTests

**Files with specialized TAP helpers (inherit `StashTestBase`, keep local `RunWithHarness`):**
TestBuiltInsTests, TestDiscoveryTests, TestFilterTests

> Actually, `RunWithHarness` will be in the base class, so these can also just delete their local copies.

### Phase 3: Migrate Analysis tests

For each of the ~20 Analysis test files:

1. Change class to inherit `AnalysisTestBase`.
2. Delete local `Analyze()` / `Validate()` / `FullAnalyze()` where they match the base class signatures.
3. Keep specialized variants that have genuinely different parameters or logic.

### Phase 4: Migrate Bytecode tests (optional, smaller scope)

`CompilerTests` and `VirtualMachineTests` share `CompileSource()`. Either:

- Extract a small `BytecodeTestBase` with `CompileSource()`, or
- Leave as-is (only 2-3 files, lower ROI).

> **Recommendation:** Do this — it's low effort and keeps consistency. `VMDebugTests` also benefits.

### Phase 5: Validation

Run `dotnet test` — all ~2,000 tests must pass. Zero behavioral change.

## 5. Additional Same-Scope Refactors

### 5.1 Using Statement Cleanup

After base class extraction, many test files can drop 3-5 `using` statements that are now only referenced by the base class. IDEs will flag these as unused. Clean them up file-by-file during migration.

### 5.2 Remove `private static` → Rely on `protected static` Inheritance

Currently many test files define helpers as `private static`. After migration, these become inherited `protected static` from the base. No access modifier change is needed in test methods that call them — they were already calling `Run(...)` directly.

### 5.3 Consolidate `_sourceName` Parameter Handling

Some files hardcode `"<test>"`, one uses a variable `sourceName`, some use a `filePath`. The base class unifies this with an optional `sourceName` parameter defaulting to `"<test>"`, eliminating ad-hoc variations.

## 6. What Is NOT In Scope

- **No runtime changes** — this is test infrastructure only.
- **No test logic changes** — assertions, test data, and test method signatures stay identical.
- **No new tests** — this is a pure refactor, not a coverage expansion.
- **No Registry test refactor** — the DB context duplication is real but architecturally different (services, not language execution). It's a separate concern.
- **No DAP test refactor** — reflection-heavy, only 3 files, not worth a base class.

## 7. Risks

| Risk                                                                                         | Likelihood | Mitigation                                                                                                            |
| -------------------------------------------------------------------------------------------- | ---------- | --------------------------------------------------------------------------------------------------------------------- |
| Helper signature mismatch (base class method doesn't perfectly match a file's local variant) | Medium     | Careful diff of each local helper against the base class before deleting. Keep genuinely different helpers local.     |
| Merge conflicts with in-flight feature branches                                              | Low        | This is a test-only refactor. Conflicts will be in using statements and class declarations, easily resolved.          |
| xUnit discovers abstract base class and tries to run it                                      | None       | xUnit only discovers classes with `[Fact]`/`[Theory]` methods. Abstract classes with no test methods are invisible.   |
| Inheritance depth concern (TempDirectoryFixture → StashTestBase → test class = 3 levels)     | Low        | Two levels of inheritance is well within reasonable. The base classes are thin utility classes, not deep hierarchies. |

## 8. Quantified Impact

| Metric                                           | Before        | After               |
| ------------------------------------------------ | ------------- | ------------------- |
| Duplicated `Run()` implementations               | ~18           | 1                   |
| Duplicated `RunExpectingError()` implementations | ~18           | 1                   |
| Duplicated `Analyze()` implementations           | ~15           | 1                   |
| Duplicated temp-directory setup/teardown         | 3             | 1                   |
| Total lines of duplicated infrastructure         | ~1,200+       | ~150 (base classes) |
| Files touched                                    | —             | ~45                 |
| New files created                                | —             | 3-4 (base classes)  |
| Maintenance cost for pipeline changes            | O(n) per file | O(1) in base class  |
