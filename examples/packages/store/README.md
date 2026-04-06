# @stash/store

Process-scoped in-memory key-value store for Stash — singleton state with prefix scoping.

> **Migration note:** This package replaces the former built-in `store` namespace. See the [Migration](#migration) section below.

## Installation

```bash
stash pkg install @stash/store
```

## Usage

### Namespace-style import

```stash
import "@stash/store" as store;

store.kv.set("user.name", "admin");
store.kv.set("user.role", "superuser");
store.kv.set("app.version", "2.1.0");

io.println(store.kv.get("user.name"));   // admin
io.println(store.kv.has("user.role"));   // true
io.println(store.kv.size());             // 3
```

### Destructured import

```stash
import { set, get, has, remove, scope, snapshot } from "@stash/store/lib/store.stash";

set("db.host", "localhost");
set("db.port", 5432);
set("db.name", "myapp");

let db = scope("db.");
// { "db.host": "localhost", "db.port": 5432, "db.name": "myapp" }

io.println(get("db.host"));   // localhost
io.println(has("db.port"));   // true
remove("db.name");
io.println(has("db.name"));   // false
```

### Singleton behaviour

The store module is cached by the import system — every file that imports it gets the same instance. State written in one module is visible in all others within the same process.

```stash
// module_a.stash
import { set } from "@stash/store/lib/store.stash";
set("shared.counter", 1);

// module_b.stash
import { get } from "@stash/store/lib/store.stash";
io.println(get("shared.counter"));   // 1
```

## API Reference

All functions are exported from `lib/store.stash`.

| Function   | Signature                     | Description                                                    |
| ---------- | ----------------------------- | -------------------------------------------------------------- |
| `set`      | `set(key, value)`             | Store a value under the given key. Returns `null`.             |
| `get`      | `get(key)`                    | Retrieve the value for a key, or `null` if not found.          |
| `has`      | `has(key) -> bool`            | Returns `true` if the key exists in the store.                 |
| `remove`   | `remove(key) -> bool`         | Remove a key. Returns `true` if it existed, `false` otherwise. |
| `keys`     | `keys() -> array`             | Returns an array of all keys in the store.                     |
| `values`   | `values() -> array`           | Returns an array of all values in the store.                   |
| `size`     | `size() -> int`               | Returns the number of entries in the store.                    |
| `clear`    | `clear()`                     | Remove all entries from the store.                             |
| `all`      | `all() -> dict`               | Returns a shallow copy of all key-value pairs as a dictionary. |
| `scope`    | `scope(prefix) -> dict`       | Returns all entries whose keys start with `prefix`.            |
| `snapshot` | `snapshot() -> StoreSnapshot` | Returns a `StoreSnapshot` of the store's current state.        |

### `scope(prefix)`

Returns a dictionary of all entries whose keys begin with the given prefix string. Useful for namespacing related keys.

```stash
import { set, scope } from "@stash/store/lib/store.stash";

set("config.debug", true);
set("config.timeout", 30);
set("config.retries", 3);
set("metrics.requests", 0);

let config = scope("config.");
// { "config.debug": true, "config.timeout": 30, "config.retries": 3 }
```

### `snapshot()`

Returns a `StoreSnapshot` struct capturing the store's state at the time of the call. The snapshot is a point-in-time copy — later mutations to the store do not affect it.

```stash
import { set, snapshot } from "@stash/store/lib/store.stash";

set("x", 1);
set("y", 2);

let snap = snapshot();
io.println(snap.size);     // 2
io.println(snap.keys);     // ["x", "y"]
io.println(snap.entries);  // { "x": 1, "y": 2 }
```

## Types

### `StoreSnapshot`

Defined in `lib/types.stash`.

```stash
struct StoreSnapshot {
    size,      // int   — number of entries at snapshot time
    keys,      // array — all keys at snapshot time
    entries    // dict  — shallow copy of all entries at snapshot time
}
```

## Migration

This package replaces the former built-in `store` namespace. The API is identical — only the import style changes.

### Before (stdlib)

```stash
store.set("user.name", "admin");
store.set("user.role", "superuser");

io.println(store.get("user.name"));
io.println(store.has("user.role"));
io.println(store.size());

let snap = store.snapshot();
```

### After (package)

```stash
import "@stash/store" as store;

store.kv.set("user.name", "admin");
store.kv.set("user.role", "superuser");

io.println(store.kv.get("user.name"));
io.println(store.kv.has("user.role"));
io.println(store.kv.size());

let snap = store.kv.snapshot();
```

Or with destructuring for a drop-in replacement feel:

```stash
import { set, get, has, size, snapshot } from "@stash/store/lib/store.stash";

set("user.name", "admin");
set("user.role", "superuser");

io.println(get("user.name"));
io.println(has("user.role"));
io.println(size());

let snap = snapshot();
```

## License

GPL-3.0-only — see [LICENSE](LICENSE).
