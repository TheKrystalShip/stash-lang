# Stdlib Extraction — Move `log` and `store` to Packages

**Status:** Backlog — Design
**Created:** 2026-04-06
**Author:** Architect

---

## 1. Purpose

Remove the `log` and `store` namespaces from the Stash standard library (`Stash.Stdlib/BuiltIns/`) and reimplement their functionality as first-party Stash packages (`@stash/log` and `@stash/store`). Both namespaces are pure application-level conveniences that require no interpreter or VM access, and are better served by the package ecosystem.

---

## 2. Rationale

### 2.1 Why `log` Doesn't Belong in Stdlib

| Concern                            | Detail                                                                                                                                                                                           |
| ---------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **No interpreter access required** | Every building block already exists: `io.eprintln()` for stderr, `time.iso()` for timestamps, `str.replace()` for template formatting, `fs.appendFile()` for file output.                        |
| **Opinionated design**             | Format template syntax (`{time}`, `{level}`, `{msg}`), level names, default format — these are design choices. Different projects want different logging. Packages allow versioned alternatives. |
| **Capability bypass**              | `log.toFile()` writes to files via C# `StreamWriter`, completely bypassing the FileSystem capability gate. A package using `fs.appendFile()` properly respects capabilities.                     |
| **Process-global static state**    | `_level`, `_format`, `_fileWriter` are shared across ALL VM instances in the same process. Module-scoped state in a package is better isolated.                                                  |
| **Industry precedent**             | Node.js (winston, pino), Rust (log + env_logger crates), Go (pre-slog) — logging is overwhelmingly a package concern, not a language concern.                                                    |

### 2.2 Why `store` Doesn't Belong in Stdlib

| Concern                              | Detail                                                                                                                                                                                                                                        |
| ------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **It's a dict wrapper**              | Every operation has a direct `dict.*` equivalent. `store.set(k,v)` = `dict.set(d,k,v)`, etc. The only non-trivial function is `scope()` (a 3-line prefix filter).                                                                             |
| **Module caching enables singleton** | Stash's import system caches modules (`ModuleCache` in `VirtualMachine.Modules.cs` stores the actual globals dict reference). A module-level `let _data = {}` creates a process-wide singleton — exactly the same semantics `store` provides. |
| **~50 lines of Stash code**          | The entire `store` API is trivially implementable in pure Stash. Keeping it in C# adds maintenance burden for zero capability gain.                                                                                                           |
| **Process-global static state**      | Same issue as `log` — `static Dictionary<string, object?>` is shared across all VM instances.                                                                                                                                                 |

### 2.3 What SHOULD Stay in Stdlib

For contrast, here's why other namespaces are correctly placed:

- **`dict`, `arr`, `str`** — Language-level data structure operations. Cannot be implemented without VM access.
- **`io`** — Provides `println`, `eprintln`, `readLine` — fundamental I/O that other packages build on.
- **`fs`, `process`, `http`** — Capability-gated OS interfaces. Require C# interop.
- **`crypto`, `encoding`** — Rely on .NET BCL cryptographic primitives. Not implementable in pure Stash.
- **`time`** — Wraps `DateTimeOffset`. Fundamental primitive, not an opinionated abstraction.

The principle: **Stdlib provides primitives and OS interfaces. Packages provide opinionated abstractions built on those primitives.**

---

## 3. Blast Radius Analysis

### 3.1 Files to Delete

| File                                           | Lines           | Purpose                          |
| ---------------------------------------------- | --------------- | -------------------------------- |
| `Stash.Stdlib/BuiltIns/LogBuiltIns.cs`         | ~215            | `log` namespace implementation   |
| `Stash.Stdlib/BuiltIns/StoreBuiltIns.cs`       | ~165            | `store` namespace implementation |
| `Stash.Tests/Interpreting/LogBuiltInsTests.cs` | ~215 (17 tests) | `log` namespace tests            |
| `Stash.Tests/Interpreting/StoreTests.cs`       | ~340 (32 tests) | `store` namespace tests          |
| `examples/logging.stash`                       | ~25             | `log` namespace demo             |

### 3.2 Files to Edit

| File                                          | Change                                                                                           |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| `Stash.Stdlib/StdlibDefinitions.cs`           | Remove `LogBuiltIns.Define()` and `StoreBuiltIns.Define()` registrations                         |
| `docs/Stash — Standard Library Reference.md`  | Remove `log` and `store` sections; add note pointing to `@stash/log` and `@stash/store` packages |
| `docs/Stash — Language Specification.md`      | Remove/update any `log.*` or `store.*` usage in examples (~3 references)                         |
| `README.md`                                   | Update example code that uses `log.*` (~2 lines)                                                 |
| `.github/instructions/stdlib.instructions.md` | Remove `log` and `store` from the namespace table; update namespace count (26 → 24)              |
| `examples/dictionaries.stash`                 | Remove `store.*` usage in the Store example section (lines ~62-74)                               |

### 3.3 Playground Impact

The Playground sandbox executes code without package installation. Removing `log` and `store` from stdlib means they won't be available in the Playground. This is acceptable — neither namespace is essential for demos, and REPL users can use `io.eprintln()` and `dict.*` directly.

### 3.4 Bytecode VM / Analysis / LSP Impact

- **Bytecode VM**: No special handling for `log` or `store`. They're registered as regular namespaces via `StdlibDefinitions`. Removing the registration is sufficient.
- **Static Analysis**: No special rules for `log` or `store`. They're discovered via `StdlibRegistry` metadata, which is auto-derived from `StdlibDefinitions`. Removing the registration removes them from analysis.
- **LSP**: Same as Analysis. Completion, hover, and signature help are auto-derived. No manual LSP code to update.

---

## 4. Package Design: `@stash/log`

### 4.1 Structure

```
@stash/log/
  index.stash          # Entry point — re-exports lib modules
  stash.json           # Package manifest
  README.md
  LICENSE
  lib/
    types.stash        # LogEntry struct, LogLevel enum
    logger.stash       # Core logging engine (module-scoped state)
    format.stash       # Format template logic
```

### 4.2 Types (`lib/types.stash`)

```stash
/// The severity levels for log filtering.
enum LogLevel {
    DEBUG,
    INFO,
    WARN,
    ERROR,
    OFF
}

/// Represents a single log entry before formatting.
struct LogEntry {
    level,
    message,
    timestamp
}
```

### 4.3 Core API (`lib/logger.stash`)

Module-level state (singleton via import cache):

```stash
import "types.stash" as types;
import "format.stash" as fmt;

/// Module-scoped state — singleton via import cache.
let _level = types.LogLevel.INFO;
let _format = "[{time}] [{level}] {msg}";
let _file_path = null;

/// Internal level ordering for comparison.
let _level_order = [
    types.LogLevel.DEBUG,
    types.LogLevel.INFO,
    types.LogLevel.WARN,
    types.LogLevel.ERROR,
    types.LogLevel.OFF
];

/// Check if a message at the given level should be emitted.
fn _should_log(msg_level) -> bool {
    let msg_idx = arr.indexOf(_level_order, msg_level);
    let cur_idx = arr.indexOf(_level_order, _level);
    return msg_idx >= cur_idx;
}

/// Write a formatted log message to the configured output.
fn _write(level, msg) {
    if (!_should_log(level)) { return; }

    let entry = types.LogEntry {
        level: level,
        message: msg,
        timestamp: time.iso()
    };

    let formatted = fmt.apply(_format, entry);

    if (_file_path != null) {
        fs.appendFile(_file_path, formatted + "\n");
    } else {
        io.eprintln(formatted);
    }
}

/// Log a message at DEBUG level.
fn debug(msg) { _write(types.LogLevel.DEBUG, conv.toStr(msg)); }

/// Log a message at INFO level.
fn info(msg) { _write(types.LogLevel.INFO, conv.toStr(msg)); }

/// Log a message at WARN level.
fn warn(msg) { _write(types.LogLevel.WARN, conv.toStr(msg)); }

/// Log a message at ERROR level.
fn error(msg) { _write(types.LogLevel.ERROR, conv.toStr(msg)); }

/// Set the minimum log level. Messages below this level are silently discarded.
fn setLevel(level) {
    if (arr.indexOf(_level_order, level) == -1) {
        throw "log.setLevel: level must be a LogLevel enum value";
    }
    _level = level;
}

/// Set the log message format template. Supports {time}, {level}, {msg} placeholders.
fn setFormat(template) {
    _format = template;
}

/// Redirect all subsequent log output to the specified file (append mode).
/// Pass null to revert to stderr.
fn toFile(path) {
    _file_path = path;
}

/// Returns the current minimum log level.
fn getLevel() -> LogLevel {
    return _level;
}
```

### 4.4 Format Logic (`lib/format.stash`)

```stash
import "types.stash" as types;

/// Map LogLevel enum values to display strings.
fn _level_name(level) -> string {
    if (level == types.LogLevel.DEBUG) { return "DEBUG"; }
    if (level == types.LogLevel.INFO) { return "INFO"; }
    if (level == types.LogLevel.WARN) { return "WARN"; }
    if (level == types.LogLevel.ERROR) { return "ERROR"; }
    return "UNKNOWN";
}

/// Apply the format template to a LogEntry.
fn apply(template, entry) -> string {
    let result = template;
    result = str.replace(result, "{time}", entry.timestamp);
    result = str.replace(result, "{level}", _level_name(entry.level));
    result = str.replace(result, "{msg}", entry.message);
    return result;
}
```

### 4.5 Entry Point (`index.stash`)

```stash
/// @stash/log — Structured, level-filtered logging for Stash.
///
/// Import the whole package:
///   import "@stash/log" as log;
///   log.logger.info("Server started");
///   log.logger.setLevel(log.types.LogLevel.DEBUG);
///
/// Or import individual modules:
///   import { info, warn, error, setLevel } from "@stash/log/lib/logger.stash";
///   import { LogLevel } from "@stash/log/lib/types.stash";

import "lib/types.stash" as types;
import "lib/logger.stash" as logger;
import "lib/format.stash" as format;
```

### 4.6 Package Manifest (`stash.json`)

```json
{
  "name": "@stash/log",
  "version": "1.0.0",
  "description": "Structured, level-filtered logging for Stash — configurable format, level filtering, and file output",
  "author": "TheKrystalShip",
  "license": "GPL-3.0",
  "main": "index.stash",
  "repository": "https://github.com/TheKrystalShip/stash-lang",
  "keywords": ["log", "logging", "debug", "structured", "levels", "sysadmin"],
  "stash": ">=1.0.0",
  "files": ["lib/", "index.stash", "README.md", "LICENSE"],
  "private": false
}
```

### 4.7 Usage Comparison

**Before (stdlib):**

```stash
log.info("Server started on port 8080");
log.setLevel("debug");
log.setFormat("{level}: {msg}");
log.toFile("/var/log/app.log");
```

**After (package):**

```stash
import { info, setLevel, setFormat, toFile } from "@stash/log/lib/logger.stash";
import { LogLevel } from "@stash/log/lib/types.stash";

info("Server started on port 8080");
setLevel(LogLevel.DEBUG);
setFormat("{level}: {msg}");
toFile("/var/log/app.log");
```

Or with namespace-style import:

```stash
import "@stash/log" as log;

log.logger.info("Server started on port 8080");
log.logger.setLevel(log.types.LogLevel.DEBUG);
```

---

## 5. Package Design: `@stash/store`

### 5.1 Structure

```
@stash/store/
  index.stash          # Entry point — re-exports lib modules
  stash.json           # Package manifest
  README.md
  LICENSE
  lib/
    types.stash        # StoreSnapshot struct
    store.stash        # Core key-value store (module-scoped state)
```

### 5.2 Types (`lib/types.stash`)

```stash
/// Represents a snapshot of the store's contents.
struct StoreSnapshot {
    size,
    keys,
    entries
}
```

### 5.3 Core API (`lib/store.stash`)

```stash
import "types.stash" as types;

/// Module-scoped state — singleton via import cache.
let _data = {};

/// Store a value under the given key. Returns null.
fn set(key, value) {
    dict.set(_data, key, value);
}

/// Retrieve the value for a key, or null if not found.
fn get(key) {
    if (dict.has(_data, key)) {
        return dict.get(_data, key);
    }
    return null;
}

/// Returns true if the key exists in the store.
fn has(key) -> bool {
    return dict.has(_data, key);
}

/// Remove a key from the store. Returns true if it existed.
fn remove(key) -> bool {
    if (!dict.has(_data, key)) {
        return false;
    }
    dict.remove(_data, key);
    return true;
}

/// Returns an array of all keys in the store.
fn keys() -> array {
    return dict.keys(_data);
}

/// Returns an array of all values in the store.
fn values() -> array {
    return dict.values(_data);
}

/// Returns the number of entries in the store.
fn size() -> int {
    return len(_data);
}

/// Remove all entries from the store.
fn clear() {
    dict.clear(_data);
}

/// Returns all key-value pairs as a dictionary (shallow copy).
fn all() -> dict {
    return dict.merge({}, _data);
}

/// Returns a dictionary of all entries whose keys start with the given prefix.
fn scope(prefix) -> dict {
    let result = {};
    let all_keys = dict.keys(_data);
    for (let i = 0; i < len(all_keys); i++) {
        let k = all_keys[i];
        if (str.startsWith(k, prefix)) {
            dict.set(result, k, dict.get(_data, k));
        }
    }
    return result;
}

/// Returns a snapshot of the store's current state.
fn snapshot() -> StoreSnapshot {
    return types.StoreSnapshot {
        size: size(),
        keys: keys(),
        entries: all()
    };
}
```

### 5.4 Entry Point (`index.stash`)

```stash
/// @stash/store — Process-scoped in-memory key-value store for Stash.
///
/// Import the whole package:
///   import "@stash/store" as store;
///   store.kv.set("user.name", "admin");
///   store.kv.get("user.name");
///
/// Or import individual functions:
///   import { set, get, has, scope } from "@stash/store/lib/store.stash";

import "lib/types.stash" as types;
import "lib/store.stash" as kv;
```

### 5.5 Package Manifest (`stash.json`)

```json
{
  "name": "@stash/store",
  "version": "1.0.0",
  "description": "Process-scoped in-memory key-value store for Stash — singleton state with prefix scoping",
  "author": "TheKrystalShip",
  "license": "GPL-3.0",
  "main": "index.stash",
  "repository": "https://github.com/TheKrystalShip/stash-lang",
  "keywords": ["store", "kv", "key-value", "state", "cache", "memory"],
  "stash": ">=1.0.0",
  "files": ["lib/", "index.stash", "README.md", "LICENSE"],
  "private": false
}
```

### 5.6 Usage Comparison

**Before (stdlib):**

```stash
store.set("app.port", 8080);
store.set("app.host", "localhost");
let port = store.get("app.port");
let app_config = store.scope("app.");
```

**After (package):**

```stash
import { set, get, scope } from "@stash/store/lib/store.stash";

set("app.port", 8080);
set("app.host", "localhost");
let port = get("app.port");
let app_config = scope("app.");
```

Or with namespace-style import:

```stash
import "@stash/store" as store;

store.kv.set("app.port", 8080);
let port = store.kv.get("app.port");
```

---

## 6. Implementation Checklist

### Phase 1: Create Packages (no breaking changes yet)

- [ ] Create `examples/packages/log/` with full package structure
- [ ] Create `examples/packages/store/` with full package structure
- [ ] Write README.md for both packages with API docs and examples
- [ ] Add Stash-level tests for both packages (`.test.stash` files)

### Phase 2: Remove from Stdlib

- [ ] Delete `Stash.Stdlib/BuiltIns/LogBuiltIns.cs`
- [ ] Delete `Stash.Stdlib/BuiltIns/StoreBuiltIns.cs`
- [ ] Remove `LogBuiltIns.Define()` from `StdlibDefinitions.cs`
- [ ] Remove `StoreBuiltIns.Define()` from `StdlibDefinitions.cs`
- [ ] Delete `Stash.Tests/Interpreting/LogBuiltInsTests.cs`
- [ ] Delete `Stash.Tests/Interpreting/StoreTests.cs`
- [ ] Delete `examples/logging.stash`
- [ ] Update `examples/dictionaries.stash` (remove `store.*` usage)

### Phase 3: Documentation Updates

- [ ] Update `docs/Stash — Standard Library Reference.md` — remove `log` and `store` sections, add migration note pointing to packages
- [ ] Update `docs/Stash — Language Specification.md` — remove/replace `log.*` examples
- [ ] Update `README.md` — remove `log.*` from example snippets
- [ ] Update `.github/instructions/stdlib.instructions.md` — remove from table, update namespace count (26 → 24)

### Phase 4: Validation

- [ ] `dotnet build` passes
- [ ] `dotnet test` passes (with removed test files gone)
- [ ] Verify no remaining references to `log.*` or `store.*` in stdlib code
- [ ] Verify package examples run correctly with `stash run`

---

## 7. Decisions & Tradeoffs

### Decision 1: Both namespaces removed simultaneously

**Chosen:** Remove `log` and `store` in the same spec/PR.
**Alternatives:** Remove them separately, or remove only `store`.
**Rationale:** The reasoning is identical for both — neither requires interpreter access, both are pure application-level conveniences, both suffer from the same static state problem. Doing them together avoids two rounds of the same documentation churn.
**Risk:** Larger blast radius in one change. Mitigated by the fact that neither namespace is deeply wired into other namespaces.

### Decision 2: Package uses `LogLevel` enum instead of strings

**Chosen:** `@stash/log` uses a `LogLevel` enum (`DEBUG`, `INFO`, `WARN`, `ERROR`, `OFF`).
**Alternatives:** Keep string-based levels (`"debug"`, `"info"`, etc.) for backward compatibility.
**Rationale:** Enums are type-safe — misspelling `"deubg"` fails silently with strings, but `LogLevel.DEUBG` is a clear error. Stash has enums; use them.
**Risk:** API divergence from the old stdlib. Acceptable because this is explicitly a new package, not a drop-in replacement.

### Decision 3: Module-level singleton state (not struct instances)

**Chosen:** Both packages use module-level `let` variables as singleton state, with exported functions operating on them.
**Alternatives:** (a) Export a `Logger` struct with methods. (b) Export a `createLogger()` factory function.
**Rationale:** The singleton pattern matches the original stdlib semantics (global state, no instantiation). Module caching in the VM guarantees singleton behavior. A factory pattern could be added later as an enhancement without breaking the simple API.
**Risk:** Same global-state coupling as the stdlib version. Mitigated by: (1) it's opt-in via `import`, not forced on every script; (2) packages can be versioned and forked.

### Decision 4: No deprecation period

**Chosen:** Remove from stdlib outright.
**Alternatives:** (a) Mark as deprecated for N releases before removal. (b) Keep both stdlib and package versions indefinitely.
**Rationale:** Stash is pre-1.0. Breaking changes are expected. Users of `log.*` and `store.*` are likely few, and the migration is a 2-line import change. A deprecation period adds complexity with marginal benefit.
**Risk:** Any existing scripts using `log.*` or `store.*` break immediately. Acceptable for a pre-1.0 language.

---

## 8. Open Questions

1. **Package testing**: Should the packages include `.test.stash` files using the TAP framework, or are the xUnit tests in `Stash.Tests/` sufficient? The existing example packages don't have tests — but these are first-party `@stash/*` packages and arguably should.

2. **`@stash/log` file output dependency**: `toFile()` requires `fs.appendFile()`, which needs FileSystem capability. Should the package gracefully handle the case where `fs` is unavailable (e.g., in the Playground), or just let it throw? The stdlib version silently had this capability — the package version makes the dependency explicit.

3. **Namespace count in docs**: Multiple docs reference "26 namespaces." After removal, this becomes 24. Need a grep to find all references.
