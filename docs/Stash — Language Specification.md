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

**Addenda:** [3b. Compound Assignment Operators](#3b-compound-assignment-operators) · [3c. Multi-line Strings](#3c-multi-line-strings) · [3d. Range Expressions](#3d-range-expressions) · [3e. Destructuring Assignment](#3e-destructuring-assignment) · [4b. The `in` Operator](#4b-the-in-operator) · [4c. The `is` Operator](#4c-the-is-operator) · [5b. Enums](#5b-enums) · [5c. Dictionaries](#5c-dictionaries) · [5d. Dictionary Dot Access](#5d-dictionary-dot-access) · [5e. Optional Chaining](#5e-optional-chaining) · [5f. Interfaces](#5f-interfaces) · [6b. Shebang Support](#6b-shebang-support) · [6c. Output Redirection](#6c-output-redirection) · [6d. Privilege Elevation (`elevate`)](#6d-privilege-elevation-elevate) · [7b. Error Handling](#7b-error-handling) · [7c. Switch Expressions](#7c-switch-expressions) · [7d. Retry Blocks](#7d-retry-blocks) · [8b. Lambda Expressions](#8b-lambda-expressions) · [8c. UFCS — Uniform Function Call Syntax](#8c-ufcs--uniform-function-call-syntax) · [8d. Extend Blocks — Type Extension Methods](#8d-extend-blocks--type-extension-methods) · [9b. Module / Import System](#9b-module--import-system)

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
- Class-based OOP with inheritance (lightweight interfaces **are** supported — see [Section 5f](#5f-interfaces))
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

Standard C-style: `+`, `-`, `*`, `/`, `%`, `==`, `!=`, `<`, `>`, `<=`, `>=`, `&&`, `||`, `!`, `?:` (ternary), `??` (null-coalescing), `?.` (optional chaining, see [Section 5e](#5e-optional-chaining)), `++` (increment), `--` (decrement). Bitwise: `&` (AND), `|` (OR), `^` (XOR), `~` (NOT), `<<` (left shift), `>>` (right shift) — see [Section 3g](#3g-bitwise-operators). Compound assignment: `+=`, `-=`, `*=`, `/=`, `%=`, `??=`, `&=`, `|=`, `^=`, `<<=`, `>>=` (see [Section 3b](#3b-compound-assignment-operators)). Range: `..` (see [Section 3d](#3d-range-expressions)). Membership: `in` (see [Section 4b](#4b-the-in-operator)). Type checking: `is` (see [Section 4c](#4c-the-is-operator)).

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

| Tag                       | Description                    |
| ------------------------- | ------------------------------ |
| `@param name description` | Documents a function parameter |
| `@return description`     | Documents the return value     |
| `@returns description`    | Alias for `@return`            |

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
let result = $(ping -c 1 ${srv.host});

// Property assignment
srv.status = result.exitCode == 0 ? Status.Active : Status.Inactive;

// Function definition
fn deploy(server, package) {
  let r = $(scp ${package} ${server.host}:/opt/);
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

// C-style for loop
for (let i = 0; i < len(servers); i++) {
  io.println("Server " + conv.toStr(i));
}

// Output redirection — write command output to files
$(ls -la /opt) > "/tmp/listing.txt";
$(make build) 2> "/tmp/errors.log";
$(cat /tmp/listing.txt) | $(grep app) >> "/tmp/matches.txt";
```

---

## 4. Type System

Dynamically typed. Values carry their type at runtime. The following built-in types exist:

| Type        | Examples                                   | Notes                                         |
| ----------- | ------------------------------------------ | --------------------------------------------- |
| `int`       | `42`, `-7`, `0`, `0xFF`, `0o755`, `0b1010` | Integer numbers (decimal, hex, octal, binary) |
| `float`     | `3.14`, `-0.5`                             | Floating-point numbers                        |
| `string`    | `"hello"`, `""`                            | Immutable strings                             |
| `bool`      | `true`, `false`                            |                                               |
| `null`      | `null`                                     | Absence of value                              |
| `array`     | `[1, 2, 3]`, `["a", 42, true]`             | Ordered, mixed-type, dynamic-size             |
| `struct`    | `Server { host: "...", ... }`              | Named structured data (see Section 5)         |
| `enum`      | `Status.Active`, `Color.Red`               | Named constants (see Section 5b)              |
| `dict`      | `{ key: value }`, `dict.new()`             | Key-value map (see Section 5c)                |
| `interface` | `interface Printable { ... }`              | Structural contract for structs (see §5f)     |
| `range`     | `1..10`, `0..100..5`                       | Lazy integer sequence (see Section 3d)        |
| `Error`     | `try failingFn()`                          | Error value (see Section 7b)                  |
| `Future`    | `async fn() { return 42; }`                | Async computation (see Section 8c)            |
| `ip`        | `@192.168.1.1`, `@::1`, `@10.0.0.0/24`     | IP address — IPv4/IPv6 with optional CIDR     |
| `duration`  | `5s`, `500ms`, `2h30m`, `1.5h`             | Time duration with unit suffixes              |
| `bytes`     | `100B`, `1.5KB`, `256MB`, `2GB`            | Byte size with binary unit suffixes           |

### Number Literals

Stash supports integer literals in four bases and optional underscore digit separators for readability.

| Format         | Prefix      | Digits              | Example                 |
| -------------- | ----------- | ------------------- | ----------------------- |
| Decimal        | _(none)_    | `0-9`               | `42`, `1_000_000`       |
| Hexadecimal    | `0x` / `0X` | `0-9`, `a-f`, `A-F` | `0xFF`, `0x00FF_00FF`   |
| Octal          | `0o` / `0O` | `0-7`               | `0o755`, `0o7_5_5`      |
| Binary         | `0b` / `0B` | `0`, `1`            | `0b1010`, `0b1111_0000` |
| Floating-point | _(none)_    | `0-9`, `.`          | `3.14`, `1_000.50`      |

**Underscore separators (`_`):** An underscore may appear between any two digits for readability. Underscores are not allowed at the start or end of a literal, may not appear consecutively, may not appear adjacent to a decimal point, and may not appear immediately after a base prefix.

```stash
let permissions = 0o755;           // octal: 493
let color = 0xFF00FF;              // hex: 16711935
let flags = 0b1010;                // binary: 10
let million = 1_000_000;           // underscore separator: 1000000
let mask = 0xFF_FF_00_00;          // hex with separators
let bits = 0b1111_0000_1010_0101;  // binary with separators
```

All hex, octal, and binary literals produce integer (`long`) values. Floating-point hex/octal/binary is not supported.

### IP Address Literals

IP address literals use the `@` prefix — a single character followed by the address with no quotes:

```stash
let addr   = @192.168.1.1;            // IPv4
let v6     = @::1;                     // IPv6 loopback
let mapped = @::ffff:192.168.1.1;     // IPv4-mapped IPv6
let cidr   = @10.0.0.0/24;            // Subnet (CIDR notation)
let link   = @fe80::1%eth0;           // IPv6 with zone ID
```

The `@` sigil means "at an address" — it already carries this meaning in networking contexts (SSH `user@host`, email `user@domain`). The lexer sees `@` and enters dedicated IP-address scanning mode, consuming hex digits, dots, colons (for IPv6), `/` (for CIDR), and `%` (for zone IDs), stopping at whitespace or any operator/delimiter.

**Why not bare `192.168.1.1`?** Bare IP addresses create deep lexer ambiguity. `10.0` is a valid float. `192.168` would tokenize as `192`, `.`, `168` (integer-dot-integer). IPv6 is impossible without a delimiter — `::1` and `fe80::1%eth0` cannot be expressed as bare tokens.

#### Type System

IP addresses are a first-class type with value-based equality:

```stash
typeof(@192.168.1.1)                    // "ip"
@192.168.1.1 is ip                      // true
@192.168.1.1 == @192.168.1.1            // true (value equality)
@192.168.1.1 == "192.168.1.1"           // false (no cross-type coercion)
$"Server: {@192.168.1.1}"               // "Server: 192.168.1.1"
```

IP addresses with different CIDR prefixes are distinct: `@10.0.0.0/24 != @10.0.0.0/16`. An IP without a prefix is distinct from one with a prefix: `@10.0.0.0 != @10.0.0.0/24`.

#### Operator Integration

Bitwise, comparison, arithmetic, and containment operators work natively on IP addresses:

**Bitwise** — subnet masking with `&`, `|`, `~`:

```stash
let addr = @192.168.1.100;
let mask = @255.255.255.0;
let network   = addr & mask;           // @192.168.1.0
let broadcast = (addr & mask) | ~mask; // @192.168.1.255
let wildcard  = ~mask;                 // @0.0.0.255
```

**Comparison** — lexicographic byte ordering with `<`, `>`, `<=`, `>=`:

```stash
@10.0.0.1 < @10.0.0.254               // true
@192.168.1.1 > @10.0.0.1              // true
```

**Arithmetic** — address offset with `+` and `-`:

```stash
@10.0.0.0 + 42                        // @10.0.0.42
@192.168.1.254 + 2                    // @192.168.2.0 (wraps octets)
@10.0.0.42 - @10.0.0.0                // 42 (integer distance)
@10.0.0.42 - 42                       // @10.0.0.0
```

**CIDR containment** — subnet membership with `in`:

```stash
@192.168.1.50 in @192.168.1.0/24      // true
@192.168.2.1 in @192.168.1.0/24       // false
```

IPv4 and IPv6 addresses cannot be mixed in operators — `@192.168.1.1 & @::1` is a runtime error.

### Duration Literals

Duration literals express time spans directly in code with unit suffixes. The numeric value is followed immediately (no space) by a unit:

| Unit         | Suffix | Example          |
| ------------ | ------ | ---------------- |
| Milliseconds | `ms`   | `500ms`, `100ms` |
| Seconds      | `s`    | `5s`, `1.5s`     |
| Minutes      | `m`    | `30m`, `2.5m`    |
| Hours        | `h`    | `1h`, `1.5h`     |
| Days         | `d`    | `7d`, `365d`     |

**Compound durations** combine multiple units in descending order:

```stash
let timeout = 2h30m;               // 2 hours 30 minutes
let precise = 1h30m15s;            // 1 hour 30 minutes 15 seconds
let full    = 1d12h30m15s500ms;    // compound with all units
```

Underscore separators work in the leading number: `1_000ms`. Float values are supported for the first number only: `1.5h` (90 minutes). Compound continuation segments are integer-only.

Internally, durations are stored as total milliseconds (a 64-bit integer).

#### Type System

```stash
typeof(5s)                             // "duration"
5s is duration                         // true
5s == 5000ms                           // true (value equality by total ms)
5s == 5                                // false (no cross-type coercion)
$"timeout: {2h30m}"                    // "timeout: 2h30m"
```

#### Properties

Durations support dot-access properties in two categories:

**Component properties** — extract a single unit from the decomposed duration (like reading a clock):

| Property        | Type  | Description                    | Example: `2h30m15s500ms` |
| --------------- | ----- | ------------------------------ | ------------------------ |
| `.days`         | `int` | Full days (0+)                 | `0`                      |
| `.hours`        | `int` | Hours component (0–23)         | `2`                      |
| `.minutes`      | `int` | Minutes component (0–59)       | `30`                     |
| `.seconds`      | `int` | Seconds component (0–59)       | `15`                     |
| `.milliseconds` | `int` | Milliseconds component (0–999) | `500`                    |

**Total properties** — express the entire duration in a single unit:

| Property        | Type    | Description        | Example: `2h30m` |
| --------------- | ------- | ------------------ | ---------------- |
| `.totalMs`      | `int`   | Total milliseconds | `9000000`        |
| `.totalSeconds` | `float` | Total seconds      | `9000.0`         |
| `.totalMinutes` | `float` | Total minutes      | `150.0`          |
| `.totalHours`   | `float` | Total hours        | `2.5`            |
| `.totalDays`    | `float` | Total days         | `0.104167`       |

#### Operator Integration

**Arithmetic:**

```stash
5s + 3s                                // 8s
10s - 3s                               // 7s
5s * 3                                 // 15s
3 * 5s                                 // 15s (commutative)
5s * 1.5                               // 7500ms
15s / 3                                // 5s
10s / 5s                               // 2.0 (float ratio)
-5s                                    // negation (-5000ms)
```

**Comparison:**

```stash
5s > 3s                                // true
1h == 60m                              // true (both are 3600000ms)
1h > 59m                               // true
```

Duration and non-duration types cannot be mixed in operators — `5s + 1KB` is a runtime error.

### ByteSize Literals

ByteSize literals express data sizes with binary unit suffixes (1 KB = 1024 bytes):

| Unit      | Suffix | Bytes             | Example          |
| --------- | ------ | ----------------- | ---------------- |
| Bytes     | `B`    | 1                 | `100B`, `0KB`    |
| Kilobytes | `KB`   | 1,024             | `1KB`, `1.5KB`   |
| Megabytes | `MB`   | 1,048,576         | `256MB`, `512MB` |
| Gigabytes | `GB`   | 1,073,741,824     | `2GB`, `1.5GB`   |
| Terabytes | `TB`   | 1,099,511,627,776 | `1TB`            |

```stash
let maxSize  = 100MB;
let diskSize = 2TB;
let chunk    = 1.5KB;
let buffer   = 1_024B;
```

Internally, byte sizes are stored as total bytes (a 64-bit integer). Float values are supported: `1.5GB`. Unlike durations, byte sizes do not have compound syntax — each literal uses a single unit.

**Note:** `0b1010` and `0B1010` are binary literals (not byte sizes). The `B` suffix is only treated as a byte unit when not followed by a binary digit — so `0B` is zero bytes, while `0B1` is the binary literal for 1.

#### Type System

```stash
typeof(1KB)                            // "bytes"
1KB is bytes                           // true
1KB == 1024B                           // true (value equality by total bytes)
1KB == 1024                            // false (no cross-type coercion)
$"size: {1536B}"                       // "size: 1.5KB"
```

#### Properties

| Property | Type    | Description             | Example: `1536B` |
| -------- | ------- | ----------------------- | ---------------- |
| `.bytes` | `int`   | Total bytes (raw value) | `1536`           |
| `.kb`    | `float` | Value in kilobytes      | `1.5`            |
| `.mb`    | `float` | Value in megabytes      | `0.001465`       |
| `.gb`    | `float` | Value in gigabytes      | `0.000001`       |
| `.tb`    | `float` | Value in terabytes      | `0.0`            |

#### Operator Integration

**Arithmetic:**

```stash
1KB + 1KB                              // 2KB (2048 bytes)
1MB - 512KB                            // 512KB
1KB * 3                                // 3KB (3072 bytes)
3 * 1KB                                // 3KB (commutative)
1MB / 2                                // 512KB
1MB / 512KB                            // 2.0 (float ratio)
-1KB                                   // negation (-1024 bytes)
```

**Comparison:**

```stash
1MB > 1KB                              // true
1KB == 1024B                           // true
1GB > 999MB                            // true
```

ByteSize and non-bytesize types cannot be mixed in operators — `1KB + 5s` is a runtime error.

### Semantic Version Literals

Semantic version literals use the `@v` prefix for inline version values following [SemVer 2.0.0](https://semver.org/):

```stash
let current    = @v2.4.1
let minimum    = @v2.0.0
let prerelease = @v3.0.0-beta.2
let tagged     = @v1.0.0-rc.1+build.456
let wildcard   = @v2.x                    // matches any 2.x.x
```

**Format:** `@v<major>.<minor>.<patch>` with optional `-<prerelease>` and `+<build>` suffixes. Wildcard patterns use `x` for minor or patch: `@v2.x` (any 2.x.x) or `@v2.4.x` (any 2.4.x).

| Component  | Description                            | Example                                        |
| ---------- | -------------------------------------- | ---------------------------------------------- |
| Major      | Breaking changes                       | `@v2.0.0` → `.major` = 2                       |
| Minor      | New features (backward-compatible)     | `@v2.4.0` → `.minor` = 4                       |
| Patch      | Bug fixes                              | `@v2.4.1` → `.patch` = 1                       |
| Prerelease | Pre-release identifier                 | `@v1.0.0-beta.2` → `.prerelease` = `"beta.2"`  |
| Build      | Build metadata (ignored in comparison) | `@v1.0.0+build.123` → `.build` = `"build.123"` |

#### Type System

```stash
typeof(@v1.0.0)                         // "semver"
@v1.0.0 is semver                       // true
@v1.0.0 == @v1.0.0                      // true
@v1.0.0 == "1.0.0"                      // false (no cross-type coercion)
$"version: {@v2.4.1}"                   // "version: 2.4.1"
```

#### Properties

| Property        | Type     | Description                                   | Example: `@v2.4.1-beta.2+build.5` |
| --------------- | -------- | --------------------------------------------- | --------------------------------- |
| `.major`        | `int`    | Major version number                          | `2`                               |
| `.minor`        | `int`    | Minor version number                          | `4`                               |
| `.patch`        | `int`    | Patch version number                          | `1`                               |
| `.prerelease`   | `string` | Pre-release identifier (empty string if none) | `"beta.2"`                        |
| `.build`        | `string` | Build metadata (empty string if none)         | `"build.5"`                       |
| `.isPrerelease` | `bool`   | Whether version has a pre-release tag         | `true`                            |

#### Comparison

Comparison follows the [SemVer 2.0.0 precedence rules](https://semver.org/#spec-item-11):

1. Major → Minor → Patch compared numerically (not lexicographically: `@v1.10.0 > @v1.9.0`)
2. A release version has **higher** precedence than its pre-release: `@v2.0.0 > @v2.0.0-alpha`
3. Pre-release identifiers compared by dot-separated segments: numeric segments numerically, alphanumeric segments lexicographically
4. Build metadata is **ignored** in all comparisons: `@v1.0.0+a == @v1.0.0+b`

```stash
@v2.0.0 > @v1.0.0                      // true
@v1.10.0 > @v1.9.0                     // true (numeric, not string)
@v2.0.0-alpha < @v2.0.0                // true (pre-release < release)
@v1.0.0-alpha < @v1.0.0-beta           // true (lexicographic)
@v1.0.0-alpha.1 < @v1.0.0-alpha.2     // true (numeric segment)
@v1.0.0+build1 == @v1.0.0+build2      // true (build metadata ignored)
```

#### Wildcard Range Matching (`in`)

Wildcard patterns match versions by major or major.minor using the `in` operator:

```stash
@v2.4.1 in @v2.x                       // true (major match)
@v3.0.0 in @v2.x                       // false
@v2.4.1 in @v2.4.x                     // true (minor match)
@v2.5.0 in @v2.4.x                     // false
```

#### Parsing from Strings

The global `semver()` function parses a string into a semver value:

```stash
let v = semver("2.4.1")                 // @v2.4.1
let pre = semver("1.0.0-beta.2")        // @v1.0.0-beta.2
```

Invalid version strings cause a runtime error.

#### Practical Use

```stash
let nodeVersion = semver($(node --version).stdout)
if (nodeVersion < @v18.0.0) {
    throw "Node 18+ required, found ${nodeVersion}"
}

// Deployment version gates
let deployed = @v2.4.1
if (deployed in @v2.x && deployed >= @v2.4.0) {
    io.println("Version ${deployed} is compatible")
}
```

### Type Coercion & Truthiness

**Truthiness:** The following values are **falsy**: `false`, `null`, `0` (integer zero), `0.0` (float zero), `""` (empty string), and **error values** (see [Section 7b](#7b-error-handling)). All other values are **truthy** (including empty arrays and struct instances).

**String concatenation (`+`):** When one operand of `+` is a string, the other operand is automatically converted to its string representation. `"count: " + 5` produces `"count: 5"`.

**String repetition (`*`):** When one operand of `*` is a string and the other is an integer, the string is repeated that many times. `"ha" * 3` produces `"hahaha"`, and `3 * "ha"` produces the same result (commutative). `"x" * 0` produces `""`. A negative count is a runtime error.

**Numeric type mixing:** When an `int` and a `float` are used in an arithmetic operation (`+`, `-`, `*`, `/`, `%`), the `int` is promoted to `float` and the result is a `float`. `5 + 3.14` produces `8.14`.

**Equality:** `==` and `!=` never perform type coercion. Values of different types are never equal (`5 != "5"`, `0 != false`, `0 != null`). Enum values are compared by identity (type + member name). Dictionaries and struct instances use **reference equality** — two distinct dictionaries or instances with identical contents are not equal (`==` returns `false`). Neither type can be used as a dictionary key.

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

**The `??=` (null-coalescing assignment)** assigns only if the variable is currently `null` or an error value:

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
| `&=`     | `x = x & y`  | Bitwise AND and assign   |
| `\|=`    | `x = x \| y` | Bitwise OR and assign    |
| `^=`     | `x = x ^ y`  | Bitwise XOR and assign   |
| `<<=`    | `x = x << y` | Left shift and assign    |
| `>>=`    | `x = x >> y` | Right shift and assign   |

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

## 3g. Bitwise Operators

Bitwise operators perform bit-level manipulation on integer values. All bitwise operators require integer (`long`) operands — applying them to any other type produces a runtime error.

### Binary Operators

| Operator | Name        | Example            | Result        |
| -------- | ----------- | ------------------ | ------------- |
| `&`      | Bitwise AND | `0b1100 & 0b1010`  | `0b1000` (8)  |
| `\|`     | Bitwise OR  | `0b1100 \| 0b1010` | `0b1110` (14) |
| `^`      | Bitwise XOR | `0b1100 ^ 0b1010`  | `0b0110` (6)  |
| `<<`     | Left shift  | `1 << 4`           | `16`          |
| `>>`     | Right shift | `128 >> 3`         | `16`          |

### Unary NOT

The `~` (bitwise NOT) operator inverts every bit of its integer operand:

```stash
let mask = 0xFF;
let inverted = ~mask;   // all bits flipped (two's complement)
let cleared = 0b1111 & ~0b0100;  // clear bit 2 → 0b1011 (11)
```

### Precedence

Within the bitwise group, operators follow C-standard relative precedence: `&` binds tighter than `^`, which binds tighter than `|`. Note that `&&`/`||` sit outside this group at separate levels — see the full table below.

```
... → Comparison → Shift (<<, >>) → Range (..) → Term (+, -) → ...
... → And (&&) → Bitwise AND (&) → Bitwise XOR (^) → Bitwise OR (|) → Or (||) → ...
... → Unary (!, -, ~) → ...
```

The full precedence chain from lowest to highest:

| Level | Operators                        | Associativity |
| ----- | -------------------------------- | ------------- |
| 1     | `=` and compound assignments     | Right         |
| 2     | `? :` (ternary)                  | Right         |
| 3     | `??` (null-coalesce)             | Right         |
| 4     | `\|` (pipe — command context)    | Left          |
| 5     | `\|\|`, `or`                     | Left          |
| 6     | `\|` (bitwise OR)                | Left          |
| 7     | `^` (bitwise XOR)                | Left          |
| 8     | `&` (bitwise AND)                | Left          |
| 9     | `&&`, `and`                      | Left          |
| 10    | `==`, `!=`                       | Left          |
| 11    | `<`, `>`, `<=`, `>=`, `in`, `is` | Left          |
| 12    | `<<`, `>>`                       | Left          |
| 13    | `..` (range)                     | Left          |
| 14    | `+`, `-`                         | Left          |
| 15    | `*`, `/`, `%`                    | Left          |
| 16    | `!`, `-`, `~` (unary)            | Right         |
| 17    | `.`, `()`, `[]`                  | Left          |

### Context-Sensitive `|` and `>>`

The `|` and `>>` tokens are shared with shell pipe and redirect syntax. The parser disambiguates based on context:

- **`|` is a pipe** when the left operand is a command expression (`CommandExpr`, `PipeExpr`, or `RedirectExpr`). Otherwise it is bitwise OR.
- **`>>` is a redirect** when the left operand is a command expression. Otherwise it is right shift.

```stash
$(ls) | $(grep ".txt")    // pipe: left is a command
let x = 0xFF | 0x0F;      // bitwise OR: left is an integer expression
$(echo "log") >> "file";   // redirect: left is a command
let y = 128 >> 3;          // right shift: left is an integer expression
```

### Type Restrictions

Bitwise operators require integer or IP address operands. Both operands must be the same type — mixing integers with IP addresses is a runtime error. Applying bitwise operators to floats, strings, booleans, or any other type produces a runtime error:

```stash
let x = 5 & 3;                        // OK: both integers
let y = @192.168.1.100 & @255.255.255.0;  // OK: both IP addresses → @192.168.1.0
let z = ~@255.255.255.0;              // OK: IP bitwise NOT → @0.0.0.255
let w = 5.0 & 3;                      // Runtime error: operands must be two integers or two IP addresses
let v = 5 & @192.168.1.1;             // Runtime error: operands must be two integers or two IP addresses
```

### Compound Assignment

Bitwise compound assignment operators desugar to the equivalent operation (see [Section 3b](#3b-compound-assignment-operators)):

```stash
let flags = 0b0000;
flags |= 0b0100;    // set bit 2    → 0b0100
flags |= 0b0001;    // set bit 0    → 0b0101
flags &= ~0b0100;   // clear bit 2  → 0b0001
flags ^= 0b0011;    // toggle bits  → 0b0010
flags <<= 2;        // shift left 2 → 0b1000
flags >>= 1;        // shift right 1→ 0b0100
```

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
| `ip` (CIDR)     | IP address falls within the CIDR subnet            |

```stash
// CIDR containment with the `in` operator:
let subnet = @192.168.1.0/24;
io.println(@192.168.1.50 in subnet);     // true  — address is in subnet
io.println(@192.168.2.1 in subnet);      // false — different network
io.println(@10.0.0.1 in @10.0.0.0/8);   // true  — /8 covers 10.x.x.x
```

Using `in` against any other type is a runtime error.

### Precedence

`in` has the same precedence as the comparison operators (`<`, `>`, `<=`, `>=`) and is non-associative with them. `x in a..b` is parsed as `x in (a..b)` because `..` binds tighter. The `is` operator shares this same precedence level — see [Section 4c](#4c-the-is-operator).

---

## 4c. The `is` Operator

The `is` operator tests the **type** of a value at runtime, returning `true` if the value matches the given type name, `false` otherwise.

```stash
42 is int          // true
"hello" is string  // true
3.14 is int        // false
null is null       // true
[1, 2] is array    // true
```

### Syntax

```
expression is typeName
expression is typeExpression
```

`typeName` is a **bare identifier** — a built-in type name (`int`, `string`, `null`, etc.) or a user-defined struct, enum, or interface name. For built-in type keywords and simple names, the identifier is used directly.

When the RHS identifier is immediately followed by `(`, `[`, or `.`, or when the RHS starts with a non-identifier token (e.g. a parenthesized expression), it is parsed as an **expression** — allowing array subscripts, function calls, and property access. The expression must evaluate to a type value (a `StashInterface`, `StashStruct`, or `StashEnum`). If it evaluates to something else, a runtime error is raised.

### Valid Type Names

| Type name       | Matches                                                           |
| --------------- | ----------------------------------------------------------------- |
| `int`           | Integer values                                                    |
| `float`         | Floating-point values                                             |
| `string`        | String values                                                     |
| `bool`          | Boolean values (`true` / `false`)                                 |
| `null`          | The `null` value                                                  |
| `array`         | Array values                                                      |
| `dict`          | Dictionary values                                                 |
| `struct`        | Struct instances (any struct type)                                |
| `enum`          | Enum values (any enum type)                                       |
| `function`      | Functions and lambdas                                             |
| `range`         | Range values (`1..10`)                                            |
| `ip`            | IP address values (`@192.168.1.1`)                                |
| `namespace`     | Namespace values (e.g. `io`, `fs`)                                |
| `Error`         | Error values returned by `try`                                    |
| `Future`        | Future values returned by async functions                         |
| _StructName_    | Instances of the named struct (e.g. `Point`)                      |
| _EnumName_      | Values of the named enum (e.g. `Color`)                           |
| _InterfaceName_ | Struct instances that conform to the interface (e.g. `Printable`) |

An unrecognised type name evaluates to `false` (no runtime error).

### Relationship to `typeof()` and `nameof()`

For built-in types, `x is T` is equivalent to `typeof(x) == "T"`. For user-defined struct and enum names, `is` performs a specific type-name match (e.g. `p is Point` checks whether `p` is specifically a `Point` instance, not just any struct). The `is` operator is a concise inline alternative to `typeof()`:

```stash
// These are equivalent:
typeof(value) == "string"
value is string
```

The companion `nameof()` function returns the **declared name** rather than the meta-type — `nameof(Printable)` returns `"Printable"` where `typeof(Printable)` returns `"interface"`. See [The `nameof()` Function](#the-nameof-function) below.

### Precedence

`is` has the same precedence as the comparison operators (`<`, `>`, `<=`, `>=`) and `in` — they all sit at the comparison level. `is` is non-associative with the other comparison operators.

### Examples

```stash
// Basic type checks
42 is int          // true
"hello" is string  // true
3.14 is int        // false
null is null       // true
[1, 2] is array    // true

// User-defined struct types
struct Point { x: int, y: int }
let p = Point { x: 1, y: 2 };
p is Point         // true
p is struct        // true  (matches any struct)

// User-defined enum types
enum Color { Red, Green, Blue }
let c = Color.Red;
c is Color         // true
c is enum          // true  (matches any enum)

// Interface conformance check
interface Printable { toString() }
struct Label : Printable {
    text
    fn toString() { return self.text; }
}
let lbl = Label { text: "hello" };
lbl is Printable   // true
lbl is Label       // true  (struct type check still works)

// Use in conditions
if (value is string) {
    io.println("It's a string!");
}

// Combine with logical operators
if (x is int && x > 0) {
    io.println("Positive integer");
}

// Negation
if (!(x is null)) {
    io.println("Not null");
}
```

### Dynamic Type Checking

When the RHS of `is` is a **variable** holding a type value (interface, struct, or enum), the check resolves the variable at runtime:

```stash
interface Printable { display() }
struct Foo : Printable {
    fn display() { return "hi"; }
}

let f = Foo {};
let iface = Printable;
f is iface              // true — resolves variable to interface

let structType = Foo;
f is structType          // true — resolves variable to struct type
```

When the RHS is an **expression** (array index, function call, property access), it is evaluated and the result checked:

```stash
let types = [Printable, Serializable, Identifiable];
item is types[0]         // true if item conforms to Printable

fn getType() { return Printable; }
item is getType()        // true if item conforms to Printable

struct TypeHolder { myType }
let h = TypeHolder { myType: Printable };
item is h.myType         // true if item conforms to Printable
```

This enables data-driven type checking — iterate over a collection of types instead of writing repetitive `if` chains:

```stash
let checks = [Printable, Serializable, Identifiable];
for (let item in inventory) {
    let tags = "";
    for (let iface in checks) {
        if (item is iface) {
            tags += $"  {nameof(iface)}";
        }
    }
    io.println($"    [{item.name}]{tags}");
}
```

### The `nameof()` Function

`nameof(value)` returns the **declared name** of a value. For user-defined types it returns the specific type or instance name; for primitives it behaves like `typeof()`:

| Value                        | `typeof()`    | `nameof()`    |
| ---------------------------- | ------------- | ------------- |
| `42`                         | `"int"`       | `"int"`       |
| `"hi"`                       | `"string"`    | `"string"`    |
| `null`                       | `"null"`      | `"null"`      |
| `Printable` (interface)      | `"interface"` | `"Printable"` |
| `Product` (struct def)       | `"struct"`    | `"Product"`   |
| `Product { ... }` (instance) | `"struct"`    | `"Product"`   |
| `Color` (enum def)           | `"enum"`      | `"Color"`     |
| `Color.Red` (enum value)     | `"enum"`      | `"Color.Red"` |
| `myFn` (named function)      | `"function"`  | `"myFn"`      |
| `typeof` (built-in)          | `"function"`  | `"typeof"`    |

`nameof()` is especially useful with dynamic `is` — when iterating over types, you can print the type name without maintaining a parallel string array.

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

Dictionaries can be created with **literal syntax** using `{ key: value }` pairs, or via the `dict` namespace:

```stash
// Dict literal — concise inline initialization
let config = { host: "localhost", port: 8080, debug: true };

// Empty dict literal
let empty = {};

// Equivalent using dict.new()
let d = dict.new();
d["name"] = "Alice";      // set via index syntax
d.age = 30;               // set via dot access
```

#### Dict Literal Details

Keys in dict literals are **bare identifiers** interpreted as string keys:

```stash
let d = { name: "Stash", version: 1 };
d["name"];     // "Stash" — key is the string "name"
d.version;     // 1
```

Values can be any expression — variables, function calls, nested literals:

```stash
let x = 10;
let d = {
    computed: x * 2,
    nested: { inner: true },
    items: [1, 2, 3]
};
d.computed;        // 20
d.nested.inner;    // true
len(d.items);      // 3
```

Dict literals produce the same `dict` type as `dict.new()` — all dict namespace functions work on them:

```stash
let d = { a: 1, b: 2 };
typeof(d);          // "dict"
len(d);             // 2
dict.has(d, "a");   // true
dict.keys(d);       // ["a", "b"]
```

#### Disambiguation

Dict literals can only appear in **expression context** (right side of `=`, function arguments, array elements, etc.). A `{` at the **start of a statement** is always parsed as a block:

```stash
// Block — { at statement start
{
    let x = 1;
}

// Dict literal — { in expression context
let d = { x: 1 };
fn process(d) { ... }
process({ timeout: 30 });
```

Struct initialization uses a **name prefix** to disambiguate:

```stash
// Struct init — name before {
let srv = Server { host: "10.0.0.1", port: 22 };

// Dict literal — no name before {
let config = { host: "10.0.0.1", port: 22 };
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

## 5f. Interfaces

Interfaces define lightweight contracts — a set of required fields and methods that a struct must provide. They enable type-safe polymorphism without inheritance.

### Declaration

```stash
interface Printable {
    toString(),
    toJson()
}
```

With fields:

```stash
interface Identifiable {
    id,
    name,
    getDisplayName()
}
```

Methods list name and parameters (excluding `self`) — no bodies. Fields are bare identifiers. Members are comma-separated. Empty interfaces are not allowed.

### Struct Implementation

A struct declares conformance with `: InterfaceName` after its name. Multiple interfaces are comma-separated:

```stash
struct User : Printable, Identifiable {
    id,
    name,
    email

    fn toString() {
        return $"{self.name} <{self.email}>";
    }

    fn toJson() {
        return json.stringify({
            id: self.id,
            name: self.name,
            email: self.email
        });
    }

    fn getDisplayName() {
        return self.name;
    }
}
```

Conformance is checked at struct definition time. If a required field or method is missing, or a method has the wrong parameter count, a runtime error is raised immediately.

### Type Checking with `is`

```stash
let user = User { id: 1, name: "Alice", email: "alice@example.com" };
user is Printable      // true
user is Identifiable   // true
user is User           // true  (struct type check still works)
```

### Multiple Interface Implementation

A struct can implement any number of interfaces and must satisfy all of their contracts:

```stash
struct Document : Printable, Identifiable, Serializable {
    // must satisfy all three contracts
}
```

### The `typeof()` Function

```stash
typeof(Printable)    // "interface"
```

### Conformance Rules

- All fields listed in the interface must exist as fields in the struct.
- All methods listed in the interface must exist as methods in the struct.
- Method parameter count must match (excluding `self`).
- Methods with default parameters satisfy interfaces requiring fewer parameters — a method `fn format(style, indent)` with a default on `indent` satisfies an interface requiring `format(style)`.

### Import/Export

Interfaces participate in the module system like structs and enums:

```stash
// shapes.stash
interface Drawable {
    draw(),
    getBounds()
}

// main.stash
import { Drawable } from "shapes.stash";
```

### Internal Representation

An interface is stored as a named contract: a list of required field names and a list of required method signatures (name + parameter count). Interfaces carry no runtime data and add no overhead to struct instances.

### Future Extensions (Not in v1)

- **Interface composition:** `interface Foo : Bar, Baz { ... }` — extending other interfaces.
- **Default implementations:** Methods with bodies that structs inherit if not overridden.

---

## 6. Shell Integration

### Process Execution

Commands are executed via **command literals** — a dedicated syntax that makes shell commands first-class in the language without wrapping them in strings.

#### Syntax: `$(command)` — Command Literals

```stash
let result = $(ls -la);
io.println(result);             // stringifies to stdout contents
io.println(result.stdout);      // captured standard output
io.println(result.stderr);      // captured standard error
io.println(result.exitCode);    // process exit code
```

`$(...)` is **always raw mode**. When the lexer encounters `$(`, it enters "command mode" and collects everything as raw text until the matching `)`. The content is not parsed as a Stash expression — it is treated as a command string that is split into a program name and arguments. Programs are invoked directly, not through a system shell.

To inject dynamic values into a command, use interpolation with `${...}`:

```stash
// Raw mode — command text is written directly
let r1 = $(ls -la);

// Dynamic values via interpolation
let flags = buildFlags();
let r2 = $(ls ${flags});

// Full dynamic command — interpolate the entire string
let cmd = "echo hello";
let r3 = $(${cmd});
```

This makes `$(...)` the **single, unified way** to execute commands. The `${...}` interpolation syntax within commands is consistent with how interpolation works elsewhere in the language.

`$(...)` returns a struct-like object with `stdout`, `stderr`, and `exitCode` fields.

#### Interpolation in Commands

Variables and expressions can be embedded using `${...}`:

```stash
let host = "192.168.1.10";
let result = $(ping -c 1 ${host});

let file = "/var/log/syslog";
let pattern = "error";
let matches = $(grep ${pattern} ${file});
```

This feels natural — commands read like commands, not like strings, but you still get dynamic values where needed.

#### Comparison With Alternatives

| Syntax        | Example          | Verdict                                                                      |
| ------------- | ---------------- | ---------------------------------------------------------------------------- |
| `exec("cmd")` | `exec("ls -la")` | Rejected — commands look like strings, not commands                          |
| `` `cmd` ``   | `` `ls -la` ``   | Viable but conflicts with potential future use of backticks                  |
| `$(cmd)`      | `$(ls -la)`      | **Chosen** — familiar from Bash, always raw mode, `${...}` for interpolation |
| `$>(cmd)`     | `$>(ls -la)`     | Passthrough variant — inherited I/O for interactive commands                 |

Implementation: backed by `System.Diagnostics.Process` in C#.

#### Passthrough Commands: `$>(command)`

While `$(...)` captures stdout and stderr into a `CommandResult`, some commands need to interact directly with the terminal — displaying real-time output, showing progress bars, or prompting the user for input. The **passthrough** syntax `$>(...)` runs a command with inherited I/O:

```stash
// Captured mode — output is buffered, not visible during execution
let result = $(dotnet build);
io.println(result);               // stringifies to stdout contents
io.println(result.stdout);        // explicit field access also works

// Passthrough mode — output streams directly to terminal, user can respond to prompts
let result = $>(dotnet build);
// result.stdout and result.stderr are empty (output went to terminal)
// result.exitCode contains the actual exit code
```

`$>(...)` mirrors how Bash runs commands in direct execution mode: the child process inherits the parent's stdin, stdout, and stderr file descriptors. This means:

- **Output is visible in real time** — progress bars, colored output, and streaming logs work naturally
- **Interactive prompts work** — commands like `npx tsc` or `apt install` that ask for confirmation receive user input
- **TTY detection works** — programs that check `isatty()` see a real terminal and behave accordingly (colors, formatting)
- **No output is captured** — `result.stdout` and `result.stderr` are always empty strings

The returned `CommandResult` still provides `exitCode` for error checking.

When stringified (e.g., in `io.println()`, string interpolation, or `conv.toStr()`), a `CommandResult` returns its `stdout` field. This eliminates the need for explicit `.stdout` access in the common case:

```stash
let r = $(echo hello);
io.println(r);                    // prints "hello\n" — implicitly uses stdout
io.println($"output: ${r}");     // "output: hello\n" — works in interpolation
let text = conv.toStr(r);        // "hello\n" — works with conv.toStr
// Fields are still directly accessible:
io.println(r.stdout);            // "hello\n"
io.println(r.stderr);            // ""
io.println(r.exitCode);          // 0
```

```stash
let result = $>(make install);
if (result.exitCode != 0) {
    io.println("Installation failed!");
    process.exit(1);
}
```

Interpolation works identically to `$(...)`:

```stash
let target = "release";
$>(cargo build --profile ${target});
```

**When to use which:**

| Syntax    | Output   | Input    | Use case                                                 |
| --------- | -------- | -------- | -------------------------------------------------------- |
| `$(cmd)`  | Captured | None     | Parse output, check stderr, pipe between commands        |
| `$>(cmd)` | Terminal | Terminal | Build tools, installers, interactive prompts, long tasks |

> **Note:** Passthrough commands cannot be used with pipes (`|`) or output redirection (`>`, `>>`), since their output is not captured. Use `$(...)` for commands that participate in pipes or redirections.

### Strict Commands

The strict command syntax `$!(...)` is an opt-in alternative to `$(...)` that **throws a `CommandError` on non-zero exit codes**. It is syntactic sugar for executing a command, checking the exit code, and throwing on failure.

#### Syntax

```stash
$!(command args...)
```

All existing command features work: string interpolation, pipes, environment variables.

```stash
$!(mkdir -p /opt/myapp)
$!(docker push ${registry}/${image}:${tag})
$!(cat /etc/hosts | grep ${hostname})
```

#### Success (Exit Code 0)

When the command exits with code 0, `$!(...)` returns a `CommandResult` — identical to `$(...)`:

```stash
let result = $!(echo "hello")
io.println(result.stdout)     // "hello"
io.println(result.exitCode)   // 0
```

#### Failure (Non-Zero Exit Code)

When the command exits with a non-zero code, `$!(...)` throws a `CommandError`:

```stash
try {
    $!(curl -f https://unreachable.example.com)
} catch (e) {
    io.println(e.type)        // "CommandError"
    io.println(e.message)     // "Command failed with exit code 7: curl -f https://unreachable.example.com"
    io.println(e.exitCode)    // 7
    io.println(e.stderr)      // "curl: (7) Failed to connect..."
    io.println(e.command)     // "curl -f https://unreachable.example.com"
}
```

The `CommandError` provides these properties:

| Property    | Type     | Description                                    |
| ----------- | -------- | ---------------------------------------------- |
| `.type`     | `string` | Always `"CommandError"`                        |
| `.message`  | `string` | Human-readable: includes exit code and command |
| `.exitCode` | `int`    | The non-zero exit code                         |
| `.stderr`   | `string` | The command's stderr output                    |
| `.stdout`   | `string` | The command's stdout output                    |
| `.command`  | `string` | The command string that was executed           |

#### Strict Passthrough Commands

The strict passthrough variant `$!>(...)` combines strict mode with inherited I/O:

```stash
$!>(apt install -y nginx)  // throws if install fails
```

#### Composition

Strict commands compose naturally with `try` expressions and `try/catch`:

```stash
// With try expression:
let version = try $!(node --version) ?? "unknown"

// With try/catch:
try {
    $!(systemctl restart nginx)
} catch (e) {
    if (e.exitCode == 3) {
        log.warn("Service not found")
    } else {
        throw e
    }
}
```

The `$(...)` contract is unchanged — it continues to never throw.

### Pipes

Pipelines can be written in two equivalent forms:

```stash
// Inline pipe syntax (recommended)
let lines = $(cat /var/log/syslog | grep error | wc -l);

// External pipe syntax (alternative)
let lines = $(cat /var/log/syslog) | $(grep error) | $(wc -l);
```

Both produce the same AST and execute identically. The two syntaxes can also be mixed:

```stash
let result = $(cmd1 | cmd2) | $(cmd3);
```

**Inline pipes:** The lexer splits `$(...)` on unquoted `|` characters at the source level. Each segment becomes a separate pipeline stage. The following characters do **not** trigger splitting:

- `||` — treated as a token boundary, not a pipe (use `||` for logical OR outside `$(...)`)
- `|` inside quotes — `$(grep "a|b")` passes the literal string to `grep`
- `|` inside `$>(...)` — passthrough commands do not split on pipes; the character is passed as-is to the shell

The `|` operator is **exclusive to command chaining** — it cannot be used between non-command expressions. For logical OR, use `||`.

#### Execution Semantics

Pipe chains use **streaming concurrent execution**. All stages in the chain launch simultaneously as OS-level processes, connected by OS-level pipes — stdout of each stage flows directly into stdin of the next with no buffering in between.

```stash
let result = $(cat /var/log/syslog | grep error | wc -l);
// All three processes start concurrently.
// result.exitCode is the exit code of 'wc -l' (the last command).
// result.stdout and result.stderr are captured from 'wc -l'.
```

Because data streams directly between processes, infinite producers work correctly:

```stash
let first5 = $(yes | head -5);
// 'yes' runs concurrently with 'head -5'.
// 'head -5' reads 5 lines then exits, causing 'yes' to terminate naturally.
```

The exit code of the entire pipe expression is the exit code of the **last command** in the chain (standard POSIX behavior). All stages run to completion regardless of earlier stages' exit codes — there is no short-circuit on failure.

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

## 6b-2. Command-Line Execution Modes

Stash supports multiple ways to execute code beyond script files and the interactive REPL.

### Inline Code (`-c`)

Execute code passed directly as a command-line argument, similar to `bash -c` or `python -c`:

```bash
stash -c 'io.println("hello");'
stash --command 'let x = 2 + 2; io.println(x);'
```

Arguments after the command string are passed to the script:

```bash
stash -c 'io.println(args.list());' foo bar
# Output: [foo, bar]
```

The source is identified as `<command>` in error diagnostics.

### Standard Input (Piping)

Stash reads and executes code from standard input when input is piped or redirected:

```bash
echo 'io.println("hello from stdin");' | stash
cat deploy.stash | stash
curl -sL https://example.com/script.stash | stash
```

Pass arguments to piped scripts using the `--` separator:

```bash
echo 'io.println(args.list());' | stash -- arg1 arg2
# Output: [arg1, arg2]
```

The source is identified as `<stdin>` in error diagnostics.

### Execution Mode Summary

| Mode        | Command                      | Source Name |
| ----------- | ---------------------------- | ----------- |
| REPL        | `stash`                      | `<stdin>`   |
| Script file | `stash script.stash`         | file path   |
| Inline code | `stash -c 'code'`            | `<command>` |
| Piped stdin | `echo 'code' \| stash`       | `<stdin>`   |
| Debug       | `stash --debug script.stash` | file path   |
| Test runner | `stash --test script.stash`  | file path   |

### Exit Codes

| Code | Meaning           |
| ---- | ----------------- |
| 0    | Success           |
| 1    | Test failures     |
| 64   | Invalid CLI usage |
| 65   | Lex/parse error   |
| 66   | File not found    |
| 70   | Runtime error     |

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

## 6d. Privilege Elevation (`elevate`)

The `elevate` block provides scoped privilege elevation for command execution. Commands inside the block are automatically prefixed with the platform's elevation program (`sudo` on Linux/macOS, `gsudo` on Windows), with credentials acquired once at block entry. No passwords or credentials ever pass through Stash data structures — authentication is handled entirely by the OS.

### Syntax

```stash
// Platform default elevator (sudo on Unix, gsudo on Windows)
elevate {
    $(apt update);
    $(apt upgrade -y);
}

// Named elevator — for doas, pkexec, or other tools
elevate("doas") {
    $(pkg install nginx);
}
```

`elevate` is a **statement**, like `while` or `if`. The optional parenthesized argument specifies the elevation program; omitting it uses the platform default. The block body is a standard block — any statements are valid inside it.

### What Gets Elevated

Only `$()` and `$>()` command expressions are affected by elevation. Low-level process functions are explicitly excluded:

| Expression                    | Elevated? | Reason                                         |
| ----------------------------- | --------- | ---------------------------------------------- |
| `$(ufw enable)`               | ✅ Yes    | Auto-prefixed with elevator                    |
| `$>(systemctl restart nginx)` | ✅ Yes    | Auto-prefixed with elevator                    |
| `$(sudo ufw enable)`          | ✅ No-op  | Already prefixed — no double-prefix            |
| `process.spawn("ufw", [...])` | ❌ No     | Low-level escape hatch — user has full control |
| `process.exec("ufw enable")`  | ❌ No     | Low-level escape hatch — user has full control |

Commands that already start with an elevation program (`sudo`, `doas`, `gsudo`, `runas`) are left unchanged to prevent double-prefixing like `sudo sudo ufw enable`.

### Dynamic Scope

The elevation context is **dynamic**, not lexical. It propagates through function calls and into imported modules:

```stash
fn restart_service(name) {
    $(systemctl restart ${name});   // elevated if called inside elevate { }
}

elevate {
    restart_service("nginx");       // the $(systemctl ...) inside is elevated
}

restart_service("nginx");           // NOT elevated — outside the block
```

This is the critical property that makes `elevate` useful for libraries. A package like `@stash/ufw` can call `$(ufw enable)` internally — the consumer wraps the call in `elevate { }` and the elevation propagates automatically.

### Nesting

`elevate` inside `elevate` is a **no-op**. The inner block executes normally under the outer elevation context. Credentials are acquired only once, when the outermost block is entered. The semantic analyzer emits a **warning** for nested `elevate` blocks:

```stash
elevate {
    elevate {           // Warning: nested elevate has no effect
        $(ufw enable);  // elevated — outer context applies
    }
}
```

### Already-Elevated Process

If the interpreter is already running as a privileged user (root on Unix, Administrator on Windows), the `elevate` block is completely transparent — no credential prompts appear, no command prefixing occurs. The block executes exactly as if the `elevate` keyword were not present.

### Credential Acquisition

When entering an `elevate` block, the interpreter performs the following steps:

1. **Privilege check** — If the process is already privileged, skip all remaining steps and execute the body directly.
2. **Elevator resolution** — Determine the elevation program from the optional argument or the platform default.
3. **Elevator discovery** — Verify the program exists on the system PATH. On Unix, if `sudo` is not found, `doas` is attempted as a fallback.
4. **Interactive authentication** — Run the credential validation command in passthrough mode so the user can interact with the OS prompt:
   - Unix (`sudo`): `$>(sudo -v)` — validates cached credentials or prompts for password
   - Unix (`doas`): `$>(doas true)` — runs a no-op command as root, prompting if needed
   - Windows (`gsudo`): `$>(gsudo cache on -d -1)` — activates credential caching and triggers the UAC consent dialog
5. **Block execution** — Commands inside the block are auto-prefixed with the elevator.
6. **Cleanup** — On block exit (including exceptions), the elevation context is cleared.

If credential acquisition fails (user cancels the prompt, wrong password, UAC denied), a `RuntimeError` is thrown.

### Cross-Platform Behavior

| Platform | Default Elevator | Credential Command     | Fallback                    |
| -------- | ---------------- | ---------------------- | --------------------------- |
| Linux    | `sudo`           | `sudo -v`              | `doas` if `sudo` not found  |
| macOS    | `sudo`           | `sudo -v`              | None                        |
| Windows  | `gsudo`          | `gsudo cache on -d -1` | None (install instructions) |

On Windows, `gsudo` is an open-source elevation tool available via `winget install gerardog.gsudo` or `scoop install gsudo`. If `gsudo` is not found, the error message includes install instructions.

### Checking Results Inside the Block

Commands inside `elevate` return `CommandResult` normally. Assign results and check exit codes as usual:

```stash
elevate {
    let update = $(apt update);
    let upgrade = $(apt upgrade -y);
    if (upgrade.exitCode != 0) {
        io.println("Upgrade failed: " + str.trim(upgrade.stderr));
    }
}
```

### Library Usage Pattern

The primary motivation for `elevate` is clean library consumption. Libraries that wrap privileged commands don't need to handle sudo logic — the consumer provides the elevation context:

```stash
import "@stash/ufw" as ufw;
import "@stash/systemd" as systemd;

elevate {
    ufw.config.enable();
    ufw.rules.allow("22/tcp");
    ufw.rules.allow("443/tcp");
    systemd.service.restart("nginx");
}
```

A single credential prompt occurs at block entry. All library calls inside are automatically elevated.

### Embedded Mode

`elevate` throws a `RuntimeError` in embedded mode (Playground, WASM), matching the existing `$>()` guard pattern:

```
RuntimeError: Privilege elevation is not available in embedded mode.
```

### Implementation

`elevate` is an `ElevateStmt` AST node with two children:

```
ElevateStmt:
  elevator: Expr?     // optional elevator program expression (null for platform default)
  body: BlockStmt      // the block of statements to execute with elevation
```

The elevation state is stored on `ExecutionContext` as two properties: `ElevationActive` (bool) and `ElevationCommand` (string). This context propagates into function calls and forked interpreters automatically. At the `ProcessStartInfo` level, the command's program name is replaced with the elevator and the original program is prepended to the argument list.

> **Design spec:** See [Elevate — Scoped Privilege Elevation](specs/Elevate%20—%20Scoped%20Privilege%20Elevation.md) for the full design document covering edge cases, security analysis, and implementation roadmap.

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

Stash supports two forms of `for` loop: the **for-in** loop for collection iteration, and the **C-style for** loop for counted/general iteration.

#### C-Style For

```stash
for (let i = 0; i < 10; i++) {
    io.println(conv.toStr(i));
}
```

The three clauses inside the parentheses are:

1. **Initializer** — executed once before the loop begins. May be a `let` declaration, an expression, or empty.
2. **Condition** — evaluated before each iteration. If falsy, the loop exits. If omitted, the loop runs forever (use `break` to exit).
3. **Increment** — executed after each iteration (including after `continue`).

All three clauses are optional. An infinite loop: `for (;;) { ... }`.

The initializer creates a new scope — variables declared with `let` in the initializer are scoped to the loop and not accessible afterward:

```stash
for (let i = 0; i < 5; i++) {
    io.println(conv.toStr(i));  // i is accessible here
}
// i is NOT accessible here
```

`break` exits the loop. `continue` skips the rest of the body but **still executes the increment** before re-checking the condition:

```stash
let sum = 0;
for (let i = 0; i < 10; i++) {
    if (i % 2 == 0) { continue; }  // i++ still runs
    sum = sum + i;
}
// sum = 25 (1 + 3 + 5 + 7 + 9)
```

#### For-In

```stash
for (let item in collection) {
    // ...
}
```

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

The `??` operator returns the left operand if it is neither `null` nor an error value, otherwise returns the right operand:

```stash
let name = inputName ?? "default";
let config = try fs.readFile("/etc/app.conf") ?? "fallback config";
```

The right operand is only evaluated if the left operand is `null` **or an error value** (short-circuit evaluation). This makes `??` the natural companion to `try` — a failed `try` returns an error (which is falsy), so `??` provides the default.

---

## 7b. Error Handling

Stash uses a **`try` expression** model with first-class **error values** — lightweight, no exception machinery, no Go-style verbosity.

### Philosophy

By default, runtime errors **crash the script** with a stack trace. This is the right behavior for most scripting — fail loudly, fix the problem. When you _expect_ an operation might fail, you opt in to error handling with `try`.

### The `try` Expression

`try` is a **prefix expression** that wraps any expression. On success, `try` returns the value normally. On failure, `try` catches the error and returns an **Error value** instead of crashing.

```stash
// Without try — script crashes if the conversion fails
let n = conv.toInt("not-a-number");

// With try — returns an Error value on failure
let n = try conv.toInt("not-a-number");
io.println(n);           // "RuntimeError: Cannot parse 'not-a-number' as integer."
io.println(n.message);   // "Cannot parse 'not-a-number' as integer."
io.println(n.type);      // "RuntimeError"

// On success — returns the value directly
let n = try conv.toInt("42");
io.println(n);           // 42
```

### Error Values

Error values are first-class values with their own type. They carry three fields:

| Field      | Type     | Description                                           |
| ---------- | -------- | ----------------------------------------------------- |
| `.message` | `string` | Human-readable error description                      |
| `.type`    | `string` | Error category (e.g. `"RuntimeError"`, `"TypeError"`) |
| `.stack`   | `array?` | Call stack at the point of failure                    |

Error values are **falsy** — they evaluate to `false` in boolean contexts. This makes them compose naturally with `??`:

```stash
// try + ?? — elegant defaults for fallible operations
let port = try conv.toInt(input) ?? 3000;
let config = try fs.readFile("/etc/app.conf") ?? "fallback";

// Type checking
typeof(err) == "Error"    // true
err is Error              // true

// lastError() returns the most recent Error value
let data = try conv.toInt("abc");
let last = lastError();
io.println(last.message);    // "Cannot parse 'abc' as integer."
```

**Note:** `lastError()` returns only the single most recent error. If multiple `try` expressions execute in sequence, only the last error is retained.

### The `throw` Statement

`throw` raises a runtime error from user code. The throw value determines the error's type and message:

```stash
// Throw a string — becomes a RuntimeError
throw "something went wrong";

// Throw a dict — use `type` for the error category, `message` for details
// `type` defaults to "Error" if omitted; `message` defaults to "Unknown error"
throw { type: "ValidationError", message: "age must be >= 0" };

// Any other value is stringified and thrown as a RuntimeError
throw 42;    // RuntimeError with message "42"
```

Thrown errors are caught by `try` just like built-in errors:

```stash
fn validateAge(age) {
    if (age < 0) {
        throw { type: "ValidationError", message: "age must be >= 0" };
    }
    return age;
}

let result = try validateAge(-5);
io.println(result.type);       // "ValidationError"
io.println(result.message);    // "age must be >= 0"

let safe = try validateAge(-1) ?? 0;  // falls back to 0
```

### Rethrow Pattern

Catch an error with `try`, inspect it, and rethrow to add context. The error type is preserved:

```stash
fn parsePositive(s) {
    let n = try conv.toInt(s);
    if (n is Error) {
        throw { type: n.type, message: $"parsePositive: {n.message}" };
    }
    if (n < 0) {
        throw { type: "RangeError", message: $"expected positive, got {n}" };
    }
    return n;
}

let err = try parsePositive("abc");
io.println(err.type);      // "RuntimeError" (preserved from conv.toInt)
io.println(err.message);   // "parsePositive: Cannot parse 'abc' as integer."

let err2 = try parsePositive("-3");
io.println(err2.type);     // "RangeError"
```

### Shell Commands Don't Need `try`

Shell command results already carry structured error information via `exitCode` and `stderr` — they never crash the script:

```stash
let result = $(ping -c 1 ${host});
if (result.exitCode != 0) {
    io.println("Host unreachable: " + result.stderr);
}
```

### Complete Example

```stash
fn loadConfig(path) {
    let raw = try fs.readFile(path) ?? "host=localhost\nport=8080";
    let cfg = dict.new();
    for (let line in str.split(raw, "\n")) {
        let trimmed = str.trim(line);
        if (trimmed == "") { continue; }
        if (!("=" in trimmed)) { continue; }
        let parts = str.split(trimmed, "=");
        cfg[str.trim(parts[0])] = str.trim(parts[1]);
    }

    let port = try conv.toInt(cfg["port"]);
    if (port is Error) {
        throw { type: "ConfigError", message: $"invalid port: {cfg["port"]}" };
    }

    cfg["port"] = port;
    return cfg;
}

let config = try loadConfig("/etc/app.conf");
if (config is Error) {
    io.println($"Config failed: {config.message}");
} else {
    io.println($"Server: {config["host"]}:{config["port"]}");
}
```

### The `try`/`catch`/`finally` Statement

For situations that need **scoped error handling** or **guaranteed cleanup**, Stash provides `try`/`catch`/`finally` blocks. These coexist with `try expr` — use whichever fits the situation:

```stash
try {
    let handle = fs.open("/tmp/data.lock");
    doWork(handle);
} catch (e) {
    log.error("Work failed: " + e.message);
} finally {
    fs.delete("/tmp/data.lock");  // ALWAYS runs
}
```

#### Syntax

```stash
try { ... }                              // bare try — error suppression
try { ... } catch (variable) { ... }     // catch errors
try { ... } finally { ... }              // guaranteed cleanup
try { ... } catch (e) { ... } finally { ... }  // both
```

The `catch` clause declares a variable that receives the Error value (same fields as `try expr` errors: `.message`, `.type`, `.stack`). The `finally` clause runs unconditionally — after the try body on success, after the catch body on error.

#### Four Forms

| Form                                | Behavior                                              |
| ----------------------------------- | ----------------------------------------------------- |
| `try { } catch (e) { }`             | Catches errors; `e` is the Error value                |
| `try { } finally { }`               | No catch — errors propagate after `finally` runs      |
| `try { } catch (e) { } finally { }` | Catches errors, then `finally` always runs            |
| `try { }`                           | Bare try — silently suppresses errors (use sparingly) |

#### Catch Variable Scoping

The catch variable is scoped to the catch block — it does not leak into the surrounding scope:

```stash
try {
    throw "boom";
} catch (e) {
    io.println(e.message);  // "boom"
}
// e is not accessible here
```

#### Finally Guarantees

`finally` executes even when control flow leaves the try/catch via `return`, `break`, or `continue`:

```stash
fn loadData() {
    try {
        return fs.readFile("/tmp/data");
    } finally {
        io.println("Cleanup runs even after return");
    }
}
```

If the `catch` block itself throws, `finally` still runs before the new error propagates.

#### Rethrow in Catch

Rethrow a caught error to add context or re-signal it:

```stash
try {
    riskyOperation();
} catch (e) {
    log.error("Failed: " + e.message);
    throw e;  // re-throws the original error
}
```

#### Nesting

`try`/`catch`/`finally` blocks can be nested. Inner catches handle errors first; uncaught errors propagate to outer handlers:

```stash
try {
    try {
        throw "inner";
    } catch (e) {
        io.println("Caught inner: " + e.message);
        throw "re-thrown";
    }
} catch (e) {
    io.println("Caught outer: " + e.message);  // "re-thrown"
}
```

#### When to Use Which

| Pattern                             | Use case                                              |
| ----------------------------------- | ----------------------------------------------------- |
| `try expr ?? default`               | Quick fallback for a single fallible expression       |
| `try expr` + `is Error`             | Inspect error details for a single expression         |
| `try { } catch (e) { }`             | Handle errors across multiple statements              |
| `try { } finally { }`               | Guaranteed resource cleanup (files, locks, temp dirs) |
| `try { } catch (e) { } finally { }` | Both error handling and cleanup                       |

Both patterns are first-class — `try expr` is lightweight and composable; `try/catch/finally` is structured and scoped. Choose based on complexity.

### Implementation

**`try expr`:** A single AST node (`TryExpr`) wrapping another expression. On failure, the interpreter catches the internal `RuntimeError`, converts it to a `StashError` value (with message, type, and stack trace), stores it for `lastError()`, and returns it. Error values are falsy, making them compatible with `??` for default-value patterns.

**`try/catch/finally`:** A statement AST node (`TryCatchStmt`) with try body, optional catch clause (keyword + variable + body), and optional finally clause (keyword + body). The parser disambiguates at the `try` keyword: if the next token is `{`, it parses a `TryCatchStmt`; otherwise, it parses a `try expr` (`TryExpr`). `throw` is a statement node (`ThrowStmt`) that raises a `RuntimeError` from user code.

### Design History

The original design used only `try expr` — lightweight, no exception machinery, no Go-style verbosity. After a gap analysis revealed that the absence of structured error handling blocked reliable deploy scripts and resource cleanup patterns, `try/catch/finally` was added as a **complement** (not a replacement). The `try expr` pattern remains the recommended choice for simple fallible expressions.

---

## 7d. Retry Blocks

The `retry` keyword introduces a language-level construct that re-executes a block of code when it fails, with configurable attempt limits, delays, backoff strategies, and failure predicates. It is a keyword, not a library function, to compose naturally with `try`, access block scope, and provide retry-aware diagnostics.

### Syntax

```stash
// Minimal form
retry (<maxAttempts>) {
    <body>
}

// Full options (inline fields)
retry (<maxAttempts>, delay: <duration>, backoff: Backoff.<strategy>, maxDelay: <duration>, jitter: <bool>, timeout: <duration>, on: [<ErrorTypes>]) {
    <body>
}

// With until clause (predicate-based retry)
retry (<maxAttempts>) until <predicate> {
    <body>
}

// With onRetry hook
retry (<maxAttempts>) onRetry (<attempt>, <error>) {
    <hook body>
} {
    <retry body>
}

// Combined
retry (<maxAttempts>, <options...>) onRetry <hookFn> until <predicateFn> {
    <body>
}
```

### Success and Failure Determination

**Exception-based (default):** The body retries when it throws an uncaught `RuntimeError`. If the body completes without throwing, the result is returned. On exhaustion, the last `RuntimeError` is re-thrown transparently.

**Predicate-based (`until` clause):** After the body completes without throwing, its return value is passed to the `until` predicate. If the predicate returns truthy, the result is returned (success). If falsy, the body is retried. On exhaustion, a `RetryExhaustedError` is thrown. The `until` clause layers on top of exception-based retry — blocks with `until` retry on both exceptions and predicate failures.

**Error type filtering (`on` option):** Restricts which error types trigger a retry. Errors not in the list propagate immediately without consuming an attempt.

### Options

| Option     | Type       | Default         | Description                                  |
| ---------- | ---------- | --------------- | -------------------------------------------- |
| `delay`    | `duration` | `0s`            | Wait time before each retry                  |
| `backoff`  | `Backoff`  | `Backoff.Fixed` | Backoff strategy: Fixed, Linear, Exponential |
| `maxDelay` | `duration` | unlimited       | Upper bound on computed delay                |
| `jitter`   | `bool`     | `false`         | ±25% random jitter on delay                  |
| `timeout`  | `duration` | none            | Wall-clock deadline for all attempts         |
| `on`       | `array`    | all error types | Error type names to retry                    |

Options can be passed as inline named fields or as a pre-built `RetryOptions` struct instance.

### Backoff Enum

```stash
enum Backoff { Fixed, Linear, Exponential }
```

- **Fixed:** Every retry waits `delay`.
- **Linear:** Delay increases by `delay` each retry (delay × attempt).
- **Exponential:** Delay doubles each retry (delay × 2^(attempt-1)).

### Attempt Context

Inside the retry body, `attempt` is bound to a `RetryContext` value:

| Field       | Type       | Description                         |
| ----------- | ---------- | ----------------------------------- |
| `current`   | `int`      | Current attempt number (1-indexed)  |
| `max`       | `int`      | Maximum attempts configured         |
| `remaining` | `int`      | Attempts remaining after this one   |
| `elapsed`   | `duration` | Wall-clock time since retry started |
| `errors`    | `array`    | All errors from previous attempts   |

### `until` Clause

The predicate receives 1 or 2 parameters: `(result)` or `(result, attemptNumber)`. Accepts inline lambdas or named function references. If the predicate itself throws, the error propagates immediately (not retried).

### `onRetry` Hook

Executes between retries — after a failure, before the next delay. Receives the failed attempt number and the error. Accepts inline blocks or named function references. The hook is NOT called after the last failed attempt. If the hook throws, the error propagates immediately.

### Expression Value

`retry` is an expression. The body's last expression is the return value:

```stash
let data = retry (3) { http.get(url) }
let safe = try retry (3) { riskyOp() } ?? fallback
```

### Composability

- **`try retry`:** Catches exhaustion as an Error value.
- **`retry` inside `try/catch`:** Exhaustion caught by enclosing handler.
- **`try/catch` inside `retry`:** Only uncaught exceptions trigger retry.
- **Nested retry:** Inner and outer blocks are independent.

### Control Flow

`return` inside a retry body returns from the enclosing function. `break` and `continue` affect the enclosing loop. These propagate through the retry block — it is not a function boundary.

### Scope

The retry body creates a fresh block scope for each attempt. Variables declared inside the body are local to that attempt. The body has read/write access to variables in enclosing scopes.

### Error Types

| Error Type            | Thrown When                                           | Key Properties                       |
| --------------------- | ----------------------------------------------------- | ------------------------------------ |
| `RetryExhaustedError` | All attempts fail the `until` predicate               | `.attempts`, `.lastValue`, `.errors` |
| `RetryTimeoutError`   | Wall-clock `timeout` exceeded                         | `.elapsed`, `.completedAttempts`     |
| `RetryPredicateError` | (Internal) Passed to `onRetry` for predicate failures | `.message`                           |

Exception-based exhaustion re-throws the original error transparently — no wrapping, no new types.

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

| Function      | Description                                                                                                                    |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| `typeof(val)` | Return the type of a value as string. See also the `is` operator ([Section 4c](#4c-the-is-operator)) for inline type checking. |
| `len(val)`    | Length of a string or array                                                                                                    |
| `lastError()` | Last error value (Error object) or null                                                                                        |

All other built-in functions are organized into namespaces (see below).

### Built-in Namespaces

Stash organizes built-in functions into **namespaces** accessed via dot notation. A small set of fundamental functions remain global (see above); everything else lives in a namespace.

Available namespaces: `io`, `conv`, `env`, `fs`, `path`, `str`, `arr`, `dict`, `math`, `time`, `json`, `ini`, `config`, `http`, `process`, `assert`, `test`.

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

## 8c. Async Functions & Await

Stash supports **language-level asynchronous programming** via `async` and `await` keywords. Async functions run their body on the .NET thread pool and return a `Future` immediately. `await` blocks until a `Future` resolves and returns its value.

### Async Function Declaration

Prefix any function declaration with `async` to make it asynchronous:

```stash
async fn fetchData(url) {
    let response = http.get(url);
    return response;
}

// Calling returns a Future immediately
let future = fetchData("https://api.example.com/data");
io.println(typeof(future));  // "Future"

// await blocks until the Future resolves
let data = await future;
```

### Async Lambdas

Lambdas can also be async — prefix the parameter list with `async`:

```stash
let double = async (x) => x * 2;
let result = await double(21);  // 42

let process = async (data) => {
    let parsed = json.parse(data);
    return parsed;
};
```

### The `await` Expression

`await` is a **prefix expression** (same precedence as `try`) that blocks until a `Future` resolves:

```stash
// Await a Future from an async function
let result = await asyncFn();

// task.run() also returns a Future
let future = task.run(() => 42);
let result = await future;

// Await a non-Future value — returns it as-is (transparent await)
let result = await 42;  // 42
```

### Parallel Execution

Async functions enable composable parallelism. Call multiple async functions without awaiting, then await all results:

```stash
async fn fetch(url) {
    return http.get(url);
}

// Spawn three parallel requests
let f1 = fetch("https://api1.example.com");
let f2 = fetch("https://api2.example.com");
let f3 = fetch("https://api3.example.com");

// Wait for all — total time ≈ slowest request, not sum
let results = await task.all([f1, f2, f3]);
```

Use `task.race()` to get the first result:

```stash
let fastest = await task.race([
    fetch("https://primary.example.com"),
    fetch("https://replica.example.com")
]);
```

### Error Handling

Errors thrown inside async functions propagate when awaited. Use `try` to catch them:

```stash
async fn riskyOp() {
    throw "something went wrong";
}

// Without try — crashes the script
let result = await riskyOp();

// With try — returns an Error value
let result = try await riskyOp();
if (result is Error) {
    io.println(result.message);  // "something went wrong"
}
```

### Future Type

A `Future` is a first-class value representing an in-progress async computation:

- **Type checking:** `value is Future` → `true` / `false`
- **typeof:** `typeof(future)` → `"Future"`
- **Truthiness:** Futures are truthy
- **Stringification:** `<Future:Running>`, `<Future:Completed>`, `<Future:Faulted>`

### Async Struct Methods

Struct methods can be declared async:

```stash
struct ApiClient {
    base_url: string

    async fn get(path) {
        return http.get($"{self.base_url}/{path}");
    }
}

let client = ApiClient { base_url: "https://api.example.com" };
let resp = await client.get("users");
```

### Companion Functions

The `task` namespace provides utility functions for working with Futures (see Standard Library Reference):

| Function             | Description                                                  |
| -------------------- | ------------------------------------------------------------ |
| `task.all(futures)`  | Returns a Future that resolves to an array of all results    |
| `task.race(futures)` | Returns a Future that resolves to the first completed result |
| `task.resolve(val)`  | Creates an already-resolved Future                           |
| `task.delay(secs)`   | Creates a Future that resolves to `null` after a delay       |

### Internal Representation

An `async fn` declaration sets `IsAsync = true` on the `FnDeclStmt` AST node. When called, `StashFunction.Call()` forks the interpreter via `interpreter.Fork()`, snapshots the environment via `Environment.Snapshot()`, runs the body on the .NET `ThreadPool`, and returns a `StashFuture` wrapping the resulting `Task<object?>`.

`await` is parsed as an `AwaitExpr` prefix expression. The interpreter's `VisitAwaitExpr` calls `StashFuture.GetResult()` which blocks on the underlying task and unwraps exceptions. If the awaited value is not a `Future`, it is returned as-is (transparent await).

---

## 8c. UFCS — Uniform Function Call Syntax

UFCS (Uniform Function Call Syntax) allows namespace functions to be called as methods on values, enabling left-to-right chaining syntax alongside the existing `namespace.function(value)` syntax.

### Motivation

Stash organizes built-in functions into namespaces: `str.upper(s)`, `arr.push(a, v)`. While consistent, this forces inside-out nesting for chained operations:

```stash
// Without UFCS: inside-out — read right-to-left
let result = str.split(str.upper(str.trim(input)), ",");

// With UFCS: left-to-right chaining
let result = input.trim().upper().split(",");
```

### Syntax

Given an expression `receiver.method(arg1, arg2, ...)`, if the receiver is not a struct instance, dictionary, enum, or namespace, the interpreter maps the receiver's type to its corresponding namespace and looks up `method` as a function. The receiver is implicitly prepended as the first argument.

```stash
// These pairs are equivalent:
str.upper(s)           ↔  s.upper()
str.split(s, ",")      ↔  s.split(",")
arr.push(a, 42)        ↔  a.push(42)
arr.map(a, (x) => x*2) ↔  a.map((x) => x*2)
```

### Type-to-Namespace Mapping

Only type-centric namespaces participate in UFCS:

| Runtime Type | Namespace |
| ------------ | --------- |
| `string`     | `str`     |
| `array`      | `arr`     |

All other namespaces (`dict`, `math`, `conv`, `fs`, `http`, `env`, `io`, etc.) are **not** eligible for UFCS. Dictionary dot access resolves to key lookup, not UFCS — `d.keys` returns the dict entry at key `"keys"`, not `dict.keys(d)`.

### Resolution Rules

When evaluating `receiver.name`, the interpreter follows this precedence:

1. **StashError** — access `.message`, `.type`, `.stack` fields
2. **Struct instance** — field or method lookup
3. **Dictionary** — key lookup
4. **Enum** — member lookup
5. **Namespace** — member lookup
6. **UFCS** — map receiver type to namespace, return bound method
7. **Error** — "No method 'name' on type 'typename'"

Existing resolution (steps 1–5) always takes priority. UFCS is a **fallback**, not an override.

### Arity Adjustment

UFCS-bound methods report arity reduced by 1 since the receiver is implicit:

```stash
str.upper     // namespace function: arity 1 (takes s)
s.upper       // UFCS method: arity 0 (receiver is implicit)

str.split     // namespace function: arity 2 (takes s, delimiter)
s.split       // UFCS method: arity 1 (takes delimiter)
```

### Chaining

Each UFCS call returns a value that can be the receiver of the next call:

```stash
// String chaining
let slug = title.trim().lower().replaceAll(" ", "-");

// Array pipelines
let names = users
    .filter((u) => u.active)
    .sortBy((u) => u.name)
    .map((u) => u.email);

// Mixed: namespace call result → UFCS
let words = fs.readFile("data.txt").trim().split("\n");
```

### Limitations

```stash
// No UFCS on dictionaries (ambiguous with key lookup)
d.keys()          // dict key lookup, NOT dict.keys(d) — use dict.keys(d)

// No UFCS on null
null.upper()      // ERROR — null has no methods

// No UFCS on numbers or booleans
42.abs()          // ERROR — use math.abs(42)
true.toStr()      // ERROR — use conv.toStr(true)

// Variadic/factory functions not reachable via UFCS
str.format("{0}", val)  // use namespace syntax
arr.zip(a, b)           // use namespace syntax
```

### Both Syntaxes Coexist

Neither syntax is deprecated. Use whichever is clearer for the context:

```stash
// UFCS for chaining
let result = input.trim().upper().split(",");

// Namespace for standalone calls
let upper = str.upper(name);

// Mixed in a single expression
let count = len(lines.filter((l) => l.contains("ERROR")));
```

---

## 8d. Extend Blocks — Type Extension Methods

Extend blocks allow adding new methods to existing types — both built-in types (`string`, `array`, `dict`, `int`, `float`) and user-defined structs. Extension methods receive an implicit `self` parameter bound to the receiver value at call time.

### Syntax

```stash
extend string {
    fn isPalindrome() {
        let reversed = self.split("").reverse().join("");
        return self.lower() == reversed.lower();
    }

    fn shout() {
        return self.upper() + "!!!";
    }
}

"hello".shout();              // "HELLO!!!"
"racecar".isPalindrome();     // true
```

### Target Types

The following types can be extended:

| Type keyword | Description           | Example                                                       |
| ------------ | --------------------- | ------------------------------------------------------------- |
| `string`     | String values         | `extend string { fn shout() { return self.upper() + "!"; } }` |
| `array`      | Array values          | `extend array { fn second() { return self[1]; } }`            |
| `dict`       | Dictionary values     | `extend dict { fn hasKey(k) { return k in self; } }`          |
| `int`        | Integer values        | `extend int { fn isEven() { return self % 2 == 0; } }`        |
| `float`      | Floating-point values | `extend float { fn doubled() { return self * 2.0; } }`        |
| User structs | Any struct in scope   | `extend MyStruct { fn greet() { return self.name; } }`        |

Types that **cannot** be extended: `bool`, `null`, `function`, `range`, `enum`, `namespace`, `Error`.

### The `self` Binding

Inside extension methods, `self` refers to the receiver value:

```stash
extend int {
    fn clamp(min, max) {
        if (self < min) { return min; }
        if (self > max) { return max; }
        return self;
    }
}

150.clamp(0, 100);    // 100

extend string {
    fn initials() {
        return self.split(" ")
            .map((w) => w[0].upper() + ".")
            .join(" ");
    }
}

"John Michael Doe".initials();    // "J. M. D."
```

For built-in types, `self` is **read-only** — reassigning `self` produces a runtime error:

```stash
extend string {
    fn broken() {
        self = "modified";   // ERROR — cannot reassign constant 'self'
    }
}
```

For struct types, `self` is a reference to the instance — field mutation via `self.field = value` is allowed, but reassigning `self` itself is not.

### Extending Structs

Extension methods on structs have full access to struct fields via `self`:

```stash
struct User { name, email, age }

extend User {
    fn isAdult() {
        return self.age >= 18;
    }

    fn displayName() {
        return self.name + " <" + self.email + ">";
    }
}

let user = User { name: "Alice", email: "alice@example.com", age: 30 };
user.isAdult();        // true
user.displayName();    // "Alice <alice@example.com>"
```

Extension methods can coexist with methods defined in the struct declaration:

```stash
struct Counter {
    count

    fn increment() {
        self.count = self.count + 1;
    }
}

extend Counter {
    fn reset() {
        self.count = 0;
    }
}

let c = Counter { count: 5 };
c.increment();    // count → 6
c.reset();        // count → 0
```

### Multiple Extend Blocks

Multiple `extend` blocks for the same type accumulate their methods:

```stash
extend string {
    fn isPalindrome() { ... }
}

extend string {
    fn isBlank() { ... }
}

// Both methods are available
"racecar".isPalindrome();    // true
"   ".isBlank();              // true
```

### Method Resolution Order

When evaluating `receiver.name(args)`, the interpreter checks in this order:

| Priority | Source                   | Example                                     |
| -------- | ------------------------ | ------------------------------------------- |
| 1        | Struct fields            | `instance.fieldName`                        |
| 2        | Struct methods           | `instance.method()` from struct declaration |
| 3        | Dict key lookup          | `myDict.key`                                |
| 4        | Enum member lookup       | `Status.Active`                             |
| 5        | Namespace member lookup  | `fs.readFile`                               |
| 6        | **Extension methods**    | Methods from `extend` blocks in scope       |
| 7        | UFCS namespace functions | `str.upper(s)` called as `s.upper()`        |
| 8        | Error                    | `"No method 'name' on type 'typename'"`     |

> **Dict exception:** For dictionaries, extension methods are checked *before* key lookup (priority 6 beats priority 3). Without this, any dict key matching an extension method name would shadow the extension, making dict extensions effectively unusable. The `dict.get(d, "key")` namespace function is available when you need explicit key access.

Key ordering rules:

- **Struct methods before extensions** — methods defined in the original struct declaration take priority over extensions. Extensions cannot silently override the struct author's methods.
- **Extensions before UFCS** — extension methods can shadow UFCS namespace functions:

```stash
extend string {
    fn upper() {
        return "CUSTOM: " + str.upper(self);
    }
}

"hello".upper();        // "CUSTOM: HELLO" — extension wins
str.upper("hello");     // "HELLO" — namespace call unaffected
```

- **Last-registration-wins on conflict** — when two `extend` blocks define a method with the same name for the same type, the last one loaded wins.

### Dict Extensions

Extension methods on dictionaries take priority over key lookup for the same name:

```stash
extend dict {
    fn isEmpty() {
        return len(dict.keys(self)) == 0;
    }
}

let d = dict.new();
d.isEmpty();     // true — calls extension method, not key lookup
```

### Constraints

1. **Methods only** — no fields, constants, or nested types inside `extend` blocks:

```stash
extend string {
    fn valid() { ... }     // OK
    let x = 5;             // ERROR — only fn declarations allowed
}
```

2. **Top-level only** — `extend` cannot appear inside functions, if-blocks, loops, or other scopes:

```stash
fn setup() {
    extend string { ... }  // ERROR — extend must be at top level
}
```

3. **Target type must exist** — the type name must resolve at the point of declaration:

```stash
extend UnknownType { ... }     // ERROR — not a known type
struct User { name }
extend User { ... }             // OK — User is defined above
```

4. **Forward references not allowed** — the struct must be declared before the `extend` block:

```stash
extend Config { ... }          // ERROR — Config not yet defined
struct Config { host, port }
```

### Scoping & Imports

Extension methods follow Stash's import model:

```stash
// string-extras.stash
extend string {
    fn shout() { return self.upper() + "!"; }
}

// main.stash
import "string-extras.stash";
"hello".shout();    // "HELLO!" — extension is active
```

Extensions propagate transitively through imports. Without an import, extensions defined in other files are not visible.

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

**Dynamic import paths** — the path in both forms can be any expression, not just a string literal:

```stash
// Variable path
let modulePath = "utils.stash";
import { deploy } from modulePath;

// Concatenation
let dir = "./lib/";
import { Parser } from dir + "parser.stash";

// String interpolation
let env = env.get("DEPLOY_ENV") ?? "dev";
import { config } from $"./config/{env}.stash";

// Dynamic namespace import
let pluginPath = "./plugins/" + pluginName + ".stash";
import pluginPath as plugin;
plugin.init();
```

The path expression is evaluated at runtime and must produce a string value. If it evaluates to a non-string type, a runtime error is raised.

> **Editor note:** Dynamic import paths cannot be statically analyzed. The language server (LSP) will show an informational hint on dynamic paths indicating that autocomplete, go-to-definition, and other editor features are unavailable for dynamically imported names. Use static string literal paths when possible for the best editor experience.

### Semantics

1. The import path expression is evaluated. For string literals, this is the literal value. For dynamic expressions (variables, concatenation, interpolation), the expression is evaluated at runtime and must produce a string. The resulting path is resolved relative to the importing script's directory.
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

Keywords: `let`, `const`, `fn`, `struct`, `enum`, `if`, `else`, `for`, `in`, `is`, `while`, `do`, `return`, `break`, `continue`, `true`, `false`, `null`, `try`, `retry`, `import`, `as`, `switch`, `and`, `or`, `async`, `await`

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
- `DictLiteralExpr` — `{ key: value, key2: value2 }`
- `IndexExpr` — `arr[i]`
- `IndexAssignExpr` — `arr[i] = val`
- `TernaryExpr` — `cond ? a : b`
- `PipeExpr` — `$(cmd1) | $(cmd2)`
- `RedirectExpr` — `$(cmd) > "file"`, `$(cmd) >> "file"`, `$(cmd) 2> "file"`, `$(cmd) &> "file"`
- `StructInitExpr` — `Server { host: "..." }`
- `CommandExpr` — `$(ls -la)`, `$(grep ${pattern} ${file})`
- `InterpolatedStringExpr` — `$"Hello {name}"`, `"Hello ${name}"`
- `TryExpr` — `try expr`
- `AwaitExpr` — `await expr`
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
- `ForStmt` — `for (init; condition; increment) { ... }`
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

The testing infrastructure follows the same architectural pattern — an `ITestHarness` interface with the same null-guard approach for zero overhead. For testing built-ins (`test.it()`, `test.describe()`, `assert` namespace) and TAP output, see [TAP — Testing Infrastructure](specs/TAP%20—%20Testing%20Infrastructure.md).

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
- [x] ~~Command syntax~~ → `$(command)` literals — always raw mode, `${expr}` for interpolation
- [x] C-style `for(;;)` loop — Implemented. Supports `for (init; cond; update) { ... }` alongside `for-in`.
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
fnDecl         → "async"? "fn" IDENTIFIER "(" parameters? ")" block ;
varDecl        → "let" ( IDENTIFIER | destructurePattern ) "=" expression ";" ;
destructurePattern → "[" IDENTIFIER ("," IDENTIFIER)* "]" | "{" IDENTIFIER ("," IDENTIFIER)* "}" ;
importDecl     → "import" "{" IDENTIFIER ("," IDENTIFIER)* "}" "from" STRING ";"
               | "import" STRING "as" IDENTIFIER ";" ;

statement      → exprStmt | ifStmt | whileStmt | forStmt | returnStmt | breakStmt | continueStmt | block ;

exprStmt       → expression ";" ;
ifStmt         → "if" "(" expression ")" block ( "else" (ifStmt | block) )? ;
whileStmt      → "while" "(" expression ")" block ;
forStmt        → forInStmt | forCStyleStmt ;
forInStmt      → "for" "(" "let" IDENTIFIER ( "," IDENTIFIER )? ( ":" IDENTIFIER )? "in" expression ")" block ;
forCStyleStmt  → "for" "(" ( varDecl | exprStmt | ";" ) expression? ";" expression? ")" block ;
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
comparison     → range ( ("<" | ">" | "<=" | ">=" | "in" | "is") range )* ;
range          → term ( ".." term ( ".." term )? )? ;
term           → factor ( ("+" | "-") factor )* ;
factor         → unary ( ("*" | "/" | "%") unary )* ;
unary          → ("!" | "-") unary | prefix ;
prefix         → ("++" | "--") IDENTIFIER | awaitExpr ;
awaitExpr      → "await" unary | tryExpr ;
tryExpr        → "try" unary | postfix ;
postfix        → call ("++" | "--")? ;
call           → primary ( "(" arguments? ")" | "." IDENTIFIER | "[" expression "]" | "switch" "{" switchArm ("," switchArm)* ","? "}" )* ;
switchArm      → ( "_" | expression ) "=>" expression ;
primary        → NUMBER | STRING | INTERPOLATED_STRING | "true" | "false" | "null"
               | IDENTIFIER | lambdaExpr | "(" expression ")"
               | "[" (expression ("," expression)*)? "]"
               | (call ".")? IDENTIFIER "{" (IDENTIFIER ":" expression ("," IDENTIFIER ":" expression)*)? "}"
               | "$(" COMMAND_TEXT ")" ;
lambdaExpr     → "async"? "(" parameters? ")" "=>" ( block | assignment ) ;

parameter      → IDENTIFIER ( ":" IDENTIFIER )? ( "=" expression )? ;
parameters     → parameter ( "," parameter )* ;
arguments      → expression ( "," expression )* ;
```

---

_This is a living document. Update as design decisions are finalized and implementation progresses._
