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

- **External programs as first-class citizens.** `$(ls -la)` just works — programs are invoked directly, no shell wrapping, no subprocess imports. Pipe chains with `|` pass stdout between processes with short-circuit-on-failure semantics built in.
- **Real data structures.** Structs, enums, and dictionaries let you model your domain — servers, deploy targets, configurations — instead of juggling parallel arrays and magic strings.
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

# Run tests with TAP output
dotnet run --project Stash.Interpreter/ -- --test tests.stash

# Debug tests (breakpoints work inside test blocks)
dotnet run --project Stash.Interpreter/ -- --debug --test tests.stash
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
| `dict`   | `dict.new()`             | Key-value pairs       |
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

### Dictionaries

```stash
let config = dict.new();
config["host"] = "localhost";
config["port"] = 8080;
config["debug"] = true;

println(config["host"]);           // "localhost"
println(dict.size(config));        // 3
println(dict.has(config, "port")); // true

// Iterate keys
for (let key in config) {
    println(key + " = " + config[key]);
}

// Pair structs with .key / .value access
for (let pair in dict.pairs(config)) {
    println(pair.key + " => " + pair.value);
}

// Merging (second dict wins on conflicts)
let merged = dict.merge(defaults, overrides);
```

Dictionaries support `string`, `int`, `float`, and `bool` keys. Access via bracket notation (`d[key]`) or dot notation (`d.key`), and use the `dict` namespace for operations like `dict.keys()`, `dict.values()`, `dict.pairs()`, `dict.forEach()`, and `dict.merge()`.

### Configuration Files

Load and manipulate configuration files directly — INI and JSON formats are supported out of the box:

```stash
// Load an INI config file into a nested dictionary
let cfg = try config.read("/etc/myapp/config.ini") ?? dict.new();

// Access values with dot notation
println(cfg.database.host);      // "localhost"
println(cfg.database.port);      // 5432

// Modify and write back
cfg.database.port = 3306;
config.write("/etc/myapp/config.ini", cfg);

// Inline INI parsing
let text = "[server]\nhost = 0.0.0.0\nport = 8080";
let settings = ini.parse(text);
println(settings.server.host);   // "0.0.0.0"

// Format conversion — read INI, write JSON
let legacy = config.read("old.ini");
config.write("modern.json", legacy);
```

The `config` namespace auto-detects format from file extension (`.json`, `.ini`, `.cfg`, `.conf`, `.properties`). The `ini` namespace provides `ini.parse()` and `ini.stringify()` for string-level control.

### String Operations

```stash
let name = "  Hello, World!  ";
let trimmed = str.trim(name);              // "Hello, World!"
let upper = str.upper(trimmed);            // "HELLO, WORLD!"

// Search and extract
println(str.contains(trimmed, "World"));   // true
println(str.indexOf(trimmed, "World"));    // 7
println(str.substring(trimmed, 0, 5));     // "Hello"

// Transform
let csv = "a,b,c";
let parts = str.split(csv, ",");           // ["a", "b", "c"]
let replaced = str.replaceAll(trimmed, "l", "L"); // "HeLLo, WorLd!"

// Padding and repetition
println(str.padStart("42", 5, "0"));       // "00042"
println(str.repeat("ab", 3));              // "ababab"
```

The `str` namespace provides 19 functions for string manipulation including case conversion, trimming, searching, slicing, splitting, replacing, padding, and more. Strings also support indexing (`s[0]`), `len(s)`, interpolation (`$"Hello {name}"`), and `for-in` iteration.

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

### Process Management

Spawn background processes, track their lifecycle, communicate with them, and control shutdown:

```stash
// Spawn a background process — returns a Process handle
let server = process.spawn("python3 -m http.server 8080");
io.println(server.pid);         // OS process ID
io.println(server.command);     // "python3 -m http.server 8080"

// Check if still running
if (process.isAlive(server)) {
    io.println("Server is running");
}

// Communicate with running processes
let proc = process.spawn("bc -l");
process.write(proc, "2 + 3\n");
let answer = process.read(proc);   // "5\n"
process.kill(proc);

// Wait for a process to finish
let worker = process.spawn("make build");
let result = process.wait(worker);
io.println("Exit code: " + result.exitCode);

// Wait with a timeout (milliseconds)
let r = process.waitTimeout(server, 5000);
if (r == null) {
    io.println("Still running after 5s");
    process.kill(server);
}

// Send specific signals
process.signal(server, process.SIGTERM);   // graceful shutdown
process.signal(server, process.SIGKILL);   // force kill

// Detach a process so it survives script exit
let daemon = process.spawn("my-daemon --config app.conf");
process.detach(daemon);
// daemon continues running after script exits

// List all tracked processes
for (let p in process.list()) {
    io.println(p.command + " (PID: " + p.pid + ")");
}
```

All spawned processes are automatically killed (SIGTERM → 3s grace → SIGKILL) when a script exits, unless explicitly detached with `process.detach()`.

| Function                        | Description                                         |
| ------------------------------- | --------------------------------------------------- |
| `process.spawn(cmd)`            | Launch background process, returns `Process` handle |
| `process.wait(proc)`            | Block until process exits, returns `CommandResult`  |
| `process.waitTimeout(proc, ms)` | Wait with timeout; returns `null` if timed out      |
| `process.kill(proc)`            | Send SIGTERM to process                             |
| `process.isAlive(proc)`         | Check if process is still running                   |
| `process.signal(proc, sig)`     | Send arbitrary signal (use `process.SIGTERM`, etc.) |
| `process.pid(proc)`             | Get OS process ID                                   |
| `process.detach(proc)`          | Detach process so it survives script exit           |
| `process.list()`                | List all tracked process handles                    |
| `process.read(proc)`            | Read available stdout (non-blocking)                |
| `process.write(proc, data)`     | Write to process stdin                              |
| `process.exit(code)`            | Terminate script with exit code                     |

Signal constants: `process.SIGHUP` (1), `process.SIGINT` (2), `process.SIGQUIT` (3), `process.SIGKILL` (9), `process.SIGUSR1` (10), `process.SIGUSR2` (12), `process.SIGTERM` (15).

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

Command expressions never crash — they return structured results with `exitCode` and `stderr`, so `try` is not needed for `$(...)`.

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

| Function                         | Description                                                                                                       |
| -------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `io.println(val)`                | Print value + newline                                                                                             |
| `io.print(val)`                  | Print value without newline                                                                                       |
| `typeof(val)`                    | Type as string: `"int"`, `"float"`, `"string"`, `"bool"`, `"null"`, `"array"`, `"struct"`, `"enum"`, `"function"` |
| `len(val)`                       | Length of string or array                                                                                         |
| `conv.toStr(val)`                | Convert any value to string                                                                                       |
| `conv.toInt(val)`                | Parse string/number to integer                                                                                    |
| `conv.toFloat(val)`              | Parse string/number to float                                                                                      |
| `fs.readFile(path)`              | Read file contents as string                                                                                      |
| `fs.writeFile(path, content)`    | Write string to file                                                                                              |
| `env.get(name)`                  | Read environment variable (null if unset)                                                                         |
| `env.set(name, value)`           | Set environment variable                                                                                          |
| `process.exit(code)`             | Terminate with exit code                                                                                          |
| `process.spawn(cmd)`             | Launch background process, returns `Process` handle                                                               |
| `process.wait(proc)`             | Block until process exits, returns `CommandResult`                                                                |
| `process.waitTimeout(proc, ms)`  | Wait with timeout; `null` if timed out                                                                            |
| `process.kill(proc)`             | Send SIGTERM to a process                                                                                         |
| `process.isAlive(proc)`          | Check if process is still running                                                                                 |
| `process.signal(proc, sig)`      | Send arbitrary signal to a process                                                                                |
| `process.detach(proc)`           | Detach process so it survives script exit                                                                         |
| `process.list()`                 | List all tracked process handles                                                                                  |
| `process.read(proc)`             | Read available stdout from running process                                                                        |
| `process.write(proc, data)`      | Write to running process stdin                                                                                    |
| `arr.push(a, val)`               | Push value onto array                                                                                             |
| `arr.pop(a)`                     | Remove and return last element                                                                                    |
| `arr.sort(a)`                    | Sort array in place (numbers or strings)                                                                          |
| `arr.map(a, fn)`                 | Return new array with `fn` applied to each element                                                                |
| `arr.filter(a, fn)`              | Return new array with elements where `fn` returns truthy                                                          |
| `arr.reduce(a, fn, init)`        | Reduce array to single value                                                                                      |
| `dict.new()`                     | Create an empty dictionary                                                                                        |
| `dict.get(d, key)`               | Get value for key, or `null` if not found                                                                         |
| `dict.set(d, key, val)`          | Set key-value pair (mutates dictionary)                                                                           |
| `dict.has(d, key)`               | Return `true` if key exists                                                                                       |
| `dict.keys(d)`                   | Return array of all keys                                                                                          |
| `dict.pairs(d)`                  | Return array of Pair structs (`.key`, `.value`)                                                                   |
| `dict.merge(d1, d2)`             | Return new dictionary combining both                                                                              |
| `str.upper(s)`                   | Convert string to uppercase                                                                                       |
| `str.lower(s)`                   | Convert string to lowercase                                                                                       |
| `str.trim(s)`                    | Remove leading/trailing whitespace                                                                                |
| `str.contains(s, sub)`           | Check if string contains substring                                                                                |
| `str.indexOf(s, sub)`            | First occurrence index, or `-1`                                                                                   |
| `str.substring(s, start, end?)`  | Extract substring                                                                                                 |
| `str.replace(s, old, new)`       | Replace first occurrence                                                                                          |
| `str.replaceAll(s, old, new)`    | Replace all occurrences                                                                                           |
| `str.split(s, delim)`            | Split string into array                                                                                           |
| `str.padStart(s, len, fill?)`    | Pad start to target length                                                                                        |
| `ini.parse(text)`                | Parse INI string into nested dict                                                                                 |
| `ini.stringify(dict)`            | Serialize dict to INI format string                                                                               |
| `config.read(path, format?)`     | Read and parse config file (auto-detects `.json`, `.ini`, `.cfg`, `.conf`)                                        |
| `config.write(path, data, fmt?)` | Write data to config file in detected/specified format                                                            |
| `config.parse(text, format)`     | Parse config string without file I/O                                                                              |
| `config.stringify(data, format)` | Serialize data to config format string                                                                            |
| `json.parse(text)`               | Parse JSON string into dict/array                                                                                 |
| `json.stringify(value)`          | Serialize value to compact JSON string                                                                            |
| `json.pretty(value)`             | Serialize value to pretty-printed JSON string                                                                     |
| `lastError()`                    | Last error caught by `try`, or null                                                                               |
| `parseArgs(tree)`                | Parse CLI arguments from an `ArgTree` definition                                                                  |
| `test(name, fn)`                 | Register and run a test case (requires `--test`)                                                                  |
| `describe(name, fn)`             | Group tests under a descriptive name                                                                              |
| `captureOutput(fn)`              | Execute `fn` with output redirected; returns captured string                                                      |
| `assert.equal(a, b)`             | Assert `a == b` (no type coercion)                                                                                |
| `assert.notEqual(a, b)`          | Assert `a != b`                                                                                                   |
| `assert.true(val)`               | Assert value is truthy                                                                                            |
| `assert.false(val)`              | Assert value is falsy                                                                                             |
| `assert.null(val)`               | Assert value is `null`                                                                                            |
| `assert.notNull(val)`            | Assert value is not `null`                                                                                        |
| `assert.greater(a, b)`           | Assert `a > b`                                                                                                    |
| `assert.less(a, b)`              | Assert `a < b`                                                                                                    |
| `assert.throws(fn)`              | Assert `fn()` throws; returns error message                                                                       |
| `assert.fail(msg?)`              | Unconditionally fail                                                                                              |

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

### Testing

Stash has built-in testing support — no external frameworks needed. Test scripts are ordinary Stash scripts that use `test()`, `describe()`, and the `assert` namespace:

```stash
#!/usr/bin/env stash

describe("string operations", () => {
    test("upper case conversion", () => {
        assert.equal(str.upper("hello"), "HELLO");
    });

    test("string contains", () => {
        assert.true(str.contains("hello world", "world"));
    });

    test("split and rejoin", () => {
        let parts = str.split("a,b,c", ",");
        assert.equal(len(parts), 3);
        assert.equal(parts[0], "a");
    });
});
```

Run with `--test` to get TAP (Test Anything Protocol) output:

```bash
dotnet run --project Stash.Interpreter/ -- --test test_strings.stash
```

```
TAP version 14
ok 1 - string operations > upper case conversion
ok 2 - string operations > string contains
ok 3 - string operations > split and rejoin
1..3
```

#### `assert` Namespace

| Function                            | Description                                     |
| ----------------------------------- | ----------------------------------------------- |
| `assert.equal(actual, expected)`    | Assert `actual == expected` (no type coercion)  |
| `assert.notEqual(actual, expected)` | Assert `actual != expected`                     |
| `assert.true(value)`                | Assert value is truthy                          |
| `assert.false(value)`               | Assert value is falsy                           |
| `assert.null(value)`                | Assert value is `null`                          |
| `assert.notNull(value)`             | Assert value is not `null`                      |
| `assert.greater(a, b)`              | Assert `a > b`                                  |
| `assert.less(a, b)`                 | Assert `a < b`                                  |
| `assert.throws(fn)`                 | Assert `fn()` throws; returns the error message |
| `assert.fail(message?)`             | Unconditionally fail                            |

#### Output Capture

Test code that produces output using `captureOutput()`:

```stash
test("greeting prints correctly", () => {
    let output = captureOutput(() => {
        io.println("Hello, world!");
    });
    assert.equal(output, "Hello, world!\n");
});
```

#### Debugging Tests

Tests and the debugger work together — set breakpoints inside test blocks:

```bash
dotnet run --project Stash.Interpreter/ -- --debug --test tests.stash
```

Breakpoints, `step`, `next`, `print`, and all debugger commands work normally inside `test()` and `describe()` blocks.

---

## Performance: Stash vs Bash

Stash's .NET-backed interpreter outperforms Bash across the board. Both languages were benchmarked with equivalent scripts performing the same operations — identical algorithms, identical iteration counts, identical checksums.

| Benchmark                 | What it tests                                            |    Stash |      Bash |   Speedup |
| ------------------------- | -------------------------------------------------------- | -------: | --------: | --------: |
| **Algorithms**            | Recursion, sorting, searching, struct usage              | 2,312 ms | 10,318 ms |  **4.5×** |
| **Function Calls**        | Dispatch overhead across 0–4 argument arities            | 2,485 ms |  3,657 ms |  **1.5×** |
| **Expression Throughput** | Dense arithmetic, 70 variables, string interpolation     | 1,033 ms |  4,981 ms |  **4.8×** |
| **Built-in Functions**    | 13 stdlib calls per iteration (math, string, conversion) |   878 ms | 23,366 ms | **26.6×** |
| **Scope Lookup**          | Variable resolution across 5-level nested closures       | 1,664 ms |  3,293 ms |  **2.0×** |

> Measured on the same machine, same workload. Stash run with `dotnet run -c Release`, Bash run with `bash`. Full scripts in [`benchmarks/`](benchmarks/).

### Why the gap widens on built-in functions

The **26.6×** speedup on built-in function calls is the most telling result. Stash resolves `math.sqrt()`, `str.upper()`, `conv.toHex()` via `FrozenDictionary` dispatch directly into .NET — these are native CLR method calls. Bash has no standard library, so equivalent operations require workarounds:

| Operation              | Stash                     | Bash workaround                                         |
| ---------------------- | ------------------------- | ------------------------------------------------------- |
| `math.sqrt(n)`         | Native `Math.Sqrt()`      | Hand-rolled Newton's method loop                        |
| `math.pow(2, 8)`       | Native `Math.Pow()`       | `$(( 2**8 ))` (integer only)                            |
| `str.upper(s)`         | `String.ToUpper()`        | `${s^^}` (Bash 4+ only)                                 |
| `str.trim(s)`          | `String.Trim()`           | Nested parameter expansion `${s#"${s%%[![:space:]]*}"}` |
| `str.replace(s, a, b)` | `String.Replace()`        | `${s/a/b}`                                              |
| `conv.toHex(n)`        | `Convert.ToString(n, 16)` | `printf -v var '%x' n`                                  |
| `conv.toFloat(s)`      | `Double.Parse()`          | Not available — integers only                           |

### What Bash can't express

Beyond raw speed, several Stash features have no Bash equivalent at all:

| Feature                  | Stash                                   | Bash                                                                  |
| ------------------------ | --------------------------------------- | --------------------------------------------------------------------- | --- | ----------------------------------- |
| **Structs**              | `struct Server { host, port }`          | Parallel arrays or associative arrays (no type safety, no dot access) |
| **Enums**                | `enum Status { Active, Inactive }`      | Integer constants by convention                                       |
| **Floating point**       | Native `double` arithmetic              | Requires `bc` or `awk` subshells                                      |
| **Closures**             | Functions capture enclosing scope       | Dynamic scoping only — no true closures                               |
| **String interpolation** | `$"Hello {name}"`                       | `"Hello ${name}"` (no expressions: `$"{x + 1}"` impossible)           |
| **Switch expressions**   | `x switch { 1 => "one", _ => "other" }` | `case` statement (no expression form)                                 |
| **Imports**              | `import { fn } from "lib.stash"`        | `source lib.sh` (pollutes global namespace)                           |
| **Error handling**       | `try expr ?? fallback`                  | `cmd                                                                  |     | fallback` (no expression-level try) |
| **Lambdas**              | `(x) => x * 2`                          | Not available                                                         |
| **Type checking**        | `typeof(val) == "int"`                  | `[[ "$val" =~ ^[0-9]+$ ]]`                                            |
| **Dictionaries**         | `dict.merge(a, b)`, `dict.pairs(d)`     | Associative arrays (no merge, no iteration helpers)                   |

### What Bash can do that Stash can't

Stash intentionally trades some shell-isms for cleaner syntax and structured programming. Here's what you give up:

| Bash feature | What it does | Stash alternative |
| --- | --- | --- |
| **Glob expansion** | `*.txt` expands inline in commands | `fs.glob("*.txt")` — explicit, no magic expansion |
| **Here documents** | `<<EOF ... EOF` for multi-line input | Multi-line string literals `"..."` |
| **Process substitution** | `diff <(cmd1) <(cmd2)` | Write to temp files, then diff |
| **Signal trapping** | `trap 'cleanup' EXIT SIGINT` | Not available — can send signals with `process.signal()`, but can't catch them |
| **Interactive job control** | `Ctrl+Z`, `fg`, `bg`, `jobs` | `process.spawn()` for background work, but no interactive shell job control |
| **Brace expansion** | `file{A,B,C}.txt`, `{1..100}` | Use loops or array construction |
| **`eval`** | Execute arbitrary strings as code | Not available — code must be in files (security by design) |
| **Subshells** | `(cd /tmp && cmd)` — isolated env | Block scoping `{ }` for variable isolation, but no subprocess isolation |
| **Coprocesses** | `coproc` for bidirectional pipes | `process.spawn()` + `process.read()` / `process.write()` (more verbose but equivalent) |
| **Aliases** | `alias ll='ls -la'` | Define a function: `fn ll() { return $(ls -la); }` |
| **POSIX compliance** | Runs on any Unix system out of the box | Requires .NET runtime (install via single command, but it's a dependency) |

Some of these are intentional design choices — no `eval` eliminates a class of injection vulnerabilities, explicit `fs.glob()` prevents surprises from wildcard expansion in arguments, and requiring .NET is the tradeoff that enables the performance and type system advantages shown above.

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
├── Testing/         # ITestHarness interface, TapReporter
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
