# Stash.Tests Guidelines

xUnit test suite with 5,800+ tests. Every project has a corresponding test subdirectory.

## Directory Map

```
Stash.Tests/
├── Analysis/         → LSP analysis: symbol resolution, type inference, formatting, diagnostics
├── Bytecode/         → VM, compiler, optimizer, serialization, CFG, peephole, LVN, DCE
├── Cli/              → CLI execution, argument parsing
├── Common/           → Shared test base classes and helpers
├── Core/             → ErrorTypeRegistry tests
├── Dap/              → Debug adapter protocol: handlers, session state, integration
├── Debugging/        → Debugger integration tests
├── Interpreting/     → All built-in namespace tests + language feature tests
├── Lexing/           → Lexer tests
├── Parsing/          → Parser tests
├── Registry/         → Package registry: database, storage, auth, API
├── Scheduler/        → Service manager tests
└── Stdlib/           → Stdlib-specific tests
```

## Naming Convention

`{Feature}_{Scenario}_{Expected}()`

Examples:
- `SortBy_NumericKey_SortsCorrectly()`
- `Dict_MergeWithConflict_RightWins()`
- `Compiler_ClosureCapture_ClosesOverCorrectVariable()`
- `SemanticValidator_UndeclaredVariable_EmitsSA0201()`

## Imports — global usings are on

`Stash.Tests.csproj` sets `<ImplicitUsings>enable</ImplicitUsings>` + `<Using Include="Xunit" />`, so `System.*` and `Xunit` are already global. Do **not** re-declare `using System;`/`using System.Linq;`/`using Xunit;`/etc. in new test files — they trigger CS8933 (duplicate global using) + CS8019 (unnecessary using) warnings. Only `using` project-specific namespaces.

## Running Tests

```bash
dotnet test                                                    # All tests
dotnet test --filter "FullyQualifiedName~ArrBuiltInsTests"    # One test class
dotnet test --filter "FullyQualifiedName~BytecodeVmTests"     # Bytecode VM tests
dotnet test --filter "FullyQualifiedName~Analysis"            # All analysis tests
dotnet test --filter "Category=Integration"                    # By category
dotnet test --filter "DisplayName~SortBy"                     # By method name fragment
dotnet test -v normal                                          # Verbose output
```

## Test Helpers

### Fixture Files

Tests that load fixture files (e.g. expected-output snapshots) use the **embedded-resource** pattern, never `CopyToOutputDirectory`. Add `<EmbeddedResource Include="Path\To\*.txt" />` to `Stash.Tests.csproj` and read via `Assembly.GetManifestResourceStream("Stash.Tests.Path.To.Filename.txt")`.

### Completion surface snapshots

`Stash.Tests/Lsp/CompletionSurfaceSnapshotTests.cs` locks in the LSP completion set for canonical cursor positions. Fixtures live under `Stash.Tests/Lsp/Snapshots/*.txt` as embedded resources. Re-baseline after an intentional change with:

```bash
STASH_SNAPSHOT_REGEN=1 dotnet test --filter FullyQualifiedName~CompletionSurfaceSnapshotTests
```

The regen run intentionally fails so the new fixture shows up as a working-tree diff (no silent re-baselining).

### Interpreting Tests

Each built-in namespace test file uses this shared pattern:

```csharp
private static object? Run(string source)
{
    // Lex → Parse → Resolve → Bytecode compile → Execute
    // Returns the value of a "result" variable from the executed script
}

private static void RunExpectingError(string source, string? messageContains = null)
{
    // Same pipeline but Assert.Throws<RuntimeError>
}
```

### Bytecode Tests

`BytecodeTestBase.cs` provides:
- `Compile(source)` → `Chunk` — compile without running
- `Run(source)` → `object?` — compile and execute, return result
- `Disassemble(source)` → `string` — get disassembly output
- `RunExpectingError(source)` — assert RuntimeError

### Analysis Tests

`AnalysisTestBase.cs` provides:
- `Analyze(source)` → `ScopeTree` — run only symbol collection
- `FullAnalyze(source)` → `AnalysisResult` — full pipeline including diagnostics
- `GetDiagnostics(source)` → `IEnumerable<SemanticDiagnostic>` — extract diagnostics

## Parallelization — serialize process-global state

xUnit runs test classes in parallel by default. A test that **captures or mutates process-global state** will flake the *full suite* non-deterministically (green in isolation, red under load) while a sibling races it. The classic case: capturing `Console.Out`/`Console.Error` to assert CLI command output — another test's `Console.WriteLine` lands in your capture buffer. Fix: join a `[Collection(...)]` whose `[CollectionDefinition]` sets `DisableParallelization = true`. **CLI command tests that print to the console (e.g. via a command's `ExecuteCore`) must use `[Collection("CliTests")]`** (defined in `Interpreting/CliPackageCommandsTests.cs`). Other process-global collections: `SystemConsoleTests`, `SystemCwdTests`, `RegistryConcurrency`.

## Adding Tests for a New Feature

1. **Find the right file** — match the feature to its namespace test file (e.g., `ArrBuiltInsTests.cs` for `arr.*`)
2. **For new language constructs** — use `Stash.Tests/Interpreting/` for behavior, `Stash.Tests/Bytecode/CompilerTests.cs` for compiled output
3. **For new diagnostics** — use `Stash.Tests/Analysis/` and assert the SA code is emitted
4. **For new CLI flags** — use `Stash.Tests/Cli/CliExecutionTests.cs`

## Test Coverage Expectations

Every PR that adds or changes behavior must include:
- Happy path test(s) — the feature works as specified
- Edge case test(s) — boundary conditions, empty inputs, type mismatches
- Error case test(s) — `RunExpectingError` for invalid usage

Run `dotnet test` and confirm zero failures before marking work complete. Never skip failing tests — fix them or understand why they fail before proceeding.
