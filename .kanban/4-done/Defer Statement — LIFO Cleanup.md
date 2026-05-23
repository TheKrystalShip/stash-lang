# `defer` Statement — LIFO Cleanup

**Status:** Design complete — ready for review
**Created:** 2025-04-17
**Origin:** Unique Language Concepts — Volume 2, Feature #6
**Priority:** Medium-High

---

## 1. Summary

Add a `defer` statement that registers cleanup code to execute automatically when the enclosing function exits — whether by return, throw, or fall-through. Deferred statements execute in **LIFO order** (last registered = first to run). Arguments in single-statement defers are **evaluated eagerly** (Go semantics); block defers use standard closure capture.

This eliminates deeply nested `try/finally` chains when acquiring multiple resources that each need independent cleanup.

---

## 2. Syntax

### Grammar

```
defer_stmt      → "defer" ( defer_body )
defer_body      → "await"? call_or_stmt
                 | block_stmt

call_or_stmt    → expression_stmt       // any single statement
block_stmt      → "{" statement* "}"    // block form
```

### Examples

```stash
// Single-statement defer (arguments evaluated eagerly):
defer conn.close()
defer fs.delete(tmpDir, { recursive: true })

// Block defer (closure semantics — variables captured by reference):
defer {
    if (deployFailed) {
        restore(conn, backup)
    }
    log("cleanup done")
}

// Async defer (awaits the deferred async operation at function exit):
defer await conn.close()

// Async block defer:
defer {
    await conn.close()
    await session.end()
}
```

### Keyword

`defer` — new keyword added to the lexer's keyword map.

---

## 3. Semantics

### 3.1 Scope: Function-Level

Deferred code executes when the **enclosing function** exits. This applies to:

- Named functions (`fn foo() { ... }`)
- Lambdas / closures (`(x) => { ... }`)
- Top-level script code (defers execute at script exit)

`defer` is NOT block-scoped. A `defer` inside an `if` block does not run at the end of the `if` — it runs at function exit.

```stash
fn example() {
    if (condition) {
        let conn = connect()
        defer conn.close()
        // conn.close() does NOT run here
    }
    doOtherStuff()
    // conn.close() runs HERE (function exit)
}
```

**Decision rationale:** Function-scoped is simpler, well-proven (Go, 15+ years), and block-scoped defer would just be `try/finally` with different syntax — minimal value add.

**Alternatives considered:**

- Block-scoped (Swift model) — rejected: too similar to existing `try/finally`; the ergonomic win of `defer` is specifically about function-level cleanup without nesting

### 3.2 Execution Order: LIFO

Deferred statements execute in reverse order of registration. This matches natural resource dependency — release resources in the reverse order of acquisition.

```stash
fn deploy() {
    let tmpDir = fs.tempDir()
    defer fs.delete(tmpDir, { recursive: true })  // runs 3rd

    let conn = ssh.connect("deploy@prod")
    defer conn.close()                             // runs 2nd

    let backup = backupCurrent(conn)
    defer restore(conn, backup)                    // runs 1st

    upload(conn, artifact)
}
```

### 3.3 Evaluation Strategy: Eager (Single-Statement) / Late (Block)

**Single-statement defer** evaluates all subexpressions at `defer` time and captures the resulting values. The call itself is deferred.

```stash
let x = 10
defer println(x)   // captures x=10 NOW
x = 20
// at function exit: prints 10

let conn = connect("server1")
defer conn.close()  // captures conn=<server1 connection> NOW
conn = connect("server2")
// at function exit: closes server1's connection (correct — prevents leak)
```

**Block defer** creates a standard closure. Variables are captured by reference (Stash's existing closure semantics).

```stash
let x = 10
defer { println(x) }  // closure captures x by reference
x = 20
// at function exit: prints 20
```

This dual model gives explicit control:

- `defer f(args)` → "save these values now, call later" — safe against reassignment
- `defer { code }` → "run this code later with whatever state exists then" — allows intentional late binding

**Decision rationale:** Go's eager evaluation prevents a common class of bugs (captured variable changes before cleanup runs). The block form provides an escape hatch when late binding is intentional.

**Alternatives considered:**

- Pure late binding (all closures) — rejected by user; Go's eager model is safer for resource management
- Pure eager (no block form) — too restrictive; blocks are needed for conditional cleanup logic

### 3.4 Conditional Defer

Because defers are registered at runtime, conditional defers work naturally:

```stash
fn deploy(env) {
    let tmpDir = fs.tempDir()
    defer fs.delete(tmpDir, { recursive: true })

    if (env == "prod") {
        let conn = ssh.connect("deploy@prod")
        defer conn.close()  // only registered if env == "prod"
    }
    // conn.close() only runs if it was registered
}
```

### 3.5 Top-Level Defers

`defer` works in top-level script code. Deferred statements execute when the script exits (after the last top-level statement completes, or on error).

```stash
// script.stash
let tmpFile = fs.tempFile()
defer fs.delete(tmpFile)

// ... script logic ...
// fs.delete(tmpFile) runs automatically when script finishes
```

**Implementation:** The top-level script body is compiled as a function. Defers in the top-level function execute when that function returns (script exit). This requires no special handling — it's the same mechanism as function-level defer.

### 3.6 Async Defer

`defer await` is supported for deferring async operations.

```stash
async fn cleanup() {
    let conn = await connect("db")
    defer await conn.close()  // conn.close() is async; awaited at function exit

    let result = await conn.query("SELECT * FROM users")
    return result
}
```

When executing deferred closures that contain `await`, the VM executes the await synchronously (blocking, same as regular `await` semantics in the current VM). Since each async function runs in its own child VM, this blocking is isolated.

**Constraint:** `defer await` is valid in both async and non-async functions because Stash's `await` is transparent (non-Future values pass through unchanged). However, static analysis will warn when async calls appear in a defer without `await` (see §7).

---

## 4. Error Handling

### 4.1 Deferred Block Throws

All defers execute regardless of errors. A throwing defer does not prevent remaining defers from running.

**Case 1: Normal exit + defer throws**
The defer's error propagates as the function's error.

```stash
fn foo() {
    defer failingCleanup()  // throws Error("cleanup failed")
    return 42
}
// foo() throws Error("cleanup failed")
```

**Case 2: Error exit + defer throws**
The original error propagates. The defer's error is attached as `.suppressed`.

```stash
fn foo() {
    defer failingCleanup()  // throws Error("cleanup failed")
    throw Error("main error")
}
// foo() throws Error("main error") with .suppressed = [Error("cleanup failed")]
```

**Case 3: Multiple defers throw**
The first error (from the most recently registered defer, since LIFO) becomes primary. Subsequent defer errors are appended to `.suppressed`.

```stash
fn foo() {
    defer cleanup1()  // throws Error("c1")  — runs 2nd
    defer cleanup2()  // throws Error("c2")  — runs 1st (LIFO)
    return 42
}
// foo() throws Error("c2") with .suppressed = [Error("c1")]
```

**Case 4: Error exit + multiple defers throw**
The original error propagates. All defer errors are appended to `.suppressed`.

### 4.2 `.suppressed` Property on StashError

Add a `.suppressed` property to `StashError`:

- Type: `array` of `Error` (empty array if none)
- Populated by the defer execution machinery
- Accessible in catch blocks: `catch (e) { println(e.suppressed) }`

---

## 5. Interactions with Existing Features

### 5.1 `try/finally`

Defers and `try/finally` compose cleanly. Within a function, `finally` blocks execute first (they're inside the function), then defers execute at function exit.

```stash
fn foo() {
    defer cleanup()           // runs 2nd
    try {
        riskyOp()
    } finally {
        log("finally ran")    // runs 1st (inside function, before exit)
    }
}
```

### 5.2 `retry`

Defers inside a `retry` block body accumulate per function, NOT per retry attempt. The retry body is compiled as a closure — defers inside it belong to that closure's scope.

```stash
retry (3) {
    let conn = connect()
    defer conn.close()  // belongs to the retry body closure
    riskyOp(conn)
}
// conn.close() runs at the end of each retry attempt (closure exit)
```

This is correct behavior — each retry attempt's closure exits independently.

### 5.3 `timeout`

Same pattern as retry — the timeout body is a closure. Defers inside it run when that closure exits.

```stash
timeout 30s {
    let tmp = fs.tempFile()
    defer fs.delete(tmp)  // runs when timeout body exits
    longOperation(tmp)
}
```

On timeout cancellation, the closure exits (via TimeoutError), and defers execute during the error-exit path.

### 5.4 `lock` (future feature)

If `lock` blocks ship (Volume 2, Feature #3), defers inside lock bodies behave identically to retry/timeout — the lock body is a closure, defers are scoped to it.

### 5.5 Closures and Lambdas

Defers inside a lambda are scoped to that lambda's execution:

```stash
let items = [1, 2, 3]
items.forEach((item) => {
    let resource = acquire(item)
    defer resource.release()  // runs when this lambda invocation returns
    process(resource)
})
// Each lambda invocation's defer runs independently
```

### 5.6 `secret` Type

Deferred operations that receive `secret` values work correctly — the secret is passed to the deferred call, and redaction rules apply as normal. Since single-statement defers evaluate eagerly, the secret value is captured at `defer` time.

### 5.7 `dry` Mode (future feature)

In dry mode, deferred operations that would perform I/O are suppressed (same as any other I/O call). `defer fs.delete(tmp)` in dry mode prints `DRY: Would delete ...` instead of deleting.

### 5.8 UFCS

Deferred calls via UFCS work naturally:

```stash
let items = [1, 2, 3]
defer items.clear()  // UFCS: arr.clear(items) — items captured eagerly
```

---

## 6. Implementation Plan

### 6.1 Lexer

**File:** `Stash.Core/Lexing/Lexer.cs`

Add `defer` to the keyword map:

```csharp
{ "defer", TokenType.Defer }
```

**File:** `Stash.Core/Lexing/TokenType.cs`

Add new token type:

```csharp
Defer,  // defer keyword
```

### 6.2 AST Node

**New file:** `Stash.Core/Parsing/AST/DeferStmt.cs`

```csharp
public class DeferStmt : Stmt
{
    public Token DeferKeyword { get; }
    public Stmt Body { get; }          // ExpressionStmt (single) or BlockStmt (block)
    public bool HasAwait { get; }      // true if "defer await ..."
    public SourceSpan Span { get; }

    // IStmtVisitor<T> pattern
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitDeferStmt(this);
}
```

**Visitor update:** Add `VisitDeferStmt(DeferStmt stmt)` to `IStmtVisitor<T>`.

### 6.3 Parser

**File:** `Stash.Core/Parsing/Parser.cs`

Parse `defer` in the statement parsing path:

```
if (Match(TokenType.Defer)) return DeferStatement();
```

`DeferStatement()`:

1. Consume `defer` keyword
2. If next token is `{`, parse a `BlockStmt` → `DeferStmt(body: block, hasAwait: false)`
3. If next token is `await`, consume it, parse expression statement → `DeferStmt(body: exprStmt, hasAwait: true)`
4. Otherwise, parse expression statement → `DeferStmt(body: exprStmt, hasAwait: false)`

### 6.4 Semantic Resolver

**File:** `Stash.Core/Resolution/SemanticResolver.cs`

`VisitDeferStmt`:

1. Record that the current function scope contains defers (set a flag — needed by the compiler)
2. Resolve the body (visit child statements for variable resolution)
3. If `HasAwait`, resolve the await expression

### 6.5 Bytecode Compiler

**Files:** `Stash.Bytecode/Compilation/Compiler.*.cs`

#### New Opcode

**File:** `Stash.Bytecode/Bytecode/OpCode.cs`

```csharp
Defer = <next_available>,   // A: closure register. Push deferred closure onto frame's defer stack.
```

Format: `Ax` — A is the register containing the deferred closure. Single register, no metadata needed.

#### Compilation Strategy

**For single-statement `defer f(a, b, c)`** (eager evaluation):

1. Evaluate `f` into a hidden local register
2. Evaluate `a`, `b`, `c` into hidden local registers
3. Create a child compiler (zero-parameter closure)
4. In the child's body, emit a call: `__f(__a, __b, __c)` using `GetUpval` to access the captured values
5. If `HasAwait`, wrap the call in an `OpCode.Await`
6. Build the closure chunk, emit `OpCode.Closure`
7. Emit `OpCode.Defer` with the closure register

**For method calls `defer obj.method(args)`** (eager):

1. Evaluate `obj` into a hidden local
2. Evaluate `args` into hidden locals
3. Create closure that calls `__obj.method(__args)` via upvalues

**For block `defer { ... }`:**

1. Create a child compiler (zero-parameter closure)
2. Compile the block body in the child compiler
3. Build the closure chunk, emit `OpCode.Closure`
4. Emit `OpCode.Defer` with the closure register

Standard upvalue capture semantics apply — variables referenced in the block are captured by reference.

### 6.6 VM Runtime

**File:** `Stash.Bytecode/Runtime/CallFrame.cs`

Add defer storage to CallFrame:

```csharp
public List<StashValue>? Defers;  // Deferred closures (LIFO). Null when no defers.
```

**File:** `Stash.Bytecode/VM/VirtualMachine.Dispatch.cs`

`OpCode.Defer` handler:

```csharp
case OpCode.Defer:
    byte a = Instruction.GetA(inst);
    StashValue closure = _stack[frame.BaseSlot + a];
    (frame.Defers ??= new List<StashValue>()).Add(closure);
    break;
```

**File:** `Stash.Bytecode/VM/VirtualMachine.Functions.cs`

Modify `ExecuteReturn` — before popping the frame, execute defers:

```csharp
private void ExecuteReturn<TDebugMode>(...) where TDebugMode : struct, IDebugMode
{
    // ... existing: get return value, close upvalues ...

    // Execute defers BEFORE popping frame
    StashValue returnValue = /* captured from register */;
    if (frame.Defers is { Count: > 0 })
    {
        returnValue = ExecuteDefers(ref frame, returnValue);
    }

    // ... existing: decrement frameCount, store return value ...
}
```

`ExecuteDefers` method:

```
ExecuteDefers(ref CallFrame frame, StashValue returnValue):
    StashError? firstError = null
    List<StashError>? suppressed = null

    for i = frame.Defers.Count - 1 downto 0:  // LIFO
        try:
            CallValue(frame.Defers[i], argCount: 0)
            // Run the deferred closure — this re-enters RunInner
        catch RuntimeError ex:
            if firstError == null:
                firstError = StashError.From(ex)
            else:
                (suppressed ??= new()).Add(StashError.From(ex))

    frame.Defers = null  // Clear to prevent double execution

    if firstError != null:
        firstError.Suppressed = suppressed
        throw firstError as RuntimeError

    return returnValue
```

**Exception unwinding path:**

In the outer `Run()` catch handler, before restoring frames, execute defers for frames being unwound:

```csharp
catch (RuntimeError ex) when (_exceptionHandlers.Count > 0)
{
    ExceptionHandler handler = _exceptionHandlers[^1];
    _exceptionHandlers.RemoveAt(_exceptionHandlers.Count - 1);

    // NEW: Execute defers for frames being unwound
    List<StashError>? suppressed = null;
    for (int i = _frameCount - 1; i > handler.FrameIndex; i--)
    {
        if (_frames[i].Defers is { Count: > 0 })
        {
            // Execute defers, collect errors as suppressed
            RunFrameDefers(ref _frames[i], suppressed);
        }
    }
    if (suppressed != null)
    {
        // Attach suppressed errors to the propagating error
        ex.SuppressedErrors = suppressed;
    }

    // ... existing: CloseUpvalues, restore frameCount, restore sp ...
}
```

#### Fast Path Optimization

The `OpReturn` fast path in `VirtualMachine.Dispatch.cs` (the inlined one that skips `ExecuteReturn` for non-closure functions) must check for defers:

```csharp
// Existing fast path condition adds: && frame.Defers == null
if (typeof(TDebugMode) == typeof(DebugOff) && !frame.Chunk.HasCapturedLocals && frame.Defers == null)
{
    // ultra-fast return path (unchanged)
}
else
{
    ExecuteReturn<TDebugMode>(ref frame, inst);
}
```

Since `Defers` is null for most frames, this adds a single null check to the fast path — negligible overhead.

### 6.7 StashError Changes

**File:** `Stash.Core/Runtime/Types/StashError.cs` (or wherever StashError is defined)

Add suppressed errors support:

```csharp
public List<StashError>? Suppressed { get; set; }
```

Expose as a Stash-accessible property:

- `err.suppressed` → returns `array` of errors (empty array if null/empty)

### 6.8 Static Analysis

**File:** `Stash.Analysis/Visitors/SemanticValidator.cs`

New diagnostics (follow DiagnosticDescriptor workflow per `static-analysis.instructions.md`):

| ID          | Severity | Rule                   | Description                                                                                      |
| ----------- | -------- | ---------------------- | ------------------------------------------------------------------------------------------------ |
| `STASH2100` | Warning  | `defer-in-loop`        | `defer` inside a loop body accumulates defers — use `try/finally` for per-iteration cleanup      |
| `STASH2101` | Warning  | `defer-async-no-await` | Async call in defer block without `await` — the operation may not complete before function exits |
| `STASH2102` | Warning  | `defer-unreachable`    | `defer` after unconditional `return` or `throw` — deferred code will never be registered         |
| `STASH2103` | Info     | `defer-empty-block`    | Empty `defer` block has no effect                                                                |

**Detection logic:**

- **`defer-in-loop`**: In `VisitDeferStmt`, check if any ancestor scope is a `for`/`while`/`do` loop. If so, emit warning.
- **`defer-async-no-await`**: In `VisitDeferStmt`, walk the body AST. If any `CallExpr` resolves to an async function and there's no surrounding `AwaitExpr`, emit warning.
- **`defer-unreachable`**: In the unreachable code analysis (existing infrastructure), flag `defer` after unconditional exits.
- **`defer-empty-block`**: In `VisitDeferStmt`, check if body is a `BlockStmt` with zero statements.

### 6.9 Semantic Tokens (LSP)

**File:** `Stash.Lsp/Handlers/SemanticTokensHandler.cs`

`defer` keyword gets the `keyword` semantic token type (same as `try`, `retry`, `timeout`, etc.).

### 6.10 Completions (LSP)

**File:** `Stash.Lsp/Handlers/CompletionHandler.cs`

- After `defer`, offer:
  - `{ }` snippet for block defer
  - `await` keyword completion
  - Standard expression completions (function names, variables)
- `defer` itself should appear in statement-position keyword completions

### 6.11 Hover (LSP)

**File:** `Stash.Lsp/Handlers/HoverHandler.cs`

Hover on `defer` keyword:

```
(keyword) defer
Registers cleanup code to execute when the enclosing function exits.
Defers execute in LIFO order (last registered = first to run).
Single-statement defers evaluate arguments eagerly.
Block defers capture variables by reference.
```

### 6.12 DAP (Debugger)

**File:** `Stash.Dap/DebugSession.cs`

When stepping through code:

- Hitting a `defer` statement shows it being "registered" (the expressions are evaluated for single-statement defer, then control moves to the next statement)
- When the function exits, the debugger steps through each deferred closure in LIFO order
- The call stack should show `[defer]` or similar annotation for deferred frames

### 6.13 Syntax Highlighting

**VS Code extension:**
**File:** `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json`

Add `defer` to the keyword pattern (alongside `try`, `retry`, `timeout`, etc.).

**Playground Monarch tokenizer:**
**File:** `Stash.Playground/wwwroot/js/stash-language.js`

Add `defer` to the `keywords` array.

### 6.14 Example Script

**New file:** `examples/defer.stash`

Demonstrate:

- Basic single-statement defer
- Block defer with conditional logic
- LIFO execution order
- Defer with try/catch interaction
- Eager vs late binding (single-statement vs block)
- Multi-resource cleanup pattern
- Defer in loops (showing the footgun and the `try/finally` alternative)

### 6.15 Tests

**New file:** `Stash.Tests/Interpreting/DeferTests.cs`
**New file:** `Stash.Tests/Bytecode/DeferTests.cs`

Test scenarios:

| Category            | Test                                            | Expected                                                |
| ------------------- | ----------------------------------------------- | ------------------------------------------------------- |
| **Basic**           | Single defer runs at function exit              | Defer body executes                                     |
| **Basic**           | Multiple defers run in LIFO order               | Last registered runs first                              |
| **Basic**           | Block defer runs at function exit               | Block body executes                                     |
| **Basic**           | Defer with no other statements                  | Defer runs on fall-through                              |
| **Eager eval**      | `defer println(x)` captures x at defer time     | Prints value at defer time, not exit time               |
| **Eager eval**      | `defer obj.method()` captures obj at defer time | Calls method on original object                         |
| **Late binding**    | `defer { println(x) }` sees x at exit time      | Prints value at exit time                               |
| **Error handling**  | Defer runs on exception                         | Defer body executes before error propagates             |
| **Error handling**  | Defer throws, function returns normally         | Defer's error propagates                                |
| **Error handling**  | Defer throws, function throws                   | Original error propagates, defer error in `.suppressed` |
| **Error handling**  | Multiple defers throw                           | First (LIFO) error propagates, rest in `.suppressed`    |
| **Error handling**  | All defers run even if one throws               | Remaining defers execute                                |
| **Scope**           | Defer in if-block runs at function exit         | Not block-scoped                                        |
| **Scope**           | Conditional defer (if false) does not run       | Defer not registered                                    |
| **Scope**           | Defer in top-level script                       | Runs at script exit                                     |
| **Scope**           | Defer inside lambda                             | Scoped to lambda invocation                             |
| **Scope**           | Defer inside retry body                         | Scoped to retry closure                                 |
| **Scope**           | Defer inside timeout body                       | Scoped to timeout closure                               |
| **Interaction**     | Defer with try/finally — finally runs first     | Correct ordering                                        |
| **Interaction**     | Defer with UFCS                                 | Works correctly                                         |
| **Async**           | `defer await asyncFn()`                         | Async call awaited at exit                              |
| **Edge cases**      | Defer in loop (accumulates)                     | All defers run at function exit                         |
| **Edge cases**      | Defer references variable from outer scope      | Upvalue capture works                                   |
| **Edge cases**      | Empty defer block                               | No-op, no error                                         |
| **Edge cases**      | Nested function defers are independent          | Each function has its own defer stack                   |
| **Static analysis** | Defer in loop triggers warning                  | `STASH2100` emitted                                     |
| **Static analysis** | Async call without await in defer               | `STASH2101` emitted                                     |
| **Static analysis** | Defer after return                              | `STASH2102` emitted                                     |

### 6.16 Documentation

**File:** `docs/Stash — Language Specification.md`

Add a new section under Control Flow (alongside try/catch/finally, retry, timeout):

- **Syntax** — single-statement and block forms
- **Scope** — function-level, LIFO order
- **Evaluation** — eager (single-statement) vs closure (block)
- **Error handling** — suppressed errors
- **Async defer** — `defer await`
- **Examples** — multi-resource cleanup, conditional defer, interaction with try/finally

---

## 7. Edge Cases and Footguns

### 7.1 Defer in Loops

```stash
fn processFiles(files) {
    for (let f in files) {
        let handle = fs.open(f)
        defer handle.close()  // ⚠️ Accumulates! All close at function exit.
    }
}
```

**Mitigation:**

- Static analysis warning `STASH2100`
- Documentation recommends `try/finally` for per-iteration cleanup:

```stash
fn processFiles(files) {
    for (let f in files) {
        let handle = fs.open(f)
        try {
            process(handle)
        } finally {
            handle.close()  // Closes each iteration
        }
    }
}
```

### 7.2 Defer Cannot Modify Return Values

Unlike Go (where named return values can be modified by defers), Stash's defer captures the return value before defers execute. Defers cannot change what the function returns.

```stash
fn foo() {
    let result = 42
    defer { result = 0 }  // modifies local variable, NOT return value
    return result          // returns 42
}
// foo() returns 42, not 0
```

**Decision rationale:** Go's named-return-modification is a subtle feature that causes confusion. For a scripting language, the simpler model (return value is final) is better.

### 7.3 Defer and Recursion

Each function invocation has its own defer stack. Recursive calls don't share defers.

```stash
fn recurse(n) {
    defer println("exit ${n}")
    if (n > 0) recurse(n - 1)
}
recurse(3)
// Prints: exit 0, exit 1, exit 2, exit 3
```

### 7.4 Defer and Closures Capturing the Same Variable

```stash
fn example() {
    let count = 0
    defer println(count)      // eager: captures count=0
    defer { println(count) }  // late: captures count by reference
    count = 5
}
// Prints: 5 (late binding block runs first — LIFO)
// Then: 0 (eager capture runs second)
```

### 7.5 Performance

- `CallFrame.Defers` is `null` for frames without defer — zero overhead for functions that don't use defer
- The `OpReturn` fast path adds a single null check (`frame.Defers == null`) — negligible
- `OpCode.Defer` pushes a closure reference — O(1)
- Defer execution at function exit is O(n) where n is the number of registered defers (typically 1–5)
- No impact on the RunInner dispatch loop hot path when defer is unused

---

## 8. Cross-Platform Considerations

`defer` has no platform-specific behavior. It's a purely runtime concept (function-exit cleanup) that works identically on Linux, macOS, and Windows.

The one cross-platform concern is `defer` with process signal handling: if the process receives SIGTERM/SIGKILL, defers may not execute (process is terminated). This is inherent to all cleanup mechanisms and should be documented. For graceful signal handling, compose with the process signal infrastructure (if/when it exists).

---

## 9. Breaking Changes

**None.** `defer` is a new keyword, which is technically a breaking change if any existing script uses `defer` as a variable or function name. However:

- `defer` is not currently a keyword or reserved word
- Grep of the codebase shows no usage of `defer` as an identifier
- The word `defer` is uncommon as a variable name in sysadmin scripts
- Risk is minimal

---

## 10. Summary of Artifacts

| Artifact            | Action                                                                          | Files                                                                          |
| ------------------- | ------------------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| Keyword             | Add `defer`                                                                     | `Lexer.cs`, `TokenType.cs`                                                     |
| AST                 | New `DeferStmt`                                                                 | `Stash.Core/Parsing/AST/DeferStmt.cs`                                          |
| Visitor             | Add `VisitDeferStmt`                                                            | `IStmtVisitor.cs`, all visitors                                                |
| Parser              | `DeferStatement()` method                                                       | `Parser.cs`                                                                    |
| Resolver            | Variable resolution + defer flag                                                | `SemanticResolver.cs`                                                          |
| Compiler            | Eager/closure compilation + `OpCode.Defer`                                      | `Compiler.*.cs`                                                                |
| Opcode              | `Defer`                                                                         | `OpCode.cs`                                                                    |
| VM                  | Defer stack on CallFrame, ExecuteDefers, modified OpReturn + exception handling | `CallFrame.cs`, `VirtualMachine.*.cs`                                          |
| StashError          | `.suppressed` property                                                          | `StashError.cs`                                                                |
| Static analysis     | 4 new diagnostics                                                               | `SemanticValidator.cs`, `DiagnosticDescriptors.cs`                             |
| LSP                 | Semantic tokens, completion, hover                                              | LSP handlers                                                                   |
| DAP                 | Defer frame annotation                                                          | `DebugSession.cs`                                                              |
| Syntax highlighting | TextMate + Monarch                                                              | `stash.tmLanguage.json`, `stash-language.js`                                   |
| Tests               | ~30 test cases                                                                  | `Stash.Tests/Interpreting/DeferTests.cs`, `Stash.Tests/Bytecode/DeferTests.cs` |
| Docs                | Language spec section                                                           | `Stash — Language Specification.md`                                            |
| Example             | Example script                                                                  | `examples/defer.stash`                                                         |

---

## 11. Decision Log

| #   | Decision                                                 | Alternatives                          | Rationale                                                            |
| --- | -------------------------------------------------------- | ------------------------------------- | -------------------------------------------------------------------- |
| D1  | Function-scoped (Go model)                               | Block-scoped (Swift)                  | Simpler, proven, block-scoped is just try/finally                    |
| D2  | Eager evaluation for single-statement, closure for block | All-closure, all-eager                | Matches Go, safe for resource management, block form is escape hatch |
| D3  | Suppressed errors attached via `.suppressed`             | Last error wins, discard              | Original error matters most; suppressed errors are still accessible  |
| D4  | Top-level defers run at script exit                      | Function-only                         | Useful for script-level cleanup; no special handling needed          |
| D5  | Defer cannot modify return value                         | Allow modification (Go named returns) | Simpler mental model for scripting language                          |
| D6  | LIFO execution order                                     | FIFO, unordered                       | Matches resource dependency pattern; universally expected            |
| D7  | VM-level defer stack (not compiler desugaring)           | Desugar to try/finally                | Handles conditional defers correctly; cleaner implementation         |
| D8  | `defer await` supported                                  | Async-in-defer only with blocks       | User requirement; clean syntax for async cleanup                     |
