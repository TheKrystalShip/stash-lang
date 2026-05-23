# Runtime Errors — Stack Traces, Source Context, and Typed Catch

**Status:** Backlog — Design
**Created:** 2026-04-26
**Category:** VM + Language + CLI
**Priority:** Pre-v1.0 Critical
**Complexity:** Large (3 self-contained levels; implement as one release)

---

## 1. Motivation

Stash's current unhandled runtime error output:

```
[runtime error at deploy.stash:42:15] Division by zero.
```

One line. No call chain. In a sysadmin scripting language where scripts call functions that call other functions across multiple files, this is genuinely painful. The developer sees where the error happened but has no idea how they got there.

This spec covers three levels of improvement that collectively bring Stash to industry parity:

| Level | Name                   | Impact                                                       |
| ----- | ---------------------- | ------------------------------------------------------------ |
| 1     | Call stack capture     | Critical — without this, all other improvements are cosmetic |
| 2     | Source context display | High — Python 3.11's biggest UX win                          |
| 3     | Typed throw/catch      | High — ergonomic and composable error handling               |

All three ship as a single release. Level 3 in particular changes the AST, parser, and compiler in ways that interact with stack capture, so they must be designed together.

---

## 2. Current State (Verified)

### 2.1 Error types on the C# side

**`RuntimeError` (C# exception)** — `Stash.Core/Runtime/RuntimeError.cs`

- `Message` — inherited from `Exception`
- `SourceSpan? Span` — nullable; carries `File`, `StartLine`, `StartColumn`
- `string? ErrorType` — custom type string (`"DeployError"`, `"TypeError"`, etc.) or `null`
- `Dictionary<string, object?>? Properties` — extra data (exit code, stderr, stdout for `CommandError`)
- `List<StashError>? SuppressedErrors` — errors from deferred cleanup
- **No call stack field** — does not exist today

**`AssertionError : RuntimeError`** — `Stash.Core/Runtime/AssertionError.cs`

- Adds `Expected` and `Actual` fields; thrown by `assert.*` builtins

### 2.2 Error types on the Stash side

**`StashError` (first-class Stash value)** — `Stash.Core/Runtime/Types/StashError.cs`

- `string Message`
- `string Type` — defaults to `"RuntimeError"`
- `List<string>? Stack` — **always `null`** for errors caught in Stash code today
- `Dictionary<string, object?>? Properties`
- `List<StashError>? Suppressed`
- Accessible from Stash code: `e.message`, `e.type`, `e.stack` (returns `[]` today), `e.suppressed`

### 2.3 Try/catch implementation

**AST** — `Stash.Core/Parsing/AST/TryCatchStmt.cs`

```csharp
public Token? CatchKeyword { get; }
public Token? CatchVariable { get; }   // single identifier, no type
public BlockStmt? CatchBody { get; }
public Token? FinallyKeyword { get; }
public BlockStmt? FinallyBody { get; }
```

**Parser** — `Stash.Core/Parsing/Parser.cs` ~L1101

```csharp
Consume(TokenType.LeftParen, "Expected '(' after 'catch'.");
catchVariable = Consume(TokenType.Identifier, "Expected variable name in 'catch'.");
Consume(TokenType.RightParen, "Expected ')' after catch variable.");
```

Current syntax: `catch(e)` — exactly one bare identifier. No type annotation.

**VM dispatch** — `Stash.Bytecode/VM/VirtualMachine.Dispatch.cs`

```csharp
catch (RuntimeError ex) when (_exceptionHandlers.Count > 0)
{
    // ...
    var stashError = new StashError(ex.Message, ex.ErrorType ?? "RuntimeError", null, ex.Properties);
    _stack[handlerFrame.BaseSlot + handler.ErrorReg] = StashValue.FromObj(stashError);
    handlerFrame.IP = handler.CatchIP;
}
```

**No type-based matching.** Every RuntimeError goes to the nearest handler.

**Source span** — `Stash.Bytecode/Bytecode/SourceMap.cs`
A `GetCurrentSpan(offset)` binary-search method already exists. The VM already calls `GetCurrentSpan()` when attaching span to RuntimeErrors from VM-level operations.

### 2.4 Built-in error messages

All builtins throw `new RuntimeError(message)` with no `ErrorType` specified (defaults to `"RuntimeError"`). Examples across namespaces:

- `arr`: `"Cannot pop from an empty array."`, `"Index {idx} is out of bounds for 'arr.insert'."`
- `dict`: `"Dictionary key cannot be null."`
- `str`: `"'str.trim' requires 1 or 2 arguments."`
- `math`: `"First argument to 'math.abs' must be a number."`

---

## 3. Level 1 — Call Stack Capture

### 3.1 What changes

The VM's `_frames` array and per-chunk `SourceMap`s already carry everything needed to produce a call stack. The information just isn't captured or stored.

#### Decision: Capture location and timing

Call stack is captured in two places:

1. **At the try/catch boundary** (when `RuntimeError` is caught in `VirtualMachine.Dispatch.cs`'s catch filter) — stack is captured and attached to the `StashError` so Stash code can access `e.stack`.

2. **At the unhandled boundary** (when `RuntimeError` propagates out of the top-level `RunInner()` call) — stack is captured and attached to the `RuntimeError` itself for CLI display.

Both capture the same way: walk `_frames[0.._frameCount]` from innermost to outermost, call `GetCurrentSpan(frame.IP - 1)` for each, and collect `(FunctionName, SourceSpan)` pairs.

**Alternative considered:** Capture the stack at the point of `throw` rather than at the handler boundary (like Node.js does via `Error.captureStackTrace`). Rejected because:

- In Stash, throw is compiled to an opcode (`Throw` = 58), and attaching stack capture to every throw site adds overhead to the hot path.
- Capturing at the handler boundary is equally informative for most use cases — the stack between `throw` and `handler` is the same stack that the developer sees.
- Exceptions that unwind through C# code (e.g., builtins) lose their intermediate C# frames, but the Stash-level frames are preserved correctly.

#### Decision: Stack frame representation

A `StackFrame` record carries the minimum needed for display:

```csharp
// Stash.Core/Runtime/
public record StackFrame(string FunctionName, SourceSpan Span);
```

Function name rules:

- Named function: the function's name from its declaration
- Anonymous function / closure: `<lambda>`
- Module top-level: `<main>`
- Block in do/while or try/catch: the enclosing function name

#### RuntimeError changes

Add one field to `RuntimeError`:

```csharp
public List<StackFrame>? CallStack { get; set; }
```

This is set by the VM — not by the `throw` site.

#### StashError.Stack population

At the try/catch boundary, before the `StashError` is placed into the catch variable register, populate `.Stack`:

```csharp
stashError.Stack = ex.CallStack?
    .Select(f => $"  at {f.FunctionName} ({f.Span.File}:{f.Span.StartLine}:{f.Span.StartColumn})")
    .ToList();
```

`e.stack` in Stash code is now a `list<string>` of formatted frame strings. This matches JavaScript's `Error.prototype.stack` pattern closely enough that users find it familiar.

### 3.2 Frame depth limit

Cap at **50 frames** in the captured list. If the actual depth exceeds 50, append a final entry: `"  ... N more frames omitted"`. Rationale: deeply recursive Stash code shouldn't produce megabyte-sized error messages. Python defaults to 1000; 50 is conservative but appropriate for a sysadmin scripting language.

### 3.3 CLI rendering — new format

Replace the current `PrintRuntimeError` format:

**Before:**

```
[runtime error at deploy.stash:42:15] Division by zero.
```

**After (Level 1 only, without source context):**

```
RuntimeError: Division by zero.
  at divide (math.stash:8:10)
  at compute (math.stash:15:3)
  at <main> (deploy.stash:42:1)
```

Rules:

- First line: `ErrorType: Message` (ErrorType defaults to `RuntimeError`)
- Then one line per stack frame in innermost-first order
- If `CallStack` is null or empty: fallback to `[runtime error at file:line:col] message` for graceful degradation

---

## 4. Level 2 — Source Context Display

### 4.1 Strategy

**Strategy A — Re-read source file at display time.** The CLI reads the source file when rendering the error output. No .stashc format changes required. Fails gracefully (silently omits context) if the file is not accessible — compiled/distributed binaries behave identically to today minus the source lines.

**Strategy B — Embed source in .stashc chunk** (optional compiler flag, post-v1.0). Not in scope for this spec.

### 4.2 Display format — innermost frame only

Source context is shown **only for the innermost (first) stack frame**. Subsequent frames show as plain `at name (file:line:col)` lines. Showing source for every frame in a 10-frame stack is overwhelming — Python's approach of showing all frames works because it's a well-known convention; Stash should lead with concise.

Format (3 lines context + caret on the error column):

```
RuntimeError: Division by zero.
  at divide (math.stash:8:10)
     7 |   fn divide(a, b) {
  >  8 |       return a / b;
     9 |   }
                    ^
  at compute (math.stash:15:3)
  at <main> (deploy.stash:42:1)
```

Formatting rules:

- Line numbers are right-padded to the same width (based on the widest number shown)
- The `>` marker appears on the error line only
- The caret `^` appears on a separate line, aligned to `StartColumn` of the span
- If the error span has a length > 1 (e.g., a binary expression), use `^^^` to cover the range; default to `^` for single-column spans
- If the column is 0 (unknown), omit the caret line
- Read lines (N-1), N, and (N+1) where N is `StartLine`; omit out-of-bounds lines gracefully

### 4.3 File caching

The CLI should cache file contents by absolute path for the duration of a single error render. A recursive call to a function in the same file would otherwise re-read the same file for each frame; with caching the file is read at most once. A `Dictionary<string, string[]?>` mapping path → lines (or `null` if unreadable) is sufficient.

### 4.4 Graceful fallback when file is unavailable

If the source file cannot be read (does not exist, insufficient permissions, or the span's `File` is `null` / `"<stdin>"`), omit the source context block entirely for that frame. The stack trace still renders. No error is printed about the missing file.

For `<stdin>` in the REPL: show the source context only if the line number matches the current input line (i.e., if the error is in the immediately executed expression). This is a nice-to-have; a simpler first implementation can skip source context for stdin entirely.

---

## 5. Level 3 — Typed Throw/Catch

### 5.1 Custom error types — structs as errors

**Industry consensus:** Seven of eight major scripting/systems languages use their existing type system to define error types (Python: subclass `Exception`; Swift: conform struct to `Error` protocol; Go: implement `error` interface; Kotlin: extend `Exception`; Rust: implement `Error` trait). Only Zig uses a dedicated keyword — and Zig's error sets are deliberately payload-free for systems-level allocation constraints that don't apply to Stash.

**Decision: Any struct can be thrown.** No marker interface, no special declaration keyword, no class hierarchy. Catch matching uses the struct's type name string.

```stash
struct DeployError { service: string, code: int }

fn rollback(svc: string) {
    throw DeployError { service: svc, code: 503 };
}

try {
    rollback("api");
} catch (DeployError e) {
    print("Service " + e.service + " failed with " + e.code);
}
```

**Alternative considered — `error` keyword (Zig-style):** Rejected. Adds a new keyword and AST node for no semantic gain over structs. Stash already has structs; teaching users two ways to define structured data is unnecessary complexity.

**Alternative considered — string convention (`catch ("DeployError" e)`):** Rejected. String-based matching is untyped, provides no tooling support (hover, go-to-definition, rename), and is universally considered a code smell in modern language design.

### 5.2 Throw — what can be thrown

`throw` accepts any Stash value. The type matching in `catch` uses:

| Thrown value            | `e.type` in catch             | Type name for matching            |
| ----------------------- | ----------------------------- | --------------------------------- |
| Struct instance         | `"DeployError"`               | The struct's declared type name   |
| Dict with `type` field  | Value of `type` field         | e.g., `"MyError"`                 |
| String                  | `"RuntimeError"`              | Not matchable by named type       |
| Number / other          | `"RuntimeError"`              | Not matchable by named type       |
| VM/builtin RuntimeError | `ErrorType ?? "RuntimeError"` | Standard type taxonomy (see §5.6) |

The existing `throw expr` syntax is unchanged. `throw DeployError { service: "api" }` is parsed as `throw <struct-literal-expression>` — no new syntax required here.

### 5.3 Bare rethrow — `throw;`

`throw;` with no argument, used inside a catch block, re-throws the original exception preserving its original source span and call stack. This is identical to C#'s `throw;` semantics.

```stash
try {
    riskyOp();
} catch (DeployError e) {
    logError(e);
    throw;   // re-throw DeployError with original span preserved
}
```

Implementation: the compiler resolves `throw;` inside a catch body to a `Rethrow` opcode (see §7.2) which re-raises the stored original `RuntimeError` rather than constructing a new one from a `StashError`. This is critical for preserving span and call stack.

**Semantic rule:** `throw;` outside a catch body is a compile-time error (SA diagnostic, see §9).

### 5.4 Catch clause syntax

```stash
try {
    ...
}
catch (TypeA e) {
    ...
}
catch (TypeB | TypeC e) {
    ...
}
catch (e) {
    ...    // untyped catch-all
}
```

Rules:

- Zero or more typed catch clauses, each matching one or more types via `|`
- At most one untyped catch-all (`catch (e)`) — must be last
- `catch (Error e)` is equivalent to `catch (e)` — `Error` is the built-in catch-all type name (see §5.5)
- Order matters: clauses are evaluated top-to-bottom; first match wins
- Multiple catch clauses are allowed (unlike current single-clause design)
- `finally` is unchanged and still valid with any combination

Grammar sketch:

```
try_stmt     ::= "try" block catch_clauses? finally_clause?
catch_clauses ::= catch_clause+
catch_clause  ::= "catch" "(" catch_spec ")" block
catch_spec    ::= identifier                         // untyped catch-all: catch (e)
               |  type_list identifier               // typed: catch (TypeA e)
type_list    ::= identifier ("|" identifier)*        // union: TypeA | TypeB
finally_clause ::= "finally" block

throw_stmt   ::= "throw" expr? ";"                  // expr? = absent means bare rethrow
```

**`catch (e)` disambiguation:** The parser determines whether a catch clause is typed or untyped by looking ahead after the opening `(`. If only one identifier is present before `)`, it is untyped. If there is a second identifier (or `|`), the first token(s) are type names.

### 5.5 The `Error` built-in type name

`Error` is a reserved built-in identifier (like `null`, `true`, `false`) that matches any error in a catch clause. It is **not** a constructable struct or type — it cannot appear on the right side of `throw` or in `typeof`/`is` checks. It is only meaningful as a catch type specifier.

`catch (Error e)` and `catch (e)` are semantically identical. The reason to support both:

- `catch (e)` maintains full backward compatibility — existing code is unaffected
- `catch (Error e)` is consistent with the typed-catch mental model (all catch clauses have a type)
- Library authors who want explicit documentation that they're catching everything should prefer `catch (Error e)`

### 5.6 Built-in error type taxonomy

Standard error type strings that stdlib functions use. These replace the current uniform `"RuntimeError"` ErrorType for more precise matching.

| Type String         | Meaning                                 | Example usages                                                           |
| ------------------- | --------------------------------------- | ------------------------------------------------------------------------ |
| `RuntimeError`      | Default / uncategorized (unchanged)     | VM undefined variable, division by zero                                  |
| `TypeError`         | Wrong type passed to a function         | `arr.join` given non-string separator                                    |
| `ValueError`        | Valid type, invalid value               | `math.sqrt` given negative number, empty string where non-empty required |
| `IndexError`        | Array or string index out of bounds     | `arr.get(99)` on a 3-element array                                       |
| `KeyError`          | Dictionary key not found                | `dict.get` on missing key (when strict)                                  |
| `IOError`           | File system or network I/O failure      | `fs.read`, `net.tcpConnectAsync`, `http.*`                               |
| `ParseError`        | Parsing failure (JSON, XML, TOML, etc.) | `json.parse`, `xml.parse`, `toml.parse`                                  |
| `AssertionError`    | Assertion failure from `assert.*`       | Already distinct C# class                                                |
| `CommandError`      | Shell command non-zero exit             | `$()` with failing command                                               |
| `NotSupportedError` | Platform-unsupported operation          | Windows-only / Linux-only features                                       |
| `TimeoutError`      | Network or async timeout                | `http.get` with timeout exceeded                                         |

**Migration:** Assigning `ErrorType` strings to existing builtins is a non-breaking additive change. Existing code using `catch (e)` and inspecting `e.type == "RuntimeError"` continues to work. Code that switches on `e.type` will benefit from the new granularity. Builtins should be updated in a single pass during this implementation.

**Implementation note:** Builtin functions currently throw `new RuntimeError(message)`. They should be updated to `new RuntimeError(message, errorType: "TypeError")` (etc.) using the new taxonomy. This is a large but mechanical update across all `BuiltIns/*.cs` files.

---

## 6. AST Changes

### 6.1 `TryCatchStmt` — before vs. after

**Before:**

```csharp
public Token? CatchKeyword { get; }
public Token? CatchVariable { get; }   // single Token
public BlockStmt? CatchBody { get; }
public Token? FinallyKeyword { get; }
public BlockStmt? FinallyBody { get; }
```

**After:**

```csharp
public IReadOnlyList<CatchClause> CatchClauses { get; }   // replaces CatchKeyword/CatchVariable/CatchBody
public Token? FinallyKeyword { get; }
public BlockStmt? FinallyBody { get; }
```

`CatchClauses` can be empty (try-finally only), or contain one or more clauses.

### 6.2 `CatchClause` — new type

```csharp
// Stash.Core/Parsing/AST/CatchClause.cs
public sealed class CatchClause
{
    public Token Keyword { get; }                   // the 'catch' token (for span)
    public IReadOnlyList<Token> TypeTokens { get; } // empty = untyped catch-all
    public Token Variable { get; }                  // the bound variable name
    public BlockStmt Body { get; }
}
```

`TypeTokens` is empty for `catch (e)` and `catch (Error e)`. It contains one or more identifier tokens for typed catch (`catch (TypeError e)`) and union catch (`catch (TypeError | ValueError e)`).

### 6.3 `ThrowStmt` — make Value nullable

**Before:**

```csharp
public Token Keyword { get; }
public Expr Value { get; }   // required
```

**After:**

```csharp
public Token Keyword { get; }
public Expr? Value { get; }  // null = bare rethrow
```

---

## 7. New Opcodes

Two new opcodes are required. They take the next available numbers after the current 93 opcodes.

### 7.1 `CatchMatch` (94)

Emitted at the entry of each typed catch clause. Checks whether the caught error matches one of the clause's type names. If not, jumps to the next clause (or to a rethrow if no clauses remain).

```
CatchMatch  A  B  C
```

- `A` — register holding the `StashError` (same as `TryBegin`'s error register)
- `B` — index into the chunk's constant pool; the constant is a `string[]` of type names for this clause (e.g., `["TypeError", "ValueError"]`)
- `C` — signed offset to jump if the error does NOT match (relative jump; target is the next clause's `CatchMatch` or the rethrow instruction)

Matching logic: the error is a match if:

1. `B` names the empty list (untyped catch-all or `Error` catch), OR
2. The `StashError.Type` string is contained in the list in `B`

### 7.2 `Rethrow` (95)

Re-throws the original `RuntimeError` that was caught by the nearest enclosing handler. Used to compile `throw;` (bare rethrow).

```
Rethrow  A
```

- `A` — register holding the `StashError` (the VM uses this to find the cached original `RuntimeError`)

Implementation note: the VM must cache the original `RuntimeError` in the `ExceptionHandler` struct when it catches it, so `Rethrow` can retrieve and re-raise it. This is essential for preserving the original span and call stack.

The `ExceptionHandler` struct (`VirtualMachine.ControlFlow.cs`) needs a new field:

```csharp
public RuntimeError? OriginalError { get; set; }
```

---

## 8. Compiler Changes — `Compiler.Exceptions.cs`

### 8.1 `VisitTryCatchStmt`

The compiler currently dispatches to four strategies based on which combination of catch/finally are present. This must be generalized:

**Multi-clause try/catch pattern:**

```
TryBegin  [error_reg]  [dispatch_ip_offset]
  ... try body ...
TryEnd
Jmp  [past_all_catches]

: dispatch_ip (entry for all catch clauses):
  CatchMatch  [error_reg]  [types_A]  [offset_to_clause_B_dispatch]
  ... clause A body ...
  Jmp  [past_all_catches]

  CatchMatch  [error_reg]  [types_B]  [offset_to_rethrow]
  ... clause B body ...
  Jmp  [past_all_catches]

  : rethrow:
  Rethrow  [error_reg]

: past_all_catches:
```

For an untyped catch-all as the last clause: the `CatchMatch` for that clause has an empty type list (always matches), so no `Rethrow` is emitted after it.

If there are no catch clauses (try-finally only): `TryBegin` points directly to a rethrow sequence that runs the finally body and then re-throws. This is the existing `CompileTryFinally` strategy.

### 8.2 Bare `throw;`

`throw;` compiles to `Rethrow [error_reg]` where `error_reg` is the error register of the immediately enclosing catch handler. The compiler must track this register in its context state.

`throw expr;` is unchanged — compiles to `Throw` opcode as before.

### 8.3 `finally` interaction

`finally` behavior is unchanged. The four compiler strategies become three with multi-clause support:

- try/catch (≥1 clause, no finally): multi-clause dispatch as above
- try/finally (no catch): existing strategy
- try/catch/finally (≥1 clause + finally): outer finally handler wraps the inner catch dispatch

---

## 9. Static Analysis Changes

### 9.1 New diagnostic rules

| Code   | Severity | Rule                                                              |
| ------ | -------- | ----------------------------------------------------------------- |
| SA0160 | Error    | Bare `throw;` used outside a catch block                          |
| SA0161 | Error    | Catch-all clause followed by unreachable typed catch clauses      |
| SA0162 | Warning  | Multiple catch-all clauses (second is unreachable)                |
| SA0163 | Warning  | Catching `RuntimeError` — consider catching a specific error type |

SA0161: catches the case where `catch (e)` or `catch (Error e)` appears before typed clauses, making the typed clauses dead code.

SA0163: advisory only — `catch (RuntimeError e)` is legitimate but the user may intend `catch (e)`.

### 9.2 Visitor updates

**SemanticResolver** (`Stash.Core/Resolution/SemanticResolver.cs`):

- Update `VisitTryCatchStmt` to iterate `CatchClauses` list
- Each clause opens a new scope, declares `Variable`, resolves `Body`, closes scope
- Track `IsInsideCatch` context flag to validate bare `throw;`
- Validate that `Error` is only used as a catch type specifier, not as a value expression

**SemanticValidator** (`Stash.Analysis/Visitors/SemanticValidator.cs`):

- Emit SA0160 for bare `throw;` outside catch
- Emit SA0161 if a catch-all clause precedes typed clauses
- Emit SA0162 for duplicate catch-all clauses

**SemanticTokenWalker** (`Stash.Analysis/Visitors/SemanticTokenWalker.cs`):

- Update `VisitTryCatchStmt` to walk `CatchClauses`
- Emit `variable.declaration` for each clause's `Variable`
- Emit `type` token for each type name in `TypeTokens` (enabling hover/go-to-definition on error type names in catch clauses)

**StashFormatter** (`Stash.Analysis/Visitors/StashFormatter.cs` and its `ControlFlowPrinter`):

- Update `PrintTryCatch` to render multiple catch clauses
- Format: each clause on its own line, union types joined with `|`

---

## 10. LSP Implications

### 10.1 Hover on catch type names

When the cursor is over `TypeError` in `catch (TypeError e)`, hover should show the error's documentation (if it's a struct type defined in user code: the struct's doc comment; if it's a built-in error type name: a brief description from the built-in taxonomy).

This requires the `SemanticTokenWalker` to emit tokens for type names in catch clauses (§9.2 above), enabling the hover handler to resolve them.

### 10.2 Go-to-definition on catch type names

If `DeployError` is a struct defined in user code and appears in `catch (DeployError e)`, go-to-definition should navigate to the struct declaration. This works naturally once the semantic token walker emits a `type` token for the type name.

### 10.3 Completion inside catch `( )`

After `catch (`, the completion provider should suggest:

- All struct types declared in scope
- The standard built-in error type names from §5.6
- `Error` for catch-all
- The catch variable identifier after type names (i.e., after a type name and optional `|`, suggest a new identifier)

This is a post-v1.0 polish item unless the LSP team has capacity.

---

## 11. Cross-Platform Considerations

- **Source file reading** for context display: use `File.ReadAllLines` with UTF-8 encoding; handle `FileNotFoundException` and `UnauthorizedAccessException` silently
- **File paths in stack frames**: normalize to forward slashes for display consistency across Windows/Linux/macOS — follow whatever convention the existing `SourceSpan.File` field uses
- **Stack frame function names**: the VM frame's function name comes from the `Chunk`'s name field, which is set by the compiler — ensure all platforms produce the same names
- **`Error` reserved identifier**: no platform-specific behavior; purely a parser/compiler concern

---

## 12. Impact Summary

| Component                       | File(s)                                             | Change Type                                                      |
| ------------------------------- | --------------------------------------------------- | ---------------------------------------------------------------- |
| `StackFrame` record             | `Stash.Core/Runtime/StackFrame.cs` (new)            | New file                                                         |
| `RuntimeError`                  | `Stash.Core/Runtime/RuntimeError.cs`                | Add `CallStack` field                                            |
| `StashError`                    | `Stash.Core/Runtime/Types/StashError.cs`            | Populate `.Stack` at catch boundary                              |
| `TryCatchStmt` AST              | `Stash.Core/Parsing/AST/TryCatchStmt.cs`            | Replace catch fields with `CatchClauses`                         |
| `CatchClause` AST               | `Stash.Core/Parsing/AST/CatchClause.cs` (new)       | New file                                                         |
| `ThrowStmt` AST                 | `Stash.Core/Parsing/AST/ThrowStmt.cs`               | Make `Value` nullable                                            |
| `OpCode`                        | `Stash.Bytecode/Bytecode/OpCode.cs`                 | Add `CatchMatch = 94`, `Rethrow = 95`                            |
| `ExceptionHandler`              | `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs`   | Add `OriginalError` field                                        |
| `VirtualMachine.Dispatch.cs`    | `Stash.Bytecode/VM/VirtualMachine.Dispatch.cs`      | Stack capture; populate `StashError.Stack`; cache original error |
| `VirtualMachine.ControlFlow.cs` | `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs`   | `CatchMatch` and `Rethrow` handlers                              |
| `Compiler.Exceptions.cs`        | `Stash.Bytecode/Compilation/Compiler.Exceptions.cs` | Multi-clause dispatch; bare `throw` → `Rethrow`                  |
| Parser                          | `Stash.Core/Parsing/Parser.cs`                      | Multi-clause catch; union type syntax; bare `throw`              |
| `SemanticResolver`              | `Stash.Core/Resolution/SemanticResolver.cs`         | Iterate `CatchClauses`; track `IsInsideCatch`                    |
| `SemanticValidator`             | `Stash.Analysis/Visitors/SemanticValidator.cs`      | SA0160–SA0163                                                    |
| `SemanticTokenWalker`           | `Stash.Analysis/Visitors/SemanticTokenWalker.cs`    | Tokens for catch type names                                      |
| `StashFormatter`                | `Stash.Analysis/Visitors/StashFormatter.cs`         | Multi-clause catch rendering                                     |
| CLI `Program.cs`                | `Stash.Cli/Program.cs`                              | Stack trace + source context rendering                           |
| All `BuiltIns/*.cs`             | `Stash.Stdlib/BuiltIns/`                            | Assign `ErrorType` from taxonomy table                           |
| Instruction Set Reference       | `docs/Bytecode VM — Instruction Set Reference.md`   | Document `CatchMatch`, `Rethrow`                                 |
| Language Specification          | `docs/Stash — Language Specification.md`            | Typed catch syntax, `throw;`, error taxonomy                     |
| Standard Library Reference      | `docs/Stash — Standard Library Reference.md`        | Document error types per namespace                               |

**Visitors that must be updated** (all 6 of the visitor pattern participants):

1. `Compiler` — `Compiler.Exceptions.cs`
2. `SemanticResolver` — iterate new `CatchClauses`
3. `SemanticValidator` — new SA diagnostics
4. `SymbolCollector` — visit `CatchClauses` (catch variable declares a symbol)
5. `SemanticTokenWalker` — type tokens in catch clauses
6. `StashFormatter` — multi-clause catch rendering

---

## 13. Test Scenarios

### 13.1 Stack trace tests

- Unhandled error in nested function call — assert correct frames in order
- Error in built-in called from user function — assert user frame appears
- Stack trace with anonymous closures — assert `<lambda>` appears
- Stack depth limit — assert truncation at 50 frames with message
- `e.stack` in Stash code (`catch (e)`) — assert non-empty list of frame strings

### 13.2 Source context tests

- Error with accessible source file — assert 3-line context + caret in output
- Error where source file does not exist — assert graceful fallback (no source context, stack trace intact)
- Error in REPL (`<stdin>`) — assert source context omitted or shown correctly
- Multi-file stack — assert source context shown only for innermost frame

### 13.3 Typed catch tests

- Struct thrown and caught by struct type name — happy path
- Struct thrown, non-matching catch then matching catch — correct clause fires
- Union catch `TypeError | ValueError` — both types matched
- Multiple catch clauses — only first matching clause fires
- Catch-all `catch (e)` after typed clauses — fallback fires correctly
- `catch (Error e)` as catch-all — equivalent to `catch (e)`
- Typed catch with no match — error propagates uncaught to caller
- Built-in error caught by type: `catch (IndexError e)` catches `arr.get` OOB
- Nested try/catch — inner typed catch, outer untyped catch-all

### 13.4 Bare rethrow tests

- `throw;` in catch — re-throws with original span preserved
- `throw;` preserves call stack (not a new stack starting at the rethrow site)
- `throw;` followed by code in same catch block — SA0161 unreachable warning or just works as expected?

### 13.5 Static analysis tests

- SA0160: `throw;` outside catch — error
- SA0161: catch-all before typed clause — error
- SA0162: duplicate catch-all — warning
- SA0163: catching `RuntimeError` — warning

---

## 14. Migration / Breaking Changes

### Breaking changes

**`e.stack` now returns a non-empty list for caught errors.**
Existing code that checks `if (e.stack == null)` or uses `e.stack` defensively is unaffected — `IVMFieldAccessible` already returns `[]` for null Stack. Code that relies on `e.stack` being an empty array and then does something meaningful with that assumption could behave differently. This is considered an acceptable break — the previous behavior (always-empty stack) was a bug, not a feature.

### Non-breaking changes

- `catch (e)` — untyped catch-all syntax unchanged; all existing code compiles
- `throw expr;` — unchanged
- `e.message`, `e.type`, `e.suppressed` — unchanged
- Dict-based error throwing (`throw { type: "...", message: "..." }`) — still works and still matchable by type string in catch
- All builtins updating to use `ErrorType` taxonomy — additive; existing code matching `"RuntimeError"` continues to work (builtins not yet updated keep `"RuntimeError"`)

---

## 15. Open Questions

**Q1 — Struct type validation at compile time**
When the user writes `catch (DeployError e)`, should the compiler verify at compile time that `DeployError` is a declared struct (or known built-in error type)?

- **Option A:** Runtime-only matching. Any string in a catch type list is accepted; if no error ever has that type, the clause simply never fires. Zero compile-time overhead.
- **Option B:** Compile-time resolution. The semantic resolver looks up `DeployError` in scope; if it's not a declared struct or known built-in error type name, emit a warning (not an error, since dynamic type names can arrive from other modules).

Answer: **Option B with warning-level severity.** Unresolved type names in catch clauses are almost certainly typos. A warning rather than an error avoids hard failures in dynamic scenarios where the struct is defined in an imported module the resolver can't fully see.

**Q2 — Error message format finalization**
The proposed format follows the `ErrorType: Message` convention (Python, Node, Ruby). Should the `[runtime error]` prefix be retired entirely, or kept as a parallel CLI flag (`--legacy-errors`) for scripts that parse the CLI output?

Answer: **Retire the old format.** The new format is strictly more informative. Scripts parsing `[runtime error]` are fragile in any case.

**Q3 — `AssertionError` and `Expected`/`Actual` fields**
`AssertionError` currently exposes `Expected` and `Actual` as C# fields. When caught as `catch (AssertionError e)` in Stash code, should `e.expected` and `e.actual` be accessible as properties?

Answer: **Yes** — add `Expected` and `Actual` to the `Properties` dictionary when converting `AssertionError` to `StashError`, using keys `"expected"` and `"actual"`. This makes `assert.deepEqual` failures inspectable from Stash code without changing the AssertionError C# class.

**Q4 — Anonymous `finally` rethrow naming**
When finally re-throws, what function name appears in the synthetic rethrow frame? This is a cosmetic question but affects test assertions.

Recommendation: **Omit the synthetic frame entirely.** `finally` is not a function call; it does not get a stack frame in Python or C#. The rethrow continues unwinding the existing stack.
