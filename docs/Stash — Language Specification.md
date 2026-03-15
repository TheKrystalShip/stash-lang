# Stash — Language Specification

> **Status:** Draft v0.1
> **Created:** March 2026
> **Purpose:** Source of truth for the design and implementation of **Stash**, a C-style interpreted shell scripting language.

---

## Table of Contents

1. [Vision & Goals](#1-vision--goals)
2. [Language Design Decisions](#2-language-design-decisions)
3. [Syntax Overview](#3-syntax-overview)
4. [Type System](#4-type-system)
5. [Structs & Objects](#5-structs--objects)
6. [Shell Integration](#6-shell-integration)
7. [Control Flow](#7-control-flow)
8. [Functions](#8-functions)
9. [Scoping Rules](#9-scoping-rules)
10. [Interpreter Architecture](#10-interpreter-architecture)
11. [Debugging Support](#11-debugging-support)
12. [Performance Strategy](#12-performance-strategy)
13. [Implementation Roadmap](#13-implementation-roadmap)
14. [References & Resources](#14-references--resources)

**Addenda:** [5b. Enums](#5b-enums) · [5c. Dictionaries](#5c-dictionaries) · [6b. Shebang Support](#6b-shebang-support) · [6c. Output Redirection](#6c-output-redirection) · [6d. Process Management](#6d-process-management) · [7b. Error Handling](#7b-error-handling) · [7c. Switch Expressions](#7c-switch-expressions) · [8b. Lambda Expressions](#8b-lambda-expressions) · [9b. Module / Import System](#9b-module--import-system) · [9c. Argument Declarations](#9c-argument-declarations) · [9d. Testing Infrastructure](#9d-testing-infrastructure)

---

## 1. Vision & Goals

**Stash** is a **dynamically typed, interpreted scripting language** that combines:

- The **shell scripting power** of Bash (process spawning, pipes, file I/O)
- The **syntax familiarity** of C/C++/C# (braces, semicolons, expressions)
- The **structured data** capabilities missing from Bash (structs/objects)

### Non-Goals (for v1)

- Static typing
- Compilation to native code or bytecode (tree-walk interpreter first)
- Class-based OOP with inheritance
- Concurrency primitives

---

## 2. Language Design Decisions

| Decision            | Choice                           | Rationale                                             |
| ------------------- | -------------------------------- | ----------------------------------------------------- |
| Typing              | Dynamic                          | Simpler to implement; appropriate for scripting       |
| Syntax style        | C-style braces and semicolons    | Familiar to C/C++/C# developers                       |
| Primary focus       | Shell scripting                  | Process execution, pipes, file I/O as first-class     |
| Scoping             | Lexical                          | Predictable; standard in modern languages             |
| Killer feature      | Structs/objects                  | Structured data manipulation missing from Bash        |
| Implementation lang | C#                               | Leverages existing expertise; strong standard library |
| Interpreter type    | Tree-walk (v1), bytecode VM (v2) | Simple first, optimize later                          |

---

## 3. Syntax Overview

### Variables

```c
let name = "deploy";
let count = 5;
let verbose = true;
let pending;              // declared without initializer (value is null)
const MAX_RETRIES = 3;    // constant — cannot be reassigned
```

Variables declared with `let` are **mutable** — they can be reassigned after declaration. Variables declared with `const` are **immutable** — any attempt to reassign a `const` produces a runtime error. `let` without an initializer sets the variable to `null`.

### Operators

Standard C-style: `+`, `-`, `*`, `/`, `%`, `==`, `!=`, `<`, `>`, `<=`, `>=`, `&&`, `||`, `!`, `?:` (ternary), `??` (null-coalescing), `++` (increment), `--` (decrement).

The `++` and `--` operators work on numeric variables, both as prefix and postfix:

```c
let i = 0;
i++;       // postfix: returns 0, then i becomes 1
++i;       // prefix: i becomes 2, then returns 2
i--;       // postfix: returns 2, then i becomes 1
--i;       // prefix: i becomes 0, then returns 0
```

Prefix returns the value **after** the change; postfix returns the value **before** the change. Using `++`/`--` on a non-numeric value produces a runtime error.

### String Interpolation

Both interpolation syntaxes are supported:

```c
let name = "world";
let greeting = "Hello ${name}";      // embedded interpolation
let greeting2 = $"Hello {name}";     // prefixed interpolation (C#-style)
let plain = "Hello " + name;          // concatenation still works
```

Both forms are explicit, intentional, and easy to read. Regular strings (without `$` prefix or `${}` markers) are never interpolated — no surprises.

The lexer treats `$"..."` as a special token type (`InterpolatedString`). Inside `"...${...}..."` strings, the lexer scans for `${` and switches to expression-parsing mode until the matching `}`.

### Comments

```c
// Single-line comment
/* Multi-line
   comment */
```

### Sample Program

```csharp
#!/usr/bin/env stash

// Modular imports
import { test } from "test.stash"

// Enums
enum Status {
  Unknown,
  Active,
  Inactive,
}

// Structs
struct Server {
  host,
  port,
  status,
}

// Constants
const DEFAULT_ADDRESS = "192.168.1.10";

// Try expression
let serverAddress = try fs.readFile("/path/to/addressFile") ?? DEFAULT_ADDRESS;

// Struct type variables
let srv = Server { host: serverAddress, port: 22, status: Status.Unknown };

// Command execution
let result = $(ping -c 1 {srv.host});

// Property assignment
srv.status = result.exitCode == 0 ? Status.Active : Status.Inactive;

// Function definition
fn deploy(server, package) {
  let r = $(scp {package} {server.host}:/opt/);
  return r.exitCode == 0;
}

// Array
let servers = [
  Server { host: "10.0.0.1", port: 22, status: Status.Unknown },
  Server { host: "10.0.0.2", port: 22, status: Status.Unknown }
];

let payload = "app.tar.gz";

// For-in loop
for (let srv in servers) {
  // Conditional
  if (deploy(srv, payload)) {
    io.println("Deployed to " + srv.host);
  } else {
    io.println($"Error deploying {payload} to {srv.host}");
  }
}

// While loop
let index = 0
while (index < 10) {
  index++;
}

// Output redirection — write command output to files
$(ls -la /opt) > "/tmp/listing.txt";
$(make build) 2> "/tmp/errors.log";
$(cat /tmp/listing.txt) | $(grep app) >> "/tmp/matches.txt";
```

---

## 4. Type System

Dynamically typed. Values carry their type at runtime. The following built-in types exist:

| Type     | Examples                       | Notes                                 |
| -------- | ------------------------------ | ------------------------------------- |
| `int`    | `42`, `-7`, `0`                | Integer numbers                       |
| `float`  | `3.14`, `-0.5`                 | Floating-point numbers                |
| `string` | `"hello"`, `""`                | Immutable strings                     |
| `bool`   | `true`, `false`                |                                       |
| `null`   | `null`                         | Absence of value                      |
| `array`  | `[1, 2, 3]`, `["a", 42, true]` | Ordered, mixed-type, dynamic-size     |
| `struct` | `Server { host: "...", ... }`  | Named structured data (see Section 5) |
| `enum`   | `Status.Active`, `Color.Red`   | Named constants (see Section 5b)      |
| `dict`   | `dict.new()`                   | Key-value map (see Section 5c)        |

### Type Coercion & Truthiness

**Truthiness:** The following values are **falsy**: `false`, `null`, `0` (integer zero), `0.0` (float zero), `""` (empty string). All other values are **truthy** (including empty arrays and struct instances).

**String concatenation (`+`):** When one operand of `+` is a string, the other operand is automatically converted to its string representation. `"count: " + 5` produces `"count: 5"`.

**Numeric type mixing:** When an `int` and a `float` are used in an arithmetic operation (`+`, `-`, `*`, `/`, `%`), the `int` is promoted to `float` and the result is a `float`. `5 + 3.14` produces `8.14`.

**Equality:** `==` and `!=` never perform type coercion. Values of different types are never equal (`5 != "5"`, `0 != false`, `0 != null`). Enum values are compared by identity (type + member name).

---

## 5. Structs & Objects

### Declaration

```c
struct Server {
    host,
    port,
    status
}
```

A `struct` declaration registers a **template** — a name and a list of field names.

### Instantiation

```c
let srv = Server { host: "10.0.0.1", port: 22, status: "unknown" };
```

Creates a new instance with the given field values.

### Field Access

```c
let h = srv.host;       // read
srv.status = "up";       // write
```

Dot access is a dictionary lookup internally.

**Note:** The dot operator (`.`) is parsed uniformly for both struct field access (`srv.host`) and enum member access (`Status.Active`). The parser produces a `DotExpr` in both cases. The resolver or interpreter determines at runtime whether the left-hand side is a struct instance (field lookup) or an enum type name (member lookup).

### Internal Representation

A struct instance is a **dictionary/hash map with a type tag**:

```
{ __type: "Server", host: "10.0.0.1", port: 22, status: "unknown" }
```

### Future Extensions (Not in v1)

- **Methods:** Functions defined inside a struct that receive an implicit `self` parameter.
- **Nested structs:** Structs as field values of other structs.
- **Default values:** Field declarations with default values.

---

## 5b. Enums

Enums provide named constants that eliminate magic strings and arbitrary integer values, making code self-documenting.

### Declaration

```c
enum Status {
    Active,
    Inactive,
    Pending
}

enum Color {
    Red,
    Green,
    Blue
}
```

### Usage

```c
let current = Status.Active;

if (current == Status.Pending) {
    io.println("Still waiting...");
}
```

### Comparison & Equality

Enum values are compared by identity — `Status.Active == Status.Active` is `true`, `Status.Active == Status.Inactive` is `false`. Enum values from different enum types are never equal (`Status.Active != Color.Red` even if both are the "first" member).

### Internal Representation

An enum value is stored as a pair: `(typeName, memberName)`. Dot access on the enum type name returns the corresponding value. The backing representation is opaque to the user — no integer mapping is exposed.

### Future Extensions (Not in v1)

- **Enum with associated values:** `enum Result { Ok(value), Err(message) }` — algebraic data types.
- **Iteration:** `for (let s in Status) { ... }` — iterating over all members.
- **String conversion:** `conv.toStr(Status.Active)` → `"Active"`.

---

## 5c. Dictionaries

Dictionaries provide dynamic key-value mappings — the complement to arrays for keyed lookups. While structs offer fixed-schema structured data, dictionaries allow keys to be added and removed at runtime.

### Creation

Dictionaries are created via the `dict` namespace:

```c
let d = dict.new();       // empty dictionary
d["name"] = "Alice";       // set via index syntax
d["age"] = 30;
```

### Key Types

Dictionary keys must be **value types**: `string`, `int`, `float`, or `bool`. Using any other type as a key (arrays, structs, functions, `null`) produces a runtime error.

### Access

Dictionaries support index syntax (`d[key]`) for both reading and writing:

```c
let d = dict.new();

// Write
d["host"] = "10.0.0.1";
d["port"] = 8080;
d[42] = "answer";

// Read — returns null for missing keys
let host = d["host"];       // "10.0.0.1"
let missing = d["nope"];    // null

// Check existence
dict.has(d, "host");        // true
dict.has(d, "nope");        // false
```

### Iteration

Dictionaries are iterable — `for-in` iterates over keys:

```c
let config = dict.new();
config["host"] = "localhost";
config["port"] = 8080;

for (let key in config) {
    io.println(key + " = " + config[key]);
}
```

### Built-in Integration

```c
typeof(dict.new())    // "dict"
len(d)                // number of key-value pairs
```

### Internal Representation

A dictionary is backed by a hash map (`Dictionary<object, object?>` in C#). Key lookup is O(1) average. The `dict` namespace provides all manipulation functions (see Section 8).

---

## 6. Shell Integration

### Process Execution

Commands are executed via **command literals** — a dedicated syntax that makes shell commands first-class in the language without wrapping them in strings.

#### Syntax: `$(command)` — Command Literals

```c
let result = $(ls -la);
io.println(result.stdout);      // captured standard output
io.println(result.stderr);      // captured standard error
io.println(result.exitCode);    // process exit code
```

`$(...)` is **always raw mode**. When the lexer encounters `$(`, it enters "command mode" and collects everything as raw text until the matching `)`. The content is not parsed as a Stash expression — it is treated as a command string that is split into a program name and arguments. Programs are invoked directly, not through a system shell.

To inject dynamic values into a command, use interpolation with `{...}`:

```c
// Raw mode — command text is written directly
let r1 = $(ls -la);

// Dynamic values via interpolation
let flags = buildFlags();
let r2 = $(ls {flags});

// Full dynamic command — interpolate the entire string
let cmd = "echo hello";
let r3 = $({cmd});
```

This makes `$(...)` the **single, unified way** to execute commands. No separate `exec()` function is needed. The `{...}` interpolation syntax within commands is consistent with how interpolation works elsewhere in the language.

`$(...)` returns a struct-like object with `stdout`, `stderr`, and `exitCode` fields.

#### Interpolation in Commands

Variables and expressions can be embedded using `{...}`:

```c
let host = "192.168.1.10";
let result = $(ping -c 1 {host});

let file = "/var/log/syslog";
let pattern = "error";
let matches = $(grep {pattern} {file});
```

This feels natural — commands read like commands, not like strings, but you still get dynamic values where needed.

#### Comparison With Alternatives

| Syntax        | Example          | Verdict                                                                     |
| ------------- | ---------------- | --------------------------------------------------------------------------- |
| `exec("cmd")` | `exec("ls -la")` | Rejected — commands look like strings, not commands                         |
| `` `cmd` ``   | `` `ls -la` ``   | Viable but conflicts with potential future use of backticks                 |
| `$(cmd)`      | `$(ls -la)`      | **Chosen** — familiar from Bash, always raw mode, `{...}` for interpolation |

Implementation: backed by `System.Diagnostics.Process` in C#.

### Pipes

Chain process outputs using `|` between command literals:

```c
let lines = $(cat /var/log/syslog) | $(grep error) | $(wc -l);
```

The `|` operator is **exclusive to command chaining** — it pipes stdout of the left process to stdin of the right. It is not a general-purpose operator and cannot be used between non-command expressions. For logical OR, use `||`.

#### Short-Circuit on Failure

Pipe chains **short-circuit on failure**. If any command in the chain exits with a non-zero exit code, the remaining commands are not executed. The result of the entire pipe expression is the `CommandResult` of the **failed command** (or the last command if all succeed).

```c
let result = $(cat /var/log/syslog) | $(grep error) | $(wc -l);
// If 'cat' fails (exitCode != 0), 'grep' and 'wc' are never started.
// result.exitCode reflects the failed command's exit code.
// result.stderr contains the failed command's error output.
```

This mirrors Bash's `set -o pipefail` behavior and prevents silent failures in command chains.

#### Output Redirection

Command output can be redirected to files using `>` (write) and `>>` (append). See [Section 6c](#6c-output-redirection) for details.

```c
$(ls -la) > "output.txt";       // write stdout to file
$(ls -la) >> "log.txt";         // append stdout to file
$(make build) 2> "errors.txt";  // stderr to file
$(make build) &> "all.txt";     // both streams to file
```

---

## 6b. Shebang Support

Stash scripts can start with a shebang line for direct execution on Unix systems:

```stash
#!/usr/bin/env stash

let name = "world";
io.println("Hello, " + name);
```

### Implementation

The lexer checks if the first two characters of the source are `#!`. If so, it skips everything until the next newline. The shebang line is never tokenized — it is treated as a comment. This is a one-line check at the start of `ScanTokens()` and has zero impact on the rest of the lexer.

### Usage

```bash
chmod +x script.stash
./script.stash
```

---

## 6c. Output Redirection

Stash supports output redirection operators for writing command output directly to files, mirroring Bash's familiar `>` and `>>` syntax.

### Syntax

```c
// Write stdout to file (creates or overwrites)
$(ls -la) > "output.txt";

// Append stdout to file
$(ls -la) >> "log.txt";

// Works with pipe chains — redirects the final output
$(cat /var/log/syslog) | $(grep error) > "filtered.txt";
$(cat log) | $(grep error) >> "errors.log";

// Interpolated file paths
let logDir = "/var/log";
$(dmesg) > "${logDir}/kernel.txt";

// Stderr redirection
$(make build) 2> "errors.txt";
$(make build) 2>> "errors.txt";

// Both streams to same file
$(make build) &> "all_output.txt";
$(make build) &>> "all_output.txt";

// Both streams to separate files
$(make build) > "stdout.txt" 2> "stderr.txt";
```

### Semantics

1. `>` and `>>` are parsed as **postfix redirection operators** that bind after pipe chains are resolved — redirection applies to the final result of the entire pipe chain.
2. They are **only valid** when the left operand is a `CommandExpr`, `PipeExpr`, or another `RedirectExpr` (for chaining stdout + stderr redirects). Using them after any other expression type is a parse error.
3. The right operand is **any expression that evaluates to a string** (the file path).
4. Redirection is **process-level** — the stream is written directly to the file, not buffered in memory. This handles arbitrarily large outputs efficiently.
5. The expression **still returns a `CommandResult`** struct. The redirected stream's field (`stdout` or `stderr`) will be an empty string since it went to the file. `exitCode` is always available.
6. `>` creates or overwrites the file; `>>` creates or appends to it.

### Stream Selectors

| Operator | Stream | Description                             |
| -------- | ------ | --------------------------------------- |
| `>`      | stdout | Write stdout to file (overwrite)        |
| `>>`     | stdout | Append stdout to file                   |
| `2>`     | stderr | Write stderr to file (overwrite)        |
| `2>>`    | stderr | Append stderr to file                   |
| `&>`     | both   | Write stdout+stderr to file (overwrite) |
| `&>>`    | both   | Append stdout+stderr to file            |

### Parsing

The `>` operator is context-sensitive — it is parsed as **redirection** only when the left operand is a `CommandExpr`, `PipeExpr`, or `RedirectExpr`. In all other contexts, `>` remains the greater-than comparison operator. This is unambiguous because comparing a raw command result with `>` is nonsensical (`$(ls) > 5`), and meaningful comparisons like `$(ls).exitCode > 0` work because `.exitCode` produces a `DotExpr`, not a `CommandExpr`.

The `2>`, `2>>`, `&>`, and `&>>` operators are scanned as distinct tokens by the lexer.

### Implementation

A `RedirectExpr` AST node wraps the command expression:

```
RedirectExpr:
  expression: Expr              // left side (CommandExpr, PipeExpr, or RedirectExpr)
  stream: Stdout | Stderr | All // which stream(s) to redirect
  append: bool                  // true for >>, false for >
  target: Expr                  // right side (evaluates to file path string)
```

At runtime, the interpreter executes the inner command and writes the selected stream(s) to the target file. The `CommandResult` is returned with empty strings for redirected streams.

---

## 6d. Process Management

Stash provides built-in process management through the `process` namespace, enabling scripts to spawn background processes, track their lifecycle, communicate with them, and control their termination. This goes beyond the synchronous `$(...)` command execution to support long-running services, parallel workloads, and process orchestration.

### Philosophy

Synchronous command execution via `$(...)` is the right default — run a command, get the result. But scripting often requires launching a process that runs alongside the script: a development server, a file watcher, a background worker. The `process` namespace provides **explicit, tracked** background process management. Every spawned process is tracked by default and cleaned up on script exit unless explicitly detached.

### The `Process` Handle

`Process` is a **built-in struct type** (like `CommandResult`, `ArgTree`, `ArgDef`) that represents a handle to a spawned process. It is returned by `process.spawn()` and accepted by all other process management functions.

#### Fields

| Field     | Type     | Description                          |
| --------- | -------- | ------------------------------------ |
| `pid`     | `int`    | OS process ID                        |
| `command` | `string` | The command string that was launched |

The `pid` and `command` fields are set at spawn time and do not change. To query live state (running/exited), use `process.isAlive()` — this is a function rather than a field because it queries the OS each time.

### Spawning Processes

```c
let server = process.spawn("python3 -m http.server 8080");
io.println("Server PID: " + server.pid);      // e.g. 12345
io.println("Command: " + server.command);      // "python3 -m http.server 8080"
```

`process.spawn(cmd)` launches a process in the background and returns immediately with a `Process` handle. The process runs concurrently with the script. The command string is parsed into a program name and arguments, and the program is invoked directly — no system shell is involved.

The spawned process's stdout and stderr are captured in internal buffers (accessible via `process.read()`), and its stdin is available for writing via `process.write()`.

### Waiting for Processes

```c
// Block until the process exits
let result = process.wait(server);
io.println("Exit code: " + result.exitCode);
io.println("Output: " + result.stdout);
```

`process.wait(proc)` blocks until the process exits and returns a `CommandResult` with `stdout`, `stderr`, and `exitCode` — identical to what `$(...)` returns for synchronous commands.

```c
// Wait with a timeout (milliseconds)
let result = process.waitTimeout(server, 5000);
if (result == null) {
    io.println("Process did not exit within 5 seconds");
    process.kill(server);
}
```

`process.waitTimeout(proc, ms)` waits up to `ms` milliseconds. Returns a `CommandResult` if the process exited in time, or `null` if it is still running.

### Checking Process State

```c
if (process.isAlive(server)) {
    io.println("Server is running");
} else {
    io.println("Server has exited");
}

let pid = process.pid(server);  // same as server.pid
```

`process.isAlive(proc)` returns `true` if the process is still running, `false` if it has exited.

`process.pid(proc)` returns the OS process ID as an integer. This is equivalent to accessing `proc.pid` directly but is provided for consistency with the functional style of the namespace.

### Killing and Signaling Processes

```c
// Send SIGTERM (graceful shutdown)
process.kill(server);

// Send a specific signal
process.signal(server, process.SIGKILL);  // force kill
process.signal(server, process.SIGHUP);   // hangup
```

`process.kill(proc)` sends `SIGTERM` (signal 15) to the process. Returns `true` if the signal was sent, `false` if the process had already exited.

`process.signal(proc, sig)` sends an arbitrary signal. The signal is specified as an integer. Common signal constants are provided on the `process` namespace:

| Constant          | Value | Description             |
| ----------------- | ----- | ----------------------- |
| `process.SIGHUP`  | 1     | Hangup                  |
| `process.SIGINT`  | 2     | Interrupt (Ctrl+C)      |
| `process.SIGQUIT` | 3     | Quit                    |
| `process.SIGKILL` | 9     | Kill (cannot be caught) |
| `process.SIGTERM` | 15    | Terminate (graceful)    |
| `process.SIGUSR1` | 10    | User-defined signal 1   |
| `process.SIGUSR2` | 12    | User-defined signal 2   |

These are integer constants — `process.SIGTERM` is just `15`. Using `process.signal(proc, 15)` is equivalent.

### Process I/O

```c
let proc = process.spawn("bc -l");
process.write(proc, "2 + 3\n");
let answer = process.read(proc);  // "5\n"
process.write(proc, "scale=4; 22/7\n");
let pi = process.read(proc);      // "3.1428\n"
process.kill(proc);
```

`process.write(proc, data)` writes a string to the process's stdin. Returns `true` if the write succeeded, `false` if the process has exited or stdin is closed.

`process.read(proc)` reads currently available stdout from the process. Returns a string with the available data, or `null` if no data is available. This is **non-blocking** — it returns immediately with whatever is in the buffer.

### Detaching Processes

```c
let daemon = process.spawn("my-daemon --config /etc/app.conf");
process.detach(daemon);
// daemon now survives script exit
// the Process handle becomes inert — further calls on it are no-ops
```

`process.detach(proc)` removes a process from the tracked process list. After detaching:

- The process will **not** be killed when the script exits.
- `process.isAlive()`, `process.kill()`, `process.signal()`, `process.wait()`, `process.read()`, and `process.write()` on the detached handle return `false`/`null` as appropriate without error.
- The `pid` and `command` fields remain accessible on the handle.

This is the mechanism for launching daemons or long-lived services that should outlive the script.

### Listing Tracked Processes

```c
let procs = process.list();
for (let p in procs) {
    io.println(p.command + " (PID: " + p.pid + ") alive=" + process.isAlive(p));
}
```

`process.list()` returns an array of all currently tracked `Process` handles (spawned and not yet detached). This is useful for cleanup, monitoring, and debugging.

### Script Exit Cleanup

When a Stash script exits (normally or due to an error), all **tracked** processes receive `SIGTERM`. This prevents orphaned processes from accumulating. The cleanup sequence:

1. Send `SIGTERM` to all tracked processes that are still alive.
2. Wait up to 3 seconds for each process to exit gracefully.
3. Send `SIGKILL` to any process that is still alive after the grace period.

Processes that have been `detach()`-ed are excluded from cleanup.

`process.exit(code)` also triggers this cleanup before terminating the script.

### Complete Example

```c
#!/usr/bin/env stash

// Launch a web server in the background
let server = process.spawn("python3 -m http.server 8080");
io.println("Started server (PID: " + server.pid + ")");

// Give it a moment to start
$(sleep 1);

// Health check
let health = $(curl -s -o /dev/null -w "%{http_code}" http://localhost:8080/);
if (health.stdout == "200") {
    io.println("Server is healthy");
} else {
    io.println("Server failed to start");
    process.exit(1);
}

// Run some tests against the server
let testResult = $(curl -s http://localhost:8080/);
io.println("Response length: " + len(testResult.stdout));

// Graceful shutdown
process.kill(server);
let finalResult = process.waitTimeout(server, 5000);
if (finalResult == null) {
    process.signal(server, process.SIGKILL);
    process.wait(server);
}

io.println("Server stopped");
```

### Implementation

The interpreter maintains a **tracked process list** (`List<TrackedProcess>`) on the interpreter instance. Each entry pairs a `StashInstance` (the `Process` handle) with the underlying `System.Diagnostics.Process` object.

- `process.spawn()` — Creates a `Process` via `System.Diagnostics.Process.Start()` without calling `WaitForExit()`. stdout/stderr are read asynchronously into `StringBuilder` buffers. The handle is added to the tracked list.
- `process.wait()` — Calls `WaitForExit()` on the underlying process, then returns a `CommandResult` with captured output.
- `process.waitTimeout()` — Calls `WaitForExit(timeout)`. Returns `null` if timed out.
- `process.kill()` — Calls `Process.Kill()` (sends SIGTERM on Linux via .NET).
- `process.signal()` — On Unix, sends signals via the POSIX `kill()` syscall. On Windows, maps common signals (SIGTERM, SIGKILL) to `Process.Kill()`.
- `process.isAlive()` — Checks `Process.HasExited`.
- `process.read()` — Returns the contents of the stdout buffer and clears it.
- `process.write()` — Writes to `Process.StandardInput`.
- `process.detach()` — Removes the entry from the tracked list.
- Script exit cleanup — A shutdown hook iterates the tracked list and sends SIGTERM, waits, then SIGKILL.

The `Process` built-in struct is pre-defined by the interpreter alongside `CommandResult`, `ArgTree`, and `ArgDef`.

### Future Extensions (Not in v1)

- **`process.onExit(proc, callback)`** — Register a lambda callback for when a process exits. Event-driven model.
- **`process.daemonize(cmd)`** — Launch as a proper daemon (double-fork, detach from terminal, redirect to `/dev/null`).
- **`process.find(name)`** — Find system processes by name (wraps `pgrep`).
- **`process.exists(pid)`** — Check if an arbitrary system process exists by PID.
- **`process.waitAll(procs)`** — Wait for multiple processes to all exit.
- **`process.waitAny(procs)`** — Wait for the first of multiple processes to exit.

---

## 7. Control Flow

### If / Else

```c
if (condition) {
    // ...
} else if (other) {
    // ...
} else {
    // ...
}
```

### While Loop

```c
while (condition) {
    // ...
}
```

### For Loop

```c
for (let item in collection) {
    // ...
}
```

Only the `for-in` form is supported. C-style `for (init; condition; increment)` is intentionally excluded — it adds complexity without significant benefit for a scripting language. May be reconsidered in a future version.

#### Iterable Types

- **`array`** — iterates over elements in order: `for (let item in [1, 2, 3]) { ... }`
- **`string`** — iterates over characters: `for (let ch in "hello") { ... }` yields `"h"`, `"e"`, `"l"`, `"l"`, `"o"`
- **`dict`** — iterates over keys: `for (let key in myDict) { ... }`

All other types produce a runtime error when used as the right-hand side of `for-in`. Iteration over struct fields and enum members may be added in a future version.

### Break / Continue

Standard `break` and `continue` within loops.

### Null-Coalescing Operator (`??`)

The `??` operator returns the left operand if it is not `null`, otherwise returns the right operand:

```c
let name = inputName ?? "default";
let config = try fs.readFile("/etc/app.conf") ?? "fallback config";
```

This is the same semantics as C#'s `??` operator. The right operand is only evaluated if the left operand is `null` (short-circuit evaluation).

---

## 7b. Error Handling

Stash uses a **`try` expression** model — lightweight, no exception machinery, no Go-style verbosity.

### Philosophy

By default, runtime errors **crash the script** with a stack trace. This is the right behavior for most scripting — fail loudly, fix the problem. When you _expect_ an operation might fail, you opt in to error handling with `try`.

### The `try` Expression

`try` is a **prefix expression** that wraps any expression. If the wrapped expression produces a runtime error, `try` catches it and returns `null` instead of crashing.

```c
// Without try — script crashes if file doesn't exist
let content = fs.readFile("/etc/missing.conf");

// With try — error becomes null
let content = try fs.readFile("/etc/missing.conf");

// With try + ?? — error becomes a default value
let content = try fs.readFile("/etc/missing.conf") ?? "default config";
```

### Error Details

When you need to know _what_ went wrong, the `lastError()` built-in returns the **most recent** error message as a string (or `null` if no error occurred):

```c
let data = try conv.toInt("abc");
if (data == null) {
    io.println(lastError());  // "Cannot parse 'abc' as integer"
}
```

**Note:** `lastError()` returns only the single most recent error. If multiple `try` expressions execute in sequence, only the last error is retained. This is a known limitation — sufficient for v1 scripting use cases.

### Shell Commands Don't Need `try`

Shell command results already carry structured error information via `exitCode` and `stderr` — they never crash the script:

```c
let result = $(ping -c 1 {host});
if (result.exitCode != 0) {
    io.println("Host unreachable: " + result.stderr);
}
```

### Implementation

`try` is a single AST node (`TryExpr`) wrapping another expression. The interpreter catches its own `RuntimeError` when evaluating the inner expression and returns `null`. The caught error message is stored for `lastError()`. When no `try` is present, errors propagate normally and crash with a stack trace.

### Comparison With Alternatives

| Approach         | Verdict                                                                                 |
| ---------------- | --------------------------------------------------------------------------------------- |
| Try/catch blocks | Rejected — requires exception machinery; overkill for a scripting language              |
| Go-style results | Rejected — too verbose; error check after every operation                               |
| `try` expression | **Chosen** — lightweight, opt-in, composes with `??`, minimal implementation complexity |

---

## 7c. Switch Expressions

Switch expressions provide concise multi-way branching based on value matching. Inspired by C#'s switch expressions, they evaluate the subject once and compare it against each arm's pattern in order.

### Syntax

```c
let result = value switch {
    pattern1 => result1,
    pattern2 => result2,
    _ => defaultResult
};
```

### Examples

```c
let day = "Monday";
let type = day switch {
    "Saturday" => "weekend",
    "Sunday" => "weekend",
    _ => "weekday"
};
io.println(type);  // "weekday"
```

```c
let status = exitCode switch {
    0 => "success",
    1 => "warning",
    2 => "error",
    _ => "unknown"
};
```

Switch expressions work with any value type — integers, strings, booleans, null, and enum values:

```c
let label = status switch {
    Status.Active => "running",
    Status.Inactive => "stopped",
    Status.Pending => "waiting",
    _ => "unknown"
};
```

### Semantics

1. The subject expression is evaluated **once**.
2. Arms are tested **in order** — the first matching arm wins.
3. Patterns are compared using **value equality** (`==` semantics, no type coercion).
4. Only the matched arm's body expression is evaluated (short-circuit).
5. The `_` discard pattern matches any value and serves as the default arm.
6. If no arm matches and no discard arm is present, a **runtime error** is raised.

### Body Expressions

Each arm's body is a single expression (not a block). Use parentheses for complex expressions if needed:

```c
let score = grade switch {
    "A" => 100,
    "B" => 85,
    "C" => 70,
    _ => 0
};
```

### Trailing Commas

A trailing comma after the last arm is permitted:

```c
let x = val switch {
    1 => "one",
    2 => "two",
    _ => "other",  // trailing comma OK
};
```

### Implementation

A switch expression is parsed as a **postfix operator** on the subject expression, at the same precedence level as `.` (member access), `()` (calls), and `[]` (indexing). The parser produces a `SwitchExpr` AST node containing the subject and a list of `SwitchArm` entries. At runtime, the interpreter evaluates the subject, walks the arms in order, and returns the body of the first arm whose pattern equals the subject.

---

## 8. Functions

### Declaration

```c
fn greet(name) {
    io.println("Hello, " + name);
}

fn add(a, b) {
    return a + b;
}
```

### Implicit Return Value

Functions that do not execute a `return` statement implicitly return `null`:

```c
fn greet(name) {
    io.println("Hello, " + name);
}

let result = greet("world");  // result is null
```

### Closures

Functions capture their enclosing lexical environment:

```c
fn makeCounter() {
    let count = 0;
    fn increment() {
        count = count + 1;
        return count;
    }
    return increment;
}

let counter = makeCounter();
io.println(counter()); // 1
io.println(counter()); // 2
```

### Built-in Functions

| Function       | Description                          |
| -------------- | ------------------------------------ |
| `typeof(val)`  | Return the type of a value as string |
| `len(val)`     | Length of a string or array          |
| `lastError()`  | Last error message (string) or null  |
| `parseArgs(t)` | Parse command-line arguments         |

All other built-in functions are organized into namespaces (see below).

### Built-in Namespaces

Stash organizes built-in functions into **namespaces** accessed via dot notation. A small set of fundamental functions remain global (see above); everything else lives in a namespace.

#### `io` — Standard I/O

| Function          | Description                     |
| ----------------- | ------------------------------- |
| `io.println(val)` | Print value followed by newline |
| `io.print(val)`   | Print value without newline     |

#### `conv` — Type Conversion

| Function            | Description             |
| ------------------- | ----------------------- |
| `conv.toStr(val)`   | Convert value to string |
| `conv.toInt(val)`   | Parse string to integer |
| `conv.toFloat(val)` | Parse string to float   |

#### `env` — Environment Variables

| Function               | Description                               |
| ---------------------- | ----------------------------------------- |
| `env.get(name)`        | Read environment variable (null if unset) |
| `env.set(name, value)` | Set environment variable                  |

#### `process` — Process Control & Management

| Function                        | Description                                                       |
| ------------------------------- | ----------------------------------------------------------------- |
| `process.exit(code)`            | Terminate the script with exit code                               |
| `process.spawn(cmd)`            | Launch a background process, returns a `Process` handle           |
| `process.wait(proc)`            | Block until a process exits, returns `CommandResult`              |
| `process.waitTimeout(proc, ms)` | Wait with timeout; returns `CommandResult` or `null` if timed out |
| `process.kill(proc)`            | Send SIGTERM to a process                                         |
| `process.isAlive(proc)`         | Check if a process is still running (returns `bool`)              |
| `process.signal(proc, sig)`     | Send an arbitrary signal to a process                             |
| `process.pid(proc)`             | Get the OS process ID                                             |
| `process.detach(proc)`          | Detach a process so it survives script exit                       |
| `process.list()`                | List all tracked (spawned) process handles                        |
| `process.read(proc)`            | Read available stdout from a running process (non-blocking)       |
| `process.write(proc, data)`     | Write to a running process's stdin                                |

See [Section 6d](#6d-process-management) for full semantics, the `Process` handle, signal constants, and examples.

#### `fs` — File System Operations

| Function                       | Description                                   |
| ------------------------------ | --------------------------------------------- |
| `fs.readFile(path)`            | Read file contents as string                  |
| `fs.writeFile(path, content)`  | Write string to file (creates or overwrites)  |
| `fs.appendFile(path, content)` | Append string to file                         |
| `fs.exists(path)`              | Check if a file exists (returns boolean)      |
| `fs.dirExists(path)`           | Check if a directory exists (returns boolean) |
| `fs.pathExists(path)`          | Check if a file or directory exists           |
| `fs.createDir(path)`           | Create a directory (including parents)        |
| `fs.delete(path)`              | Delete a file or directory (recursive)        |
| `fs.copy(src, dst)`            | Copy a file (overwrites destination)          |
| `fs.move(src, dst)`            | Move/rename a file (overwrites destination)   |
| `fs.size(path)`                | Get file size in bytes                        |
| `fs.listDir(path)`             | List entries in a directory (returns array)   |

#### `path` — Path Manipulation

| Function          | Description                        |
| ----------------- | ---------------------------------- |
| `path.abs(p)`     | Get absolute path                  |
| `path.dir(p)`     | Get directory portion of path      |
| `path.base(p)`    | Get filename with extension        |
| `path.name(p)`    | Get filename without extension     |
| `path.ext(p)`     | Get file extension (including `.`) |
| `path.join(a, b)` | Join two path segments             |

#### `arr` — Array Operations

All `arr` functions take the target array as the first argument. Functions that mutate the array do so **in-place**.

##### Core Manipulation

| Function                          | Description                                               |
| --------------------------------- | --------------------------------------------------------- |
| `arr.push(array, value)`          | Add value to end of array                                 |
| `arr.pop(array)`                  | Remove and return last element (error if empty)           |
| `arr.peek(array)`                 | Return last element without removing (error if empty)     |
| `arr.insert(array, index, value)` | Insert value at index (shifts elements right)             |
| `arr.removeAt(array, index)`      | Remove and return element at index                        |
| `arr.remove(array, value)`        | Remove first occurrence of value; returns `true` if found |
| `arr.clear(array)`                | Remove all elements                                       |

##### Searching

| Function                     | Description                                            |
| ---------------------------- | ------------------------------------------------------ |
| `arr.contains(array, value)` | Return `true` if value exists in array                 |
| `arr.indexOf(array, value)`  | Return index of first occurrence, or `-1` if not found |

##### Transformation

| Function                       | Description                                                     |
| ------------------------------ | --------------------------------------------------------------- |
| `arr.slice(array, start, end)` | Return new sub-array from start (inclusive) to end (exclusive)  |
| `arr.concat(array1, array2)`   | Return new array combining both arrays                          |
| `arr.join(array, separator)`   | Join elements into a string with separator                      |
| `arr.reverse(array)`           | Reverse array in-place                                          |
| `arr.sort(array)`              | Sort array in-place (numbers and strings; error on mixed types) |

##### Higher-Order Functions

| Function                         | Description                                                   |
| -------------------------------- | ------------------------------------------------------------- |
| `arr.map(array, fn)`             | Return new array with `fn(element)` applied to each element   |
| `arr.filter(array, fn)`          | Return new array of elements where `fn(element)` is truthy    |
| `arr.forEach(array, fn)`         | Call `fn(element)` for each element                           |
| `arr.find(array, fn)`            | Return first element where `fn(element)` is truthy, or `null` |
| `arr.reduce(array, fn, initial)` | Fold array: calls `fn(accumulator, element)` for each element |

##### Examples

```c
let nums = [3, 1, 4, 1, 5];

// Core manipulation
arr.push(nums, 9);              // [3, 1, 4, 1, 5, 9]
let last = arr.pop(nums);       // last = 9, nums = [3, 1, 4, 1, 5]
arr.insert(nums, 0, 0);         // [0, 3, 1, 4, 1, 5]
arr.removeAt(nums, 0);          // [3, 1, 4, 1, 5]

// Searching
arr.contains(nums, 4);          // true
arr.indexOf(nums, 1);           // 1

// Transformation
let sub = arr.slice(nums, 1, 3);    // [1, 4]
let all = arr.concat(nums, [6, 7]); // [3, 1, 4, 1, 5, 6, 7]
arr.sort(nums);                     // [1, 1, 3, 4, 5]
let csv = arr.join(nums, ", ");     // "1, 1, 3, 4, 5"

// Higher-order functions
let doubled = arr.map(nums, (x) => x * 2);      // [2, 2, 6, 8, 10]
let big = arr.filter(nums, (x) => x > 2);        // [3, 4, 5]
let sum = arr.reduce(nums, (acc, x) => acc + x, 0); // 14
let found = arr.find(nums, (x) => x > 3);        // 4
arr.forEach(nums, (x) => io.println(x));          // prints each element
```

#### `dict` — Dictionary Operations

All `dict` functions (except `dict.new` and `dict.merge`) take the target dictionary as the first argument.

| Function                  | Description                                                         |
| ------------------------- | ------------------------------------------------------------------- |
| `dict.new()`              | Create an empty dictionary                                          |
| `dict.get(d, key)`        | Get value for key, or `null` if not found                           |
| `dict.set(d, key, value)` | Set key-value pair (mutates dictionary)                             |
| `dict.has(d, key)`        | Return `true` if key exists                                         |
| `dict.remove(d, key)`     | Remove key; returns `true` if found                                 |
| `dict.clear(d)`           | Remove all entries                                                  |
| `dict.keys(d)`            | Return array of all keys                                            |
| `dict.values(d)`          | Return array of all values                                          |
| `dict.size(d)`            | Return number of entries                                            |
| `dict.pairs(d)`           | Return array of Pair structs (each with `.key` and `.value` fields) |
| `dict.forEach(d, fn)`     | Call `fn(key, value)` for each entry                                |
| `dict.merge(d1, d2)`      | Return new dictionary combining both (d2 wins on key conflicts)     |

##### Index Syntax

Dictionaries also support index access using `d[key]` and `d[key] = value`:

```c
let d = dict.new();
d["name"] = "Alice";
d["age"] = 30;
let name = d["name"];       // "Alice"
let missing = d["nope"];    // null (no error)
```

##### Examples

```c
let config = dict.new();
config["host"] = "localhost";
config["port"] = 8080;
config["debug"] = true;

// Check and retrieve
if (dict.has(config, "host")) {
    io.println("Host: " + config["host"]);
}

// Iteration
for (let key in config) {
    io.println(key + " = " + config[key]);
}

// Pair struct iteration
let pairs = dict.pairs(config);
for (let pair in pairs) {
    io.println(pair.key + " = " + pair.value);
}

// Higher-order usage
dict.forEach(config, (k, v) => {
    io.println(k + " => " + v);
});

// Merging
let defaults = dict.new();
defaults["timeout"] = 30;
defaults["retries"] = 3;

let merged = dict.merge(defaults, config);
// merged has all keys from both; config values take priority
```

Namespace members are accessed with dot notation: `fs.exists("/etc/hosts")`. Namespaces are first-class values — `typeof(fs)` returns `"namespace"`. Assignment to namespace members is not permitted.

Standard library to be expanded as needed.

#### `str` — String Operations

All `str` functions take the target string as the first argument. Strings are immutable — functions return new strings rather than modifying in place.

| Function                        | Description                                                                          |
| ------------------------------- | ------------------------------------------------------------------------------------ |
| `str.upper(s)`                  | Convert to uppercase                                                                 |
| `str.lower(s)`                  | Convert to lowercase                                                                 |
| `str.trim(s)`                   | Remove leading and trailing whitespace                                               |
| `str.trimStart(s)`              | Remove leading whitespace                                                            |
| `str.trimEnd(s)`                | Remove trailing whitespace                                                           |
| `str.contains(s, sub)`          | Return `true` if `s` contains `sub`                                                  |
| `str.startsWith(s, prefix)`     | Return `true` if `s` starts with `prefix`                                            |
| `str.endsWith(s, suffix)`       | Return `true` if `s` ends with `suffix`                                              |
| `str.indexOf(s, sub)`           | Return index of first occurrence of `sub`, or `-1`                                   |
| `str.lastIndexOf(s, sub)`       | Return index of last occurrence of `sub`, or `-1`                                    |
| `str.substring(s, start, end?)` | Extract substring from `start` to `end` (exclusive); `end` defaults to string length |
| `str.replace(s, old, new)`      | Replace first occurrence of `old` with `new`                                         |
| `str.replaceAll(s, old, new)`   | Replace all occurrences of `old` with `new`                                          |
| `str.split(s, delimiter)`       | Split string into array by `delimiter`                                               |
| `str.repeat(s, count)`          | Repeat string `count` times                                                          |
| `str.reverse(s)`                | Reverse the string                                                                   |
| `str.chars(s)`                  | Convert to array of single-character strings                                         |
| `str.padStart(s, len, fill?)`   | Pad start to `len` characters with `fill` (default `" "`)                            |
| `str.padEnd(s, len, fill?)`     | Pad end to `len` characters with `fill` (default `" "`)                              |

##### Examples

```c
let name = "  Hello, World!  ";

// Case conversion
io.println(str.upper(name));           // "  HELLO, WORLD!  "
io.println(str.lower(name));           // "  hello, world!  "

// Trimming
let trimmed = str.trim(name);          // "Hello, World!"

// Search
io.println(str.contains(trimmed, "World"));   // true
io.println(str.indexOf(trimmed, "World"));    // 7

// Extraction & transformation
io.println(str.substring(trimmed, 0, 5));     // "Hello"
io.println(str.replace(trimmed, "World", "Stash")); // "Hello, Stash!"

// Splitting & joining
let parts = str.split("a,b,c", ",");          // ["a", "b", "c"]
let repeated = str.repeat("ab", 3);           // "ababab"

// Padding
io.println(str.padStart("42", 5, "0"));       // "00042"
io.println(str.padEnd("hi", 6));              // "hi    "
```

---

## 8b. Lambda Expressions

Lambda expressions (arrow functions) provide a concise syntax for creating anonymous functions. They are first-class values that can be assigned to variables, passed as arguments, and returned from functions.

### Syntax

**Expression body** — implicit return of a single expression:

```c
let double = (x) => x * 2;
let add = (a, b) => a + b;
let greet = () => "hello";
```

**Block body** — explicit `return` for multi-statement logic:

```c
let abs = (x) => {
    if (x < 0) {
        return -x;
    }
    return x;
};
```

### Parameters

Lambdas support zero or more parameters, with optional type annotations:

```c
let noParams = () => 42;
let oneParam = (x) => x + 1;
let typed = (x: int, y: int) => x + y;
```

### Closures

Lambdas capture their enclosing lexical environment, just like named functions:

```c
fn makeMultiplier(factor) {
    return (x) => x * factor;
}

let triple = makeMultiplier(3);
io.println(triple(5));  // 15
```

### Higher-Order Usage

Lambdas are particularly useful when passed as arguments to other functions:

```c
fn apply(f, x) {
    return f(x);
}

let result = apply((x) => x * x, 4);  // 16
```

### Internal Representation

A lambda expression is evaluated to a `StashLambda` — an `IStashCallable` that stores the parameter list and body along with the closure environment at the point of definition. Expression-body lambdas implicitly return their expression; block-body lambdas require explicit `return` and default to `null`.

---

## 9. Scoping Rules

**Lexical scoping.** A variable is visible in the block where it's declared and all nested blocks.

```c
let x = 10;           // global scope
{
    let y = 20;        // block scope
    io.println(x + y);    // x is visible here (30)
}
// y is NOT visible here
```

### Implementation

A **chain of `Environment` objects**, each with a reference to its parent:

```
Global Env ← Function Env ← Block Env
```

Variable lookup walks up the chain. A resolver pass at parse time binds each variable reference to a (depth, slot) pair for efficient runtime access.

---

## 9b. Module / Import System

Stash supports **selective imports** — you can import specific declarations from another script file rather than sourcing the entire file.

### Syntax

**Selective import** — import specific names into the current scope:

```c
import { deploy, Server } from "utils.stash";
import { Status } from "enums.stash";

// Use imported names directly
let srv = Server { host: "10.0.0.1", port: 22, status: Status.Active };
deploy(srv, "app.tar.gz");
```

Only the names listed in `{ ... }` are made available in the importing script's scope. Other declarations in the imported file are not visible.

**Namespace import** — import an entire module as a namespace:

```c
import "utils.stash" as utils;
import "enums.stash" as enums;

// Access via dot notation
let srv = utils.Server { host: "10.0.0.1", port: 22, status: Status.Active };
utils.deploy(srv, "app.tar.gz");
let status = enums.Status.Active;
```

All top-level declarations from the module are wrapped in a `StashNamespace` object and bound to the given alias. Members are accessed with dot notation. The alias is a regular value — `typeof(utils)` returns `"namespace"`.

### Semantics

1. The interpreter resolves the file path relative to the importing script's directory.
2. If the file has not been imported before, it is **lexed, parsed, and executed** in an isolated module environment.
3. Each imported file is **executed only once** — subsequent imports of the same file reuse the cached module environment (no re-execution).
4. The requested names are looked up in the module's top-level environment. If a name is not found, a runtime error is raised.
5. The resolved values (functions, structs, enums, variables) are bound into the importing script's current scope.

### What Can Be Imported

- Functions (`fn`)
- Struct declarations (`struct`)
- Enum declarations (`enum`)
- Top-level variables (`let`) and constants (`const`)

### Implementation

The interpreter maintains a `Dictionary<string, Environment>` of already-loaded modules (keyed by absolute file path). When an `ImportStmt` is executed:

1. Resolve the file path and check the module cache.
2. If not cached: read the file, lex, parse, resolve, and execute it into a fresh `Environment`. Cache the result.
3. For each name in the import list, look it up in the module's environment and bind it into the current scope.

This is straightforward to implement — no new parsing concepts beyond the `ImportStmt`, and the module execution reuses the entire existing interpreter pipeline.

### Circular Imports

Circular dependencies are **detected and rejected**. During import resolution, the interpreter tracks the set of files currently being loaded (the "import stack"). If a file appears in its own import chain, a compile-time error is raised before execution begins:

```
Error: circular import detected
  a.stash imports b.stash
  b.stash imports a.stash
```

This is checked during the resolve/import phase, not at runtime.

### Future Extensions (Not in v1)

- **Wildcard imports:** `import * from "utils.stash";` — imports everything (bash-style, for convenience).
- **Per-name aliased imports:** `import { deploy as remoteDeploy } from "utils.stash";` — rename individual names on import.
- **Relative path shortcuts:** `import { util } from "./lib/";` — directory-based module resolution.

---

## 9c. Argument Declarations

Stash provides built-in **`ArgTree`** and **`ArgDef`** structs plus a **`parseArgs()`** function for declarative CLI argument parsing. Instead of manually parsing `argv`, scripts construct an `ArgTree` describing expected flags, options, positional arguments, and subcommands, then call `parseArgs()` to get a struct of parsed values. The interpreter handles parsing, validation, type coercion, and help generation automatically.

### Built-in Structs

`ArgTree` and `ArgDef` are pre-defined by the interpreter — scripts use them directly without declaring them.

#### `ArgTree` Fields

| Field         | Type              | Description                                        |
| ------------- | ----------------- | -------------------------------------------------- |
| `name`        | string (optional) | Script name (used in help text and usage line)     |
| `version`     | string (optional) | Version string (used with auto `--version` flag)   |
| `description` | string            | Script description (required, can be `""`)         |
| `flags`       | array (optional)  | Array of `ArgDef` entries for boolean switches     |
| `options`     | array (optional)  | Array of `ArgDef` entries for value-taking options |
| `commands`    | array (optional)  | Array of `ArgDef` entries for subcommands          |
| `positionals` | array (optional)  | Array of `ArgDef` entries for positional arguments |

#### `ArgDef` Fields

| Field         | Type               | Description                                                 |
| ------------- | ------------------ | ----------------------------------------------------------- |
| `name`        | string (required)  | Long name of the argument                                   |
| `short`       | string (optional)  | Single-character short form                                 |
| `type`        | string (optional)  | Type for coercion: `"string"`, `"int"`, `"float"`, `"bool"` |
| `default`     | any (optional)     | Default value (any expression)                              |
| `description` | string             | Description for help text (required, can be `""`)           |
| `required`    | bool (optional)    | Whether the argument must be provided (default: `false`)    |
| `args`        | ArgTree (optional) | Nested `ArgTree` for subcommand flags/options/positionals   |

### Syntax

```c
let args = parseArgs(ArgTree {
    name: "deploy",
    version: "1.0.0",
    description: "A deployment tool",
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
                flags: [ArgDef { name: "force", short: "f", description: "Force deployment" }],
                options: [ArgDef { name: "timeout", type: "int", default: 30, description: "Timeout in seconds" }]
            }
        }
    ]
});
```

`parseArgs()` returns a `StashInstance` bound to the variable; all parsed values are accessed via dot notation.

### Flags

Flags are boolean switches that default to `false` and become `true` when present. Each flag is an `ArgDef` in the `flags` array.

Usage: `--name` or `-short`

**Special flags:**

- A flag named `help` will automatically print formatted help text and exit when `--help` or its short form is passed.
- A flag named `version` will automatically print the `version` metadata value and exit when `--version` is passed (requires `version` to be set on the `ArgTree`).

### Options

Options take a value and support type coercion. Each option is an `ArgDef` in the `options` array.

Usage: `--name value`, `-s value`, or `--name=value`

**Type coercion:**

| Type     | Stash Type | Accepted Values                                    |
| -------- | ---------- | -------------------------------------------------- |
| `string` | string     | Any string (default if no `type` specified)        |
| `int`    | int (long) | Integer strings (e.g., `"42"`, `"-1"`)             |
| `float`  | float      | Decimal strings (e.g., `"3.14"`)                   |
| `bool`   | bool       | `"true"`, `"false"`, `"1"`, `"0"`, `"yes"`, `"no"` |

A runtime error is raised if the value cannot be parsed as the specified type.

### Positional Arguments

Positional arguments are captured in declaration order. Non-flag, non-option, non-command arguments fill positionals sequentially. Each positional is an `ArgDef` in the `positionals` array.

Usage: `./script.stash myhost` — the first non-flag, non-option argument fills the first positional.

### Subcommands

`ArgDef` entries in the `commands` array define named subcommands. An `args` field on the `ArgDef` provides an `ArgTree` describing the subcommand's own flags, options, and positionals.

When a subcommand is matched, `args.command` is set to the command name as a string, and `args.<commandName>` contains the subcommand's parsed values:

```c
if (args.command == "deploy") {
    io.println(args.deploy.force);    // bool
    io.println(args.deploy.timeout);  // int
}
```

### Accessing Parsed Values

All parsed values are accessible via dot notation on the variable returned by `parseArgs()`:

```c
let args = parseArgs(ArgTree {
    flags:     [ArgDef { name: "verbose", short: "v", description: "" }],
    options:   [ArgDef { name: "port", type: "int", default: 8080, description: "" }],
    positionals: [ArgDef { name: "file", required: true, description: "" }]
});

io.println(args.verbose);    // false (or true if --verbose was passed)
io.println(args.port);       // 8080 (or user-provided value)
io.println(args.file);       // first positional argument value
io.println(args.command);    // name of matched subcommand, or null
io.println(args.deploy.force); // subcommand flag value
```

### Validation & Error Handling

`parseArgs()` performs automatic validation:

- **Required options/positionals:** A runtime error is raised if a required argument is not provided.
- **Unknown arguments:** A runtime error is raised for unrecognized flags or options.
- **Type coercion failures:** A runtime error is raised if a value cannot be parsed as the declared type.
- **Missing option values:** A runtime error is raised if an option flag is provided without a corresponding value (e.g., `--port` at the end of the argument list).

### Auto-Generated Help

When a `help` flag is defined and triggered, the interpreter automatically generates formatted help text:

```
my-tool v1.0.0
A deployment tool

USAGE:
  my-tool [command] [options] <target>

COMMANDS:
  deploy    Deploy the application
  rollback  Rollback the deployment

ARGUMENTS:
  <target>  Target host (required)

OPTIONS:
  -v, --verbose        Enable verbose output
  -p, --port <int>     Port to listen on (default: 8080)
  -h, --help           Show help

COMMAND 'deploy':
  -f, --force          Force deployment
      --timeout <int>  Timeout in seconds (default: 30)
```

### Implementation

`parseArgs()` is a built-in function and `ArgTree`/`ArgDef` are built-in struct types pre-defined by the interpreter. At runtime, `parseArgs()` receives an `ArgTree` instance, processes the script's command-line arguments (`_scriptArgs`), performs type coercion, validates required arguments, and returns a `StashInstance` of type `"Args"` with all parsed values bound as fields.

---

## 9d. Testing Infrastructure

Stash provides built-in testing primitives that enable structured, tooling-friendly test execution. Unlike Bash, which lacks any testing support, Stash includes assertion functions and test registration as first-class built-ins, with a pluggable harness that producers TAP-compliant output by default.

### Philosophy

Test scripts are **ordinary Stash scripts**. There is no special file format, no magic — `test()` and `assert.equal()` are regular function calls. The language provides the hooks; users and the ecosystem can build full testing frameworks on top.

By default, runtime errors **crash the script**. The testing harness changes this: when running in test mode (`--test`), assertion failures inside `test()` blocks are **caught and recorded** rather than crashing. Execution continues to the next test, collecting all results.

### Running Tests

```bash
stash --test math_test.stash          # Run tests with TAP output
stash --test tests/                    # Run all .stash files in directory
```

The `--test` flag activates test mode: a `TapReporter` is attached as the test harness, and the interpreter executes the script normally. `test()` calls register and run tests through the harness.

### Test Registration

Tests are registered with the `test()` global function, which takes a name and a lambda:

```c
test("addition works", () => {
    assert.equal(1 + 1, 2);
});

test("string interpolation", () => {
    let name = "world";
    assert.equal("hello ${name}", "hello world");
});
```

Each `test()` call:
1. Notifies the harness that a test is starting.
2. Executes the lambda in an error-catching wrapper.
3. Reports pass or fail to the harness.
4. **Continues execution** — failures do not crash the script.

### Test Grouping

Tests can be grouped with `describe()` for organizational clarity:

```c
describe("math operations", () => {
    test("addition", () => {
        assert.equal(2 + 3, 5);
    });

    test("multiplication", () => {
        assert.equal(3 * 4, 12);
    });
});
```

`describe()` blocks produce hierarchical test names in the output (e.g., `math operations > addition`). Nesting is supported.

### `assert` Namespace

The `assert` namespace provides structured assertion functions. When an assertion fails, it throws an `AssertionError` (a subclass of `RuntimeError`) carrying structured data (expected value, actual value, message, source location).

| Function                        | Description                                                              |
| ------------------------------- | ------------------------------------------------------------------------ |
| `assert.equal(actual, expected)` | Assert `actual == expected` (no type coercion)                          |
| `assert.notEqual(actual, expected)` | Assert `actual != expected`                                          |
| `assert.true(value)`            | Assert that `value` is truthy                                           |
| `assert.false(value)`           | Assert that `value` is falsy                                            |
| `assert.null(value)`            | Assert that `value` is `null`                                           |
| `assert.notNull(value)`         | Assert that `value` is not `null`                                       |
| `assert.greater(a, b)`          | Assert `a > b`                                                          |
| `assert.less(a, b)`             | Assert `a < b`                                                          |
| `assert.throws(fn)`             | Assert that `fn()` throws a runtime error; returns the error message    |
| `assert.fail(message?)`         | Unconditionally fail with an optional message                           |

#### Assertion Error Messages

Assertion failures produce descriptive error messages with source location:

```
assert.equal failed: expected 42 but got 17
  at test_math.stash:14:5
```

When running outside test mode (no `--test` flag), assertion failures are regular `RuntimeError` exceptions — the script crashes on the first failure, just like any other error.

### TAP Output

When running with `--test`, output follows the [Test Anything Protocol (TAP)](https://testanything.org/) version 14:

```
TAP version 14
1..3
ok 1 - math operations > addition
not ok 2 - string equality
  ---
  message: "assert.equal failed: expected \"hello\" but got \"world\""
  severity: fail
  at:
    file: test_strings.stash
    line: 14
    column: 5
  ...
ok 3 - null handling
```

TAP is a well-established protocol supported by CI/CD systems, test runners, and reporting tools.

### Test Harness Architecture

The testing infrastructure mirrors the debugging architecture:

| Concept        | Debugging Parallel       | Testing                   |
| -------------- | ------------------------ | ------------------------- |
| Interface      | `IDebugger`              | `ITestHarness`            |
| Default impl   | `CliDebugger`            | `TapReporter`             |
| CLI flag       | `--debug`                | `--test`                  |
| Null when off  | `_debugger`              | `_testHarness`            |
| Zero overhead  | Null-guarded hooks       | Null-guarded hooks        |
| Protocol adapter | DAP `DebugSession`     | Future test explorer      |

The `ITestHarness` interface defines the contract between the interpreter and any test reporter:

- `OnTestStart(name, span)` — A test case begins.
- `OnTestPass(name, duration)` — A test case passed.
- `OnTestFail(name, message, span, duration)` — A test case failed.
- `OnTestSkip(name, reason)` — A test case was skipped.
- `OnSuiteStart(name)` — A `describe()` group begins.
- `OnSuiteEnd(name, passed, failed, skipped)` — A `describe()` group ends.
- `OnRunComplete(passed, failed, skipped)` — All tests finished.

When no test harness is attached (`_testHarness` is null), all testing hooks are skipped — zero runtime overhead, identical to how `_debugger` works.

### Global Test Functions

| Function                  | Description                                                         |
| ------------------------- | ------------------------------------------------------------------- |
| `test(name, fn)`          | Register and run a test case                                        |
| `describe(name, fn)`      | Group tests under a descriptive name                                |
| `captureOutput(fn)`       | Execute `fn()` with output redirected; returns captured string      |

These are global functions (not namespaced) because they are used at the top level of test scripts, similar to how `typeof()` and `len()` are global.

### Complete Example

```c
#!/usr/bin/env stash

import { deploy, Server } from "deploy.stash";

describe("deployment", () => {
    test("creates server instance", () => {
        let srv = Server { host: "10.0.0.1", port: 22, status: "unknown" };
        assert.equal(srv.host, "10.0.0.1");
        assert.equal(srv.port, 22);
    });

    test("deploy returns boolean", () => {
        let srv = Server { host: "localhost", port: 22, status: "unknown" };
        let result = deploy(srv, "app.tar.gz");
        assert.equal(typeof(result), "bool");
    });

    test("null coalescing works", () => {
        let val = null ?? "default";
        assert.equal(val, "default");
    });

    test("type coercion does not happen", () => {
        assert.notEqual(5, "5");
        assert.notEqual(0, false);
        assert.notEqual(0, null);
    });
});
```

### Output Capture

The `captureOutput()` global function redirects `io.println` and `io.print` output to a string during the execution of a given function, then returns the captured output:

```c
let output = captureOutput(() => {
    io.println("hello");
    io.print("world");
});
assert.equal(output, "hello\nworld");
```

This is the backbone for testing code that produces side-effect output. During capture, the interpreter's output writer is temporarily swapped to a string buffer — output from the captured function is not printed to the console.

#### Output Abstraction

The interpreter provides pluggable output via `TextWriter` properties:

- `Output` — Writer used by `io.println` and `io.print` (defaults to `Console.Out`).
- `ErrorOutput` — Writer for error output (defaults to `Console.Error`).

These abstractions also bridge to the debugger: `io.println` and `io.print` call `IDebugger.OnOutput()` when a debugger is attached, enabling DAP output events in VS Code.

### Implementation

- **`ITestHarness`** — Interface in `Stash.Interpreter/Testing/`, mirroring `IDebugger` in `Stash.Interpreter/Debugging/`.
- **`TapReporter`** — Default implementation producing TAP version 14 output.
- **`AssertionError`** — Subclass of `RuntimeError` carrying `Expected` and `Actual` fields for structured reporting.
- **`TestBuiltIns`** — Registers `test()`, `describe()`, `captureOutput()`, and the `assert` namespace into the interpreter's global environment, following the same pattern as `IoBuiltIns`, `ArrBuiltIns`, etc.
- **`--test` CLI flag** — Attaches a `TapReporter` to the interpreter and sets the exit code based on test results.
- **Output abstraction** — `Interpreter.Output` and `Interpreter.ErrorOutput` (`TextWriter` properties) replace direct `Console` access in `IoBuiltIns`. `IDebugger.OnOutput()` is called from `io.println`/`io.print` when a debugger is attached.

### Future Extensions (Not in v1)

- **`test.skip(name, fn)`** — Skip a test with a reason.
- **`test.only(name, fn)`** — Run only this test (for debugging).
- **`assert.deepEqual(a, b)`** — Deep equality for arrays, dicts, and structs.
- **`assert.closeTo(a, b, delta)`** — Float comparison with tolerance.
- **`--test-format=tap|json|junit`** — Alternate output formats.
- **Test discovery** — `stash --test tests/` runs all `*_test.stash` or `test_*.stash` files in a directory.
- **Setup/teardown** — `beforeEach()`, `afterEach()`, `beforeAll()`, `afterAll()`.
- **IDE integration** — VS Code test explorer adapter (parallel to DAP for debugging).

---

## 10. Interpreter Architecture

### Pipeline

```
Source Code → Lexer → Tokens → Parser → AST → Interpreter → Execution
                                          ↑
                                    Resolver Pass
                                 (variable binding)
```

### Components

| Component       | Responsibility                                         |
| --------------- | ------------------------------------------------------ |
| **Lexer**       | Reads source text, produces stream of tokens           |
| **Parser**      | Recursive descent; consumes tokens, produces AST       |
| **Resolver**    | Post-parse pass; binds variables to scope depth/slot   |
| **Interpreter** | Tree-walk; visits AST nodes and executes them          |
| **Environment** | Stores variable bindings; supports lexical scope chain |
| **REPL**        | Interactive read-eval-print loop                       |

### Token Types

Keywords: `let`, `const`, `fn`, `struct`, `enum`, `if`, `else`, `for`, `in`, `while`, `return`, `break`, `continue`, `true`, `false`, `null`, `try`, `import`, `as`, `switch`

Contextual keywords: `from` (only reserved after `import`, can be used as a variable name elsewhere)

Operators: `+`, `-`, `*`, `/`, `%`, `=`, `==`, `!=`, `<`, `>`, `<=`, `>=`, `&&`, `||`, `!`, `?`, `:`, `??`, `++`, `--`, `=>`, `>>`, `2>`, `2>>`, `&>`, `&>>`

Note: `|` (pipe) is not listed as a general operator — it is a special syntactic form exclusive to command chaining (see Section 6).

Note: `>` and `>>` serve dual roles depending on context. After a command expression (`CommandExpr`, `PipeExpr`, or `RedirectExpr`), they are output redirection operators (see Section 6c). Everywhere else, `>` is the greater-than comparison operator. `>>` is exclusively a redirection operator.

Delimiters: `(`, `)`, `{`, `}`, `[`, `]`, `,`, `.`, `;`

Literals: integer, float, string, interpolated string, command literal `$(...)`

Identifiers: user-defined names

### AST Node Types

**Expressions:**

- `LiteralExpr` — numbers, strings, booleans, null
- `IdentifierExpr` — variable reference
- `BinaryExpr` — `a + b`, `a == b`, etc.
- `UnaryExpr` — `-x`, `!x`
- `PrefixExpr` — `++x`, `--x`
- `PostfixExpr` — `x++`, `x--`
- `CallExpr` — `fn(args)`
- `DotExpr` — `obj.field`
- `AssignExpr` — `x = val`
- `DotAssignExpr` — `obj.field = val`
- `ArrayExpr` — `[1, 2, 3]`
- `IndexExpr` — `arr[i]`
- `IndexAssignExpr` — `arr[i] = val`
- `TernaryExpr` — `cond ? a : b`
- `PipeExpr` — `$(cmd1) | $(cmd2)`
- `RedirectExpr` — `$(cmd) > "file"`, `$(cmd) >> "file"`, `$(cmd) 2> "file"`, `$(cmd) &> "file"`
- `StructInitExpr` — `Server { host: "..." }`
- `CommandExpr` — `$(ls -la)`, `$(grep {pattern} {file})`
- `InterpolatedStringExpr` — `$"Hello {name}"`, `"Hello ${name}"`
- `TryExpr` — `try expr`
- `NullCoalesceExpr` — `a ?? b`
- `SwitchExpr` — `subject switch { pattern => result, ... }`
- `LambdaExpr` — `(params) => expr` or `(params) => { body }`

**Statements:**

- `ExprStmt` — expression as statement
- `VarDeclStmt` — `let x = ...;` or `let x;`
- `ConstDeclStmt` — `const X = ...;`
- `BlockStmt` — `{ ... }`
- `IfStmt` — `if (...) { ... } else { ... }`
- `WhileStmt` — `while (...) { ... }`
- `ForInStmt` — `for (let x in y) { ... }`
- `FnDeclStmt` — `fn name(params) { ... }`
- `ReturnStmt` — `return expr;`
- `BreakStmt` — `break;`
- `ContinueStmt` — `continue;`
- `StructDeclStmt` — `struct Name { fields }`
- `EnumDeclStmt` — `enum Name { Member1, Member2 }`
- `ImportStmt` — `import { name1, name2 } from "file.stash";`
- `ImportAsStmt` — `import "file.stash" as name;`

All AST nodes carry a `SourceSpan` for debugging (see Section 11).

---

## 11. Debugging Support

### Day-One Requirements

Two things must be built into the architecture from the start:

#### 1. Source Location Tracking

Every token and AST node carries a `SourceSpan`:

```csharp
public record SourceSpan(
    string File,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn
);
```

This enables meaningful error messages, stack traces, and future debugger integration.

#### 2. Call Stack

The interpreter maintains a stack of `CallFrame` objects:

```csharp
public class CallFrame
{
    public string FunctionName { get; init; }
    public SourceSpan CallSite { get; init; }
    public Environment LocalScope { get; init; }
}
```

Produces stack traces on error:

```
Error: cannot access field 'port' on null
  at deploy() in scripts/deploy.stash:14:5
  at main() in scripts/main.stash:42:9
```

### Debug Hook Interface

A debug hook in the interpreter's execution loop, called before each statement:

```csharp
public interface IDebugger
{
    void OnBeforeExecute(SourceSpan span, Environment env);
    void OnFunctionEnter(string name, SourceSpan callSite, Environment env);
    void OnFunctionExit(string name);
    void OnError(RuntimeError error, IReadOnlyList<CallFrame> callStack);
}
```

When no debugger is attached, the hook is a null check (zero overhead).

### Debugger Features (Layered)

| Feature            | How It Works                                                      |
| ------------------ | ----------------------------------------------------------------- |
| **Breakpoints**    | Debugger checks if current `SourceSpan.Line` has a breakpoint set |
| **Step Over**      | Pause when `callStack.Count <= pausedDepth`                       |
| **Step Into**      | Pause on next `OnBeforeExecute` unconditionally                   |
| **Step Out**       | Pause when `callStack.Count < pausedDepth`                        |
| **Variable Watch** | Read `Environment` and walk parent chain                          |
| **Struct Expand**  | Enumerate dictionary fields of a struct instance                  |

### Future: DAP (Debug Adapter Protocol)

Microsoft's Debug Adapter Protocol allows integration with VS Code and other editors. With the hooks above, a DAP adapter becomes a thin translation layer rather than a rewrite.

---

## 12. Performance Strategy

### Philosophy

Build correct first, measure, then optimize the hot path. A tree-walk interpreter in C# with zero optimizations is already faster than Bash.

### Where Time Is Spent

```
Lexing/Parsing:  ~10-20%  (runs once per script load)
Execution:       ~80-90%  (runs repeatedly)
  ├── Dispatch:  which AST node to execute next
  ├── Lookups:   variable and field resolution
  └── Allocs:    creating values, strings, intermediates
```

### Optimization Tiers

#### Tier 1 — Easy Wins (Apply During Development)

| Optimization                             | Where              | Impact  |
| ---------------------------------------- | ------------------ | ------- |
| String interning                         | Lexer              | High    |
| `FrozenDictionary` for keywords/builtins | Lexer, Interpreter | Low-Med |
| `ReadOnlySpan<char>` in lexer            | Lexer              | High    |

#### Tier 2 — Architectural (Apply After v1 Works)

| Optimization                                      | Where                  | Impact  |
| ------------------------------------------------- | ---------------------- | ------- |
| Variable resolution at parse time (resolver pass) | Resolver + Interpreter | Highest |
| Slot-based environments (array, not dictionary)   | Environment            | High    |
| `ArrayPool<T>` for function call argument arrays  | Interpreter            | Medium  |

#### Tier 3 — Nuclear Option

If the tree-walk interpreter hits a performance wall: **switch to a bytecode VM**. This is a 10-50x speedup and dwarfs all micro-optimizations. The bytecode VM compiles the AST to a flat array of opcodes, and a tight `switch`-dispatch loop executes them.

### What NOT to Optimize

- `stackalloc` everywhere (only for fixed-size small buffers)
- `Unsafe` code / raw pointers (marginal gain, high bug risk)
- Custom memory allocators (over-engineered for v1)
- Premature async in the core execution loop

---

## 13. Implementation Roadmap

### Phase 1 — Foundation

| Step | Milestone                           | Key Concepts                                     |
| ---- | ----------------------------------- | ------------------------------------------------ |
| 1.1  | Lexer                               | Token types, source spans, string interning      |
| 1.2  | Parser + AST                        | Recursive descent, Pratt parsing for expressions |
| 1.3  | Tree-walk interpreter (expressions) | Arithmetic, comparisons, booleans                |
| 1.4  | REPL                                | Read-Eval-Print loop                             |

**Milestone:** Evaluate `1 + 2 * 3` correctly in the REPL.

### Phase 2 — Language Core

| Step | Milestone                           | Key Concepts                              |
| ---- | ----------------------------------- | ----------------------------------------- |
| 2.1  | Variables (`let`, `const`)          | Environment, variable binding, mutability |
| 2.2  | Control flow (`if`, `while`, `for`) | Statement execution, truthiness           |
| 2.3  | Functions (`fn`, `return`)          | Call stack, frames, closures              |
| 2.4  | Resolver pass                       | Variable resolution at parse time         |

**Milestone:** Recursive fibonacci function works.

### Phase 3 — Structured Data

| Step | Milestone          | Key Concepts                                 |
| ---- | ------------------ | -------------------------------------------- |
| 3.1  | Arrays             | Array literals, indexing, `len()`            |
| 3.2  | Structs            | Declaration, instantiation, dot access       |
| 3.3  | Enums              | Declaration, dot access, identity comparison |
| 3.4  | Built-in functions | `println`, `typeof`, `len`, `toStr`, etc.    |

**Milestone:** Create a struct, populate it, iterate over an array of structs. Use enums for status values.

### Phase 4 — Shell Integration

| Step | Milestone              | Key Concepts                                                                  |
| ---- | ---------------------- | ----------------------------------------------------------------------------- |
| 4.1  | Command literals `$()` | Lexer command mode (always raw + interpolation), `System.Diagnostics.Process` |
| 4.2  | Pipe operator          | Chaining process stdout → stdin                                               |
| 4.3  | File I/O built-ins     | `fs.readFile`, `fs.writeFile`                                                 |
| 4.4  | Environment variables  | `env.get("PATH")`, `env.set("KEY", "val")`                                    |

**Milestone:** A script that SSHs into a server, checks a service, and reports status.

### Phase 5 — Polish

| Step | Milestone             | Key Concepts                                          |
| ---- | --------------------- | ----------------------------------------------------- |
| 5.1  | Error handling        | `try` expression, `lastError()`, `??` null-coalescing |
| 5.2  | Script file execution | `./stash script.stash`, shebang support               |
| 5.3  | Selective imports     | `import { fn1, fn2 } from "utils.stash";`             |
| 5.4  | CLI debugger          | `break`, `step`, `print`, `continue`                  |

### Phase 6 — Future

- Bytecode VM (if performance requires it)
- DAP adapter (VS Code debugging)
- Methods on structs
- C-style `for(;;)` loops
- Regular expressions
- Standard library expansion

---

## 14. References & Resources

### Essential Reading

| Resource                                         | Description                                                                                                      |
| ------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------- |
| **Crafting Interpreters** — Robert Nystrom       | The definitive guide. Free at craftinginterpreters.com. Covers tree-walk interpreter (Java) and bytecode VM (C). |
| **Writing An Interpreter In Go** — Thorsten Ball | Concise, practical. Builds "Monkey" language step-by-step.                                                       |
| **Writing A Compiler In Go** — Thorsten Ball     | Sequel. Converts the tree-walk interpreter to a bytecode compiler + VM.                                          |

### Supplementary

| Resource                                                     | Description                                                           |
| ------------------------------------------------------------ | --------------------------------------------------------------------- |
| **Engineering a Compiler** — Cooper & Torczon                | Academic depth on parsing theory and optimization.                    |
| **Structure and Interpretation of Computer Programs** (SICP) | Classic. Builds a Scheme interpreter.                                 |
| **Immo Landwerth's "Minsk" YouTube series**                  | Builds a compiler live in C#. Directly applicable to your tech stack. |

### Specifications & Protocols

| Resource                           | Description                                               |
| ---------------------------------- | --------------------------------------------------------- |
| **Debug Adapter Protocol (DAP)**   | Microsoft's protocol for editor-debugger communication.   |
| **Language Server Protocol (LSP)** | For future IDE support (autocomplete, diagnostics, etc.). |

---

## Appendix A — Open Questions

- [x] ~~Language name~~ → **Stash**
- [x] ~~File extension~~ → `.stash` (default), `.sth` (short form, tentative)
- [x] ~~String interpolation syntax~~ → Both `"Hello ${name}"` and `$"Hello {name}"` supported
- [x] ~~Enums~~ → Included in v1 (see Section 5b)
- [x] ~~Command syntax~~ → `$(command)` literals — always raw mode, `{expr}` for interpolation
- [x] ~~C-style `for(;;)` loop~~ → No. Only `for-in` in v1. May revisit later.
- [x] ~~Error handling model~~ → `try` expression + `??` null-coalescing (see Section 7b)
- [x] ~~Null handling~~ → `??` null-coalescing operator included (see Section 7)
- [x] ~~Shebang support~~ → Yes. Lexer skips `#!` lines (see Section 6b)
- [x] ~~Module/import system~~ → Selective imports: `import { a, b } from "file.stash";` (see Section 9b)
- [x] ~~Argument parsing~~ → Declarative `args` block with flags, options, positionals, subcommands (see Section 9c)

## Appendix B — Grammar (Draft, EBNF)

```ebnf
program        → shebang? declaration* EOF ;
shebang        → "#!" <everything until newline> ;

declaration    → structDecl | enumDecl | fnDecl | varDecl | importDecl | statement ;

structDecl     → "struct" IDENTIFIER "{" IDENTIFIER ("," IDENTIFIER)* "}" ;
enumDecl       → "enum" IDENTIFIER "{" IDENTIFIER ("," IDENTIFIER)* "}" ;
fnDecl         → "fn" IDENTIFIER "(" parameters? ")" block ;
varDecl        → "let" IDENTIFIER "=" expression ";" ;
importDecl     → "import" "{" IDENTIFIER ("," IDENTIFIER)* "}" "from" STRING ";"
               | "import" STRING "as" IDENTIFIER ";" ;

statement      → exprStmt | ifStmt | whileStmt | forStmt | returnStmt | breakStmt | continueStmt | block ;

exprStmt       → expression ";" ;
ifStmt         → "if" "(" expression ")" block ( "else" (ifStmt | block) )? ;
whileStmt      → "while" "(" expression ")" block ;
forStmt        → "for" "(" "let" IDENTIFIER "in" expression ")" block ;
returnStmt     → "return" expression? ";" ;
breakStmt      → "break" ";" ;
continueStmt   → "continue" ";" ;
block          → "{" declaration* "}" ;

expression     → assignment ;
assignment     → (call ".")? IDENTIFIER "=" assignment | ternary ;
ternary        → nullCoalesce ( "?" expression ":" ternary )? ;
nullCoalesce   → redirect ( "??" redirect )* ;
redirect       → pipe ( redirectOp expression )* ;
redirectOp     → ">" | ">>" | "2>" | "2>>" | "&>" | "&>>" ;
pipe           → logic_or ( "|" logic_or )* ;
logic_or       → logic_and ( "||" logic_and )* ;
logic_and      → equality ( "&&" equality )* ;
equality       → comparison ( ("==" | "!=") comparison )* ;
comparison     → term ( ("<" | ">" | "<=" | ">=") term )* ;
term           → factor ( ("+" | "-") factor )* ;
factor         → unary ( ("*" | "/" | "%") unary )* ;
unary          → ("!" | "-") unary | prefix ;
prefix         → ("++" | "--") IDENTIFIER | tryExpr ;
tryExpr        → "try" unary | postfix ;
postfix        → call ("++" | "--")? ;
call           → primary ( "(" arguments? ")" | "." IDENTIFIER | "[" expression "]" | "switch" "{" switchArm ("," switchArm)* ","? "}" )* ;
switchArm      → ( "_" | expression ) "=>" expression ;
primary        → NUMBER | STRING | INTERPOLATED_STRING | "true" | "false" | "null"
               | IDENTIFIER | lambdaExpr | "(" expression ")"
               | "[" (expression ("," expression)*)? "]"
               | (call ".")? IDENTIFIER "{" (IDENTIFIER ":" expression ("," IDENTIFIER ":" expression)*)? "}"
               | "$(" COMMAND_TEXT ")" ;
lambdaExpr     → "(" parameters? ")" "=>" ( block | assignment ) ;

parameters     → IDENTIFIER ( "," IDENTIFIER )* ;
arguments      → expression ( "," expression )* ;
```

---

_This is a living document. Update as design decisions are finalized and implementation progresses._
