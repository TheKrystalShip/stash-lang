# Stash

A dynamically typed, interpreted scripting language that combines the **shell scripting power** of Bash, the **syntax familiarity** of C/C++/C#, and the **structured data** capabilities that shell languages have always lacked.

```stash
#!/usr/bin/env stash

import { deploy } from "deploy.stash";

enum Status { Unknown, Active, Inactive }

struct Server { host, port, status }

const DEFAULT_HOST = "192.168.1.10";

// Graceful error handling — try catches runtime errors, ?? provides a fallback
let address = try readFile("/etc/server.conf") ?? DEFAULT_HOST;

let srv = Server { host: address, port: 22, status: Status.Unknown };

// First-class command execution — raw shell commands with interpolation
let result = $(ping -c 1 {srv.host});

// Ternary with enum values
srv.status = result.exitCode == 0 ? Status.Active : Status.Inactive;

fn checkServers(servers, payload) {
    for (let s in servers) {
        if (deploy(s, payload)) {
            println($"Deployed {payload} to {s.host}");
        } else {
            let err = lastError();
            println("Failed: " + err);
        }
    }
}

let servers = [
    Server { host: "10.0.0.1", port: 22, status: Status.Unknown },
    Server { host: "10.0.0.2", port: 22, status: Status.Unknown },
];

checkServers(servers, "app.tar.gz");

// Pipe chains — stdout flows between commands, short-circuits on failure
let errors = $(cat /var/log/syslog) | $(grep error) | $(wc -l);
println("Error count: " + errors.stdout);
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

### Design Philosophy

Stash is **opinionated about simplicity.** Some things are intentionally left out:

| Decision       | Choice                               | Why                                                             |
| -------------- | ------------------------------------ | --------------------------------------------------------------- |
| Typing         | Dynamic                              | Appropriate for scripting; no type annotations to slow you down |
| Syntax         | C-style braces and semicolons        | Familiar to the majority of developers                          |
| Loops          | `for-in` only (no C-style `for(;;)`) | Simpler, covers the scripting use case                          |
| OOP            | Structs without inheritance          | Data modeling without the complexity of class hierarchies       |
| Error handling | `try` expression + `??`              | Lightweight, composable, no exception machinery                 |
| Commands       | `$(...)` literals                    | Commands look like commands, not strings                        |

---

## Getting Started

### Building

```bash
dotnet build Stash/
```

### Running

```bash
# Start the interactive REPL
dotnet run --project Stash/

# Run a script file
dotnet run --project Stash/ -- script.stash

# Run with the CLI debugger
dotnet run --project Stash/ -- --debug script.stash
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
dotnet test Stash.Tests/
```

---

## Language Reference

### Variables & Constants

```stash
let name = "deploy";          // mutable variable
let count = 5;
let verbose = true;
let pending;                  // declared without initializer → null
const MAX_RETRIES = 3;        // immutable — reassignment is a runtime error
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
fn greet(name) {
    println("Hello, " + name);
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
println(counter());  // 1
println(counter());  // 2
```

Functions without an explicit `return` implicitly return `null`.

### Structs

```stash
struct Server {
    host,
    port,
    status
}

let srv = Server { host: "10.0.0.1", port: 22, status: "up" };
println(srv.host);      // read
srv.status = "down";    // write
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

### Error Handling

By default, runtime errors crash the script with a message. When you _expect_ something might fail, opt in:

```stash
// try catches runtime errors and returns null
let content = try readFile("/etc/missing.conf");

// Combine with ?? for a default value
let content = try readFile("/etc/missing.conf") ?? "fallback config";

// Inspect what went wrong
let data = try toInt("abc");
if (data == null) {
    println(lastError());  // "Cannot parse 'abc' as integer"
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

### Built-in Functions

| Function                   | Description                                                                                                       |
| -------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `println(val)`             | Print value + newline                                                                                             |
| `print(val)`               | Print value without newline                                                                                       |
| `typeof(val)`              | Type as string: `"int"`, `"float"`, `"string"`, `"bool"`, `"null"`, `"array"`, `"struct"`, `"enum"`, `"function"` |
| `len(val)`                 | Length of string or array                                                                                         |
| `toStr(val)`               | Convert any value to string                                                                                       |
| `toInt(val)`               | Parse string/number to integer                                                                                    |
| `toFloat(val)`             | Parse string/number to float                                                                                      |
| `readFile(path)`           | Read file contents as string                                                                                      |
| `writeFile(path, content)` | Write string to file                                                                                              |
| `exit(code)`               | Terminate with exit code                                                                                          |
| `lastError()`              | Last error caught by `try`, or null                                                                               |
| `env(name)`                | Read environment variable                                                                                         |
| `setEnv(name, value)`      | Set environment variable                                                                                          |

### Debugger

Run any script with the built-in CLI debugger:

```bash
dotnet run --project Stash/ -- --debug script.stash
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
Stash/
├── Common/          # Shared types (SourceSpan)
├── Lexing/          # Lexer, Token, TokenType
├── Parsing/         # Recursive descent parser
│   └── AST/         # All expression and statement node types
├── Interpreting/    # Tree-walk interpreter, Environment, runtime types
├── Debugging/       # IDebugger interface, CallFrame, CLI debugger
└── Program.cs       # Entry point (REPL + file execution)

Stash.Tests/         # xUnit test suite
├── Lexing/          # Lexer tests
├── Parsing/         # Parser tests
└── Interpreting/    # Interpreter + Environment tests
```

## Architecture

```
Source Code → Lexer → Tokens → Parser → AST → Interpreter → Execution
```

The interpreter is a tree-walk evaluator that visits AST nodes directly. The lexer and parser run once per script load; execution is the hot path. A bytecode VM is a potential future optimization if performance demands it.

---

## License

GPL-3.0 — see [LICENSE](LICENSE) for details.
