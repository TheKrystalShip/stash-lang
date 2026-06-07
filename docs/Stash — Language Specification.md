# Stash - Language Specification

> **Status:** Draft v1 language specification
> **Audience:** implementers, tool authors, and advanced users
> **Purpose:** normative source of truth for Stash syntax and language semantics.

Stash is a dynamically typed, interpreted scripting language with C-style syntax and
first-class shell integration. This document specifies the behavior of the language
itself. The standard library, tools, package manager, bytecode format, and editor
protocols are specified separately.

**Companion documents:**

- [Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md) - built-in namespaces, global functions, and library errors
- [Shell - Interactive Shell Mode](Shell%20%E2%80%94%20Interactive%20Shell%20Mode.md) - interactive shell behavior
- [Bytecode VM - Instruction Set Reference](Bytecode%20VM%20%E2%80%94%20Instruction%20Set%20Reference.md) - VM instruction semantics
- [Bytecode VM - Binary Format](Bytecode%20VM%20%E2%80%94%20Binary%20Format%20%28.stashc%29.md) - compiled bytecode format
- [LSP - Language Server Protocol](LSP%20%E2%80%94%20Language%20Server%20Protocol.md) - language server behavior
- [DAP - Debug Adapter Protocol](DAP%20%E2%80%94%20Debug%20Adapter%20Protocol.md) - debug adapter behavior
- [TAP - Testing Infrastructure](TAP%20%E2%80%94%20Testing%20Infrastructure.md) - test primitives and TAP output
- [PKG - Package Manager CLI](PKG%20%E2%80%94%20Package%20Manager%20CLI.md) - package management
- [Registry - Package Registry](Registry%20%E2%80%94%20Package%20Registry.md) - registry service behavior

---

## Contents

1. [Conformance](#conformance)
2. [Terminology](#terminology)
3. [Lexical Structure](#lexical-structure)
4. [Source Files and Modules](#source-files-and-modules)
5. [Values and Types](#values-and-types)
6. [Bindings and Scope](#bindings-and-scope)
   - [The `readonly` Modifier](#the-readonly-modifier)
7. [Expressions](#expressions)
8. [Statements and Control Flow](#statements-and-control-flow)
9. [Functions, Closures, and Async](#functions-closures-and-async)
10. [Aggregate Types and Members](#aggregate-types-and-members)
11. [Function References](#function-references)
12. [Namespace Members](#namespace-members)
13. [Shell Integration](#shell-integration)
14. [Errors and Cleanup](#errors-and-cleanup)
15. [Runtime Behavior](#runtime-behavior)
16. [Appendix A: Grammar](#appendix-a-grammar)
17. [Appendix B: Reserved and Contextual Syntax](#appendix-b-reserved-and-contextual-syntax)

---

## Conformance

An implementation conforms to this specification if it accepts every syntactically
valid program described here and gives it the observable behavior specified here.
If this document says a construct "is a parse error", a conforming implementation
must reject that source before execution. If this document says an operation
"produces a runtime error", a conforming implementation must report an error during
evaluation and must not continue as if the operation succeeded.

This document uses the following normative terms:

- **must** and **must not** describe required behavior.
- **may** describes permitted behavior.
- **parse error** means the source text is not a valid Stash program.
- **runtime error** means evaluation fails after parsing succeeds.
- **implementation-defined** behavior must be documented by the implementation.

Implementation architecture, bytecode opcodes, optimizer behavior, editor protocol
details, and standard-library function contracts are outside the scope of this
document unless they affect source-language semantics.

## Terminology

A **program** is a sequence of declarations and statements. A **source file** is a
program stored in a file. A **module** is a source file evaluated for import.

A **binding** associates a name with a value. A binding declared with `let` is
mutable. A binding declared with `const` is immutable after initialization.

An **expression** evaluates to a value or produces a runtime error. A **statement**
performs control flow, creates bindings, or evaluates an expression for its effects.

A value is **truthy** or **falsey** according to the truthiness rules in
[Truthiness](#truthiness). A value is **nullish** only when it is `null`.

## Lexical Structure

### Source Text

Stash source text is Unicode text. The language syntax is defined in terms of
characters and tokens. Implementations may normalize source text for diagnostics,
but tokenization must preserve the program's lexical meaning.

A semicolon terminates most declarations and expression statements. Blocks are
delimited with `{` and `}`.

### Shebang

If a source file begins with `#!`, the first line is a shebang line. A shebang line
must be ignored for language semantics.

```stash
#!/usr/bin/env stash
io.println("hello");
```

### Comments

Line comments begin with `//` and continue to the end of the line. Block comments
begin with `/*` and end with `*/`.

```stash
// line comment
/* block
   comment */
```

Comments are trivia and must not affect program behavior.

### Documentation Comments

Documentation comments attach to the declaration immediately following them.
Triple-slash comments use `///`. Block documentation comments use `/** ... */`.

```stash
/// Adds two numbers.
/// @param a left operand
/// @param b right operand
/// @return the sum
fn add(a, b) {
    return a + b;
}
```

The supported documentation tags are `@param`, `@return`, `@returns`, and
`@throws`. Four leading slashes (`////`) are a regular comment, not a documentation
comment. An empty `/**/` is a regular block comment.

### Identifiers and Keywords

Identifiers name variables, constants, functions, parameters, fields, types,
modules, and namespaces. Identifiers are case-sensitive.

The following words are reserved keywords:

```text
as break case catch const continue default defer do else enum extend false
finally fn for if import in interface is let lock null return struct switch
throw true try while
```

The following words are contextual keywords. They have keyword meaning only in the
positions specified by this document:

```text
and async await elevate export from onRetry or retry timeout until
```

### Literals

Stash has literal syntax for `null`, booleans, numbers, strings, arrays,
dictionaries, command expressions, IP addresses, durations, byte sizes, and semantic
versions.

```stash
null;
true;
42;
3.14;
"text";
[1, 2, 3];
{ name: "web", port: 443 };
@192.168.1.1;
5m30s;
128MB;
@v1.2.3-beta.1;
```

### String Literals

Double-quoted strings produce string values.

```stash
let path = "C:\\Users\\Admin";
let msg = "hello\nworld";
```

The following escapes must be recognized:

| Escape | Meaning         |
| ------ | --------------- |
| `\\`   | backslash       |
| `\"`   | double quote    |
| `\n`   | line feed       |
| `\t`   | horizontal tab  |
| `\r`   | carriage return |
| `\0`   | NUL             |
| `\$`   | dollar sign     |

Any other backslash escape is a lex error.

### Interpolated Strings

Stash supports embedded interpolation and prefixed interpolation.

```stash
let name = "world";
let a = "hello ${name}";
let b = $"hello {name}";
```

Interpolation slots contain Stash expressions. The expression is evaluated at
runtime and converted to a string. A regular string without `$"..."` or `${...}`
must not interpolate.

### Multi-line Strings

Triple-quoted strings may span multiple lines.

```stash
let script = """
    set -e
    echo "deploy"
""";
```

Multi-line strings support the same escape sequences as ordinary strings.
Interpolation is allowed where the string form is explicitly interpolated. Common
indentation may be stripped by the implementation according to the documented
multi-line string indentation rule; this transformation must be deterministic.

### Numeric Literals

Integer literals produce integer values. Floating-point literals produce floating
values. Number literals may use decimal, binary (`0b`), octal (`0o`), and
hexadecimal (`0x`) notation where accepted by the lexer.

```stash
let dec = 42;
let bin = 0b101010;
let oct = 0o52;
let hex = 0x2A;
let pi = 3.14159;
```

### Domain Literals

IP address literals begin with `@` and produce IP address values.

```stash
@127.0.0.1;
@::1;
@10.0.0.0/24;
```

Duration literals produce duration values.

```stash
500ms;
5s;
2h30m;
```

Byte-size literals produce byte-size values.

```stash
100B;
1.5MB;
2GB;
```

Semantic-version literals begin with `@v`.

```stash
@v1.0.0;
@v1.0.0-rc.1+build.456;
```

Semantic-version comparison must compare major, minor, and patch numerically.
Prerelease identifiers sort before the corresponding release version. Build metadata
must be ignored for ordering and equality.

## Source Files and Modules

### Program Structure

A source file is parsed as a sequence of declarations and statements until end of
file.

```stash
import { readConfig } from "config.stash";

const DEFAULT_PORT = 443;

fn main() {
    io.println(DEFAULT_PORT);
}
```

Top-level statements execute in source order when the file is evaluated as a
program or module.

### Imports

Selective imports bind names exported by another module into the current scope.

```stash
import { deploy, rollback } from "ops.stash";
```

Namespace imports bind the evaluated module to a namespace name.

```stash
import "ops.stash" as ops;
ops.deploy();
```

The module path expression must evaluate to a string. An implementation must resolve
relative module paths relative to the importing file unless it documents a different
module resolution base.

Import cycles produce a runtime error unless an implementation explicitly documents
cycle handling.

### Package Name Grammar

A **package name** is the string value used to identify a published package in `stash.json` manifests and in import paths that reference registry packages. Only the **scoped form** is valid:

```
@[a-z][a-z0-9-]{0,38}/[a-z][a-z0-9-]{0,38}
```

Formally:

```
packageName = "@" scopeSegment "/" nameSegment
scopeSegment = [a-z][a-z0-9-]{0,38}
nameSegment  = [a-z][a-z0-9-]{0,38}
```

Rules:
- Each segment starts with a lowercase ASCII letter (`[a-z]`).
- Each segment contains only lowercase letters, digits, and hyphens (`[a-z0-9-]`).
- Each segment is between 1 and 39 characters (one leading letter plus up to 38 continuation characters).
- The `@` prefix is mandatory; flat (unscoped) names such as `http` or `stash-http` are invalid.
- The `/` separator is mandatory and appears exactly once, after the scope segment.
- Combined length of `@{scope}/{name}` must not exceed 64 characters.

Examples of valid names: `@stash/http`, `@my-org/widget`, `@alice/tools`.
Examples of invalid names: `http` (no scope), `@stash` (no `/`), `@Stash/Http` (uppercase), `@stash/` (empty name segment).

`PackageManifest.IsValidPackageName` implements this check using the generated regex
`^@[a-z][a-z0-9-]{0,38}/[a-z][a-z0-9-]{0,38}$`. `ValidateForPublishing` rejects a manifest whose `name` does not match.

In URL routing the leading `@` is stripped: package `@stash/http` is addressed as `/api/v1/packages/stash/http`. The registry server canonicalizes back to the `@{scope}/{name}` form for response bodies and database lookups.

### Exports

By default, every top-level binding in a Stash module is **module-private** —
not visible to importers. A symbol is exported only when it is explicitly
annotated with the `export` keyword, either at the declaration site or via a
standalone `export { }` block. There is one rule: if you want a name accessible
to importers, you must annotate it with `export`.

#### Declaration-site form

Prefix any supported top-level declaration with `export`:

```stash
export fn diff(a, b) { ... }
export async fn fetch(url) { ... }
export const VERSION: str = "1.0.0";
export struct Point { x: int, y: int }
export enum Status { Ok, Err }
export interface Closer { fn close() }
```

Once any `export` annotation appears in a file, only annotated symbols are
visible to importers; all other top-level symbols become module-private.

#### Block form

Names may also be listed in a standalone `export { }` block anywhere at the
top level:

```stash
fn helper() { ... }           // module-private
fn diff(a, b) { ... }
const VERSION = "1.0.0";

export { diff, VERSION };     // only diff and VERSION are exported
```

The two forms may be combined in a single file. The effective export set is
their union:

```stash
export fn diff(a, b) { ... }
fn _internal() { ... }
const VERSION = "1.0.0";
export { VERSION };           // diff (from decl-site) and VERSION are exported
```

An empty block `export { }` is valid and the module exposes zero symbols —
useful for scripts that are loaded for side effects only.

#### Syntax restrictions

The following forms are parse-time errors:

| Rejected form                   | Reason                                                                     |
| ------------------------------- | -------------------------------------------------------------------------- |
| `export let x = 0;`             | Mutable bindings cannot be exported. Use an accessor function.             |
| `export extend Type { ... }`    | `extend` is a side-effect declaration with no name; it cannot be exported. |
| `export import "x.stash" as x;` | Import statements cannot be exported.                                      |

The static analyzer enforces additional semantic constraints and reports them
as diagnostics:

| Diagnostic | Trigger                                                                                              |
| ---------- | ---------------------------------------------------------------------------------------------------- |
| SA0805     | `export { x }` where `x` is a `let` binding                                                          |
| SA0806     | `export { x }` where `x` is an imported name                                                         |
| SA0807     | `export { x }` where `x` is not declared at the top level                                            |
| SA0808     | A name appears in the export set more than once (via any combination of forms)                       |
| SA0809     | An importer references a name that exists in the module but is not exported (information-level hint) |

### Re-exports

A re-export statement combines an `import` with an export-set contribution in a
single declaration. The primary motivation is **barrel files** (`index.stash`)
that collect and re-expose symbols from several sub-modules so consumers can
import from one place.

#### Form 1: Namespace re-export

```stash
export "lib/data.stash" as data;
```

This is exactly equivalent to writing:

```stash
import "lib/data.stash" as data;
export { data };
```

The alias `data` is **also bound as a local** in the same module (see
"Same-module binding" below), so the same file may use it immediately:

```stash
export "lib/math.stash" as math;

// Same-module use — works because `math` is a local binding too.
io.println(math.pi);
```

#### Form 2: Selective re-export

```stash
export { Color, Size, Direction } from "lib/types.stash";
```

This is exactly equivalent to:

```stash
import { Color, Size, Direction } from "lib/types.stash";
export { Color, Size, Direction };
```

A trailing comma after the last name is allowed. An empty list
`export {} from "p";` is rejected as an error (SA0823).

The imported names are **also bound as locals** in the re-exporting module:

```stash
export { Color } from "lib/types.stash";

// Same-module use — `Color` is a local binding.
let bg = Color.Red;
io.println(bg);
```

#### Path is an expression (D-9)

Both forms accept any expression that evaluates to a string at runtime,
mirroring `import`'s grammar:

```stash
const TYPES_PATH = "lib/types.stash";
export { Color } from TYPES_PATH;

const MATH_MOD  = "lib/math.stash";
export MATH_MOD as math;
```

#### No wildcard re-export

`export * from "p";` is a compile-time error (SA0822). Stash rejects wildcard
imports on principle; wildcard re-exports are symmetrically disallowed.

#### Same-module binding (D-12)

A re-export statement **also introduces the alias or selected names as local
bindings** in the current module, exactly as if a plain `import` statement had
appeared. The runtime value (a namespace, function, struct type, etc.) is the
same object a regular `import` would produce.

```stash
// index.stash — barrel + same-module use
export "lib/fmt.stash"   as fmt;
export { encode, decode } from "lib/codec.stash";

// Works inside the same file:
io.println(fmt.version);
let raw = encode("hello");
```

This makes re-export statements a strict superset of their two-line
`import` + `export` equivalents — migrating from the two-line form never
removes a local binding that was already usable.

#### Unused-import interaction (D-11)

Re-export statements count as **uses** of the imported name or module.
A barrel file that only imports and re-exports a name does NOT trigger the
unused-import diagnostic, because contributing a name to the export set is a
meaningful use.

```stash
// This barrel file emits no SA-unused diagnostics.
export "lib/fmt.stash"   as fmt;
export { Color, Size }   from "lib/types.stash";
```

#### Diagnostics

| Diagnostic | Level | Trigger | Example |
| ---------- | ----- | ------- | ------- |
| SA0822 | Error | Wildcard re-export (`export * from "p"`) | `export * from "lib/x.stash";` |
| SA0823 | Error | Empty re-export list (`export {} from "p"`) | `export {} from "lib/x.stash";` |
| SA0824 | Error | Re-export alias collides with an existing top-level binding | `const fmt = 1; export "lib/fmt.stash" as fmt;` |
| SA0825 | Error | Re-exported name not in the source module's export set | `export { _internal } from "lib/x.stash";` |
| SA0826 | Error | Re-export cycle detected | `A` re-exports `B`, `B` re-exports `A` |
| SA0827 | Information | Redundant `import { x } from "p"; export { x };` pair — use `export { x } from "p";` instead | `import { x } from "p"; export { x };` |

#### Bytecode invariant

Re-export forms compile to the same bytecode as their `import` equivalents
plus an export-set entry. No new opcodes are required, and `.stashc` files
remain at format version v4 — the serializer is unchanged.

#### Interaction with `fn export(...)`

`export` is a **contextual (soft) keyword**. It gains keyword meaning only at
statement boundaries when followed by `fn`, `async`, `const`, `struct`, `enum`,
`interface`, `{`, or an **expression starter** (for the path re-export form). In
all other positions — including function names, variable names, and call
expressions — it is a plain identifier:

```stash
fn export(container, path) { ... }   // valid: "export" is the function name
export(container, "/tmp/out");       // valid: call expression
let export = 5;                      // valid: let binding named "export"
```

The path re-export form (`export <expr> as <id>;`) is distinguished from a call
expression `export(...)` by a lookahead rule: the keyword activates only when
the token sequence from the current position to the next depth-0 semicolon ends
in `as <Identifier> ;`.

## Values and Types

### Type Model

Stash is dynamically typed. Values carry runtime type information. Type
annotations may appear in declarations and signatures; they are advisory
metadata consumed by editor tooling and the static analyzer and are **erased
at compile time**. They do not participate in runtime dispatch. See _Type
Hints_ below for the full statement.

The core value categories are:

| Type name   | Description                                  |
| ----------- | -------------------------------------------- |
| `null`      | absence of a value                           |
| `bool`      | `true` or `false`                            |
| `int`       | integer number                               |
| `float`     | floating-point number                        |
| `byte`      | integer value in the range 0..255            |
| `string`    | text value                                   |
| `array`     | ordered mutable sequence                     |
| typed array | primitive homogeneous byte array (`byte[]`) |
| `dict`      | key-value mapping                            |
| `range`     | half-open integer range (a..b)               |
| `struct`    | user-defined aggregate instance              |
| `enum`      | enum member value                            |
| `interface` | interface type used for conformance checks   |
| `namespace` | standard-library or imported namespace       |
| `function`  | callable function or lambda                  |
| `Future`    | asynchronous computation                     |
| `Error`     | throwable error value                        |
| `secret`    | redaction wrapper                            |
| `ip`        | IP address or network                        |
| `duration`  | elapsed time duration                        |
| `bytes`     | byte quantity                                |
| `semver`    | semantic version                             |

The type names above are the complete closed set returned by `typeof`. A future
runtime value category is a breaking change to §Values and Types; tooling and
libraries may rely on this set being closed.

### Ranges

A `range` value (constructed by the `..` operator; see §Expressions) is a
first-class value with `typeof` string `"range"`. Ranges compare by reference
identity (`1..3 == 1..3` is `false` for two distinct constructions; the same
range value compared to itself is `true`). A range is always truthy,
regardless of whether the range is empty (`0..0`). Iteration semantics, `in`
membership, and arithmetic interactions are defined in §Expressions.

### `typeof` and `nameof`

`typeof(value)` returns one of the strings enumerated in the type table above —
a closed vocabulary. It is total: it never raises for any value, including
`null`. For `int`, `float`, `bool`, `null`, `byte`, `string`, `array`, `dict`,
`range`, `function`, `namespace`, `secret`, `duration`, `bytes`, `ip`, and
`semver`, the string names the runtime category directly.

For aggregate, opaque, and user-defined values:

- `typeof(struct_instance)` returns `"struct"`. To recover the user-visible
  struct name, use `nameof(instance)`.
- `typeof(enum_value)` returns `"enum"`. `nameof(enum_value)` returns the
  **fully-qualified member name** — the enum type name joined to the member
  name by a dot (e.g. `nameof(Color.Red) == "Color.Red"`). This differs from
  `nameof(Color)` (the enum *type* identifier), which returns the bare type
  name `"Color"`. It also differs from struct instances, where
  `nameof(struct_instance)` returns only the type name (e.g. `"P"`).
- `typeof(struct_type)` / `typeof(enum_type)` (the type *identifier*) returns
  `"struct"` / `"enum"` respectively; the *type* and its *instance* are
  indistinguishable by `typeof`.
- `typeof(future)` returns `"Future"` (capitalized; Future is a runtime category,
  not a user-defined type — the capitalization is normative).
- `typeof(error)` returns `"Error"` for any first-class Error value caught by
  `try/catch`. The user-visible error type (e.g. `"TypeError"`) is recoverable
  via `nameof(err)` or the `err.type` property.
- `typeof(typed_array)` returns the element-type string suffixed with `[]`.
  The only reachable typed array in the current runtime is `byte[]` (constructed
  via `buf.from(...)`, `buf.alloc(...)`, or a `process.read*` byte buffer).
  Adding a new typed-array category (e.g. `int[]`, `float[]`, `string[]`) is a
  breaking change to §Values and Types per the closed-set sentence at the end of
  the type table; type annotations of that form erase to plain `array` at compile
  time and never produce those typeof strings.

`nameof(value)` returns the declared type or binding name where one exists,
and falls back to the same string `typeof` would return for values with no
named declaration. For user-defined struct, enum, and interface types,
`nameof` returns the user-visible name (e.g. `nameof(Server) == "Server"`).
**Exception — enum values:** `nameof(enum_value)` returns the fully-qualified
member path, not just the type name (e.g. `nameof(Color.Red) == "Color.Red"`
rather than `"Color"`). The enum *type* identifier and struct instances do not
share this exception: `nameof(Color) == "Color"` and `nameof(p) == "P"` (where
`p` is a struct instance).

### Truthiness

The following values, and no others, are **falsey**:

- `null`
- `false`
- the integer `0`
- the float `0.0` (and the float `-0.0`)
- the byte `0`
- the empty string `""`

**Every other value is truthy.** This explicitly includes:

- The empty array `[]` and the empty dictionary `{}`. Use `len(value) == 0` or
  `arr.isEmpty(value)` / `dict.isEmpty(value)` for emptiness checks.
- Every caught Error value. `try { ... } catch (e) { if (e) { ... } }` enters
  the `if` body — `e` is a real value, not an absence-marker.
- Every struct instance, enum value, function, namespace, future, range, secret,
  `ip`, `duration`, `bytes`, and `semver` value, regardless of internal state.
- `NaN` (the float). Use `value != value` to detect NaN.
- The string `" "` (a single space).

Conditions in `if`, `while`, `do while`, ternary expressions, logical operators
(`&&`, `||`, `!`), and retry predicates (`until`) use these rules. The same rules
apply uniformly in CLI, embedded host, LSP runtime, and DAP-driven execution
contexts.

### Equality

`==` evaluates to `true` when its operands are equal. `!=` evaluates to the logical
negation of `==`. Equality is **total**: it returns a boolean for any two operands
and never raises.

**Numeric-coercion rule.** The three numeric categories — `int`, `float`, `byte` —
form a single equivalence class for `==` and `!=`. Operands from any two of these
categories are compared by **mathematical value**: `1 == 1.0` is `true`, `0 == 0.0`
is `true`, `-0.0 == 0` is `true`, `byte 0 == 0` is `true`, `byte 7 == 7.0` is
`true`. This matches the byte→int and int↔float promotion in arithmetic and
relational operators (see *Type Coercion*). NaN follows IEEE 754: `NaN != NaN` even
with itself.

**Non-numeric cross-category rule.** Outside the numeric equivalence class, two
values of different runtime categories (per `typeof`) are never equal: `1 == true`
is `false`, `0 == null` is `false`, `0 == ""` is `false`, `null == false` is
`false`, `[] == null` is `false`. No other cross-category coercion is performed.

**Per-category rule (within a category, or within the numeric equivalence class).**

| Category | `==` semantics |
| -------- | -------------- |
| `null`, `bool`, `string` | by value |
| `int`, `float`, `byte` | by **mathematical value across the three categories** (IEEE 754 for `float`: `0.0 == -0.0`, `NaN != NaN`) |
| `duration`, `bytes`, `ip`, `semver` | by value (semver per semver precedence) |
| `secret`, `array`, `dict`, `struct`, `enum`, `function`, `namespace`, `future`, `range`, `Error` | by reference identity (same value handle ↔ equal) |

The reference-identity rule for `function` and `namespace` implies `io.println ==
io.println` is `true` (the same registered function handle), and `io == io` is
`true` (the same namespace singleton). Two arrays with the same elements but
distinct constructions are **not** equal (`[1] == [1]` is `false`); use
`arr.equals` / `dict.equals` for structural comparison. Two distinct
`secret("x")` constructions are **not** equal (`secret("x") == secret("x")` is
`false`) — to compare contents, `reveal()` first; for security-sensitive
comparison use `buf.equals(buf.from(reveal(a)), buf.from(reveal(b)))` (a
constant-time comparator backed by `CryptographicOperations.FixedTimeEquals`;
see *Secret Values*).

**Strict assert equality (Edit E2, DE3).** The `assert.equal` and `assert.notEqual` built-ins use a type-strict equality: operands of different `typeof` are never equal even within the numeric equivalence class. `assert.equal(1, 1.0)` raises `AssertionError` even though `1 == 1.0` is `true`. Within a single type, value equality applies with the following floating-point specifics: `assert.equal(0.0, -0.0)` passes (±0 unified), and `assert.equal(NaN, NaN)` passes (NaN is reflexive under strict assert equality). Aggregates and secrets compare by reference identity: `assert.equal(secret("x"), secret("x"))` raises (two distinct constructions); `let t = secret("x"); assert.equal(t, t)` passes (aliased handle). Test authors who want operator-== semantics in an assertion write `assert.true(a == b);` instead.

**Array membership uses SameValueZero — a value-equality comparator aligned with the
numeric-coercion rule, with one floating-point delta.** The `in` operator (when the
right-hand side is an array), `arr.contains`, `arr.includes`, `arr.indexOf`,
`arr.lastIndexOf`, `arr.remove`, and `arr.unique` (de-duplication compares elements
pairwise under SameValueZero) all use **SameValueZero equality**: int/float/byte operands
compare by mathematical value (the numeric-coercion rule above), the floats `+0.0` and
`-0.0` are the same value, and `NaN` is **self-equal by value**. Two `NaN`s in the same
array are considered equal even though `NaN != NaN` under `==`.

Concretely:

- `1 in [1.0]` is `true`. `arr.contains([1], 1.0)` is `true`.
  `arr.contains([0], -0.0)` is `true`.
- `NaN in [NaN]` is `true` (`NaN`-self-equal by value, not by identity).

This is the **collection equivalent of `==`** with the single delta that NaN is treated
as self-equal by value (a key collection users universally expect — a value put in is a
value found back). The `==` operator is unaffected: `NaN == NaN` remains `false` and
`+0.0 == -0.0` remains `true` per IEEE 754. The non-numeric branch of SameValueZero
delegates to the per-category equality rule above: two distinct `secret("x")` constructions
are **not** the same array element (reference identity per §Secret Values); two arrays with
the same elements but distinct constructions are **not** the same element (reference
identity).

**Dictionary keys use SameValueZero — the same comparator as array membership (Edit E1,
keying half).** Integer key `1` and float key `1.0` are the same dictionary key:
`let d = {}; d[1] = "a"` returns `"a"` for `d[1.0]`. The floats `+0.0` and `-0.0`
are the same key. A `NaN` key round-trips: a value stored under a `NaN` key is
retrieved by the same `NaN` expression.

For non-numeric types, the per-category equality rule governs keying:

- Strings key by value (two `"foo"` expressions are the same key).
- `duration`, `bytes`, `ip`, and `semver` key by value (per the per-category equality
  rule above): `let d = {}; d[1KB] = 1; d[1KB]` returns `1`. Two `5s` duration
  expressions, two `@10.0.0.1` ip expressions, and two `semver("1.2.3")` expressions
  each produce the same key.
- Secrets, arrays, dicts, struct instances, functions, namespaces, futures, ranges,
  and Error values key by reference identity — two distinct `secret("x")` constructions
  are distinct keys; two `[1, 2]` arrays with distinct identities are distinct keys.
  A handle that is the same object is always the same key:
  `let t = secret("x"); d[t] = 1; d[t]` returns `1`.

**Three named modes — one source of truth.** Every equality decision in Stash
resolves through exactly three named modes, all defined in one place
(`StashEquality`): **`OperatorEquals`** backs `==` and `!=` (the numeric-coercion
rule above, with `NaN != NaN` per IEEE 754); **`SameValueZero`** backs collection
membership and dictionary keying (the same numeric coercion, but with `NaN`
self-equal by value); and **`StrictEquals`** backs `assert.equal` and
`assert.notEqual` (type-strict — no cross-category numeric coercion). These three
are the complete vocabulary of value equality: there is no fourth mode, and no code
path compares two runtime values by any other rule. (The constant-pool interning key
the compiler uses to deduplicate literals is *structural identity*, not a value-equality
mode — it is intentionally at least as fine as `==` and is not part of this set.) A
future addition to this set is a breaking change to §Equality and requires a new
normative clause here.

**Floating-point edges.** `0.0 == -0.0` is `true`. `NaN != NaN` (and is the only
value not equal to itself). `NaN` is reachable from a Stash script only via
overflow arithmetic (`conv.toFloat("1e308") * 10.0` produces `Infinity`;
`Infinity - Infinity` produces `NaN`); literal `0.0 / 0.0` raises
`RuntimeError("Division by zero.")`.

**The `==` operator's compile-time evaluation agrees with its runtime evaluation.**
The compiler folds a literal equality only when the folded result equals the runtime
result. In particular, `1 == 1.0` is `true` at both compile time and at runtime —
the same boolean as `let i = 1; let f = 1.0; i == f`. A folder regression that
diverges from the runtime path is a defect.

### Type Coercion

Stash performs implicit conversion only in the following narrow, enumerated cases.
No other operator or operation performs implicit conversion between types:

1. **Numeric promotion in arithmetic, relational, AND equality operators.**
   When `+`, `-`, `*`, `/`, `%`, `**`, `<`, `<=`, `>`, `>=`, `==`, `!=` are
   applied to two numeric operands of different categories (any pair drawn from
   `int`, `float`, `byte`), the operands are promoted to a common category by
   mathematical value. For arithmetic and relational operators, the result category
   is the wider of the two (`int`/`byte` → `int`; anything paired with `float` →
   `float`). For equality (`==`/`!=`), the result is a `bool`; see *Equality* for
   the full rule.
2. **Byte promotion to int in arithmetic and relational operators.** A `byte`
   operand to `+`, `-`, `*`, `/`, `%`, `**`, `<`, `<=`, `>`, `>=` is promoted
   to `int` before the operation. The result category is `int` or `float`
   (per rule 1), never `byte`. Byte equality is covered by rule 1 (the three
   numeric categories form a single equivalence class for `==`/`!=`).
3. **String concatenation with `+`.** When one operand of `+` is a `string`,
   the other operand is stringified via the same rule as `conv.toStr` and the
   result is a `string`. A `secret` operand of `+` produces a `secret` result
   (see *Secret Values*).

Outside these three cases, every operand mismatch raises a `RuntimeError` with
the operand types in the message. Use the `conv` namespace for explicit
conversions.

### Secret Values

`secret(value)` wraps `value` for redaction. The wrapped value is stored
unmodified inside the secret. A `secret` value displays as the redacted form
`"******"` (six asterisks) in every stringification context — `io.println`,
string interpolation (`"...${secret}..."`), `conv.toStr`, and the `+`
string-concatenation operator (which propagates taint — see below). `reveal(s)`
returns the wrapped value and is the only way to escape redaction; there is no capability gate on reveal.

```stash
let token = secret("abc123");
io.println(token);       // ******
let raw = reveal(token); // "abc123"
```

`reveal` requires a secret argument. Passing a non-secret value to `reveal`
raises a `RuntimeError`.

**Idempotent wrap.** `secret(secret(x))` wraps `x` with no inner-secret
nesting; `reveal(secret(secret(x))) == reveal(secret(x))`. Secrets do not
nest; the inner secret's value is unwrapped before wrapping. The outer and
inner handles are distinct objects (per *Equality* below), so `outer == inner`
is `false`.

```stash
let inner = secret("x");
let outer = secret(inner); // unwraps: reveal(outer) == "x"
typeof(outer);             // "secret"
// outer == inner is false — distinct handles (reference identity)
```

**Taint-propagating `+`.** When `+` is applied with a `secret` operand on
either side, the result is a `secret` wrapping the concatenation. The non-
secret operand is stringified through `conv.toStr` before concatenation. This
applies whether the other operand is a `string`, a number, or any other value.

```stash
let s = secret("abc123");
typeof(s + "!")      // "secret"
typeof("prefix=" + s) // "secret"
reveal(s + "!")      // "abc123!"
reveal("prefix=" + s) // "prefix=abc123"
```

**Equality — reference identity.** `secret(a) == secret(b)` is `true` only
when both operands are the **same secret handle** (`let t = secret("x"); t
== t` is `true`); two distinct `secret("x")` constructions are **not** equal
(`secret("x") == secret("x")` is `false`). The wrapped value never participates
in equality. To compare contents, `reveal()` both secrets first and compare
the revealed values; for security-sensitive comparison use
`buf.equals(buf.from(reveal(a)), buf.from(reveal(b)))` (a constant-time
comparator backed by `CryptographicOperations.FixedTimeEquals` that resists
timing side-channels). This is safety-by-default: == cannot be used as an oracle for the wrapped value.

A `secret` value may be used as a dictionary key; it keys by reference
identity per the equality rule above (§Equality, Edit E1 keying half). Two
distinct `secret("x")` constructions are distinct keys: `d[secret("x")] = 1`
followed by `d[secret("x")]` returns `null`. A named binding is the same key
as itself: `let t = secret("x"); d[t] = 1; d[t]` returns `1`.

**`typeof` and truthiness.** `typeof(s) == "secret"` for any secret `s`.
`nameof(s)` returns `"secret"`. A `secret` is truthy regardless of inner
value — `secret(0)`, `secret(null)`, and `secret("")` are all truthy.

## Bindings and Scope

### Variable Declarations

`let` creates a mutable binding. If no initializer is present, the binding is
initialized to `null`.

```stash
let name = "deploy";
let pending;
pending = true;
```

Assignment to an undeclared name produces a runtime error.

### Constant Declarations

`const` creates an immutable binding and must have an initializer.

```stash
const MAX_RETRIES = 3;
```

Assigning to a `const` binding produces a runtime error.

### Type Hints

Bindings, parameters, fields, and return positions may include type annotations.

```stash
let port: int = 443;
fn open(path: string) -> bool {
    return fs.exists(path);
}
```

**Type annotations are advisory metadata.** They are surfaced by editor tooling
(hover, completion, diagnostics) and consumed by the static analyzer, but they
are **erased at compile time** and have **no effect on runtime behavior**. A
value that does not match its annotation will not raise a runtime error.

```stash
let n: int = "not a number";  // runs without error; analyzer warns (SA0301)
let arr: int[] = [1, "two"];  // runs without error; array contains mixed types
```

For an explicit runtime check, use `is`:

```stash
if value is int {
    // ...
}
```

For value conversion (e.g. narrowing an `int` to a `byte`) use the `conv`
namespace:

```stash
let b = conv.toByte(200);     // b is a byte
```

A type annotation accepts a type name, an array suffix (`T[]`), or a
namespace-qualified dotted path. The head of a dotted path resolves like any
identifier; trailing segments name members of that head. Dotted paths may also
carry the `[]` suffix (`diff.Edit[]`).

```stash
import "diff" as diff;

fn render(options: diff.DiffOptions) -> diff.DiffResult {
    return diff.lines("a", "b", options);
}
```

Every type-position production — `let`/`const`/`fn` parameters and return,
`for-in`, struct and interface fields, lambda parameters, `catch (T e)`,
`extend T`, struct literal `T { ... }`, and `is T` — parses through the same
grammar and accepts the same forms.

The two grammar positions whose right-hand side is an _expression_ — `is`
followed by a value (where `value` evaluates to a type at runtime) and a
struct literal whose `T` resolves through a local binding — continue to do
what they always did at runtime. Erasure removes implicit, compiler-emitted
checks; it does not change explicit user-written ones.

### Destructuring Declarations

Array destructuring binds elements by position.

```stash
let [host, port] = ["localhost", 8080];
```

Dictionary destructuring binds fields by key.

```stash
let { name, status } = server;
```

The rest operator captures remaining values.

```stash
let [first, ...rest] = values;
let { name, ...meta } = record;
```

A destructuring declaration that cannot match its source value produces a runtime
error.

### Lexical Scope

Stash uses lexical scope. Blocks create nested scopes. A name lookup begins in the
innermost scope and proceeds outward.

```stash
let x = 1;
{
    let x = 2;
    io.println(x); // 2
}
io.println(x);     // 1
```

Closures capture bindings from their lexical environment.

### `unset`

`unset` removes mutable bindings or dictionary entries.

```stash
unset temp;
unset config.debug, config.trace;
```

Unsetting a missing binding, immutable binding, or unsupported target produces a
runtime error.

### The `readonly` Modifier

`readonly` composes with `let` or `const` to mark the declared value as **deeply
and transitively frozen**. Every write to any part of the frozen object graph —
including nested dicts, arrays, and struct instances reachable from the value —
throws `ReadOnlyError` at runtime.

```stash
let D            = { a: 1 }  // reassignable name, mutable value
const D          = { a: 1 }  // fixed name,        mutable value (JS-style)
readonly let D   = { a: 1 }  // reassignable name, deeply-frozen value
readonly const D = { a: 1 }  // fixed name,        deeply-frozen value (fully locked)
```

#### Syntax and Soft-Keyword Treatment

`readonly` is a **soft (contextual) keyword**: it is only special immediately
before `let` or `const`. Everywhere else it remains a valid identifier.

```stash
readonly const Config = { host: "localhost", ports: [80, 443] };

let readonly = true;  // ok — identifier, not the modifier
```

This matches the existing soft declaration modifiers `async` and `export`.
`readonly` is unambiguous on a two-token lookahead because `let`/`const` are
hard keywords.

`readonly` may not appear in a `for`-init clause (a rebound loop variable
cannot be meaningfully frozen); it may appear in an `export` site as
`export readonly const` (single canonical order).

#### Deep / Transitive Semantics

Freezing is **deep and transitive**: it reaches every nested dict, array, struct,
and `StashError` reachable through the initializer value. Traversal is cycle-safe.
Function / closure values encountered during traversal are treated as opaque and
are not frozen.

```stash
readonly const Config = { host: "localhost", ports: [80, 443] };
Config.host = "x";       // throws ReadOnlyError
Config.ports.push(22);   // throws — deep: nested array is frozen too
```

For `readonly let`, every value assigned to the binding — including the
initializer and every subsequent rebind — is deep-frozen at assignment time.

```stash
readonly let Snapshot = { x: 1 };
Snapshot.x = 2;            // throws ReadOnlyError
Snapshot = { x: 3 };      // ok — binding is rebindable
Snapshot.x = 4;            // throws — new value is also frozen
```

#### Deep vs Shallow — Design Rationale

**Why deep?** `readonly` is *value immutability for safe sharing*, not the
*binding/annotation* immutability that C# `readonly`, JS `const`, TypeScript
`readonly`, Java `final`, and Kotlin `val` provide. Those are shallow because
they fix a *name or type*, not a *value*. Stash `readonly` must be deep because
its primary motivation is safe sharing across async child VMs: a shallowly-frozen
value with mutable nested collections is still unsafe to share across the async
boundary, which would defeat the purpose.

Languages whose freeze-like primitive is shallow (JS `Object.freeze`, Python
`frozenset`) avoid the threading hazard via architecture — JS is single-threaded
by design; Python has the GIL. Stash already declined both approaches by offering
true concurrency through `async fn`. So the spec frames C# `readonly` as
*"different category, not bolder"*: C# freezes a *slot*, Stash freezes a *graph*.

#### The Aliasing Footgun — Retroactive In-Place Freeze

The genuine novelty of Stash `readonly` is **retroactive in-place freeze**: the
modifier decorates an initializer that may reference pre-existing values, so a
deep-freeze can reach a nested value that some other (non-`readonly`) binding
still aliases.

```stash
// Aliasing footgun: freeze is applied in-place, reaching pre-existing aliases
let shared = { count: 0 };
readonly const snap = { data: shared };

// `shared` is now frozen — it was reached during snap's deep-freeze traversal
// even though `shared` itself carries no `readonly` keyword.
shared.count = 1;       // throws ReadOnlyError — loud, never silent data skew
```

Two properties keep this safe:
- **Loud failure:** the runtime throws `ReadOnlyError` at the offending write.
  There is never silent data skew.
- **Visible origin:** every deep-freeze originates at a syntactically visible
  `readonly` declaration, never from an arbitrary function call.

A *clone-on-freeze* alternative (deep-copy the initializer) was considered and
rejected: it would convert the loud throw into a *silent* divergence between the
frozen copy and the still-mutable original — precisely the bug class immutability
exists to prevent — and would introduce copy-semantics into an otherwise
reference-semantic language.

#### Direct Aliasing

Frozenness is **carried by the value, not the binding**. An alias to a frozen
value is equally blocked regardless of whether the alias's declaration uses
`readonly`:

```stash
readonly const D = { a: 1 };
let alias = D;
alias.a = 2;            // throws ReadOnlyError — same frozen value
```

#### Primitives

On primitives (`int`, `float`, `bool`, `string`, `duration`, `ip_address`,
`semver`, `byte_size`, enums, etc.) `readonly` is a harmless no-op — primitive
values are already immutable. The binding axis (`const` vs `let`) continues to
apply independently.

```stash
readonly let n = 42;    // ok — no freeze semantics needed
n = 99;                 // ok — binding is let, reassignment is allowed
```

#### Catching `ReadOnlyError`

Mutation of a frozen value throws `ReadOnlyError`, which can be caught like any
other error:

```stash
readonly const Config = { host: "localhost" };
try {
    Config.host = "x";
} catch (ReadOnlyError e) {
    io.println("cannot mutate: " + e.message);
}
```

## Expressions

### Evaluation Order

Expressions evaluate left to right unless a construct explicitly short-circuits.
Function arguments evaluate before the call. Assignment evaluates the right-hand
side before updating the target.

### Primary Expressions

Primary expressions include literals, identifiers, grouped expressions, arrays,
dictionaries, struct literals, command expressions, and lambdas.

```stash
(1 + 2);
[1, 2, 3];
{ host: "localhost", port: 5432 };
Server { host: "web", port: 443 };
```

### Arrays

Array literals create mutable ordered sequences.

```stash
let xs = [1, 2, 3];
let empty = [];
```

Indexing an array uses zero-based indexes. Indexing outside the valid range
produces a runtime error.

```stash
xs[0];      // 1
xs[1] = 9;
```

Spread syntax inserts the elements of an iterable into an array or call argument
list.

```stash
let all = [0, ...xs, 4];
fn callAll(args) {
    run(...args);
}
```

### Dictionaries

Dictionary literals create mutable key-value mappings.

```stash
let server = {
    host: "localhost",
    port: 8080,
};
```

Three key forms are supported in dict literals (see also Appendix A grammar):

- **Identifier key** — `name: value` — the identifier becomes the string key.
- **String-literal key** — `"key-string": value` — allows keys that are not valid
  identifiers (e.g. contain hyphens, spaces, or other special characters).
- **Computed key** — `[expression]: value` — the expression is evaluated at runtime
  and its result is used as the key.

```stash
let field = "dynamic";
let d = {
    plain:        1,
    "with-dash":  2,
    [field]:      3,
};

d.plain;           // 1
d["with-dash"];    // 2
d["dynamic"];      // 3
```

Dictionary keys may be written as identifiers for string keys or as bracket access
for computed keys.

```stash
server.host;
server["host"];
server.port = 443;
```

Dot access is equivalent to string-key lookup for valid identifier keys. Missing
keys evaluate to `null` for reads unless a stricter dictionary mode is documented by
the implementation.

### Member Access and Optional Chaining

`expr.name` accesses a field, method, namespace member, or dictionary key.
`expr?.name` evaluates to `null` when `expr` evaluates to `null`; otherwise it
performs the same access as `.`.

```stash
server?.config?.port ?? 443;
```

Optional chaining must not suppress errors other than a null receiver.

### Calls

A call evaluates the callee and arguments, then invokes the callee.

```stash
deploy("prod", retries: 3);
```

Calling a non-callable value produces a runtime error.

### Operators

The following table lists operators from highest to lowest precedence.

| Precedence      | Operators and forms                                                        |
| --------------- | -------------------------------------------------------------------------- |
| postfix         | call, index, member access, `expr++`, `expr--`, switch expression          |
| prefix          | `!`, `-`, `~`, `++expr`, `--expr`, `try`, `await`                          |
| multiplicative  | `*`, `/`, `%`                                                              |
| additive        | `+`, `-`                                                                   |
| range           | `..`, `start..end..step`                                                   |
| shift           | `<<`, `>>`                                                                 |
| comparison      | `<`, `>`, `<=`, `>=`, `in`, `is`                                           |
| equality        | `==`, `!=`                                                                 |
| bitwise AND     | `&`                                                                        |
| bitwise XOR     | `^`                                                                        |
| bitwise OR      | `\|` outside command-pipe context                                          |
| logical AND     | `&&`, `and`                                                                |
| logical OR      | `\|\| `, `or`                                                              |
| pipe            | `\|` in command-pipe context                                               |
| redirection     | `>`, `>>`, `2>`, `2>>`, `&>`, `&>>`                                        |
| null coalescing | `??`                                                                       |
| ternary         | `? :`                                                                      |
| assignment      | `=`, `+=`, `-=`, `*=`, `/=`, `%=`, `??=`, `&=`, `\| =`, `^=`, `<<=`, `>>=` |

Where `|` could be either a command pipe or bitwise OR, command expressions take
priority in command-pipe context.

### Arithmetic and Bitwise Operators

`+`, `-`, `*`, `/`, and `%` operate on numeric values. `+` may also concatenate
strings. Invalid operand types produce a runtime error.

Bitwise operators operate on integer values.

```stash
1 + 2;
"a" + "b";
flags & mask;
flags |= 0x10;
~bits;
```

### Logical Operators

`&&` and `and` are equivalent. `||` and `or` are equivalent.

Logical AND evaluates its right operand only if the left operand is truthy. Logical
OR evaluates its right operand only if the left operand is falsey.

### Null Coalescing

`a ?? b` evaluates to `a` if `a` is not `null`; otherwise it evaluates to `b`.
The right operand must not be evaluated when the left operand is not `null`.

`target ??= value` assigns `value` to `target` only when `target` currently
evaluates to `null`.

### Ternary Operator

The ternary operator evaluates one of two branches.

```stash
let mode = debug ? "debug" : "release";
```

Only the selected branch is evaluated.

### Range Expressions

`start..end` creates a range. `start..end..step` creates a stepped range.

```stash
1..5;
10..0..-1;
```

Ranges may be iterated with `for` and tested with `in`. A zero step produces a
runtime error.

### Membership

`value in collection` tests membership. Strings test substring membership. Arrays
test element membership. Dictionaries test key membership. Ranges test whether the
value falls within the range and aligns with the step.

```stash
"sh" in "stash";
"host" in config;
3 in 1..5;
```

Array element membership uses **SameValueZero equality**: `1 in [1.0]` is `true`;
`NaN in [NaN]` is `true`. See *Equality → Array membership uses SameValueZero*.
Dict key membership also uses **SameValueZero equality**: `1.0 in d` is `true` when
`d` has the integer key `1`. See *Equality → Dictionary keys use SameValueZero*.

Using `in` with an unsupported right operand produces a runtime error.

### Type Checks

`value is TypeName` evaluates to `true` when the value has the named type or
conforms to the named interface.

```stash
value is string;
server is Runnable;
null is null;
```

Unknown type names produce a runtime error.

### Assignment

Assignment targets must be assignable locations: mutable bindings, fields,
dictionary entries, or indexed elements.

```stash
count = count + 1;
server.port = 443;
items[0] = "first";
```

Assignment to any non-assignable expression is a parse error or runtime error.

Compound assignment evaluates the target once, applies the corresponding operator,
and stores the result.

### Increment and Decrement

`++` and `--` operate on numeric assignable targets. Prefix forms evaluate to the
value after the update. Postfix forms evaluate to the value before the update.

```stash
let i = 0;
i++;  // evaluates to 0, then i is 1
++i;  // i is 2, expression evaluates to 2
```

Using `++` or `--` on a non-numeric value produces a runtime error.

### Switch Expressions

A switch expression matches a subject against arms and evaluates the first matching
arm.

```stash
let label = code switch {
    200 => "ok",
    404 => "missing",
    _ => "error",
};
```

The discard pattern `_` matches any subject. If no arm matches and no discard arm
exists, evaluation produces a runtime error.

## Statements and Control Flow

### Blocks

A block is a sequence of declarations and statements enclosed in braces.

```stash
{
    let tmp = compute();
    io.println(tmp);
}
```

### Expression Statements

An expression followed by a semicolon is evaluated for its effects.

```stash
deploy();
count += 1;
```

### Conditional Statements

`if` evaluates its condition using truthiness. If the condition is truthy, the
then-branch executes. Otherwise, the `else` branch executes if present.

```stash
if (ok) {
    deploy();
} else if (dryRun) {
    preview();
} else {
    fail();
}
```

### Loops

`while` repeats while its condition is truthy.

```stash
while (queue.length > 0) {
    process(queue.pop());
}
```

`do while` executes the body once before testing its condition.

```stash
do {
    retryOnce();
} while (again);
```

C-style `for` loops contain initializer, condition, and increment clauses.

```stash
for (let i = 0; i < 10; i++) {
    io.println(i);
}
```

`for in` loops iterate over iterable values.

```stash
for (let item in items) {
    io.println(item);
}

for (let key, value in config) {
    io.println("${key}=${value}");
}
```

Using `for in` with a non-iterable value produces a runtime error.

### Break and Continue

`break;` exits the innermost loop or switch statement. `continue;` skips to the
next iteration of the innermost loop. Using either outside an allowed construct is a
parse error or static error.

### Return

`return expr;` exits the current function and yields `expr`. `return;` yields
`null`. Returning outside a function produces a runtime error unless the
implementation defines top-level returns.

### Switch Statements

A switch statement executes the first matching case body.

```stash
switch (status) {
    case "active": {
        start();
    }
    case "inactive", "paused": {
        stop();
    }
    default: {
        report(status);
    }
}
```

Cases are tested in source order. `default` matches any subject not matched by an
earlier case. Switch statement arms do not fall through.

### Lock Blocks

`lock` acquires file-based mutual exclusion for the duration of a block.

```stash
lock "/tmp/deploy.lock" {
    deploy();
}

lock "/tmp/deploy.lock" (wait: 30s, stale: 5m) {
    deploy();
}
```

Failure to acquire or release the lock produces a runtime error. The lock must be
released when control leaves the block, including via `return`, `throw`, `break`, or
`continue`.

### Elevate Blocks

`elevate` executes a block with elevated privileges.

```stash
elevate {
    fs.writeFile("/etc/app.conf", config);
}

elevate("sudo") {
    process.exec("systemctl restart app");
}
```

The elevation mechanism is implementation-defined and platform-specific. If
elevation is unavailable or denied, evaluation produces a runtime error.

## Functions, Closures, and Async

### Function Declarations

Functions are declared with `fn`.

```stash
fn add(a, b) {
    return a + b;
}
```

Parameters may have type hints and default values.

```stash
fn connect(host: string, port: int = 443) {
    return "${host}:${port}";
}
```

Default parameter expressions are evaluated when the function is called and the
argument is omitted.

### Rest Parameters and Spread Arguments

A rest parameter captures remaining positional arguments as an array.

```stash
fn sum(...values) {
    let total = 0;
    for (let value in values) {
        total += value;
    }
    return total;
}
```

Spread arguments expand an iterable into positional arguments.

```stash
sum(...[1, 2, 3]);
```

### Lambdas

Lambda expressions create anonymous functions.

```stash
let double = (x) => x * 2;
let logAll = (items) => {
    for (let item in items) {
        io.println(item);
    }
};
```

Expression-body lambdas return the expression value. Block-body lambdas return
`null` unless a `return` statement is executed.

### Closures

Functions and lambdas capture lexical bindings they reference.

```stash
fn makeCounter() {
    let count = 0;
    return () => {
        count++;
        return count;
    };
}
```

Captured mutable bindings remain mutable through the closure.

### Async Functions and Await

Stash has **two concurrency systems** that are deliberately separate and do not bridge:

| | **System A — Futures (parallel)** | **System B — Event queue (serial)** |
| --- | --- | --- |
| **Surface** | `async fn`, `await`, `task.*`, `arr.par*` | `fs.watch`, `signal.on`, `event.poll`, `event.loop` |
| **Where the body runs** | Real **thread-pool thread** (forked child VM) | The **main VM thread**, at park / drain points |
| **State sharing** | Deep-clone or freeze-share isolation at fork; call-local mutations | Shared — callback mutations reach the parent after the drain point |
| **Concurrency with main script** | Genuinely parallel | Zero — runs only when the main thread is parked |
| **How results reach the caller** | Only via `await` / `task.await` | Direct mutation of captured state, visible after the drain point |

The two systems are **non-interacting**: `event.poll()` does not advance a Future; `await`
does not drain the event queue. Both properties are part of the contract — the dimension
suite tests them explicitly as positive invariants, not as gaps.

#### The Futures system — `async fn` and `await`

`async fn` declares an asynchronous function. Calling an async function immediately returns
a `Future` — the body begins executing on a thread-pool thread.

```stash
async fn fetchAll(urls) {
    return await http.get(urls[0]);
}

let future = fetchAll(urls);
let result = await future;
```

`async` lambdas are also allowed.

```stash
// task.delay takes seconds as a number (not a duration literal).
let work = async () => await task.delay(1);
```

**D6 — `await` is blocking and uncolored.** `await expr` evaluates `expr`. If the result
is a `Future`, `await` blocks the *current thread* until the Future resolves and evaluates
to its result. If the result is not a `Future`, `await` evaluates to the value unchanged.
`await` works at the top level, inside non-async functions, and inside loops — it does not
require an `async` context. Only `async fn` (or `task.run`) *spawns* a new parallel task;
`await` merely *joins* one.

**D7 — Error-type fidelity.** When a Future fails, the error type is preserved through
`await`:

- A thrown `StashError` (e.g. `TypeError`, `ValueError`, `IOError`, `StateError`) survives
  `await` with its type and message intact. Awaiting a faulted Future throws that same error.
- A C# exception that escapes the task wraps to a generic `RuntimeError` with the message
  `"Future failed: <original message>"`.
- `throw "string"` inside a task wraps to `RuntimeError("string")`.
- Cancellation (`task.cancel`) throws `CancellationError` when the cancelled Future is awaited.

**D8 — Double-await is idempotent; `await` unwraps exactly one level.** Awaiting the same
Future a second time returns the cached result (the body runs once). `await` does not
recursively unwrap Future-of-Future — it unwraps exactly one level. If you need the inner
value from a Future-of-Future, either `await` twice or use `task.run(async fn)` which
already flattens one level.

**D8 (extended) — `await` on a settled Future is non-blocking and replays the outcome.**
Awaiting a Future whose status is:

- `task.Status.Completed` returns the cached result without blocking;
- `task.Status.Failed` rethrows the cached error with full type fidelity (per D7) — the
  second and subsequent awaits throw the *same* error type and message, not a wrapped copy;
- `task.Status.Cancelled` throws `CancellationError` without blocking; subsequent awaits
  also throw `CancellationError`.

The body never runs more than once. There is no distinction between "first await on a
settled Future" and "second await after the first completed" — both replay the cached
outcome.

**D9 — Future is a first-class value.** A Future behaves like any other value:

- `typeof future == "Future"` is true.
- `==` tests reference identity — two variables holding the same Future are `==`; two
  separately-created Futures are not.
- `conv.toStr(future)` (or implicit stringification) produces `"<Future:Running>"`,
  `"<Future:Completed>"`, `"<Future:Failed>"`, or `"<Future:Cancelled>"`.
- Futures can be stored in arrays, dicts, and struct fields; they can be returned from
  functions and passed as arguments.
- A Future handle is **shared**, not cloned, across the isolation boundary — a child task
  can return a Future created by the parent and the parent can await it. The handle is safe
  to share because it is effectively immutable (it wraps a thread-safe .NET `Task`).

**`task.resolve(value?)` — already-resolved Future.** Returns a Future that has already
resolved to `value`. The argument is **optional**; calling `task.resolve()` returns a Future
resolved to `null`. The returned Future:

- has status `task.Status.Completed` from the moment of creation;
- returns `value` (or `null` if omitted) when awaited; awaiting it never blocks;
- is **observation-tracked** like any other Future (a `task.resolve(…)` that is never awaited
  does not trigger the unobserved-fault report, because it has not faulted);
- is fail-safe — `task.resolve` itself never throws.

**`task.delay(seconds)` — timed Future.** Returns a Future that resolves to `null` after
`seconds` seconds (a `number`, e.g. `0.1` or `1`). The Future has status
`task.Status.Running` until the delay elapses, then transitions to `task.Status.Completed`.
Cancelling a delay Future with `task.cancel` transitions it to `task.Status.Cancelled` at the
next park point; awaiting a cancelled delay throws `CancellationError`. `task.delay(0)` is a
zero-second delay that still resolves on the thread pool — it does *not* synchronously
complete. `task.delay` is **not** an event-queue drain point: queued callbacks (`fs.watch`,
`signal.on`) are not drained while a Future from `task.delay` is being awaited; use
`time.sleep` instead for that purpose.

**Combinator pairing — fail-fast vs. collect-all.**

`task.all`, `task.race`, and `task.awaitAny` are **fail-fast** combinators (analogous to
`Promise.all`, `Promise.race`, `Promise.any`): if any constituent task faults, the
combinator throws immediately with the original error type; remaining tasks are cancelled.

`task.awaitAll` is the **collect-all** combinator (analogous to `Promise.allSettled`): it
waits for every Future in the array regardless of failure and returns an array of per-element
results. A faulted element becomes a `StashError` value with the **original error type
preserved** (e.g. a task that throws `TypeError` produces a `StashError` whose
`.type == "TypeError"`). A cancelled element becomes a `StashError` with
`.type == "CancellationError"`. `task.awaitAll` never throws.

**D10 — `arr.par*` order preservation, fail-fast, and `maxConcurrency`.** `arr.parMap`,
`arr.parFilter`, and `arr.parForEach` execute their callback in parallel across elements:

- Results are returned in **input order** (parMap preserves the index mapping; parFilter
  preserves relative order of elements that pass).
- If any callback throws, **the first error encountered is rethrown** (fail-fast); other
  in-flight callbacks are not waited for.
- All three accept an optional third argument `maxConcurrency` (default: unbounded, using
  all available cores). Passing `maxConcurrency = N` limits the thread-pool parallelism to
  `N` simultaneous callbacks; must be `>= 1`.
- If the callback is an `async` function, its returned Future is automatically awaited, so
  `arr.parMap([1,2,3], async (x) => x * 2)` returns `[2, 4, 6]` — not an array of Futures.

**D10 (extended) — `arr.parForEach` return value.** `arr.parForEach` is side-effect-only and
returns `null`. (`arr.parMap` returns an array of results; `arr.parFilter` returns an array of
elements that passed; `arr.parForEach` returns nothing observable beyond its callbacks' effects.)

**Process handle boundary (D5).** `process.spawn()` returns a handle that is bound to the task
context that created it. Using a parent's handle inside a child task (via `task.run` or
`async fn`) throws `StateError` with the message
`"'<funcName>': process handle does not cross task boundaries. Spawn the process inside the same task that uses it."`,
where `<funcName>` is the name of the `process.*` operation invoked (e.g. `process.wait`).
The boundary is enforced for all `process.*` operations (`process.wait`, `process.kill`,
`process.read`, `process.write`, etc.) that take a `Process` handle argument.
Communicate `Process` handles via return values, not closure capture.

**Socket handle task-affinity (D5 — enforcement pending).** D5's cross-task handle boundary is
**intended to apply to socket handles** (`TcpConnection`, `TcpServer`, `TcpClient`, and all
socket types) as well as to `Process` handles — the same rationale (silent misbehavior is the
worst outcome) applies equally to sockets. However, this boundary is **not yet enforced for
socket handles**: only `Process` handles are enforced today (via per-context tracking in the
runtime). Cross-task socket-handle use is therefore **unsupported and unsafe**: concurrent
same-direction access to an underlying `NetworkStream` or `TcpClient` silently corrupts data
without raising an exception, and the async socket path throws `IOError` rather than
`StateError` when a deep-cloned handle is used across a task boundary. This is a **known,
tracked gap** — an oversight from the original `async-correctness` ship, where D5 named sockets
in scope but only Process enforcement was built. Do not share socket handles across task
boundaries. The planned enforcement work (which will make cross-task socket use throw
`StateError` matching the Process behavior) is tracked in
`.kanban/0-backlog/bugs/tcp-socket-handle-task-boundary-enforcement.md`.

**Cancellation, timeout, and task status.** A running task can be cancelled with
`task.cancel(future)`. Cancellation is **cooperative**, not pre-emptive: the task observes its
cancellation token at park points (such as `time.sleep` and blocking I/O), so it stops at the
next park point rather than being interrupted mid-instruction. Once cancellation has propagated,
awaiting the future throws `CancellationError` and `task.status(future)` returns
`task.Status.Cancelled`.

`task.cancel(future)` returns `null`. It is **idempotent**: cancelling a Future that has already
settled (`task.Status.Completed`, `task.Status.Failed`, or `task.Status.Cancelled`) is a no-op
— the call returns `null` without raising. A second `task.cancel(future)` on the same Future is
also a no-op. Cancelling a non-`Future` value throws `TypeError`. All `task.*` builtins
that consume a `Future` argument (`task.await`, `task.status`, `task.cancel`) throw `TypeError`
when given a non-Future value.

`task.status(future)` reports a future's lifecycle state. It returns a value of the closed enum
`task.Status`, whose members are `task.Status.Running`, `task.Status.Completed`,
`task.Status.Failed`, and `task.Status.Cancelled`. There is no top-level `Status` binding —
always use the namespace-qualified form. Adding a new member to `task.Status` is a breaking
change to the §Async surface. Because cancellation is cooperative, a status read taken
immediately after `task.cancel` may still observe `task.Status.Running` until the task reaches
its next park point.

`task.timeout(ms, fn)` runs `fn` under a deadline and is **distinct from external cancellation**:
when the deadline elapses it throws `TimeoutError` (never `CancellationError`), while still
cancelling the underlying work so the timed-out computation does not keep running.

**Unawaited tasks — dropped, but faults are reported.** Futures are never implicitly awaited; a
task you spawn and never await is fire-and-forget. Only the results and errors you `await`
(directly, or via `task.await` / `task.awaitAll` / `task.awaitAny` / `task.all` / `task.race`)
reach your code.

- **Still running at exit → dropped.** When the main script returns, a task still executing on a
  thread-pool thread is abandoned: its remaining work neither delays process exit nor produces a
  result. To let it finish, `await` it, or hold the VM open with `event.loop()` or a `time.sleep`
  loop.

**Negative space — still-running at exit is dropped, not drained.** A Future whose status is
`task.Status.Running` when the main script returns is silently abandoned. The runtime does not
wait, does not drain pending work, and does not report. This is intentional negative space: the
unobserved-fault report (D1) scans *faulted-and-unobserved* tasks only. To let a task finish,
`await` it or hold the VM open with `event.loop()` or a `time.sleep` loop.

- **Faulted but never observed → reported.** If a task **faults** (throws) and its error is never
  observed by any `await` / `task.*` consumer, the runtime prints a single block to **stderr** at
  script exit:

  ```
  warning: <N> unobserved async error(s):
    <ErrorType>: <message>
    ...
  ```

  so a silently-swallowed background error becomes visible instead of vanishing. The process
  **exit code is unchanged** by this report. An explicitly **cancelled** task (via `task.cancel`)
  is *not* reported, and reading `task.status(future)` does **not** count as observing the error —
  you can inspect a future's state without suppressing the warning. This report is emitted by the
  CLI runtime; a host that embeds Stash through the hosting SDK does not receive it and surfaces
  task errors however it chooses.

#### Async-child global isolation

When an `async fn` body is invoked, the runtime forks a child VM on a thread-pool thread.
Each captured global that the child might access is handled at fork time based on whether
the value is frozen:

- **Frozen values** (`readonly` declarations or values passed through `freeze`) are shared
  by reference across the fork. Frozen graphs are immutable and cannot be racily mutated,
  so sharing is safe.
- **Non-frozen reference-typed values** (mutable arrays, dicts, instances) are **deep-cloned**
  into the child at fork time. Each task gets its own independent copy.

This means mutations to a captured, non-frozen value inside an `async fn` body are
**call-local** — they affect only the child's clone, not the parent's original.

#### Migration note — call-local mutation

Prior to the hermetic-VM isolation release, a non-frozen captured global mutated inside an
`async fn` body would (racily) write through the shared reference and affect the parent. This
was an unsafe data race with undefined behavior under concurrent mutation.

After this change, such mutation is call-local: the child's writes never reach the parent.

**Recommended pattern:** communicate results via return values and `await` / `task.all`.

```stash
async fn compute(input) {
    // safe: returns a new value rather than mutating a captured global
    return input * 2;
}

let results = await task.all([compute(1), compute(2), compute(3)]);
// results = [2, 4, 6]
```

If shared read-only access across async tasks is needed, `freeze` the value before spawning.
Frozen values share by reference; any attempt to write throws `ReadOnlyError`.

```stash
readonly let config = { host: "localhost", port: 8080 };

async fn handler(req) {
    // config is frozen — reads are safe, writes throw ReadOnlyError
    return config.host + ":" + conv.toStr(config.port);
}
```

If a non-frozen value to be captured contains a cycle, the deep-clone at fork time throws
`ValueError` with the cycle path in the message. Fix: `freeze` one node on the cycle (which
makes the entire reachable graph frozen and eligible for reference-sharing).

#### The event-queue system — background-thread callback marshaling

**D11 — Two-systems model.** The event-queue system is System B: callbacks registered with
`fs.watch`, `signal.on`, and similar event-source functions run on the **main VM thread**
at drain points, with full access to shared mutable state and zero concurrency against the
main script. This is distinct from System A (Futures), which runs truly in parallel on
thread-pool threads.

Background-thread callbacks — those registered with `fs.watch` and similar event-source
functions — run their bodies on the **VM thread**, not on the OS event thread that detected
the change. The delivery mechanism is a **per-VM callback queue**: when an event fires on a
background thread the producer enqueues `(callable, args)` onto the queue of the VM that
registered the watcher. The VM thread drains that queue **inline at yield points** — coarse
points where the main script is parked and there is no concurrent execution.

**Safety invariant.** Because delivery only happens when the main thread is parked, a queued
callback runs with **zero concurrency** against the main script. It therefore has full access
to shared globals and captured upvalues — mutations it makes are visible to the parent
immediately after the yield point returns. This is the JS/Lua event-loop bargain.

**Same-thread callbacks** (`arr.map`, sort comparators, `assert` body functions, and other
synchronous higher-order functions) are unaffected: they still run inline on the same VM
thread and always share caller state.

**Contrast with `async fn`.** `async fn` bodies are deliberately isolated: they run on a
real thread-pool thread, each gets a deep clone of captured state at fork time, and they
communicate with the caller only through their return value and `await`. The asymmetry is
not an inconsistency — parallel execution requires isolation; serial queued delivery does
not. Both designs are coherent; mixed designs (a callback on a background thread with free
access to shared mutable state) are not.

##### Drain points

The VM drains its callback queue at three **drain points** — places where the main script
explicitly parks itself:

| Drain point | Behavior |
| --- | --- |
| `time.sleep(secs)` | Waits until the duration elapses *or* the queue becomes non-empty, whichever comes first; drains all queued callbacks; recomputes remaining wait time; repeats until time is up. |
| `event.poll()` | Drains everything currently queued and returns immediately without blocking. |
| `event.loop()` | Blocks and drains indefinitely until the script's cancellation token fires, then throws `CancellationError`. |

A script that never reaches a drain point — for example a tight `while(true){}` with no
`time.sleep` — never drains. Insert a `time.sleep` or `event.poll()` to yield.

##### Run-to-completion (reentrancy)

The drain is **non-reentrant**: if a queued callback itself calls `time.sleep` or
`event.poll`, those calls do their primitive thing (sleep, or return immediately) but do
**not** trigger a nested drain. Each callback runs to completion before the next one fires.
This is identical to the JS task model.

Consequence: a slow callback delays the `time.sleep` that drained it from returning. If
`time.sleep(0.1)` drains a callback that takes 200 ms, the sleep returns at ≥ 300 ms total.
This is an inherent consequence of run-to-completion delivery, identical to JS, and is
documented rather than avoided.

##### Explicit-park lifetime

End-of-script (returning from the main body) **exits** the VM: active watchers are torn
down and any events still queued are **dropped**. There is no implicit "wait for the queue
to drain" hook.

To keep a script alive while events flow, hold the VM open with `event.loop()` or a
`while`-loop that calls `time.sleep`:

```stash
// Config hot-reload — keep reacting until cancelled (e.g. Ctrl-C).
let cfg = json.parse(fs.readFile("config.json"));
fs.watch("config.json", (e) => {
    cfg = json.parse(fs.readFile("config.json"));
    io.println("Config reloaded.");
});
event.loop();   // blocks; drains on each wakeup; exits on CancellationError
```

To flush before a clean exit, call `event.poll()` explicitly:

```stash
event.poll();   // drain any last pending callbacks
// now safe to return from main
```

##### File-watch example: closure mutation via the callback queue

The following pattern is the canonical demonstration of the marshaling model. Before this
feature, `running` would never flip for the parent because the callback ran in an isolated
child VM. Now the callback is queued and delivered at each `time.sleep`, making the mutation
visible.

```stash
let running = true;
let watcher = fs.watch(watchDir, (e) => {
    running = false;   // mutation reaches the parent at the next drain point
});

while (running) {
    time.sleep(0.1);   // drain point — queued callbacks run here
}
fs.unwatch(watcher);
```

##### Documented races

Two races are inherent to the model and are documented rather than eliminated:

- **Sleep-skew.** A slow callback can make `time.sleep(d)` return later than `d`. This is
  the same tradeoff as JS and is unavoidable with run-to-completion drain.
- **Already-queued after unwatch/off.** Calling `fs.unwatch(w)` or `signal.off(SIG)` stops
  *future* enqueues, but a callback that was already in the queue when `unwatch`/`off` was
  called still fires at the next drain point.

##### Signal callbacks — delivery mechanism vs. OS registration

The marshaling mechanism above applies to any callback delivery path, including
`signal.on`. The handler's body is enqueued and delivered on the VM thread at the next
drain point, so the closure-mutation pattern works:

```stash
// The delivery mechanism is correct for this shape — the callback is marshaled
// onto the VM thread and `stop` flips for the parent loop at the next time.sleep().
let stop = false;
signal.on(Signal.Term, () => { stop = true; });
while (!stop) { time.sleep(0.1); }
```

Note: as of this release, `Signal.Term` and related `Signal.*` members are not wired to
real OS-level POSIX signal registration due to a name-domain mismatch in the underlying
implementation. Synthetic dispatch (used internally by tests) works correctly. For
intercepting a real OS `SIGTERM` today, use `sys.onSignal(sys.Signal.SIGTERM, cb)`. The
name-domain mismatch is tracked in the backlog and will be fixed in a future release.

#### The `event` Namespace

The `event` namespace provides the two explicit drain points: `event.poll()` and
`event.loop()`. Neither function requires any capability — they operate purely on the VM's
internal callback queue.

**`event.poll()`** drains everything currently queued and returns immediately. It is
infallible. When called from inside a queued callback (i.e. while a drain is already in
progress), it is a no-op — the reentrancy guard prevents nested drains.

**`event.loop()`** blocks and drains indefinitely. It parks the calling thread and wakes on
each queue signal to drain, then parks again. It exits only when the script's cancellation
token fires, at which point it throws `CancellationError`. When called from inside a queued
callback, it is a no-op.

```stash
// event.poll() — drain and return immediately
let polled = false;
let w = fs.watch(dir, (e) => { polled = true; });
fs.writeFile(path.join(dir, "f.txt"), "x");
// spin until the watch debounce fires
while (!polled) { event.poll(); }
io.println("polled: " + conv.toStr(polled));   // "polled: true"
fs.unwatch(w);

// event.loop() — run until cancelled
fs.watch(cfgPath, (e) => { io.println("Config changed."); });
io.println("Watching — Ctrl-C to stop.");
event.loop();   // throws CancellationError on Ctrl-C; script exits cleanly
```

### Methods

Struct declarations and extension blocks may define methods. A method call binds
the receiver as `self`.

```stash
struct Server {
    host,

    fn url() {
        return "https://${self.host}";
    }
}
```

The receiver expression is evaluated before arguments.

### Uniform Function Call Syntax

Uniform function call syntax permits namespace functions to be called as if they
were methods on the first argument.

```stash
str.trim(name);
name.trim();
```

The method form must resolve to the same callable and argument list as the
corresponding namespace call. If both an inherent method and a UFCS candidate exist,
the method resolution order in [Method Resolution](#method-resolution) applies.

## Aggregate Types and Members

### Structs

A struct declaration introduces a user-defined aggregate type.

```stash
struct Server {
    host: string,
    port: int,

    fn endpoint() {
        return "${self.host}:${self.port}";
    }
}
```

Struct literals instantiate structs.

```stash
let server = Server {
    host: "localhost",
    port: 8080,
};
```

Missing required fields or unknown fields produce a runtime error. Field access uses
dot syntax.

### Enums

An enum declaration introduces named members.

```stash
enum Status {
    Unknown,
    Active,
    Inactive,
}

let s = Status.Active;
```

Enum members compare by enum type and member identity.

### Interfaces

An interface declares a structural contract.

```stash
interface Runnable {
    fn run() -> int,
    name: string,
}
```

A struct may declare interface conformance after its name.

```stash
struct Job: Runnable {
    name,

    fn run() {
        return 0;
    }
}
```

Conformance requires every interface field and method to be present with compatible
signatures. `value is InterfaceName` evaluates to `true` for conforming values.

### Extension Blocks

An extension block adds methods to an existing type.

```stash
extend string {
    fn quoted() {
        return "\"${self}\"";
    }
}
```

Extensions may target built-in types, structs, and dictionaries where supported.
Extension methods participate in normal method lookup.

### Method Resolution

When resolving `receiver.name(...)`, the implementation must search in this order:

1. Inherent methods declared on the receiver's type.
2. Extension methods in scope.
3. UFCS candidates from imported or built-in namespaces.
4. Dictionary key lookup when the receiver is a dictionary and `name` is a key.

Ambiguous extension or UFCS matches produce a runtime error or static diagnostic.

## Function References

Every namespace entry has a static declaration kind: `Function`, `DataMember`, or
`Constant`. When the bare member-access form `ns.name` is used and `name` is a
`Function`-kind entry, the result is the **function reference** — the callable
value stored at that slot.  The function reference can be captured in a variable,
passed as an argument, and invoked like any other callable.

```stash
let printer = io.println;
printer("hello");           // "hello" — captured function, called later

let upper = str.upper;
io.println(upper("abc"));  // "ABC"

let abs = math.abs;
io.println(abs(-5));        // 5
```

The type of a function reference is `"function"`, which `typeof` confirms:

```stash
io.println(typeof(io.println));   // "function"
```

This works because `ns.name` always means "load the value declared at the slot."
For a `Function` slot, that value is callable. For a `DataMember` slot, the value
is the result of invoking the registered getter (not callable). For a `Constant`
slot, the value is the stored snapshot.

**Key asymmetry to internalize:**

- `Function`-kind entries yield a **callable** when accessed bare — a first-class
  function reference that can be stored, passed, and invoked.
- `DataMember`-kind entries yield the **underlying value** computed by the
  registered getter — they are **not callable**. Calling a `DataMember` with
  parentheses (`ns.member(...)`) is a compile-time error (SA0846).
- `Constant`-kind entries yield the stored snapshot. Constants may be callable if
  the stored value is itself callable, but the common case is a plain value (e.g.
  `math.PI`).

Assigning a captured function reference to a variable, returning it from a
function, or passing it as an argument all behave the same as any other first-class
value in Stash. Function references are ordinary values.

## Namespace Members

A **namespace member** is a read-only, context-bound value exposed by a stdlib
namespace through a bare property access. Unlike a function entry (which stores a
callable), a member entry stores a getter that is invoked when the member is
accessed. The result — an integer, string, array, or other value — is returned
directly to the caller without the need for parentheses.

```stash
io.println(cli.argc);     // number of script arguments (e.g. 3)
io.println(cli.argv);     // array of script arguments (e.g. ["arg1", "arg2", "arg3"])
io.println(env.cwd);      // current working directory (e.g. "/home/user/project")
io.println(os.name());    // operating system name (e.g. "linux")
```

### Declaration Kinds

Every entry in a namespace carries one of three static declaration kinds:

| Kind         | `ns.x` yields                         | `ns.x(...)` allowed?         |
| ------------ | -------------------------------------- | ---------------------------- |
| `Function`   | the function reference (callable)      | yes — normal call            |
| `DataMember` | result of invoking the getter          | **no — SA0846 at compile time** |
| `Constant`   | the stored snapshot                    | only if the value is callable |

Kinds are fixed at registration time and cannot change. Calling a `DataMember`
with parentheses is always a compile-time error (SA0846 — "X is a value member,
not a function. Drop the parentheses."). There is no runtime fallback that
detects-and-invokes.

### Stability: Cached and Live Members

Each `DataMember` carries a **stability annotation** that controls how often the
getter is invoked:

- **`Cached` (default)**: the getter is invoked on first access; the result is
  stored. Subsequent accesses return the same reference. Identity is preserved
  across the process lifetime. Examples: `cli.argc`, `cli.argv`, `env.home`.

- **`Live`**: the getter is invoked on every access. Returned values may differ
  between accesses if the underlying host state changed. Example: `env.cwd`
  (changes after `env.chdir`).

This stability split is modeled directly on **JavaScript ES module bindings**:
`Cached` members behave like `const` exports from a JS ES module (identity-stable
for the binding's lifetime), while `Live` members behave like `let` exports that
the source module reassigns (re-evaluated on each access, no identity guarantee).

```stash
// Live member — env.cwd re-reads the host on each access
io.println(env.cwd);           // "/home/user/project"
env.chdir("/tmp");
io.println(env.cwd);           // "/tmp" — reflects the chdir
```

### Cross-Language Framing

Namespace members are modeled on two established precedents:

**JS ES module live bindings** — The `Cached`/`Live` stability split mirrors the
JS ES module design directly. A `Cached` member behaves like a `const` export from
a JS ES module: the binding is set once and its identity is stable for the module's
lifetime. A `Live` member behaves like a `let` export that the source module
reassigns: each read goes back to the source for the current value. Like an ES
module binding, bare access returns a value and assignment is a static error
(SA0845 — "X is read-only"), mirroring a `TypeError: Assignment to constant
variable.` on module bindings.

**C# properties** — Like a C# property, what looks like a field access actually
invokes a getter under the hood. LSP hover surfaces the declaration kind so the
distinction is not hidden from tool users. Python (`sys.argv = []` silently
succeeds) and Go (relies on naming convention) are explicitly **not** the model.

### Read-Only Contract

Namespace members are read-only. Assignment is rejected statically:

```stash
cli.argv = [];   // SA0845 — 'argv' is read-only (compile-time error)
```

For dynamic receivers (where the namespace is accessed through a variable), the
runtime raises `ReadOnlyError`:

```stash
let ns = cli;
try {
    ns.argc = 0;
} catch (ReadOnlyError e) {
    io.println("caught: " + e.message);
    // "caught: Cannot assign to 'cli.argc': namespace members are read-only."
}
```

### Side-Effect Contract

Namespace member access is not a pure O(1) field read. The precise contract is:

1. **`Cached` members** return the same reference across all accesses for the
   process lifetime — the getter runs at most once and the result is memoized.
   Identity is stable: `cli.argv === cli.argv` holds.

2. **`Live` members** invoke the getter on every access. Returned values may be
   distinct between accesses if the underlying host state changed. Identity is
   **not** guaranteed: two successive reads of `env.cwd` after an `env.chdir` may
   return different strings.

3. **Reference-typed returns are frozen at the boundary** regardless of stability
   mode. Arrays and dicts returned by a member getter are frozen before being
   handed to the caller, identical to the frozen-collection semantics used
   elsewhere in the stdlib. As a result, `cli.argv[0] = "x"` raises a
   frozen-write error rather than silently mutating the backing array.

4. **Getters may throw.** Each member's documentation lists its `Throws` contract.
   `env.cwd` may throw `IOError` if the working directory cannot be determined;
   members with no listed throws are guaranteed not to throw under normal
   conditions.

### v1 Member Set

The following six members were migrated from zero-argument functions to namespace
members in the same release that introduced this feature. Their old call form
(`ns.x()`) is a compile-time error (SA0846); rewrite all call sites to bare
access (`ns.x`). (The original set also included `env.os` and `env.arch`, which
were subsequently removed — platform and architecture queries now live in the
`os` namespace as `os.name()` and `os.arch()`.)

| Member          | Stability | Throws     | Notes                                             |
| --------------- | --------- | ---------- | ------------------------------------------------- |
| `cli.argc`      | Cached    | —          | Count of script arguments. Set at process start.  |
| `cli.argv`      | Cached    | —          | Array of script arguments. Frozen; mutation errors. |
| `env.cwd`       | Live      | `IOError`  | Current working directory. Re-read on each access. |
| `env.home`      | Cached    | —          | User home directory. Process-lifetime constant.   |
| `env.user`      | Cached    | —          | Current username. Requires `Environment` capability. |
| `env.hostname`  | Cached    | —          | Host name. Requires `Environment` capability.     |

### OS Namespace: Platform Version Semantics

The `os` namespace provides `os.isMacOSVersionAtLeast`, `os.isWindowsVersionAtLeast`, and
`os.isLinuxVersionAtLeast` as cross-platform guards. All three return `false` on the wrong host
without throwing. However, their underlying version sources differ:

- `os.isMacOSVersionAtLeast` and `os.isWindowsVersionAtLeast` delegate to
  `OperatingSystem.IsMacOSVersionAtLeast` / `OperatingSystem.IsWindowsVersionAtLeast`.
- `os.isLinuxVersionAtLeast(major, minor?)` compares against the **kernel version** reported
  by `Environment.OSVersion.Version` (the numeric components of `uname -r`), not a distribution
  release number. This is the only Linux version information available from .NET, because
  `OperatingSystem` exposes no `IsLinuxVersionAtLeast` equivalent.

Scripts that need to test "are we on Ubuntu 24.04?" cannot use this helper; it reports the
kernel version only.

## Shell Integration

### Command Expressions

Command expressions execute external commands.

```stash
let output = $(git status --short);
```

A normal command expression captures stdout as a string and does not throw solely
because the command exits non-zero.

### Strict Commands

Strict command expressions produce a runtime error when the command exits non-zero.

```stash
$!(npm test);
```

The runtime error must expose command, exit code, stdout, and stderr when available.

### Passthrough Commands

Passthrough command expressions stream process output directly to the current
standard streams.

```stash
$>(npm test);
$!>(npm test);
```

The strict passthrough form produces a runtime error on non-zero exit.

### Streaming Commands

Streaming command expressions return a stream-like handle or iterable process value.

```stash
for (let line in $<(tail -f app.log)) {
    io.println(line);
}
```

The strict streaming form produces a runtime error when the command fails.

```stash
$!<(grep ERROR app.log);
```

### Shell Interpolation

Command expressions may contain interpolation slots.

```stash
let branch = "main";
$(git checkout ${branch});
$(printf '%s\n' ${...items});
```

Interpolated values must be passed as safe command arguments, not concatenated into
an unsafe shell string, unless the command mode explicitly documents raw shell
evaluation.

### Pipes

The pipe operator connects command output to command input.

```stash
$(cat access.log) | $(grep ERROR) | $(wc -l);
```

The following syntax is also valid and de-sugars into the separate command pipe chain at runtime.

```stash
$(cat access.log | grep ERROR | wc -l);
```

When all operands are command expressions, `|` is a command pipe. In non-command
integer expression context, `|` is bitwise OR.

### Output Redirection

Command output may be redirected to files.

```stash
$(generate) > "out.txt";
$(generate) >> "out.txt";
$(generate) 2> "err.txt";
$(generate) 2>> "err.txt";
$(generate) &> "all.txt";
$(generate) &>> "all.txt";
```

Redirection targets must evaluate to paths. Redirection failures produce runtime
errors.

### Command-Line Execution Modes

An implementation may support inline execution and standard-input execution.

```text
stash -c 'io.println("hello");'
echo 'io.println("hello");' | stash
```

When supported, these modes must parse and evaluate their input as normal Stash
programs.

### Script Argument Parsing

Script arguments are accessed through the `cli` namespace. The `args` namespace
(`args.list`, `args.count`, `args.parse`, `args.build`) was removed in the same
release that introduced `cli`; it is not available in conforming implementations.

Use `cli.argv` to retrieve the raw argument array and `cli.argc` to get the
count. For typed, validated, and documented argument parsing use `cli.schema`,
`cli.parse`, and `cli.tryParse`:

```stash
schema = cli.schema({
    input:   cli.positional("string", { required: true }),
    output:  cli.option("string", { short: "o", default: "./out" }),
    verbose: cli.flag({ short: "v" }),
})

args = cli.parse(schema)
io.println(args.input)
```

The full `cli` namespace API is specified in the
[Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md).

## Errors and Cleanup

### Error Values

Stash errors are values. The standard built-in error types are specified in the
[Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md).

An error value must expose a message. Caught errors also expose `.type` and `.stack`
where supported.

### Throw

`throw expr;` evaluates `expr` and raises it as an error.

```stash
throw ValueError { message: "invalid port" };
```

`throw;` inside a catch clause rethrows the current error. A bare `throw;` outside a
catch clause produces a runtime error.

### Try Expressions

`try expr` evaluates `expr`. If evaluation succeeds, the expression evaluates to the
result. If evaluation produces an error, the expression evaluates to the error value
instead of throwing it.

```stash
let result = try fs.readFile("missing.txt");
if (result is Error) {
    io.eprintln(result.message);
}
```

### Try/Catch/Finally Statements

`try` statements handle thrown errors.

```stash
try {
    deploy();
} catch (CommandError e) {
    io.eprintln(e.stderr);
} catch (e) {
    io.eprintln(e.message);
} finally {
    cleanup();
}
```

Catch clauses are tested in source order. A typed catch matches errors of that type.
An untyped catch matches any error. The `finally` block executes whenever control
leaves the `try` statement.

### Retry Expressions

`retry` re-executes a block until it succeeds, reaches the maximum attempt count, or
an `until` predicate stops it.

```stash
let result = retry (3, backoff: Backoff.Exponential, delay: 1s)
    onRetry (attempt, error) {
        log.warn("retrying", { attempt: attempt, error: error.message });
    }
    until (value) => value.ok
{
    http.get(url);
};
```

The retry body evaluates to the successful result. If all attempts fail, evaluation
produces the last error.

### Timeout Expressions

`timeout duration { block }` bounds execution time.

```stash
let result = timeout 30s {
    deploy();
};
```

If the body completes before the duration, the expression evaluates to the body's
value. If time expires, evaluation produces a timeout error.

### Defer

`defer` registers cleanup code to run when the current function scope exits.

```stash
fn useFile(path) {
    let handle = fs.open(path);
    defer handle.close();
    return handle.readAll();
}
```

Deferred actions run in last-in, first-out order. Deferred actions run on normal
return and on error unwinding.

## Runtime Behavior

### Diagnostics

Parse errors, runtime errors, and static diagnostics must report source location
when location information is available. Diagnostic code formats and suppression
directives are specified by the analysis tooling, not by this language
specification.

### Formatting

The formatter must preserve program semantics. Formatter configuration and ignore
directives are specified by the formatter documentation and analysis tooling.

### Debugging

Debugger-visible behavior must correspond to the source-language control flow in
this document. DAP wire behavior is specified in the
[DAP document](DAP%20%E2%80%94%20Debug%20Adapter%20Protocol.md).

### Embedded Mode and Side Effects

Host applications may embed the Stash runtime. Embedded hosts may restrict process
execution, file access, privilege elevation, networking, or other side effects. A
restricted side effect must produce a runtime error or documented host diagnostic.

#### Per-VM environment and working-directory overlay

Each `VirtualMachine` instance maintains its own **per-VM view** of the process environment
and working directory, independent of other VM instances and the real host process state:

- **Working directory.** `env.cwd` reads, and `env.chdir` / `env.popDir` / `env.withDir`
  write, a per-VM `WorkingDirectory` field. The real `System.Environment.CurrentDirectory`
  is read once at VM construction to seed the initial value and is never modified thereafter.
  Two VMs in the same process can have different working directories; neither's `env.chdir`
  affects the other.

- **Environment variables.** `env.get` / `env.has` / `env.all` / `env.withPrefix` read from
  a per-VM overlay first, then fall back to the real process environment for variables not
  in the overlay. `env.set` / `env.unset` / `env.loadFile` write only to the overlay.
  `System.Environment` is never mutated by any of these calls. Variables explicitly unset via
  `env.unset` remain shadowed even if they exist in the real process environment.

- **Spawned processes** (`process.spawn`, `process.exec`, `process.pipeline`, etc.) inherit
  the VM's view: `ProcessStartInfo.WorkingDirectory` is seeded from the per-VM working
  directory, and the process environment is seeded from the merged overlay-over-real-env
  view. Per-call `cwd` / `env` options continue to override the per-VM defaults.

- **Process-identity reads** (`env.home`, `env.hostname`, `env.user`) are not overlayable.
  They reflect the host process identity, are stable for the process lifetime, and are never
  influenced by the per-VM overlay.

This isolation means that in an embedding scenario two `StashEngine` instances can safely
call `env.chdir` and `env.set` concurrently without affecting each other or the host process.

## Appendix A: Grammar

This grammar is normative for broad source shape and operator precedence. Where the
grammar and the prose disagree, the prose semantics control.

```ebnf
program             = shebang? declaration* EOF ;
shebang             = "#!" textUntilNewline ;

declaration         = asyncFunctionDecl
                    | functionDecl
                    | variableDecl
                    | constDecl
                    | structDecl
                    | enumDecl
                    | interfaceDecl
                    | extendDecl
                    | importDecl
                    | exportDecl
                    | exportBlock
                    | exportModuleAs
                    | exportFrom
                    | statement ;

variableDecl        = "let" (identifier typed? | destructurePattern) initializer? ";" ;
constDecl           = "const" (identifier typed? | destructurePattern) "=" expression ";" ;
initializer         = "=" expression ;
typed               = ":" typeHint ;
typeHint            = identifier ("[]")? | "[" typeHint "]" ;

destructurePattern  = arrayPattern | dictPattern ;
arrayPattern        = "[" patternItemList? "]" ;
dictPattern         = "{" patternItemList? "}" ;
patternItemList     = patternItem ("," patternItem)* ","? ;
patternItem         = identifier | "..." identifier ;

functionDecl        = "fn" identifier "(" parameterList? ")" returnHint? block ;
asyncFunctionDecl   = "async" functionDecl ;
parameterList       = parameter ("," parameter)* ","? ;
parameter           = restParameter | identifier typed? defaultValue? ;
restParameter       = "..." identifier typed? ;
defaultValue        = "=" expression ;
returnHint          = "->" typeHint ;

structDecl          = "struct" identifier interfaceList? "{"
                      structFieldList? functionDecl* asyncFunctionDecl* "}" ;
interfaceList       = ":" identifier ("," identifier)* ;
structFieldList     = structField ("," structField)* ","? ;
structField         = identifier typed? ;

enumDecl            = "enum" identifier "{" identifier ("," identifier)* ","? "}" ;

interfaceDecl       = "interface" identifier "{" interfaceMemberList? "}" ;
interfaceMemberList = interfaceMember ("," interfaceMember)* ","? ;
interfaceMember     = identifier typed?
                    | "fn"? identifier "(" parameterList? ")" returnHint? ;

extendDecl          = "extend" typeHint "{" functionDecl* asyncFunctionDecl* "}" ;

importDecl          = "import" "{" identifier ("," identifier)* ","? "}" "from" expression ";"
                    | "import" expression "as" identifier ";" ;

exportDecl          = "export" decoratedDecl ;

decoratedDecl       = functionDecl
                    | asyncFunctionDecl
                    | constDecl
                    | structDecl
                    | enumDecl
                    | interfaceDecl ;

exportBlock         = "export" "{" exportName ("," exportName)* ","? "}" ";" ;

exportModuleAs      = "export" expression "as" identifier ";" ;

exportFrom          = "export" "{" [ exportName ("," exportName)* ","? ] "}" "from" expression ";" ;

exportName          = identifier ;

statement           = block
                    | ifStmt
                    | whileStmt
                    | doWhileStmt
                    | forStmt
                    | switchStmt
                    | tryStmt
                    | returnStmt
                    | throwStmt
                    | breakStmt
                    | continueStmt
                    | deferStmt
                    | lockStmt
                    | elevateStmt
                    | unsetStmt
                    | expressionStmt ;

block               = "{" declaration* "}" ;
ifStmt              = "if" "(" expression ")" block ("else" (ifStmt | block))? ;
whileStmt           = "while" "(" expression ")" block ;
doWhileStmt         = "do" block "while" "(" expression ")" ";" ;
forStmt             = forInStmt | forCStyleStmt ;
forInStmt           = "for" "(" "let" identifier ("," identifier)? typed? "in" expression ")" block ;
forCStyleStmt       = "for" "(" (variableDecl | expressionStmt | ";")
                      expression? ";" expression? ")" block ;
switchStmt          = "switch" "(" expression ")" "{"
                      (caseArm | defaultArm)* "}" ;
caseArm             = "case" expression ("," expression)* ":" block ;
defaultArm          = "default" ":" block ;
tryStmt             = "try" block catchClause* finallyClause? ;
catchClause         = "catch" "(" catchTypeList? identifier ")" block ;
catchTypeList       = identifier ("|" identifier)* ;
finallyClause       = "finally" block ;
returnStmt          = "return" expression? ";" ;
throwStmt           = "throw" expression? ";" ;
breakStmt           = "break" ";" ;
continueStmt        = "continue" ";" ;
deferStmt           = "defer" (block | expressionStmt) ;
lockStmt            = "lock" expression lockOptions? block ;
lockOptions         = "(" namedArgumentList? ")" ;
elevateStmt         = "elevate" ("(" expression ")")? block ;
unsetStmt           = "unset" assignable ("," assignable)* ";" ;
expressionStmt      = expression ";" ;

expression          = assignment ;
assignment          = assignable assignmentOp assignment | ternary ;
assignmentOp        = "=" | "+=" | "-=" | "*=" | "/=" | "%=" | "??="
                    | "&=" | "|=" | "^=" | "<<=" | ">>=" ;
ternary             = nullCoalesce ("?" expression ":" expression)? ;
nullCoalesce        = redirect ("??" redirect)* ;
redirect            = pipe (redirectOp expression)* ;
redirectOp          = ">" | ">>" | "2>" | "2>>" | "&>" | "&>>" ;
pipe                = logicalOr ("|" logicalOr)* ;
logicalOr           = logicalAnd (("||" | "or") logicalAnd)* ;
logicalAnd          = bitwiseOr (("&&" | "and") bitwiseOr)* ;
bitwiseOr           = bitwiseXor ("|" bitwiseXor)* ;
bitwiseXor          = bitwiseAnd ("^" bitwiseAnd)* ;
bitwiseAnd          = equality ("&" equality)* ;
equality            = comparison (("==" | "!=") comparison)* ;
comparison          = shift ((compareOp | "in") shift)* ("is" typeHint)? ;
compareOp           = "<" | ">" | "<=" | ">=" ;
shift               = range (("<<" | ">>") range)* ;
range               = term (".." term (".." term)?)? ;
term                = factor (("+" | "-") factor)* ;
factor              = unary (("*" | "/" | "%") unary)* ;
unary               = ("!" | "-" | "~" | "try" | "await") unary
                    | ("++" | "--") assignable
                    | postfix ;
postfix             = primary postfixPart* ("++" | "--")? ;
postfixPart         = "(" argumentList? ")"
                    | "[" expression "]"
                    | ("." | "?.") identifier
                    | switchExprTail ;
switchExprTail      = "switch" "{" switchExprArm ("," switchExprArm)* ","? "}" ;
switchExprArm       = (expression | "_") "=>" expression ;

primary             = literal
                    | identifier
                    | arrayLiteral
                    | dictLiteral
                    | structLiteral
                    | commandLiteral
                    | lambda
                    | "(" expression ")" ;

arrayLiteral        = "[" argumentList? "]" ;
dictLiteral         = "{" dictEntryList? "}" ;
dictEntryList       = dictEntry ("," dictEntry)* ","? ;
dictEntry           = (identifier | stringLiteral | "[" expression "]") ":" expression ;
structLiteral       = identifier "{" dictEntryList? "}" ;
lambda              = "async"? "(" parameterList? ")" "=>" (block | expression) ;
argumentList        = argument ("," argument)* ","? ;
argument            = spreadArgument | namedArgument | expression ;
spreadArgument      = "..." expression ;
namedArgument       = identifier ":" expression ;

literal             = "null" | "true" | "false" | numberLiteral | stringLiteral
                    | interpolatedString | ipLiteral | durationLiteral
                    | byteSizeLiteral | semVerLiteral ;
commandLiteral      = "$(" commandText ")"
                    | "$!(" commandText ")"
                    | "$>(" commandText ")"
                    | "$!>(" commandText ")"
                    | "$<(" commandText ")"
                    | "$!<(" commandText ")" ;

assignable          = identifier
                    | postfix "." identifier
                    | postfix "[" expression "]" ;
```

## Appendix B: Reserved and Contextual Syntax

The following syntax is part of the language surface and must remain reserved for
the behavior specified in this document:

- `async fn`, `async (...) => ...`
- `await expr`
- `retry (...) ... { ... }`
- `timeout duration { ... }`
- `defer`
- `lock`
- `elevate`
- `extend`
- `switch` expressions and statements
- command literals beginning with `$(`, `$!(`, `$>(`, `$!>(`, `$<(`, and `$!<(`

Implementations must not assign incompatible meanings to reserved or contextual
syntax. Future revisions may refine the semantics of reserved forms while
preserving source compatibility where practical.
