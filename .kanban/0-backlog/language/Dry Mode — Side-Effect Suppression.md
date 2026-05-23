# `dry` Mode — Side-Effect Suppression

**Status:** Design / In Review
**Created:** 2025-04-27
**Author:** Spec Architect
**Source:** Unique Language Concepts — Volume 2, Feature #4
**Conviction level:** High

---

## Table of Contents

1. [Overview](#1-overview)
2. [Architecture Decision: Can This Be Compiler-Only?](#2-architecture-decision-can-this-be-compiler-only)
3. [Syntax & Grammar](#3-syntax--grammar)
4. [Semantics](#4-semantics)
5. [Implementation Plan](#5-implementation-plan)
   - 5.1 Interface Layer — `IExecutionContext`
   - 5.2 Runtime Layer — `VMContext` & `StashEngine`
   - 5.3 VM Dispatch Layer — New Opcodes
   - 5.4 VM Opcode Handler Changes
   - 5.5 Stdlib Built-in Changes (Complete List)
   - 5.6 `sys` Namespace Additions
   - 5.7 CLI Flag
6. [AST & Parser](#6-ast--parser)
7. [Compiler](#7-compiler)
8. [Tooling](#8-tooling)
   - 8.1 Static Analysis
   - 8.2 LSP
   - 8.3 DAP
   - 8.4 Playground
   - 8.5 VS Code Extension (TextMate Grammar)
9. [Testing Strategy](#9-testing-strategy)
10. [Example Script](#10-example-script)
11. [Decision Log](#11-decision-log)
12. [Open Questions & Future Extensions](#12-open-questions--future-extensions)

---

## 1. Overview

**The problem:** Every deployment script needs a dry-run mode. Ansible has `--check`. Terraform has `plan`. `make` has `-n`. But no general-purpose scripting language bakes dry-run into the runtime. You always end up wrapping every side-effecting call in `if (!dryRun) { ... } else { println("Would do X") }` — tedious, error-prone, and doubles code. Someone always forgets to wrap a call.

**The solution:** A first-class `dry` mode where the Stash **runtime** suppresses side-effecting operations automatically, printing a `DRY:` description instead. Script authors write their real logic once; the runtime handles suppression. No code changes needed to add dry-run support to an existing script.

```stash
// Activate from the CLI:
// $ stash deploy.stash --dry

fn deploy(version, env) {
    let servers = config.read("servers.toml").servers

    for (let server in servers) {
        $(ssh deploy@${server} "systemctl restart app")
        // DRY: Would execute: ssh deploy@web-1 systemctl restart app

        fs.writeFile("/etc/app/config.toml", render(version))
        // DRY: Would write 847 bytes to /etc/app/config.toml
    }

    http.post("https://hooks.slack.com/...", json.stringify({ text: "Deployed!" }))
    // DRY: Would POST https://hooks.slack.com/... (42 bytes)
}

// Query from code:
if (sys.isDry()) {
    io.println("Running in dry-run mode — no changes will be made")
}

// Activate for a scope only (useful for testing dry behavior):
dry {
    dangerousOperation()  // suppressed
    $(rm -rf /tmp/old)    // suppressed
}
// Back to normal mode
```

---

## 2. Architecture Decision: Can This Be Compiler-Only?

**Short answer: No. VM changes are unavoidable, but they are minimal, targeted, and do not affect the hot dispatch loop.**

### Why the compiler alone cannot implement dry mode

Dry mode requires suppressing **three distinct categories of operations**:

#### Category 1 — Shell commands: `$(cmd)` and `$>(cmd)` (requires VM changes)

The `$(...)` and `$>(...)` syntax compiles to `OpCode.Command` (value 64) and is handled inside `ExecuteCommand()` in `VirtualMachine.Strings.cs`. This method calls `ExecCaptured()` / `ExecPassthrough()` directly — there is no stdlib function involved. **No amount of compiler transformation can intercept this without touching the VM handler.**

#### Category 2 — Built-in I/O functions (can be handled at stdlib level, but needs a flag from somewhere)

`fs.writeFile`, `http.post`, `process.spawn`, etc. are all stdlib built-ins that receive `IInterpreterContext ctx`. They can check a dry-mode flag on the context — but the flag must be _set by the VM_ (from the CLI `--dry` flag or the `dry { }` block). **The stdlib check is possible; the source of the flag requires VM/runtime involvement.**

#### Category 3 — Lock acquisition (requires VM changes)

`lock path { }` compiles to `OpCode.LockBegin` (96), which calls `FileLockHandle.Acquire()` directly inside the VM handler. There is even an existing `// TODO: dry mode — skip actual acquisition` comment at this exact line. This is confirmation from the original implementer that VM-level handling is expected.

### What the VM changes actually are (and what they are NOT)

**What they are:**

- Add `bool IsDryMode { get; }` to `IExecutionContext` interface (~2 lines)
- Add `bool IsDryMode { get; set; }` to `VMContext` (~2 lines)
- Add `DryRun = true/false` to `StashEngine` configuration (~3 lines)
- Add guard clauses in 3 opcode handlers: `ExecuteCommand`, `ExecuteLockBegin`, and `ExecutePipeChain` (~15 lines combined)
- Add two new opcodes: `DryBegin` (98) and `DryEnd` (99) for the `dry { }` block (~25 lines in dispatch + 5 lines in compiler)

**What they are NOT:**

- No changes to the main `RunInner<TDebugMode>()` dispatch loop's core logic
- No additional per-opcode overhead in the hot path — the dry-mode check is only in handlers that deal with I/O (`ExecuteCommand`, `ExecuteLockBegin`). These operations are I/O bound and never in tight computational loops. A single branch test here is noise compared to `execve()` syscall cost.
- `DryBegin`/`DryEnd` opcodes set a flag and return immediately — they are the cheapest possible opcodes. Since `dry { }` blocks always contain I/O operations (they have no purpose otherwise), these opcodes are never in hot loops.

### Comparison to `timeout` block (the precedent)

The `timeout` block (Timeout = 76) is the direct architectural precedent. It:

1. Has its own opcode
2. Modifies `VMContext` state (`_ct` field + `SetCancellationToken()`)
3. All downstream I/O operations check the modified state (CancellationToken)
4. Restores state in a `finally` clause

Dry mode follows **exactly the same pattern**, replacing CancellationToken with `IsDryMode`. The implementation complexity is lower than `timeout` because there is no linked CTS to create — just a boolean flip.

---

## 3. Syntax & Grammar

### 3.1 `dry { }` Block Statement

```ebnf
DryStmt ::= "dry" Block
Block   ::= "{" Statement* "}"
```

The `dry` keyword followed immediately by `{` is a dry statement. The block runs with dry mode active regardless of whether `--dry` was passed (dry blocks are cumulative: if already in dry mode, they remain in dry mode).

```stash
dry {
    // all side-effecting operations here are suppressed
    $(rm -rf /tmp/cache)
    fs.writeFile("output.txt", data)
    http.post(url, payload)
}
// back to previous dry mode state
```

**`dry { }` is an expression-statement** (the block evaluates to the last expression in the block, like all blocks in Stash). So this is valid:

```stash
let result = dry {
    processData(input)  // fs/network suppressed; pure computation works fine
}
```

### 3.2 Parser Disambiguation: `dry { }` vs. Future `dry.allow`

The parser disambiguates on the token following `dry`:

- `dry {` → `DryStmt`
- `dry .` → expression statement (the `dry` identifier starts a member-access expression)

This means `dry` is simultaneously a keyword (when followed by `{`) and a valid identifier (when followed by `.`). This is the same pattern Stash uses for `timeout`, `retry`, and `lock`. It enables a future `dry` namespace with functions like `dry.allow { }` without any grammar conflicts.

The precedent in the parser: `lock "path" { }` is parsed as `LockStmt` when `lock` is followed by an expression, but `lock` could hypothetically be an identifier in other contexts. The same disambiguation logic applies.

### 3.3 CLI Flag

```
stash <script> --dry
stash <script> --simulate        (alias)
```

When `--dry` or `--simulate` is present, `VMContext.IsDryMode` is set to `true` before execution. The flag applies to the entire script execution.

### 3.4 `sys.isDry()` — Programmatic Query

```stash
sys.isDry()   // → bool
```

Returns `true` if the current execution context is in dry mode (either via `--dry` CLI flag or within a `dry { }` block).

---

## 4. Semantics

### 4.1 What IS Suppressed (Complete List)

These operations **do not execute** in dry mode. Instead, they print a `DRY:` line to stderr and return a safe mock value.

| Operation                       | Dry output format                           | Mock return value                         |
| ------------------------------- | ------------------------------------------- | ----------------------------------------- |
| `$(cmd)`                        | `DRY: Would execute: <cmd>`                 | `{ stdout: "", stderr: "", exitCode: 0 }` |
| `$>(cmd)` (passthrough)         | `DRY: Would execute (passthrough): <cmd>`   | `{ exitCode: 0 }`                         |
| `process.spawn(cmd)`            | `DRY: Would spawn: <cmd>`                   | mock Process handle (see §4.4)            |
| `process.exec(cmd)`             | `DRY: Would execute: <cmd>`                 | `{ stdout: "", stderr: "", exitCode: 0 }` |
| `fs.writeFile(path, data)`      | `DRY: Would write <N> bytes to <path>`      | `null`                                    |
| `fs.writeFileBytes(path, data)` | `DRY: Would write <N> bytes to <path>`      | `null`                                    |
| `fs.appendFile(path, data)`     | `DRY: Would append <N> bytes to <path>`     | `null`                                    |
| `fs.delete(path)`               | `DRY: Would delete <path>`                  | `null`                                    |
| `fs.copy(src, dst)`             | `DRY: Would copy <src> → <dst>`             | `null`                                    |
| `fs.move(src, dst)`             | `DRY: Would move <src> → <dst>`             | `null`                                    |
| `fs.rename(path, name)`         | `DRY: Would rename <path> to <name>`        | `null`                                    |
| `fs.mkdir(path)`                | `DRY: Would create directory <path>`        | `null`                                    |
| `fs.mkdirAll(path)`             | `DRY: Would create directories <path>`      | `null`                                    |
| `fs.chmod(path, mode)`          | `DRY: Would chmod <path> to <mode>`         | `null`                                    |
| `fs.chown(path, user, group)`   | `DRY: Would chown <path> to <user>:<group>` | `null`                                    |
| `http.post(url, body)`          | `DRY: Would POST <url> (<N> bytes)`         | mock HTTP response (see §4.4)             |
| `http.put(url, body)`           | `DRY: Would PUT <url> (<N> bytes)`          | mock HTTP response                        |
| `http.patch(url, body)`         | `DRY: Would PATCH <url> (<N> bytes)`        | mock HTTP response                        |
| `http.delete(url)`              | `DRY: Would DELETE <url>`                   | mock HTTP response                        |
| `lock path { }`                 | `DRY: Would acquire lock <path>`            | body still executes                       |

> **Note on `lock`:** The lock file is not created and not acquired. The body of the `lock` block **still executes** (in dry mode, so its I/O is also suppressed). This mirrors how `timeout` still executes its body — the lock is the gate, not the operation.

### 4.2 What is NOT Suppressed (Complete List)

Reads, pure computation, and observability operations are **never suppressed**, even in dry mode:

| Category               | Examples                                                                                                     |
| ---------------------- | ------------------------------------------------------------------------------------------------------------ |
| Reads                  | `fs.readFile`, `fs.readFileBytes`, `fs.exists`, `fs.stat`, `fs.list`, `fs.glob`, `fs.tempDir`, `fs.tempFile` |
| HTTP reads             | `http.get`, `http.head`                                                                                      |
| Environment            | `env.get`, `env.all`, `env.home`, `env.user`                                                                 |
| System queries         | `sys.isDry`, `sys.platform`, `sys.arch`, `sys.hostname`, `sys.pid`, `sys.memUsage`, `sys.cpuCount`           |
| Logging                | ALL `log.*` functions — logging is observability, never suppressed                                           |
| I/O output             | `io.println`, `io.print`, `io.eprintln`, `io.eprint`                                                         |
| Configuration reads    | `config.read`, `config.parse`, `ini.read`, `ini.parse`                                                       |
| Data parsing           | `json.parse`, `json.stringify`, `csv.parse`, `yaml.parse`                                                    |
| String operations      | ALL `str.*`                                                                                                  |
| Array operations       | ALL `arr.*`                                                                                                  |
| Dictionary operations  | ALL `dict.*`                                                                                                 |
| Math                   | ALL `math.*`                                                                                                 |
| Time queries           | `time.now`, `time.utcNow`, `time.format`, `time.parse`, `time.since`, `time.until`                           |
| Crypto (read-like)     | `crypto.hash`, `crypto.hmac`, `crypto.randomBytes`, `crypto.randomStr`                                       |
| Encoding               | ALL `encoding.*`, ALL `conv.*`                                                                               |
| Path operations        | ALL `path.*`                                                                                                 |
| Type operations        | `typeof`, `is`, `len`                                                                                        |
| User-defined functions | Execute normally (their I/O side effects are suppressed by the above rules)                                  |

### 4.3 Output Format

All dry-mode messages are written to **stderr** (via `ctx.ErrorOutput`). The format is:

```
DRY: Would <verb> ...
```

Messages must use `RuntimeOps.Stringify()` for any value interpolation — this ensures **secrets are automatically redacted** in dry output:

```stash
let password = secret(env.get("DB_PASS"))
$(psql -U admin -p ${password} -c "VACUUM")
// DRY: Would execute: psql -U admin -p ****** -c VACUUM
// ↑ password is redacted, not leaked in dry output
```

### 4.4 Mock Return Values

**Shell commands** (`$(cmd)`, `$>(cmd)`, `process.exec`):

```stash
// Return value in dry mode:
{ stdout: "", stderr: "", exitCode: 0 }
```

**HTTP mutating methods** (`http.post`, `http.put`, `http.patch`, `http.delete`):

```stash
// Return value in dry mode:
{ status: 200, statusText: "OK", body: "", headers: {} }
```

**Process.spawn mock handle:**
The mock handle is a `StashInstance` of the `Process` struct type with:

```stash
{
    pid: 0,
    stdin: null,
    stdout: null,
    stderr: null,
}
```

Calling `.wait()` on a mock handle returns `{ exitCode: 0, stdout: "", stderr: "" }`.

**Rationale for returning "success" mocks:** Scripts often branch on the result of side-effecting operations:

```stash
let resp = http.post(url, payload)
if (resp.status != 200) {
    throw ValueError { message: "API call failed" }
}
// In dry mode, this check passes because mock returns status: 200
```

Returning synthetic success values allows the script to continue executing its full logical path, so the author can see _all_ the operations that would execute — not just the first one. This is the same behavior as Ansible's `--check` and Terraform's `plan`.

> **IMPORTANT caveat, which MUST be documented:** If a script branches on the _content_ of suppressed output (e.g., `if ($(kubectl get pods).stdout.contains("Error"))`), dry mode will not produce realistic content. The `dry.allow { }` escape hatch (§12) addresses this for future use. Document this limitation clearly.

### 4.5 Nested Dry Blocks and Idempotency

Entering a `dry { }` block when already in dry mode (via `--dry` or an outer `dry { }`) is a no-op for the flag — the mode is already active. The flag is treated as a counter (`_dryDepth: int`) rather than a simple boolean to support correct nesting:

```stash
dry {
    dry {           // _dryDepth: 2 — still in dry mode
        $(rm -rf /) // suppressed
    }               // _dryDepth: 1 — still in dry mode
    $(rm -rf /)     // suppressed
}                   // _dryDepth: 0 — back to normal (if --dry not set)
```

When `--dry` is set on the CLI, `_dryDepth` starts at 1. Exiting all `dry { }` blocks still leaves `_dryDepth = 1` (the CLI sets the floor). A `dry { }` block can never _disable_ dry mode — it can only activate or deepen it.

**Implementation:** `VMContext._dryDepth: int` (not `IsDryMode: bool`). `IsDryMode { get => _dryDepth > 0; }`.

### 4.6 Interaction with `timeout`

Dry mode and timeout compose cleanly — they are orthogonal state dimensions on VMContext. A timeout still counts down during a dry block (the reads that DO execute take time). Suppressed operations return instantly, making timeout expiry very unlikely in dry mode.

```stash
timeout 30s {
    dry {
        http.post(url, data)  // suppressed instantly, timeout not consumed
        fs.readFile("data.json")  // reads still execute and consume timeout
    }
}
```

### 4.7 Interaction with `lock`

Inside a `dry { }` block (or with `--dry`), lock _acquisition_ is suppressed. The lock body still executes (with its I/O suppressed). The DRY message is printed at `LockBegin` dispatch time.

```stash
dry {
    lock "/var/run/deploy.lock" {
        // DRY: Would acquire lock /var/run/deploy.lock
        deploy()   // deploy's I/O is also suppressed
    }
}
```

### 4.8 Interaction with `defer`

Deferred statements execute at function exit regardless of dry mode. If a `defer` was registered while in dry mode (inside a `dry { }` block), the defer's I/O will also be suppressed when it runs — because it runs in the same function frame which still has the dry context. If the `dry { }` block has already exited by the time the defer runs, the dry depth has been restored and the defer runs normally.

```stash
fn deploy() {
    defer fs.delete("/tmp/workdir")  // runs at function exit

    dry {
        defer fs.delete("/tmp/dryworkdir")  // also runs at function exit, but dry mode ends before then?
    }
    // _dryDepth restored here — defers registered inside dry{} run later, in NORMAL mode
}
```

> **Decision:** Deferred statements capture the dry-mode state at the time of **execution** (when the defer actually runs), not at registration time. This is consistent with Stash's defer semantics where closures capture by reference. When the defer runs (after `dry { }` exits and restores depth), it runs in the parent context — which may or may not be dry mode. This is the correct and intuitive behavior: defers are part of the enclosing function, not the `dry` block.

### 4.9 Error Handling

Errors thrown inside a `dry { }` block are propagated normally. The dry mode flag is restored in a `finally`-equivalent cleanup (guaranteed by the DryEnd opcode being emitted in a protected region). If the block throws and is caught by an outer `try/catch`, dry mode is correctly restored before the catch handler runs.

```stash
try {
    dry {
        $(invalid-command)   // suppressed, no exception from the command itself
        throw ValueError { message: "Dry block error" }  // throw still works
    }
} catch (e) {
    // dry mode is restored here — catch runs in normal mode (or outer dry mode)
    println(e.message)  // "Dry block error"
}
```

---

## 5. Implementation Plan

### 5.1 Interface Layer — `IExecutionContext`

**File:** `Stash.Core/Runtime/IExecutionContext.cs`

Add one property:

```csharp
/// <summary>Returns true if the current execution context is in dry-run mode.</summary>
bool IsDryMode { get; }
```

> **Why on `IExecutionContext` and not `IBuiltInContext`?** `IBuiltInContext` is the minimal interface for external stdlib authors. Dry mode is an internal runtime concept that should not surface to external stdlib packages — those packages cannot safely implement dry mode (they don't know all the invariants). Exposing it on `IExecutionContext` (which only Stash's own stdlib implementation sees) is the correct boundary. External package authors who want dry-mode support can call `sys.isDry()` from Stash code instead.

### 5.2 Runtime Layer — `VMContext` & `StashEngine`

**File:** `Stash.Bytecode/Runtime/VMContext.cs`

Add:

```csharp
private int _dryDepth;  // 0 = normal, >0 = dry mode active

public bool IsDryMode => _dryDepth > 0;
internal void EnterDry() => _dryDepth++;
internal void ExitDry() { if (_dryDepth > 0) _dryDepth--; }
```

**File:** `Stash.Bytecode/StashEngine.cs`

Add a `DryRun` property to engine options (following the same pattern as `StepLimit`, `EmbeddedMode`):

```csharp
public bool DryRun { get; set; }
```

Before calling `Execute()`, the engine propagates this to the VM:

```csharp
if (DryRun)
    _vmContext.EnterDry();   // sets initial floor of _dryDepth = 1
```

### 5.3 VM Dispatch Layer — New Opcodes

**File:** `Stash.Bytecode/Bytecode/OpCode.cs`

Add two opcodes after the current maximum (97):

```csharp
DryBegin = 98,   // A=unused, B=unused, C=unused — enters dry mode (increments _dryDepth)
DryEnd   = 99,   // A=unused, B=unused, C=unused — exits dry mode (decrements _dryDepth)
```

**Why not reuse existing mechanisms (like hidden built-in calls)?**

The `dry { }` block must guarantee restoration even when the body throws. This requires the `DryEnd` opcode to be emitted in the exception-handling table (just as `LockEnd` is), so the VM dispatches it unconditionally during unwinding. Hidden built-in calls cannot participate in the exception table — they can be skipped by an uncaught throw. Therefore, opcodes are the only safe mechanism.

**Performance note:** `DryBegin` and `DryEnd` do a single integer increment/decrement. They are not in any hot code path — they wrap blocks containing I/O operations. Their addition to the dispatch switch adds approximately 2 jump targets across 100 opcodes, which is imperceptible.

**File:** `Stash.Bytecode/VM/VirtualMachine.Dispatch.cs`

Add to the dispatch switch:

```csharp
case OpCode.DryBegin:
    _context.EnterDry();
    break;
case OpCode.DryEnd:
    _context.ExitDry();
    break;
```

**File:** `Stash.Bytecode/Bytecode/Metadata.cs`

No new metadata tag needed — `DryBegin`/`DryEnd` carry no operands.

### 5.4 VM Opcode Handler Changes

#### 5.4.1 `ExecuteCommand` — `$(cmd)` and `$>(cmd)`

**File:** `Stash.Bytecode/VM/VirtualMachine.Strings.cs`

After the command string is assembled and before `ExecCaptured` / `ExecPassthrough` is called:

```csharp
// Dry mode: suppress execution and print description
if (_context.IsDryMode)
{
    string verb = isPassthrough ? "Would execute (passthrough)" : "Would execute";
    _context.ErrorOutput.WriteLine($"DRY: {verb}: {command}");

    if (isPassthrough)
    {
        _stack[@base + a] = StashValue.FromInt(0);  // exitCode
    }
    else
    {
        var mockResult = /* build CommandResult StashInstance with empty stdout/stderr, exitCode=0 */;
        _stack[@base + a] = StashValue.FromObj(mockResult);
    }
    return;
}
```

#### 5.4.2 `ExecutePipeChain` — Pipeline commands `$(a | b | c)`

**File:** `Stash.Bytecode/VM/VirtualMachine.Strings.cs` (or wherever pipe chain is handled)

Same pattern: if `_context.IsDryMode`, print `DRY: Would execute pipeline: <cmd>` and return mock.

#### 5.4.3 `ExecuteLockBegin` — `lock path { }`

**File:** `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs`

The existing TODO is at the `FileLockHandle.Acquire()` call site:

```csharp
// TODO: dry mode — skip actual acquisition   ← existing comment
if (_context.IsDryMode)
{
    // Store a sentinel null handle so LockEnd can see it was a dry run
    _stack[@base + a] = StashValue.Null;   // errReg = null (no error)
    string pathStr = /* extract path from R(B) */;
    _context.ErrorOutput.WriteLine($"DRY: Would acquire lock {pathStr}");
    // Do NOT call FileLockHandle.Acquire — just let body execute
    return;
}
handle = FileLockHandle.Acquire(path, waitMs, staleMs, _ct);
```

`ExecuteLockEnd` must check whether the handle is null/sentinel before calling `handle.Release()`:

```csharp
// In ExecuteLockEnd:
var handle = /* get from stack */;
if (handle is null)
    return;  // dry mode — nothing to release
handle.Release();
```

### 5.5 Stdlib Built-in Changes (Complete List)

Each built-in that must be suppressed follows the same pattern:

```csharp
if (ctx.IsDryMode)
{
    ctx.ErrorOutput.WriteLine($"DRY: Would <verb> <description>");
    return <mock_value>;
}
// ... actual I/O operation ...
```

The `IInterpreterContext ctx` parameter gives access to `IsDryMode` (via `IExecutionContext`) and `ErrorOutput`.

#### `Stash.Stdlib/BuiltIns/FsBuiltIns.cs`

| Function                        | DRY message                                 | Mock return |
| ------------------------------- | ------------------------------------------- | ----------- |
| `fs.writeFile(path, content)`   | `DRY: Would write N bytes to <path>`        | `null`      |
| `fs.writeFileBytes(path, data)` | `DRY: Would write N bytes to <path>`        | `null`      |
| `fs.appendFile(path, content)`  | `DRY: Would append N bytes to <path>`       | `null`      |
| `fs.delete(path)`               | `DRY: Would delete <path>`                  | `null`      |
| `fs.copy(src, dst)`             | `DRY: Would copy <src> → <dst>`             | `null`      |
| `fs.move(src, dst)`             | `DRY: Would move <src> → <dst>`             | `null`      |
| `fs.rename(path, name)`         | `DRY: Would rename <path> to <name>`        | `null`      |
| `fs.mkdir(path)`                | `DRY: Would create directory <path>`        | `null`      |
| `fs.mkdirAll(path)`             | `DRY: Would create directories <path>`      | `null`      |
| `fs.chmod(path, mode)`          | `DRY: Would chmod <path> to <mode>`         | `null`      |
| `fs.chown(path, user, group)`   | `DRY: Would chown <path> to <user>:<group>` | `null`      |
| `fs.link(src, dst)`             | `DRY: Would create link <dst> → <src>`      | `null`      |
| `fs.symlink(src, dst)`          | `DRY: Would create symlink <dst> → <src>`   | `null`      |
| `fs.setPerms(path, perms)`      | `DRY: Would set permissions on <path>`      | `null`      |

**NOT suppressed in fs:** `fs.readFile`, `fs.readFileBytes`, `fs.readLines`, `fs.exists`, `fs.stat`, `fs.list`, `fs.glob`, `fs.tempDir`, `fs.tempFile`, `fs.size`, `fs.isDir`, `fs.isFile`, `fs.absPath`, `fs.resolve`, `fs.join`.

#### `Stash.Stdlib/BuiltIns/HttpBuiltIns.cs`

| Function                | DRY message                        | Mock return                                                        |
| ----------------------- | ---------------------------------- | ------------------------------------------------------------------ |
| `http.post(url, body)`  | `DRY: Would POST <url> (N bytes)`  | `{ status: 200, statusText: "OK", body: "", headers: {} }`         |
| `http.put(url, body)`   | `DRY: Would PUT <url> (N bytes)`   | same                                                               |
| `http.patch(url, body)` | `DRY: Would PATCH <url> (N bytes)` | same                                                               |
| `http.delete(url)`      | `DRY: Would DELETE <url>`          | `{ status: 204, statusText: "No Content", body: "", headers: {} }` |

**NOT suppressed:** `http.get`, `http.head` (reads, always safe).

#### `Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs`

| Function             | DRY message                     | Mock return                               |
| -------------------- | ------------------------------- | ----------------------------------------- |
| `process.spawn(cmd)` | `DRY: Would spawn: <cmd>`       | mock Process handle (see §4.4)            |
| `process.exec(cmd)`  | `DRY: Would execute: <cmd>`     | `{ stdout: "", stderr: "", exitCode: 0 }` |
| `process.kill(pid)`  | `DRY: Would kill process <pid>` | `null`                                    |

**NOT suppressed:** `process.list`, `process.find`, `process.pid`, `process.env`, `process.args`, `process.alive`.

#### `Stash.Stdlib/BuiltIns/SysBuiltIns.cs`

No suppression needed — `sys.*` functions are informational reads. `sys.isDry()` is added here (see §5.6).

#### What about `net.*`?

The `net` namespace is proposed in Feature #8 (health probes). Since it doesn't exist yet, this spec notes: when `net.probe` and `net.waitFor` are implemented, they MUST check `ctx.IsDryMode`. This should be part of the `net` namespace implementation spec.

### 5.6 `sys` Namespace Additions

**File:** `Stash.Stdlib/BuiltIns/SysBuiltIns.cs`

Add one function:

```csharp
ns.Function("isDry", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
{
    return StashValue.FromBool(ctx.IsDryMode);
}, description: "Returns true if the current execution is in dry-run mode.", returns: "bool");
```

**Stdlib reference entry:**

```
sys.isDry() → bool
```

Returns `true` if the runtime is currently in dry-run mode (either via the `--dry` CLI flag or within a `dry { }` block).

### 5.7 CLI Flag

**File:** `Stash.Cli/Program.cs`

Add flag parsing (following the same pattern as existing flags):

```csharp
else if (arg == "--dry" || arg == "--simulate")
{
    dryRun = true;
}
```

When creating the `StashEngine`:

```csharp
var engine = new StashEngine(capabilities)
{
    DryRun = dryRun,
    // ... other options
};
```

**Help text addition:**

```
--dry              Run in dry-run mode. Side-effecting operations are suppressed
                   and described instead of executed.
--simulate         Alias for --dry.
```

**REPL behavior:** The REPL does NOT enable `--dry` as an argument (REPL is interactive; dry mode in REPL would be confusing). `sys.isDry()` would return `false` in the REPL unless the user explicitly uses a `dry { }` block. A user can enable dry mode in the REPL by entering `dry { ... }` for a scope.

---

## 6. AST & Parser

### 6.1 New AST Node: `DryStmt`

**File:** `Stash.Core/Parsing/AST/DryStmt.cs`

```csharp
// Stash.Core/Parsing/AST/DryStmt.cs
namespace Stash.Core.Parsing.AST;

public sealed class DryStmt(BlockExpr body, SourceSpan span) : Stmt
{
    public BlockExpr Body { get; } = body;
    public override SourceSpan Span { get; } = span;
    public override TResult Accept<TResult>(IStmtVisitor<TResult> visitor) => visitor.VisitDryStmt(this);
}
```

### 6.2 Token Type

**File:** `Stash.Core/Lexing/TokenType.cs`

Add `Dry` to the keywords enum (alongside `Lock`, `Timeout`, `Defer`):

```csharp
Dry,
```

**File:** `Stash.Core/Lexing/Lexer.cs` (keyword table)

Add `"dry"` → `TokenType.Dry`.

### 6.3 Parser Change

**File:** `Stash.Core/Parsing/Parser.cs`

In the statement-parsing method (the large `switch` on current token type), add:

```csharp
case TokenType.Dry:
    return ParseDryStmt();
```

New method:

```csharp
private DryStmt ParseDryStmt()
{
    var span = Current.Span;
    Consume(TokenType.Dry);
    var body = ParseBlock();  // parses { ... }
    return new DryStmt(body, span.To(body.Span));
}
```

**Disambiguation logic:** The existing parser only reaches `ParseDryStmt()` from the statement-parsing switch when `TokenType.Dry` is the current token AND the parser is in statement context. If `dry` appears in expression context (e.g., `dry.allow { }` in a future version where `dry` is also a namespace identifier), the expression parser handles it as an identifier. This works because Stash uses a statement-first dispatch: the `TokenType.Dry` case in the statement switch is only checked at statement boundaries.

### 6.4 Visitor Interface Update

**File:** `Stash.Core/Parsing/AST/IStmtVisitor.cs`

Add:

```csharp
TResult VisitDryStmt(DryStmt stmt);
```

All six visitors must implement this:

| Visitor               | File                                                 | Implementation                                      |
| --------------------- | ---------------------------------------------------- | --------------------------------------------------- |
| `Compiler`            | `Stash.Bytecode/Compilation/Compiler.ControlFlow.cs` | Emit `DryBegin`, body, `DryEnd` in protected region |
| `SemanticResolver`    | `Stash.Analysis/Visitors/SemanticResolver.cs`        | Visit body                                          |
| `SemanticValidator`   | `Stash.Analysis/Visitors/SemanticValidator.cs`       | Validate body + emit SA090x rules                   |
| `SymbolCollector`     | `Stash.Analysis/Visitors/SymbolCollector.cs`         | Visit body                                          |
| `SemanticTokenWalker` | `Stash.Analysis/Visitors/SemanticTokenWalker.cs`     | Emit semantic token for `dry` keyword               |
| `StashFormatter`      | `Stash.Format/StashFormatter.cs`                     | Format: `dry {\n    body\n}`                        |

---

## 7. Compiler

**File:** `Stash.Bytecode/Compilation/Compiler.ControlFlow.cs`

### 7.1 `VisitDryStmt` Implementation

The compiler must guarantee `DryEnd` runs even if the body throws. This is achieved by emitting `DryEnd` into the exception handler table, just as `LockEnd` is emitted for lock blocks.

```
Emit(OpCode.DryBegin)

// Open a protected region (same mechanism as try/finally)
int exceptionTableEntry = BeginProtectedRegion(emitFinallyOnThrow: DryEnd_opcode)

// Compile the block body normally
VisitBlock(stmt.Body)

EndProtectedRegion(exceptionTableEntry)

Emit(OpCode.DryEnd)
```

The exception table entry causes the VM to dispatch `DryEnd` before re-raising any exception thrown from the body, restoring the dry depth even on error.

### 7.2 Serialization

**File:** `Stash.Bytecode/Serialization/BytecodeWriter.cs` / `BytecodeReader.cs`

`DryBegin` (98) and `DryEnd` (99) are zero-operand opcodes. No metadata tags needed. Serialization is automatic since opcode values are stored verbatim.

---

## 8. Tooling

### 8.1 Static Analysis

**File:** `Stash.Analysis/Models/DiagnosticDescriptors.cs`

Add diagnostic descriptors in the SA09xx range (dry mode):

| Code   | Severity | Name                            | Description                                                                                            |
| ------ | -------- | ------------------------------- | ------------------------------------------------------------------------------------------------------ |
| SA0900 | Warning  | `DryBlockContainsNoSideEffects` | `dry { }` block contains no side-effecting operations — the block has no effect                        |
| SA0901 | Warning  | `NestedDryBlocks`               | Inner `dry { }` block is redundant — already in dry mode from outer block or `--dry` flag context      |
| SA0902 | Info     | `ControlFlowDependsOnDryOutput` | Script branches on the result of a suppressed operation — behavior differs between dry and normal mode |

**Implementation notes:**

- **SA0900:** The `SemanticValidator` visits a `DryStmt` and checks whether the body contains any nodes that would be suppressed (shell commands, write built-in calls, etc.). If not, emit SA0900. This requires a simple recursive scan of the body's AST. The check can be conservative (only flag obviously-no-side-effect bodies like pure expressions).

- **SA0901:** Track a `_dryDepth: int` in `SemanticValidator` (analogous to `_tryDepth` for SA0813). Increment on entering `DryStmt.Body`, decrement on exit. If depth > 0 when entering another `DryStmt`, emit SA0901.

- **SA0902:** This is a best-effort, low-precision diagnostic. Flag patterns like `if ($(cmd).exitCode != 0) { ... }` or `if ($(cmd).stdout.contains(...)) { ... }` that appear inside or immediately after a `dry { }` block. This is hard to implement precisely; defer to v2 if complexity is too high.

**File:** `Stash.Analysis/Visitors/SemanticValidator.cs`

Add `VisitDryStmt`:

```csharp
public override Diagnostic? VisitDryStmt(DryStmt stmt)
{
    _dryDepth++;
    try
    {
        if (_dryDepth > 1)
            EmitDiagnostic(DiagnosticDescriptors.SA0901, stmt.Span);

        Visit(stmt.Body);

        if (!BodyContainsSideEffects(stmt.Body))
            EmitDiagnostic(DiagnosticDescriptors.SA0900, stmt.Span);
    }
    finally
    {
        _dryDepth--;
    }
    return null;
}
```

### 8.2 LSP

**File:** `Stash.Lsp/Handlers/CompletionHandler.cs`

Add `dry` to the keyword completion list.

**File:** `Stash.Analysis/Visitors/SemanticTokenWalker.cs`

In `VisitDryStmt`, emit a `keyword` semantic token for the `dry` keyword span.

**Hover:** The `dry` keyword and `sys.isDry()` should have hover documentation. The hover for `sys.isDry()` comes from its stdlib registration description. The hover for the `dry` keyword comes from a keyword hover provider — add an entry: `dry` → "Enters dry-run mode for the enclosed block. Side-effecting operations are suppressed and described instead of executed."

**Folding:** The `dry { }` body is a `BlockExpr` — folding is already handled generically by block folding logic.

### 8.3 DAP

**File:** `Stash.Dap/DebugSession.cs`

When the debugger displays the variables panel, add a synthetic variable showing the dry mode state:

```
[Runtime]
  dryMode: true    ← shown when IsDryMode is true
```

This is added to the "runtime state" section of the variables panel (if one exists alongside `lastError` and similar synthetic vars).

Breakpoints inside `dry { }` blocks work normally. Expression evaluation in the watch panel respects dry mode — the watch evaluator uses the current VMContext, so if `IsDryMode` is true, watch expression side effects are also suppressed. This is a **safety benefit**: debugging a dry-mode script doesn't accidentally cause real side effects through watch evaluation.

### 8.4 Playground

**File:** `Stash.Playground/wwwroot/js/stash-language.js` (Monarch tokenizer)

Add `dry` to the keywords array in the Monarch tokenizer so it receives keyword syntax highlighting.

The Playground's `PlaygroundExecutor` sandbox runs without real filesystem/network access — Playground I/O is already simulated. Adding `--dry` support to the Playground is **not necessary**. However, the `dry { }` keyword and `sys.isDry()` should work syntactically. Playground-mode already acts like a superset of dry mode (nothing actually executes). `sys.isDry()` returns `false` in the Playground (not technically dry mode — the Playground has its own sandboxing mechanism that is orthogonal to dry mode).

### 8.5 VS Code Extension — TextMate Grammar

**File:** `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json`

Add `dry` to the list of control-flow keywords (alongside `lock`, `timeout`, `defer`, `retry`):

```json
{
  "match": "\\b(dry)\\b",
  "name": "keyword.control.dry.stash"
}
```

Or add to the existing keyword alternation pattern.

---

## 9. Testing Strategy

**File:** `Stash.Tests/Bytecode/DryModeTests.cs` (new file)

### 9.1 Core Suppression Tests (MUST pass — zero tolerance for actual execution)

Each test must verify the suppressed operation **did not execute** (not just that dry output was printed):

```
// Shell commands
DryMode_ShellCommand_IsNotExecuted()
    // Create a temp file. In dry mode, $(rm tempFile) should not delete it.
    // Assert: file still exists after dry execution.

DryMode_ShellCommandPassthrough_IsNotExecuted()
    // $>(echo something) in dry mode — verify no output to stdout

DryMode_ShellCommand_ReturnsMockResult()
    // let r = dry { $(ls) }; assert r.exitCode == 0; assert r.stdout == ""

// File system
DryMode_FsWriteFile_DoesNotCreateFile()
    // dry { fs.writeFile("test_dry_output.txt", "data") }
    // Assert: file does not exist

DryMode_FsDelete_DoesNotDeleteFile()
    // Create a real file. dry { fs.delete(path) }. Assert: file still exists.

DryMode_FsCopy_DoesNotCopyFile()
    // Create src file. dry { fs.copy(src, dst) }. Assert: dst does not exist.

DryMode_FsMove_DoesNotMoveFile()
    // Create src file. dry { fs.move(src, dst) }. Assert: src still exists, dst does not.

DryMode_FsMkdir_DoesNotCreateDirectory()
    // dry { fs.mkdir("test_dry_dir") }. Assert: directory does not exist.

DryMode_FsAppendFile_DoesNotModifyFile()
    // Create file with "hello". dry { fs.appendFile(path, " world") }
    // Assert: file still contains only "hello"

// HTTP (use a mock HTTP server or check no network call was made)
DryMode_HttpPost_DoesNotSendRequest()
    // Start a local test HTTP server. dry { http.post(url, body) }
    // Assert: server received 0 requests.

DryMode_HttpPost_ReturnsMockResponse()
    // let r = dry { http.post(url, "{}") }
    // assert r.status == 200; assert r.body == ""

DryMode_HttpPut_IsNotExecuted()
DryMode_HttpPatch_IsNotExecuted()
DryMode_HttpDelete_IsNotExecuted()

// Process
DryMode_ProcessSpawn_DoesNotStartProcess()
DryMode_ProcessExec_DoesNotExecute()

// Lock
DryMode_Lock_DoesNotCreateLockFile()
    // dry { lock "/tmp/test.lock" { } }. Assert: /tmp/test.lock does not exist.

DryMode_Lock_BodyStillExecutes()
    // dry { lock "/tmp/test.lock" { result = 42 } }. Assert: result == 42.
```

### 9.2 Read-Through Tests (reads must still work in dry mode)

```
DryMode_FsReadFile_ExecutesNormally()
    // Write a file. dry { let c = fs.readFile(path) }. Assert: c == expected content.

DryMode_FsExists_ExecutesNormally()
DryMode_HttpGet_ExecutesNormally()
DryMode_EnvGet_ExecutesNormally()
```

### 9.3 `dry { }` Block Semantics Tests

```
DryMode_Block_RestoresDryModeAfterExit()
    // dry { }; assert !sys.isDry()

DryMode_Block_RestoresDryModeAfterException()
    // try { dry { throw ValueError { message: "x" } } } catch (e) { }
    // assert !sys.isDry() after the try

DryMode_NestedBlocks_CorrectDepth()
    // dry { assert sys.isDry(); dry { assert sys.isDry() }; assert sys.isDry() }
    // assert !sys.isDry()

DryMode_Block_CanReturnValue()
    // let x = dry { 42 }; assert x == 42

DryMode_Block_SuppressesInsideFunction()
    // fn sideEffect() { fs.writeFile("x.txt", "y") }
    // dry { sideEffect() } — assert file not created
```

### 9.4 `sys.isDry()` Tests

```
SysIsDry_ReturnsFalseByDefault()
SysIsDry_ReturnsTrueInsideDryBlock()
SysIsDry_ReturnsFalseAfterDryBlock()
SysIsDry_ReturnsTrueWithCliFlag()  // set DryRun = true on StashEngine
```

### 9.5 Mock Return Value Tests

```
DryMode_ShellCommand_ReturnsMockWithCorrectShape()
DryMode_HttpPost_ReturnsMockWithCorrectShape()
DryMode_ProcessSpawn_ReturnsMockWithCorrectShape()
```

### 9.6 DRY Output Format Tests

```
DryMode_ShellCommand_PrintsDryPrefix()
DryMode_FsWriteFile_PrintsBytesAndPath()
DryMode_HttpPost_PrintsMethodAndUrl()
DryMode_Lock_PrintsLockPath()
DryMode_SecretInCommand_IsRedacted()
    // let k = secret("abc"); dry { $(cmd ${k}) }
    // Assert: DRY output contains "******" not "abc"
```

### 9.7 Static Analysis Tests

**File:** `Stash.Tests/Analysis/DryModeAnalysisTests.cs`

```
SA0900_EmptyDryBlock_Warns()
SA0900_DryBlockWithOnlyReads_Warns()
SA0900_DryBlockWithWrite_DoesNotWarn()
SA0901_NestedDryBlocks_Warns()
SA0901_SingleDryBlock_DoesNotWarn()
```

### 9.8 `StashEngine.DryRun = true` Integration Test

```
Engine_DryRun_SuppressesEntireScript()
    // Create script: fs.writeFile("x.txt", "data"); $(echo hi)
    // Run with StashEngine { DryRun = true }
    // Assert: x.txt not created, echo not executed
```

---

## 10. Example Script

**File:** `examples/dry_mode.stash`

```stash
// dry_mode.stash — demonstrates the dry mode feature
//
// Run normally:  stash dry_mode.stash
// Run in dry mode: stash dry_mode.stash --dry

fn deployApp(version, target) {
    let configPath = "/etc/myapp/config.toml"
    let backupPath = "/var/backups/config-${time.format(time.now(), "yyyy-MM-dd")}.bak"

    io.println("Deploying v${version} to ${target}...")

    // Reads always happen — even in dry mode
    if (!fs.exists(configPath)) {
        throw ValueError { message: "Config file not found: ${configPath}" }
    }
    let config = fs.readFile(configPath)

    // Backup first (suppressed in dry mode)
    fs.copy(configPath, backupPath)

    // Write new config (suppressed in dry mode)
    let newConfig = str.replace(config, "version = .*", "version = \"${version}\"")
    fs.writeFile(configPath, newConfig)

    // Restart service (suppressed in dry mode)
    let result = $(systemctl restart myapp)
    if (result.exitCode != 0) {
        throw ValueError { message: "Failed to restart myapp: ${result.stderr}" }
    }

    // Notify (suppressed in dry mode)
    http.post("https://hooks.example.com/deploy", json.stringify({
        version: version,
        target:  target,
        status:  "success"
    }))

    io.println("Deploy complete.")
}

// Check if we're in dry mode and inform the user
if (sys.isDry()) {
    io.println("=== DRY RUN MODE — No changes will be made ===\n")
}

// You can also activate dry mode for just a scope:
dry {
    io.println("Testing dry mode locally:")
    fs.writeFile("/tmp/test.txt", "this should not appear")
    $(echo "this should not execute")
}

// Run the actual deployment
deployApp("2.4.1", "production")
```

---

## 11. Decision Log

### Decision 1: VM changes are required for `$(cmd)` suppression

**Chosen:** Modify `ExecuteCommand` in the VM.
**Rejected:** Compiler-only transformation (impossible — `$(cmd)` is an opcode, not a built-in call).
**Rationale:** Shell command execution is at the opcode level. No compiler transformation can intercept it without VM changes. The changes are minimal: ~10 lines in `ExecuteCommand`.
**Risk:** None — `_context.IsDryMode` is a simple boolean check added before the `ExecCaptured()` call.

### Decision 2: Use `_dryDepth: int` not `IsDryMode: bool`

**Chosen:** Integer depth counter, `IsDryMode => _dryDepth > 0`.
**Rejected:** Boolean flag that gets set/cleared.
**Rationale:** A boolean would be incorrectly cleared when exiting an inner `dry { }` block that was entered while `--dry` is globally active. The depth counter correctly handles: global dry floor (depth=1 from `--dry`) + block nesting (depth 2, 3...). Exiting all blocks brings depth back to 1 (the CLI floor), not 0.
**Risk:** None — integer arithmetic.

### Decision 3: New DryBegin/DryEnd opcodes (not hidden built-in calls)

**Chosen:** Opcodes 98 (`DryBegin`) and 99 (`DryEnd`) emitted by the compiler.
**Rejected:** Compile `dry { }` as calls to hidden global functions `__dryBegin()` / `__dryEnd()`.
**Rationale:** Hidden built-in calls cannot participate in the exception handler table. If the body of `dry { }` throws an uncaught exception, the `__dryEnd()` call would be skipped, leaking the depth increment. Opcodes can be emitted into the exception table (like `LockEnd`) and dispatched unconditionally during unwinding. This is the established pattern for all block-level constructs in Stash (timeout, lock, defer).
**Risk:** Two new opcodes in the dispatch switch. Impact is negligible — they are never in hot paths.

### Decision 4: `dry { }` is a statement, NOT a `dry` namespace

**Chosen:** `dry` is a keyword that parses as `DryStmt` when followed by `{`.
**Rejected (for now):** `dry` as a namespace with `dry.enter()`, `dry.exit()`, `dry.allow()`.
**Rationale:** The keyword approach follows the established pattern for `lock`, `timeout`, `defer`. It's cleaner and provides direct syntactic support. The `dry.allow { }` escape hatch is deferred to v2 (see §12) because implementing it requires either parser disambiguation (when `dry` followed by `.`) or a separate namespace, both of which add complexity. The core value of dry mode doesn't require `dry.allow` in v1.
**Risk:** If `dry.allow { }` is later added, the parser must be extended to handle `dry .` as an expression. This is precedented in the existing parser (keyword-as-identifier disambiguation).

### Decision 5: Mock returns are synthetic "success" values

**Chosen:** Suppressed operations return empty-but-valid mock results.
**Rejected:** Returning `null` for everything, or throwing a DryModeError.
**Rationale:** Scripts branch on command results. If `$(cmd)` returns `null`, the script would crash on `result.exitCode`. If it throws, dry mode cannot traverse the full script path. Returning a mock success value lets the script complete its full logical path, giving the author a complete picture of what would execute. This matches Ansible `--check` and Terraform `plan` behavior.
**Risk:** If the script depends on specific command output for logic (e.g., `if $(kubectl get pods).stdout.contains("CrashLoop")`), the mock output will cause different branching. This is a known limitation, documented in §4.4 and mitigated by the future `dry.allow { }` escape hatch.

### Decision 6: No dry mode in the Playground

**Chosen:** Playground does not support `--dry` and `sys.isDry()` returns `false` in Playground.
**Rationale:** Playground already sandboxes all I/O — it has its own capability-gate mechanism orthogonal to dry mode. Mixing the two adds complexity with no user benefit. Playground users can use `dry { }` blocks in their code (they compile and execute correctly) but the suppression messages would be confusing since nothing actually executes in the Playground anyway.

### Decision 7: Dry mode output goes to stderr

**Chosen:** `DRY:` messages → `ctx.ErrorOutput` (stderr).
**Rejected:** stdout.
**Rationale:** In scripts that capture output (e.g., `let lines = $(script.stash).stdout`), stdout must be clean for the caller. stderr is the correct channel for diagnostic/observability output (same as logging).

---

## 12. Open Questions & Future Extensions

### 12.1 `dry.allow { }` — Escape Hatch (v2)

The original proposal includes `dry.allow { }` to temporarily suspend dry mode for a scope:

```stash
dry {
    dangerousOperation()   // suppressed
    dry.allow {
        io.println("Deployment started")  // always executes
    }
}
```

This requires either:

- Parser disambiguation: `dry {` → DryStmt, `dry.allow {` → UndryStmt (a separate AST node)
- Or: `dry` as a namespace with `allow()` accepting a trailing block

The disambiguation approach is cleaner. The parser peeks ahead: `dry` + `.` → expression (namespace call), `dry` + `{` → DryStmt. If `dry.allow { }` is added as a trailing-block syntax, the parser treats `dry` as an identifier (namespace reference) when followed by `.`.

For v2: add `DryAllowStmt` (or implement as `dry.allow()` with trailing block), which decrements the dry depth for its scope and restores on exit.

**Implementation sketch:**

- New `UndryStmt` AST node (or a `DryAllowStmt`)
- Compiler emits `DryEnd` (decrements), body, `DryBegin` (increments back)
- Note: `DryEnd` when `_dryDepth == 1` (CLI floor) would set depth to 0, genuinely suspending dry mode even for `--dry` scripts. This is the intended behavior for `dry.allow` — it's an explicit escape hatch.

### 12.2 Verbose Mode — Show Mock Values

Currently dry mode always shows the mock return value description in the `DRY:` message. A future `--dry-verbose` flag could additionally print the mock value being returned.

### 12.3 Dry Mode Record / Audit Log

A `--dry-log=<file>` option that writes all suppressed operations to a JSON file — enabling scripted verification that the correct operations would execute.

### 12.4 Configurable Dry Output Prefix

Currently the prefix is `DRY:`. A future option could allow custom prefixes or quiet mode (suppress the DRY lines entirely, just skip execution).

### 12.5 `net.*` Namespace Dry Support

When the `net` namespace (Feature #8, health probes) is implemented:

- `net.probe()` — in dry mode, print `DRY: Would probe <host>:<port>` and return `false` (conservative: assume probe would fail to avoid hiding problems)
- `net.waitFor()` — in dry mode, print `DRY: Would wait for <host>:<port>` and return immediately

### 12.6 SA0902 — Branching on Suppressed Output

The static analysis rule for detecting `if ($(cmd).stdout.contains(...))` patterns is deferred to v2. The analysis would require tracking which variables hold results of suppressed operations — a lightweight form of taint analysis that is non-trivial to implement correctly in the existing `SemanticValidator`.

---

_Spec status: Ready for Orchestrator review. All mandatory checklist items from `language-changes.instructions.md` are addressed: documentation sections identified, tooling verified, example script outlined, test scenarios listed._
