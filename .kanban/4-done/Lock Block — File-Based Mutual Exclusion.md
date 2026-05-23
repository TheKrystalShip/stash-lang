# Lock Block — File-Based Mutual Exclusion

**Status:** Design / Ready for review
**Created:** 2025-04-27
**Author:** Architect
**Source:** Unique Language Concepts — Volume 2, §3

---

## 1. Overview & Motivation

Deployment scripts, cron jobs, and maintenance tasks must not run concurrently. The current Stash stdlib provides no mechanism for this. Scripts that try to solve it manually fall into one of three failure modes:

1. **Race conditions** — check-then-create is not atomic. Two processes can both see the lock file absent and both proceed.
2. **Stale locks** — if a script crashes without cleanup, the lock file remains forever, blocking all future runs.
3. **No signal cleanup** — `fs.delete()` in a `finally` block doesn't run on `SIGKILL` or abrupt VM exit.

The `lock` block solves all three at the language level. This is not achievable with a library — guaranteed cleanup on process termination requires runtime integration, and atomic lock acquisition requires OS-level file locking primitives.

**Design goal:** Make the correct thing the easy thing. A sysadmin should be able to add mutual exclusion to any script block in one line, without understanding `flock(2)`, `LockFileEx`, PID files, or signal handlers.

---

## 2. Syntax & Grammar

```bnf
lock_stmt    ::= "lock" path_expr lock_options? block
lock_options ::= "(" named_option ("," named_option)* ")"
named_option ::= IDENTIFIER ":" expr
path_expr    ::= call_expr        (* any expression evaluating to a string path *)
```

**Examples:**

```stash
// Minimal — block forever until lock is acquired:
lock "/var/run/deploy.lock" {
    deploy(version)
}

// Non-blocking — throw LockError immediately if already locked:
lock "/var/run/backup.lock" (wait: 0s) {
    backup()
}

// Wait up to 30 seconds before giving up:
lock "/var/run/job.lock" (wait: 30s) {
    runJob()
}

// Stale lock detection — steal if lock file is older than 1 hour:
lock "/var/run/nightly.lock" (stale: 1h) {
    nightlyMaintenance()
}

// Combined — wait 10s and steal stale locks:
lock "/var/run/sync.lock" (wait: 10s, stale: 2h) {
    syncData()
}

// Composition with retry and timeout:
retry (3, delay: 10s) {
    lock "/var/run/deploy.lock" (wait: 0s) {
        timeout 5m {
            deploy()
        }
    }
}
```

### 2.1 Parsing Classification

`lock` is a **statement** (like `elevate`, not like `retry` or `timeout`). Rationale:

- Lock is a **scope guard**, not a computation. Its purpose is to protect a critical section — it does not produce a value that callers need to consume.
- `return` inside a `lock` body means return from the enclosing function, exactly as it does everywhere in Stash. Making `lock` an Expr would require a second meaning for "value produced by the block" that is neither `return` nor any existing syntax — forcing the awkward trailing-bare-expression idiom.
- The natural way to extract a value from a critical section is an explicit variable assigned inside the block, which makes the intent obvious: the variable is declared outside, assigned inside. This is semantically meaningful — it signals to readers that the variable is shared-mutable state being protected.
- `elevate` is a Stmt for the same reason: it is a privilege scope guard, not a value producer. `lock` belongs in the same category.
- `retry` and `timeout` remain Exprs because they genuinely compute results: `let response = timeout 5s { http.get(url) }` is natural and common. The equivalent `lock` pattern is not.

`lock` **cannot** appear in expression position. `let x = lock ... { }` is a parse error.

### 2.2 Parsing Disambiguation

The parser must distinguish `lock "/path" (wait: 30s) { }` from `lock "/path" (someVar) { }` (where `(someVar)` might look like a call argument):

- After the path expression, if the next token is `(` followed by `IDENTIFIER ':'`, it is the named-options list.
- If the next token is `(` followed by anything else, it is a grouping expression (part of the path expression).
- If the next token is `{`, there are no options.

This is the same disambiguation pattern used in `ParseRetryExpr()` for named options vs. struct expressions.

### 2.3 Parser Location

Parsed in `LockStatement()`, called from `Statement()`, consistent with `ElevateStatement()`.

Token type: `TokenType.Lock` (new keyword).

**Keyword registration:** Add `"lock"` to the lexer keyword table. `lock` is not a contextual keyword — it is reserved.

---

## 3. Semantics

### 3.1 Lock Acquisition

The lock is an **OS-level advisory file lock** on the specified path:

- **Linux / macOS:** `flock(2)` exclusive lock (`LOCK_EX`) on the file descriptor. This is a BSD-style advisory lock — other processes that do not call `flock` are not blocked by it. This is acceptable for Stash scripts coordinating multiple instances of themselves.
- **Windows:** `LockFileEx` exclusive lock on the file range. Windows file locks are mandatory — other processes cannot read or write the locked region regardless of whether they call `LockFileEx`.

Implementation in .NET: Open a `FileStream` with `FileMode.OpenOrCreate`, `FileAccess.ReadWrite`, `FileShare.None`. On Unix this invokes `open(2)` + `flock(2)` internally. On Windows this invokes `CreateFile` + `LockFileEx`.

The lock file is created if it does not exist. The lock file is **never deleted on release** — it persists as an empty marker file. Deletion is only done after stale lock detection. This avoids the TOCTOU race between deleting and re-creating the file.

> **Rationale for not deleting:** If process A holds the lock, process B tries to acquire it, process A deletes + releases, then process B may acquire a new inode while process C also creates a new inode — two holders. File deletion + re-creation breaks lock correctness. Keeping the file and just releasing the OS lock avoids this.

### 3.2 Lock Options

| Option  | Type       | Default   | Description                                                                                                    |
| ------- | ---------- | --------- | -------------------------------------------------------------------------------------------------------------- |
| `wait`  | `duration` | `forever` | Maximum time to wait for lock acquisition. `0s` = non-blocking.                                                |
| `stale` | `duration` | none      | If the lock file's PID content is for a dead process AND the file is older than this duration, steal the lock. |

**`wait` semantics:**

- `wait: 0s` — attempt acquisition immediately; throw `LockError` if already held.
- `wait: Xs` — poll for the lock for up to `Xs`. Polling interval is 50ms (not user-configurable in v1). Throws `LockError` if timeout expires before acquisition.
- No `wait` option (default) — block indefinitely until the lock is acquired. This is the standard `flock` default behavior.

> **Warning on default:** Blocking indefinitely is standard but dangerous in automated contexts. The static analysis engine will emit SA0803 (see §7) when a `lock` block without a `wait` option is not enclosed in a `timeout` block or a `retry` block. This nudges users toward explicit timeout management without being prescriptive.

**`stale` semantics:**

1. On lock acquisition failure (another process holds the lock), check the lock file content for a PID.
2. If the PID is readable and the process is not running (checked via platform process API), AND the lock file mtime is older than the `stale` duration → steal the lock.
3. Stealing: Re-open the file exclusively. Write the current PID.
4. If the PID is still running, do not steal regardless of file age.

Lock file content format: `<PID>\n` (ASCII decimal, newline-terminated). Written by `LockBegin`. The PID is the current process PID at the time of lock acquisition.

### 3.3 Lock Release & Guaranteed Cleanup

Lock release is guaranteed in all exit paths:

1. **Normal block exit** — `LockEnd` opcode releases the OS file lock.
2. **Exception thrown inside block** — compiler-generated try/finally pattern ensures `LockEnd` runs before the exception propagates.
3. **Process exit (normal)** — VM registers an `AppDomain.ProcessExit` handler on first lock acquisition. All active lock handles are released.
4. **SIGINT / SIGTERM** — VM registers a `PosixSignalRegistration` (Unix) and `Console.CancelKeyPress` (Windows) on first lock acquisition. All active lock handles are released before process terminates.
5. **Unhandled exception** — `AppDomain.UnhandledException` handler releases all active lock handles before the default crash handler runs.

> **SIGKILL / TerminateProcess:** Cannot be caught. Lock files will remain but contain a dead PID. The `stale` option exists specifically for this case — a new invocation with `stale: Xh` will detect the dead PID and steal the lock.

The signal/exit handlers are registered once (lazily, on first `LockBegin`) and stored in `VMContext`. They reference a `List<FileLockHandle>` that tracks all active locks for this VM instance.

### 3.4 Nested Locks

Multiple `lock` blocks may be nested, provided the paths differ:

```stash
lock "/var/run/outer.lock" {
    lock "/var/run/inner.lock" {
        // Both locks held here
    }
    // Only outer lock held
}
```

Nested locks on the **same path** will deadlock on Unix (because `flock` is per-process-per-fd, and a second `flock` on the same fd from the same process is a no-op — but the VM tracks lock state so we can detect and throw). On Windows, a second `LockFileEx` on the same file from the same process will also deadlock.

**Static analysis** (SA0802) detects nested `lock` blocks on the same string literal path and reports an error (see §7).

**Runtime protection:** `VMContext` maintains the set of paths currently locked by this VM instance. If `LockBegin` is called with a path already in the locked set, it throws `LockError` with message `"Attempted to acquire lock on already-locked path (would deadlock): /path"`.

Lock acquisition order is tracked in `VMContext.ActiveLocks` (a `Stack<FileLockHandle>`). `LockEnd` pops the top of the stack and releases it.

### 3.5 No Return Value

`lock` is a statement and produces no value. It cannot appear in expression position.

To capture a value from within a critical section, declare the variable outside the block and assign inside:

```stash
let previousVersion = null
lock "/var/run/version.lock" {
    previousVersion = str.trim(fs.readFile("current_version.txt"))
    fs.writeFile("current_version.txt", "v2.0.0")
}
io.println("Previous version: ${previousVersion}")
```

This pattern is intentional — the variable declaration outside communicates to readers that `previousVersion` is shared-mutable state being protected by the lock. It is more explicit about intent than an implicit block return value.

### 3.6 Interaction with Existing Features

| Feature     | Interaction                                                                                                                                                                                                                                                           |
| ----------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `timeout`   | `lock` does not consume the ambient `CancellationToken` during the wait phase. The `wait` option is independent. To time-bound the entire lock + body, wrap in `timeout`.                                                                                             |
| `retry`     | Clean composition: `retry { lock(wait: 0s) { } }` polls for lock via retry's delay.                                                                                                                                                                                   |
| `defer`     | `defer` inside a `lock` block executes before `LockEnd` — deferred cleanup happens while the lock is still held. This is correct: the lock protects the cleanup operations too.                                                                                       |
| `try/catch` | Exceptions escape the `lock` block after `LockEnd` runs. `LockError` is catchable with `catch (LockError e)`.                                                                                                                                                         |
| `dry` mode  | When `sys.isDry()` is `true`, the lock block does NOT acquire the OS file lock. It prints `DRY: Would acquire lock: /path`. The body still executes (dry mode suppresses side effects, not control flow). This enables dry-running deployment scripts that use locks. |
| Playground  | Throws `NotSupportedError` immediately — file locking is unavailable in the WASM sandbox.                                                                                                                                                                             |

---

## 4. Error Model

### 4.1 New Built-In Error Type: `LockError`

A new entry in `StashErrorTypes` and `StdlibRegistry._globalStructs`:

```
LockError
```

Fields: `message` (string), `path` (string — the lock file path).

**Thrown when:**

- Lock acquisition times out (`wait: Xs` expired).
- Lock acquisition fails in non-blocking mode (`wait: 0s`).
- Nested lock on same path detected at runtime.
- `lock` is used in Playground sandbox.

**Not thrown for:** File system errors (permission denied, disk full, etc.) — those throw `IOError` as usual.

### 4.2 Error Type Registration

Add `LockError` to:

1. `Stash.Core/Runtime/ErrorTypes.cs` — `public const string LockError = "LockError";`
2. `Stash.Stdlib/Registry/StdlibRegistry.Types.cs` — new `BuiltInStruct` entry in `_globalStructs` with fields `message`, `path`.
3. `Stash.Stdlib/BuiltIns/GlobalBuiltIns.cs` — register the type so `throw LockError { message: "...", path: "..." }` works.

---

## 5. Cross-Platform Behavior

| Aspect              | Linux                                                            | macOS                      | Windows                                                               |
| ------------------- | ---------------------------------------------------------------- | -------------------------- | --------------------------------------------------------------------- |
| Lock primitive      | `flock(2)` LOCK_EX via `FileStream(FileShare.None)`              | Same as Linux              | `LockFileEx` via `FileStream(FileShare.None)`                         |
| Lock semantics      | Advisory (only Stash processes using `lock { }` are coordinated) | Advisory                   | Mandatory (OS enforces against all processes)                         |
| PID check for stale | `Process.GetProcessById()` + `/proc/<pid>/status`                | `Process.GetProcessById()` | `Process.GetProcessById()`                                            |
| Signal cleanup      | `PosixSignalRegistration` (SIGINT, SIGTERM, SIGHUP)              | Same as Linux              | `Console.CancelKeyPress`                                              |
| Lock file location  | Any writable path                                                | Any writable path          | Any writable path (backslash vs. forward slash: normalize before use) |
| Non-blocking flag   | `flock(2)` with `LOCK_NB`                                        | Same                       | `LOCKFILE_FAIL_IMMEDIATELY` via `LockFileEx`                          |
| Process exit hook   | `AppDomain.ProcessExit`                                          | Same                       | Same                                                                  |

**Path normalization:** The `path` argument is passed through `Path.GetFullPath()` before use. This ensures that `/var/run/deploy.lock` and `./deploy.lock` (from the same directory) are treated as the same path for duplicate-lock detection.

**Recommended lock file locations:**

| Platform | Recommended                     | Example                           |
| -------- | ------------------------------- | --------------------------------- |
| Linux    | `/var/run/` or `/tmp/`          | `/var/run/deploy.lock`            |
| macOS    | `/var/run/` or `/tmp/`          | `/var/run/myapp.lock`             |
| Windows  | `%TEMP%` or well-known app data | `${env.get("TEMP")}\\deploy.lock` |

The standard library should provide `sys.lockDir()` returning the platform-appropriate directory for lock files (future convenience, not required for v1).

---

## 6. Implementation Plan

### 6.1 Stash.Core Changes

#### 6.1.1 New AST Node: `LockStmt`

**File:** `Stash.Core/Parsing/AST/LockStmt.cs`

```csharp
namespace Stash.Core.Parsing.AST;

public sealed class LockStmt : Stmt
{
    public Token LockKeyword { get; }
    public Expr Path { get; }
    public List<(Token Name, Expr Value)>? Options { get; }   // null = no options
    public BlockStmt Body { get; }

    public LockStmt(Token lockKeyword, Expr path, List<(Token, Expr)>? options,
                    BlockStmt body, SourceSpan span)
        : base(span)
    {
        LockKeyword = lockKeyword;
        Path = path;
        Options = options;
        Body = body;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitLockStmt(this);
}
```

#### 6.1.2 IStmtVisitor Interface

**File:** `Stash.Core/Parsing/AST/IStmtVisitor.cs`

Add: `T VisitLockStmt(LockStmt stmt);`

#### 6.1.4 New Error Type Constant

**File:** `Stash.Core/Runtime/ErrorTypes.cs`

Add: `public const string LockError = "LockError";`

#### 6.1.5 Lexer Keyword

**File:** wherever keywords are registered (likely `Lexer.cs` keyword dictionary)

Add: `"lock"` → `TokenType.Lock`

Add `Lock` to `TokenType` enum.

#### 6.1.5 Parser

**File:** `Stash.Core/Parsing/Parser.cs`

Add `LockStatement()` called from `Statement()`, following the same structure as `ElevateStatement()`:

```
LockStatement():
  Consume(TokenType.Lock)
  lockKeyword ← Previous()
  path ← Call()   // same as timeout — parse path as call_expr
  options ← null
  if Check(TokenType.LeftParen) AND lookahead+1 is Identifier AND lookahead+2 is Colon:
      Advance()   // consume '('
      options ← ParseNamedOptions()
      Consume(TokenType.RightParen)
  body ← ParseBlock()
  return LockStmt(lockKeyword, path, options, body, span)
```

Named options parsing reuses the same pattern as `ParseRetryExpr()`.

---

### 6.2 Stash.Bytecode Changes

#### 6.2.1 New Opcodes

**File:** `Stash.Bytecode/Bytecode/OpCode.cs`

```csharp
LockBegin = 96,   // ABx: A=errorDestReg, B=pathReg, Bx=constIdx(LockMetadata)
LockEnd   = 97,   // A: no operands (A=0)
```

**Encoding:**

- `LockBegin` ABx format: A = scratch register for error capture if lock fails; B = path expression register; x = constant pool index for `LockMetadata`.
- `LockEnd` A format: no operands (A=0). The VM pops the active lock stack.

#### 6.2.2 New Metadata Record

**File:** `Stash.Bytecode/Bytecode/Metadata.cs`

```csharp
public sealed record LockMetadata(
    int OptionCount,   // number of (name, value) option pairs; -1 = struct expr (reserved)
    bool HasWait,      // wait option present
    bool HasStale      // stale option present
);
```

**Binary serialization** (for `.stashc` format):
| Field | Type | Size |
|-------|------|------|
| OptionCount | i32 LE | 4 bytes |
| HasWait | u8 | 1 byte |
| HasStale | u8 | 1 byte |
| **Total** | | **6 bytes** |

Update `BytecodeWriter` and `BytecodeReader` with `WriteLockMetadata` / `ReadLockMetadata`.

#### 6.2.3 Compiler Changes

**File:** `Stash.Bytecode/Compilation/Compiler.ControlFlow.cs`

Add `VisitLockStmt(LockStmt stmt)`, following the same file as `VisitElevateStmt`:

```
Register layout (consecutive from base register):
  base+0   pathReg       ← compiled path expression
  base+1   waitReg       ← compiled wait duration (if HasWait), else Null
  base+2   staleReg      ← compiled stale duration (if HasStale), else Null
  base+3   errReg        ← scratch for error in cleanup path

Emit sequence:
  1. Compile path expr → pathReg
  2. Compile wait option → waitReg (or LoadNull if absent)
  3. Compile stale option → staleReg (or LoadNull if absent)
  4. Emit: LockBegin(errReg=base+3, pathReg=base+0, metaConstIdx)
     (LockBegin may throw LockError/IOError — no cleanup needed, lock not held)
  5. Emit: TryBegin(errDest=base+3) → patch errorJumpOffset
  6. Compile body (statements only; result discarded)
  7. Emit: TryEnd
  8. Emit: LockEnd
  9. Emit: Jmp → endLabel
  10. Patch errorJumpOffset:
  11. Emit: LockEnd           ← guaranteed release on error
  12. Emit: Throw(base+3)     ← re-throw original error
  13. Patch endLabel
```

Note: no `resultReg` — `lock` is a statement and the body result is not captured or returned.

This pattern mirrors `VisitElevateStmt()` in `Compiler.Exceptions.cs` and ensures guaranteed `LockEnd` on any exit path from the block.

**File:** `Stash.Bytecode/Compilation/Compiler.cs` (or appropriate partial)

Add `VisitLockStmt` to the `IStmtVisitor<StashValue>` implementation dispatch.

#### 6.2.4 VM Execution

**File:** `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs`

Add `ExecuteLockBegin()` and `ExecuteLockEnd()` handlers:

```
ExecuteLockBegin(pathReg, waitReg, staleReg, errReg, meta):
  1. path ← Stringify(registers[pathReg])
  2. path ← Path.GetFullPath(path)   // normalize
  3. check VMContext.ActiveLocks for path → throw LockError if duplicate
  4. if sys.isDry():
       emit "DRY: Would acquire lock: {path}"
       push DryLockHandle(path) onto VMContext.ActiveLocks
       return
  5. wait ← registers[waitReg] is Duration ? registers[waitReg].TotalMs : -1 (infinite)
  6. stale ← registers[staleReg] is Duration ? registers[staleReg] : null
  7. handle ← FileLockHandle.Acquire(path, wait, stale)
     (throws LockError or IOError if acquisition fails)
  8. VMContext.ActiveLocks.Push(handle)
  9. if not already registered: register process exit/signal cleanup handlers

ExecuteLockEnd():
  1. if VMContext.ActiveLocks.Count == 0: return (defensive)
  2. handle ← VMContext.ActiveLocks.Pop()
  3. handle.Release()
     (releases OS file lock; does not delete lock file)
```

**File:** `Stash.Bytecode/Runtime/VMContext.cs`

Add:

```csharp
public Stack<FileLockHandle> ActiveLocks { get; } = new();
private bool _lockCleanupRegistered;

public void EnsureLockCleanupRegistered()
{
    if (_lockCleanupRegistered) return;
    _lockCleanupRegistered = true;
    AppDomain.CurrentDomain.ProcessExit += (_, _) => ReleaseAllLocks();
    AppDomain.CurrentDomain.UnhandledException += (_, _) => ReleaseAllLocks();
    // Unix signal handlers:
    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
    {
        PosixSignalRegistration.Create(PosixSignal.SIGINT,  _ => ReleaseAllLocks());
        PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => ReleaseAllLocks());
        PosixSignalRegistration.Create(PosixSignal.SIGHUP,  _ => ReleaseAllLocks());
    }
    else
    {
        Console.CancelKeyPress += (_, _) => ReleaseAllLocks();
    }
}

private void ReleaseAllLocks()
{
    while (ActiveLocks.TryPop(out var handle))
        handle.Release();
}
```

#### 6.2.5 FileLockHandle

**File:** `Stash.Bytecode/Runtime/FileLockHandle.cs` (new file)

```csharp
internal sealed class FileLockHandle : IDisposable
{
    public string Path { get; }
    private FileStream? _stream;

    private FileLockHandle(string path, FileStream stream)
    {
        Path = path;
        _stream = stream;
    }

    public static FileLockHandle Acquire(string path, long waitMs, Duration? stale)
    {
        // 1. Attempt to open/create the file with exclusive lock
        // 2. If waitMs == 0: non-blocking; throw LockError immediately on failure
        // 3. If waitMs > 0: poll with 50ms interval until acquired or timeout
        // 4. If waitMs < 0 (infinite): block using FileShare.None open retry loop
        // 5. If still locked and stale != null: read PID from file, check liveness
        //    - If dead: delete and retry → acquire
        //    - If alive: throw LockError
        // 6. On success: write current PID to file
        // 7. Return handle
    }

    public void Release()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose() => Release();
}
```

**On stale lock detection:**

```csharp
private static bool IsProcessAlive(int pid)
{
    try { Process.GetProcessById(pid); return true; }
    catch (ArgumentException) { return false; }
}
```

On Windows, PIDs can be reused. If `.NET` returns a process for the PID but its `StartTime` is newer than the lock file's mtime, treat as stale (the original locking process died and a new process got the same PID). Include this check in the spec as an implementation note.

#### 6.2.6 Opcode Dispatch

**File:** `Stash.Bytecode/VM/VirtualMachine.Dispatch.cs`

Add cases for `OpCode.LockBegin` and `OpCode.LockEnd` in the dispatch switch.

#### 6.2.7 Bytecode Serialization

**File:** `Stash.Bytecode/Serialization/BytecodeWriter.cs`
**File:** `Stash.Bytecode/Serialization/BytecodeReader.cs`

Add serialization/deserialization for `LockMetadata` (6 bytes per §6.2.2).

#### 6.2.8 Bytecode Verifier

**File:** `Stash.Bytecode/Bytecode/BytecodeVerifier.cs`

Add verification rules:

- Every `LockBegin` instruction must be followed (on the normal path) by a `LockEnd` (verified structurally via the compiler's emit pattern; the verifier should check that `LockBegin` count ≤ `LockEnd` count per chunk).

---

### 6.3 Stash.Stdlib Changes

#### 6.3.1 LockError Type Registration

**File:** `Stash.Stdlib/Registry/StdlibRegistry.Types.cs`

Add to `_globalStructs`:

```csharp
new BuiltInStruct(StashErrorTypes.LockError, [
    new BuiltInField("message", "string"),
    new BuiltInField("path",    "string"),
])
```

#### 6.3.2 GlobalBuiltIns Registration

**File:** `Stash.Stdlib/BuiltIns/GlobalBuiltIns.cs`

Register `LockError` so `throw LockError { message: "...", path: "..." }` creates the correct instance. Follow the same pattern as other error type registrations.

---

### 6.4 Stash.Analysis Changes

All six visitors must implement `VisitLockStmt`. Details below.

#### 6.4.1 SemanticResolver

**File:** `Stash.Core/Resolution/SemanticResolver.cs`

`VisitLockStmt`: Resolve the `Path` expression and all option value expressions. Resolve the `Body` block. No return type to infer — `lock` is a statement.

#### 6.4.2 SemanticValidator

**File:** `Stash.Analysis/Visitors/SemanticValidator.cs`

`VisitLockStmt`: Walk path, options, body. Emit diagnostics (see §7).

#### 6.4.3 SymbolCollector

**File:** `Stash.Analysis/Visitors/SymbolCollector.cs`

`VisitLockStmt`: Walk body for symbol collection (same as other block visitors). The lock block introduces no new symbol scope — variables declared inside are scoped to the body block.

#### 6.4.4 SemanticTokenWalker

**File:** `Stash.Analysis/Visitors/SemanticTokenWalker.cs`

`VisitLockStmt`: Emit `lock` keyword token as `keyword` semantic token type (consistent with other control flow keywords).

#### 6.4.5 StashFormatter

**File:** `Stash.Analysis/Visitors/StashFormatter.cs` (or `StashFormatter.ControlFlow.cs` partial if it exists)

`VisitLockStmt`: Format as:

```
lock <path> [(<opt1>: <val1>[, <opt2>: <val2>])] {
    <body>
}
```

- Path and options on the same line as `lock`.
- Body indented one level.
- No spaces inside option list parentheses.
- Trailing semicolon emitted after the closing `}` (statement, consistent with `elevate`).

---

### 6.5 Stash.Cli Changes

**File:** `Stash.Cli/Program.cs`

No CLI-level changes required. Lock blocks are a language construct — the CLI already runs `.stash` scripts normally.

If `--dry` flag is added (from Volume 2 §4), `lock` blocks must check `VMContext.IsDryMode`. (See dry mode integration in §3.6.)

---

### 6.6 Stash.Lsp Changes

**File:** `Stash.Lsp/Handlers/CompletionHandler.cs`

Add `lock` to keyword completions. Add completion snippets:

```
lock "${1:path}" {\n\t${2}\n}
lock "${1:path}" (wait: ${2:30s}) {\n\t${3}\n}
```

**File:** `Stash.Lsp/Handlers/HoverHandler.cs`

Add hover documentation for `lock` keyword. Describe: acquires exclusive file lock, options (wait, stale), error behavior, cleanup guarantee.

**File:** `Stash.Lsp/Handlers/SemanticTokensHandler.cs`

`lock` is already covered by the `keyword` semantic token type — no new token type needed.

---

### 6.7 Stash.Dap Changes

**File:** `Stash.Dap/DebugSession.cs`

`lock` blocks are transparent to the debugger — the body executes normally under the debugger. No special DAP changes needed beyond what's automatic from the visitor pattern.

Consider: DAP variable display for the "active lock path" in the VM's `ActiveLocks` stack as a synthetic scope variable (`<lock:path>`). This is a nice-to-have.

---

### 6.8 Stash.Playground Changes

**File:** `Stash.Playground/wwwroot/js/stash-language.js` (Monarch tokenizer)

Add `lock` to the keywords list:

```js
'lock',
```

**Playground execution (sandbox gate):**

The `PlaygroundExecutor` must detect `lock` blocks and throw `NotSupportedError` immediately. The simplest approach: in `ExecuteLockBegin()`, check `IBuiltInContext.IsPlayground` (a new property, or use an existing sandbox detection mechanism) and throw.

Alternatively: the static analysis pre-pass for the Playground can detect `lock` expressions and report an error before execution, providing a better UX.

---

### 6.9 VS Code Extension Changes

**File:** `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json`

Add `lock` to the control flow keywords pattern:

```json
"lock"
```

This ensures `lock` is highlighted correctly in files not processed by the language server (e.g., large files where semantic tokens are disabled).

---

## 7. Static Analysis Diagnostics

New diagnostic codes in the `SA08xx` range (lock-specific):

| Code   | Name                       | Severity | Condition                                                                                                                                             |
| ------ | -------------------------- | -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| SA0800 | `LockPathNotString`        | Warning  | The `lock` path expression is not a string literal and cannot be statically validated.                                                                |
| SA0801 | `LockBodyEmpty`            | Warning  | The `lock` block body is empty — the lock serves no purpose.                                                                                          |
| SA0802 | `LockNestedSamePath`       | Error    | A `lock` block on the same string literal path is nested inside another `lock` on that path. This will deadlock at runtime.                           |
| SA0803 | `LockUnbounded`            | Info     | A `lock` block without a `wait` option is not enclosed in a `timeout` block. This will wait indefinitely if another process holds the lock.           |
| SA0804 | `LockWaitZeroWithoutCatch` | Warning  | A `lock` block with `wait: 0s` is not enclosed in `try`. `LockError` will be unhandled if the lock is already held. |

**DiagnosticDescriptor registration:**

**File:** `Stash.Analysis/Models/DiagnosticDescriptors.cs`

Add the five descriptors following the existing pattern.

---

## 8. LSP & Tooling Summary

| Component         | Change Required                     | Notes                 |
| ----------------- | ----------------------------------- | --------------------- |
| Lexer keyword     | `"lock"` → `TokenType.Lock`         | New reserved keyword  |
| Semantic tokens   | None (uses existing `keyword` type) | —                     |
| Completions       | Add `lock` snippets                 | Two snippet forms     |
| Hover             | Add `lock` docs                     | Path, options, errors |
| Formatter         | `VisitLockStmt`                     | Block indentation; trailing `;` |
| TextMate grammar  | Add `lock` to keywords              | VS Code extension     |
| Monarch tokenizer | Add `lock` to keywords              | Playground            |
| Static analysis   | SA0800–SA0804                       | See §7                |

---

## 9. Test Scenarios

**File:** `Stash.Tests/Bytecode/LockTests.cs`

### Happy Path Tests

```
Lock_SimpleBlock_ExecutesBody()
Lock_ReleasesOnNormalExit()
Lock_BlocksSecondAcquire_WhenFirstHeld()
Lock_WaitZero_ThrowsLockError_WhenLocked()
Lock_WaitDuration_AcquiresAfterRelease()
Lock_StaleLock_DeadPid_StealsLock()
Lock_StaleLock_LivePid_ThrowsLockError()
Lock_NestedDifferentPaths_BothAcquired()
Lock_ComposesWithTimeout()
Lock_ComposesWithRetryAndWaitZero()
Lock_ComposesWithDefer_DeferRunsWhileLockHeld()
Lock_BodyException_ReleasesLock()
Lock_DryMode_DoesNotAcquireLock()
Lock_DryMode_PrintsDryMessage()
```

### Static Analysis Tests

**File:** `Stash.Tests/Analysis/LockAnalysisTests.cs`

```
SA0800_LockPathNotString_EmitsWarning()
SA0801_LockBodyEmpty_EmitsWarning()
SA0802_LockNestedSamePath_EmitsError()
SA0803_LockUnbounded_EmitsInfo()
SA0803_LockInsideTimeout_NoWarning()
SA0804_LockWaitZeroNoTry_EmitsWarning()
SA0804_LockWaitZeroWithTry_NoWarning()
```

### Error Type Tests

**File:** `Stash.Tests/Bytecode/ErrorTypeTests.cs` (extend existing)

```
LockError_IsCatchableByType()
LockError_HasPathField()
LockError_IsThrowableAsStruct()
```

---

## 10. Documentation

### 10.1 Language Specification

**File:** `docs/Stash — Language Specification.md`

Add a new section under Control Flow:

```
## Lock Block

lock <path> [(<options>)] {
    <body>
}

Acquires an exclusive file-based lock on <path> for the duration of <body>.
The lock is guaranteed to be released when the block exits (normally or via exception).
lock is a statement — it produces no value.

### Options
- wait: <duration>  — maximum time to wait for lock acquisition (default: wait forever)
- stale: <duration> — steal lock if holder is dead and lock file is older than this

### Errors
Throws LockError if the lock cannot be acquired within the wait window.
Throws IOError if the lock file path is inaccessible.
```

### 10.2 Standard Library Reference

**File:** `docs/Stash — Standard Library Reference.md`

Add `LockError` to the Error Types section:

```
LockError
  Fields: message (string), path (string)
  Thrown by: lock block when lock cannot be acquired
```

---

## 11. Example Script

**File:** `examples/lock_block.stash`

```stash
// lock_block.stash — demonstrating file-based mutual exclusion

// --- Basic use: only one instance runs at a time ---
fn runExclusive() {
  lock "/var/run/demo.lock" {
    io.println("Lock acquired — doing exclusive work");
    time.sleep(2s);
    io.println("Done");
  }
}

// --- Non-blocking: fail immediately if locked ---
fn tryOrSkip() {
  try lock "/var/run/demo.lock" (wait: 0s) {
    io.println("Got the lock, proceeding");
    doWork();
  }
  catch (LockError e) {
    io.println("Another instance is running, skipping: ${e.path}");
  }
}

// --- Wait with timeout ---
fn waitAtMost30s() {
  lock "/var/run/demo.lock" (wait: 30s) {
    io.println("Lock acquired after waiting");
    doWork();
  }
}

// --- Stale lock recovery ---
fn withStaleRecovery() {
  lock "/var/run/demo.lock" (wait: 10s, stale: 1h) {
    io.println("Lock acquired (stealing if previous holder died)");
    doWork();
  }
}

// --- Composition: retry + lock + timeout ---
fn reliableDeploy(version) {
  retry (5, delay: 15s) {
    lock "/var/run/deploy.lock" (wait: 0s) {
      timeout 10m {
        io.println("Deploying ${version}");
        $(./deploy.sh ${version});
      }
    }
  }
}

// --- Capturing a value from a critical section ---
// Declare outside, assign inside — makes the shared-mutable intent explicit:
let previousVersion = null;
lock "/var/run/version.lock" {
  previousVersion = str.trim(fs.readFile("current_version.txt"));
  fs.writeFile("current_version.txt", "v2.0.0");
}
io.println("Previous version was: ${previousVersion}");

// --- Lock with defer inside (defer runs while lock still held) ---
fn deployWithCleanup(version, tmpDir) {
  lock "/var/run/deploy.lock" {
    defer fs.delete(tmpDir, { recursive: true });
    let artifact = download(version, tmpDir);
    upload(artifact);
  }
  // tmpDir deleted, lock released
}
```

---

## 12. Decision Log

### D1: Statement vs. Expression

**Decision:** `lock` is a `Stmt` (`LockStmt`), not an `Expr`. It is parsed in `Statement()` via `LockStatement()`, consistent with `ElevateStmt`.

**Rejected:** `LockExpr` (following the `retry`/`timeout` pattern).

**Rationale:** Lock is a scope guard, not a computation. The expression form (`let x = lock ... { expr }`) produces implicit trailing-expression semantics that are unintuitive — `return` inside the block exits the enclosing function, not the lock block, so there is no clean keyword to produce a value. The workaround (bare trailing expression) is idiomatic in Rust but foreign to Stash, where functions use explicit `return`. Making `lock` an Expr would introduce two competing idioms for the same concept. The correct pattern for extracting a value from a critical section is to declare the variable outside the block and assign inside — which is also more explicit about the variable being shared-mutable state under protection. This aligns with `elevate`, which is also a Stmt for the same reason: it is a scope modifier, not a value producer.

**Risk:** Users who want `let x = lock ... { expr }` cannot write it. The workaround (declare-outside, assign-inside) is one extra line and communicates intent more clearly. Not a real loss.

---

### D2: Lock File Not Deleted on Release

**Decision:** Lock files are never deleted on release. They persist as marker files.

**Rejected:** Delete the lock file on release.

**Rationale:** Delete + recreate creates a TOCTOU race. If process A holds lock, B waits, A deletes + closes fd, B opens a NEW file (new inode), C also opens a new file → B and C both have file handles to different inodes and both think they hold the lock. Keeping the file and only releasing the OS lock (closing the file descriptor) avoids this.

**Risk:** Lock files accumulate in `/var/run`. Acceptable — lock files are tiny and `/var/run` is the conventional location. Document that lock files are persistent.

---

### D3: Default Wait is Infinite

**Decision:** The default wait (no `wait` option) blocks indefinitely, matching `flock` convention.

**Rejected:** Default wait of `30s`.

**Rationale:** `flock` default is block-forever. Scripts coordinating with each other expect this. A finite default would break valid patterns (long deployments, long backup jobs). SA0803 nudges users toward explicit timeout management without forcing it.

**Risk:** Scripts that assume "quickly" can hang. Mitigated by SA0803 lint warning.

---

### D4: Advisory Locking on Unix

**Decision:** On Unix, `lock` uses advisory locking (`flock(2)`). Non-Stash processes can bypass it.

**Rejected:** Mandatory locking via `chmod` +`fcntl` (unreliable across Linux kernel versions).

**Rationale:** Advisory locking is reliable, portable, and sufficient for coordinating multiple instances of the same Stash script. Mandatory locking on Linux is unreliable and deprecated on many kernels. The use case (script-to-script coordination) does not require locking against uncooperative processes.

**Risk:** A non-Stash process can write to the locked file. Document this clearly. It is not a defect — it is expected advisory semantics.

---

### D5: LockError as a New Built-In Error Type

**Decision:** Introduce `LockError` as a 9th built-in error type (joining the 8 in `StashErrorTypes`).

**Rejected:** Reuse `TimeoutError` for wait-expired cases and `IOError` for all lock failures.

**Rationale:** `LockError` is semantically distinct — it means "another process holds the exclusive lock." `TimeoutError` is for general operation timeouts (HTTP, SSH). Reusing `TimeoutError` would make catch clauses ambiguous. Reusing `IOError` would lose the `path` field distinction. Catchable-by-type requires a unique type name.

**Risk:** Adds to the error taxonomy surface area. Acceptable — the taxonomy is deliberately small and `LockError` is clearly justified.

---

### D6: No lock Handle Exposed to User Code

**Decision:** The `lock` block does not expose a lock handle to user code. Acquisition and release are fully managed by the runtime.

**Rejected:** Expose a `LockHandle` object with `.release()` method.

**Rationale:** Manual release defeats the guarantee. A handle that escapes the block could be released prematurely or never. The RAII-style block syntax is intentional — cleanup is automatic, like `defer`.

**Risk:** Advanced users cannot implement lock promotion/demotion or lock handoff. Accepted — these are not sysadmin use cases.

---

## 13. Open Questions & Risks

### OQ1: Recursive Lock (Same Process, Same Path)

**Current decision:** Runtime `LockError` + SA0802 static analysis.

**Alternative:** Support recursive locking (lock count). Rejected — adds complexity and the use case is almost always a bug.

**Status:** Decided. No recursive locking.

---

### OQ2: `wait: forever` Explicit Syntax

Should users be able to write `lock "/path" (wait: forever) { }` to be explicit about infinite wait?

**Options:**

- A: No. Default (no `wait` option) is implicit forever. `forever` is not a keyword.
- B: Yes. Add a `forever` duration sentinel value (`Duration.Max`). `wait: forever` is explicit and communicates intent.

**Recommendation:** Option A for v1. Adding `forever` as a duration literal would affect the duration type system broadly (Volume 2 §1 work). Defer.

**Status:** Open. Defer to v2.

---

### OQ3: Lock Polling Interval

Default polling interval is 50ms. Should this be configurable?

**Recommendation:** No for v1. 50ms is the standard `flock` retry interval in most tooling. A configurable interval adds surface area without real value for sysadmin scripts.

**Status:** Decided for v1. Fixed at 50ms.

---

### OQ4: Lock on Non-File Resources

Future: should `lock` work on named semaphores or network-based locks (e.g., via `net.lock("redis://...")`)?

**Recommendation:** Out of scope for v1. This spec covers file-based locking only. Network locking is a significantly different design problem (distributed consensus, network partitions). Keep `lock` file-based.

**Status:** Deferred. Future spec.

---

### OQ5: Interaction with `dry` Mode

When `sys.isDry()` is true, should the lock body execute normally or be suppressed?

**Current decision:** Body executes normally in dry mode (dry mode suppresses side effects, not control flow). The OS lock is NOT acquired. This means dry-run scripts can still exhibit control flow side effects (mutations to in-memory state) — which is acceptable.

**Risk:** A dry-run script that acquires no lock might behave differently than the real run (e.g., two dry-run instances run concurrently and both execute a "would do X" print that the real script would serialize). Acceptable — dry mode is inherently approximate.

**Status:** Decided.

---

### OQ6: Windows PID Reuse in Stale Detection

Windows PIDs can be reused within ~49 days. A stale lock file containing a live PID that belongs to a different process will not be stolen (the process is alive). This is conservative — it's safer to NOT steal than to steal incorrectly and create two holders.

**Mitigation documented:** The `stale` option's `mtime` check provides a secondary signal. If the PID is alive but the lock file is very old (older than `stale`), the implementation should log a warning: "Lock at /path appears stale but PID <N> is still alive — not stealing." This is informational, not an error.

**Status:** Decided. Conservative stale detection (dead PID required, not just old file).

---

## 14. Implementation Checklist

(For the Orchestrator agent picking up this spec)

- [ ] `Stash.Core/Runtime/ErrorTypes.cs` — add `LockError` constant
- [ ] `Stash.Core/Parsing/AST/IStmtVisitor.cs` — add `VisitLockStmt`
- [ ] `Stash.Core/Parsing/AST/LockStmt.cs` — new file
- [ ] `Stash.Core/Parsing/Lexer.cs` — add `"lock"` keyword → `TokenType.Lock`
- [ ] `Stash.Core/Parsing/TokenType.cs` — add `Lock` enum value
- [ ] `Stash.Core/Parsing/Parser.cs` — add `LockStatement()`, call from `Statement()`
- [ ] `Stash.Bytecode/Bytecode/OpCode.cs` — add `LockBegin = 96`, `LockEnd = 97`
- [ ] `Stash.Bytecode/Bytecode/Metadata.cs` — add `LockMetadata` record
- [ ] `Stash.Bytecode/Bytecode/BytecodeVerifier.cs` — add LockBegin/LockEnd balance check
- [ ] `Stash.Bytecode/Runtime/FileLockHandle.cs` — new file
- [ ] `Stash.Bytecode/Runtime/VMContext.cs` — add `ActiveLocks`, `EnsureLockCleanupRegistered`
- [ ] `Stash.Bytecode/Compilation/Compiler.ControlFlow.cs` — add `VisitLockStmt`
- [ ] `Stash.Bytecode/VM/VirtualMachine.Dispatch.cs` — add `LockBegin`/`LockEnd` cases
- [ ] `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs` — add `ExecuteLockBegin`, `ExecuteLockEnd`
- [ ] `Stash.Bytecode/Serialization/BytecodeWriter.cs` — add `WriteLockMetadata`
- [ ] `Stash.Bytecode/Serialization/BytecodeReader.cs` — add `ReadLockMetadata`
- [ ] `Stash.Stdlib/Registry/StdlibRegistry.Types.cs` — register `LockError` struct
- [ ] `Stash.Stdlib/BuiltIns/GlobalBuiltIns.cs` — register `LockError` type
- [ ] `Stash.Core/Resolution/SemanticResolver.cs` — add `VisitLockStmt`
- [ ] `Stash.Analysis/Visitors/SemanticValidator.cs` — add `VisitLockStmt` + SA0800–SA0804
- [ ] `Stash.Analysis/Visitors/SymbolCollector.cs` — add `VisitLockStmt`
- [ ] `Stash.Analysis/Visitors/SemanticTokenWalker.cs` — add `VisitLockStmt`
- [ ] `Stash.Analysis/Visitors/StashFormatter.cs` — add `VisitLockStmt`
- [ ] `Stash.Analysis/Models/DiagnosticDescriptors.cs` — add SA0800–SA0804
- [ ] `Stash.Lsp/Handlers/CompletionHandler.cs` — add `lock` snippets
- [ ] `Stash.Lsp/Handlers/HoverHandler.cs` — add `lock` hover doc
- [ ] `Stash.Playground/wwwroot/js/stash-language.js` — add `lock` to keywords
- [ ] `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json` — add `lock` to keywords
- [ ] `docs/Stash — Language Specification.md` — add Lock Block section
- [ ] `docs/Stash — Standard Library Reference.md` — add `LockError` to Error Types
- [ ] `examples/lock_block.stash` — new example file
- [ ] `Stash.Tests/Bytecode/LockTests.cs` — new test file (≥15 tests)
- [ ] `Stash.Tests/Analysis/LockAnalysisTests.cs` — new test file (≥7 tests)
- [ ] `Stash.Tests/Bytecode/ErrorTypeTests.cs` — extend with 3 LockError tests
- [ ] Run `dotnet test` — zero failures required
