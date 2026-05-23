# UFCS — Uniform Function Call Syntax

> **Status:** Approved Proposal
> **Created:** April 2026
> **Purpose:** Allow namespace functions to be called as methods on values, enabling left-to-right chaining syntax alongside the existing `namespace.function(value)` syntax.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Design](#2-design)
3. [Type-to-Namespace Mapping](#3-type-to-namespace-mapping)
4. [Semantics](#4-semantics)
5. [Resolution Rules](#5-resolution-rules)
6. [Scope & Limitations](#6-scope--limitations)
7. [Examples](#7-examples)
8. [Implementation Strategy](#8-implementation-strategy)
9. [LSP Integration](#9-lsp-integration)
10. [Future: `extend` Blocks](#10-future-extend-blocks)

---

## 1. Motivation

Stash organizes built-in functions into namespaces: `str.upper(s)`, `arr.push(a, v)`, `dict.keys(d)`. While consistent, this forces inside-out nesting for chained operations:

```stash
// Current: inside-out — read right-to-left
let result = str.split(str.upper(str.trim(input)), ",");

// Desired: left-to-right chaining
let result = input.trim().upper().split(",");
```

Developers from Python (`s.upper()`), JavaScript (`s.trim()`), Ruby (`s.upcase`), and C# (`s.ToUpper()`) expect method call syntax on values. The gap is documented in [Missing Scripting Fundamentals — Gap Analysis §6.1](Missing%20Scripting%20Fundamentals%20—%20Gap%20Analysis.md).

### Design Principles

1. **No breaking changes** — `str.upper(s)` continues to work identically
2. **No wrapper types** — primitives remain `string`, `long`, `double`, `bool` internally
3. **No new AST nodes** — `s.upper()` already parses as `CallExpr(DotExpr(s, "upper"), [])`
4. **Minimal interpreter changes** — one new fallback branch in `VisitDotExpr`
5. **Both syntaxes coexist permanently** — neither is deprecated

---

## 2. Design

UFCS allows any namespace function whose first parameter matches the receiver's type to be called as a method on that value. The receiver is implicitly prepended to the argument list.

```stash
// These two are equivalent:
str.upper(s)        // explicit namespace call
s.upper()           // UFCS method call

// With arguments — receiver becomes the first argument:
str.split(s, ",")   // explicit
s.split(",")         // UFCS — s is prepended as first arg

// Chaining — each call returns a value, which becomes the next receiver:
input.trim().upper().split(",")
// Equivalent to: str.split(str.upper(str.trim(input)), ",")
```

### Formal Rule

Given an expression `receiver.method(arg1, arg2, ...)`:

1. If the receiver is a struct instance, enum, namespace, or dictionary — use existing resolution (unchanged)
2. Otherwise, determine the receiver's runtime type
3. Look up the corresponding namespace for that type
4. If the namespace has a function named `method` — return a bound callable that prepends the receiver to the argument list
5. If no match — throw a runtime error

---

## 3. Type-to-Namespace Mapping

Only type-centric namespaces participate in UFCS. Module-centric namespaces (`fs`, `http`, `sys`, etc.) are excluded because their functions don't operate on a value of a specific type.

### Primary Mappings

| Runtime Type | C# Type         | Namespace | Coverage                  |
| ------------ | --------------- | --------- | ------------------------- |
| `string`     | `string`        | `str`     | ~95% of `str.*` functions |
| `array`      | `List<object?>` | `arr`     | ~95% of `arr.*` functions |

### Excluded from UFCS

| Namespace  | Reason                                                                                                                                                                                                                                                   |
| ---------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `dict`     | Conflicts with dictionary dot-access for key lookup — `d.keys` is ambiguous between the dict entry `"keys"` and `dict.keys(d)`. Dict methods remain namespace-only.                                                                                      |
| `math`     | Mixed — some functions are type-centric (`math.abs(n)`), many are not (`math.random()`, `math.PI`). Static namespace access is more natural for math.                                                                                                    |
| `conv`     | Receiver type is ambiguous — `conv.toStr(val)` applies to all types, which makes UFCS mapping unclear.                                                                                                                                                   |
| All others | Module-centric (no natural receiver) — `fs`, `http`, `sys`, `env`, `io`, `path`, `process`, `log`, `time`, `json`, `yaml`, `toml`, `ini`, `config`, `crypto`, `encoding`, `term`, `store`, `tpl`, `test`, `assert`, `args`, `ssh`, `sftp`, `task`, `pkg` |

### Rationale for Dict Exclusion

Dictionaries use dot access for key lookup: `myDict.someKey` returns the value at key `"someKey"`. Adding UFCS would create ambiguity:

```stash
let d = { "keys": "my value" };
d.keys;       // Today: returns "my value" (dict entry lookup)
d.keys();     // With UFCS: would this call dict.keys(d) or try to call "my value" as a function?
```

To avoid this confusion, dictionaries retain namespace-only syntax: `dict.keys(d)`, `dict.values(d)`, etc. This is a deliberate trade-off — dict operations are less commonly chained than string/array operations.

---

## 4. Semantics

### Argument Rewriting

UFCS rewrites the call by prepending the receiver as the first argument:

```stash
s.upper()           → str.upper(s)            // 0 args → 1 arg
s.split(",")         → str.split(s, ",")       // 1 arg  → 2 args
s.padStart(10, "0") → str.padStart(s, 10, "0") // 2 args → 3 args

a.push(42)          → arr.push(a, 42)
a.map((x) => x * 2) → arr.map(a, (x) => x * 2)
```

### Arity Adjustment

The UFCS-bound callable reports `Arity - 1` (and `MinArity - 1`) compared to the underlying namespace function, since the receiver is implicit:

```stash
str.upper     // arity 1 (takes s)
s.upper       // arity 0 (receiver is implicit)

str.split     // arity 2 (takes s, delimiter)
s.split       // arity 1 (takes delimiter; s is implicit)
```

### Return Values

UFCS does not alter return values. The return type is whatever the namespace function returns. This is what enables chaining:

```stash
input.trim()          // returns string → string has UFCS methods
     .upper()         // returns string → string has UFCS methods
     .split(",")      // returns array  → array has UFCS methods
     .map((x) => x.trim())  // returns array
```

### Truthiness, Equality, Type

UFCS is purely syntactic dispatch. It does not affect truthiness, equality, or `typeof()`. A string is still `"string"`, an array is still `"array"`.

---

## 5. Resolution Rules

When evaluating `receiver.name`, the interpreter follows this precedence:

1. **StashError** — access `.message`, `.type`, `.stack` fields
2. **StashInstance** (struct) — field lookup, then method lookup (returns `StashBoundMethod`)
3. **StashDictionary** — key lookup (returns entry value)
4. **StashEnum** — member lookup (returns `StashEnumValue`)
5. **StashNamespace** — member lookup (returns function/constant)
6. **UFCS lookup** _(new)_ — map receiver type to namespace, look up function, return `BuiltInBoundMethod`
7. **Error** — "No member 'name' on type 'typename'"

### Priority

Existing resolution (steps 1–5) always takes priority over UFCS (step 6). This means:

- Struct fields shadow UFCS methods of the same name
- Dict entries shadow UFCS methods (if dict UFCS were ever enabled)
- Namespace members are resolved before UFCS

UFCS is a **fallback**, not an override.

---

## 6. Scope & Limitations

### What UFCS Enables

```stash
// String chaining
let slug = title.trim().lower().replaceAll(" ", "-");

// Array pipelines
let adults = users.filter((u) => u.age >= 18).sortBy((u) => u.name).map((u) => u.email);

// Mixed chaining
let words = fs.readFile("data.txt").trim().split("\n").map((line) => line.trim());
```

### What UFCS Does NOT Enable

```stash
// No UFCS on dicts (ambiguous with key lookup)
d.keys()           // ERROR — use dict.keys(d)

// No UFCS on null
null.something()   // ERROR — null has no methods

// No UFCS on booleans
true.toStr()       // ERROR — no namespace maps to bool

// No UFCS for functions with no natural receiver
str.format("{0} is {1}", name, age)  // No UFCS — which arg is the receiver?
str.join(",", parts)                  // No UFCS — separator isn't the "string being operated on"
arr.zip(a, b)                         // No UFCS — two arrays, which is the receiver?
arr.of(1, 2, 3)                       // No UFCS — factory function, no existing value
```

### Functions Excluded from UFCS per Namespace

Within mapped namespaces, functions where the first parameter is NOT the receiver type are excluded from UFCS. They remain accessible only via namespace syntax.

**`str` exclusions:**

- `str.format(template, ...args)` — template string isn't meaningfully a receiver
- `str.join(delimiter, array)` — delimiter is a string but the operation is on the array

**`arr` exclusions:**

- `arr.of(...values)` — factory, no existing array
- `arr.zip(a, b)` — two arrays, neither is a clear receiver
- `arr.range(start, end)` — factory

---

## 7. Examples

### String Processing

```stash
// Before (nested):
let result = str.replaceAll(str.lower(str.trim(input)), " ", "-");

// After (chained):
let result = input.trim().lower().replaceAll(" ", "-");
```

### Array Pipelines

```stash
// Before:
let names = arr.map(
    arr.filter(
        arr.sortBy(users, (u) => u.age),
        (u) => u.active
    ),
    (u) => u.name
);

// After:
let names = users
    .sortBy((u) => u.age)
    .filter((u) => u.active)
    .map((u) => u.name);
```

### Mixed Namespace and UFCS

```stash
// Both syntaxes work — use whichever is clearer:
let lines = fs.readFile("log.txt").split("\n");     // fs.readFile is namespace, .split is UFCS
let count = len(lines.filter((l) => l.contains("ERROR")));  // .filter and .contains are UFCS

// Namespace syntax remains available for disambiguation or preference:
let upper = str.upper(name);   // explicit
let upper = name.upper();      // UFCS — equivalent
```

---

## 8. Implementation Strategy

### New Runtime Type: `BuiltInBoundMethod`

A lightweight `IStashCallable` wrapper (~20 lines) that binds a receiver to a namespace function:

```
BuiltInBoundMethod
├── _receiver: object?       — the value (e.g., the string "hello")
├── _function: IStashCallable — the namespace function (e.g., str.upper)
├── Arity → _function.Arity - 1
├── MinArity → max(0, _function.MinArity - 1)
└── Call(ctx, args) → _function.Call(ctx, [_receiver, ...args])
```

### Interpreter Change: `VisitDotExpr` Fallback

In `Interpreter.Expressions.cs`, `VisitDotExpr` currently throws for primitives. Add a UFCS fallback before the error:

```
Existing dispatch:
  StashError    → field access
  StashInstance → field/method access
  StashDictionary → key lookup
  StashEnum     → member lookup
  StashNamespace → function lookup
+ NEW: UFCS     → type-to-namespace lookup → BuiltInBoundMethod
  Error         → "No member 'x' on type 'y'"
```

### Type-to-Namespace Resolution

A static/frozen mapping from C# runtime type to namespace name. Stored in the interpreter or as a utility:

```
string         → "str"
List<object?>  → "arr"
```

The mapping consults the global environment to retrieve the `StashNamespace`, then checks `HasMember(methodName)`.

### Files Changed

| File                         | Change                                                 |
| ---------------------------- | ------------------------------------------------------ |
| New: `BuiltInBoundMethod.cs` | New class in `Stash.Core/Runtime/Types/` (~25 lines)   |
| `Interpreter.Expressions.cs` | Add UFCS fallback in `VisitDotExpr` (~20 lines)        |
| `SemanticValidator.cs`       | Validate UFCS calls, suppress false "no member" errors |
| `SymbolCollector.cs`         | Record UFCS method references for Find References      |
| `SemanticTokenWalker.cs`     | Classify UFCS method tokens for highlighting           |
| `StdlibRegistry.cs`          | Add `GetNamespaceForType(string)` query method         |

### Files NOT Changed

- **No parser changes** — `s.upper()` already parses correctly
- **No lexer changes** — no new tokens
- **No AST changes** — no new node types
- **No BuiltIn files** — all 31 namespace implementations remain untouched
- **No existing tests** — all current behavior is preserved

---

## 9. LSP Integration

### Autocomplete on Typed Values

When the LSP detects a `.` after a value with a known type, it should offer UFCS methods:

```stash
myString.     // Shows: upper(), lower(), trim(), split(delimiter), contains(sub), ...
myArray.      // Shows: push(item), pop(), map(callback), filter(callback), ...
```

Method signatures in completion items should **exclude the first parameter** (the implicit receiver):

```
str.upper(s: string) → string       // Namespace signature
→ displayed as:
upper() → string                     // UFCS completion item
```

### Hover Information

Hovering over a UFCS method call should show the full namespace function signature with a note:

```
(UFCS) str.upper(s: string) → string
Returns the string converted to uppercase.
```

### Go-to-Definition

UFCS method references should resolve to the underlying namespace function definition for Go-to-Definition and Find References support.

---

## 10. Future: `extend` Blocks

UFCS provides method syntax for built-in namespace functions. A future `extend` feature would allow users and packages to add custom methods to types. This is documented separately in [Extend Blocks — Type Extension Methods](Extend%20Blocks%20—%20Type%20Extension%20Methods.md).

The UFCS infrastructure (type-to-method resolution in `VisitDotExpr`, `BuiltInBoundMethod` pattern) provides the foundation that `extend` blocks would build upon.

---

## Appendix: Comparison with Other Languages

| Language   | Feature              | Syntax                                  | Scope                                   |
| ---------- | -------------------- | --------------------------------------- | --------------------------------------- |
| **D**      | UFCS                 | `f(x, y)` ↔ `x.f(y)`                    | Bidirectional, any free function        |
| **Nim**    | UFCS                 | `f(x, y)` ↔ `x.f(y)`                    | Bidirectional, any proc                 |
| **Rust**   | No UFCS              | Methods via `impl` blocks only          | Must explicitly implement               |
| **Kotlin** | Extension functions  | `fun String.isPalin()`                  | Declared per type, imported             |
| **C#**     | Extension methods    | `static void Upper(this string s)`      | Requires `using`, static class          |
| **Swift**  | Extensions           | `extension String { ... }`              | Open extension on any type              |
| **Stash**  | UFCS (this proposal) | Namespace functions callable as methods | One-directional, type-mapped namespaces |
