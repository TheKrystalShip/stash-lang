# Spread/Rest Parameters — Language Feature

> **Status:** Design (backlog)
> **Created:** April 2026
> **Purpose:** Add spread and rest parameter syntax (`...`) to Stash, enabling variadic user-defined functions, array/dict spreading in literals and calls, and rest elements in destructuring patterns.
> **Ref:** [Missing Scripting Fundamentals — Gap Analysis](Missing%20Scripting%20Fundamentals%20—%20Gap%20Analysis.md) §4.1

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Design Overview](#2-design-overview)
3. [Syntax & Semantics](#3-syntax--semantics)
   - 3.1 [Rest Parameters in Function Declarations](#31-rest-parameters-in-function-declarations)
   - 3.2 [Spread in Function Calls](#32-spread-in-function-calls)
   - 3.3 [Spread in Array Literals](#33-spread-in-array-literals)
   - 3.4 [Spread in Dictionary Literals](#34-spread-in-dictionary-literals)
   - 3.5 [Rest in Array Destructuring](#35-rest-in-array-destructuring)
   - 3.6 [Rest in Object Destructuring](#36-rest-in-object-destructuring)
4. [Grammar](#4-grammar)
5. [Interaction with Existing Features](#5-interaction-with-existing-features)
6. [Edge Cases & Error Conditions](#6-edge-cases--error-conditions)
7. [Implementation Impact](#7-implementation-impact)
   - 7.6 [Static Analysis Diagnostics](#76-static-analysis-diagnostics)
8. [Cross-Platform Considerations](#8-cross-platform-considerations)
9. [LSP / DAP / Tooling Impact](#9-lsp--dap--tooling-impact)
10. [Test Scenarios](#10-test-scenarios)
11. [Decision Log](#11-decision-log)

---

## 1. Motivation

Stash currently has no way to write user-defined variadic functions. The only variadic callables are built-ins that set `Arity = -1` and manually validate `args.Count`. User-defined functions must declare a fixed number of parameters (with optional defaults), which forces awkward patterns:

```stash
// Today: must pass an explicit array
fn log(level, messages) {
    for (let msg in messages) {
        io.println($"[{level}] {msg}");
    }
}
log("INFO", ["Server started", "Port 8080"]);  // Caller wraps in array

// Desired: natural variadic call
fn log(level, ...messages) {
    for (let msg in messages) {
        io.println($"[{level}] {msg}");
    }
}
log("INFO", "Server started", "Port 8080");  // Natural call syntax
```

Beyond variadic functions, the absence of spread syntax forces verbose workarounds for common operations:

```stash
// Today: array concatenation
let combined = arr.concat(arr.concat(a, b), c);

// Desired: inline spreading
let combined = [...a, ...b, ...c];

// Today: dict merging with overrides
let config = dict.merge(dict.merge(defaults, overrides), { debug: true });

// Desired: inline spreading
let config = { ...defaults, ...overrides, debug: true };

// Today: passing array as individual arguments
fn add(a, b, c) { return a + b + c; }
let nums = [1, 2, 3];
// No way to call add with the elements of nums without add(nums[0], nums[1], nums[2])

// Desired:
add(...nums);
```

Every major scripting language has this feature: JavaScript (`...`), Python (`*`/`**`), Ruby (`*`/`**`), PHP (`...`), Kotlin (`*`). Its absence is a constant friction point for Stash users.

---

## 2. Design Overview

The `...` (three-dot ellipsis) operator serves as both **spread** (expanding a collection inline) and **rest** (collecting excess values into an array/dict). It appears in six contexts:

| Context                   | Syntax                   | Role   | Example                                        |
| ------------------------- | ------------------------ | ------ | ---------------------------------------------- |
| Function/lambda parameter | `fn f(...args)`          | Rest   | Collects excess arguments into an array        |
| Function/lambda call      | `f(...arr)`              | Spread | Expands array elements as individual arguments |
| Array literal             | `[...arr, x]`            | Spread | Expands array elements inline                  |
| Dictionary literal        | `{...dict, k: v}`        | Spread | Expands dict entries inline                    |
| Array destructuring       | `let [a, ...rest] = arr` | Rest   | Collects remaining elements into an array      |
| Object destructuring      | `let {a, ...rest} = obj` | Rest   | Collects remaining fields into a dict          |

A single new token (`DotDotDot`) and a single new AST node (`SpreadExpr`) cover all six contexts, with minor modifications to existing parameter and destructuring AST nodes.

---

## 3. Syntax & Semantics

### 3.1 Rest Parameters in Function Declarations

A rest parameter collects all excess positional arguments into an array.

```stash
fn log(level, ...messages) {
    for (let msg in messages) {
        io.println($"[{level}] {msg}");
    }
}

log("INFO", "one", "two", "three");
// level = "INFO", messages = ["one", "two", "three"]

log("WARN");
// level = "WARN", messages = []  (empty array, not an error)

log();
// ERROR: Expected at least 1 argument but got 0.
```

**Rules:**

1. A function may have **at most one** rest parameter.
2. The rest parameter must be the **last parameter** in the list.
3. The rest parameter **cannot have a default value** — it implicitly defaults to `[]` (empty array).
4. The rest parameter **may have a type hint** for documentation: `...args: string` (not enforced at runtime, consistent with existing type hints).
5. A function with a rest parameter becomes variadic: `Arity = -1`, `MinArity` = count of non-rest required parameters.

**Interaction with default parameters:**

```stash
fn configure(host, port = 8080, ...options) {
    io.println($"Host: {host}, Port: {port}");
    io.println($"Options: {options}");
}

configure("localhost");
// host = "localhost", port = 8080, options = []

configure("localhost", 9090);
// host = "localhost", port = 9090, options = []

configure("localhost", 9090, "verbose", "dry-run");
// host = "localhost", port = 9090, options = ["verbose", "dry-run"]
```

Arguments are bound left-to-right: required parameters first, then parameters with defaults, then any remaining arguments go into the rest parameter. This follows JavaScript semantics exactly.

**Lambdas work identically:**

```stash
let sum = (...nums) => {
    let total = 0;
    for (let n in nums) { total += n; }
    return total;
};

sum(1, 2, 3);  // 6

// Expression-body lambda:
let first = (head, ...tail) => head;
```

**Invalid declarations (parser errors):**

```stash
fn bad1(...a, b) { }          // ERROR: Rest parameter must be last
fn bad2(...a, ...b) { }       // ERROR: Only one rest parameter allowed
fn bad3(...a = []) { }        // ERROR: Rest parameter cannot have a default value
```

---

### 3.2 Spread in Function Calls

The `...expr` syntax in a call argument position evaluates `expr`, which must produce an array, and expands its elements as individual positional arguments.

```stash
fn add(a, b, c) { return a + b + c; }

let nums = [1, 2, 3];
add(...nums);    // add(1, 2, 3) → 6

// Mixed spread and regular arguments:
let pair = [2, 3];
add(1, ...pair);  // add(1, 2, 3) → 6

// Multiple spreads:
let first = [1, 2];
let second = [3];
add(...first, ...second);  // add(1, 2, 3) → 6

// Spread with extra args:
fn greet(greeting, ...names) { }
let people = ["Alice", "Bob"];
greet("Hello", ...people, "Charlie");
// greeting = "Hello", names = ["Alice", "Bob", "Charlie"]
```

**Runtime error if the spread target is not an array:**

```stash
let x = 5;
add(...x);   // RuntimeError: Cannot spread non-array value in function call.

let d = { a: 1 };
add(...d);   // RuntimeError: Cannot spread non-array value in function call.
```

**Arity checking happens after spread expansion:**

```stash
fn exact(a, b) { return a + b; }

let args = [1, 2, 3];
exact(...args);  // RuntimeError: Expected 2 arguments but got 3.

let short = [1];
exact(...short);  // RuntimeError: Expected 2 arguments but got 1.
```

---

### 3.3 Spread in Array Literals

The `...expr` syntax inside `[...]` evaluates `expr`, which must produce an array, and inserts its elements inline.

```stash
let a = [1, 2, 3];
let b = [0, ...a, 4];     // [0, 1, 2, 3, 4]

let c = [...a, ...a];     // [1, 2, 3, 1, 2, 3]

let empty = [];
let d = [1, ...empty, 2]; // [1, 2]

// Shallow — nested arrays are NOT flattened:
let nested = [[1, 2], [3, 4]];
let flat = [...nested];   // [[1, 2], [3, 4]]  — NOT [1, 2, 3, 4]
```

**Runtime error if the spread target is not an array:**

```stash
let x = 5;
let bad = [...x];   // RuntimeError: Cannot spread non-array value into array literal.

let s = "hello";
let bad = [...s];   // RuntimeError: Cannot spread non-array value into array literal.
```

> **Decision:** Strings are NOT spreadable into arrays. Unlike JavaScript where `[..."abc"]` produces `["a","b","c"]`, Stash keeps spread strictly typed — only arrays spread in array context. Use `str.split(s, "")` for character splitting.

---

### 3.4 Spread in Dictionary Literals

The `...expr` syntax inside `{...}` evaluates `expr`, which must produce a dictionary or struct instance, and merges its entries inline.

```stash
let defaults = { host: "localhost", port: 8080 };
let overrides = { port: 9090, debug: true };

let config = { ...defaults, ...overrides, name: "myapp" };
// { host: "localhost", port: 9090, debug: true, name: "myapp" }
// Later entries overwrite earlier ones (port: 9090 wins)

// Spread an empty dict — no-op:
let empty = {};
let d = { ...empty, key: "val" };  // { key: "val" }
```

**Evaluation order matters — later keys overwrite earlier ones:**

```stash
let base = { a: 1, b: 2, c: 3 };
let result = { ...base, b: 99, ...{ c: 100 } };
// { a: 1, b: 99, c: 100 }
// b overwritten by explicit key, c overwritten by second spread
```

**Spreading struct instances into a dictionary:**

```stash
struct Point { x, y }
let p = Point { x: 10, y: 20 };

let d = { ...p, z: 30 };
// { x: 10, y: 20, z: 30 }  — struct fields become dict entries
```

> **Decision:** Both dicts and struct instances are spreadable in dictionary literals. Struct fields become dict key-value pairs. This is useful for serialization patterns and struct-to-dict conversion. The result is always a dictionary, never a struct.

**Runtime errors:**

```stash
let x = 5;
let bad = { ...x };     // RuntimeError: Cannot spread value into dictionary literal. Expected dictionary or struct instance.

let arr = [1, 2, 3];
let bad = { ...arr };   // RuntimeError: Cannot spread value into dictionary literal. Expected dictionary or struct instance.
```

---

### 3.5 Rest in Array Destructuring

The `...name` syntax in an array destructuring pattern captures all remaining elements into a new array.

```stash
let arr = [1, 2, 3, 4, 5];

let [first, ...rest] = arr;
// first = 1, rest = [2, 3, 4, 5]

let [a, b, ...tail] = arr;
// a = 1, b = 2, tail = [3, 4, 5]

let [...all] = arr;
// all = [1, 2, 3, 4, 5]  (shallow copy)

// Fewer elements than named bindings:
let [x, y, z, ...remaining] = [1, 2];
// x = 1, y = 2, z = null, remaining = []

// Exact number of elements:
let [p, q, ...extra] = [10, 20];
// p = 10, q = 20, extra = []
```

**Rules:**

1. Rest must be the **last element** in the pattern.
2. Only **one** rest element allowed.
3. If the array has fewer elements than named bindings (excluding rest), missing bindings get `null` (existing behavior) and the rest gets `[]`.
4. The rest always produces an array, even if empty.

**Works with `const`:**

```stash
const [head, ...tail] = [1, 2, 3];
// head and tail are both constants
```

---

### 3.6 Rest in Object Destructuring

The `...name` syntax in an object destructuring pattern captures all remaining fields into a new dictionary.

```stash
let config = { host: "localhost", port: 8080, debug: true, verbose: false };

let { host, port, ...rest } = config;
// host = "localhost", port = 8080, rest = { debug: true, verbose: false }

let { debug, ...others } = config;
// debug = true, others = { host: "localhost", port: 8080, verbose: false }

// No properties left for rest:
let { host, port, debug, verbose, ...empty } = config;
// empty = {}
```

**With struct instances:**

```stash
struct Server { host, port, name }
let s = Server { host: "localhost", port: 80, name: "web" };

let { host, ...rest } = s;
// host = "localhost", rest = { port: 80, name: "web" }
// rest is a dict (not a struct), containing the un-destructured fields
```

**Rules:**

1. Rest must be the **last element** in the pattern.
2. Only **one** rest element allowed.
3. The rest always produces a **dictionary**, even when destructuring a struct instance.
4. The rest captures all fields NOT explicitly named in the pattern.

---

## 4. Grammar

### Token Addition

```
DotDotDot  ::=  "..."
```

Added alongside existing `Dot` (`.`) and `DotDot` (`..`). The lexer chains: if the next char after `..` is also `.`, emit `DotDotDot` instead of `DotDot`.

### Productions

```ebnf
(* Function / lambda parameters *)
ParamList    ::= Param ("," Param)* ("," RestParam)?
               | RestParam
Param        ::= IDENTIFIER (":" Type)? ("=" Expr)?
RestParam    ::= "..." IDENTIFIER (":" Type)?

(* Function call arguments *)
ArgList      ::= Arg ("," Arg)*
Arg          ::= "..." Expr
               | Expr

(* Array literal *)
ArrayLiteral ::= "[" (ArrayElem ("," ArrayElem)*)? "]"
ArrayElem    ::= "..." Expr
               | Expr

(* Dictionary literal *)
DictLiteral  ::= "{" (DictEntry ("," DictEntry)*)? "}"
DictEntry    ::= "..." Expr
               | IDENTIFIER ":" Expr

(* Array destructuring *)
ArrayDestructure ::= ("let" | "const") "[" DestructNames "]" "=" Expr ";"
DestructNames    ::= IDENTIFIER ("," IDENTIFIER)* ("," "..." IDENTIFIER)?
                   | "..." IDENTIFIER

(* Object destructuring *)
ObjDestructure   ::= ("let" | "const") "{" DestructFields "}" "=" Expr ";"
DestructFields   ::= IDENTIFIER ("," IDENTIFIER)* ("," "..." IDENTIFIER)?
                   | "..." IDENTIFIER
```

### Precedence

The `...` prefix has **no precedence conflicts**. It always appears in specific syntactic positions:

- Before a parameter name (declaration context, unambiguous)
- Before an expression (call/literal context, prefix operator in contained position)

It does NOT conflict with:

- `..` (range operator) — ranges appear in expression context between two operands (`a..b`), never prefixed
- `.` (member access) — always infix between two expressions

---

## 5. Interaction with Existing Features

### 5.1 Default Parameters

Rest parameter must follow all other parameters, including those with defaults:

```stash
fn f(a, b = 5, ...rest) { }  // Valid

fn f(a, ...rest, b = 5) { }  // ERROR: Rest parameter must be last
```

Argument binding order: required → default → rest. This is consistent with the existing left-to-right binding in `UserCallable.BindParameters`.

### 5.2 UFCS (Unified Function Call Syntax)

UFCS transforms `receiver.method(args)` into `namespace.method(receiver, args)`. Spread in the call args works naturally:

```stash
let extra = [", "];
let result = ["a", "b", "c"].join(...extra);
// Transforms to: str.join(["a", "b", "c"], ", ")
// UFCS prepends receiver, then spread expands normally
```

No special handling needed — spread is resolved during argument evaluation, which happens after UFCS rewrites the call.

### 5.3 Pipe Operator / Command Expressions

No interaction. `|` in command context is a shell pipe; `...` has no command-context meaning. They operate at different syntax levels.

### 5.4 Optional Chaining

```stash
obj?.method(...args);  // Valid — spread inside optionally-chained call
```

No special interaction.

### 5.5 `try` Expression

```stash
let result = try fn(...args);  // Valid — try wraps the call result
```

No interaction — `try` wraps the return value, not the argument expansion.

### 5.6 Type Hints

```stash
fn process(...items: string) { }
// Type hint suggests each element of items is a string
// Not enforced at runtime (consistent with existing type hint behavior)
// Shown in LSP hover/signature help
```

### 5.7 Async Functions

```stash
async fn fetchAll(...urls: string) {
    let tasks = arr.map(urls, (url) => task.run(() => http.get(url)));
    return task.all(tasks);
}
```

No special interaction — rest parameters work identically in async functions.

### 5.8 Built-in Functions

Built-in functions that already use `isVariadic: true` work with spread arguments transparently — spread expansion happens at the call site before the built-in receives its `List<object?>`.

```stash
let parts = ["world", "!"];
io.println(...parts);  // Spread expands to io.println("world", "!") — then arity check applies
```

### 5.9 `arr.push` / `arr.concat` / `dict.merge`

Spread does NOT replace these functions — they remain useful for programmatic operations. Spread is syntactic sugar for literal contexts that improves readability:

```stash
// These are equivalent:
let result = arr.concat(a, b);
let result = [...a, ...b];

// These are equivalent:
let result = dict.merge(d1, d2);
let result = { ...d1, ...d2 };
```

---

## 6. Edge Cases & Error Conditions

### 6.1 Parser Errors (compile-time)

| Code                      | Error Message                                                     |
| ------------------------- | ----------------------------------------------------------------- |
| `fn f(...a, b) { }`       | `Rest parameter must be the last parameter.`                      |
| `fn f(...a, ...b) { }`    | `Only one rest parameter is allowed.`                             |
| `fn f(...a = []) { }`     | `Rest parameter cannot have a default value.`                     |
| `let [...a, b] = arr;`    | `Rest element must be the last element in destructuring pattern.` |
| `let [...a, ...b] = arr;` | `Only one rest element is allowed in destructuring pattern.`      |
| `let { ...a, b } = obj;`  | `Rest element must be the last element in destructuring pattern.` |

### 6.2 Runtime Errors

| Code             | Error Message                                                                          |
| ---------------- | -------------------------------------------------------------------------------------- |
| `add(...5)`      | `Cannot spread non-array value in function call.`                                      |
| `add(...{a: 1})` | `Cannot spread non-array value in function call.`                                      |
| `[...5]`         | `Cannot spread non-array value into array literal.`                                    |
| `[..."hello"]`   | `Cannot spread non-array value into array literal.`                                    |
| `{...5}`         | `Cannot spread value into dictionary literal. Expected dictionary or struct instance.` |
| `{...[1, 2]}`    | `Cannot spread value into dictionary literal. Expected dictionary or struct instance.` |

### 6.3 Semantics Edge Cases

**Spread is always shallow:**

```stash
let nested = [[1, 2], [3, 4]];
let result = [...nested];  // [[1, 2], [3, 4]] — NOT [1, 2, 3, 4]
// Use arr.flat(nested) for flattening
```

**Rest produces a new array/dict (not a view/slice):**

```stash
let arr = [1, 2, 3];
let [head, ...tail] = arr;
arr.push(arr, 4);
// tail is still [2, 3] — it's a new copy, not affected by mutations
```

**Spreading `null` or missing values:**

```stash
let x = null;
add(...x);      // RuntimeError: Cannot spread non-array value in function call.
[...null];      // RuntimeError: Cannot spread non-array value into array literal.
{...null};      // RuntimeError: Cannot spread value into dictionary literal. Expected dictionary or struct instance.
```

**Empty spread is a no-op:**

```stash
let empty = [];
[1, ...empty, 2];  // [1, 2]

let emptyDict = {};
{ ...emptyDict, k: "v" };  // { k: "v" }
```

**Rest with no remaining elements produces empty collection:**

```stash
let [a, ...rest] = [1];     // a = 1, rest = []
let { x, ...rest } = { x: 1 };  // x = 1, rest = {}
```

**Spread in dict literal — key collision semantics:**

```stash
let d1 = { a: 1, b: 2 };
let d2 = { b: 3, c: 4 };
let result = { ...d1, b: 99, ...d2 };
// Evaluation is left-to-right:
// Step 1: { a: 1, b: 2 }        — from d1
// Step 2: { a: 1, b: 99 }       — explicit key overwrites
// Step 3: { a: 1, b: 3, c: 4 }  — d2 overwrites b again
// result = { a: 1, b: 3, c: 4 }
```

---

## 7. Implementation Impact

### 7.1 Lexer (`Stash.Core/Lexing/`)

**TokenType.cs:** Add `DotDotDot` enum member after `DotDot`.

**Lexer.cs:** Extend the `.` case to chain three dots:

```
case '.':
    if Match('.') then
        if Match('.') then DotDotDot
        else DotDot
    else Dot
```

### 7.2 Parser (`Stash.Core/Parsing/`)

**New AST node:** `SpreadExpr` — wraps an inner `Expr` and marks it as spread.

```
SpreadExpr {
    Expression: Expr     // The expression being spread
    Operator: Token      // The ... token (for SourceSpan)
}
```

**Modified AST nodes:**

- `FnDeclStmt` — add `bool HasRestParameter` (or `Token? RestParameter`). If the last param was prefixed with `...`, this flag is set.
- `LambdaExpr` — same addition as `FnDeclStmt`.
- `DestructureStmt` — add `Token? RestName` to capture the `...name` rest variable.

**Parser changes:**

1. **Function parameter parsing** (`FnDeclaration`, ~line 328): Before parsing each parameter name, check for `DotDotDot`. If found, parse the rest parameter, enforce it's the last one, and set the rest flag.
2. **Lambda parameter parsing** (`ParseLambda`, ~line 2191): Same change.
3. **Call argument parsing** (`FinishCall`, ~line 1941 area): When parsing each argument, check for leading `DotDotDot`. If found, wrap the next expression in `SpreadExpr`.
4. **Array literal parsing** (~line 1941): Same — check for `DotDotDot` before each element.
5. **Dict literal parsing**: Check for `DotDotDot` before each entry. If found, parse expression (not key-value pair) and represent as a spread entry.
6. **Destructuring parsing** (`DestructureDeclaration`, ~line 273): Before parsing each name, check for `DotDotDot`. If found, parse the rest name, enforce it's last.

### 7.3 Interpreter (`Stash.Interpreter/`)

**`VisitCallExpr`** (~Interpreter.Expressions.cs line 1177): After evaluating arguments, flatten any `SpreadExpr` results. Currently `arguments` is built by evaluating each arg expression — add a post-pass that expands spread elements into the flat argument list.

**`UserCallable.BindParameters`** (~Types/UserCallable.cs line 56): When rest parameter is present, bind named params normally, then pack all remaining arguments into a `List<object?>` for the rest parameter.

**`VisitArrayExpr`**: Currently evaluates each element and adds to a list. For `SpreadExpr` elements, evaluate the inner expression and add each array element individually (flattening one level).

**`VisitDictLiteralExpr`**: For spread entries, evaluate the expression (must be dict or struct instance) and merge all key-value pairs into the result dict.

**`VisitDestructureStmt`**: For array patterns with rest, bind named variables up to the rest position, then pack remaining array elements into a new list for the rest variable. For object patterns with rest, bind named fields, then collect all un-named fields into a new dict.

### 7.4 Analysis (`Stash.Analysis/`)

The static analysis engine requires changes across all four concerns: symbol collection, type inference, semantic validation (diagnostics), and semantic token classification. This is the most impactful area — getting analysis right means users see problems **before** running their scripts.

See **§7.6 Static Analysis Diagnostics** for the full diagnostic catalog.

#### SymbolCollector

- **Rest parameters:** Register rest parameters as `SymbolKind.Parameter` with `TypeHint = "array"`. Mark the containing function symbol as variadic — set the function's `RequiredParameterCount` to the count of non-rest, non-default parameters (consistent with existing `MinArity` logic). Store the rest parameter in `ParameterNames` with a `...` prefix to distinguish it in signature help.
- **Rest in destructuring:** Register rest bindings as `SymbolKind.Variable` (or `SymbolKind.Constant` for `const` destructuring) with `TypeHint = "array"` for array patterns and `TypeHint = "dict"` for object patterns.
- **SpreadExpr:** Walk the inner expression to record any references it contains (e.g., `...myArray` records a read reference to `myArray`).

#### TypeInferenceEngine

- **SpreadExpr:** Not a type-producing node itself — it expands into the enclosing context. `InferExpressionType` should recurse into the inner expression for diagnostic purposes but the spread node itself has no standalone type.
- **Rest parameter type:** Always `"array"` — hardcoded during symbol collection, no inference needed.
- **Rest destructuring type:** `"array"` for array patterns, `"dict"` for object patterns.

#### SemanticValidator

All new diagnostics are detailed in **§7.6**. The validator needs:

- `VisitSpreadExpr(SpreadExpr expr)` — visit inner expression, then perform type-based checks.
- Modified `VisitCallExpr` — adjusted arity logic when spread arguments are present.
- Modified `VisitDestructureStmt` — validate rest element position and count (reinforcing parser checks for analysis-only code paths like the REPL).

#### SemanticTokenWalker

- `VisitSpreadExpr` — classify the `...` token as `SemanticTokenType.Operator`. Then recurse into the inner expression for its own classification.

### 7.5 AST Visitors

Both `IExprVisitor<T>` and `IStmtVisitor<T>` must be updated:

- `IExprVisitor<T>` gets `VisitSpreadExpr(SpreadExpr expr)` for the new `SpreadExpr` node.
- All existing visitor implementations must handle the new node (LSP semantic tokens, DAP evaluator, analysis visitors, etc.).

### 7.6 Static Analysis Diagnostics

This section catalogs every diagnostic the `SemanticValidator` should emit for spread/rest usage. Diagnostics are grouped by context and severity. Each diagnostic specifies what triggers it, the message text, the severity level, and what the underline span should cover.

The static analysis philosophy for spread/rest: **catch what we can prove, stay silent on what's ambiguous.** Since spread counts are dynamic, we can't always verify arity — but we CAN verify types, structural violations, and obvious mistakes.

#### 7.6.1 Type Mismatch Diagnostics (Warning)

These diagnostics fire when the type inference engine can statically determine that the expression being spread has an incompatible type. They are **warnings** (not errors) because Stash is dynamically typed — the inferred type might be wrong if the variable was reassigned.

**SA-SPREAD-01: Spreading non-array in array context**

Fires when: A `SpreadExpr` appears inside an array literal `[...]` or a function call argument list, and the inner expression's inferred type is known and is NOT `"array"`.

```stash
let x: int = 5;
let bad = [...x];      // ⚠ Warning on `...x`
//         ~~~~

let s: string = "hello";
let bad = [...s];      // ⚠ Warning on `...s`
//         ~~~~

let d: dict = { a: 1 };
add(...d);             // ⚠ Warning on `...d`
//  ~~~~
```

| Field   | Value                                                    |
| ------- | -------------------------------------------------------- |
| Message | `"Spread argument has type '{type}', expected 'array'."` |
| Level   | `Warning`                                                |
| Span    | The `SpreadExpr` span (covers `...expr`)                 |

Does NOT fire when: The inferred type is `null` (unknown type — we can't prove it's wrong), or `"array"`.

**SA-SPREAD-02: Spreading non-dict/struct in dictionary context**

Fires when: A `SpreadExpr` appears inside a dictionary literal `{...}`, and the inner expression's inferred type is known and is NOT `"dict"` and is NOT a known struct name.

```stash
let x: int = 5;
let bad = { ...x };       // ⚠ Warning on `...x`
//          ~~~~

let arr: array = [1, 2];
let bad = { ...arr };     // ⚠ Warning on `...arr`
//          ~~~~~~
```

| Field   | Value                                                                      |
| ------- | -------------------------------------------------------------------------- |
| Message | `"Spread argument has type '{type}', expected 'dict' or struct instance."` |
| Level   | `Warning`                                                                  |
| Span    | The `SpreadExpr` span                                                      |

**SA-SPREAD-03: Spreading null literal**

Fires when: The spread inner expression is a literal `null`. This is always an error at runtime, and since it's a literal, we can be certain.

```stash
let bad = [...null];     // ⚠ Warning on `...null`
let bad = { ...null };   // ⚠ Warning on `...null`
add(...null);            // ⚠ Warning on `...null`
```

| Field   | Value                                             |
| ------- | ------------------------------------------------- |
| Message | `"Spreading 'null' will always fail at runtime."` |
| Level   | `Warning`                                         |
| Span    | The `SpreadExpr` span                             |

#### 7.6.2 Arity Diagnostics (Error / Adjusted)

The existing arity check in `VisitCallExpr` must be updated to handle spread arguments. The current logic counts `expr.Arguments.Count` directly — this breaks when some arguments are `SpreadExpr` because each spread may expand into 0..N actual arguments at runtime.

**Modified arity checking strategy:**

1. **No spread arguments present:** Existing logic applies unchanged. Count arguments, compare against `RequiredParameterCount..ParameterCount` range.

2. **Spread arguments present, callee has rest parameter:** Skip arity checking entirely. The rest parameter absorbs any excess arguments, and the spread count is unknown statically. No diagnostic.

3. **Spread arguments present, callee has NO rest parameter (fixed arity):**

   Count the **statically-known minimum arguments** — the number of non-spread arguments. If this minimum already exceeds the function's maximum arity, emit an error.

**SA-ARITY-01: Too many non-spread arguments with spread present**

Fires when: A call contains spread arguments AND the count of non-spread arguments alone already exceeds the callee's maximum parameter count.

```stash
fn add(a, b) { return a + b; }
let extra = [1, 2];

add(1, 2, 3, ...extra);   // ❌ Error: 3 non-spread args already > 2 params
//  ~~~~~~~~~~~~~~~~~~
```

| Field   | Value                                                                               |
| ------- | ----------------------------------------------------------------------------------- |
| Message | `"At least {minCount} arguments provided but '{name}' expects at most {maxArity}."` |
| Level   | `Error`                                                                             |
| Span    | The call's closing paren span (consistent with existing arity errors)               |

**SA-ARITY-02: Skip exact arity check when spread is present**

When spread arguments are present and the non-spread count doesn't obviously violate arity, **emit no diagnostic**. The spread could expand to any number of elements.

```stash
fn add(a, b) { return a + b; }
let args = getArgs();

add(...args);        // No diagnostic — could be [1, 2] at runtime
add(1, ...args);     // No diagnostic — args could be [2]
```

**Rationale:** False positives are worse than silence. If the user is spreading, they know the count is dynamic. A wrong arity error that shows up despite correct runtime behavior would erode trust in the analysis engine.

**SA-ARITY-03: Spread into known-arity built-in**

For non-variadic stdlib functions (where `IsVariadic == false`), the same strategy applies: count non-spread args, error only if the minimum already exceeds the expected arity. Do NOT special-case built-ins differently from user functions.

#### 7.6.3 Argument Type Diagnostics with Spread (Warning)

The existing argument type checking in `VisitCallExpr` iterates over arguments positionally: argument `i` is checked against parameter type `i`. With spread, this positional mapping breaks — we don't know which parameter each spread element maps to.

**Strategy:**

- **Non-spread arguments before the first spread:** Check types normally against the corresponding parameter positions (position is still known).
- **Spread arguments and all arguments after a spread:** Skip per-argument type checking. The mapping is indeterminate.

```stash
fn process(name: string, count: int, flag: bool) { }
let args = [42, true];

process(123, ...args);
//      ~~~
// ⚠ Warning: Argument 'name' expects type 'string' but got 'int'.
// (The first arg is non-spread, position is known — type check applies)
// (args[0]→count and args[1]→flag are NOT checked — spread makes positions unknown)
```

This is conservative but correct. We report what we can prove and stay silent on the rest.

#### 7.6.4 Rest Parameter Diagnostics

**SA-REST-01: Unused rest parameter** (Information, IsUnnecessary)

Fires when: A rest parameter in a function declaration (or a rest binding in destructuring) is never referenced in the function body / subsequent scope.

```stash
fn process(a, ...rest) {  // ℹ 'rest' is declared but never used
    return a * 2;          //    ~~~~
}

let [head, ...tail] = getItems();  // ℹ 'tail' is declared but never used
io.println(head);                  //    ~~~~
```

| Field         | Value                                                                                                          |
| ------------- | -------------------------------------------------------------------------------------------------------------- |
| Message       | `"Rest parameter '...{name}' is declared but never used."` / `"Variable '{name}' is declared but never used."` |
| Level         | `Information`                                                                                                  |
| Span          | The rest parameter/binding name span                                                                           |
| IsUnnecessary | `true` (renders faded in editor)                                                                               |

This is handled by the existing `CheckUnusedSymbols` infrastructure — rest parameters are registered as symbols like any other parameter, so unused detection applies automatically. The only change is cosmetic: the message could use "Rest parameter" as the label instead of "Parameter" to be more specific. This is optional.

**SA-REST-02: Rest parameter type annotation mismatch**

Fires when: A rest parameter has a type annotation that is not a valid known type. Reuses the existing `ValidateTypeHint` logic.

```stash
fn log(level, ...messages: Frobnitz) { }
//                         ~~~~~~~~
// ⚠ Warning: Unknown type 'Frobnitz'.
```

No new diagnostic needed — the existing `ValidateTypeHint` machinery handles this. Just ensure the SymbolCollector records the rest parameter's type hint so the validator can check it.

#### 7.6.5 Informational Diagnostics (Style)

**SA-SPREAD-04: Redundant spread of literal array in function call**

Fires when: A `SpreadExpr` wraps an inline `ArrayExpr` (array literal) in a function call context. The spread is unnecessary — the elements could be passed directly as arguments.

```stash
fn add(a, b, c) { return a + b + c; }

add(...[1, 2, 3]);      // ℹ Unnecessary spread of array literal
//  ~~~~~~~~~~~~         //   — use add(1, 2, 3) instead
```

| Field         | Value                                                                                                    |
| ------------- | -------------------------------------------------------------------------------------------------------- |
| Message       | `"Unnecessary spread of array literal in function call. Pass the elements as direct arguments instead."` |
| Level         | `Information`                                                                                            |
| Span          | The `SpreadExpr` span                                                                                    |
| IsUnnecessary | `true`                                                                                                   |

Does NOT fire when:

- The spread is inside an array literal `[...[1, 2]]` — this might be intentional for flattening or readability.
- The spread is in a dict literal — not applicable (arrays can't spread into dicts).

> **Note:** This is a low-priority style hint. It's useful but should not block implementation. It can be added in a follow-up pass.

**SA-SPREAD-05: Empty spread**

Fires when: A `SpreadExpr` wraps an inline empty `ArrayExpr` (`...[]`) in any context, or an inline empty `DictLiteralExpr` (`...{}`) in a dict literal context. The spread is a guaranteed no-op.

```stash
let result = [1, ...[], 2];    // ℹ Spreading empty array literal has no effect
//               ~~~~~

let config = { ...{}, a: 1 };  // ℹ Spreading empty dictionary literal has no effect
//             ~~~~~
```

| Field         | Value                                                             |
| ------------- | ----------------------------------------------------------------- |
| Message       | `"Spreading an empty {array\|dictionary} literal has no effect."` |
| Level         | `Information`                                                     |
| Span          | The `SpreadExpr` span                                             |
| IsUnnecessary | `true`                                                            |

#### 7.6.6 Diagnostic Summary Table

| Code         | Context                      | Level       | Message Pattern                                                            | When                                             |
| ------------ | ---------------------------- | ----------- | -------------------------------------------------------------------------- | ------------------------------------------------ |
| SA-SPREAD-01 | Array literal, call args     | Warning     | `"Spread argument has type '{type}', expected 'array'."`                   | Inferred type ≠ array                            |
| SA-SPREAD-02 | Dict literal                 | Warning     | `"Spread argument has type '{type}', expected 'dict' or struct instance."` | Inferred type ≠ dict/struct                      |
| SA-SPREAD-03 | Any spread context           | Warning     | `"Spreading 'null' will always fail at runtime."`                          | Inner expr is literal null                       |
| SA-ARITY-01  | Function call                | Error       | `"At least {min} arguments provided but '{fn}' expects at most {max}."`    | Non-spread args > max arity                      |
| SA-REST-01   | Function decl, destructuring | Information | `"Rest parameter '...{name}' is declared but never used."`                 | No references to rest binding                    |
| SA-REST-02   | Function decl                | Warning     | `"Unknown type '{type}'."`                                                 | Invalid type hint on rest param (existing infra) |
| SA-SPREAD-04 | Function call                | Information | `"Unnecessary spread of array literal in function call."`                  | `f(...[1,2,3])`                                  |
| SA-SPREAD-05 | Array/dict literal, call     | Information | `"Spreading an empty {type} literal has no effect."`                       | `...[]` or `...{}`                               |

#### 7.6.7 What We Deliberately Do NOT Diagnose

These cases were considered and rejected for static analysis:

1. **"Spread may produce wrong argument count"** — When the spread target is a variable (not a literal), we can't know its length. Emitting a "might fail at runtime" warning would be noisy and unhelpful. The user chose to spread — they know the count is dynamic.

2. **"Spreading into a non-variadic function"** — Spreading into a fixed-arity function is legitimate (e.g., `add(...pair)` where `pair` always has 2 elements). Warning on every spread-into-fixed-arity call would produce false positives on correct code.

3. **"Dict spread may overwrite keys"** — `{ ...a, ...b }` where `a` and `b` may share keys. This is intentional behavior (last-wins semantics) and a core use case (config override patterns). Warning would hurt valid code.

4. **"Rest parameter shadows outer variable"** — Stash does not warn on shadowing in general. Adding it only for rest parameters would be inconsistent. This is a future consideration for a general shadowing analysis pass.

5. **"Array destructuring rest produces copy"** — `let [a, ...rest] = arr` creates a new array. We don't warn about this because copy-on-destructure is expected behavior, and a "did you know this copies?" hint would be noisy.

---

## 8. Cross-Platform Considerations

No cross-platform issues. Spread/rest is a pure language feature with no OS-specific behavior. All operations (array creation, dict creation, argument passing) are platform-independent .NET operations.

---

## 9. LSP / DAP / Tooling Impact

### LSP

| Feature             | Impact                                                                                                                                                                                                    |
| ------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Semantic tokens** | `...` should get a dedicated token type (operator). Rest parameter names should still be tokenized as parameters.                                                                                         |
| **Signature help**  | Must display rest parameters with `...` prefix in signatures: `fn log(level, ...messages)`                                                                                                                |
| **Hover**           | Hovering over a rest parameter should show its array type: `(rest parameter) ...messages: array`                                                                                                          |
| **Completions**     | Inside `...expr`, completions should suggest array-typed variables (in call/array context) or dict-typed variables (in dict literal context). At minimum, do not filter completions — show all variables. |
| **Diagnostics**     | Forward parser errors for invalid rest positions.                                                                                                                                                         |

### DAP

| Feature                   | Impact                                                                                                                                    |
| ------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| **Variables view**        | Rest parameters should display as arrays in the variables pane: `messages = [3 items]` — no special handling needed, they're just arrays. |
| **Expression evaluation** | Spread in watch expressions: `[...arr, 4]` should work in the debug evaluator.                                                            |

### VS Code Extension

| Feature                            | Impact                                                                        |
| ---------------------------------- | ----------------------------------------------------------------------------- |
| **TextMate grammar**               | Add `...` as an operator token. Match `...identifier` in parameter positions. |
| **Monarch tokenizer** (Playground) | Same as TextMate — recognize `...` as operator.                               |

### Static Analysis

| Feature                       | Impact                                                                                                                                 |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| **Resolver**                  | Walk `SpreadExpr` inner expressions. Validate rest parameter constraints. Register rest bindings with correct type hints.              |
| **Unused variable detection** | Rest parameters and rest destructuring bindings checked for usage automatically via existing `CheckUnusedSymbols` (SA-REST-01).        |
| **Type mismatch warnings**    | Spreading non-array in array/call context (SA-SPREAD-01), non-dict/struct in dict context (SA-SPREAD-02), null literal (SA-SPREAD-03). |
| **Arity checking**            | Adjusted logic: skip exact count when spread is present; error only when non-spread args exceed max arity (SA-ARITY-01).               |
| **Style hints**               | Unnecessary spread of array literal `f(...[1,2])` (SA-SPREAD-04), empty spread `...[]` (SA-SPREAD-05). Low priority — can be deferred. |

Full diagnostic catalog with message text, severity levels, trigger conditions, and rationale for omitted diagnostics: see **§7.6 Static Analysis Diagnostics**.

---

## 10. Test Scenarios

### Rest Parameters

| #   | Test                                                                       | Expected            |
| --- | -------------------------------------------------------------------------- | ------------------- |
| 1   | `fn f(...args) { return args; } f(1, 2, 3);`                               | `[1, 2, 3]`         |
| 2   | `fn f(...args) { return args; } f();`                                      | `[]`                |
| 3   | `fn f(a, ...rest) { return rest; } f(1, 2, 3);`                            | `[2, 3]`            |
| 4   | `fn f(a, ...rest) { return rest; } f(1);`                                  | `[]`                |
| 5   | `fn f(a, ...rest) { } f();`                                                | RuntimeError: arity |
| 6   | `fn f(a, b = 5, ...rest) { return [a, b, rest]; } f(1);`                   | `[1, 5, []]`        |
| 7   | `fn f(a, b = 5, ...rest) { return [a, b, rest]; } f(1, 2, 3, 4);`          | `[1, 2, [3, 4]]`    |
| 8   | Lambda: `let f = (...args) => args; f(1, 2);`                              | `[1, 2]`            |
| 9   | Lambda: `let f = (a, ...rest) => rest; f(1, 2, 3);`                        | `[2, 3]`            |
| 10  | Rest with type hint: `fn f(...args: string) { return args; } f("a", "b");` | `["a", "b"]`        |

### Rest Parameter Errors

| #   | Test                   | Expected                       |
| --- | ---------------------- | ------------------------------ |
| 11  | `fn f(...a, b) { }`    | ParseError: rest must be last  |
| 12  | `fn f(...a, ...b) { }` | ParseError: only one rest      |
| 13  | `fn f(...a = []) { }`  | ParseError: no default on rest |

### Spread in Calls

| #   | Test                                                               | Expected                              |
| --- | ------------------------------------------------------------------ | ------------------------------------- |
| 14  | `fn f(a, b) { return a + b; } let args = [1, 2]; f(...args);`      | `3`                                   |
| 15  | `fn f(a, b, c) { } let args = [1, 2]; f(...args, 3);`              | Works                                 |
| 16  | `fn f(a, b, c) { } f(...[1], ...[2, 3]);`                          | Works                                 |
| 17  | `fn f(a, b) { } f(...[1, 2, 3]);`                                  | RuntimeError: arity                   |
| 18  | `fn f(a) { } f(...5);`                                             | RuntimeError: cannot spread non-array |
| 19  | `fn f(...args) { return args; } let a = [1, 2]; f(...a, 3, ...a);` | `[1, 2, 3, 1, 2]`                     |

### Spread in Array Literals

| #   | Test                          | Expected                              |
| --- | ----------------------------- | ------------------------------------- |
| 20  | `let a = [1, 2]; [...a, 3];`  | `[1, 2, 3]`                           |
| 21  | `[...[1, 2], ...[3, 4]];`     | `[1, 2, 3, 4]`                        |
| 22  | `let e = []; [1, ...e, 2];`   | `[1, 2]`                              |
| 23  | `let n = [[1], [2]]; [...n];` | `[[1], [2]]` (shallow)                |
| 24  | `[...5];`                     | RuntimeError                          |
| 25  | `[..."abc"];`                 | RuntimeError (strings not spreadable) |
| 26  | `[...null];`                  | RuntimeError                          |

### Spread in Dict Literals

| #   | Test                                                                | Expected                   |
| --- | ------------------------------------------------------------------- | -------------------------- |
| 27  | `let d = { a: 1 }; { ...d, b: 2 };`                                 | `{ a: 1, b: 2 }`           |
| 28  | `{ ...{ a: 1 }, ...{ a: 2 } };`                                     | `{ a: 2 }` (last wins)     |
| 29  | `{ ...{ a: 1 }, a: 2 };`                                            | `{ a: 2 }` (explicit wins) |
| 30  | `{ a: 1, ...{ a: 2 } };`                                            | `{ a: 2 }` (spread wins)   |
| 31  | `let e = {}; { ...e, k: "v" };`                                     | `{ k: "v" }`               |
| 32  | Spread struct: `struct S { x } let s = S { x: 1 }; { ...s, y: 2 };` | `{ x: 1, y: 2 }`           |
| 33  | `{ ...5 };`                                                         | RuntimeError               |
| 34  | `{ ...[1, 2] };`                                                    | RuntimeError               |
| 35  | `{ ...null };`                                                      | RuntimeError               |

### Rest in Array Destructuring

| #   | Test                                 | Expected                      |
| --- | ------------------------------------ | ----------------------------- |
| 36  | `let [a, ...rest] = [1, 2, 3];`      | `a=1, rest=[2,3]`             |
| 37  | `let [a, ...rest] = [1];`            | `a=1, rest=[]`                |
| 38  | `let [...all] = [1, 2, 3];`          | `all=[1,2,3]`                 |
| 39  | `let [a, b, ...rest] = [1];`         | `a=1, b=null, rest=[]`        |
| 40  | `const [a, ...rest] = [1, 2]; rest;` | `rest=[2]` (const)            |
| 41  | `let [...a, b] = [1, 2];`            | ParseError: rest must be last |

### Rest in Object Destructuring

| #   | Test                                                                       | Expected                      |
| --- | -------------------------------------------------------------------------- | ----------------------------- |
| 42  | `let { a, ...rest } = { a: 1, b: 2, c: 3 };`                               | `a=1, rest={b:2,c:3}`         |
| 43  | `let { a, ...rest } = { a: 1 };`                                           | `a=1, rest={}`                |
| 44  | `let { ...all } = { x: 1, y: 2 };`                                         | `all={x:1,y:2}`               |
| 45  | Struct: `struct S { x, y } let s = S { x: 1, y: 2 }; let { x, ...r } = s;` | `x=1, r={y:2}`                |
| 46  | `let { ...a, b } = { a: 1, b: 2 };`                                        | ParseError: rest must be last |

### Interaction Tests

| #   | Test                                                                                                     | Expected    |
| --- | -------------------------------------------------------------------------------------------------------- | ----------- |
| 47  | UFCS with spread: `let args = [", "]; [1, 2, 3].join(...args);`                                          | `"1, 2, 3"` |
| 48  | Nested rest: `fn outer(...args) { fn inner(...xs) { return xs; } return inner(...args); } outer(1,2,3);` | `[1,2,3]`   |
| 49  | Spread result of function call: `fn get() { return [1,2,3]; } [...get(), 4];`                            | `[1,2,3,4]` |
| 50  | Rest in async: `async fn f(...args) { return args; } task.await(f(1,2));`                                | `[1,2]`     |

### Static Analysis Diagnostics

| #   | Test                                                           | Expected Diagnostic                                                  |
| --- | -------------------------------------------------------------- | -------------------------------------------------------------------- |
| 51  | `let x: int = 5; [...x];`                                      | Warning: SA-SPREAD-01 — type 'int', expected 'array'                 |
| 52  | `let s: string = "hi"; [...s];`                                | Warning: SA-SPREAD-01 — type 'string', expected 'array'              |
| 53  | `let d: dict = {}; fn f(a) {} f(...d);`                        | Warning: SA-SPREAD-01 — type 'dict', expected 'array'                |
| 54  | `let x: int = 5; { ...x };`                                    | Warning: SA-SPREAD-02 — type 'int', expected 'dict' or struct        |
| 55  | `let a: array = []; { ...a };`                                 | Warning: SA-SPREAD-02 — type 'array', expected 'dict' or struct      |
| 56  | `[...null];`                                                   | Warning: SA-SPREAD-03 — spreading null                               |
| 57  | `{ ...null };`                                                 | Warning: SA-SPREAD-03 — spreading null                               |
| 58  | `fn f(a) {} f(...null);`                                       | Warning: SA-SPREAD-03 — spreading null                               |
| 59  | `fn f(a, b) {} let x = [1]; f(1, 2, 3, ...x);`                 | Error: SA-ARITY-01 — at least 3 args, expects at most 2              |
| 60  | `fn f(a, b) {} let x = [1]; f(...x);`                          | No diagnostic (spread count unknown)                                 |
| 61  | `fn f(a, b) {} f(1, ...getArgs());`                            | No diagnostic (spread count unknown)                                 |
| 62  | `fn f(a, ...rest) {} f(1, 2, ...getArgs());`                   | No diagnostic (rest absorbs excess)                                  |
| 63  | `fn f(a, ...rest) { return a; }`                               | Information: SA-REST-01 — rest unused (faded)                        |
| 64  | `let [head, ...tail] = [1, 2, 3]; io.println(head);`           | Information: SA-REST-01 — tail unused (faded)                        |
| 65  | `fn f(a, b) {} f(...[1, 2, 3]);`                               | Information: SA-SPREAD-04 — unnecessary spread of literal            |
| 66  | `let r = [1, ...[], 2];`                                       | Information: SA-SPREAD-05 — empty spread has no effect               |
| 67  | `let r = { ...{}, a: 1 };`                                     | Information: SA-SPREAD-05 — empty spread has no effect               |
| 68  | `fn f(...args: Frobnitz) {}`                                   | Warning: unknown type 'Frobnitz' (existing SA-REST-02)               |
| 69  | `let a: array = [1]; [...a, 3];`                               | No diagnostic (valid spread of array)                                |
| 70  | `let d: dict = { a: 1 }; { ...d, b: 2 };`                      | No diagnostic (valid spread of dict)                                 |
| 71  | `struct S { x } let s = S { x: 1 }; { ...s };`                 | No diagnostic (valid spread of struct into dict)                     |
| 72  | `fn f(name: string, count: int) {} let x = [1]; f(123, ...x);` | Warning: arg 'name' expects 'string' got 'int' (pre-spread arg only) |

---

## 11. Decision Log

### D1: Token syntax — `...` (three dots)

- **Decision:** Use `...` as the spread/rest operator.
- **Alternatives:**
  - `*` / `**` (Python/Ruby style) — rejected. `*` is already multiplication. `**` is unused, but inconsistent with Stash's C-family syntax heritage.
  - Keyword like `spread` — rejected. Verbose, unfamiliar.
- **Rationale:** `...` is the standard in C-family languages (JS, TS, PHP, Kotlin, Dart). It's what Stash developers will expect. No token conflicts.

### D2: Strings are NOT spreadable

- **Decision:** `[..."hello"]` is a runtime error, not `["h","e","l","l","o"]`.
- **Alternatives:**
  - Allow string spreading into character arrays (like JavaScript).
- **Rationale:** Stash strings are not iterable collections. Implicit character splitting is a footgun and rarely the intended behavior. Use `str.split(s, "")` explicitly. This keeps the type contract clear: arrays spread in array contexts, dicts/structs spread in dict contexts.

### D3: Structs ARE spreadable in dict literals

- **Decision:** `{ ...structInstance }` flattens struct fields into dict entries.
- **Alternatives:**
  - Only allow dicts to be spread in dict literals — require `dict.fromStruct()` for structs.
- **Rationale:** Structs and dicts are both key-value containers. Spreading a struct into a dict is a natural serialization pattern. The implementation is trivial since struct instances already expose field enumeration. The result is always a dict — no ambiguity about the output type.

### D4: Rest param comes after default params

- **Decision:** `fn f(a, b = 5, ...rest)` is valid. Arguments bind left-to-right: required, default, rest.
- **Alternatives:**
  - Require rest params only after required params (no mixing with defaults).
- **Rationale:** JavaScript allows this and the semantics are clear and useful. A function like `fn format(template, separator = ", ", ...values)` is a natural API shape. The binding order follows logically from the existing left-to-right parameter binding in `UserCallable.BindParameters`.

### D5: Spread expansion happens before arity checking

- **Decision:** All spread expressions are evaluated and flattened into the argument list before the callee's arity is checked.
- **Alternatives:**
  - Check arity before expansion (impossible — we don't know how many elements each spread produces until runtime).
- **Rationale:** This is the only workable approach and matches every language that has spread operators. Arity errors after spread report the actual expanded count: "Expected 2 arguments but got 5."
