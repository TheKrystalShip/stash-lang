# Error Handling — Greenfield Architecture Vision

> **Status:** Backlog — Architectural exploration / vision document
> **Created:** 2026-05-10
> **Author:** Spec Architect (via design conversation)
> **Scope:** A first-principles redesign of error handling for Stash, assuming no backwards compatibility constraints. Intended to provoke a decision, not to be implemented as-is.

---

## 0. How To Read This Document

This is not a feature spec. It is a **design vision**. It picks a position, defends it, and shows what Stash would look like under it. Decisions are intentionally opinionated. Section 10 is honest about where the position is weak.

The document presumes familiarity with what Stash already has today:

- `try expr` (prefix operator) — runs `expr`, returns the value on success or an `Error` value on failure.
- `try { ... } catch (TypeName e) { ... }` — block form with structured catch.
- `throw <string>` / `throw <dict>` / `throw TypeName { ... }` (struct throw) — three throw forms.
- `retry (n, ...) { body }` with `onRetry`, `until`, `on:` filtering.
- `defer` for LIFO cleanup.
- Built-in error types in `ErrorTypeRegistry`: `ValueError`, `TypeError`, `ParseError`, `IndexError`, `IOError`, `NotSupportedError`, `TimeoutError`, `CommandError`, `LockError`, `StateError`, `AliasError`, `CancellationError`.
- `$(cmd)` returns a `CommandResult { stdout, stderr, exitCode }` (does **not** throw on non-zero).
- `$!(cmd)` is the strict form — throws `CommandError` on non-zero.
- `lastError()` builtin retrieves the most recent error.

Files cited for grounding:
- `/home/heisen/stash-lang/Stash.Core/Runtime/RuntimeError.cs`
- `/home/heisen/stash-lang/Stash.Core/Runtime/ErrorTypeRegistry.cs`
- `/home/heisen/stash-lang/Stash.Core/Runtime/ErrorTypes.cs`
- `/home/heisen/stash-lang/Stash.Core/Parsing/AST/TryExpr.cs`
- `/home/heisen/stash-lang/Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs` (lines 1327–1370 — strict-mode CommandError throw sites)
- `/home/heisen/stash-lang/examples/error_handling.stash`
- `/home/heisen/stash-lang/examples/retry_blocks.stash`
- `/home/heisen/stash-lang/examples/safe_shell.stash`

---

## 1. Philosophy — North Star

> **Stash is the language you reach for when you expect things to fail.**
> Sysadmin scripts run in environments full of partial failure: networks flap, disks fill, daemons restart, permission models drift, hosts disappear. The error story should match the *reality of the domain*, not import a sensibility from compiled application languages.

Three principles flow from this:

### 1.1 Errors are values, not control-flow surprises

A junior sysadmin reading `let r = fs.readFile(path);` should be able to tell from the *line of code in front of them* that `r` could be a failure, without running the program or reading `fs.readFile`'s docs. Today, they cannot — `fs.readFile` throws, and the failure mode is invisible at the call site. **An invisible failure mode is a bug factory.**

The corollary: errors are **plain Stash values** that flow through normal expressions. They are not magic things that vault past five stack frames and land in a `catch` clause two files away. The control-flow surface area of error handling should be small and local.

### 1.2 Failure must be cheap to express, but never silent

The sin of Bash is `cmd; cmd2` — failure of the first is invisible. The sin of Java is checked exceptions everywhere. Stash should make ignoring a failure require a *visible token at the call site*. The token can be one character. It cannot be zero characters.

The contrapositive: if you handle the failure properly, that should *also* be cheap and obvious — not a five-line `try`/`catch` ceremony around a one-line call.

### 1.3 Errors carry rich, structured context — by default

`"failed to read file"` is useless. `IOError { path: "/etc/foo", os: "ENOENT", op: "open" }` is debuggable, scriptable, and pattern-matchable. The error payload is part of the type, not a string to grep. Sysadmin scripts routinely do "if exit code 4, retry; if exit code 7, escalate" — that requires structured access, not stringly typed errors.

### 1.4 Implication: Stash today is half-right

Today's Stash already treats errors as first-class values *after* `try`. But the unit before `try` is exception-throwing — invisible at the call site. The proposal below reverses that: **the unit is a result-bearing expression**, and "throw and unwind" is opt-in via `!`. We keep `try`/`catch` blocks for the cases where exception-shaped flow genuinely fits.

---

## 2. The Fundamental Choice

### 2.1 Survey of options, scored against Stash's domain

| Model                        | Local reasoning | Ergonomics for short scripts | Composability | Info density | Perf in register VM |
| ---------------------------- | --------------- | ---------------------------- | ------------- | ------------ | ------------------- |
| Pure exceptions (today)      | Bad             | Excellent                    | OK            | OK           | Bad on unwind       |
| Pure Result/Either           | Excellent       | Painful (`?` tax everywhere) | Excellent     | Excellent    | Excellent           |
| Multi-return tuples (Go)     | Excellent       | Verbose `if err != nil`      | OK            | Mediocre     | Excellent           |
| Checked errors (Java)        | Good            | Hostile                      | Bad           | Good         | Bad                 |
| Effect systems (Koka)        | Excellent       | Brain-bending                | Excellent     | Excellent    | Mediocre            |
| Resumable conditions (CL)    | Mediocre        | Powerful but obscure         | Excellent     | Excellent    | Bad                 |
| **Hybrid (proposed)**        | Good            | Excellent                    | Excellent     | Excellent    | Good                |

### 2.2 The position

**Stash should adopt a *fallible-by-default value model* with an opt-in *propagation operator* and a small *unwinding escape hatch* for genuinely-exceptional flow.**

Concretely:

1. **Every expression that can fail evaluates to a value of type `T | Error`** (a *fallible* value). This is not Rust's tagged `Result<T, E>` — it is a structural union. `Error` is a first-class type with subtypes (the existing `ErrorTypeRegistry`).
2. **`!` is the propagation operator.** `let n = parseInt(s)!;` — if `parseInt` returned an `Error`, `!` propagates it out of the enclosing fallible scope. If it returned `42`, `!` unwraps to `42`.
3. **Functions are fallible-by-default.** Any function may return an `Error`. No checked-exception ceremony at declaration sites. Annotations are *opt-in for static analysis*, not mandatory for correctness.
4. **`throw` exists, but it is the *unwinding* escape hatch — used for truly out-of-band failure that should not pollute every call site.** `panic` is the sharper word — and the better one. We rename `throw` → `panic` (or keep `throw` as a deprecated alias). `try`/`catch` block form catches panics. Plain `Error` values are *not* unwinding — they are returned.
5. **`try expr` becomes a *catch-panic-as-value* operator** — it converts an unwinding `panic` into a returned `Error` value. This is what today's `try expr` does in spirit; we just stop pretending the underlying call returns a non-fallible value.

The mental model:

```
                       returns Error?     panics?
plain call f()              yes              yes (rare; programmer errors, OOM, etc.)
f()!                        no — propagates  yes
try f()                     no — value is    no — caught into Error
                            T or Error
```

### 2.3 Why this fits Stash specifically

- **The domain demands cheap composability.** Sysadmin scripts pipe failures together: read file → parse → upload → log. With Result+`?`, this stays tight: `upload(parse(readFile(p)!)!)!`. With exceptions you cannot tell from reading the line that any of those can fail. With Go-style tuples you write 12 lines. The propagation operator is a one-character tax that is *visible*.
- **Errors-as-values play well with `dict`-heavy idioms.** Sysadmins routinely build error reports, log them as JSON, send them to Slack. Today, you have to remember to convert. With errors-as-values, `errors.push(err)` and `json.encode(errors)` Just Work without ceremony.
- **Unwinding stays available where it earns its keep.** The interactive REPL's "uncaught error → red traceback" UX is genuinely good, and so is `try { do_a_lot_of_stuff } catch { rollback() }` for transactional scripts. Those keep working — but they are *the rarer case*, not the default.
- **Fits the register VM.** Today's exception unwinding pays a real cost: `RuntimeError` is a C# `throw`. With errors-as-values, the *common* failure path is a normal return. Unwinding is reserved for `panic` — uncommon. We *gain* perf, not lose it.

### 2.4 Why not pure Result?

Rust's `Result<T, E>` has a parametric `E`. That is great for Rust, where you need to enumerate variants and exhaustively match. In a dynamically-typed sysadmin scripting language, the user does not want to declare `enum NetworkError { Timeout, Refused, Unreachable }` before they can write a 10-line script. The Stash `Error` type is **a structural, open hierarchy**, not a closed parametric type. You get the propagation ergonomics of `Result<T, E>` *without* the type-parameter ceremony.

### 2.5 Why not Go-style tuples?

```stash
let n, err = parseInt(s);
if (err != null) { return null, err; }
```

This is what Stash would look like with multi-return. After 50 of those, you write a script in Bash again. Sysadmin scripts cannot afford this verbosity. The propagation operator collapses it to one character.

### 2.6 Why not effect types?

Beautiful in Koka. Wrong audience. Stash users are not type-system enthusiasts — they want to ship a deploy script before lunch.

---

## 3. Concrete Syntax

> All examples below are **proposed**, not current Stash.

### 3.1 The propagation operator `!`

```stash
fn loadConfig(path: string) {
  let raw     = fs.readFile(path)!;       // propagate IOError on failure
  let parsed  = json.decode(raw)!;        // propagate ParseError on failure
  let port    = parsed["port"]!;          // propagate IndexError on missing key
  return parsed;
}

let cfg = loadConfig("/etc/app.json");
if (cfg is Error) {                       // local, visible failure check
  log.warn($"config load failed: {cfg.message}");
  return;
}
```

**Rules of `!`:**
1. `!` is a **postfix expression operator** at primary precedence (binds tighter than `.`, `()`, `[]` chaining? No — see 3.1.1).
2. If the operand is **not** an `Error` value, `!` is the identity.
3. If the operand **is** an `Error`, `!` short-circuits: control flow returns immediately from the *enclosing fallible scope*, with the error as the return value.
4. The "enclosing fallible scope" is the nearest enclosing function body, `try { ... }` block, or top-level script. (This is closer to Swift's `try` than Rust's `?`, but with the explicit mark at the call site.)

#### 3.1.1 Precedence and chaining

```stash
let user = fetchUser(id)!.name;           // call, propagate, then field access
let lines = fs.readFile(p)!.split("\n");  // ! binds to the call result
```

Parses as `(fs.readFile(p))!.split("\n")`. Postfix `!` has the same precedence as `.` and `[]`, left-associative within that group.

#### 3.1.2 No silent loss of failure

`!` MUST appear visibly at the call site — there is no implicit propagation. The operator is the entire ergonomic improvement; making it invisible is a regression.

`!` outside a fallible scope is a **compile-time error** (top-level script counts as fallible).

### 3.2 Returning errors as values

A function returns an error simply by returning one — no special syntax:

```stash
fn lookupHost(name: string) {
  if (name == "") {
    return ValueError { message: "empty hostname" };
  }
  let result = $(getent hosts ${name});
  if (result.exitCode != 0) {
    return CommandError {
      message: $"getent failed for {name}",
      exitCode: result.exitCode,
      stderr: result.stderr,
      command: "getent",
    };
  }
  return str.split(result.stdout, " ")[0];
}
```

No declaration, no `-> Result<string, Error>`. Just `return err`. The static analyzer (section 8) infers fallibility.

### 3.3 `try` on an expression — *catch into value*

`try` ceases to be redundant. It now means: "if this expression *panics*, capture the panic as an `Error` value." For pure errors-as-values, `try` is a no-op.

```stash
let n = try parseInt(s);    // n is int | Error
let n = try riskyPanic();   // n is whatever | Error — even a panic becomes a value
```

`try` is the **only** way to neutralize a panic into a value. Without it, panics keep unwinding.

### 3.4 `try { } catch { }` — *unwinding scope*

Block-form `try`/`catch` is for unwinding panics. Pattern-matching catches are first-class:

```stash
try {
  for (let host in hosts) {
    deploy(host)!;          // ! converts Error → return; doesn't unwind
  }
  panic ScheduleError { message: "deploy phase done" };  // explicit panic
} catch (CommandError e) {
  log.error($"command died: {e.command} exit={e.exitCode}");
} catch (e is IOError | TimeoutError) {
  log.warn($"transient: {e.message}");
} catch (e) {
  log.fatal(e);
  rethrow;
}
```

`!` and `panic`/`catch` cooperate: `!` returns *out* of the function; if every function on the stack does `!`, the error eventually surfaces at a `try`/`catch` block or to the script root.

### 3.5 `panic` (replaces `throw`)

```stash
fn assertEven(n: int) {
  if (n % 2 != 0) panic ValueError { message: $"expected even, got {n}" };
}
```

`panic` unwinds the stack. It is for:
- Genuine programmer errors (assertion failures, invariant violations).
- Conditions where threading the error through every caller would be absurd (OOM, fatal config, cancelled context).
- Test framework failures (`assert` panics).

In day-to-day sysadmin code, `panic` should be **rare**. A reviewer should ask "why didn't you `return Error { ... }` here?" when they see one.

### 3.6 `??` and `?:` for defaults

```stash
let port = parseInt(env.get("PORT"))! ?? 8080;
                                  ^^ if Error, fall back
```

`??` already treats `Error` as falsy in today's Stash. We keep that.

But there's a subtlety: with the new model, `parseInt(env.get("PORT"))` *returns* an Error. We don't want `!` here — we want the value-or-default. So we drop the `!`:

```stash
let port = parseInt(env.get("PORT")) ?? 8080;
```

Both forms are valid. **`!` propagates; `??` defaults.** They are the two ways to dispose of an Error inline.

### 3.7 Pattern matching on errors

```stash
match readConfig(path) {
  case Config c => use(c),
  case IOError { code: "ENOENT" } => writeDefaultConfig(path),
  case IOError e => log.fatal(e),
  case ParseError e => log.fatal($"bad config: {e.message}"),
  case Error e => log.fatal($"unknown: {e}"),
}
```

`match` (Stash already has match expressions) gains first-class destructuring of error subtypes and their fields.

### 3.8 The full sysadmin example — same scenario, three flavors

**Today (exception-style):**
```stash
fn deploy(host) {
  try {
    let cfg = fs.readFile($"/etc/deploy/{host}.conf");
    let port = conv.toInt(cfg);
    let result = $!(ssh ${host} systemctl restart app);
  } catch (e) {
    log.error($"deploy of {host} failed: {e.message}");
    return false;
  }
  return true;
}
```

**Proposed (errors-as-values + propagation):**
```stash
fn deploy(host: string) {
  let cfg    = fs.readFile($"/etc/deploy/{host}.conf")!;
  let port   = parseInt(cfg)!;
  let result = sh!($"ssh {host} systemctl restart app")!;
  return ok;
}

for (let host in hosts) {
  let r = deploy(host);
  if (r is Error) {
    log.error($"deploy of {host} failed: {r.message} (chain: {r.causeChain})");
    continue;             // junior sysadmin can read this top to bottom
  }
}
```

**Proposed (when fail-fast IS what you want):**
```stash
try {
  for (let host in hosts) {
    deploy(host)!;        // first failure unwinds
  }
} catch (e) {
  log.fatal(e);
  exit(1);
}
```

The point: **the *same primitives* compose into both patterns**. The user picks "continue past errors" vs "stop on first error" by choosing whether to `!`-propagate or to inspect the value.

---

## 4. The Error Value

### 4.1 Type definition (conceptual)

```stash
struct Error {
  type:       string,           // canonical type name; e.g. "IOError"
  message:    string,           // human-readable, present on every error
  span:       SourceSpan?,      // where it originated, when known
  cause:      Error?,           // nested cause (linked-list of causes)
  stack:      StackFrame[]?,    // captured at error construction
  data:       dict,             // open-ended structured payload
  // Subtypes add typed fields via struct extension; see 4.3
}
```

All fields except `type` and `message` are nullable / empty-by-default. The runtime guarantees `type` and `message`.

### 4.2 Subtypes are real types, not strings

Today, `ErrorTypeRegistry` is a `HashSet<string>`. The new design promotes them to **first-class struct types** (in fact today's `Error Type System` spec already started this — see `/home/heisen/stash-lang/.kanban/4-done/Error Type System — Built-in Error Types and Struct Throw Semantics.md`).

```stash
struct IOError extends Error {
  path:  string?,
  op:    string?,           // "open", "read", "write", "unlink", ...
  code:  string?,           // POSIX errno string ("ENOENT", "EACCES", ...)
}

struct CommandError extends Error {
  command:   string,
  argv:      string[],
  exitCode:  int,
  stdout:    string,
  stderr:    string,
  signal:    Signal?,
  duration:  duration,
}

struct TimeoutError extends Error {
  elapsed:   duration,
  budget:    duration,
}
```

Pattern matching destructures by struct type:
```stash
match err {
  case IOError { code: "ENOENT", path: p } => createFile(p),
  case IOError { code: "EACCES" }          => requestSudo(),
  case CommandError { exitCode: 137 }      => log.warn("OOM-killed; backing off"),
}
```

### 4.3 User-defined errors

Just declare a struct that extends `Error`:

```stash
struct DeployError extends Error {
  host:   string,
  phase:  string,        // "fetch" | "build" | "restart"
}

fn deploy(host) {
  let r = fetch(host);
  if (r is Error) {
    return DeployError {
      message: $"fetch failed for {host}: {r.message}",
      host:    host,
      phase:   "fetch",
      cause:   r,
    };
  }
  // ...
}
```

`is Error` matches any subtype. `is DeployError` matches only that one.

### 4.4 Cause chains and source attribution

Every error remembers what caused it. Errors propagated via `!` automatically thread `cause`:

```stash
fn loadConfig(path) {
  return fs.readFile(path)!;     // returns IOError on ENOENT
}

fn boot() {
  let cfg = loadConfig("/etc/app.json")!;
  // If readFile failed: IOError flows up unchanged (no synthetic wrapper).
  // The call stack is captured at the original throw site.
}
```

But `!` on a *function* whose return type is a *new error subtype* should auto-wrap:

```stash
fn fetchManifest(host) -> _ | DeployError {     // error-type annotation (opt-in, see 8)
  let body = http.get($"http://{host}/manifest.json")!;
  // Implicit wrap: HTTPError → DeployError { phase: "fetch", host, cause: <inner> }
}
```

This is the one place we need a small DSL: `-> T | DeployError` says "any propagated error in this function gets wrapped as a `DeployError` with `cause` set." Otherwise the model is "errors flow up unchanged."

### 4.5 Stringification

`str(err)` produces a multi-line, indented chain:

```
DeployError: fetch failed for host01.prod
  caused by: HTTPError: 503 Service Unavailable
  caused by: TimeoutError: deadline 5s exceeded
  at deploy.stash:42:14
```

Junior sysadmin reads it, knows what to do.

### 4.6 Predicate matching helpers

```stash
err is Error                  // any error
err is IOError                // exactly IOError
err matches { code: "ENOENT" }   // structural match on data fields
err.is("IOError")             // dynamic, when type is in a variable
```

---

## 5. Stdlib Contract

### 5.1 The rule

> Every stdlib function that can fail returns an `Error` value as part of its return type. **No stdlib function panics on a recoverable failure.** Panics are reserved for: out-of-bounds programmer errors (`arr[1000]` on a 3-element array), invalid arguments where the caller has clearly violated a precondition (`str.split(null, ",")` — passing wrong type), and resource exhaustion.

### 5.2 The taxonomy

For every stdlib namespace, partition each function into one of three buckets:

| Bucket           | Behavior                                  | Example                                |
| ---------------- | ----------------------------------------- | -------------------------------------- |
| **Infallible**   | Always returns `T`, never errors          | `str.upper(s)`, `arr.len(a)`, `math.abs(x)` |
| **Fallible**     | Returns `T \| Error`                      | `fs.readFile(p)`, `http.get(url)`, `parseInt(s)` |
| **Panicking**    | Returns `T`; panics on programmer error   | `arr.get(a, i)` — out of bounds → panic |

The third bucket is small and is for true *bugs* (out-of-range, null-argument, type mismatch). It deliberately does NOT return `Error` because pretending those are recoverable lies to the reader.

For the second bucket, the documentation MUST list which `Error` subtypes it can return:

```
fs.readFile(path: string) -> string | IOError
fs.readFile may return:
  IOError { code: "ENOENT" } — file not found
  IOError { code: "EACCES" } — permission denied
  IOError { code: "EISDIR" } — path is a directory
```

This is documentation, not a type signature with checked exceptions. The static analyzer surfaces it on hover.

### 5.3 Predicates and "missing-vs-error"

`dict.get(d, key)` should return `null` for "missing key", not `Error`. Missing is *not* an error — it is an expected outcome of looking up a value. Reserve `Error` for *operational failure*.

A useful guideline: "If the docs would say 'returns null for missing', use null. If they would say 'fails', use Error."

`str.find(s, "needle")` → returns `int` (index) or `null` (not found). Not an error.
`fs.readFile(p)` → returns `string` or `IOError`. The file system saying "no" *is* an error.

The distinction: **null is for absent values. Error is for failed operations.** Sysadmins pattern-match this all day; the language should reflect it.

---

## 6. Shell Integration

This is the crown jewel of Stash. The error model has to make `$(...)` *delightful*.

### 6.1 The four shell command sigils

| Syntax     | Returns                                | On non-zero exit                      |
| ---------- | -------------------------------------- | ------------------------------------- |
| `$(cmd)`   | `CommandResult { stdout, stderr, exitCode }` | Returns normally; `exitCode != 0`. |
| `$!(cmd)`  | `string` (stdout)                      | **Returns** `CommandError`.           |
| `$?(cmd)`  | `int` (exit code only, no capture)     | Always returns; never errors.         |
| `$&(cmd)`  | `Process` handle (streaming)           | Errors surface via `.wait()` result.  |

The proposed change vs today: **`$!(cmd)` *returns* `CommandError`, it does not *panic*.** Combine with `!` for propagation:

```stash
let manifest = $!(curl -fsS https://example.com/m.json)!;
```

Read this aloud: "command bang — fail nonzero — bang — propagate." It is dense, but every character is doing work, and after one read it is obvious.

Today, `$!(...)` throws (panics). That violates "errors are values" and forces every shell-command-heavy function into a `try`/`catch` block. The proposal makes shell commands first-class participants in the value model.

### 6.2 `set -e` analogue — the strict scope

For long shell-heavy scripts, opt into "any non-zero exit code is a propagated error":

```stash
strict {
  $(cd /opt/app);
  $(git pull);
  $(systemctl restart app);
}
```

Inside `strict { ... }`, `$(cmd)` desugars to `$!(cmd)!`. Every command that fails propagates immediately. This is the opt-in equivalent of `set -euo pipefail`, but lexical and visible.

### 6.3 Pipelines

```stash
let lines = $(grep ERROR /var/log/syslog | head -20)!;
```

Pipelines fail when any stage fails. The propagated `CommandError` includes which stage failed (`failingStage: int`).

### 6.4 Parallel fanout

Sysadmin gold:

```stash
let results = par.map(hosts, fn(h) {
  return $!(ssh ${h} uptime)!;     // returns string OR CommandError per host
});

let (ok, failed) = arr.partition(results, fn(r) => !(r is Error));
log.info($"succeeded: {len(ok)}, failed: {len(failed)}");
for (let f in failed) {
  log.error($"  {f.command}: exit {f.exitCode}");
}
```

Note: inside a `par.map` worker, `!` returns the error *from that worker*, not from the outer scope. The outer code gets a list-of-(value-or-error). This is exactly the pattern sysadmins want.

---

## 7. Control Flow Primitives

### 7.1 Inventory

| Primitive       | Purpose                                          | Status               |
| --------------- | ------------------------------------------------ | -------------------- |
| `!`             | Propagate Error out of fallible scope            | **NEW**              |
| `try expr`      | Capture panic into Error value                   | KEEP (semantics change slightly) |
| `try { } catch` | Block-scoped panic recovery, pattern-matched     | KEEP                 |
| `panic`         | Unwinding throw (rename of `throw`)              | RENAME               |
| `defer`         | LIFO cleanup, runs on normal AND panic exit      | KEEP                 |
| `retry`         | Bounded retry loop with backoff/until/onRetry    | KEEP, semantics tweak |
| `match`         | Pattern matching, including error subtypes       | EXTEND               |
| `??` / `??=`    | Default-on-null-or-error                          | KEEP                 |

### 7.2 `retry` interactions

Retry's `on:` filter today uses string type names. Promote to actual types:
```stash
retry (5, on: [NetworkError, TimeoutError]) { fetch()!; }
```

Inside the retry body, `!` propagates errors *to the retry harness*, not past it. The retry harness inspects whether the error matches the `on:` filter; if yes, retries; if no, returns the error from the retry expression. This is a clean composition — `!` becomes the bridge.

### 7.3 `defer` and panics

`defer` already runs on both normal exit and panic — keep that. With errors-as-values, `defer` ALSO needs to run when a function returns via `!`-propagation. Today's `defer` semantics handle "function exits"; we extend "exits" to include propagated returns. No new syntax.

Suppressed errors: if a `defer` handler itself fails, attach to `error.suppressed[]` rather than overwriting. (Today's `RuntimeError.SuppressedErrors` already does this — keep.)

### 7.4 Panic vs Error — the rule

> **Panic is for "this script is now broken." Error is for "this operation didn't work."**

A divide-by-zero is a panic (the script is broken).
A missing file is an Error (the operation didn't work).
An assertion failure is a panic (an invariant is violated).
A 503 from an HTTP call is an Error (the operation didn't work).
An OOM is a panic.
A timeout is an Error.
An invalid type passed to a built-in is a panic.
A user-supplied invalid config is an Error.

### 7.5 Top-level (script root) behavior

A script that ends with an unhandled propagated `Error` exits with status code derived from the error type (see 7.6). A script with an unhandled panic exits 1 and prints the full panic chain. Both print structured output to stderr.

### 7.6 Exit-code mapping

| Error subtype       | Exit code |
| ------------------- | --------- |
| Normal completion   | 0         |
| `ValueError`        | 64        |
| `IOError`           | 66        |
| `CommandError`      | passes through `exitCode` if set, else 1 |
| `TimeoutError`      | 124       |
| `CancellationError` | 130       |
| Other/panic         | 1         |

These are the BSD `sysexits.h` codes. Sysadmin tooling already consumes them.

---

## 8. Static Analysis & Tooling

### 8.1 What the analyzer can catch (without explicit annotations)

- **Unhandled error values.** `let x = parseInt(s);` followed by use of `x` as an int without checking → diagnostic. Either `!`-propagate, `??`-default, or `if (x is Error)` check.
- **`!` outside fallible scope.** Compile-time error.
- **Unreachable `catch` arm.** `catch (Error)` followed by `catch (IOError)` — the second is dead.
- **Forgotten `!` on a known-fallible call.** `fs.readFile(p).split("\n")` — `.split` on a `string | Error` is a type error; suggest `!`.
- **Suspicious `try` around an infallible expression.** Warning.
- **Panic in stdlib-bucket-2 function.** Internal lint: stdlib functions classified as "fallible" must not panic.

### 8.2 Opt-in error-type annotations

```stash
fn deploy(host: string) -> bool | DeployError | TimeoutError {
  // ...
}
```

When annotations are present, the analyzer **enforces** that only those error types escape (others must be caught/wrapped/converted). This gives Rust-level rigor to people who want it, without forcing ceremony on people who don't.

### 8.3 LSP

- **Hover on a fallible expression** shows `string | IOError` — the user sees the error possibility on every hover.
- **Hover on `!`** shows "propagates IOError | ParseError" — the union of subtypes flowing through.
- **Diagnostic codes** for all rules above; suppressible via `// stash-disable-next-line SAxxxx` (already supported).
- **Quick fixes**: "Add `!` to propagate", "Wrap with `try`", "Add `?? default`".
- **Completion** on an `Error` value offers `.message`, `.type`, `.cause`, plus subtype-specific fields (`.exitCode` on `CommandError`).

### 8.4 DAP

- **"Stop on caught error"** breakpoint mode — pause when *any* `Error` value is constructed, even before propagation/handling. Sysadmin debugging gold.
- **"Stop on panic"** is the default.
- **Variable inspector** renders error chains as expandable trees.
- Inline value reporting (DAP feature already in done/) shows the resolved type union next to each variable.

### 8.5 REPL

Unhandled error from a top-level expression renders as a styled multi-line block:

```
> let x = fs.readFile("/nope");
> x
IOError: no such file or directory
  path:    /nope
  op:      open
  code:    ENOENT
  at:      <repl>:1:9
```

Not a stack trace dump — a structured error card. Junior-friendly.

---

## 9. Migration / Coexistence

A clean-sheet adoption is unrealistic in one step. A pragmatic v1:

**v1 (the smallest viable subset):**
1. Implement `!` propagation operator (parser + compiler + a new `Propagate` opcode).
2. Convert ~10 high-traffic stdlib functions to return `Error` instead of throw: `parseInt`, `parseFloat`, `fs.readFile`, `fs.writeFile`, `http.get`, `json.decode`, `dict.get` (already null), `env.get` (already null), `time.parse`. Old throw-based behavior available behind `_strict` suffix or a `legacy` capability flag.
3. Rename `throw` → `panic`; keep `throw` as a deprecated alias for one minor version.
4. Rework `$!(cmd)` from "panic on nonzero" to "return CommandError on nonzero." Old behavior available via explicit `panic`: `let r = $!(cmd); if (r is Error) panic r;`.
5. Document the panic-vs-error rule prominently.

**v2:**
1. `strict { }` block.
2. Error-type annotations (`-> T | E`) and the analyzer rules in 8.1.
3. `match` extensions for error-subtype patterns.
4. Implicit wrap in functions with annotated error type.

**v3:**
1. Per-namespace audit of all 35 namespaces against the section 5 contract. Anything that throws on a recoverable condition is reclassified.

The deprecation plan keeps `throw`/`try expr` working through v2 with deprecation warnings, removed in v3. The existing kanban already has migration plumbing (Diagnostic Codes & Suppression spec is done), so the warning UX is solved.

---

## 10. Open Questions and Honest Tradeoffs

This is the section where I admit the design hurts in places.

### 10.1 The `!` looks like negation

`fs.readFile(p)!` — for a programmer used to `!x` meaning "not x," this is jarring. Swift and Rust have the same complaint and shipped anyway. The mitigation is that postfix `!` in Stash NEVER means negation (Stash's negation is `!x`, prefix-only). The lexer/parser disambiguates by position. But in code review, `value!` is going to look like "not value" to fresh eyes for a while. **I considered alternatives**: `?` (Rust-like), `try` prefix (Swift-like). `?` is taken (could re-purpose); `try` is verbose (`let n = try parseInt(s);` four extra characters per call). I'd want to prototype both and run user testing.

### 10.2 "Errors are just values" leaks abstraction

Today, you can write `let n = parseInt(s); io.println(n + 1);` — and if `parseInt` throws, the `+1` never runs. With errors-as-values, `n + 1` would *succeed* with a bogus result if we're not careful — `Error + 1` would have to be a runtime panic, and that's a *new* class of bug ("I forgot the `!`"). The static analyzer catches it (8.1), but unannotated dynamic code hits it at runtime. **Mitigation**: arithmetic / indexing / field-access on an `Error` value is a panic, and the panic message says "did you forget `!`?". Acceptable, but ugly.

### 10.3 The fallibility infection is real, just smaller

If I add `!` to one call, callers might need `!`. That's the propagation chain. It's still strictly better than checked exceptions, but it's not free. The `!` chain is visible, which is good — but in some scripts it gets noisy. The retort: that noise is *meaningful*, telling you "this whole pipeline is fallible." Bash hides it; Stash will not.

### 10.4 The shell `$!(cmd)!` pattern is syntactically dense

`$!(...)` then immediately `!` reads as line noise. A possible sugar: `$!!(cmd)` collapses both — but that's two new sigils, and "minimal sigils" is a Stash value. I'd defer this until usage shows the friction is real.

### 10.5 Performance of the open Error type

Every fallible call site potentially returns a heap-allocated `StashInstance`. With register-VM, that's allocation pressure. Today's exception path is also heap-allocating (a `RuntimeError`), so it's not a regression — but it's not a win either, except on the "common-case-no-error" path where exceptions cost zero (no construction) and Result-style returns... also cost zero, since no Error is constructed when none happens. So for the common case, errors-as-values is *strictly faster* than exception-throw. The cold path (lots of errors) is roughly equivalent. Net: small win.

### 10.6 Effect on `defer`

`defer` runs on `!`-propagation? Yes, in the proposal. But that means `!` is no longer a simple "return the value early" — it's a "structured exit" that runs deferreds. Implementation-wise this is a new opcode (`PropagateError`) that the VM treats like a return-with-defers. Not free, but already paid for: defer infrastructure exists.

### 10.7 The "open hierarchy" tax

Because `Error` is open and structural, `match` cannot prove exhaustiveness. The exhaustive-match analyzer (already a done spec) explicitly bows out for open types. We accept that pattern matches on errors are non-exhaustive in general, with a "default arm or warning" rule. Closed user-defined enums can still be exhaustive.

### 10.8 What I'd prototype first

Two probes, before committing to the full design:

1. **Convert `parseInt` and `fs.readFile` to errors-as-values for one release**, ship the `!` operator, and see how 50 real Stash scripts in `examples/` change. Measure: do they get shorter? More readable? Are bugs caught earlier?
2. **A/B the `!` vs `?` choice on a focus group of sysadmins.** Half the cohort writes scripts with `!`, half with `?`. Survey readability. The Swift/Rust community fights about this every year; I'd rather have data.

Without those prototypes I would not green-light the full migration. The vision is sound; the syntax is the bikeshed and bikesheds matter.

---

## 11. Decision Log

| Date       | Decision                                                                                          | Status     |
| ---------- | ------------------------------------------------------------------------------------------------- | ---------- |
| 2026-05-10 | Initial vision drafted: errors-as-values + `!` propagation + `panic` for unwinding. Hybrid model. | Proposed   |
| —          | Choose between `!` and `?` for propagation operator                                               | Open       |
| —          | Keep `try expr` semantics (catch-panic-into-value) or remove it                                   | Open       |
| —          | Define implicit-wrap rules for annotated error types                                              | Open       |
| —          | Audit each of 35 stdlib namespaces against the section 5 contract                                 | Open       |
| —          | Decide on `$!(cmd)` semantics change (return-error vs panic vs both, scoped via `strict`)         | Open       |

---

## Appendix A — Side-by-side reference

| Scenario                       | Today                                      | Proposed                                |
| ------------------------------ | ------------------------------------------ | --------------------------------------- |
| Read a file, fall back         | `let s = try fs.readFile(p) ?? "default";` | `let s = fs.readFile(p) ?? "default";`  |
| Read a file, propagate failure | `let s = fs.readFile(p);` (throws)         | `let s = fs.readFile(p)!;`              |
| Read a file, inspect locally   | `let s = try fs.readFile(p); if (s is Error) ...` | `let s = fs.readFile(p); if (s is Error) ...` |
| Strict shell                   | `let r = $!(cmd);` (throws)                | `let r = $!(cmd)!;` or `strict { $(cmd); }` |
| Validate then act              | `try { validate(); act(); } catch (e) { ... }` | `validate()!; act()!;` (in fallible scope) |
| Panic on assertion             | `throw ValueError { ... }`                 | `panic ValueError { ... }`              |
| Catch all unwinding            | `try { ... } catch (e) { ... }`            | unchanged                               |
| Retry transient                | `retry (5, on: ["NetworkError"]) { ... }`  | `retry (5, on: [NetworkError]) { ... }` |
