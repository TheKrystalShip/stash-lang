# Contributing to Stash

Thank you for your interest in contributing to Stash! This guide will help you get up and running quickly.

---

## 1. Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [VS Code](https://code.visualstudio.com/) with the Stash extension (recommended)

### Clone and Build

```bash
git clone https://github.com/stash-lang/stash.git
cd stash
dotnet build
```

### Run Tests

```bash
dotnet test
```

To run tests for a specific namespace:

```bash
dotnet test --filter "FullyQualifiedName~ArrBuiltInsTests"
```

### Run a Script

```bash
dotnet run --project Stash.Cli/ -- examples/hello.stash
```

### Start the REPL

```bash
dotnet run --project Stash.Cli/
```

---

## 2. Project Structure

### Projects

| Project            | Description                                                 |
| ------------------ | ----------------------------------------------------------- |
| `Stash.Core`       | Lexer, Parser, 54 AST node types (no dependencies)          |
| `Stash.Stdlib`     | Built-in namespace registry, 35 standard library namespaces |
| `Stash.Bytecode`   | Bytecode compiler + register-based VM, 94 opcodes           |
| `Stash.Analysis`   | Static analysis engine, diagnostics, semantic resolvers     |
| `Stash.Cli`        | REPL and script runner (Native AOT)                         |
| `Stash.Lsp`        | Language Server Protocol (OmniSharp — **not** Native AOT)   |
| `Stash.Dap`        | Debug Adapter Protocol (OmniSharp — **not** Native AOT)     |
| `Stash.Check`      | Static analysis CLI (Native AOT)                            |
| `Stash.Format`     | Code formatter CLI (Native AOT)                             |
| `Stash.Tap`        | TAP test framework runtime                                  |
| `Stash.Tpl`        | Templating engine                                           |
| `Stash.Scheduler`  | Cross-platform OS service management                        |
| `Stash.Playground` | Browser-based playground (Blazor WASM)                      |
| `Stash.Registry`   | Package registry server (ASP.NET Core)                      |
| `Stash.Tests`      | xUnit test suite (5,800+ tests)                             |

### Layer Diagram

```
Layer 0 (Foundation)  →  Stash.Core
Layer 1 (Libraries)   →  Stash.Stdlib, Stash.Analysis, Stash.Tpl, Stash.Scheduler
Layer 2 (Runtime)     →  Stash.Bytecode
Layer 3 (Tooling)     →  Stash.Cli, Stash.Lsp, Stash.Dap, Stash.Check, Stash.Format, Stash.Playground, Stash.Registry
Layer 4 (Tests)       →  Stash.Tests, Stash.Tap
```

`Stash.Core` has no dependencies and must stay that way. `Stash.Bytecode` depends on `Stash.Stdlib` (not the other way around). `Stash.Lsp`, `Stash.Dap`, and `Stash.Registry` must not reference each other.

### Where to Add Things

- **New stdlib function** → `Stash.Stdlib/BuiltIns/<Namespace>BuiltIns.cs`
- **New AST node** → `Stash.Core/Parsing/AST/`

---

## 3. Code Style

Stash is a C# codebase. Please follow these conventions:

- **Namespaces:** File-scoped (`namespace Stash.Foo;`)
- **Nullable:** Enabled — annotate everything correctly
- **Indentation:** 4 spaces, LF line endings
- **Naming:**
  - `PascalCase` for all public members
  - `_camelCase` for private fields
- **`var` usage:**
  - **Never** for built-in types: `string name = ...`, `int count = ...`, `bool flag = ...`
  - **Always** when the type is obvious from the right-hand side: `var engine = new StashEngine()`

These rules are enforced by [`.editorconfig`](.editorconfig).

---

## 4. Adding a Standard Library Function

1. Open the appropriate file in `Stash.Stdlib/BuiltIns/` (e.g., `IoBuiltIns.cs` for the `io` namespace).
2. Implement your function as a `BuiltInFunction` delegate.
3. Register it in the namespace builder using `ns.Function(...)`.
4. Add documentation to [`docs/Stash — Standard Library Reference.md`](docs/Stash%20—%20Standard%20Library%20Reference.md).
5. Write tests in the corresponding test file (e.g., `Stash.Tests/BuiltIns/IoBuiltInsTests.cs`).

### Test Naming Convention

```
{Function}_{Scenario}_{Expected}()
```

**Example:** Adding `io.readLine` requires:

- Updating `IoBuiltIns.cs` with the implementation and registration
- Adding an entry to the Standard Library Reference doc
- Writing tests in `IoBuiltInsTests.cs`

---

## 5. Adding a Language Feature

Before starting, read [`.github/instructions/language-changes.instructions.md`](.github/instructions/language-changes.instructions.md) for the full mandatory checklist.

At a high level:

1. **Add an AST node** in `Stash.Core/Parsing/AST/`
2. **Update ALL six visitors** — every visitor must handle every node:
   - `Compiler` (bytecode emission)
   - `SemanticResolver` (name resolution)
   - `SemanticValidator` (type/usage checks)
   - `SymbolCollector` (LSP symbol index)
   - `SemanticTokenWalker` (LSP syntax highlighting)
   - `StashFormatter` (code formatter)
3. **Write tests** in `Stash.Tests/`
4. **Update documentation** in `docs/`

Missing a visitor is a common mistake — the build will usually catch it via exhaustive pattern matching, but double-check manually.

---

## 6. Submitting a PR

- **Branch naming:** `feature/description`, `fix/description`, `docs/description`
- **All tests must pass** before opening a PR:
  ```bash
  dotnet test
  ```
- **One feature per PR** — keep changes focused and reviewable
- **Link related issues** in the PR description

---

## 7. Filing Issues

### Bug Reports

Please include:

- Stash version (output of `stash --version`)
- Operating system and version
- A minimal Stash script that reproduces the issue

### Feature Requests

Describe the **use case** you are trying to solve, not just the API you want. Good feature requests explain _why_ something is needed, which helps us design the right solution.

---

Thanks for contributing!
