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
let result = $(ping -c 1 {srv.host});
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
- **Real data structures.** Structs, enums, and dictionaries let you model your domain instead of juggling parallel arrays and magic strings.
- **C-style syntax.** If you know C, C++, C#, Java, or JavaScript, you can read Stash immediately — braces, semicolons, `if`/`else`/`while`/`for`.
- **Sensible error handling.** `try` catches errors inline, `??` provides fallbacks, and `lastError()` gives you details — no ceremony.
- **Built-in parallelism.** `task.run(() => work())` spawns isolated parallel tasks with snapshot semantics — no shared-state bugs. `arr.parMap`, `arr.parFilter`, and `arr.parForEach` parallelize data processing in one line.
- **Modules.** `import { deploy, Server } from "utils.stash"` — selective imports with module caching and circular dependency detection.
- **Lambdas and switch expressions.** `(x) => x * 2` for inline functions, `value switch { 1 => "one", _ => "other" }` for concise multi-way branching.

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
let result = $(ping -c 1 {srv.host});
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
import { deploy, Server } from "utils.stash";
import "lib/utils.stash" as utils;
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

- **Language Server (LSP)** — completions, hover, diagnostics, go-to-definition, rename, and formatting for any LSP-compatible editor. Full analysis pipeline averages **1.2ms** per document change across real-world scripts. See [docs/LSP — Language Server Protocol.md](docs/LSP%20—%20Language%20Server%20Protocol.md).
- **Debug Adapter (DAP)** — breakpoints, stepping, variable inspection for VS Code and other DAP clients. See [docs/DAP — Debug Adapter Protocol.md](docs/DAP%20—%20Debug%20Adapter%20Protocol.md).
- **Built-in Test Runner** — `test()`, `describe()`, `assert.*` with TAP output. No external framework needed. See [docs/TAP — Testing Infrastructure.md](docs/TAP%20—%20Testing%20Infrastructure.md).
- **CLI Debugger** — built-in step debugger with breakpoints, call stack inspection, and variable printing. Run any script with `--debug`.
- **Templating Engine** — Jinja2-style templates with variable interpolation, filters, conditionals, loops, includes, and whitespace control via the `tpl` namespace. See [docs/TPL — Templating Engine.md](docs/TPL%20—%20Templating%20Engine.md).

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

| Document                                                                         | Description                                       |
| -------------------------------------------------------------------------------- | ------------------------------------------------- |
| [Language Specification](docs/Stash%20—%20Language%20Specification.md)           | Complete syntax, type system, and language design |
| [Standard Library Reference](docs/Stash%20—%20Standard%20Library%20Reference.md) | All built-in functions and namespaces             |
| [Language Server Protocol](docs/LSP%20—%20Language%20Server%20Protocol.md)       | LSP architecture and supported features           |
| [Debug Adapter Protocol](docs/DAP%20—%20Debug%20Adapter%20Protocol.md)           | DAP implementation and debugging support          |
| [Testing Infrastructure](docs/TAP%20—%20Testing%20Infrastructure.md)             | Built-in test runner and TAP output               |
| [Templating Engine](docs/TPL%20—%20Templating%20Engine.md)                       | Jinja2-style template rendering and `tpl` namespace |
| [Package Registry](docs/Registry%20—%20Package%20Registry.md)                    | Self-hosted registry server and CLI integration     |

---

## License

GPL-3.0 — see [LICENSE](LICENSE) for details.
