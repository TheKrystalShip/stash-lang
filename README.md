# Stash

A dynamically typed scripting language for system administration. Stash combines C-style syntax with first-class shell command execution, real data structures (structs, enums, interfaces), and 29 namespaces of standard library functions — compiled to a register-based bytecode VM for fast execution on Linux, macOS, and Windows.

[**Try it in the Playground →**](https://playground.stash-lang.dev/)

---

## Table of Contents

- [Why Stash?](#why-stash)
- [Quick Tour](#quick-tour)
- [Standard Library](#standard-library)
- [Getting Started](#getting-started)
- [Tooling](#tooling)
- [Package Management](#package-management)
- [Package Registry](#package-registry)
- [Embedding](#embedding)
- [Performance](#performance)
- [Architecture](#architecture)
- [Documentation](#documentation)
- [License](#license)

---

## Why Stash?

- **Shell commands are first-class.** `$(ls -la)` just works — no subprocess imports, no string wrangling. Pipe chains short-circuit on failure.
- **Real data structures.** Structs, enums, interfaces, and dictionaries — model your domain instead of juggling parallel arrays and magic strings.
- **C-style syntax.** If you know C, C++, C#, Java, or JavaScript, you can read and write Stash immediately.
- **Register-based bytecode VM.** Constant folding, dead branch elimination, peephole optimization, inline caching, and integer-specialized loop opcodes. No tree-walking overhead.
- **Sensible error handling.** `try` expression with `??` fallback for inline errors. Full `try/catch/finally` also available.
- **Built-in parallelism and async/await.** `task.run()`, `arr.parMap`, `arr.parForEach` — isolated tasks with snapshot semantics, no shared-state bugs.
- **Interfaces and structural contracts.** Define with `interface`, check with `is`, extend any type with `extend` blocks.
- **UFCS.** Call any function as a method on its first argument: `nums.sort()` instead of `arr.sort(nums)`.
- **Rich literals.** Durations (`5s`), byte sizes (`1.5GB`), semver (`@v1.2.3`), IP addresses (`@192.168.1.0/24`), hex/octal/binary numbers.
- **System administration built-in.** `elevate` for privilege escalation, `retry` for transient failures, signal handling, file watching, SSH/SFTP.
- **29 stdlib namespaces, ~376 functions.** HTTP, crypto, YAML, TOML, JSON, INI, templating, encoding, networking, and more — all cross-platform.
- **Full toolchain.** LSP, DAP, static analyzer (63 rules), formatter, TAP test runner, browser playground — no external tools required.

---

## Quick Tour

#### Variables & Types

```stash
let name = "deploy";       // mutable
const MAX: int = 3;        // immutable with optional type hint
let pending;               // null until assigned
```

#### Structs & Enums

```stash
enum Status { Active, Inactive, Pending }

struct Server {
  host: ip,
  port: int,
  status: Status
}

let srv = Server { host: @10.0.0.1/16, port: 22, status: Status.Active };
```

#### Command Execution

```stash
let result = $(ping -c 1 ${srv.host});
io.println(result.stdout);    // captured stdout
io.println(result.exitCode);  // 0 on success

let count = $(cat /var/log/syslog | grep error | wc -l);  // pipe chain
```

#### Error Handling

```stash
// Inline — try expression with ?? fallback
let config = try fs.readFile("/etc/app.conf") ?? "{}";

// Full try/catch/finally
try {
  let data = json.parse(fs.readFile("/etc/app.conf"));
} catch (e) {
  io.eprintln("Config failed: " + e.message);
} finally {
  io.println("Done");
}
```

#### Functions & Lambdas

```stash
fn add(a: int, b: int) -> int { return a + b; }

let double = (x) => x * 2;
let result = arr.map([1, 2, 3], double);  // [2, 4, 6]
```

#### Optional Chaining & Null Coalescing

```stash
let port = config?.database?.port ?? 3306;
let user = session?.user?.name ?? "anonymous";
```

#### Switch Expressions & Statements

```stash
// Expression — inline value
let label = srv.status switch {
    Status.Active   => "running",
    Status.Inactive => "stopped",
    _               => "unknown"
};

// Statement — with multi-pattern cases
switch (status) {
    case "active": { io.println("Active"); }
    case "inactive", "banned": { io.println("Blocked"); }
    default: { io.println("Unknown"); }
}
```

#### Parallelism & Async/Await

```stash
// Parallel tasks with snapshot semantics
let h1 = task.run(() => crypto.sha256("file1"));
let h2 = task.run(() => crypto.sha256("file2"));
let results = task.awaitAll([h1, h2]);

// Async/await for non-blocking I/O
async fn fetchStatus(host: string) -> string {
  return $(curl -s ${host}/health).stdout;
}
let status = await fetchStatus("https://api.example.com");
```

#### Imports

```stash
import { deploy, Server } from "utils.stash";      // selective
import "lib/utils.stash" as utils;                  // aliased namespace
import $"{env.home()}/config.stash";               // dynamic path
```

#### Interfaces

```stash
interface Deployable {
  fn deploy(target: string) -> bool;
  fn rollback();
}

struct App : Deployable {
  name: string,
  fn deploy(target: string) -> bool {
    return $(scp ${self.name}.tar.gz ${target}:/opt/).exitCode == 0;
  }
  fn rollback() { io.println("Rolling back " + self.name); }
}

let app = App { name: "web-api" };
if (app is Deployable) { app.deploy("prod-server"); }
```

#### Extend Blocks & UFCS

```stash
// Add methods to any existing type
extend string {
  fn shout() -> string { return str.upper(self) + "!!!"; }
}
"hello".shout();  // "HELLO!!!"

// UFCS — any function callable as a method on its first argument
let sorted = [3, 1, 2].sort();   // arr.sort([3, 1, 2])
let upper  = "hello".upper();    // str.upper("hello")
```

#### Rich Literals

```stash
let timeout    = 30s;             // Duration
let maxSize    = 1.5GB;           // ByteSize
let version    = @v2.1.0;         // SemVer
let subnet     = @192.168.1.0/24; // IP Address
let perms      = 0o755;           // Octal
let flags      = 0b1010_0101;     // Binary with separators
```

#### Regex Captures with Named Groups

```stash
let m = str.capture("2026-04-08", "(?<year>\\d{4})-(?<month>\\d+)-(?<day>\\d+)");
io.println(m.namedGroups["year"]);   // "2026"
io.println(m.namedGroups["month"]);  // "04"
```

#### System Administration

```stash
// Retry with exponential backoff
retry (maxAttempts: 3, delay: 2s, backoff: Backoff.Exponential) {
  $!(curl -f https://api.example.com/deploy);
}

// Privilege escalation (sudo / gsudo)
elevate {
  $(systemctl restart nginx);
  fs.writeFile("/etc/nginx/nginx.conf", newConfig);
}

// File watching
fs.watch("/etc/nginx/", (event) => {
  $(nginx -t && nginx -s reload);
});

// Signal handling
process.onSignal(process.SIGTERM, () => {
  cleanup();
  process.exit(0);
});
```

---

## Standard Library

29 namespaces, ~376 functions. All cross-platform.

| Category          | Namespaces                                          |
| ----------------- | --------------------------------------------------- |
| **I/O & System**  | `io`, `fs`, `path`, `env`, `sys`, `process`, `term` |
| **Data**          | `str`, `arr`, `dict`, `math`, `conv`                |
| **Time**          | `time`                                              |
| **Serialization** | `json`, `yaml`, `toml`, `ini`, `config`             |
| **Network**       | `http`, `net`, `ssh`, `sftp`                        |
| **Security**      | `crypto`, `encoding`                                |
| **Concurrency**   | `task`                                              |
| **Tooling**       | `tpl`, `args`, `assert`, `test`                     |

**Global functions:** `typeof()`, `nameof()`, `len()`

See the [Standard Library Reference](docs/Stash%20—%20Standard%20Library%20Reference.md) for the full function list.

---

## Getting Started

```bash
dotnet build                                         # Build all projects
dotnet run --project Stash.Cli/                      # Start the REPL
dotnet run --project Stash.Cli/ -- script.stash      # Run a script
dotnet run --project Stash.Cli/ -- -c 'io.println("hi");'
echo 'code' | dotnet run --project Stash.Cli/        # Pipe from stdin
dotnet test                                          # Run 5,700+ tests
stash-check .                                        # Lint all .stash files
stash-format --write .                               # Format all files
```

Make scripts directly executable with a shebang:

```stash
#!/usr/bin/env stash
io.println("Hello from Stash!");
```

```bash
chmod +x hello.stash && ./hello.stash
```

---

## Tooling

Stash ships with a complete toolchain — no plugins or third-party tools required:

- **Language Server (LSP)** — completions, hover, diagnostics, go-to-definition, rename, semantic tokens, type narrowing, UFCS support, and range/on-type formatting. Full analysis pipeline averages **1.2ms** per document change. See [docs/LSP — Language Server Protocol.md](docs/LSP%20—%20Language%20Server%20Protocol.md).
- **Debug Adapter (DAP)** — breakpoints, stepping, variable inspection for VS Code and other DAP clients. See [docs/DAP — Debug Adapter Protocol.md](docs/DAP%20—%20Debug%20Adapter%20Protocol.md).
- **Built-in Test Runner (TAP)** — `test.it()`, `test.describe()`, `assert.*` with lifecycle hooks (`beforeAll`, `afterAll`, `beforeEach`, `afterEach`) and TAP output. No external framework needed. See [docs/TAP — Testing Infrastructure.md](docs/TAP%20—%20Testing%20Infrastructure.md).
- **Static Analysis CLI (`stash-check`)** — 63 diagnostic rules across 14 categories, autofix support (safe/unsafe classification), flow analysis, `.stashcheck` config with presets and per-file overrides, and 5 output formats: `text`, `grouped`, `json`, `github`, `sarif`. Watch mode for incremental re-analysis.
- **Code Formatter (`stash-format`)** — `--write`, `--check`, and `--diff` modes. `.stashformat` config with 22+ options (indent style, print width, trailing commas, import sorting, etc.) and `.editorconfig` fallback.
- **VS Code Extension** — LSP/DAP clients, TAP test runner integration, syntax highlighting, and a **bytecode disassembly visualizer**.
- **Templating Engine** — Jinja2-style templates with filters, conditionals, loops, includes, and whitespace control via `tpl.render` / `tpl.renderFile`. See [docs/TPL — Templating Engine.md](docs/TPL%20—%20Templating%20Engine.md).
- **Browser Playground** — Live at [playground.stash-lang.dev](https://playground.stash-lang.dev/). Monaco editor, WASM runtime, curated examples — no installation required. See [docs/Playground — Browser Playground.md](docs/Playground%20—%20Browser%20Playground.md).

---

## Package Management

Stash includes a built-in package manager. Packages are defined by a `stash.json` manifest, locked with `stash-lock.json`, and installed into a `stashes/` directory.

```bash
stash pkg init                          # Create a new stash.json
stash pkg install http-client@1.2.0     # Add and install a dependency
stash pkg install                       # Install all dependencies
stash pkg update                        # Re-resolve to latest matching versions
stash pkg search http-client            # Search a registry
stash pkg publish                       # Publish the current package
stash pkg login --registry <url>        # Authenticate with a registry
```

Dependencies support semver constraints (`^1.2.0`, `~1.0.0`, `*`) and Git sources (`git:https://github.com/user/repo.git#v1.0.0`).

All registry commands accept `--registry <url>`. Without it, the CLI uses the default registry from `~/.stash/config.json` (set automatically on first `stash pkg login`).

See [docs/PKG — Package Manager CLI.md](docs/PKG%20—%20Package%20Manager%20CLI.md) for the full reference.

---

## Package Registry

Stash ships with a self-hosted package registry server. Run your own registry with zero external dependencies — SQLite and local filesystem storage work out of the box.

```bash
dotnet run --project Stash.Registry/    # Start the registry server
```

The registry provides a full REST API for publishing, installing, searching, and managing packages. It includes JWT authentication with scoped tokens, rate limiting, integrity verification, and audit logging. PostgreSQL and S3 backends are supported for larger deployments.

See [docs/Registry — Package Registry.md](docs/Registry%20—%20Package%20Registry.md) for the full API reference, configuration options, and architecture details.

---

## Embedding

Stash can be embedded into any .NET application as a scripting engine. The `Stash.Bytecode` library exposes a clean `StashEngine` API:

```csharp
using Stash.Interpreting;

var engine = new StashEngine(StashCapabilities.None); // sandboxed

engine.SetGlobal("playerName", "Alice");
engine.SetGlobal("getHealth", engine.CreateFunction("getHealth", 0, (args) => 100L));

engine.Run("io.println(\"Hello, \" + playerName);");
var result = engine.Evaluate("playerName");
```

**Sandboxing** — Use `StashCapabilities` flags to control what scripts can access:

| Flag          | Controls                                   |
| ------------- | ------------------------------------------ |
| `FileSystem`  | `fs.*` — file/directory operations         |
| `Network`     | `http.*` — HTTP requests                   |
| `Process`     | `process.*`, `exit()` — process management |
| `Environment` | `env.*` — environment variables            |

`StashCapabilities.None` disables all system access. `StashCapabilities.All` enables everything (default for CLI).

See the [embedding demo](examples/EmbeddingDemo/) for a full working example.

---

## Performance

Stash compiles to a **register-based bytecode VM** with constant folding, dead branch elimination, peephole optimization, superinstructions, inline caching, and integer-specialized loop opcodes.

**What each benchmark tests**

| Benchmark                 | What it tests                                                                                  |
| ------------------------- | ---------------------------------------------------------------------------------------------- |
| **Algorithms**            | Recursion (fib 26), bubble sort (1,000 elements), binary search (10,000 lookups), struct usage |
| **Function Calls**        | 600,000 calls across 0–4 argument arities + a compute function                                 |
| **Expression Throughput** | Dense arithmetic over 70 variables, string interpolation, nested math                          |
| **Built-in Functions**    | 2,600,000 stdlib calls — math, string, and conversion functions                                |
| **Scope Lookup**          | Variable resolution across 5-level nested closures (100,000 iterations)                        |

**Results**

| Benchmark                 |  Stash | Python | Node.js |   Ruby |   Perl |    Lua |      Bash |
| ------------------------- | -----: | -----: | ------: | -----: | -----: | -----: | --------: |
| **Algorithms**            | 162 ms |  88 ms |    7 ms |  56 ms | 208 ms |  34 ms | 10,412 ms |
| **Function Calls**        |  93 ms |  84 ms |    3 ms |  17 ms | 101 ms |  13 ms |  3,743 ms |
| **Expression Throughput** | 165 ms | 181 ms |   18 ms | 188 ms | 131 ms |  85 ms |  4,955 ms |
| **Built-in Functions**    | 202 ms | 319 ms |   29 ms | 347 ms | 341 ms | 207 ms | 23,508 ms |
| **Scope Lookup**          | 111 ms | 103 ms |    5 ms | 124 ms | 241 ms |  58 ms |  3,307 ms |

> Measured on the same machine, same workload, identical algorithms and iteration counts across all languages. Median of 3 runs.
>
> Full benchmark scripts in [`benchmarks/`](benchmarks/).

---

## Architecture

```
Stash.Core          → Lexer, Parser, 46 AST node types
Stash.Stdlib        → Standard library metadata registry (29 namespaces, ~376 functions)
Stash.Bytecode      → Register-based bytecode VM (compiler + VM, 70+ opcodes)
Stash.Analysis      → Static analysis engine (63 rules, autofix, flow analysis)
Stash.Cli           → REPL + script runner (Native AOT)
Stash.Lsp           → Language Server Protocol
Stash.Dap           → Debug Adapter Protocol
Stash.Tpl           → Templating engine
Stash.Tap           → TAP test framework
Stash.Check         → Static analysis CLI (Native AOT)
Stash.Format        → Code formatter CLI (Native AOT)
Stash.Playground    → Browser playground (Blazor WASM)
Stash.Registry      → Package registry server (ASP.NET Core)
Stash.Tests         → 5,700+ xUnit tests
```

---

## Documentation

| Document                                                                         | Description                                         |
| -------------------------------------------------------------------------------- | --------------------------------------------------- |
| [Language Specification](docs/Stash%20—%20Language%20Specification.md)           | Complete syntax, type system, and language design   |
| [Standard Library Reference](docs/Stash%20—%20Standard%20Library%20Reference.md) | All 29 namespaces and ~376 functions                |
| [Language Server Protocol](docs/LSP%20—%20Language%20Server%20Protocol.md)       | LSP architecture and supported features             |
| [Debug Adapter Protocol](docs/DAP%20—%20Debug%20Adapter%20Protocol.md)           | DAP implementation and debugging support            |
| [Testing Infrastructure](docs/TAP%20—%20Testing%20Infrastructure.md)             | Built-in test runner and TAP output                 |
| [Templating Engine](docs/TPL%20—%20Templating%20Engine.md)                       | Jinja2-style template rendering and `tpl` namespace |
| [Package Registry](docs/Registry%20—%20Package%20Registry.md)                    | Self-hosted registry server and CLI integration     |
| [Package Manager CLI](docs/PKG%20—%20Package%20Manager%20CLI.md)                 | Package manager commands and manifest format        |
| [Browser Playground](docs/Playground%20—%20Browser%20Playground.md)              | Interactive WASM-based playground                   |

---

## License

GPL-3.0 — see [LICENSE](LICENSE) for details.
