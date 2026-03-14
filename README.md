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

If you have ever written Bash beyond a few lines, you know the pain. Variable quoting, no real data structures, arcane syntax for basic operations, and error handling that is an afterthought. Meanwhile, reaching for Python or Ruby for a deployment script means losing the directness of shell commands — everything becomes `subprocess.run()` string wrangling.

**Stash sits in the gap.** It gives you:

- **Shell commands as first-class citizens.** `$(ls -la)` just works. No string wrapping, no subprocess imports. Pipe chains with `|` pass stdout between processes exactly like Bash, but with short-circuit-on-failure semantics built in.
- **Real data structures.** Structs and enums let you model your domain — servers, deploy targets, configurations — instead of juggling parallel arrays and magic strings.
- **C-style syntax.** If you know C, C++, C#, Java, or JavaScript, you can read Stash immediately. Braces, semicolons, `if`/`else`/`while`/`for` — nothing surprising.
- **Sensible error handling.** `try` expressions catch errors inline, `??` provides fallbacks, and `lastError()` gives you the details when you need them. No try/catch ceremony, no Go-style error-value tuples.
- **Modules.** `import { deploy, Server } from "utils.stash";` — selective imports with module caching and circular dependency detection.
- **Lambdas and switch expressions.** `(x) => x * 2` for inline functions, `value switch { 1 => "one", _ => "other" }` for concise multi-way branching — modern expression syntax for scripting.

### Design Philosophy

Stash is **opinionated about simplicity.** Some things are intentionally left out:

| Decision       | Choice                               | Why                                                        |
| -------------- | ------------------------------------ | ---------------------------------------------------------- |
| Typing         | Dynamic with optional type hints     | Appropriate for scripting; add types when you want clarity |
| Syntax         | C-style braces and semicolons        | Familiar to the majority of developers                     |
| Loops          | `for-in` only (no C-style `for(;;)`) | Simpler, covers the scripting use case                     |
| OOP            | Structs without inheritance          | Data modeling without the complexity of class hierarchies  |
| Error handling | `try` expression + `??`              | Lightweight, composable, no exception machinery            |
| Commands       | `$(...)` literals                    | Commands look like commands, not strings                   |

---

## Getting Started

### Building

```bash
dotnet build
```

### Running

```bash
# Start the interactive REPL
dotnet run --project Stash.Interpreter/

# Run a script file
dotnet run --project Stash.Interpreter/ -- script.stash

# Run a script with arguments (everything after the script path is passed to the script)
dotnet run --project Stash.Interpreter/ -- script.stash --verbose --port 8080 target-host

# Run with the CLI debugger
dotnet run --project Stash.Interpreter/ -- --debug script.stash
```

Or make a script directly executable:

```bash
#!/usr/bin/env stash
println("Hello from Stash!");
```

```bash
chmod +x hello.stash
./hello.stash
```

### Running Tests

```bash
dotnet test
```

---

## Language Reference

### Variables & Constants

```stash
let name = "deploy";              // mutable variable
let count: int = 5;               // optional type hint
let verbose = true;
let pending;                      // declared without initializer → null
const MAX_RETRIES: int = 3;       // immutable — reassignment is a runtime error
```

### Types

Stash is dynamically typed. Values carry their type at runtime:

| Type     | Examples                 | Notes                 |
| -------- | ------------------------ | --------------------- |
| `int`    | `42`, `-7`, `0`          | 64-bit integer        |
| `float`  | `3.14`, `-0.5`           | 64-bit floating point |
| `string` | `"hello"`, `""`          | Immutable             |
| `bool`   | `true`, `false`          |                       |
| `null`   | `null`                   | Absence of value      |
| `array`  | `[1, "two", true]`       | Ordered, mixed-type   |
| `struct` | `Server { host: "..." }` | Named structured data |
| `enum`   | `Status.Active`          | Named constants       |

### Operators

Standard C-style: `+`, `-`, `*`, `/`, `%`, `==`, `!=`, `<`, `>`, `<=`, `>=`, `&&`, `||`, `!`, `?:` (ternary), `??` (null-coalescing), `++`, `--`.

### String Interpolation

```stash
let name = "world";
let a = "Hello ${name}";        // embedded syntax
let b = $"Hello {name}";        // prefixed syntax (C#-style)
let c = "Hello " + name;        // concatenation
```

### Functions & Closures

```stash
fn greet(name: string) {
    io.println("Hello, " + name);
}

fn add(a: int, b: int) -> int {
    return a + b;
}

fn makeCounter() {
    let count = 0;
    fn increment() {
        count = count + 1;
        return count;
    }
    return increment;
}

let counter = makeCounter();
io.println(counter());  // 1
io.println(counter());  // 2
```

Functions without an explicit `return` implicitly return `null`. Parameter type hints and return type annotations (`->`) are optional.

### Lambdas

```stash
let double = (x) => x * 2;
let add = (a: int, b: int) => a + b;

// Block body for multi-statement logic
let abs = (x) => {
    if (x < 0) { return -x; }
    return x;
};

// Higher-order usage
fn apply(f, x) { return f(x); }
let result = apply((x) => x * x, 4);  // 16
```

Lambdas capture their enclosing scope (closures) and can be passed as arguments or returned from functions.

### Switch Expressions

```stash
let type = day switch {
    "Saturday" => "weekend",
    "Sunday" => "weekend",
    _ => "weekday"
};

// Works with any type — integers, strings, enums
let label = status switch {
    Status.Active => "running",
    Status.Inactive => "stopped",
    _ => "unknown"
};
```

Arms are tested in order. The `_` discard matches anything (default arm). If no arm matches, a runtime error is raised.

### Structs

```stash
struct Server {
    host: string,
    port: int,
    status
}

let srv = Server { host: "10.0.0.1", port: 22, status: "up" };
io.println(srv.host);      // read
srv.status = "down";       // write
```

### Enums

```stash
enum Status { Active, Inactive, Pending }
enum Color { Red, Green, Blue }

let s = Status.Active;
println(s == Status.Active);     // true
println(s == Color.Red);         // false — different enum types are never equal
```

### Arrays

```stash
let nums = [1, 2, 3];
println(nums[0]);           // 1
nums[2] = 99;               // index assignment
println(len(nums));         // 3
```

### Control Flow

```stash
// If / else
if (condition) {
    // ...
} else if (other) {
    // ...
} else {
    // ...
}

// While loop
while (retries > 0) {
    retries--;
}

// For-in loop (arrays and strings)
for (let item in [1, 2, 3]) {
    println(item);
}

for (let ch in "hello") {
    print(ch);  // h e l l o
}

// Break and continue
while (true) {
    if (done) { break; }
    if (skip) { continue; }
}
```

### Command Execution

Commands are first-class — no string wrapping required:

```stash
let result = $(ls -la /tmp);
println(result.stdout);       // captured standard output
println(result.stderr);       // captured standard error
println(result.exitCode);     // process exit code
```

Dynamic values via interpolation:

```stash
let host = "192.168.1.10";
let r = $(ping -c 1 {host});
```

Pipe chains pass stdout → stdin between commands:

```stash
let lines = $(cat /var/log/syslog) | $(grep error) | $(wc -l);
```

Pipes **short-circuit on failure** — if any command in the chain fails (non-zero exit code), subsequent commands are not executed.

### Output Redirection

Redirect command output to files using Bash-style `>` (write) and `>>` (append) operators:

```stash
// Write stdout to a file (creates or overwrites)
$(ls -la) > "/tmp/listing.txt";

// Append stdout to a file
$(ls -la) >> "/tmp/log.txt";

// Redirect stderr
$(make build) 2> "/tmp/errors.txt";

// Redirect both stdout and stderr
$(make build) &> "/tmp/all_output.txt";

// Works with pipe chains — redirects the final result
$(cat /var/log/syslog) | $(grep error) > "/tmp/filtered.txt";

// Combine multiple redirects (stdout + stderr to separate files)
$(make build) > "/tmp/out.txt" 2> "/tmp/err.txt";
```

| Operator | Stream          | Mode      |
| -------- | --------------- | --------- |
| `>`      | stdout          | Overwrite |
| `>>`     | stdout          | Append    |
| `2>`     | stderr          | Overwrite |
| `2>>`    | stderr          | Append    |
| `&>`     | stdout + stderr | Overwrite |
| `&>>`    | stdout + stderr | Append    |

Redirection still returns a `CommandResult` — `exitCode` is always available. The redirected stream's field will be empty since its content was written to the file.

### Error Handling

By default, runtime errors crash the script with a message. When you _expect_ something might fail, opt in:

```stash
// try catches runtime errors and returns null
let content = try fs.readFile("/etc/missing.conf");

// Combine with ?? for a default value
let content = try fs.readFile("/etc/missing.conf") ?? "fallback config";

// Inspect what went wrong
let data = try conv.toInt("abc");
if (data == null) {
    io.println(lastError());  // "Cannot parse 'abc' as integer"
}
```

Shell commands never crash — they return structured results with `exitCode` and `stderr`, so `try` is not needed for `$(...)`.

### Imports

```stash
import { deploy, Server } from "utils.stash";
import { Status } from "enums.stash";

let srv = Server { host: "10.0.0.1", port: 22, status: Status.Active };
deploy(srv, "app.tar.gz");
```

- Only the names listed in `{ ... }` are imported.
- Each module is executed once and cached.
- Circular imports are detected and rejected.
- Functions, structs, enums, variables, and constants can all be imported.

### Argument Parsing

Define CLI arguments declaratively using the built-in `ArgTree` and `ArgDef` structs and the `parseArgs()` function:

```stash
#!/usr/bin/env stash

let args = parseArgs(ArgTree {
    name: "deploy",
    version: "1.0.0",
    description: "Deployment tool",
    flags: [
        ArgDef { name: "help", short: "h", description: "Show help" },
        ArgDef { name: "verbose", short: "v", description: "Enable verbose output" }
    ],
    options: [
        ArgDef { name: "port", short: "p", type: "int", default: 8080, description: "Port to listen on" }
    ],
    positionals: [
        ArgDef { name: "target", type: "string", required: true, description: "Target host" }
    ],
    commands: [
        ArgDef {
            name: "deploy",
            description: "Deploy the application",
            args: ArgTree {
                flags: [ArgDef { name: "force", short: "f", description: "Force deploy" }],
                options: [ArgDef { name: "timeout", type: "int", default: 30, description: "" }]
            }
        }
    ]
});

// Parsed values available via args.*
if (args.verbose) {
    println($"Deploying to {args.target} on port {args.port}");
}

if (args.command == "deploy") {
    println($"Force: {args.deploy.force}");
    println($"Timeout: {args.deploy.timeout}");
}
```

`ArgTree` and `ArgDef` are built-in struct types — no declaration needed. `parseArgs()` is a built-in function that returns a struct of parsed values. It supports **flags** (boolean switches), **options** (typed values with defaults), **positional arguments**, and **subcommands**. Flags and options accept both long (`--verbose`) and short (`-v`) forms, and options support `--key=value` syntax. A `help` flag automatically generates formatted help text. Required arguments are validated at runtime.

### Built-in Functions

| Function                      | Description                                                                                                       |
| ----------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `io.println(val)`             | Print value + newline                                                                                             |
| `io.print(val)`               | Print value without newline                                                                                       |
| `typeof(val)`                 | Type as string: `"int"`, `"float"`, `"string"`, `"bool"`, `"null"`, `"array"`, `"struct"`, `"enum"`, `"function"` |
| `len(val)`                    | Length of string or array                                                                                         |
| `conv.toStr(val)`             | Convert any value to string                                                                                       |
| `conv.toInt(val)`             | Parse string/number to integer                                                                                    |
| `conv.toFloat(val)`           | Parse string/number to float                                                                                      |
| `fs.readFile(path)`           | Read file contents as string                                                                                      |
| `fs.writeFile(path, content)` | Write string to file                                                                                              |
| `env.get(name)`               | Read environment variable (null if unset)                                                                         |
| `env.set(name, value)`        | Set environment variable                                                                                          |
| `process.exit(code)`          | Terminate with exit code                                                                                          |
| `lastError()`                 | Last error caught by `try`, or null                                                                               |
| `parseArgs(tree)`             | Parse CLI arguments from an `ArgTree` definition                                                                  |

### Debugger

Run any script with the built-in CLI debugger:

```bash
dotnet run --project Stash.Interpreter/ -- --debug script.stash
```

| Command                      | Description                                            |
| ---------------------------- | ------------------------------------------------------ |
| `break <line>` or `b <line>` | Set breakpoint at line (current file)                  |
| `break <file>:<line>`        | Set breakpoint at file and line                        |
| `step` / `s`                 | Step into — pause at next statement                    |
| `next` / `n`                 | Step over — pause at next statement at same call depth |
| `out` / `o`                  | Step out — pause when returning to caller              |
| `continue` / `c`             | Resume execution                                       |
| `print <var>` / `p <var>`    | Inspect a variable                                     |
| `stack` / `bt`               | Print call stack                                       |
| `breakpoints` / `bl`         | List all breakpoints                                   |
| `clear [location]`           | Remove breakpoint(s)                                   |
| `quit` / `q`                 | Exit debugger                                          |

---

## Project Structure

```
Stash.Core/
├── Common/          # Shared types (SourceSpan, DiagnosticError)
├── Lexing/          # Lexer, Token, TokenType
└── Parsing/         # Recursive descent parser
    └── AST/         # All expression and statement node types

Stash.Interpreter/
├── Interpreting/    # Tree-walk interpreter, Environment, runtime types
├── Debugging/       # IDebugger interface, CallFrame, CLI debugger
└── Program.cs       # Entry point (REPL + file execution)

Stash.Lsp/           # Language Server Protocol implementation
├── Analysis/        # Semantic analysis, symbol collection, formatting
└── Handlers/        # LSP request handlers

Stash.Tests/         # xUnit test suite
├── Lexing/          # Lexer tests
├── Parsing/         # Parser tests
├── Interpreting/    # Interpreter + Environment tests
└── Analysis/        # LSP analysis tests
```

## Architecture

```
Source Code → Lexer → Tokens → Parser → AST → Interpreter → Execution
```

The interpreter is a tree-walk evaluator that visits AST nodes directly. The lexer and parser run once per script load; execution is the hot path. A bytecode VM is a potential future optimization if performance demands it.

---

## License

GPL-3.0 — see [LICENSE](LICENSE) for details.
