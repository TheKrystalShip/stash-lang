# Retry Blocks — Language Construct Specification

## Status: Draft

## Summary

`retry` is a language-level construct that re-executes a block of code when it fails, with configurable attempt limits, delays, backoff strategies, and failure predicates. It is a **keyword**, not a library function, because it must compose with the error system (`try retry`), access block scope naturally, and provide retry-aware diagnostics.

This document exhaustively covers the semantics of success/failure determination, interaction with Stash's error system, shell command handling, scope rules, and edge cases.

---

## 1. Core Syntax

```stash
retry (<maxAttempts>) {
    <body>
}

retry (<maxAttempts>, <options...>) {
    <body>
}
```

The retry expression evaluates `<body>` up to `<maxAttempts>` times. If the body completes without failure, its result value is returned. If all attempts are exhausted, the last failure is propagated.

### 1.1 Minimal Form

```stash
let data = retry (3) {
    http.get("https://api.example.com/data")
}
```

### 1.2 Full Options (Inline Fields)

```stash
retry (5, delay: 1s, backoff: Backoff.Exponential, maxDelay: 30s, jitter: true, timeout: 2m, on: [NetworkError, TimeoutError]) {
    // body
}
```

### 1.3 Full Options (RetryOptions Struct)

```stash
let opts = RetryOptions {
    delay: 1s,
    backoff: Backoff.Exponential,
    maxDelay: 30s,
    jitter: true,
    timeout: 2m,
    on: [NetworkError, TimeoutError]
}
retry (5, opts) {
    // body
}
```

### 1.4 With `until` Clause

```stash
retry (10, delay: 500ms) until (result) => result.status == "ready" {
    http.get("https://api.example.com/status")
}
```

### 1.5 With `onRetry` Hook

```stash
retry (3, delay: 2s) onRetry (n, err) {
    log.warn("Attempt ${n} failed: ${err.message}")
} {
    deployToServer()
}
```

---

## 2. Success and Failure Determination

This is the central design problem. Stash has **two distinct failure modes** that a retry block must handle:

1. **Exceptions** — `RuntimeError` thrown by `throw`, built-in functions, or type errors
2. **Soft failures** — shell commands returning non-zero exit codes, API responses with error status codes, values that are "wrong" but not exceptional

### 2.1 Mechanism 1: Exception-Based (Default)

By default, a retry block retries when the body **throws an uncaught exception**.

```stash
retry (3) {
    let data = json.parse(http.get(url).body)  // throws if parse fails or network error
    data
}
```

**Semantics:**

- If the body executes to completion without throwing → **success**, return the body's result value.
- If the body throws a `RuntimeError` → **failure**, the error is caught internally, and the body is re-executed.
- If all attempts throw → **exhaustion**, the last `RuntimeError` is re-thrown (not caught, propagated to caller).

This mirrors how `try/catch` works — the retry block acts as an implicit catch-and-re-execute loop.

**What counts as "throwing":**

- `throw "message"` or `throw { type: "...", message: "..." }`
- Built-in function errors (e.g., `json.parse` on invalid JSON)
- Type errors, null reference errors, division by zero
- Any `RuntimeError` propagating from called functions

**What does NOT count as "throwing":**

- Shell commands (`$(...)`) — these **never throw**, they return `CommandResult`
- Functions that return error values instead of throwing
- Returning `null`, `false`, or any other falsy value

### 2.2 Mechanism 2: Predicate-Based (`until` Clause)

The `until` clause provides a predicate function that evaluates the body's **return value** to determine success. This solves the shell command problem and enables result-based retry logic.

```stash
// Retry until the command succeeds (exit code 0):
let result = retry (5, delay: 2s) until (r) => r.exitCode == 0 {
    $(ping -c 1 server.example.com)
}

// Retry until an API returns healthy status:
let health = retry (10, delay: 5s) until (r) => r.status == 200 {
    http.get("https://api.example.com/health")
}

// Retry until a file appears:
retry (30, delay: 1s) until (r) => r == true {
    fs.exists("/tmp/ready.lock")
}
```

**Semantics:**

- The body executes. If it throws → **failure** (same as exception-based), retry.
- If the body completes without throwing, the return value is passed to the `until` predicate.
- If the predicate returns truthy → **success**, return the body's result value.
- If the predicate returns falsy → **failure**, retry.
- If all attempts fail the predicate → **exhaustion**, throw a `RetryExhaustedError` (see §2.5).

**Key property:** The `until` clause layers **on top of** exception-based retry. It does not replace it. A block with `until` retries on **both** exceptions and predicate failures.

### 2.3 Mechanism 3: Error Type Filtering (`on` Option)

The `on` option restricts which error types trigger a retry. Errors not in the list propagate immediately without consuming an attempt.

```stash
retry (3, on: [NetworkError, TimeoutError]) {
    http.get("https://api.example.com/data")
}
```

**Semantics:**

- If the body throws an error whose `.type` matches any entry in the `on` list → **retriable failure**, retry.
- If the body throws an error whose `.type` does NOT match → **non-retriable failure**, immediately re-throw (do not consume an attempt).
- This enables fail-fast for programming errors (wrong argument types, null references) while retrying transient failures (network issues, timeouts).

**Type matching rules:**

- `on: [Error]` — matches all error types (the base type)
- `on: [NetworkError]` — matches errors with `.type == "NetworkError"` exactly
- `on: [NetworkError, TimeoutError]` — matches either type

```stash
// Only retry network failures; crash immediately on parse errors:
retry (3, on: [NetworkError]) {
    let resp = http.get(url)              // NetworkError → retry
    let data = json.parse(resp.body)      // ParseError → crash immediately, don't waste retries
    data
}
```

### 2.4 Combining Mechanisms

All three mechanisms compose naturally:

```stash
// Retry up to 5 times, only on NetworkError, and only if the response has data:
retry (5, delay: 1s, on: [NetworkError]) until (r) => len(r.items) > 0 {
    json.parse(http.get(url).body)
}
```

**Evaluation order per attempt:**

1. Execute the body.
2. If the body throws:
   a. If `on` is specified and the error type is NOT in the list → propagate immediately.
   b. Otherwise → mark as failure, proceed to retry logic.
3. If the body completes without throwing:
   a. If `until` is specified and the predicate returns falsy → mark as failure, proceed to retry logic.
   b. Otherwise → **success**, return the result.
4. If marked as failure and `onRetry` is specified → invoke the hook with the failed attempt number and error.
5. If marked as failure and attempts remain → wait (delay/backoff), then go to step 1.
6. If marked as failure and no attempts remain → **exhaustion** (see §2.5).

### 2.5 Exhaustion Behavior

When all attempts are consumed without success:

- **Exception-based exhaustion:** The last caught `RuntimeError` is re-thrown with its original type, message, and stack trace. The retry block is transparent — it's as if the last attempt ran without retry wrapping. The full error history from all attempts is available during execution via `attempt.errors` but is **not** carried on the re-thrown error itself.

- **Predicate-based exhaustion:** A `RetryExhaustedError` is thrown because the body didn't throw — it just returned an unsatisfying value. The error includes the complete error history:

  ```
  RetryExhaustedError: All 5 retry attempts exhausted — predicate not satisfied
  ```

  ```stash
  let result = try retry (5) until (r) => r.ready { checkStatus() }
  if (result is Error && result.type == "RetryExhaustedError") {
      println(result.attempts)   // 5
      println(result.lastValue)  // last body result
      for (let err in result.errors) {
          log.error("  - ${err.message}")
      }
  }
  ```

- **Mixed exhaustion (both exceptions and predicate failures):** The last failure determines the error. If the last attempt threw an exception, that exception is re-thrown (transparent). If the last attempt completed but failed the predicate, `RetryExhaustedError` is thrown with the full `.errors` history.

Note: During execution, `attempt.errors` on the `RetryContext` always provides the running list of errors from all previous attempts, regardless of failure mode. This is the primary mechanism for observing error history within the retry body and `onRetry` hooks.

---

## 3. The Shell Command Problem

Shell commands (`$(...)`) in Stash **never throw**. They return `CommandResult { stdout, stderr, exitCode }`. This creates a fundamental tension: a naive retry block around a failing command would "succeed" on the first attempt because no exception was raised.

```stash
// BUG: This never retries — $(curl ...) doesn't throw on failure:
retry (3) {
    $(curl -f https://api.example.com/health)
}
```

### 3.1 Design Options Considered

| Approach                               | Description                                               | Pros                                    | Cons                                             |
| -------------------------------------- | --------------------------------------------------------- | --------------------------------------- | ------------------------------------------------ |
| **A. Exception-only**                  | Users must check exit codes and `throw` manually          | Explicit, no magic                      | Verbose, easy to forget                          |
| **B. `until` clause**                  | Users write predicates for command results                | Flexible, composable                    | Requires learning `until` syntax                 |
| **C. Auto-throw on non-zero**          | Retry block auto-throws for commands with `exitCode != 0` | Convenient                              | Magical, breaks "commands never throw" contract  |
| **D. Strict command syntax `$!(...)`** | New syntax that throws on non-zero                        | Explicit opt-in, reusable outside retry | Language-wide change, separate feature           |
| **E. `assertSuccess` option**          | Retry option that auto-checks command results             | Self-documenting                        | Only works for commands, not other soft failures |

### 3.2 Chosen Approach: `until` Clause (Option B) as Primary, Manual Throw (Option A) as Fallback

**Rationale:**

- The `until` clause is the **general-purpose** solution that works for commands, HTTP responses, any return value. It doesn't special-case commands.
- Manual throw remains available for complex validation logic that doesn't fit a single predicate.
- We do NOT change the "commands never throw" contract — it's a foundational Stash principle.
- We do NOT add auto-throw magic inside retry blocks — retry blocks should not change the semantics of the code inside them.

**Idiomatic patterns for commands:**

```stash
// Pattern 1: until clause (preferred)
let result = retry (3, delay: 2s) until (r) => r.exitCode == 0 {
    $(systemctl restart nginx)
}

// Pattern 2: manual throw (for complex logic)
retry (3) {
    let result = $(deploy.sh --env production)
    if (result.exitCode != 0) {
        throw { type: "DeployError", message: result.stderr }
    }
    result
}

// Pattern 3: helper function (for repeated use)
fn runOrThrow(cmd) {
    let r = cmd
    if (r.exitCode != 0) {
        throw { type: "CommandError", message: r.stderr }
    }
    r
}

retry (3) {
    runOrThrow($(deploy.sh))
}
```

### 3.3 Strict Command Syntax

The strict command syntax `$!(...)` is specified in a dedicated document: `docs/specs/Strict Command Syntax.md`. It is an independent feature that composes naturally with retry blocks but does not block the retry implementation.

```stash
// With strict command syntax (see docs/specs/Strict Command Syntax.md):
retry (3) {
    $!(deploy.sh)   // throws CommandError on non-zero exit
}
```

---

## 4. Options Reference

### 4.1 Backoff Enum

Backoff strategies are represented as a typed enum, not magic strings:

```stash
enum Backoff {
    Fixed,
    Linear,
    Exponential
}
```

Using string literals like `"fixed"` or `"exponential"` is a type error. Always use `Backoff.Fixed`, `Backoff.Linear`, or `Backoff.Exponential`.

### 4.2 RetryOptions Struct

All retry options are defined in the `RetryOptions` struct:

```stash
struct RetryOptions {
    delay: duration,       // Default: 0s — wait before first retry
    backoff: Backoff,      // Default: Backoff.Fixed — backoff strategy
    maxDelay: duration,    // Default: null — upper bound on delay
    jitter: bool,          // Default: false — ±25% random jitter
    timeout: duration,     // Default: null — wall-clock deadline for all attempts
    on: array              // Default: null — error types to retry (null = all)
}
```

### 4.3 Passing Options: Three Forms

**Form 1: Inline fields (syntactic sugar)**

The most common form. Named fields after the attempt count implicitly construct a `RetryOptions` value:

```stash
// Form 1: Inline fields (syntactic sugar — constructs RetryOptions implicitly)
retry (5, delay: 1s, backoff: Backoff.Exponential, maxDelay: 30s) {
    http.get(url)
}
```

**Form 2: Pre-built struct instance**

Construct a `RetryOptions` struct and pass it as the second argument. Enables LSP autocomplete on field names and makes configuration explicit:

```stash
// Form 2: Pre-built struct instance (enables reuse and LSP autocomplete)
let opts = RetryOptions { delay: 1s, backoff: Backoff.Exponential, maxDelay: 30s }
retry (5, opts) {
    http.get(url)
}
```

**Form 3: Reusable options across multiple retry blocks**

The struct form enables sharing the same options object without repetition:

```stash
// Form 3: Reusable options across multiple retry blocks
let networkOpts = RetryOptions {
    delay: 2s,
    backoff: Backoff.Exponential,
    maxDelay: 30s,
    jitter: true,
    on: [NetworkError, TimeoutError]
}

retry (3, networkOpts) { http.get(url1) }
retry (3, networkOpts) { http.get(url2) }
```

Inline fields are syntactic sugar that constructs a `RetryOptions` behind the scenes. Both forms give the LSP enough information to autocomplete field names and validate types.

### 4.4 Backoff Strategies

**Fixed** (`Backoff.Fixed`, default): Every retry waits `delay`.

```
Attempt 1 → fail → wait 1s → Attempt 2 → fail → wait 1s → Attempt 3
```

**Linear** (`Backoff.Linear`): Delay increases by `delay` each retry.

```
Attempt 1 → fail → wait 1s → Attempt 2 → fail → wait 2s → Attempt 3 → fail → wait 3s
```

**Exponential** (`Backoff.Exponential`): Delay doubles each retry.

```
Attempt 1 → fail → wait 1s → Attempt 2 → fail → wait 2s → Attempt 3 → fail → wait 4s
```

### 4.5 Jitter

When `jitter: true`, the computed delay is multiplied by a random factor in `[0.75, 1.25]`. This prevents synchronized retries from multiple scripts hitting the same endpoint simultaneously.

### 4.6 Timeout

The `timeout` option sets a wall-clock deadline for the entire retry operation. If the timeout is reached mid-attempt, the current attempt is **not interrupted** — the timeout is checked between attempts. If the remaining time is less than the next delay, the retry block gives up immediately.

```stash
// Will stop retrying after ~30 seconds total, even if attempts remain:
retry (100, delay: 5s, timeout: 30s) {
    http.get(url)
}
```

If timeout is reached, a `RetryTimeoutError` is thrown:

```
RetryTimeoutError: Retry timed out after 30s (completed 6 of 100 attempts)
```

---

## 5. `until` Clause Semantics

### 5.1 Syntax

```stash
retry (<maxAttempts>, <options...>) until <predicate> {
    <body>
}
```

The predicate is a function expression (lambda or function reference) that receives the body's return value and returns a boolean. The predicate can optionally accept a second parameter: the current attempt number.

### 5.2 Predicate Arity

The `until` predicate can accept **1 or 2 parameters**:

- **1 parameter** `(result)` — just the body's return value
- **2 parameters** `(result, attemptNumber)` — value plus the current attempt as an integer

The interpreter checks the predicate's arity and passes arguments accordingly. The unused attempt parameter can be discarded with `_`:

```stash
// Just value:
retry (5) until (r) => r.ready { checkStatus() }

// Value + attempt number:
retry (5) until (r, n) => r.ready || n >= 3 { checkStatus() }

// Discard attempt number explicitly:
retry (5) until (r, _) => r.ready { checkStatus() }
```

### 5.3 Named Function References

The `until` predicate accepts either an inline lambda or a named function reference:

```stash
// Inline lambda:
retry (5) until (r) => r.exitCode == 0 { $(cmd) }

// Named function:
fn isSuccess(result) { return result.exitCode == 0 }
retry (5) until isSuccess { $(cmd) }

// Named function with attempt number:
fn isDone(result, attempt: int) {
    return result.ready || attempt > 3
}
retry (5) until isDone { checkStatus() }
```

### 5.4 Evaluation

```stash
let result = retry (5) until (r) => r.exitCode == 0 {
    $(ping -c 1 server.example.com)
}
```

Per attempt:

1. Execute the body. If it throws → skip predicate, go to retry logic.
2. Pass the body's return value (and optionally the attempt number) to the `until` predicate.
3. If predicate returns truthy → success, return value to caller.
4. If predicate returns falsy → failure, go to retry logic.

### 5.5 Predicate Errors

If the predicate itself throws, the error propagates immediately (not retried). The predicate is evaluation logic, not retryable code.

### 5.6 Predicate Receives Last Value on Exhaustion

On exhaustion, the `RetryExhaustedError` carries the last body result as `.lastValue`:

```stash
let result = try retry (3) until (r) => r.status == "ready" {
    http.get("https://api.example.com/status")
}
if (result is Error) {
    println(result.message)  // "All 3 retry attempts exhausted — predicate not satisfied"
}
```

---

## 6. `onRetry` Hook

### 6.1 Syntax

```stash
retry (<maxAttempts>, <options...>) onRetry (<attempt>, <error>) {
    <hook body>
} {
    <retry body>
}
```

Either parameter can be discarded with `_` if unused: `onRetry (_, err) { ... }` or `onRetry (n, _) { ... }`.

The `onRetry` hook executes between retries — after a failure, before the next delay. It receives the **failed attempt number** (1-indexed — `1` after the first attempt fails, `2` after the second, etc.) and the error from that attempt.

### 6.2 Named Function References

Like `until`, `onRetry` accepts either an inline block or a named function reference:

```stash
// Inline block:
retry (5) onRetry (n, err) {
    log.warn("Attempt ${n} failed: ${err.message}")
} { doWork() }

// Named function:
fn logRetry(attempt: int, error: Error) {
    log.warn("Attempt ${attempt} failed: ${error.message}")
}
retry (5) onRetry logRetry { doWork() }
```

**Combined `onRetry` and `until` with named functions:**

```stash
fn retryHook(attempt: int, error: Error) {
    log.warn("Attempt ${attempt} failed: ${error.message}")
}

fn untilReady(result, attempt: int) {
    return result.status == "ready"
}

retry (5, delay: 2s) onRetry retryHook until untilReady {
    http.get("https://api.example.com/status")
}
```

### 6.3 Purpose

- Logging retry attempts with context
- Incrementally adjusting state between retries (e.g., rotating endpoints)
- Collecting per-attempt diagnostics

```stash
let endpoints = ["https://primary.api.com", "https://fallback.api.com"]
let idx = 0

retry (6, delay: 1s) onRetry (n, err) {
    log.warn("Attempt ${n} failed: ${err.message}, rotating endpoint")
    idx = (idx + 1) % len(endpoints)
} {
    http.get(endpoints[idx] + "/data")
}
```

### 6.4 Hook Errors

If the `onRetry` hook throws, the error propagates immediately. Hooks should not contain retryable logic.

### 6.5 Hook Receives Predicate Failures

When using `until`, the `onRetry` hook receives a synthesized `RetryPredicateError` for predicate failures:

```stash
retry (5) onRetry (n, err) {
    println(err.type)     // "RetryPredicateError" for predicate failures
    println(err.message)  // "Predicate not satisfied"
} until (r) => r.exitCode == 0 {
    $(curl -f url)
}
```

For exception-based failures, `err` is the original `StashError` from the caught exception.

### 6.6 Async Readiness

The `onRetry` hook is designed to be async-compatible:

- The hook is invoked **between attempts**, never concurrently with the retry body.
- The implementation calls the hook and **awaits its result** before proceeding to the delay and next attempt. This means if async support is introduced later, the hook can be an `async` function without breaking the sequential execution model.
- The hook's return value is ignored — it is called for side effects only. A future async hook returning a promise/task would not affect control flow.
- Implementation note: The interpreter invokes the hook using the same callable-invocation path used for regular function calls. When async support is added to that path, hooks get async support for free.

---

## 7. Composability with Error Handling

### 7.1 `try retry` — Catch Exhaustion

The `retry` expression can be wrapped in `try` to catch exhaustion as an Error value:

```stash
let result = try retry (3) {
    http.get(url)
}
if (result is Error) {
    log.error("All retries failed: ${result.message}")
    fallback()
}
```

This works exactly like `try <expr>` — if the retry exhausts and re-throws, `try` catches it and returns a `StashError` value.

### 7.2 `retry` inside `try/catch/finally`

```stash
try {
    retry (3) {
        deploy()
    }
} catch (e) {
    log.error("Deployment failed after 3 attempts: ${e.message}")
    rollback()
} finally {
    cleanup()
}
```

The retry block exhausts → re-throws → caught by the enclosing `try/catch`.

### 7.3 `try/catch` inside `retry`

```stash
retry (3) {
    try {
        riskySetup()
    } catch (e) {
        log.warn("Setup failed: ${e.message}")
        // Caught — retry block sees success (no exception escaped the body)
    }
    deploy()  // If this throws, the retry catches it
}
```

**Important:** If code inside the retry body catches its own exceptions, those don't trigger retry. Only **uncaught** exceptions escaping the body trigger retry.

### 7.4 Nested Retry

```stash
retry (3, delay: 10s) {
    // Outer retry for full deployment
    retry (5, delay: 1s) {
        // Inner retry for API calls
        http.get("https://api.example.com/deploy")
    }
    verifyDeployment()
}
```

Inner and outer retry blocks are independent. If the inner exhausts and re-throws, the outer catches and retries the entire body (inner + `verifyDeployment`).

---

## 8. Scope and State

### 8.1 Variable Scoping

The retry body creates a **new block scope** for each attempt. Variables declared inside the body are local to that attempt and do not persist between retries.

```stash
retry (3) {
    let attempt_data = []   // Fresh array each attempt
    arr.push(attempt_data, "item")
    // attempt_data is ["item"], not accumulating across retries
}
```

### 8.2 Outer Scope Access

The retry body has read/write access to variables in enclosing scopes:

```stash
let total = 0
retry (5) {
    total = total + 1
    println("Attempt ${attempt.current} of ${attempt.max}")
    doWork()
}
println("Succeeded after ${total} attempts")
```

This is consistent with how all block scopes work in Stash.

### 8.3 Side Effects Between Retries

Side effects from failed attempts (file writes, network calls, database updates) are **not rolled back**. The retry block only re-executes the body; it has no transactional semantics. Users should design idempotent operations inside retry blocks.

```stash
// CAUTION: If append succeeds but the next line throws, the append persists:
retry (3) {
    fs.appendFile("log.txt", "attempting deploy\n")  // Side effect survives failure
    deploy()                                          // If this throws, log.txt already has the line
}
```

This is explicitly NOT a transaction block. Documenting this clearly avoids false expectations.

---

## 9. Control Flow Inside Retry Bodies

### 9.1 `return` Statement

A `return` inside a retry body returns from the **enclosing function**, not just the retry block:

```stash
fn fetchData() {
    retry (3) {
        let data = http.get(url)
        if (data.cached) {
            return data  // Returns from fetchData()
        }
        json.parse(data.body)  // This is the retry block's result
    }
}
```

This is consistent with how `return` works inside all block constructs (loops, if/else, etc.). The retry block is not a function boundary.

### 9.2 `break` and `continue`

If a retry block is inside a loop, `break` and `continue` affect the **enclosing loop**, not the retry mechanism:

```stash
for (let server in servers) {
    retry (3) {
        let health = http.get("https://${server}/health")
        if (health.status == 404) {
            continue  // Skips to next server in the for loop
        }
        processHealth(health)
    }
}
```

To explicitly stop retrying without exhausting all attempts, the body should succeed (not throw / satisfy `until` predicate), or throw a non-retriable error type when using `on`.

### 9.3 Aborting Retries Early

There is no `break`-like syntax to exit the retry loop specifically. The design rationale:

- **Non-retriable errors:** Use `on` to exclude error types that shouldn't trigger retries.
- **Conditional success:** Use `until` to define what success looks like and just return early.
- **Give up completely:** Throw a specific error type not in the `on` list.

```stash
// Pattern: abort retries on fatal errors
retry (5, on: [NetworkError, TimeoutError]) {
    let resp = http.get(url)
    if (resp.status == 401) {
        // Auth failures are permanent — don't retry
        throw { type: "AuthError", message: "Unauthorized" }
        // AuthError is not in `on` list → propagates immediately
    }
    resp
}
```

---

## 10. Retry as an Expression

`retry` is an **expression**, not just a statement. It evaluates to the body's return value on success.

```stash
// Used as expression in variable binding:
let data = retry (3) { http.get(url) }

// Used in function arguments:
process(retry (3) { fetchConfig() })

// Used with try:
let result = try retry (3) { riskyOperation() }

// Used with null-coalescing:
let config = try retry (3) { loadRemoteConfig() } ?? defaultConfig
```

The body's **last expression** is the return value (consistent with all block expressions in Stash).

---

## 11. Attempt Context

### 11.1 Naming: Design Candidates

Three candidates were considered for exposing retry context inside the body:

**`attempt` (RECOMMENDED)**

Binds a `RetryContext` struct. Reads naturally: `attempt.current`, `attempt.max`, `attempt.remaining`. Analogous to `self` in struct methods — scoped, contextual, auto-bound by the interpreter.

*Con:* Could shadow a user variable named `attempt`. This is a minor concern: `attempt` is an uncommon variable name, and shadowing follows the same rules as any block scope.

**`retry` (recontextualized keyword)**

The same keyword, but with a context-dependent meaning inside the retry body: `retry.current`, `retry.max`.

*Pro:* Ties the accessor directly to the construct name.
*Con:* Creates parsing ambiguity — `retry` already starts a retry expression. Nested retry blocks would have confusing meaning (`retry.current` in the inner body could refer to the inner or outer block).

**`ctx`**

Generic context accessor: `ctx.current`, `ctx.max`.

*Pro:* Won't shadow commonly used variable names.
*Con:* Too generic. The name `ctx` conveys no retry-specific meaning and could apply to any scoped context construct.

**Recommendation:** `attempt`. It mirrors `self` (scoped, contextual, auto-bound), reads like English ("attempt.current"), and encourages use of the rich-context API. Variable shadowing is a minor concern since `attempt` is uncommon and follows normal block-scope shadowing rules.

### 11.2 The `RetryContext` Struct

`attempt` is bound to a `RetryContext` value that the interpreter populates on each iteration. It is an implicit struct — not user-instantiable, and not exported for direct construction:

```stash
// Implicit struct — not user-instantiable, bound by the interpreter
struct RetryContext {
    current: int,       // Current attempt number (1-indexed)
    max: int,           // Maximum attempts configured
    remaining: int,     // Attempts remaining after this one
    elapsed: duration,  // Wall-clock time since retry started
    errors: array       // All errors from previous attempts (empty on first attempt)
}
```

### 11.3 Examples

```stash
retry (5, delay: 1s) {
    log.info("Attempt ${attempt.current} of ${attempt.max}, ${attempt.remaining} left")
    if (attempt.current > 1) {
        log.warn("Previous errors: ${attempt.errors}")
    }
    http.get(url)
}
```

Using `attempt.elapsed` for adaptive behavior:

```stash
retry (20, delay: 500ms) {
    if (attempt.elapsed > 5s) {
        log.warn("Slow retry loop — ${attempt.current} attempts in ${attempt.elapsed}")
    }
    http.get(url)
}
```

### 11.4 Scoping Rules

`attempt` is scoped to the retry block. In nested retry blocks, the inner block's `attempt` shadows the outer block's `attempt`:

```stash
retry (3, delay: 10s) {
    // attempt.current is 1, 2, or 3 for the outer block
    retry (5, delay: 1s) {
        // attempt.current is 1..5 for the inner block
        // The outer attempt context is shadowed here
        http.get("https://api.example.com/deploy")
    }
    verifyDeployment()
}
```

---

## 12. Error Types Introduced

| Error Type            | Thrown When                                                | Carried Properties                                                                                          |
| --------------------- | ---------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| `RetryExhaustedError` | All attempts fail the `until` predicate                    | `.message`, `.attempts` (count), `.lastValue` (last body result), `.errors` (array of all attempt errors)   |
| `RetryTimeoutError`   | Wall-clock `timeout` exceeded                              | `.message`, `.elapsed` (duration), `.completedAttempts` (count)                                             |
| `RetryPredicateError` | (Internal) Passed to `onRetry` hook for predicate failures | `.message`                                                                                                  |

Exception-based exhaustion re-throws the **original** error — no wrapping, no new types. The retry block is transparent. The full error history is available during execution via `attempt.errors` on the `RetryContext`, but is not carried on the re-thrown error itself. `RetryExhaustedError.errors` is only populated for predicate-based and mixed exhaustion.

---

## 13. Grammar

```
retryExpr      → "retry" "(" expression ("," retryArgs)? ")"
                 ("onRetry" onRetryClause)?
                 ("until" untilClause)?
                 block ;

retryArgs      → retryOption ("," retryOption)*
               | expression ;  // RetryOptions struct instance

retryOption    → IDENTIFIER ":" expression ;

untilClause    → expression ;  // lambda or function reference

onRetryClause  → "(" IDENTIFIER "," IDENTIFIER ")" block
               | expression ;  // function reference
```

The `retry` keyword is followed by:

1. Parenthesized max-attempts expression and optional named options (or a `RetryOptions` struct instance)
2. Optional `onRetry` hook with either an inline parameter list and block, or a function reference
3. Optional `until` clause with a predicate expression (lambda or function reference)
4. The retry body block

### 13.1 Parsing Precedence

`retry` parses at expression level, similar to `try`. It binds tighter than assignment but looser than binary operators:

```stash
let x = retry (3) { expr }        // ✓ retry binds to the block
let x = retry (3) { a } + b       // ✗ syntax error — retry block is the full expression
let x = (retry (3) { a }) + b     // ✓ explicit grouping needed for arithmetic
```

---

## 14. Interaction with Async/Parallel

### 14.1 Retry Inside `async`

```stash
let task = async {
    retry (3, delay: 1s) {
        http.get(url)
    }
}
let result = await task
```

The retry block runs entirely within the async context. Delays are async-aware (they do not block other tasks).

### 14.2 Retry Inside `parallel`

```stash
let results = parallel {
    retry (3) { http.get(url1) }
    retry (3) { http.get(url2) }
    retry (3) { http.get(url3) }
}
```

Each branch retries independently within its parallel execution context.

---

## 15. Implementation Considerations

### 15.1 AST Node

```
RetryExpr {
    MaxAttempts: Expr
    Options: List<(Token Name, Expr Value)> | Expr  // named fields OR RetryOptions struct instance
    UntilClause: Expr?            // Optional — lambda expression or function reference identifier
    OnRetryClause: OnRetryNode?   // Optional — inline block or function reference
    Body: BlockStmt
    SourceSpan
}

OnRetryNode {
    IsReference: bool             // true = function reference, false = inline block
    ParamAttempt: Token?          // Named param for attempt number (inline block only)
    ParamError: Token?            // Named param for error (inline block only)
    Body: BlockStmt?              // Inline block body (IsReference = false)
    Reference: Expr?              // Function reference expression (IsReference = true)
}
```

### 15.2 Interpreter Pseudocode

```
function visitRetryExpr(node):
    maxAttempts = evaluate(node.MaxAttempts)
    options = evaluateOptions(node.Options)  // delay, backoff, on, jitter, etc.

    lastError = null
    lastFailureWasException = false
    lastValue = null
    collectedErrors = []
    startTime = now()

    for currentAttempt in 1..maxAttempts:
        // Check timeout before each attempt
        if options.timeout and (now() - startTime) >= options.timeout:
            throw RetryTimeoutError(elapsed: now() - startTime, completedAttempts: currentAttempt - 1)

        // Bind attempt context
        retryContext = RetryContext {
            current   = currentAttempt,
            max       = maxAttempts,
            remaining = maxAttempts - currentAttempt,
            elapsed   = now() - startTime,
            errors    = collectedErrors
        }
        environment.define("attempt", retryContext)

        try:
            result = executeBlock(node.Body)

            // Check until predicate
            if node.UntilClause:
                predArity = getArity(node.UntilClause)
                predArgs = predArity >= 2 ? [result, currentAttempt] : [result]
                if callCallable(node.UntilClause, predArgs):
                    return result   // Success
                else:
                    lastValue = result
                    lastFailureWasException = false
                    predicateErr = RetryPredicateError("Predicate not satisfied")
                    collectedErrors.push(predicateErr)
                    if node.OnRetryClause and currentAttempt < maxAttempts:
                        invokeOnRetryHook(node.OnRetryClause, currentAttempt, predicateErr)
            else:
                return result       // No predicate — body didn't throw, success

        catch RuntimeError as e:
            // Check error type filter
            if options.on and e.type not in options.on:
                rethrow e           // Non-retriable — propagate immediately

            lastError = e
            lastFailureWasException = true
            collectedErrors.push(StashError.fromRuntimeError(e))

            if node.OnRetryClause and currentAttempt < maxAttempts:
                invokeOnRetryHook(node.OnRetryClause, currentAttempt, StashError.fromRuntimeError(e))

        // Delay before next attempt (skip delay after last attempt)
        if currentAttempt < maxAttempts:
            delay = computeDelay(options, currentAttempt)
            sleep(delay)

    // Exhaustion
    if lastFailureWasException and lastError:
        rethrow lastError           // Re-throw original error — transparent
    else:
        throw RetryExhaustedError(attempts: maxAttempts, lastValue: lastValue, errors: collectedErrors)


function invokeOnRetryHook(clause, attemptNumber, error):
    if clause.IsReference:
        callCallable(clause.Reference, [attemptNumber, error])
    else:
        newEnv = environment.pushScope()
        newEnv.define(clause.ParamAttempt, attemptNumber)
        newEnv.define(clause.ParamError, error)
        executeBlock(clause.Body, newEnv)
    // Hook return value is ignored — awaited but discarded
```

### 15.3 New Token Type

`Retry` keyword token — added to the reserved word list alongside `try`, `catch`, `throw`, etc.

### 15.4 Static Analysis

The analysis engine should:

- Warn when a retry body contains only shell commands without an `until` clause (likely bug — commands don't throw)
- Warn when max attempts is `1` (retry with 1 attempt is just executing the block)
- Validate that `on` values are string literals or identifiers (error type names)
- Validate that `until` expression is callable (lambda or function reference)
- Warn when `backoff` is set but `delay` is `0s` (backoff has no effect without a base delay)

---

## 16. Edge Cases

### 16.1 Max Attempts = 0

```stash
retry (0) { expr }
```

The body is never executed. Immediately throws `RetryExhaustedError` with zero attempts and no errors. This should produce a static analysis warning.

### 16.2 Max Attempts = 1

```stash
retry (1) { expr }
```

Equivalent to just executing `expr` — no retry occurs. The block runs once and either succeeds or throws. Static analysis warning recommended.

### 16.3 Body With No Throwable Operations

```stash
retry (3) {
    let x = 5 + 3
    x
}
```

Always succeeds on the first attempt. Not an error, but the retry is pointless. Low-priority static analysis hint.

### 16.4 Timeout Shorter Than Delay

```stash
retry (10, delay: 30s, timeout: 5s) { expr }
```

First attempt runs. If it fails, timeout check before attempt 2 fires → `RetryTimeoutError`. Effectively only one attempt is possible.

### 16.5 Concurrent Mutation in Outer Scope

```stash
let counter = 0
retry (3) {
    counter = counter + 1   // Persists across retries (outer scope)
    if (counter < 3) { throw "not yet" }
    counter
}
// counter is 3
```

This is intentional and documented — outer scope mutations persist. The user controls what state carries over.

### 16.6 Predicate Always Returns Falsy

```stash
retry (3) until (r) => false { "hello" }
```

All 3 attempts "succeed" (no throw) but the predicate rejects every result → `RetryExhaustedError`.

---

## 17. Design Decisions and Rationale

### 17.1 Why `retry` Is a Keyword, Not a Function

| Concern                     | Keyword                                | Function                                       |
| --------------------------- | -------------------------------------- | ---------------------------------------------- |
| Block scope access          | Natural — retry body is a block        | Requires closure allocation                    |
| `try retry` composition     | Direct — parser handles it             | Cannot compose syntactically                   |
| Stack traces                | Retry-aware frames                     | Generic "anonymous closure" frames             |
| Static analysis             | AST-level warnings                     | Opaque function call                           |
| `return`/`break`/`continue` | Respects enclosing function/loop scope | Would return from the lambda, not the function |
| Implicit `attempt` context  | Clean injection                        | Requires callback parameter or global          |

### 17.2 Why `until` Instead of `when` or `while`

- `when` is ambiguous — "when should I retry?" vs "when is it done?"
- `while` implies "keep going while true" which is the **opposite** of what we want (we retry while failing, but `while` would read as "retry while this is true" meaning retry on success)
- `until` clearly means "keep retrying until this condition is met" — natural English reading

### 17.3 Why Exceptions Are the Default Failure Signal

Stash's built-in functions (http, json, fs, etc.) signal failure through exceptions. The most common retry targets — network calls, file operations, parsing — throw on failure. Making exceptions the default retry trigger means the simplest `retry (n) { ... }` does the right thing for the common case.

### 17.4 Why No Transaction/Rollback Semantics

Implementing rollback would require:

- Tracking all side effects (file writes, network calls, env mutations)
- Providing undo operations for each
- Handling partial-undo failures

This is a database-level feature, not a scripting language feature. It would add enormous complexity for marginal benefit. Instead, we document that retried operations should be **idempotent** and provide the `onRetry` hook for manual cleanup between attempts.

### 17.5 Why No `break` for the Retry Loop

A retry-specific `break` would:

- Conflict with loop `break` semantics (which `break` do you mean inside a `for` + `retry`?)
- Require a new keyword or overloaded meaning
- Be unnecessary — the `on` type filter handles "don't retry this error" cleanly

### 17.6 Why a Backoff Enum, Not Magic Strings

`Backoff.Fixed`, `Backoff.Linear`, and `Backoff.Exponential` replace string literals for the backoff strategy because:

- **Type safety:** The compiler and static analysis can reject invalid values. String literals like `"exponetial"` (or any other typo) would be silent runtime bugs.
- **LSP autocomplete:** The LSP can offer completions for `Backoff.` members. String literal values offer no completion or validation.
- **Consistency:** Stash uses enums for all fixed option sets. Using a string here would be the only exception, introducing an inconsistency in the language's API design.
- **Discoverability:** Enum members document what strategies exist. Strings require reading documentation to know valid values.

### 17.7 Why `RetryOptions` Is a Struct

The options are represented as a formal struct rather than an ad-hoc dictionary because:

- **Reuse:** A `RetryOptions` value can be shared across multiple retry blocks without repeating inline fields.
- **LSP support:** Field names get autocomplete and type-checking in both the inline-field form and the struct literal form.
- **Explicit structure:** Fields have known types — `backoff: Backoff`, `delay: duration`, etc. — enabling static analysis.
- **Consistency:** Stash's philosophy prefers structs over anonymous dicts for structured data passed between constructs.

The inline-field form (`retry (5, delay: 1s, ...)`) is syntactic sugar that constructs a `RetryOptions` implicitly, making the common case concise without sacrificing the struct's benefits.

### 17.8 Why `attempt` Is a Contextual Keyword

The contextual keyword `attempt` (binding a `RetryContext` struct) was chosen over a plain integer variable (`_attempt`) because:

- **Rich context:** A single integer tells you only the current count. `RetryContext` provides `current`, `max`, `remaining`, `elapsed`, and the full `errors` history — all of which are useful in retry bodies.
- **Mirrors `self`:** Just as `self` is auto-bound inside struct methods, `attempt` is auto-bound inside retry bodies. Both follow the pattern of the interpreter providing a contextual value without requiring an explicit declaration.
- **Naming clarity:** `attempt.current` reads as natural English. `_attempt` carries the Stash "discard" convention for underscore-prefixed identifiers, which is the wrong semantic signal — this variable is explicitly meant to be used.
- **Extensibility:** Adding new fields to `RetryContext` in the future is backward-compatible. Adding a new implicit integer variable would require a new name.

---

## 18. Standard Library Integration

### 18.1 `retry` Namespace (Future Consideration)

A potential standard library namespace for querying retry state from within nested function calls:

```stash
fn deploy() {
    // Is this being called from inside a retry block?
    if (retry.active()) {
        log.info("Deploy attempt ${retry.currentAttempt()}")
    }
}

retry (3) { deploy() }
```

This is a **future consideration** — it requires threading retry context through the call stack and adds complexity. The `attempt` contextual keyword covers the common case within the immediate retry body. The namespace name `retry` would conflict with the keyword inside retry bodies, so naming disambiguation would need to be handled by the parser based on syntactic context.

---

## 19. Examples

### 19.1 HTTP API Polling

```stash
let deployment = retry (30, delay: 2s, timeout: 2m) until (r) => r.status == "complete" {
    let resp = http.get("https://api.example.com/deployments/${id}")
    json.parse(resp.body)
}
println("Deployment complete: ${deployment.url}")
```

### 19.2 SSH Connection with Exponential Backoff

```stash
let conn = retry (5, delay: 1s, backoff: Backoff.Exponential, maxDelay: 30s) {
    ssh.connect({ host: "prod-server", user: "deploy", key: "~/.ssh/id_rsa" })
}
```

### 19.3 Command Retry with Logging

```stash
fn logServiceRestart(n: int, err: Error) {
    log.warn("Service restart attempt ${n} failed: ${err.message}")
}

retry (3, delay: 5s) onRetry logServiceRestart until (r) => r.exitCode == 0 {
    $(systemctl restart myservice)
}
```

### 19.4 Database Migration with Error Filtering

```stash
retry (3, delay: 2s, on: [ConnectionError, LockError]) {
    db.migrate("migrations/")
}
// MigrationError (e.g., syntax error in SQL) propagates immediately
// ConnectionError, LockError trigger retries
```

### 19.5 Composing with `try` and `??`

```stash
let config = try retry (3) { loadRemoteConfig() } ?? loadLocalConfig()
```

### 19.6 Nested Retry for Multi-Stage Operations

```stash
retry (2, delay: 30s) {
    // Stage 1: Pull image (retry network failures)
    retry (5, delay: 2s, on: [NetworkError]) {
        $(docker pull myapp:latest)
    }

    // Stage 2: Deploy (retry container scheduling)
    retry (3, delay: 10s) until (r) => r.exitCode == 0 {
        $(kubectl rollout restart deployment/myapp)
    }

    // Stage 3: Verify
    retry (10, delay: 5s, timeout: 1m) until (r) => r.ready {
        json.parse(http.get("https://myapp.example.com/health").body)
    }
}
```

### 19.7 Using `attempt` Context for Adaptive Behavior

```stash
retry (5, delay: 1s, backoff: Backoff.Exponential) {
    log.info("Attempt ${attempt.current} of ${attempt.max} (${attempt.remaining} remaining)")
    if (attempt.current > 1) {
        log.warn("${len(attempt.errors)} previous failure(s)")
    }
    http.get(url)
}
```

### 19.8 Reusable RetryOptions

```stash
let networkOpts = RetryOptions {
    delay: 2s,
    backoff: Backoff.Exponential,
    maxDelay: 30s,
    jitter: true,
    on: [NetworkError, TimeoutError]
}

retry (3, networkOpts) { http.get(url1) }
retry (3, networkOpts) { http.get(url2) }
retry (5, networkOpts) { http.get(url3) }
```

### 19.9 Named Functions for `until` and `onRetry`

```stash
fn retryHook(n: int, err: Error) {
    log.warn("Attempt ${n} failed: ${err.message}")
    metrics.increment("api.retry", { attempt: n })
}

fn isReady(result, n: int) {
    return result.status == "ready" || n >= 5
}

retry (10, delay: 2s) onRetry retryHook until isReady {
    http.get("https://api.example.com/status")
}
```

### 19.10 Inspecting All Errors on Exhaustion

```stash
let result = try retry (5, delay: 1s) until (r) => r.status == "ready" {
    http.get("https://api.example.com/status")
}
if (result is Error && result.type == "RetryExhaustedError") {
    log.error("All ${result.attempts} attempts failed")
    for (let err in result.errors) {
        log.error("  - ${err.message}")
    }
}
```

---

## 20. Design Decisions Log

The following questions were raised during design and have been resolved:

### 20.1 `_attempt` naming

**Resolved:** The `_attempt` integer variable is replaced by `attempt`, a contextual keyword that binds a `RetryContext` struct. This provides rich context (`current`, `max`, `remaining`, `elapsed`, `errors`) rather than just a counter, mirrors the `self` pattern from struct methods, and eliminates the misleading underscore prefix (which conventionally indicates "unused" in Stash). See §11.

### 20.2 `onRetry` async support

**Resolved:** The `onRetry` hook is designed to be async-ready from the outset. The implementation invokes the hook using the same callable path as regular function calls and awaits its result before proceeding to the next attempt. When async support is added to the callable path (a planned future feature), hooks automatically gain async support without any breaking changes. See §6.6.

### 20.3 Decorator syntax

**Resolved: No.** The `retry` expression is already concise. A decorator form (`@retry(3, delay: 1s)`) would add parser complexity and a new syntax concept for minimal ergonomic gain. The inline form composes better with `try`, `??`, and assignment.

### 20.4 Error collection

**Resolved:** All errors from all attempts are collected. During execution, `attempt.errors` (on the `RetryContext`) grows with each failed attempt. On exhaustion, `RetryExhaustedError.errors` carries the complete list, including predicate failures (as `RetryPredicateError` entries). Exception-based exhaustion still re-throws the original error transparently, but the full history is available when caught with `try`. See §2.5 and §12.

### 20.5 `until` predicate attempt number

**Resolved:** The `until` predicate optionally accepts a second parameter for the current attempt number. The interpreter checks arity at call time and passes `(result)` or `(result, attemptNumber)` accordingly. Unused parameters can be discarded with `_`. See §5.2 and §5.3.
