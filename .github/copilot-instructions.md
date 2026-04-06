# Stash Language — Project Guidelines

Stash is a cross-platform scripting language for system administration. The interpreter is .NET-based and compiles to native binaries for Linux, macOS, and Windows. It combines C-style syntax with first-class shell command execution (`$(...)` syntax), built-in data structures (structs, enums, dictionaries), and 24 namespaces of standard library functions. Language features and standard library additions must work across all three platforms.

## Architecture

```
Stash.Core          → Lexer (two-pointer scanner), Parser (recursive-descent), 46 AST node types
Stash.Stdlib        → Built-in metadata registry, model records, single source of truth for all namespaces
Stash.Bytecode      → Bytecode VM (compiler + stack-based VM), 24 built-in namespaces
Stash.Analysis      → Static analysis engine, resolvers, visitors for diagnostics and tooling
Stash.Cli           → REPL + script runner (Native AOT)
Stash.Lsp           → Language Server Protocol (OmniSharp — NOT AOT, requires reflection)
Stash.Dap           → Debug Adapter Protocol (OmniSharp — NOT AOT, requires reflection)
Stash.Playground    → Browser-based interactive playground (Blazor WASM, Monaco editor)
Stash.Registry      → Package registry server (ASP.NET Core, EF Core, JWT auth)
Stash.Tests         → xUnit test suite (~2,000 tests)
```

**Key constraint:** LSP and DAP use OmniSharp/DryIoc which requires reflection. They must **never** be built with Native AOT — only the CLI uses AOT. See `build.stash` for publish commands and binary size guards.

The VS Code extension lives at `.vscode/extensions/stash-lang/` (TypeScript — LSP/DAP clients, TAP test runner, syntax highlighting).

## Build & Test

```bash
dotnet build                            # Build all projects
dotnet test                             # Run all ~2,000 xUnit tests
dotnet run --project Stash.Cli/ -- file.stash   # Run a script
dotnet run --project Stash.Cli/         # Start REPL
```

Test a specific namespace: `dotnet test --filter "FullyQualifiedName~ArrBuiltInsTests"`

## Code Conventions

- **C# style:** File-scoped namespaces, nullable enabled, 4-space indent, LF line endings
- **Naming:** `PascalCase` public, `_camelCase` private fields — enforced in `.editorconfig`
- **var usage:** Never for built-in types (`string`, `int`), always when type is apparent from RHS
- **AST nodes:** Each has a `SourceSpan` for diagnostics. Expression nodes implement `IExprVisitor<T>`, statement nodes `IStmtVisitor<T>`
- **Built-in namespaces:** One file per namespace in `Stash.Stdlib/BuiltIns/` (e.g., `ArrBuiltIns.cs`). Register functions via `BuiltInFunction` delegates
- **Tests:** `{Feature}_{Scenario}_{Expected}()` naming in `Stash.Tests/`, one test file per namespace (`ArrBuiltInsTests.cs`, `DictBuiltInsTests.cs`, etc.)

## Language Semantics (for writing tests and interpreting behavior)

- **No type coercion on equality:** `5 != "5"`, `0 != false`, `0 != null`
- **Truthiness:** Falsy values are `null`, `false`, `0`, `0.0`, `""`
- **Short-circuit returns operands:** `null || "default"` → `"default"`, `"a" && "b"` → `"b"`
- **Reference equality** for dictionaries and struct instances (no value-based `Equals`)
- **Shallow copy** on `dict.merge` — nested structures are shared, not cloned

## Documentation

Detailed docs live in `docs/` — link to these instead of duplicating content:

| Topic                         | File                                         |
| ----------------------------- | -------------------------------------------- |
| Full language spec            | `docs/Stash — Language Specification.md`     |
| All 24 namespaces + functions | `docs/Stash — Standard Library Reference.md` |
| LSP features & architecture   | `docs/LSP — Language Server Protocol.md`     |
| DAP features & architecture   | `docs/DAP — Debug Adapter Protocol.md`       |
| TAP test framework            | `docs/TAP — Testing Infrastructure.md`       |
| Templating engine             | `docs/TPL — Templating Engine.md`            |
| Package manager CLI           | `docs/PKG — Package Manager CLI.md`          |
| Package registry              | `docs/Registry — Package Registry.md`        |
| Design specs & analysis       | `docs/specs/`                                |
