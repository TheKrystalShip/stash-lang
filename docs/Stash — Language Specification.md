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
7. [Expressions](#expressions)
8. [Statements and Control Flow](#statements-and-control-flow)
9. [Functions, Closures, and Async](#functions-closures-and-async)
10. [Aggregate Types and Members](#aggregate-types-and-members)
11. [Shell Integration](#shell-integration)
12. [Errors and Cleanup](#errors-and-cleanup)
13. [Runtime Behavior](#runtime-behavior)
14. [Appendix A: Grammar](#appendix-a-grammar)
15. [Appendix B: Reserved and Contextual Syntax](#appendix-b-reserved-and-contextual-syntax)

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

### Exports

By default, every top-level binding in a Stash module is visible to importers.
When a module author wants to restrict the public surface, they annotate
declarations or list names explicitly with the `export` contextual keyword.

**Back-compatibility rule:** A module that contains zero `export` annotations
behaves as before — every top-level binding is accessible to importers. The
opt-in model means that all existing scripts and packages continue to work
without modification.

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

An empty block `export { }` is valid. `HasExplicitExports` becomes `true` and the
module exposes zero symbols — useful for scripts that are loaded for side effects
only.

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
| typed array | primitive homogeneous array such as `byte[]` |
| `dict`      | key-value mapping                            |
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
| `bytesize`  | byte quantity                                |
| `semver`    | semantic version                             |

### `typeof` and `nameof`

`typeof(value)` returns a string naming the runtime category of `value`.
`nameof(value)` returns the declared type or binding name where available. For user
types, `nameof` must preserve the user-visible struct, enum, or interface name.

```stash
typeof(1);        // "int"
typeof(null);     // "null"
nameof(Server);   // "Server"
```

The complete standard-library contract for these functions is in the
[Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md).

### Truthiness

The following values are falsey:

- `null`
- `false`
- numeric zero
- empty string
- empty array
- empty dictionary

All other values are truthy. Conditions in `if`, `while`, `do while`, ternary
expressions, logical operators, and retry predicates use these rules.

### Equality

`==` evaluates to `true` when its operands are equal. `!=` evaluates to the logical
negation of `==`.

Primitive values compare by value. Struct instances, arrays, dictionaries, errors,
futures, namespaces, and functions compare according to their runtime representation
unless a more specific rule is defined by the implementation. Semantic-version
values compare according to semantic-version rules.

Cross-type coercion must not be performed for equality unless explicitly specified
for a type.

### Type Coercion

Stash does not perform broad implicit conversion between unrelated types. Operators
may define narrow coercions, such as string concatenation with `+`. Library
functions in the `conv` namespace provide explicit conversions.

### Secret Values

`secret(value)` wraps a value for redaction. A secret value must display as redacted
when printed, stringified, interpolated, or concatenated. `reveal(secretValue)`
returns the wrapped value.

```stash
let token = secret("abc123");
io.println(token);       // redacted
let raw = reveal(token); // "abc123"
```

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

`async fn` declares an asynchronous function. Calling an async function returns a
`Future`.

```stash
async fn fetchAll(urls) {
    return await http.get(urls[0]);
}

let future = fetchAll(urls);
let result = await future;
```

`async` lambdas are also allowed.

```stash
let work = async () => await task.delay(1s);
```

`await expr` evaluates `expr`. If the result is a `Future`, `await` waits for it to
resolve and evaluates to its result. Awaiting a non-`Future` value evaluates to that
value.

If a future fails with an error, `await` produces that error.

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
