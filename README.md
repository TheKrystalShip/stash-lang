# Stash

A dynamically typed, interpreted scripting language that combines the **shell scripting power** of Bash, the **syntax familiarity** of C/C++/C#, and the **structured data** capabilities that shell languages have always lacked.

```stash
#!/usr/bin/env stash

import { deploy } from "deploy.stash";
import "lib/utils.stash" as utils;

enum Status { Unknown, Active, Inactive }

struct Server {
  host: string,
  port: int,
  status: Status
}

const DEFAULT_HOST: string = "192.168.1.10";

// try catches runtime errors, ?? provides a fallback
let address: string = try fs.readFile("/etc/server.conf") ?? DEFAULT_HOST;

let srv = Server { host: address, port: 22, status: Status.Unknown };

// First-class command execution with interpolation
let result = $(ping -c 1 ${srv.host});
srv.status = result.exitCode == 0 ? Status.Active : Status.Inactive;

// Functions with type hints
fn checkServer(server: Server) -> string {
  return server.status switch {
    Status.Active => "online",
    Status.Inactive => "offline",
    _ => "unknown"
  };
}

// Lambdas — concise anonymous functions
let format = (label: string, value) => $"{label}: {value}";

let servers = [
  Server { host: "10.0.0.1", port: 22, status: Status.Unknown },
  Server { host: "10.0.0.2", port: 22, status: Status.Unknown },
];

for (let s: Server in servers) {
  if (deploy(s, "app.tar.gz")) {
    utils.log(format("Deployed to", s.host));
  } else {
    utils.log("Failed: ${lastError()}");
  }
}

// Pipe chains — stdout flows between commands, short-circuits on failure
let errors = $(cat /var/log/syslog) | $(grep error) | $(wc -l);
io.println("Error count: " + errors.stdout);
```

---

## Why Stash?

- **External programs as first-class citizens.** `$(ls -la)` just works — no subprocess imports, no string wrangling. Pipe chains with `|` short-circuit on failure.
- **Real data structures.** Structs, enums, interfaces, and dictionaries let you model your domain instead of juggling parallel arrays and magic strings.
- **C-style syntax.** If you know C, C++, C#, Java, or JavaScript, you can read Stash immediately — braces, semicolons, `if`/`else`/`while`/`for`.
- **Sensible error handling.** `try` catches errors inline, `??` provides fallbacks, and `lastError()` gives you details — no ceremony.
- **Built-in parallelism and async/await.** `task.run(() => work())` spawns isolated parallel tasks with snapshot semantics — no shared-state bugs. `arr.parMap`, `arr.parFilter`, and `arr.parForEach` parallelize data processing in one line. `async fn` and `await` provide non-blocking async programming.
- **Modules.** `import { deploy, Server } from "utils.stash"` — selective imports with module caching and circular dependency detection.
- **Lambdas and switch expressions.** `(x) => x * 2` for inline functions, `value switch { 1 => "one", _ => "other" }` for concise multi-way branching.
- **Interfaces and type safety.** Define contracts with `interface`, check types with `is`, and extend types with `extend` blocks.
- **Rich literal types.** Durations (`5s`, `100ms`), byte sizes (`1.5GB`, `512KB`), semver (`v1.2.3`), IP addresses (`192.168.1.0/24`), and hex/octal/binary numbers (`0xFF`, `0o755`, `0b1010`).
- **System administration built-in.** SSH/SFTP for remote ops, `elevate` blocks for privilege escalation, `retry` blocks for transient failures, signal handling, file watching, and permissions management.

---

## Quick Tour

#### Variables & Types

```stash
let name = "deploy";          // mutable
const MAX: int = 3;           // immutable, optional type hint
let pending;                  // null until assigned
```

#### Structs & Enums

```stash
enum Status { Active, Inactive, Pending }

struct Server {
    host: string,
    port: int,
    status: Status
}

let srv = Server { host: "10.0.0.1", port: 22, status: Status.Active };
```

#### Command Execution

```stash
let result = $(ping -c 1 ${srv.host});
io.println(result.stdout);    // captured stdout
io.println(result.exitCode);  // 0 on success

// Pipe chains — short-circuit on failure
let count = $(cat /var/log/syslog) | $(grep error) | $(wc -l);
```

#### Error Handling

```stash
let content = try fs.readFile("/etc/missing.conf") ?? "fallback config";
```

#### Functions & Lambdas

```stash
fn add(a: int, b: int) -> int {
    return a + b;
}

let double = (x) => x * 2;
let result = arr.map([1, 2, 3], double);  // [2, 4, 6]
```

#### Parallelism

```stash
// Spawn isolated parallel tasks
let h1 = task.run(() => crypto.sha256("file1"));
let h2 = task.run(() => crypto.sha256("file2"));
let results = task.awaitAll([h1, h2]);

// Parallel data processing
let hashes = arr.parMap(files, (f) => crypto.sha256(f));
```

#### Switch Expressions

```stash
let label = srv.status switch {
    Status.Active   => "running",
    Status.Inactive => "stopped",
    _               => "unknown"
};
```

#### Imports

```stash
import { deploy, Server } from "utils.stash"; // Individual components
import "lib/utils.stash" as utils;            // Entire script, aliased to a namespace
import $"{env.home()}/config.stash";          // Dynamic import paths
```

#### Interfaces

Define contracts with `interface` and check structural conformance with `is`.

```stash
interface Deployable {
  fn deploy(target: string) -> bool;
  fn rollback();
}

struct App : Deployable {
  name: string,
  version: string

  fn deploy(target: string) -> bool {
    let result = $(scp ${self.name}.tar.gz ${target}:/opt/);
    return result.exitCode == 0;
  }

  fn rollback() {
    io.println("Rolling back " + self.name);
  }
}

let app = App { name: "web-api", version: "2.1.0" };
if (app is Deployable) {
  app.deploy("prod-server");
}
```

#### Try / Catch / Finally

```stash
try {
  let data = fs.readFile("/etc/app.conf");
  let config = json.parse(data);
} catch (e) {
  log.error("Config failed: " + e.message);
} finally {
  log.info("Config loading complete");
}
```

#### Async / Await

```stash
async fn fetchStatus(host: string) -> string {
  let result = $(curl -s ${host}/health);
  return result.stdout;
}

let status = await fetchStatus("https://api.example.com");
```

#### Spread & Rest Parameters

```stash
fn log(level: string, ...messages) {
  for (let msg in messages) {
    io.println($"[{level}] {msg}");
  }
}

log("INFO", "Starting", "server", "on port 8080");

let base = [1, 2, 3];
let extended = [...base, 4, 5];  // [1, 2, 3, 4, 5]
```

#### Rich Literals

```stash
let timeout = 30s;            // Duration
let maxSize = 1.5GB;          // ByteSize
let appVersion = v2.1.0;      // SemVer
let subnet = 192.168.1.0/24;  // IP Address
let perms = 0o755;             // Octal number
let mask = 0xFF00;             // Hex number
let flags = 0b1010_0101;       // Binary with separators
```

#### Retry Blocks

```stash
retry (maxAttempts: 3, delay: 2s, backoff: "exponential") {
  $!(curl -f https://api.example.com/deploy);
}
```

#### Elevate Blocks

Escalate privileges for a scoped block — uses `sudo` on Linux/macOS and `runas` on Windows.

```stash
elevate {
  $(systemctl restart nginx);
  fs.writeFile("/etc/nginx/nginx.conf", newConfig);
}
```

#### Extend Blocks

Add methods to any existing type without inheritance.

```stash
extend string {
  fn shout(self) -> string {
    return str.upper(self) + "!!!";
  }
}

let greeting = "hello";
io.println(greeting.shout());  // "HELLO!!!"
```

#### UFCS (Uniform Function Call Syntax)

Any named function can be called as a method on its first argument.

```stash
fn double(n: int) -> int { return n * 2; }

let result = 5.double();       // Same as double(5) → 10

// Works with stdlib too
let nums = [3, 1, 2];
let sorted = nums.sort();  // Same as arr.sort(nums)
```

#### File Watching

```stash
fs.watch("/etc/nginx/", (event) => {
  io.println($"File {event.path} was {event.type}");
  $(nginx -t && nginx -s reload);
});
```

#### Signal Handling

```stash
process.onSignal(process.SIGTERM, () => {
  io.println("Shutting down gracefully...");
  cleanup();
  process.exit(0);
});
```

---

## Getting Started

```bash
dotnet build                                         # Build
dotnet run --project Stash.Cli/                      # Start the REPL
dotnet run --project Stash.Cli/ -- script.stash      # Run a script
dotnet run --project Stash.Cli/ -- -c 'code'         # Run inline code
echo 'code' | dotnet run --project Stash.Cli/        # Pipe from stdin
dotnet test                                          # Run the test suite
stash-check .                                        # Lint all .stash files
stash-format --check .                               # Check formatting
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

Stash ships with a full toolchain — no plugins or third-party tools required:

- **Language Server (LSP)** — completions, hover, diagnostics, go-to-definition, rename, semantic token highlighting, workspace-level indexing, type narrowing with `is`, UFCS support, and range/on-type formatting for any LSP-compatible editor. Full analysis pipeline averages **1.2ms** per document change across real-world scripts. See [docs/LSP — Language Server Protocol.md](docs/LSP%20—%20Language%20Server%20Protocol.md).
- **Debug Adapter (DAP)** — breakpoints, stepping, variable inspection for VS Code and other DAP clients. See [docs/DAP — Debug Adapter Protocol.md](docs/DAP%20—%20Debug%20Adapter%20Protocol.md).
- **Built-in Test Runner** — `test()`, `describe()`, `assert.*` with TAP output. No external framework needed. See [docs/TAP — Testing Infrastructure.md](docs/TAP%20—%20Testing%20Infrastructure.md).
- **CLI Debugger** — built-in step debugger with breakpoints, call stack inspection, and variable printing. Run any script with `--debug`.
- **Templating Engine** — Jinja2-style templates with variable interpolation, filters, conditionals, loops, includes, and whitespace control via the `tpl` namespace. See [docs/TPL — Templating Engine.md](docs/TPL%20—%20Templating%20Engine.md).
- **Static Analysis CLI (`stash-check`)** — Standalone linter with SARIF output for CI/CD pipelines. Runs the full analysis engine without requiring the interpreter or LSP.
- **Code Formatter (`stash-format`)** — Standalone formatter with `--write`, `--check`, and `--diff` modes. Enforce consistent style in CI or format on save.
- **Browser Playground** — Interactive WASM-based playground with Monaco editor for trying Stash in the browser. No installation required. See [docs/Playground — Browser Playground.md](docs/Playground%20—%20Browser%20Playground.md).

---

## Package Management

Stash includes a built-in package manager for sharing and reusing code. Packages are defined by a `stash.json` manifest, locked with `stash-lock.json`, and installed into a `stashes/` directory.

```bash
stash pkg init                          # Create a new stash.json
stash pkg install http-client@1.2.0     # Add and install a dependency
stash pkg install                       # Install all dependencies from stash.json
stash pkg update                        # Re-resolve to latest matching versions
stash pkg search http-client            # Search a registry for packages
stash pkg publish                       # Publish the current package
stash pkg login --registry <url>        # Authenticate with a registry
```

Dependencies support semver constraints (`^1.2.0`, `~1.0.0`, `*`) and Git sources (`git:https://github.com/user/repo.git#v1.0.0`).

All registry commands accept `--registry <url>` to target a specific registry. Without it, the CLI uses the default registry from `~/.stash/config.json` (set automatically on first `stash pkg login`).

---

## Package Registry

Stash ships with a self-hosted package registry server for teams and organizations. Run your own registry with zero external dependencies — SQLite and local filesystem storage work out of the box.

```bash
dotnet run --project Stash.Registry/    # Start the registry server
```

The registry provides a full REST API for publishing, installing, searching, and managing packages. It includes JWT authentication with scoped tokens, rate limiting, integrity verification, and audit logging. For larger deployments, PostgreSQL and S3 backends are supported.

See [docs/Registry — Package Registry.md](docs/Registry%20—%20Package%20Registry.md) for the full API reference, configuration options, and architecture details.

---

## Embedding

Stash can be embedded into any .NET application as a scripting engine — similar to how Lua is embedded in games. The `Stash.Interpreter` library provides a clean `StashEngine` API:

```csharp
using Stash.Interpreting;

var engine = new StashEngine(StashCapabilities.None); // sandboxed

// Inject host variables and functions
engine.SetGlobal("playerName", "Alice");
engine.SetGlobal("getHealth", engine.CreateFunction("getHealth", 0,
    (args) => 100L));

// Run Stash code
engine.Run("io.println(\"Hello, \" + playerName);");

// Evaluate expressions and read values back
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

Stash's .NET-backed interpreter outperforms Bash across the board. Both languages were benchmarked with equivalent scripts performing the same operations — identical algorithms, identical iteration counts, identical checksums. The numbers represent the median of 3 separate runs for each benchmark.

| Benchmark                 | What it tests                                            |    Stash |      Bash |   Speedup |
| ------------------------- | -------------------------------------------------------- | -------: | --------: | --------: |
| **Algorithms**            | Recursion, sorting, searching, struct usage              | 2,130 ms | 10,610 ms |  **5.0×** |
| **Function Calls**        | Dispatch overhead across 0–4 argument arities            | 1,944 ms |  3,712 ms |  **1.9×** |
| **Expression Throughput** | Dense arithmetic, 70 variables, string interpolation     | 1,375 ms |  4,865 ms |  **3.5×** |
| **Built-in Functions**    | 13 stdlib calls per iteration (math, string, conversion) |   830 ms | 24,000 ms | **28.9×** |
| **Scope Lookup**          | Variable resolution across 5-level nested closures       | 1,599 ms |  3,125 ms |  **2.0×** |

> Measured on the same machine, same workload. Full scripts in [`benchmarks/`](benchmarks/).
>
> Example scripts are in [`examples/`](examples/).

---

## Documentation

| Document                                                                           | Description                                         |
| ---------------------------------------------------------------------------------- | --------------------------------------------------- |
| [Language Specification](docs/Stash%20—%20Language%20Specification.md)             | Complete syntax, type system, and language design   |
| [Standard Library Reference](docs/Stash%20—%20Standard%20Library%20Reference.md)   | All built-in functions and namespaces               |
| [Language Server Protocol](docs/LSP%20—%20Language%20Server%20Protocol.md)         | LSP architecture and supported features             |
| [Debug Adapter Protocol](docs/DAP%20—%20Debug%20Adapter%20Protocol.md)             | DAP implementation and debugging support            |
| [Testing Infrastructure](docs/TAP%20—%20Testing%20Infrastructure.md)               | Built-in test runner and TAP output                 |
| [Templating Engine](docs/TPL%20—%20Templating%20Engine.md)                         | Jinja2-style template rendering and `tpl` namespace |
| [Package Registry](docs/Registry%20—%20Package%20Registry.md)                      | Self-hosted registry server and CLI integration     |
| [Package Manager CLI](docs/PKG%20—%20Package%20Manager%20CLI.md)                   | Package manager commands and manifest format        |
| [Browser Playground](docs/Playground%20—%20Browser%20Playground.md)                | Interactive WASM-based playground                   |

---

## Architecture

```
Stash.Core          → Lexer, Parser, 46 AST node types
Stash.Stdlib        → Standard library metadata registry (30 namespaces)
Stash.Interpreter   → Tree-walk interpreter, environment chain
Stash.Analysis      → Static analysis engine, diagnostics, formatting
Stash.Cli           → REPL + script runner (Native AOT)
Stash.Lsp           → Language Server Protocol
Stash.Dap           → Debug Adapter Protocol
Stash.Tpl           → Templating engine
Stash.Tap           → TAP test framework
Stash.Check         → Static analysis CLI (Native AOT)
Stash.Format        → Code formatter CLI (Native AOT)
Stash.Playground    → Browser playground (Blazor WASM)
Stash.Registry      → Package registry server (ASP.NET Core)
Stash.Tests         → 4,400+ xUnit tests
```

---

## License

GPL-3.0 — see [LICENSE](LICENSE) for details.
