# Extend Blocks — Type Extension Methods

> **Status:** Approved Proposal
> **Created:** April 2026
> **Depends on:** [UFCS — Uniform Function Call Syntax](UFCS%20—%20Uniform%20Function%20Call%20Syntax.md) (blocking prerequisite)
> **Purpose:** Allow users and packages to add new methods to existing types — both built-in types (`string`, `array`, `dict`, `int`, `float`) and user-defined structs — using `extend` blocks.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Syntax](#2-syntax)
3. [Semantics](#3-semantics)
4. [Target Types](#4-target-types)
5. [The `self` Binding](#5-the-self-binding)
6. [Scoping & Imports](#6-scoping--imports)
7. [Method Resolution Order](#7-method-resolution-order)
8. [Constraints & Restrictions](#8-constraints--restrictions)
9. [Examples](#9-examples)
10. [Package Ecosystem](#10-package-ecosystem)
11. [Implementation Strategy](#11-implementation-strategy)
12. [LSP Integration](#12-lsp-integration)
13. [Future Considerations](#13-future-considerations)

---

## 1. Motivation

UFCS ([spec](UFCS%20—%20Uniform%20Function%20Call%20Syntax.md)) enables calling existing stdlib namespace functions as methods on values: `input.trim().upper()` instead of `str.upper(str.trim(input))`. However, UFCS is limited to functions that already exist in the stdlib namespaces. Users cannot add their own methods to types.

This creates a hard boundary: the set of methods available on a type is fixed at language release. Every useful string utility, array helper, or struct behavior must either live in the stdlib or exist as a free-standing function that breaks the chaining syntax.

`extend` blocks remove this boundary. They allow users and packages to add methods to any type, enabling:

- **Domain-specific methods on built-in types:** `email.isValidEmail()`, `path.toSlug()`, `users.groupBy((u) => u.role)`
- **Behavior additions to structs after their declaration:** Split struct definition and methods across files
- **Package-distributed type extensions:** `@stash/string-utils` adds `.toCamelCase()`, `.toSnakeCase()` to all strings
- **Chaining continuity:** User-defined methods chain naturally with stdlib methods and UFCS

### Design Influences

| Language   | Feature                                                      | What Stash borrows                                                           |
| ---------- | ------------------------------------------------------------ | ---------------------------------------------------------------------------- |
| **Swift**  | `extension String { ... }`                                   | Syntax shape, `self` binding, method-only extensions                         |
| **Kotlin** | `fun String.isPalindrome()`                                  | Per-function extensions importable via module system                         |
| **Rust**   | `impl Trait for Type { ... }`                                | Explicit type targeting, no monkey-patching of internals                     |
| **C#**     | `static class Extensions { static void Foo(this string s) }` | Activation via imports (Stash uses its existing `import` instead of `using`) |
| **Ruby**   | Open classes                                                 | What Stash explicitly **avoids** — global mutation, no scoping               |

---

## 2. Syntax

### Extending a built-in type

```stash
extend string {
    fn isPalindrome() {
        return self == self.reverse();
    }

    fn wordCount() {
        return self.split(" ").filter((w) => w != "").length();
    }
}
```

### Extending a user-defined struct

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
```

### Multiple extend blocks for the same type

```stash
// Perfectly valid — methods accumulate
extend string {
    fn isPalindrome() { ... }
}

extend string {
    fn isEmail() { ... }
}

// Both .isPalindrome() and .isEmail() are available
```

### Grammar

```
extendStmt → "extend" IDENTIFIER "{" fnDecl* "}"
```

The body contains only `fn` (and `async fn`) declarations — no fields, no constants, no nested types. The `IDENTIFIER` must resolve to a known type name: a built-in type keyword (`string`, `array`, `dict`, `int`, `float`) or a user-defined struct name in scope.

---

## 3. Semantics

### Core Behavior

An `extend` block registers new methods on a type. Once registered (and in scope — see [§6](#6-scoping--imports)), these methods are callable via dot syntax on any value of that type:

```stash
extend string {
    fn shout() {
        return self.upper() + "!!!";
    }
}

let greeting = "hello";
greeting.shout();          // "HELLO!!!"
"world".shout();           // "WORLD!!!"
```

### Extension methods are not fields

Extension methods exist in the method resolution chain only — they do not appear in struct field listings, `typeof()` output, or serialization. `extend` adds behavior, not data.

```stash
struct Point { x, y }

extend Point {
    fn distanceTo(other) {
        let dx = self.x - other.x;
        let dy = self.y - other.y;
        return math.sqrt(dx * dx + dy * dy);
    }
}

let p = Point { x: 3, y: 4 };
typeof(p);          // "struct" — unchanged
json.stringify(p);  // {"x":3,"y":4} — no method in output
p.distanceTo(Point { x: 0, y: 0 });  // 5.0
```

### Extension methods cannot add fields

```stash
extend string {
    fn foo() { ... }     // OK — method
    cachedLength,         // ERROR — cannot add fields via extend
}
```

### Extension methods can call other methods

Extension methods have full access to:

- `self` — the receiver value
- Other extension methods on the same type (if in scope)
- UFCS methods from namespace mappings
- Struct methods (for struct extensions)
- Any name in the closure scope (variables, functions, imports)

```stash
extend array {
    fn compact() {
        return self.filter((x) => x != null);  // UFCS: arr.filter
    }

    fn compactMap(fn) {
        return self.compact().map(fn);  // calls the extension above + UFCS
    }
}
```

---

## 4. Target Types

### Extendable built-in types

| Type keyword | Runtime type      | Example                                                |
| ------------ | ----------------- | ------------------------------------------------------ |
| `string`     | `string`          | `extend string { fn isPalindrome() { ... } }`          |
| `array`      | `List<object?>`   | `extend array { fn second() { return self[1]; } }`     |
| `dict`       | `StashDictionary` | `extend dict { fn invert() { ... } }`                  |
| `int`        | `long`            | `extend int { fn isEven() { return self % 2 == 0; } }` |
| `float`      | `double`          | `extend float { fn roundTo(places) { ... } }`          |

### Extendable user-defined types

Any `struct` that is in scope at the point of the `extend` declaration:

```stash
struct Config { host, port, debug }

extend Config {
    fn url() {
        let scheme = self.debug ? "http" : "https";
        return scheme + "://" + self.host + ":" + conv.toStr(self.port);
    }
}
```

The struct must be declared **before** the `extend` block. Forward references are not allowed:

```stash
extend Config { ... }           // ERROR — Config is not yet defined
struct Config { host, port }
```

### Non-extendable types

| Type        | Reason                                                                         |
| ----------- | ------------------------------------------------------------------------------ |
| `bool`      | Too few meaningful methods; extending `true.something()` is confusing          |
| `null`      | No methods on null — null must remain the "absence of value"                   |
| `function`  | Functions are callables, not data — extending them conflates concerns          |
| `range`     | Ranges are lazy iterators; methods should go in a future `range` namespace     |
| `enum`      | Enum members are values, not instances — extension methods have no `self`      |
| `namespace` | Namespaces are lookup containers, not values                                   |
| `Error`     | Error has fixed fields (`.message`, `.type`, `.stack`) — extend would conflict |

---

## 5. The `self` Binding

Inside an `extend` block's methods, `self` refers to the value the method is called on. The type of `self` depends on the target type.

### For built-in types

```stash
extend string {
    fn isPalindrome() {
        // self is a string value (e.g., "racecar")
        return self == self.reverse();
    }
}

extend array {
    fn second() {
        // self is a List<object?> (e.g., [1, 2, 3])
        return self[1];
    }
}

extend int {
    fn isEven() {
        // self is a long value (e.g., 42)
        return self % 2 == 0;
    }
}
```

### For user-defined structs

```stash
extend User {
    fn greet() {
        // self is a StashInstance with fields .name, .email, .age
        return "Hello, " + self.name;
    }
}
```

This is identical to how `self` works in struct method declarations today. The binding mechanism is the same: a `self` variable is injected into the method's closure scope before parameter binding.

### `self` is read-only for built-in types

For built-in types, `self` is the **value** — you cannot reassign `self`:

```stash
extend string {
    fn broken() {
        self = "modified";   // ERROR — cannot reassign self
    }
}
```

For struct types, `self` is a reference to the instance — you can modify `self.field` (mutate fields), but not reassign `self` itself:

```stash
extend User {
    fn birthday() {
        self.age++;          // OK — mutates the instance
        self = User { ... }; // ERROR — cannot reassign self
    }
}
```

---

## 6. Scoping & Imports

Extension methods follow Stash's existing import model. When a file is imported, any `extend` blocks in that file become active in the importing scope.

### File-level activation

```stash
// file: string-extras.stash
extend string {
    fn isPalindrome() {
        return self == self.reverse();
    }
    fn toSlug() {
        return self.lower().replaceRegex("[^a-z0-9]+", "-");
    }
}

// file: main.stash
import "string-extras.stash";

"racecar".isPalindrome();    // true — extension is active
"Hello World".toSlug();      // "hello-world" — extension is active
```

### Without import, extensions are not visible

```stash
// file: main.stash
// No import of string-extras.stash

"racecar".isPalindrome();    // ERROR — no method 'isPalindrome' on string
```

### Same-file extensions are immediately active

```stash
// file: main.stash
extend string {
    fn shout() { return self.upper() + "!"; }
}

"hello".shout();    // "HELLO!" — defined above, active in this file
```

### Transitive imports

Extensions propagate through imports. If `a.stash` imports `b.stash`, and `b.stash` contains `extend string { ... }`, those extensions are available in `a.stash`.

```stash
// file: base.stash
extend string {
    fn isPalindrome() { ... }
}

// file: utils.stash
import "base.stash";
// isPalindrome is available here

extend string {
    fn shout() { ... }
}

// file: main.stash
import "utils.stash";
// Both isPalindrome (from base.stash via utils.stash) and shout (from utils.stash) are available
```

This follows the same transitive behavior as struct and function imports — no special semantics needed.

### `import ... as` namespacing

When using `import "file.stash" as ns`, the extension methods are **still activated** on the target types. Extensions cannot be namespaced — they operate on types, not on identifiers:

```stash
import "string-extras.stash" as extras;

// extras.isPalindrome  — NOT how it works (isPalindrome is not a namespace member)
"racecar".isPalindrome();    // This is how it works — extension is active on string
```

This is intentional: extensions modify type behavior, not namespace contents.

---

## 7. Method Resolution Order

When evaluating `receiver.name(args)`, the interpreter checks in this order:

| Priority | Source                       | Example                                                          |
| -------- | ---------------------------- | ---------------------------------------------------------------- |
| 1        | **Struct fields**            | `instance.fieldName` — direct field access                       |
| 2        | **Struct methods**           | `instance.method()` — methods from original `struct` declaration |
| 3        | **Dict key lookup**          | `myDict.key` — entry by key name                                 |
| 4        | **Enum member lookup**       | `Status.Active` — enum member                                    |
| 5        | **Namespace member lookup**  | `fs.readFile` — namespace function                               |
| 6        | **Extension methods**        | Methods from `extend` blocks in scope                            |
| 7        | **UFCS namespace functions** | `str.upper(s)` called as `s.upper()`                             |
| 8        | **Error**                    | `"No method 'name' on type 'typename'"`                          |

> **Dict exception:** For dictionaries, extension methods are checked *before* key lookup (priority 6 beats priority 3). Without this, any dict key matching an extension method name would shadow the extension, making dict extensions effectively unusable. The `dict.get(d, "key")` namespace function is available when you need explicit key access.

### Key ordering decisions

**Extensions before UFCS (6 before 7):** Extension methods can shadow UFCS namespace functions. This allows users to override or customize stdlib behavior per-type:

```stash
// UFCS resolves str.upper(s) as s.upper()
// An extension can override this:
extend string {
    fn upper() {
        // Custom upper that preserves certain characters
        return str.replaceRegex(str.upper(self), "SS", "ß");
    }
}

"straße".upper();         // Uses extension — custom behavior
str.upper("straße");      // Namespace call — original behavior, unaffected
```

**Struct methods before extensions (2 before 6):** Methods defined in the original struct declaration take priority over extensions. This prevents extensions from silently overriding the struct author's methods:

```stash
struct Logger {
    level

    fn info(msg) {
        io.println("[INFO] " + msg);
    }
}

extend Logger {
    fn info(msg) {
        // This is NEVER called — original struct method wins
        io.println("[" + self.level + "] " + msg);
    }
}

let log = Logger { level: "DEBUG" };
log.info("test");     // "[INFO] test" — original method
```

### Conflict between multiple extend blocks

When two `extend` blocks for the same type define a method with the same name, the **last one loaded wins** (last-registration-wins):

```stash
import "a.stash";   // extend string { fn foo() { return "A"; } }
import "b.stash";   // extend string { fn foo() { return "B"; } }

"x".foo();           // "B" — b.stash was imported last
```

This follows the natural behavior of dictionary insertion (the extension registry is keyed by method name). A **warning** should be emitted by the LSP/semantic validator when a shadowed extension method is detected.

---

## 8. Constraints & Restrictions

### Methods only — no fields, constants, or nested types

```stash
extend string {
    fn valid() { ... }          // OK
    async fn fetch() { ... }    // OK

    cachedLen,                   // ERROR — cannot add fields
    const MAX = 100;             // ERROR — cannot add constants
    struct Inner { ... }         // ERROR — cannot nest type declarations
}
```

### No `extend` inside functions or blocks

`extend` is a top-level statement — it cannot appear inside functions, if-blocks, loops, or other scopes:

```stash
fn setup() {
    extend string {              // ERROR — extend must be at top level
        fn foo() { ... }
    }
}

if (true) {
    extend string { ... }       // ERROR — extend must be at top level
}
```

This prevents conditional or runtime-dependent method registration, which would make method availability unpredictable.

### The target type must exist

The type name after `extend` must resolve to a known type at the point of the declaration:

```stash
extend UnknownType { ... }      // ERROR — 'UnknownType' is not a defined type

struct User { name }
extend User { ... }             // OK — User is defined above

import { Config } from "config.stash";
extend Config { ... }           // OK — Config was imported
```

### No recursive self-extension

An extension method can call other extension methods on `self`, but the extension cannot be used to define the type itself:

```stash
extend string {
    fn a() { return self.b(); }  // OK — calls extension method b() below
    fn b() { return "B"; }
}
```

### Method name collision with struct fields

If a struct has a field and an extension defines a method with the same name, the **field wins** (per resolution order §7):

```stash
struct Item { name, value }

extend Item {
    fn value() {                 // This is callable only via explicit means
        return self.value * 2;   // Refers to the field, not this method (no infinite loop)
    }
}

let item = Item { name: "x", value: 10 };
item.value;      // 10 — field access
```

---

## 9. Examples

### String utilities

```stash
extend string {
    fn isPalindrome() {
        let cleaned = self.lower().replaceRegex("[^a-z0-9]", "");
        return cleaned == cleaned.reverse();
    }

    fn isBlank() {
        return self.trim() == "";
    }

    fn initials() {
        return self.split(" ")
            .filter((w) => w != "")
            .map((w) => str.substring(w, 0, 1).upper())
            .join(". ") + ".";
    }

    fn toSlug() {
        return self.lower()
            .replaceRegex("[^a-z0-9\\s-]", "")
            .trim()
            .replaceRegex("[\\s]+", "-");
    }
}

"A man a plan a canal Panama".isPalindrome();  // true
"   \n  ".isBlank();                            // true
"John Michael Doe".initials();                  // "J. M. D."
"Hello, World! 123".toSlug();                   // "hello-world-123"
```

### Array helpers

```stash
extend array {
    fn compact() {
        return self.filter((x) => x != null);
    }

    fn first() {
        return len(self) > 0 ? self[0] : null;
    }

    fn last() {
        return len(self) > 0 ? self[len(self) - 1] : null;
    }

    fn isEmpty() {
        return len(self) == 0;
    }

    fn partition(predicate) {
        let pass = self.filter(predicate);
        let fail = self.filter((x) => !predicate(x));
        return [pass, fail];
    }

    fn groupBy(keyFn) {
        let groups = dict.new();
        for (let item in self) {
            let key = conv.toStr(keyFn(item));
            if (!dict.has(groups, key)) {
                dict.set(groups, key, []);
            }
            arr.push(dict.get(groups, key), item);
        }
        return groups;
    }
}

[1, null, 2, null, 3].compact();              // [1, 2, 3]
[10, 20, 30].first();                          // 10
[].isEmpty();                                   // true
[1, 2, 3, 4, 5].partition((n) => n > 3);      // [[4, 5], [1, 2, 3]]
```

### Struct extensions — separating definition from behavior

```stash
// file: models/user.stash
struct User {
    name,
    email,
    role,
    active
}

// file: models/user-display.stash
import { User } from "models/user.stash";

extend User {
    fn displayName() {
        return self.name + " (" + self.role + ")";
    }

    fn avatar() {
        let hash = crypto.md5(self.email.lower().trim());
        return "https://gravatar.com/avatar/" + hash;
    }
}

// file: models/user-validation.stash
import { User } from "models/user.stash";

extend User {
    fn isValid() {
        if (self.name.isBlank()) return false;
        if (!self.email.contains("@")) return false;
        return true;
    }

    fn errors() {
        let errs = [];
        if (self.name.isBlank()) arr.push(errs, "Name is required");
        if (!self.email.contains("@")) arr.push(errs, "Invalid email");
        return errs;
    }
}

// file: main.stash
import { User } from "models/user.stash";
import "models/user-display.stash";
import "models/user-validation.stash";

let user = User { name: "Alice", email: "alice@example.com", role: "admin", active: true };
io.println(user.displayName());   // "Alice (admin)"
io.println(user.isValid());       // true
```

### Numeric extensions

```stash
extend int {
    fn isEven() {
        return self % 2 == 0;
    }

    fn times(callback) {
        for (let i in 0..self) {
            callback(i);
        }
    }

    fn clamp(min, max) {
        if (self < min) return min;
        if (self > max) return max;
        return self;
    }
}

42.isEven();         // true
3.times((i) => io.println(i));  // prints 0, 1, 2
150.clamp(0, 100);   // 100
```

---

## 10. Package Ecosystem

Extension blocks are the mechanism by which Stash packages enhance built-in types. A package ships `.stash` files containing `extend` blocks. Users import the package and immediately gain new methods.

### Example: `@stash/string-utils`

```stash
// node_modules/@stash/string-utils/index.stash

extend string {
    fn toCamelCase() { ... }
    fn toSnakeCase() { ... }
    fn toKebabCase() { ... }
    fn toPascalCase() { ... }
    fn isEmail() { ... }
    fn isUrl() { ... }
    fn isIPv4() { ... }
    fn mask(visibleChars, maskChar = "*") { ... }
}
```

```stash
// User code:
import "@stash/string-utils";

let className = "my-component-name".toCamelCase();  // "myComponentName"
let isValid = "user@example.com".isEmail();          // true
let masked = "4111111111111111".mask(4);              // "************1111"
```

### Example: `@stash/collections`

```stash
// node_modules/@stash/collections/index.stash

extend array {
    fn compact() { ... }
    fn chunk(size) { ... }
    fn interleave(other) { ... }
    fn frequencies() { ... }
    fn tally(keyFn) { ... }
    fn sliding(size, step = 1) { ... }
}

extend dict {
    fn invert() { ... }
    fn pick(keys) { ... }
    fn omit(keys) { ... }
    fn deepMerge(other) { ... }
}
```

### Discovery

The LSP should index extension methods from imported packages and surface them in autocomplete. When a developer types `myString.` in a file that imports `@stash/string-utils`, the completion list should include both stdlib UFCS methods and package extension methods.

---

## 11. Implementation Strategy

### Prerequisites

- **UFCS must be implemented first.** The `VisitDotExpr` fallback pipeline (type-to-namespace mapping, `BuiltInBoundMethod`) must exist. Extension methods plug into this pipeline at priority 6 (between struct methods and UFCS).

### New Components

#### 1. Keyword: `extend`

**Lexer** ([Lexer.cs](../../Stash.Core/Lexing/Lexer.cs)): Add `["extend"] = TokenType.Extend` to the `_keywords` frozen dictionary.

**TokenType** ([TokenType.cs](../../Stash.Core/Lexing/TokenType.cs)): Add `Extend` enum value with XML doc comment.

#### 2. AST Node: `ExtendStmt`

New file: `Stash.Core/Parsing/AST/ExtendStmt.cs`

```
ExtendStmt : Stmt
├── TypeName: Token           — the type being extended ("string", "User", etc.)
├── Methods: List<FnDeclStmt> — method declarations
└── Span: SourceSpan
```

Implements `Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitExtendStmt(this)`.

#### 3. Visitor Interface: `VisitExtendStmt`

Add to `IStmtVisitor<T>`:

```
T VisitExtendStmt(ExtendStmt stmt);
```

6 implementations required (one per visitor).

#### 4. Parser: `ExtendDeclaration()`

In [Parser.cs](../../Stash.Core/Parsing/Parser.cs), add handling for `TokenType.Extend` in the `Declaration()` method. Parsing logic:

1. Consume `extend` keyword
2. Consume type name identifier
3. Consume `{`
4. Parse method declarations in a loop (reuse the struct method parsing pattern — check for `fn` / `async fn`, delegate to `FnDeclaration()`)
5. Consume `}`
6. Return `ExtendStmt`

#### 5. Extension Registry

New class: `ExtensionRegistry` — stores extension methods keyed by `(typeName, methodName)`.

```
ExtensionRegistry
├── Register(typeName, methodName, IStashCallable)
├── TryGetMethod(typeName, methodName) → IStashCallable?
├── GetMethodsForType(typeName) → IReadOnlyDictionary<string, IStashCallable>
└── HasMethod(typeName, methodName) → bool
```

The registry is stored on the `Interpreter` instance (not global). Each interpreter has its own registry, populated by `VisitExtendStmt` as files are loaded and imports are processed.

#### 6. Interpreter: `VisitExtendStmt`

```
1. Resolve TypeName — validate it's a known type:
   - Built-in type keyword ("string", "array", "dict", "int", "float")
   - Or a StashStruct in scope
2. For each FnDeclStmt in Methods:
   a. Wrap as StashFunction with current closure environment
   b. Register in ExtensionRegistry as (typeName, methodName, function)
3. For struct types: also add directly to StashStruct.Methods dictionary
   (since struct methods and extensions share the same lookup path)
```

#### 7. `VisitDotExpr` Integration

In `Interpreter.Expressions.cs`, between the existing namespace lookup and the UFCS fallback, add extension method resolution:

```
... existing checks (StashError, StashInstance, StashDictionary, StashEnum, StashNamespace) ...

// Extension method lookup (new)
string typeName = GetTypeName(obj);  // "string", "array", "int", struct name, etc.
if (typeName is not null && _extensionRegistry.TryGetMethod(typeName, expr.Name.Lexeme, out var extMethod))
{
    return new BuiltInBoundMethod(obj, extMethod);  // Reuses UFCS's BuiltInBoundMethod
}

// UFCS namespace lookup (existing from UFCS implementation)
...
```

### Files Changed

| File                         | Change                                                   | Effort            |
| ---------------------------- | -------------------------------------------------------- | ----------------- |
| `TokenType.cs`               | Add `Extend` enum value                                  | Trivial           |
| `Lexer.cs`                   | Add `"extend"` to keyword map                            | Trivial           |
| New: `ExtendStmt.cs`         | New AST node class                                       | Small (~25 lines) |
| `IStmtVisitor.cs`            | Add `VisitExtendStmt` method                             | Trivial           |
| `Parser.cs`                  | Add `ExtendDeclaration()` parsing                        | Small (~25 lines) |
| New: `ExtensionRegistry.cs`  | Extension method storage                                 | Small (~40 lines) |
| `Interpreter.cs`             | Add `_extensionRegistry` field, init                     | Trivial           |
| `Interpreter.Statements.cs`  | Add `VisitExtendStmt`                                    | Small (~25 lines) |
| `Interpreter.Expressions.cs` | Add extension lookup in `VisitDotExpr`                   | Small (~10 lines) |
| `Resolver.cs`                | Add `VisitExtendStmt` — resolve method bodies            | Small             |
| `SemanticValidator.cs`       | Add `VisitExtendStmt` — validate type exists, duplicates | Medium            |
| `SymbolCollector.cs`         | Add `VisitExtendStmt` — record extension method symbols  | Medium            |
| `SemanticTokenWalker.cs`     | Add `VisitExtendStmt` — classify tokens                  | Small             |
| `StashFormatter.cs`          | Add `VisitExtendStmt` — format extension block           | Small             |
| `StdlibRegistry.Types.cs`    | Add `IsExtendableType(string)` helper                    | Trivial           |

### Files NOT Changed

- **No changes to existing BuiltIn files** — extensions are user-defined, not stdlib
- **No changes to `StashNamespace`** — extensions use a separate registry
- **No changes to `BuiltInBoundMethod`** — extensions reuse the UFCS infrastructure
- **No changes to `StashInstance`** — struct extensions go through `StashStruct.Methods` (existing path)

### Estimated Scope

~200 lines of new code across parser, interpreter, and visitors. ~50 lines of modifications to existing files. One new AST node, one new runtime class, one new keyword.

---

## 12. LSP Integration

### Autocomplete

When the LSP detects `.` after a typed value, it should include extension methods from all `extend` blocks visible in the current file's import chain:

```stash
import "@stash/string-utils";

myString.              // Shows:
                       //   upper() → string          (UFCS: str namespace)
                       //   trim() → string           (UFCS: str namespace)
                       //   toCamelCase() → string    (extension: @stash/string-utils)
                       //   isEmail() → bool          (extension: @stash/string-utils)
```

Extension methods should be visually distinguished from UFCS methods in the completion list (e.g., with a different icon or detail label like `(extension)`).

### Hover

Hovering over an extension method call should show:

```
(extension) fn isPalindrome() → bool
Defined in: string-extras.stash
```

### Go-to-Definition

Extension method references should resolve to the `fn` declaration inside the `extend` block.

### Find References

All call sites of an extension method should be findable via Find References, including across files.

### Diagnostics

The semantic validator should emit:

- **Error:** `extend` target type does not exist
- **Error:** `extend` block contains non-method declarations (fields, constants)
- **Warning:** Extension method shadows an existing struct method (will never be called)
- **Warning:** Extension method shadows another extension method from a different import
- **Info:** Extension method shadows a UFCS namespace function (intentional override)

---

## 13. Future Considerations

### Interface default methods via `extend`

A natural evolution would allow `extend` on interfaces to provide default method implementations:

```stash
interface Printable {
    fn toString();
}

extend Printable {
    fn print() {
        io.println(self.toString());  // default implementation using required method
    }
}
```

This is deferred — it introduces method resolution complexity (struct methods vs interface defaults vs extensions) that deserves its own analysis.

### `extend` with interface conformance

Another future direction: using `extend` to retroactively make a type conform to an interface:

```stash
interface Serializable {
    fn serialize();
}

extend User : Serializable {
    fn serialize() {
        return json.stringify({ name: self.name, email: self.email });
    }
}
```

This is similar to Rust's `impl Trait for Type` and Swift's retroactive conformance. Deferred for now.

### Extension properties

Some languages allow extension properties (computed values):

```stash
extend string {
    get length {
        return len(self);
    }
}

"hello".length;    // 5 — no parentheses
```

Stash has no property concept yet (struct fields are direct access). If computed properties are ever added, they should work in `extend` blocks. Deferred until the property system is designed.
