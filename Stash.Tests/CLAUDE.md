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
