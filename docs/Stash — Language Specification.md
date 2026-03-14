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

**Addenda:** [5b. Enums](#5b-enums) · [6b. Shebang Support](#6b-shebang-support) · [7b. Error Handling](#7b-error-handling) · [7c. Switch Expressions](#7c-switch-expressions) · [8b. Lambda Expressions](#8b-lambda-expressions) · [9b. Module / Import System](#9b-module--import-system) · [9c. Argument Declarations](#9c-argument-declarations)

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

`$(...)` is **always raw mode**. When the lexer encounters `$(`, it enters "command mode" and collects everything as raw text until the matching `)`. The content is not parsed as a Stash expression — it is treated as a shell command.

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

| Function           | Description                     |
| ------------------ | ------------------------------- |
| `io.println(val)`  | Print value followed by newline |
| `io.print(val)`    | Print value without newline     |

#### `conv` — Type Conversion

| Function            | Description              |
| ------------------- | ------------------------ |
| `conv.toStr(val)`   | Convert value to string  |
| `conv.toInt(val)`   | Parse string to integer  |
| `conv.toFloat(val)` | Parse string to float    |

#### `env` — Environment Variables

| Function               | Description                               |
| ---------------------- | ----------------------------------------- |
| `env.get(name)`        | Read environment variable (null if unset) |
| `env.set(name, value)` | Set environment variable                  |

#### `process` — Process Control

| Function             | Description                         |
| -------------------- | ----------------------------------- |
| `process.exit(code)` | Terminate the script with exit code |

#### `fs` — File System Operations

| Function                         | Description                                      |
| -------------------------------- | ------------------------------------------------ |
| `fs.readFile(path)`              | Read file contents as string                     |
| `fs.writeFile(path, content)`    | Write string to file (creates or overwrites)     |
| `fs.appendFile(path, content)`   | Append string to file                            |
| `fs.exists(path)`                | Check if a file exists (returns boolean)         |
| `fs.dirExists(path)`             | Check if a directory exists (returns boolean)    |
| `fs.pathExists(path)`            | Check if a file or directory exists              |
| `fs.createDir(path)`             | Create a directory (including parents)           |
| `fs.delete(path)`                | Delete a file or directory (recursive)           |
| `fs.copy(src, dst)`              | Copy a file (overwrites destination)             |
| `fs.move(src, dst)`              | Move/rename a file (overwrites destination)      |
| `fs.size(path)`                  | Get file size in bytes                           |
| `fs.listDir(path)`               | List entries in a directory (returns array)      |

#### `path` — Path Manipulation

| Function                         | Description                                      |
| -------------------------------- | ------------------------------------------------ |
| `path.abs(p)`                    | Get absolute path                                |
| `path.dir(p)`                    | Get directory portion of path                    |
| `path.base(p)`                   | Get filename with extension                      |
| `path.name(p)`                   | Get filename without extension                   |
| `path.ext(p)`                    | Get file extension (including `.`)               |
| `path.join(a, b)`                | Join two path segments                           |

Namespace members are accessed with dot notation: `fs.exists("/etc/hosts")`. Namespaces are first-class values — `typeof(fs)` returns `"namespace"`. Assignment to namespace members is not permitted.

Standard library to be expanded as needed.

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

| Field         | Type              | Description                                                      |
| ------------- | ----------------- | ---------------------------------------------------------------- |
| `name`        | string (optional) | Script name (used in help text and usage line)                   |
| `version`     | string (optional) | Version string (used with auto `--version` flag)                 |
| `description` | string            | Script description (required, can be `""`)                       |
| `flags`       | array (optional)  | Array of `ArgDef` entries for boolean switches                   |
| `options`     | array (optional)  | Array of `ArgDef` entries for value-taking options               |
| `commands`    | array (optional)  | Array of `ArgDef` entries for subcommands                        |
| `positionals` | array (optional)  | Array of `ArgDef` entries for positional arguments               |

#### `ArgDef` Fields

| Field         | Type               | Description                                                                |
| ------------- | ------------------ | -------------------------------------------------------------------------- |
| `name`        | string (required)  | Long name of the argument                                                  |
| `short`       | string (optional)  | Single-character short form                                                |
| `type`        | string (optional)  | Type for coercion: `"string"`, `"int"`, `"float"`, `"bool"`               |
| `default`     | any (optional)     | Default value (any expression)                                             |
| `description` | string             | Description for help text (required, can be `""`)                          |
| `required`    | bool (optional)    | Whether the argument must be provided (default: `false`)                   |
| `args`        | ArgTree (optional) | Nested `ArgTree` for subcommand flags/options/positionals                  |

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

| Type     | Stash Type | Accepted Values                                              |
| -------- | ---------- | ------------------------------------------------------------ |
| `string` | string     | Any string (default if no `type` specified)                  |
| `int`    | int (long) | Integer strings (e.g., `"42"`, `"-1"`)                       |
| `float`  | float      | Decimal strings (e.g., `"3.14"`)                             |
| `bool`   | bool       | `"true"`, `"false"`, `"1"`, `"0"`, `"yes"`, `"no"`          |

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

Operators: `+`, `-`, `*`, `/`, `%`, `=`, `==`, `!=`, `<`, `>`, `<=`, `>=`, `&&`, `||`, `!`, `?`, `:`, `??`, `++`, `--`, `=>`

Note: `|` (pipe) is not listed as a general operator — it is a special syntactic form exclusive to command chaining (see Section 6).

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
nullCoalesce   → pipe ( "??" pipe )* ;
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
