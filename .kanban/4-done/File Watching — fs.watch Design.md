# File Watching — `fs.watch` Design

> **Status:** Design (0-backlog)
> **Created:** April 2026
> **Origin:** [Missing Scripting Fundamentals — Gap Analysis](../1-todo/Missing%20Scripting%20Fundamentals%20—%20Gap%20Analysis.md), §5.1

---

## Table of Contents

1. [Problem Statement](#1-problem-statement)
2. [Prior Art](#2-prior-art)
3. [Proposed API](#3-proposed-api)
4. [Semantic Rules](#4-semantic-rules)
5. [Cross-Platform Behavior](#5-cross-platform-behavior)
6. [Implementation Surface](#6-implementation-surface)
7. [Design Decisions](#7-design-decisions)
8. [Edge Cases](#8-edge-cases)
9. [Test Scenarios](#9-test-scenarios)
10. [LSP / DAP Implications](#10-lsp--dap-implications)
11. [Open Questions](#11-open-questions)

---

## 1. Problem Statement

Stash has no way to watch files or directories for changes. This blocks three categories of real-world scripts:

1. **Log tailing** — process new lines as they're appended to a log file (the `tail -f` use case)
2. **Config hot-reload** — re-read configuration when a file changes without polling
3. **Build/deploy watchers** — trigger actions when source files or artifacts change

Today, the only workaround is a polling loop:

```stash
// Polling — wasteful, latency-bound to interval, misses rapid changes
while (true) {
    let newMtime = fs.modifiedAt("/etc/app/config.toml");
    if (newMtime != lastMtime) {
        reloadConfig();
        lastMtime = newMtime;
    }
    sleep(2000);
}
```

This consumes CPU, introduces latency, and misses changes that happen between polls (e.g., a file created and deleted within the interval). The operating system provides native notification mechanisms (inotify on Linux, FSEvents on macOS, ReadDirectoryChangesW on Windows) that .NET wraps via `System.IO.FileSystemWatcher`. Stash should expose this.

---

## 2. Prior Art

### 2.1 Other Languages

| Language   | API                                          | Model          | Notes                                                                     |
| ---------- | -------------------------------------------- | -------------- | ------------------------------------------------------------------------- |
| Python     | `watchdog` (third-party)                     | Callback       | No stdlib solution; `os.inotify` is Linux-only                            |
| Ruby       | `Listen` gem (third-party)                   | Callback       | No stdlib solution                                                        |
| Node.js    | `fs.watch(path, callback)`                   | Event callback | Returns a `FSWatcher` handle; notorious for cross-platform inconsistency  |
| Go         | `fsnotify` (third-party)                     | Channel        | Events sent over a Go channel; uses inotify/kqueue/ReadDirectoryChanges   |
| PowerShell | `Register-ObjectEvent -InputObject $watcher` | Event callback | Wraps .NET `FileSystemWatcher`; event-driven with `-Action` script blocks |
| Deno       | `Deno.watchFs(paths)`                        | Async iterator | Built-in; returns async iterable of events                                |

**Observation:** Most languages don't have this in stdlib — it's usually a third-party library. Python, Ruby, and Go all require external packages. Node.js and Deno are exceptions. By adding this as a built-in, Stash would be ahead of Python and Ruby for sysadmin use cases.

**Observation:** The callback model (Node.js, PowerShell) is the most common pattern and aligns with Stash's existing `sys.onSignal()` and `process.onExit()` patterns.

### 2.2 Existing Stash Patterns

Stash already has two callback-registration patterns:

| Pattern            | Registration                       | Storage                               | Execution Model                       | Cleanup                  |
| ------------------ | ---------------------------------- | ------------------------------------- | ------------------------------------- | ------------------------ |
| `sys.onSignal()`   | `sys.onSignal(signal, handler)`    | `ConcurrentDictionary` (static)       | `ctx.Fork()` — isolated child context | `sys.offSignal(signal)`  |
| `process.onExit()` | `process.onExit(handle, callback)` | `ctx.ProcessExitCallbacks` (instance) | Direct call on same context           | Fires once, then removed |

`fs.watch` is closer to `sys.onSignal()`:

- Long-lived (persists until explicitly stopped or script exits)
- Fires on a background thread (.NET FileSystemWatcher uses the thread pool)
- Needs `ctx.Fork()` for safe execution in an isolated context
- Needs explicit unregistration (`fs.unwatch()`)
- Needs cleanup on script exit (dispose the underlying watcher)

### 2.3 .NET FileSystemWatcher

`System.IO.FileSystemWatcher` provides:

- **Events:** `Changed`, `Created`, `Deleted`, `Renamed`, `Error`
- **Properties:** `Path`, `Filter`/`Filters`, `IncludeSubdirectories`, `NotifyFilter`, `InternalBufferSize`
- **Implements `IDisposable`** — must be disposed to stop watching and release OS resources
- **Cross-platform:** Available on all .NET targets (Linux inotify, macOS FSEvents, Windows ReadDirectoryChangesW)

**Known issues (documented by Microsoft):**

- A single file operation may raise multiple events (e.g., save = delete + create on some editors)
- Buffer overflow if many changes happen rapidly — events are lost silently unless the `Error` event is handled
- On macOS, FSEvents is less granular than inotify — some `NotifyFilter` values may not behave identically
- Cut-and-paste of a folder reports only the folder rename, not its contents
- Network paths have a 64KB buffer limit
- Hidden files are not ignored by default

---

## 3. Proposed API

### 3.1 Core Functions

```stash
// Watch a path for changes — returns a Watcher handle
let watcher = fs.watch("/var/log/app.log", (event) => {
    io.println($"[{event.type}] {event.path}");
});

// Watch with options
let watcher = fs.watch("/opt/app/src", (event) => {
    if (event.type == "modified") {
        rebuild();
    }
}, { recursive: true, filter: "*.stash" });

// Stop watching
fs.unwatch(watcher);
```

### 3.2 Function Signatures

#### `fs.watch(path, callback, options?)`

| Parameter  | Type                          | Required | Description                                           |
| ---------- | ----------------------------- | -------- | ----------------------------------------------------- |
| `path`     | `string`                      | Yes      | File or directory path to watch                       |
| `callback` | `function(event: WatchEvent)` | Yes      | Called for each file system event                     |
| `options`  | `WatchOptions`                | No       | Configuration options (recursive, filter, bufferSize) |

**Returns:** `Watcher` — a handle used to stop watching via `fs.unwatch()`

**Errors:**

- `RuntimeError` if `path` does not exist
- `RuntimeError` if `path` is not readable
- `RuntimeError` if the OS rejects the watch (e.g., too many inotify watches on Linux)

#### `fs.unwatch(watcher)`

| Parameter | Type      | Required | Description            |
| --------- | --------- | -------- | ---------------------- |
| `watcher` | `Watcher` | Yes      | Handle from `fs.watch` |

**Returns:** `null`

**Behavior:** Stops the watcher, disposes the underlying `FileSystemWatcher`, and removes it from tracking. Calling `fs.unwatch()` on an already-unwatched handle is a no-op (not an error).

### 3.3 Built-In Structs

```stash
struct WatchEvent {
    type: string,     // "created" | "modified" | "deleted" | "renamed"
    path: string,     // Absolute path of the affected file/directory
    oldPath: string   // Only populated for "renamed" events; null otherwise
}

struct WatchOptions {
    recursive: bool,    // Watch subdirectories (default: false)
    filter: string,     // Glob filter for filenames (default: "*" — all files)
    bufferSize: int,    // Internal buffer size in bytes (default: 8192)
    debounce: int       // Debounce window in milliseconds (default: 100; 0 = no debounce)
}
```

### 3.4 Usage Examples

**Log tailing trigger:**

```stash
let watcher = fs.watch("/var/log/syslog", (event) => {
    if (event.type == "modified") {
        let lines = fs.readLines("/var/log/syslog");
        let newLines = arr.slice(lines, lastLineCount, len(lines));
        for (let line in newLines) {
            processLogLine(line);
        }
        lastLineCount = len(lines);
    }
});
```

**Config hot-reload:**

```stash
let watcher = fs.watch("/etc/myapp/config.toml", (event) => {
    if (event.type == "modified") {
        log.info("Config changed, reloading...");
        config = config.read("/etc/myapp/config.toml");
    }
});
```

**Build watcher:**

```stash
let watcher = fs.watch("./src", (event) => {
    if (event.type != "deleted") {
        log.info($"Source changed: {event.path}");
        $(dotnet build);
    }
}, { recursive: true, filter: "*.cs" });

io.println("Watching for changes... Press Ctrl+C to stop.");
sleep(-1);  // Block forever (or use a signal handler)
```

**Watching multiple paths:**

```stash
let watchers = [];
let paths = ["/var/log/app1.log", "/var/log/app2.log", "/var/log/app3.log"];

for (let p in paths) {
    let w = fs.watch(p, (event) => {
        log.info($"[{path.base(event.path)}] {event.type}");
    });
    watchers = arr.push(watchers, w);
}

// Later: stop all
for (let w in watchers) {
    fs.unwatch(w);
}
```

---

## 4. Semantic Rules

### 4.1 Callback Execution

1. **Forked context.** Each callback invocation runs in an isolated child context (`ctx.Fork()`), exactly like `sys.onSignal()` handlers. This means:
   - The callback can read variables from the enclosing scope (captured at registration time via closure)
   - The callback cannot modify variables in the parent scope (fork semantics = snapshot)
   - Errors inside the callback are non-fatal — they do not crash the script

2. **Thread safety.** .NET `FileSystemWatcher` fires events on thread pool threads. The forked context provides isolation, but the callback itself must not assume single-threaded execution. Multiple events may fire concurrently if changes happen rapidly.

3. **Non-blocking.** `fs.watch()` returns immediately. The script continues executing after registration. The callback fires asynchronously when events occur.

### 4.2 Watcher Lifecycle

1. **Registration:** `fs.watch()` creates a `FileSystemWatcher`, attaches event handlers, sets `EnableRaisingEvents = true`, and returns a `Watcher` handle.
2. **Active:** Events fire the callback on thread pool threads via forked contexts.
3. **Unregistration:** `fs.unwatch(watcher)` disposes the `FileSystemWatcher` and removes it from tracked resources.
4. **Script exit:** All active watchers are disposed automatically during interpreter cleanup (like tracked processes).

### 4.3 Event Deduplication

.NET `FileSystemWatcher` is notorious for firing duplicate events. For example, saving a file may generate two `Changed` events in rapid succession. **Stash should debounce events by default.**

**Proposed debounce strategy:**

- Events for the same `(path, type)` pair that arrive within **100ms** of each other are collapsed into a single callback invocation.
- The debounce window is not configurable in v1 (keeps the API simple; can be added to `WatchOptions` later if needed).
- Renamed events are never debounced (each rename is a distinct, atomic operation).

### 4.4 Error Handling

- If the underlying `FileSystemWatcher` raises an `Error` event (buffer overflow, access denied), Stash logs a warning via `log.warn()` but does not crash the script or invoke the user callback.
- If the callback throws an error, it is caught silently (same pattern as `sys.onSignal()` handlers). The watcher remains active.
- If the watched path is deleted, the watcher fires a `deleted` event and then **remains active** (the path may be recreated). This matches `FileSystemWatcher` default behavior.

---

## 5. Cross-Platform Behavior

### 5.1 Platform Comparison

| Aspect               | Linux (inotify)                                               | macOS (FSEvents)              | Windows (ReadDirectoryChangesW) |
| -------------------- | ------------------------------------------------------------- | ----------------------------- | ------------------------------- |
| **Granularity**      | Per-file/directory                                            | Per-directory (coarser)       | Per-directory                   |
| **Recursive**        | Requires one watch per subdir                                 | Native recursive support      | Native recursive support        |
| **Max watches**      | `/proc/sys/fs/inotify/max_user_watches` (default: 8192–65536) | No hard limit                 | No hard limit                   |
| **Rename detection** | Reports old and new name                                      | May report as delete + create | Reports old and new name        |
| **Symlinks**         | Watches the link target                                       | Watches the link target       | Watches the link target         |
| **Network paths**    | Limited                                                       | Not supported                 | Supported (64KB buffer limit)   |

### 5.2 Stash-Level Guarantees

Stash normalizes platform differences so scripts behave consistently:

1. **Event types are consistent:** Always `"created"`, `"modified"`, `"deleted"`, `"renamed"`. Platform-specific event names are mapped.
2. **Paths are absolute:** `event.path` always returns an absolute path, even if `fs.watch()` was called with a relative path.
3. **Renamed events:** If the platform (macOS) reports a rename as delete + create, Stash passes them through as-is rather than attempting to synthesize a rename. **This is the decided policy: pass through whatever the OS sends.** We do not modify or normalize OS event semantics. Users who need cross-platform rename detection should handle both rename events and delete + create patterns.
4. **Recursive watching:** Works on all three platforms. On Linux, .NET internally manages multiple inotify watches for subdirectories.

### 5.3 Linux-Specific: inotify Watch Limits

On Linux, each `fs.watch()` with `recursive: true` may consume one inotify watch per subdirectory. The default system limit is often 8,192. If a script watches a large directory tree, it can exhaust the limit.

**Stash behavior:**

- If `FileSystemWatcher` throws due to the inotify limit, `fs.watch()` throws a `RuntimeError` with a message like: `"Cannot watch '/path': inotify watch limit reached. Increase fs.inotify.max_user_watches (current: 8192)."`
- This is not something Stash can fix — it's an OS configuration issue — but the error message should be actionable.

---

## 6. Implementation Surface

### 6.1 Summary

This feature is **entirely a stdlib addition**. No parser, lexer, or AST changes are needed. No new language syntax. The scope is:

| Component             | Changes                                                                                                                        |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| **Stash.Stdlib**      | Add `fs.watch()` and `fs.unwatch()` to `FsBuiltIns.cs`; add `WatchEvent` and `WatchOptions` structs; add `Watcher` handle type |
| **Stash.Interpreter** | Add watcher tracking to `IInterpreterContext` (parallel to process tracking); add cleanup on exit                              |
| **Stash.Analysis**    | No changes (built-in functions are already recognized from metadata)                                                           |
| **Stash.Core**        | No changes                                                                                                                     |
| **Stash.Lsp**         | No changes (completion/hover auto-populated from stdlib metadata)                                                              |
| **Stash.Dap**         | No changes                                                                                                                     |
| **Stash.Tests**       | New test file `FsWatchBuiltInsTests.cs`                                                                                        |
| **Docs**              | Update `Stash — Standard Library Reference.md` with `fs.watch`, `fs.unwatch`, structs                                          |

### 6.2 Detailed Changes

#### Stash.Stdlib/BuiltIns/FsBuiltIns.cs

Add to the `Define()` method:

- **Static state:** `ConcurrentDictionary<StashInstance, FileSystemWatcher>` to track active watchers by handle
- **`fs.watch(path, callback, options?)`:** Create `FileSystemWatcher`, attach events, return `Watcher` handle
- **`fs.unwatch(watcher)`:** Dispose and remove from tracking
- **Debounce logic:** `ConcurrentDictionary<string, Timer>` for path-based debouncing
- **Cleanup method:** `DisposeAllWatchers()` called on interpreter shutdown

#### Stash.Stdlib — Built-In Structs

Add to the struct definitions (same pattern as `FilePermission`/`FilePermissions`):

- `WatchEvent` struct with `type`, `path`, `oldPath` fields
- `WatchOptions` struct with `recursive`, `filter`, `bufferSize`, `debounce` fields
- `Watcher` marker handle (opaque — users only pass it to `fs.unwatch()`)

#### Stash.Interpreter — Watcher Tracking

Add to `IInterpreterContext` / concrete interpreter:

- `List<(StashInstance Handle, FileSystemWatcher OsWatcher)> TrackedWatchers` — parallel to `TrackedProcesses`
- In `CleanupTrackedProcesses()` (or a new `CleanupTrackedResources()`): dispose all active watchers

This is the only interpreter change — it's plumbing for cleanup, not semantic changes.

#### Stash.Tests

New test file: `FsWatchBuiltInsTests.cs`

Tests need to be designed carefully because file watching is inherently asynchronous and timing-dependent. Strategy:

- Create a temp directory for each test
- Register a watcher
- Perform a file operation
- Wait up to N seconds for the callback to fire (with a `ManualResetEventSlim` or `TaskCompletionSource`)
- Assert the event details
- Clean up the watcher and temp directory

### 6.3 Capability Gating

`fs.watch` and `fs.unwatch` are part of the `fs` namespace, which is already gated behind `StashCapabilities.FileSystem`. No new capability flag needed.

---

## 7. Design Decisions

### 7.1 Why `fs.watch` + `fs.unwatch` (Not a New Namespace)?

**Decision:** File watching lives in the `fs` namespace, not a new `watch` or `fsw` namespace.

**Alternatives considered:**

- **New `watch` namespace** (`watch.file()`, `watch.dir()`): Over-engineered for two functions. Creates namespace proliferation.
- **New `fsw` namespace**: Fragmented — users would look for it under `fs` first.

**Rationale:** File watching is a file system operation. `fs.watch` is discoverable and consistent with `fs.stat`, `fs.glob`, etc. The `sys` namespace already demonstrates that event registration functions (`onSignal`/`offSignal`) coexist with utility functions in the same namespace.

### 7.2 Why a Callback Model (Not Async Iterator)?

**Decision:** `fs.watch(path, callback)` with explicit `fs.unwatch()`.

**Alternatives considered:**

- **Async iterator** (`for await (let event in fs.watch(path))`): Stash doesn't have `for await` or async iterators. Adding them is a much larger language change.
- **Synchronous blocking** (`fs.waitForChange(path)`): Useful for "wait once" but doesn't support continuous monitoring without a loop, which is the primary use case.
- **Channel/future-based** (`task.run(() => fs.pollChanges(path))`): Extra complexity with no benefit over callbacks.

**Rationale:** The callback model matches `sys.onSignal()` exactly. Users already know this pattern. It requires zero language additions — pure stdlib work.

### 7.3 Why Debounce by Default (and Configurable)?

**Decision:** 100ms default debounce window for same `(path, type)` events, configurable via `WatchOptions.debounce`.

**Alternatives considered:**

- **No debounce** — forward every raw event: Leads to user confusion ("why does my callback fire twice when I save?"). Every Node.js tutorial for `fs.watch` warns about this and recommends manual debouncing.
- **Hardcoded debounce with no user control:** Too rigid. Some use cases (build watchers, audit logging) need either faster response or raw events.

**Rationale:** The 95% use case is "tell me when something changed" — not "tell me about every intermediate write buffer flush." Debouncing by default makes the feature Just Work. However, giving the user the option to change it (or disable it with `debounce: 0`) costs nothing in API complexity and avoids a future breaking change. The `debounce` field is part of `WatchOptions` from day one.

### 7.4 Why Fork the Context (Not Direct Call)?

**Decision:** Callbacks execute in a `ctx.Fork()` child context.

#### Cross-Language Research: How Others Handle Callback Scope

We surveyed five major languages/libraries to understand what developers expect:

| Language/Library          | Model            | Can callback mutate parent scope?          | Thread safety model                        |
| ------------------------- | ---------------- | ------------------------------------------ | ------------------------------------------ |
| **Node.js** `fs.watch()`  | Closure callback | **Yes** — JS closures capture by reference | Single-threaded event loop; no concurrency |
| **Python** `watchdog`     | Handler class    | **Yes** — via `self.*` on handler object   | Background thread; needs manual locks      |
| **Go** `fsnotify`         | Channel          | **Yes** — goroutine captures by reference  | Goroutine; needs mutex for shared state    |
| **Deno** `Deno.watchFs()` | Async iterator   | **Yes** — same scope, `for await` loop     | Single-threaded; no concurrency            |
| **Ruby** `Listen`         | Block callback   | **Yes** — Ruby blocks capture by reference | Background thread; needs manual locks      |

**Finding:** Every language allows callbacks to mutate parent scope variables. However, this is only safe in single-threaded runtimes (Node.js, Deno). In multi-threaded contexts (Python, Go, Ruby), developers must add their own synchronization (locks, mutexes, channels) — which is a common source of bugs.

#### Why Stash's Fork Approach Is the Right Tradeoff

Stash is a **tree-walking interpreter** where variable access is not atomic. `FileSystemWatcher` fires events on **.NET thread pool threads**, which means callbacks execute concurrently with the main script. If callbacks could mutate the parent `Environment` chain directly, every variable read/write in the entire interpreter would need locking — a catastrophic performance hit for the common case.

`ctx.Fork()` sidesteps this entirely by giving each callback invocation a snapshot of the environment. This means:

- **No race conditions** — the main script and callbacks never contend on the same mutable state
- **No locks needed** — neither in the interpreter nor in user code
- **Consistent with existing patterns** — `sys.onSignal()` and `task.run()` already work this way

The tradeoff is that callbacks cannot directly mutate parent scope variables. But shared **reference types** (dicts, struct instances) are still shared — the fork snapshots the environment bindings, not the objects they point to. So the workaround is natural:

```stash
// Shared state via reference type — this works
let state = { configDirty: false };

fs.watch("/etc/app/config.toml", (event) => {
    state.configDirty = true;  // Mutates the shared dict object
});

// Main loop reads the same dict
while (true) {
    if (state.configDirty) {
        reloadConfig();
        state.configDirty = false;
    }
    sleep(1000);
}
```

**This is actually safer than what Python, Go, and Ruby offer** — those languages give you rope to hang yourself with unsynchronized shared mutable state. Stash's approach forces communication through reference types, which is more explicit and less error-prone.

> **Note:** The forked context inherits a snapshot of value-type variable bindings. Changes to variables declared with `let` in the outer scope are not visible inside the callback, and vice versa. However, mutations to objects (dicts, struct instances) reachable from those bindings _are_ visible in both directions, because the bindings point to the same heap object.

### 7.5 Why Opaque `Watcher` Handle (Not the FileSystemWatcher itself)?

**Decision:** `fs.watch()` returns a `Watcher` struct instance that is opaque — it has no user-accessible fields.

**Alternatives considered:**

- **Return a dict with methods:** Stash doesn't have method syntax on dicts.
- **Return a struct with status fields** (path, isActive, etc.): Tempting, but adds complexity for v1. Status fields can be added later.

**Rationale:** The handle's only purpose is to be passed to `fs.unwatch()`. Keeping it opaque follows the precedent of `Process` handles from `process.spawn()` — they're returned as struct instances but primarily serve as handles.

> **Revision possibility:** If users need to inspect active watchers, we can add `watcher.path`, `watcher.active` fields later without breaking changes.

---

## 8. Edge Cases

### 8.1 Watching a File That Doesn't Exist

`fs.watch()` throws a `RuntimeError`. You cannot watch a non-existent path.

**Rationale:** `FileSystemWatcher` requires a valid path. Watching for a file to _appear_ requires watching its parent directory — users should do that explicitly.

### 8.2 Watched File/Directory Is Deleted

The watcher fires a `"deleted"` event and remains active. If the file is recreated, subsequent events will fire.

On some platforms, deleting and recreating a file may cause the watcher to stop working (the inode changed). **Stash documents this as platform-dependent behavior** and recommends watching the parent directory instead of a specific file when delete-and-recreate is expected.

### 8.3 Watching the Same Path Twice

Each `fs.watch()` call creates an independent watcher. Two watchers on the same path will both fire for the same events. This is intentional — different parts of a script may have different concerns about the same path.

### 8.4 `fs.unwatch()` After Script Starts Exiting

Calling `fs.unwatch()` during cleanup (e.g., in a `finally` block) works normally. Calling it after the watcher was already auto-cleaned by the interpreter shutdown is a no-op.

### 8.5 Very Large Directory Trees

Recursive watching of a directory with thousands of subdirectories may:

- Hit inotify limits on Linux (discussed in §5.3)
- Consume significant memory for the internal buffer
- Cause slow startup as `FileSystemWatcher` enumerates subdirectories

`fs.watch()` does not warn about this — the OS handles it and reports errors.

### 8.6 Rapid File Changes (Buffer Overflow)

If changes happen faster than Stash can process them, `FileSystemWatcher` may overflow its internal buffer. Stash handles the `Error` event by logging `log.warn("fs.watch: internal buffer overflow — some events may have been missed")`. The watcher remains active.

Users who need reliable event processing for high-throughput scenarios should increase `bufferSize` in `WatchOptions`.

### 8.7 Callback Throws an Error

Errors inside watcher callbacks are caught and silently discarded (same as `sys.onSignal()`). The watcher remains active. This prevents a single bad event from killing the monitoring loop.

---

## 9. Test Scenarios

### 9.1 Happy Path

| #   | Test                                 | Description                                                           |
| --- | ------------------------------------ | --------------------------------------------------------------------- |
| 1   | Watch file, modify it                | Callback fires with `type == "modified"`, correct `path`              |
| 2   | Watch directory, create file in it   | Callback fires with `type == "created"`, correct `path`               |
| 3   | Watch directory, delete file in it   | Callback fires with `type == "deleted"`, correct `path`               |
| 4   | Watch directory, rename file in it   | Callback fires with `type == "renamed"`, correct `path` and `oldPath` |
| 5   | Watch with filter `"*.txt"`          | Only `.txt` changes trigger the callback                              |
| 6   | Watch with `recursive: true`         | Changes in subdirectories trigger the callback                        |
| 7   | `fs.unwatch()` stops the watcher     | No more callbacks after unwatch                                       |
| 8   | Multiple watchers on different paths | Each watcher fires independently                                      |

### 9.2 Edge Cases

| #   | Test                                    | Description                             |
| --- | --------------------------------------- | --------------------------------------- |
| 9   | Watch non-existent path                 | Throws `RuntimeError`                   |
| 10  | `fs.unwatch()` with invalid handle      | No-op, no error                         |
| 11  | Double `fs.unwatch()` on same handle    | Second call is no-op                    |
| 12  | Callback error doesn't kill watcher     | Watcher continues after callback throws |
| 13  | Watcher cleanup on interpreter shutdown | Watchers disposed when script exits     |
| 14  | Watch same path twice                   | Both watchers fire independently        |

### 9.3 Options Validation

| #   | Test                                      | Description                                            |
| --- | ----------------------------------------- | ------------------------------------------------------ |
| 15  | Default options (no options arg)          | Works with defaults: non-recursive, all files, 8KB buf |
| 16  | `recursive: false` on subdirectory change | Subdirectory changes do NOT trigger callback           |
| 17  | `filter: "*.log"` ignores non-matching    | `.txt` changes don't trigger, `.log` changes do        |
| 18  | Custom `bufferSize`                       | Watcher created with specified buffer size             |

### 9.4 Debounce

| #   | Test                                 | Description                                           |
| --- | ------------------------------------ | ----------------------------------------------------- |
| 19  | Rapid writes to same file (default)  | Callback fires once (within 100ms debounce window)    |
| 20  | Writes to different files            | Callbacks fire independently (no cross-file debounce) |
| 21  | Rename is not debounced              | Each rename fires immediately                         |
| 22  | Custom debounce window (e.g., 500ms) | Events within 500ms window are collapsed              |
| 23  | `debounce: 0` disables debouncing    | Every raw OS event fires the callback                 |

### 9.5 Callback Scope Isolation

| #   | Test                            | Description                                               |
| --- | ------------------------------- | --------------------------------------------------------- |
| 24  | Value-type variable not shared  | `let x = 0` in parent; callback increments; parent sees 0 |
| 25  | Reference-type object IS shared | Dict mutated in callback is visible in parent scope       |
| 26  | Callback error is non-fatal     | Callback throws; watcher continues; script unaffected     |

---

## 10. LSP / DAP Implications

### LSP

- **Completion:** `fs.watch` and `fs.unwatch` auto-complete from stdlib metadata. No special handling needed.
- **Hover:** Documentation from the `documentation:` parameter on the function definition. No special handling needed.
- **Diagnostics:** No new diagnostics. Argument type mismatches caught by existing static analysis.
- **Signature help:** Parameter info populated from metadata (same as all other built-ins).

### DAP

- **Breakpoints in callbacks:** Watcher callbacks run in forked contexts. The DAP should be able to pause in a callback if a breakpoint is set inside it. This depends on the forked context inheriting the debug session — **needs verification** with the existing `sys.onSignal()` callback debugging behavior.
- **Variable inspection:** The `Watcher` handle should appear as a struct instance in the Variables view.

### No New Analysis Rules

No new semantic diagnostics, lint rules, or analysis visitors are needed.

---

## 11. Resolved Questions

All design questions from the initial draft have been resolved. Decisions are recorded here for traceability.

### Q1: `WatchOptions` — struct or dict? → **Struct (resolved)**

`WatchOptions` is a struct, consistent with project conventions and the `FilePermissions` precedent. Inline construction is available in Stash:

```stash
fs.watch("./src", handler, WatchOptions { recursive: true, filter: "*.stash" });
```

### Q2: `fs.watchOnce()` convenience function? → **Deferred to v2 (resolved)**

Not included in v1. The self-unwatch pattern works today:

```stash
let watcher = null;
watcher = fs.watch("/tmp/signal.txt", (event) => {
    fs.unwatch(watcher);
    processEvent(event);
});
```

If demand warrants it, `fs.watchOnce()` can be added in a future release with no breaking changes.

### Q3: Debounce — hardcoded or configurable? → **Configurable from day one (resolved)**

The `debounce` field is part of `WatchOptions` with a 100ms default. Setting `debounce: 0` disables debouncing. This avoids a future breaking change and gives power users the control they need without adding API surface.

See §4.3 (debounce strategy) and §7.3 (design decision) for details.

### Q4: Can callbacks mutate parent scope? → **Fork semantics, shared via reference types (resolved)**

Callbacks execute in `ctx.Fork()` child contexts. They **cannot** mutate parent scope value-type variables (e.g., `let counter = 0`) — this is by design for thread safety.

However, **reference types (dicts, struct instances) are shared** between parent and callback scopes. The fork snapshots variable bindings, not heap objects. So communication is possible through shared objects:

```stash
let state = { configDirty: false };
fs.watch("/etc/app/config.toml", (event) => {
    state.configDirty = true;  // Mutates the shared dict — visible in parent
});
```

This approach was validated by researching how 5 other languages handle callback scope (see §7.4 for the full analysis). **Every other language allows parent scope mutation, but requires manual synchronization in multi-threaded contexts** (Python, Go, Ruby). Stash's Fork approach is safer because it eliminates race conditions without requiring user-managed locks. The reference-type escape hatch provides a clear, explicit pattern for cross-context communication.

---

> **Assessment:** This spec is **implementation-ready**. All open questions have been resolved. The feature is entirely a stdlib addition — no parser, lexer, or AST changes. The implementation surface is small (~200 lines of C# in FsBuiltIns, ~30 lines of interpreter plumbing, ~400 lines of tests). The biggest risk is cross-platform behavior differences in `FileSystemWatcher`, which are mitigated by configurable debouncing, OS-event passthrough, and clear documentation of platform-dependent edge cases.
