# Stash — Language Specification

> **Status:** Draft v0.1
> **Created:** March 2026
> **Purpose:** Source of truth for the design and implementation of **Stash**, a C-style interpreted shell scripting language.
>
> **Companion documents:**
>
> - [Standard Library Reference](Stash%20—%20Standard%20Library%20Reference.md) — built-in namespace functions and argument parsing
> - [DAP — Debug Adapter Protocol](specs/DAP%20—%20Debug%20Adapter%20Protocol.md) — debug adapter server implementation
> - [LSP — Language Server Protocol](specs/LSP%20—%20Language%20Server%20Protocol.md) — language server implementation
> - [TAP — Testing Infrastructure](specs/TAP%20—%20Testing%20Infrastructure.md) — testing primitives, assert namespace, TAP output

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

**Addenda:** [3b. Compound Assignment Operators](#3b-compound-assignment-operators) · [3c. Multi-line Strings](#3c-multi-line-strings) · [3d. Range Expressions](#3d-range-expressions) · [3e. Destructuring Assignment](#3e-destructuring-assignment) · [4b. The `in` Operator](#4b-the-in-operator) · [5b. Enums](#5b-enums) · [5c. Dictionaries](#5c-dictionaries) · [5d. Dictionary Dot Access](#5d-dictionary-dot-access) · [5e. Optional Chaining](#5e-optional-chaining) · [6b. Shebang Support](#6b-shebang-support) · [6c. Output Redirection](#6c-output-redirection) · [7b. Error Handling](#7b-error-handling) · [7c. Switch Expressions](#7c-switch-expressions) · [8b. Lambda Expressions](#8b-lambda-expressions) · [9b. Module / Import System](#9b-module--import-system)

> **Standard Library:** Namespace reference tables, process management, argument parsing, and testing infrastructure are documented in the [Standard Library Reference](Stash%20—%20Standard%20Library%20Reference.md).

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

```stash
let name = "deploy";
let count = 5;
let verbose = true;
let pending;              // declared without initializer (value is null)
const MAX_RETRIES = 3;    // constant — cannot be reassigned
```

Variables declared with `let` are **mutable** — they can be reassigned after declaration. Variables declared with `const` are **immutable** — any attempt to reassign a `const` produces a runtime error. `let` without an initializer sets the variable to `null`.

### Operators

Standard C-style: `+`, `-`, `*`, `/`, `%`, `==`, `!=`, `<`, `>`, `<=`, `>=`, `&&`, `||`, `!`, `?:` (ternary), `??` (null-coalescing), `?.` (optional chaining, see [Section 5e](#5e-optional-chaining)), `++` (increment), `--` (decrement). Compound assignment: `+=`, `-=`, `*=`, `/=`, `%=`, `??=` (see [Section 3b](#3b-compound-assignment-operators)). Range: `..` (see [Section 3d](#3d-range-expressions)). Membership: `in` (see [Section 4b](#4b-the-in-operator)).

Keyword aliases: `and` is a synonym for `&&`, and `or` is a synonym for `||`. They are pure syntactic sugar — identical precedence, same short-circuit evaluation, identical semantics.

The `++` and `--` operators work on numeric variables, both as prefix and postfix:

```stash
let i = 0;
i++;       // postfix: returns 0, then i becomes 1
++i;       // prefix: i becomes 2, then returns 2
i--;       // postfix: returns 2, then i becomes 1
--i;       // prefix: i becomes 0, then returns 0
```

Prefix returns the value **after** the change; postfix returns the value **before** the change. Using `++`/`--` on a non-numeric value produces a runtime error.

### String Interpolation

Both interpolation syntaxes are supported:

```stash
let name = "world";
let greeting = "Hello ${name}";      // embedded interpolation
let greeting2 = $"Hello {name}";     // prefixed interpolation (C#-style)
let plain = "Hello " + name;          // concatenation still works
```

Both forms are explicit, intentional, and easy to read. Regular strings (without `$` prefix or `${}` markers) are never interpolated — no surprises.

The lexer treats `$"..."` as a special token type (`InterpolatedString`). Inside `"...${...}..."` strings, the lexer scans for `${` and switches to expression-parsing mode until the matching `}`.

### Comments

```stash
// Single-line comment
/* Multi-line
   comment */
```

### Documentation Comments

Stash supports documentation comments that attach to the declaration immediately following them. They are surfaced by the language server on hover and in signature help.

**Triple-slash** (`///`) — line-level doc comments:

```stash
/// Adds two numbers together.
/// @param a First number
/// @param b Second number
/// @return The sum of a and b
fn add(a, b) {
    return a + b;
}
```

**Block doc comments** (`/** ... */`) — multi-line doc comments:

```stash
/**
 * Checks whether a value exceeds a threshold.
 * @param value The number to test
 * @param threshold Upper bound
 * @return true if value > threshold
 */
fn exceeds(value, threshold) {
    return value > threshold;
}
```

Documentation comments also attach to variables and constants (useful for documenting lambdas):

```stash
/// Formats a greeting message.
/// @param name The person's name
/// @return A greeting string
let greet = (name) => "Hello, ${name}!";
```

**Supported tags:**

| Tag | Description |
|-----|-------------|
| `@param name description` | Documents a function parameter |
| `@return description` | Documents the return value |
| `@returns description` | Alias for `@return` |

> **Note:** `////` (four slashes) is treated as a regular comment, not a doc comment. Similarly, `/**/` (empty block) is a regular block comment.

### Sample Program

```stash
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

| Type     | Examples                       | Notes                                  |
| -------- | ------------------------------ | -------------------------------------- |
| `int`    | `42`, `-7`, `0`                | Integer numbers                        |
| `float`  | `3.14`, `-0.5`                 | Floating-point numbers                 |
| `string` | `"hello"`, `""`                | Immutable strings                      |
| `bool`   | `true`, `false`                |                                        |
| `null`   | `null`                         | Absence of value                       |
| `array`  | `[1, 2, 3]`, `["a", 42, true]` | Ordered, mixed-type, dynamic-size      |
| `struct` | `Server { host: "...", ... }`  | Named structured data (see Section 5)  |
| `enum`   | `Status.Active`, `Color.Red`   | Named constants (see Section 5b)       |
| `dict`   | `dict.new()`                   | Key-value map (see Section 5c)         |
| `range`  | `1..10`, `0..100..5`           | Lazy integer sequence (see Section 3d) |

### Type Coercion & Truthiness

**Truthiness:** The following values are **falsy**: `false`, `null`, `0` (integer zero), `0.0` (float zero), `""` (empty string). All other values are **truthy** (including empty arrays and struct instances).

**String concatenation (`+`):** When one operand of `+` is a string, the other operand is automatically converted to its string representation. `"count: " + 5` produces `"count: 5"`.

**String repetition (`*`):** When one operand of `*` is a string and the other is an integer, the string is repeated that many times. `"ha" * 3` produces `"hahaha"`, and `3 * "ha"` produces the same result (commutative). `"x" * 0` produces `""`. A negative count is a runtime error.

**Numeric type mixing:** When an `int` and a `float` are used in an arithmetic operation (`+`, `-`, `*`, `/`, `%`), the `int` is promoted to `float` and the result is a `float`. `5 + 3.14` produces `8.14`.

**Equality:** `==` and `!=` never perform type coercion. Values of different types are never equal (`5 != "5"`, `0 != false`, `0 != null`). Enum values are compared by identity (type + member name).

---

## 5. Structs & Objects

### Declaration

```stash
struct Server {
    host,
    port,
    status
}
```

A `struct` declaration registers a **template** — a name and a list of field names.

### Instantiation

```stash
let srv = Server { host: "10.0.0.1", port: 22, status: "unknown" };
```

Creates a new instance with the given field values.

#### Shorthand Initialization

When a variable name matches the field name, the value can be omitted:

```stash
let host = "10.0.0.1";
let port = 22;
let status = "unknown";

// Shorthand — equivalent to { host: host, port: port, status: status }
let srv = Server { host, port, status };

// Mixed — shorthand and explicit values can be combined
let srv2 = Server { host, port: 8080, status };
```

This is purely syntactic sugar — the parser generates the same `(field, value)` pairs as explicit initialization. The field name is used as an identifier expression for the value.

### Field Access

```stash
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

### Methods

Structs support method declarations — functions defined inside the struct body that receive an implicit `self` parameter bound to the instance at call time.

#### Syntax

Methods are declared with `fn` inside the struct body, after any field declarations:

```stash
struct Counter {
    count

    fn increment() {
        self.count = self.count + 1;
    }

    fn add(n) {
        self.count = self.count + n;
    }

    fn get() {
        return self.count;
    }
}
```

Fields and methods are separated naturally — fields are comma-separated identifiers, methods start with `fn`.

#### Method Calls

Methods are called via dot access on an instance:

```stash
let c = Counter { count: 0 };
c.increment();
c.add(5);
io.println(c.get());  // 6
```

#### The `self` Parameter

- `self` is **implicitly** bound when a method is called — it is not declared in the parameter list.
- Inside a method body, `self` refers to the instance the method was called on.
- `self` provides access to all fields and other methods of the instance.
- `self` is **not** available outside method bodies.

#### Method Storage

Methods are stored on the **struct template**, not on individual instances. All instances of a struct share the same method definitions. This means:

- Adding methods does not increase per-instance memory.
- Methods cannot be overridden on individual instances.
- When a method is accessed via dot notation (e.g., `c.increment`), it produces a **bound method** — an object that captures both the method function and the target instance.

#### Field/Method Name Collision

If a field and a method share the same name, the **field takes precedence** during dot access. The method is effectively shadowed.

#### Methods Calling Other Methods

Methods can call other methods on the same instance via `self`:

```stash
struct Rect {
    w, h

    fn area() {
        return self.w * self.h;
    }

    fn describe() {
        return $"Rect({self.w}x{self.h}, area={self.area()})";
    }
}
```

### Future Extensions (Not in v1)

- **Nested structs:** Structs as field values of other structs.
- **Default values:** Field declarations with default values.

---

## 3b. Compound Assignment Operators

Compound assignment operators combine an arithmetic or null-coalescing operation with assignment:

```stash
let count = 10;
count += 5;        // count = count + 5  → 15
count -= 3;        // count = count - 3  → 12
count *= 2;        // count = count * 2  → 24
count /= 4;        // count = count / 4  → 6
count %= 4;        // count = count % 4  → 2
```

The `??=` (null-coalescing assignment) assigns only if the variable is currently `null`:

```stash
let name = null;
name ??= "default";   // name is now "default"
name ??= "other";     // name is still "default" (was not null)
```

### Supported Operators

| Operator | Equivalent   | Description              |
| -------- | ------------ | ------------------------ |
| `+=`     | `x = x + y`  | Add and assign           |
| `-=`     | `x = x - y`  | Subtract and assign      |
| `*=`     | `x = x * y`  | Multiply and assign      |
| `/=`     | `x = x / y`  | Divide and assign        |
| `%=`     | `x = x % y`  | Modulo and assign        |
| `??=`    | `x = x ?? y` | Null-coalesce and assign |

### Semantics

Compound assignment is **desugared by the parser** into the equivalent assignment. `x += 1` is parsed as `x = x + 1`. This means compound assignment shares all the validation and behavior of regular assignment — it cannot reassign `const` variables, and it follows the same scoping rules.

Compound assignment works on variables, struct fields, dictionary entries, and array elements:

```stash
srv.port += 1;          // struct field
config["retries"] -= 1; // dict entry by index
nums[0] *= 10;          // array element
```

---

## 3c. Multi-line Strings

Triple-quoted strings (`"""..."""`) allow string literals that span multiple lines, preserving newlines and automatically handling indentation.

### Basic Usage

```stash
let text = """
    Hello,
    World!
""";
// Result: "Hello,\nWorld!\n"
```

The opening `"""` must be followed by a newline. The closing `"""` must appear on its own line. Leading whitespace is stripped based on the common indentation of all non-empty lines.

### Indentation Stripping

The lexer determines the minimum indentation across all non-empty lines and strips that prefix from every line:

```stash
let sql = """
    SELECT *
    FROM users
    WHERE active = true
""";
// Each line has 4 spaces of indent stripped → no leading spaces in the result
```

Lines with deeper indentation retain the extra spaces:

```stash
let code = """
    fn main() {
        println("hello");
    }
""";
// "fn main() {\n    println(\"hello\");\n}\n"
```

### Interpolation

Use the `$"""..."""` prefix for interpolation within multi-line strings:

```stash
let table = "users";
let query = $"""
    SELECT *
    FROM {table}
    WHERE active = true
""";
```

This follows the same `{expr}` interpolation syntax as `$"..."` strings.

### Implementation

Multi-line strings are handled entirely in the lexer. The lexer detects `"""` (or `$"""`) and scans until the closing `"""`. The `StripCommonIndent` method removes the shared whitespace prefix. The result is a standard `String` or `InterpolatedString` token — no parser or interpreter changes required.

---

## 3d. Range Expressions

Range expressions create lazy integer sequences using the `..` operator:

```stash
let r = 1..5;        // range: 1, 2, 3, 4 (end-exclusive)
let r2 = 0..10..2;   // range with step: 0, 2, 4, 6, 8
let r3 = 5..0;       // descending: 5, 4, 3, 2, 1 (auto step -1)
```

### Syntax

```
start..end             // step defaults to 1 (or -1 if start > end)
start..end..step       // explicit step
```

Both `start` and `end` must evaluate to integers. `step`, if provided, must also be an integer and must not be zero.

### Semantics

1. Ranges are **end-exclusive** — `1..5` produces `1, 2, 3, 4` (not including 5). This matches Python's `range()` behavior.
2. If `start > end` and no step is given, the step defaults to `-1` (automatic descending).
3. An explicit step of 0 is a runtime error.
4. Ranges are a distinct runtime type (`"range"` via `typeof()`).
5. Ranges are **lazy** — they do not allocate an array. Values are generated on demand during iteration.

### Iteration

Ranges are iterable with `for-in`:

```stash
for (let i in 1..5) {
    io.println(i);    // 1, 2, 3, 4
}

for (let i in 0..10..2) {
    io.println(i);    // 0, 2, 4, 6, 8
}

for (let i in 10..0) {
    io.println(i);    // 10, 9, 8, ..., 1
}
```

### Membership

The `in` operator tests whether an integer falls within a range:

```stash
println(3 in 1..10);    // true
println(10 in 1..10);   // false (end-exclusive)
println(5 in 0..10..2); // false (5 is not a step multiple)
```

### Precedence

The `..` operator sits between comparison and addition in the precedence table:

```
... → Comparison → Range → Term → ...
```

This means `1..2+3` parses as `1..(2+3)` → `1..5`, and `x in 1..10` parses as `x in (1..10)`.

### Internal Representation

A range is backed by a `StashRange` class holding `Start`, `End`, and `Step` values (all `long`). The `Iterate()` method yields values lazily. `ToString()` formats as `start..end` or `start..end..step`.

---

## 3e. Destructuring Assignment

Destructuring allows unpacking arrays and dictionaries into individual variables in a single declaration:

### Array Destructuring

```stash
let [a, b, c] = [1, 2, 3];
io.println(a);  // 1
io.println(b);  // 2
io.println(c);  // 3
```

Binds variables by **position** — the first variable gets the first element, and so on.

### Dictionary Destructuring

```stash
let config = dict.new();
config["host"] = "localhost";
config["port"] = 8080;

let {host, port} = config;
io.println(host);  // "localhost"
io.println(port);  // 8080
```

Binds variables by **key name** — the variable name is used as the dictionary key for lookup.

### Const Destructuring

```stash
const [x, y] = [10, 20];
// x and y are immutable — reassignment is a runtime error
```

### Partial Destructuring

```stash
// Extra elements are ignored
let [first, second] = [1, 2, 3, 4];    // first=1, second=2

// Missing elements become null
let [a, b, c] = [1];                    // a=1, b=null, c=null
```

### Implementation

Destructuring is a dedicated AST node (`DestructureStmt`) with a `PatternKind` (Array or Object), a list of variable names, a `const` flag, and an initializer expression. The parser detects destructuring when it sees `let [` or `let {` (similarly for `const`). At runtime, the interpreter evaluates the initializer and distributes values to each named variable.

---

## 4b. The `in` Operator

The `in` operator tests membership or containment:

```stash
println(3 in [1, 2, 3, 4]);     // true  — array membership
println(5 in [1, 2, 3, 4]);     // false
println("o" in "hello");        // true  — substring/char check
println("key" in myDict);       // true  — dictionary key existence
println(3 in 1..10);            // true  — range membership
println(10 in 1..10);           // false (end-exclusive)
```

### Semantics by Type

| Right-hand side | Test performed                                     |
| --------------- | -------------------------------------------------- |
| `array`         | Element equality (`==`) against each item          |
| `string`        | Substring / character containment                  |
| `dict`          | Key existence (equivalent to `dict.has(d, key)`)   |
| `range`         | Integer falls within the range respecting the step |

Using `in` against any other type is a runtime error.

### Precedence

`in` has the same precedence as the comparison operators (`<`, `>`, `<=`, `>=`) and is non-associative with them. `x in a..b` is parsed as `x in (a..b)` because `..` binds tighter.

---

## 5b. Enums

Enums provide named constants that eliminate magic strings and arbitrary integer values, making code self-documenting.

### Declaration

```stash
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

```stash
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

```stash
let d = dict.new();       // empty dictionary
d["name"] = "Alice";      // set via index syntax
d.age = 30;               // set via dot access
```

### Key Types

Dictionary keys must be **value types**: `string`, `int`, `float`, or `bool`. Using any other type as a key (arrays, structs, functions, `null`) produces a runtime error.

### Access

Dictionaries support index syntax (`d[key]`) for both reading and writing:

```stash
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

```stash
let config = dict.new();
config["host"] = "localhost";
config["port"] = 8080;

for (let key in config) {
    io.println(key + " = " + config[key]);
}
```

### Built-in Integration

```stash
typeof(dict.new())    // "dict"
len(d)                // number of key-value pairs
```

### Internal Representation

A dictionary is backed by a hash map (`Dictionary<object, object?>` in C#). Key lookup is O(1) average. The `dict` namespace provides all manipulation functions (see Section 8).

---

## 5d. Dictionary Dot Access

Dictionaries support **dot notation** for reading and writing string-keyed entries, providing a convenient alternative to bracket notation when keys are valid identifiers.

### Reading

```stash
let d = dict.new();
d["name"] = "Alice";
d["age"] = 30;

// These are equivalent:
let name1 = d["name"];   // bracket notation
let name2 = d.name;      // dot notation
```

Dot access returns `null` for missing keys — the same behavior as bracket notation:

```stash
let missing = d.nonexistent;  // null (no error)
```

### Writing

```stash
let d = dict.new();
d.host = "localhost";    // creates the key "host"
d.port = 8080;           // creates the key "port"
d.host = "10.0.0.1";    // overwrites existing key
```

### Nested Access

Dot notation chains naturally for nested dictionaries:

```stash
let cfg = json.parse("{\"database\": {\"host\": \"localhost\", \"port\": 5432}}");

// Nested dot access
let host = cfg.database.host;    // "localhost"
let port = cfg.database.port;    // 5432

// Nested dot assignment
cfg.database.port = 3306;
```

This is especially powerful with `config.read()` and `ini.parse()`, where config files are loaded as nested dictionaries and accessed with a clean, natural syntax.

### When to Use Bracket vs. Dot Notation

| Syntax     | Use when                                                 |
| ---------- | -------------------------------------------------------- |
| `d["key"]` | Key is dynamic, computed, or contains special characters |
| `d.key`    | Key is a known identifier — cleaner and more readable    |

Both notations are fully interchangeable for string keys. Bracket notation is required for non-string keys (`d[42]`).

### Interaction with Other Types

Dot notation works on **dictionaries**, **struct instances**, **enums**, and **namespaces**. For struct instances, dot access validates that the field exists (throws if not). For dictionaries, dot access simply performs a string key lookup (returns `null` if missing — no error).

---

## 5e. Optional Chaining

The `?.` operator provides safe member access on potentially-null values. If the left-hand side is `null`, the expression short-circuits to `null` instead of throwing a runtime error:

```stash
let port = config?.database?.port;       // null if config or database is null
let port = config?.database?.port ?? 3306;  // with default via null-coalescing
```

### Semantics

1. `a?.b` evaluates `a`. If `a` is `null`, the result is `null` — `b` is never accessed.
2. If `a` is not `null`, `a?.b` behaves identically to `a.b` — field access on struct instances, key lookup on dictionaries, member access on enums and namespaces.
3. Multiple `?.` operators can be chained: `a?.b?.c` — each link independently checks for `null`.
4. Composes naturally with `??` (null-coalescing): `a?.b ?? default` returns `default` when any step is `null`.

### Comparison with Regular Dot

| Syntax | Left is `null` | Left is non-null |
| ------ | -------------- | ---------------- |
| `a.b`  | Runtime error  | Field/key access |
| `a?.b` | Returns `null` | Field/key access |

### Examples

```stash
// Safe navigation through nested config
let d = dict.new();
d["db"] = null;
let host = d?.db?.host;       // null (db is null, no error)
let host2 = d?.db?.host ?? "localhost";  // "localhost"

// Struct field access
struct Server { host, port }
let srv = Server { host: "10.0.0.1", port: 22 };
let h = srv?.host;            // "10.0.0.1"

let empty = null;
let h2 = empty?.host;         // null (no error)
```

### Implementation

The `?.` operator is implemented as a `QuestionDot` token type. `DotExpr` has an `IsOptional` boolean flag (default `false`). When the parser encounters `?.`, it creates a `DotExpr` with `IsOptional = true`. At runtime, the interpreter checks this flag: if the object evaluates to `null` and `IsOptional` is `true`, it returns `null` immediately instead of throwing.

---

## 6. Shell Integration

### Process Execution

Commands are executed via **command literals** — a dedicated syntax that makes shell commands first-class in the language without wrapping them in strings.

#### Syntax: `$(command)` — Command Literals

```stash
let result = $(ls -la);
io.println(result.stdout);      // captured standard output
io.println(result.stderr);      // captured standard error
io.println(result.exitCode);    // process exit code
```

`$(...)` is **always raw mode**. When the lexer encounters `$(`, it enters "command mode" and collects everything as raw text until the matching `)`. The content is not parsed as a Stash expression — it is treated as a command string that is split into a program name and arguments. Programs are invoked directly, not through a system shell.

To inject dynamic values into a command, use interpolation with `{...}`:

```stash
// Raw mode — command text is written directly
let r1 = $(ls -la);

// Dynamic values via interpolation
let flags = buildFlags();
let r2 = $(ls {flags});

// Full dynamic command — interpolate the entire string
let cmd = "echo hello";
let r3 = $({cmd});
```

This makes `$(...)` the **single, unified way** to execute commands. The `{...}` interpolation syntax within commands is consistent with how interpolation works elsewhere in the language.

`$(...)` returns a struct-like object with `stdout`, `stderr`, and `exitCode` fields.

#### Interpolation in Commands

Variables and expressions can be embedded using `{...}`:

```stash
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

```stash
let lines = $(cat /var/log/syslog) | $(grep error) | $(wc -l);
```

The `|` operator is **exclusive to command chaining** — it pipes stdout of the left process to stdin of the right. It is not a general-purpose operator and cannot be used between non-command expressions. For logical OR, use `||`.

#### Short-Circuit on Failure

Pipe chains **short-circuit on failure**. If any command in the chain exits with a non-zero exit code, the remaining commands are not executed. The result of the entire pipe expression is the `CommandResult` of the **failed command** (or the last command if all succeed).

```stash
let result = $(cat /var/log/syslog) | $(grep error) | $(wc -l);
// If 'cat' fails (exitCode != 0), 'grep' and 'wc' are never started.
// result.exitCode reflects the failed command's exit code.
// result.stderr contains the failed command's error output.
```

This mirrors Bash's `set -o pipefail` behavior and prevents silent failures in command chains.

#### Output Redirection

Command output can be redirected to files using `>` (write) and `>>` (append). See [Section 6c](#6c-output-redirection) for details.

```stash
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

```stash
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

## 7. Control Flow

### If / Else

```stash
if (condition) {
    // ...
} else if (other) {
    // ...
} else {
    // ...
}
```

### While Loop

```stash
while (condition) {
    // ...
}
```

### Do-While Loop

A `do-while` loop executes its body **at least once**, then repeats while the condition remains truthy:

```stash
do {
    let input = io.readLine("Enter 'yes': ");
} while (input != "yes");
```

Standard `break` and `continue` are supported inside `do-while` loops. The semicolon after the closing `)` is required.

### For Loop

```stash
for (let item in collection) {
    // ...
}
```

Only the `for-in` form is supported. C-style `for (init; condition; increment)` is intentionally excluded — it adds complexity without significant benefit for a scripting language. May be reconsidered in a future version.

#### For-in with Index

A two-variable form provides the iteration index alongside each value:

```stash
for (let i, item in ["a", "b", "c"]) {
    io.println($"{i}: {item}");
}
// Output: 0: a, 1: b, 2: c
```

The first variable (`i`) receives the zero-based index (as an integer), and the second variable (`item`) receives the element value. This works with arrays, strings, and ranges:

```stash
for (let i, ch in "hello") {
    io.println($"{i}: {ch}");   // 0: h, 1: e, 2: l, ...
}
```

The index is independent of the collection's values — for ranges, the index counts iterations while the value yields range elements:

```stash
for (let i, val in 5..8) {
    io.println($"index={i}, value={val}");
}
// index=0, value=5
// index=1, value=6
// index=2, value=7
```

#### Dictionary Key-Value Iteration

For dictionaries, the two-variable form iterates over **key-value pairs** instead of index-key pairs:

```stash
let config = dict.new();
config["host"] = "localhost";
config["port"] = 8080;

for (let key, value in config) {
    io.println($"{key} = {value}");
}
// host = localhost
// port = 8080
```

This follows Go's `for k, v := range m` pattern. The first variable receives the dictionary key and the second receives the corresponding value. Single-variable iteration still yields keys only:

```stash
for (let key in config) {
    io.println(key);  // "host", "port"
}
```

#### Iterable Types

- **`array`** — iterates over elements in order: `for (let item in [1, 2, 3]) { ... }`
- **`string`** — iterates over characters: `for (let ch in "hello") { ... }` yields `"h"`, `"e"`, `"l"`, `"l"`, `"o"`
- **`dict`** — iterates over keys: `for (let key in myDict) { ... }`. With two variables, iterates key-value pairs: `for (let key, value in myDict) { ... }`
- **`range`** — iterates over integer values: `for (let i in 1..10) { ... }` yields `1` through `9` (end-exclusive)

All other types produce a runtime error when used as the right-hand side of `for-in`.

#### Snapshot Safety

All `for-in` loops iterate over a **snapshot** of the collection taken at loop entry. Modifications to the collection during iteration (adding, removing, or replacing elements) do not affect the loop's iteration order or count:

```stash
let items = [1, 2, 3];
for (let item in items) {
    arr.push(items, item * 10);  // safe — does not affect iteration
    io.println(item);            // prints 1, 2, 3
}
// items is now [1, 2, 3, 10, 20, 30]

let d = dict.new();
d["a"] = 1; d["b"] = 2;
for (let key in d) {
    dict.remove(d, key);  // safe — snapshot preserves original keys
}
// d is now empty
```

This applies to arrays and dictionaries. Strings and ranges are inherently safe (strings are immutable; ranges yield computed values).

### Break / Continue

Standard `break` and `continue` within loops.

### Null-Coalescing Operator (`??`)

The `??` operator returns the left operand if it is not `null`, otherwise returns the right operand:

```stash
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

```stash
// Without try — script crashes if file doesn't exist
let content = fs.readFile("/etc/missing.conf");

// With try — error becomes null
let content = try fs.readFile("/etc/missing.conf");

// With try + ?? — error becomes a default value
let content = try fs.readFile("/etc/missing.conf") ?? "default config";
```

### Error Details

When you need to know _what_ went wrong, the `lastError()` built-in returns the **most recent** error message as a string (or `null` if no error occurred):

```stash
let data = try conv.toInt("abc");
if (data == null) {
    io.println(lastError());  // "Cannot parse 'abc' as integer"
}
```

**Note:** `lastError()` returns only the single most recent error. If multiple `try` expressions execute in sequence, only the last error is retained. This is a known limitation — sufficient for v1 scripting use cases.

### Shell Commands Don't Need `try`

Shell command results already carry structured error information via `exitCode` and `stderr` — they never crash the script:

```stash
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

```stash
let result = value switch {
    pattern1 => result1,
    pattern2 => result2,
    _ => defaultResult
};
```

### Examples

```stash
let day = "Monday";
let type = day switch {
    "Saturday" => "weekend",
    "Sunday" => "weekend",
    _ => "weekday"
};
io.println(type);  // "weekday"
```

```stash
let status = exitCode switch {
    0 => "success",
    1 => "warning",
    2 => "error",
    _ => "unknown"
};
```

Switch expressions work with any value type — integers, strings, booleans, null, and enum values:

```stash
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

```stash
let score = grade switch {
    "A" => 100,
    "B" => 85,
    "C" => 70,
    _ => 0
};
```

### Trailing Commas

A trailing comma after the last arm is permitted:

```stash
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

```stash
fn greet(name) {
    io.println("Hello, " + name);
}

fn add(a, b) {
    return a + b;
}
```

### Default Parameter Values

Function parameters can have **default values** — if the caller omits an argument, the default is used instead. Default parameters must be **trailing** (right-to-left), same as C#.

```stash
fn greet(name, greeting = "Hello") {
    io.println(greeting + ", " + name);
}

greet("Alice");           // "Hello, Alice"
greet("Alice", "Hi");     // "Hi, Alice"
```

Default values work with optional type annotations:

```stash
fn connect(host: string, port: int = 8080, secure: bool = false) {
    io.println("Connecting to " + host + ":" + conv.toStr(port));
}

connect("localhost");                  // port=8080, secure=false
connect("localhost", 443);             // secure=false
connect("localhost", 443, true);       // all provided
```

**Rules:**

- Once a parameter has a default value, all subsequent parameters must also have defaults
- Default values are expressions evaluated at **call time** (not definition time)
- Calling with too few required arguments or too many total arguments is a runtime error

```stash
// Parse error — non-default after default
fn bad(a = 1, b) { }

// Runtime error — 'a' is required
fn f(a, b = 5) { return a + b; }
f();  // Error: Expected 1 to 2 arguments but got 0
```

### Implicit Return Value

Functions that do not execute a `return` statement implicitly return `null`:

```stash
fn greet(name) {
    io.println("Hello, " + name);
}

let result = greet("world");  // result is null
```

### Closures

Functions capture their enclosing lexical environment:

```stash
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

| Function           | Description                               |
| ------------------ | ----------------------------------------- |
| `typeof(val)`      | Return the type of a value as string      |
| `len(val)`         | Length of a string or array               |
| `lastError()`      | Last error message (string) or null       |
| `parseArgs(t)`     | Parse command-line arguments              |
| `test(s, f)`       | Run a test                                |
| `declare(s, f)`    | Group tests together                      |
| `captureOutput(f)` | Redirects the output of any internal call |

All other built-in functions are organized into namespaces (see below).

### Built-in Namespaces

Stash organizes built-in functions into **namespaces** accessed via dot notation. A small set of fundamental functions remain global (see above); everything else lives in a namespace.

Available namespaces: `io`, `conv`, `env`, `fs`, `path`, `str`, `arr`, `dict`, `math`, `time`, `json`, `ini`, `config`, `http`, `process`, `assert`.

Namespace members are accessed with dot notation: `fs.exists("/etc/hosts")`. Namespaces are first-class values — `typeof(fs)` returns `"namespace"`. Assignment to namespace members is not permitted.

See the [Standard Library Reference](Stash%20—%20Standard%20Library%20Reference.md) for complete documentation of all namespace functions.

---

## 8b. Lambda Expressions

Lambda expressions (arrow functions) provide a concise syntax for creating anonymous functions. They are first-class values that can be assigned to variables, passed as arguments, and returned from functions.

### Syntax

**Expression body** — implicit return of a single expression:

```stash
let double = (x) => x * 2;
let add = (a, b) => a + b;
let greet = () => "hello";
```

**Block body** — explicit `return` for multi-statement logic:

```stash
let abs = (x) => {
    if (x < 0) {
        return -x;
    }
    return x;
};
```

### Parameters

Lambdas support zero or more parameters, with optional type annotations and default values:

```stash
let noParams = () => 42;
let oneParam = (x) => x + 1;
let typed = (x: int, y: int) => x + y;
let withDefault = (x, factor = 2) => x * factor;
```

Default values follow the same trailing-only rules as named functions:

```stash
let connect = (host: string, port: int = 8080) => host + ":" + conv.toStr(port);
connect("localhost");        // "localhost:8080"
connect("localhost", 443);   // "localhost:443"
```

### Closures

Lambdas capture their enclosing lexical environment, just like named functions:

```stash
fn makeMultiplier(factor) {
    return (x) => x * factor;
}

let triple = makeMultiplier(3);
io.println(triple(5));  // 15
```

### Higher-Order Usage

Lambdas are particularly useful when passed as arguments to other functions:

```stash
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

```stash
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

```stash
import { deploy, Server } from "utils.stash";
import { Status } from "enums.stash";

// Use imported names directly
let srv = Server { host: "10.0.0.1", port: 22, status: Status.Active };
deploy(srv, "app.tar.gz");
```

Only the names listed in `{ ... }` are made available in the importing script's scope. Other declarations in the imported file are not visible.

**Namespace import** — import an entire module as a namespace:

```stash
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

Keywords: `let`, `const`, `fn`, `struct`, `enum`, `if`, `else`, `for`, `in`, `while`, `do`, `return`, `break`, `continue`, `true`, `false`, `null`, `try`, `import`, `as`, `switch`, `and`, `or`

`and` and `or` are keyword aliases for `&&` and `||` respectively — they have identical precedence, short-circuit behavior, and semantics.

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
- `StructDeclStmt` — `struct Name { fields, fn methods... }`
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

### DAP (Debug Adapter Protocol)

The debug hook interface above enables integration with VS Code and other editors through the Debug Adapter Protocol. The DAP server (`Stash.Dap`) is a thin translation layer on top of these hooks. For full DAP server documentation, see [DAP — Debug Adapter Protocol](specs/DAP%20—%20Debug%20Adapter%20Protocol.md).

### Testing Hooks

The testing infrastructure follows the same architectural pattern — an `ITestHarness` interface with the same null-guard approach for zero overhead. For testing built-ins (`test()`, `describe()`, `assert` namespace) and TAP output, see [TAP — Testing Infrastructure](specs/TAP%20—%20Testing%20Infrastructure.md).

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

| Optimization                             | Where              | Impact  | Status                                                                            |
| ---------------------------------------- | ------------------ | ------- | --------------------------------------------------------------------------------- |
| String interning                         | Lexer              | High    | ✅ Done — `string.Intern()` in `ScanIdentifier()`                                 |
| `FrozenDictionary` for keywords/builtins | Lexer, Interpreter | Low-Med | ✅ Done — Lexer keywords + `StashNamespace.Freeze()` for all built-in namespaces  |
| `ReadOnlySpan<char>` in lexer            | Lexer              | Medium  | ✅ Done — Span-based `ScanNumber()` parsing + `GetAlternateLookup` keyword lookup |

#### Tier 2 — Architectural (Apply After v1 Works)

| Optimization                                      | Where                  | Impact  | Status                                                                                      |
| ------------------------------------------------- | ---------------------- | ------- | ------------------------------------------------------------------------------------------- |
| Variable resolution at parse time (resolver pass) | Resolver + Interpreter | Highest | ✅ Done — `Resolver` class computes scope distances; `GetAt()`/`AssignAt()` used at runtime |
| Slot-based environments (array, not dictionary)   | Environment            | High    | ❌ Not started — `Dictionary<string, object?>` backing                                      |
| Pre-sized argument lists for function calls       | Interpreter            | Low     | ✅ Done — `List<object?>(expr.Arguments.Count)` pre-allocation                              |

#### Tier 3 — Nuclear Option

If the tree-walk interpreter hits a performance wall: **switch to a bytecode VM**. This is a 10-50x speedup and dwarfs all micro-optimizations. The bytecode VM compiles the AST to a flat array of opcodes, and a tight `switch`-dispatch loop executes them. ❌ Not started.

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
- ~~Methods on structs~~ ✅ Implemented
- C-style `for(;;)` loops
- Regular expressions

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

| Resource                                                                          | Description                                                   |
| --------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| [DAP — Debug Adapter Protocol](specs/DAP%20—%20Debug%20Adapter%20Protocol.md)     | Stash debug adapter server — breakpoints, stepping, variables |
| [LSP — Language Server Protocol](specs/LSP%20—%20Language%20Server%20Protocol.md) | Stash language server — diagnostics, completion, navigation   |
| [TAP — Testing Infrastructure](specs/TAP%20—%20Testing%20Infrastructure.md)       | Testing primitives, assert namespace, TAP output              |

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
varDecl        → "let" ( IDENTIFIER | destructurePattern ) "=" expression ";" ;
destructurePattern → "[" IDENTIFIER ("," IDENTIFIER)* "]" | "{" IDENTIFIER ("," IDENTIFIER)* "}" ;
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
assignment     → (call ".")? IDENTIFIER ("=" | "+=" | "-=" | "*=" | "/=" | "%=" | "??=") assignment | ternary ;
ternary        → nullCoalesce ( "?" expression ":" ternary )? ;
nullCoalesce   → redirect ( "??" redirect )* ;
redirect       → pipe ( redirectOp expression )* ;
redirectOp     → ">" | ">>" | "2>" | "2>>" | "&>" | "&>>" ;
pipe           → logic_or ( "|" logic_or )* ;
logic_or       → logic_and ( "||" | "or" logic_and )* ;
logic_and      → equality ( "&&" | "and" equality )* ;
equality       → comparison ( ("==" | "!=") comparison )* ;
comparison     → range ( ("<" | ">" | "<=" | ">=" | "in") range )* ;
range          → term ( ".." term ( ".." term )? )? ;
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

parameter      → IDENTIFIER ( ":" IDENTIFIER )? ( "=" expression )? ;
parameters     → parameter ( "," parameter )* ;
arguments      → expression ( "," expression )* ;
```

---

_This is a living document. Update as design decisions are finalized and implementation progresses._
