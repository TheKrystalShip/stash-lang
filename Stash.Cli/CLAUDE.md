# Stash.Cli Guidelines

The primary CLI entry point for Stash. Built with **Native AOT** — no reflection, no dynamic code generation. Also contains the REPL, shell mode, package manager CLI, and service manager CLI.

## AOT Constraints

This project is published as a Native AOT binary. This means:
- **No reflection** (`typeof(T)` is fine; `Type.GetType(string)` is not)
- **No `dynamic`**
- **No `Activator.CreateInstance` or `MethodInfo.Invoke`** for runtime-discovered types
- **No `Expression<T>` compilation** at runtime
- **Trimming is aggressive** — code that appears unreachable at compile time may be stripped

If you add a dependency, verify it is AOT-compatible. Check the project file for existing trim suppressions — adding new ones requires justification.

**Build the AOT binary:**
```bash
dotnet publish Stash.Cli/ -c Release -o .bench-bin   # for benchmarking
dotnet publish Stash.Cli/ -c Release                  # for distribution
```

Use `build.stash` for official release builds — it includes binary size guards (currently ~8MB limit for Linux x64).

## CLI Flags Reference

| Flag | Purpose |
| ---- | ------- |
| `--help`, `-h` | Show help |
| `--version`, `-v` | Show version (currently 0.5.0) |
| `-c`, `--command <code>` | Execute inline code |
| `--test` | Run with TAP test harness |
| `--test-list` | List test names (discovery mode, no execution) |
| `--test-filter=<pat>` | Semicolon-separated prefix-match filter |
| `--debug` | Attach DAP debugger |
| `--compile` | Compile script to `.stashc` bytecode |
| `--strip` | Strip debug info from compiled bytecode |
| `--verify` | Verify bytecode file integrity |
| `--disassemble` | Print bytecode disassembly without executing |
| `--no-optimize` | Disable bytecode optimizer passes |
| `--shell` | Enable REPL shell mode (experimental) |
| `--no-shell` | Disable shell mode (overrides `STASH_SHELL=1`) |
| `--no-history` | Disable persistent REPL history |
| `--reset-prompt` | Re-extract prompt bootstrap scripts and exit |
| `-o`, `--output <path>` | Output path for compiled bytecode |

## Subcommands

| Subcommand | Aliases | Entry Point |
| ---------- | ------- | ----------- |
| Package manager | `pkg`, `p` | `PackageManager/Commands/PackageCommands.cs` |
| Service manager | `service`, `svc` | `ServiceManager/ServiceCommands.cs` |
| AST graph | `ast`, `a` | `AstGraph/AstCommands.cs` |

## Project Structure

```
Stash.Cli/
├── Program.cs              → Entry point: argument parsing + mode dispatch
├── Repl/                   → REPL loop, multi-line input handling
├── LineEditor.cs           → Readline-like line editor
├── MultiLineReader.cs      → Brace/paren depth tracking for multi-line input
├── Completion/             → Tab completion providers
├── History/                → Persistent REPL history (~/.stash/history)
├── Shell/                  → Shell mode: directory stack, prompt, integration
├── PackageManager/         → `stash pkg` subcommand implementation
│   ├── Commands/           → install, publish, search, info, etc.
│   └── ...
├── ServiceManager/         → `stash service` subcommand (wraps Stash.Scheduler)
│   └── ServiceCommands.cs
└── AstGraph/               → `stash ast` subcommand (Graphviz DOT AST visualizer)
    ├── AstCommands.cs      → Entry point dispatcher
    ├── AstRunner.cs        → Lex → Parse → (Resolve) → DOT generation
    ├── Models/
    │   ├── AstOptions.cs   → CLI arg parsing (--output, --semantic)
    │   └── AstResult.cs    → Success/error result type
    └── Visitors/
        └── AstDotVisitor.cs → IExprVisitor + IStmtVisitor → DOT output
```

## REPL Modes

Two input modes in the REPL:
- **Statement mode** — input containing `;` or `{` is parsed as a full program and executed
- **Expression mode** — bare expression input is evaluated and the result is printed

Shell mode (`--shell`) integrates with the host shell for `cd`, directory stack (`pushd`/`popd`), and environment variable propagation.

## Execution Pipeline

```
CLI args → argument parsing → mode detection
    ↓
Lex (Lexer) → Parse (Parser) → SemanticResolver → Compile (Compiler) → Execute (VirtualMachine)
```

Errors at each stage go to stderr. Exit code 0 = success, 1 = script error, 2 = parse/compile error.

## Package Manager (stash pkg)

Wraps the Stash Registry REST API. Commands: `install`, `publish`, `search`, `info`, `login`, `logout`, `whoami`, `init`. Config stored at `~/.stash/config.json`. See `docs/PKG — Package Manager CLI.md`.

## Service Manager (stash service)

Wraps `Stash.Scheduler` for OS-level service management. Commands: `install`, `uninstall`, `start`, `stop`, `status`, `restart`, `logs`. Dispatches to systemd/launchd/Task Scheduler based on platform. See `docs/Scheduler — Service Manager.md`.

## AST Graph (stash ast)

Generates Graphviz DOT graphs of the abstract syntax tree. Options: `--output`/`-o` (file path), `--semantic`/`-s` (include scope resolution info). Output is a DOT digraph to stdout or file. Render with `dot -Tpng ast.dot -o ast.png`.
