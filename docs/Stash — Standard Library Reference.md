# Stash — Standard Library Reference

> Standard library reference for **Stash**, the C-style shell scripting language.
> For language syntax, semantics, and interpreter details, see the [Language Specification](Stash%20—%20Language%20Specification.md).
>
> **Companion documents:**
>
> - [Language Specification](Stash%20—%20Language%20Specification.md) — syntax, type system, scoping rules, interpreter architecture
> - [DAP — Debug Adapter Protocol](specs/DAP%20—%20Debug%20Adapter%20Protocol.md) — debug adapter server
> - [LSP — Language Server Protocol](specs/LSP%20—%20Language%20Server%20Protocol.md) — language server
> - [TAP — Testing Infrastructure](specs/TAP%20—%20Testing%20Infrastructure.md) — testing primitives, assert namespace, TAP output

---

## Table of Contents

1. [Overview](#overview)
2. [`io` — Standard I/O](#io--standard-io)
3. [`conv` — Type Conversion](#conv--type-conversion)
4. [`env` — Environment Variables](#env--environment-variables)
5. [`fs` — File System Operations](#fs--file-system-operations)
6. [`path` — Path Manipulation](#path--path-manipulation)
7. [`str` — String Operations](#str--string-operations)
8. [`arr` — Array Operations](#arr--array-operations)
9. [`dict` — Dictionary Operations](#dict--dictionary-operations)
10. [`math` — Math Functions](#math--math-functions)
11. [`time` — Time & Date](#time--time--date)
12. [`json` — JSON](#json--json)
13. [`ini` — INI Configuration](#ini--ini-configuration)
14. [`yaml` — YAML](#yaml--yaml)
15. [`toml` — TOML](#toml--toml)
16. [`config` — Format-Agnostic Configuration](#config--format-agnostic-configuration)
17. [`http` — HTTP Requests](#http--http-requests)
18. [`process` — Process Management](#process--process-management)
19. [`tpl` — Templating](#tpl--templating)
20. [`store` — In-Memory Store](#store--in-memory-store)
21. [`crypto` — Cryptography & Hashing](#crypto--cryptography--hashing)
22. [`encoding` — Encoding & Decoding](#encoding--encoding--decoding)
23. [`term` — Terminal Formatting](#term--terminal-formatting)
24. [`sys` — System Information](#sys--system-information)
25. [`task` — Parallel Tasks](#task--parallel-tasks)
26. [`net` — Networking](#net--networking)
27. [`ssh` — SSH Remote Execution](#ssh--ssh-remote-execution)
28. [`sftp` — SFTP File Transfer](#sftp--sftp-file-transfer)
29. [`log` — Logging](#log--logging)
30. [Argument Parsing](#argument-parsing)

---

## Overview

Stash organizes built-in functions into **namespaces** accessed via dot notation (e.g., `fs.readFile(path)`). A small set of fundamental functions remain global (`typeof`, `nameof`, `len`, `lastError`); everything else lives in a namespace.

Namespaces are first-class values — `typeof(fs)` returns `"namespace"`. Assignment to namespace members is not permitted.

### Global Functions

| Function      | Description                                                                                   |
| ------------- | --------------------------------------------------------------------------------------------- |
| `typeof(val)` | Return the type of a value as string (returns `"Error"` for error values)                     |
| `nameof(val)` | Return the declared name of a value — struct/enum/interface names instead of meta-type string |
| `len(val)`    | Length of a string or array                                                                   |
| `lastError()` | Last error value (Error object with `.message`, `.type`, `.stack`) or null                    |

---

## `io` — Standard I/O

| Function               | Description                                           |
| ---------------------- | ----------------------------------------------------- |
| `io.println(val)`      | Print value followed by newline                       |
| `io.print(val)`        | Print value without newline                           |
| `io.eprintln(val)`     | Print value followed by newline to standard error     |
| `io.eprint(val)`       | Print value without newline to standard error         |
| `io.readLine(prompt?)` | Read a line from standard input, with optional prompt |
| `io.confirm(prompt)`   | Display a [y/N] confirmation prompt, returns boolean  |

---

## `conv` — Type Conversion

| Function               | Description                                           |
| ---------------------- | ----------------------------------------------------- |
| `conv.toStr(val)`      | Convert value to string                               |
| `conv.toInt(val)`      | Parse string to integer                               |
| `conv.toFloat(val)`    | Parse string to float                                 |
| `conv.toBool(val)`     | Convert a value to boolean using truthiness rules     |
| `conv.toHex(val)`      | Convert an integer to hexadecimal string              |
| `conv.toOct(val)`      | Convert an integer to octal string                    |
| `conv.toBin(val)`      | Convert an integer to binary string                   |
| `conv.fromHex(s)`      | Parse a hexadecimal string to integer (supports `0x`) |
| `conv.fromOct(s)`      | Parse an octal string to integer (supports `0o`)      |
| `conv.fromBin(s)`      | Parse a binary string to integer (supports `0b`)      |
| `conv.charCode(c)`     | Return the Unicode code point of the first character  |
| `conv.fromCharCode(n)` | Return a character from its Unicode code point        |

---

## `env` — Environment Variables

| Function                 | Description                                                           |
| ------------------------ | --------------------------------------------------------------------- |
| `env.get(name)`          | Read environment variable (null if unset)                             |
| `env.set(name, value)`   | Set environment variable                                              |
| `env.has(name)`          | Check if an environment variable exists                               |
| `env.all()`              | Return all environment variables as a dictionary                      |
| `env.withPrefix(prefix)` | Return all environment variables starting with prefix as a dictionary |
| `env.remove(name)`       | Delete an environment variable                                        |
| `env.cwd()`              | Return the current working directory                                  |
| `env.home()`             | Return the user's home directory path                                 |
| `env.hostname()`         | Return the machine hostname                                           |
| `env.user()`             | Return the current username                                           |
| `env.os()`               | Return the OS name (`"linux"`, `"macos"`, `"windows"`)                |
| `env.arch()`             | Return the CPU architecture (`"x64"`, `"arm64"`, etc.)                |

### `env.withPrefix(prefix)`

Returns a dictionary containing all environment variables whose names start with the given prefix. Keys in the returned dictionary retain their full original names.

```stash
env.set("MYAPP_HOST", "localhost");
env.set("MYAPP_PORT", "8080");
env.set("OTHER_VAR", "value");

let appVars = env.withPrefix("MYAPP_");
// appVars contains: { "MYAPP_HOST": "localhost", "MYAPP_PORT": "8080" }
// "OTHER_VAR" is excluded

for (let key in appVars) {
    io.println(key + " = " + appVars[key]);
}
```

### `env.loadFile(path, prefix?)`

Loads environment variables from a `.env`-style file. Returns the number of variables loaded. When the optional `prefix` parameter is provided, each key is prefixed before being set in the environment — this prevents collisions with existing environment variables.

```stash
// .env file contains: HOST=localhost, PORT=8080

// Load without prefix (keys set as-is)
env.loadFile(".env");
io.println(env.get("HOST"));          // "localhost"

// Load with prefix (keys get prefix prepended)
env.loadFile(".env", "MYAPP_");
io.println(env.get("MYAPP_HOST"));    // "localhost"
io.println(env.get("MYAPP_PORT"));    // "8080"
```

Combine with `env.withPrefix` to load and retrieve namespaced configuration:

```stash
env.loadFile(".env", "MYAPP_");
let config = env.withPrefix("MYAPP_");
```

---

## `fs` — File System Operations

| Function                               | Description                                                                       |
| -------------------------------------- | --------------------------------------------------------------------------------- |
| `fs.readFile(path)`                    | Read file contents as string                                                      |
| `fs.writeFile(path, content)`          | Write string to file (creates or overwrites)                                      |
| `fs.appendFile(path, content)`         | Append string to file                                                             |
| `fs.readLines(path)`                   | Read file as array of lines                                                       |
| `fs.exists(path)`                      | Check if a file exists (returns boolean)                                          |
| `fs.dirExists(path)`                   | Check if a directory exists (returns boolean)                                     |
| `fs.pathExists(path)`                  | Check if a file or directory exists                                               |
| `fs.isFile(path)`                      | Check if path is a file (returns boolean)                                         |
| `fs.isDir(path)`                       | Check if path is a directory (returns boolean)                                    |
| `fs.isSymlink(path)`                   | Check if path is a symbolic link                                                  |
| `fs.createDir(path)`                   | Create a directory (including parents)                                            |
| `fs.delete(path)`                      | Delete a file or directory (recursive)                                            |
| `fs.copy(src, dst)`                    | Copy a file (overwrites destination)                                              |
| `fs.move(src, dst)`                    | Move/rename a file (overwrites destination)                                       |
| `fs.size(path)`                        | Get file size in bytes                                                            |
| `fs.listDir(path)`                     | List entries in a directory (returns array)                                       |
| `fs.glob(pattern)`                     | Find files matching a glob pattern                                                |
| `fs.walk(path)`                        | Recursively list all files under a directory                                      |
| `fs.tempFile()`                        | Create a temporary file, returns its path                                         |
| `fs.tempDir()`                         | Create a temporary directory, returns its path                                    |
| `fs.modifiedAt(path)`                  | Get last modified time as Unix timestamp                                          |
| `fs.createFile(path)`                  | Create an empty file or update modified time                                      |
| `fs.symlink(target, path)`             | Create a symbolic link                                                            |
| `fs.stat(path)`                        | Get full file info dict (size, isFile, isDir, isSymlink, modified, created, name) |
| `fs.readable(path)`                    | Check if current process can read the path (returns boolean)                      |
| `fs.writable(path)`                    | Check if current process can write to the path (returns boolean)                  |
| `fs.executable(path)`                  | Check if a file is executable (Unix: mode bits, Windows: file extension)          |
| `fs.getPermissions(path)`              | Get file permission details (returns `FilePermissions` struct)                    |
| `fs.setPermissions(path, permissions)` | Set file permissions from a `FilePermissions` struct                              |
| `fs.setReadOnly(path, readOnly)`       | Set or clear the read-only state of a file or directory                           |
| `fs.setExecutable(path, executable)`   | Set or clear the executable bit (Unix) — no-op on Windows                         |
| `fs.watch(path, callback, options?)`   | Watch a file or directory for changes, returns a `Watcher` handle                 |
| `fs.unwatch(watcher)`                  | Stop a file watcher previously created by `fs.watch()`                            |

### Permission Types

```stash
struct FilePermission {
    read: bool,
    write: bool,
    execute: bool
}

struct FilePermissions {
    owner: FilePermission,
    group: FilePermission,
    others: FilePermission
}
```

`fs.getPermissions(path)` returns a `FilePermissions` struct describing the read, write, and execute permissions for the file's owner, group, and others. On Unix, this maps directly to file mode bits. On Windows, it approximates using the read-only attribute and file extension.

```stash
let perms = fs.getPermissions("/opt/app/run.sh");
if (perms.owner.execute) {
    io.println("Script is executable");
}
```

`fs.setPermissions(path, permissions)` applies a `FilePermissions` struct to set the full permission bits. On Unix, this sets the full `rwx` mode for owner/group/others. On Windows, it controls the read-only attribute based on the owner's write permission.

```stash
// Get current permissions, modify, and apply
let perms = fs.getPermissions("config.toml");
perms.owner.write = false;
perms.group.write = false;
perms.others.write = false;
fs.setPermissions("config.toml", perms);
```

`fs.setReadOnly(path, readOnly)` and `fs.setExecutable(path, executable)` are convenience functions for the most common permission operations:

```stash
fs.setReadOnly("config.toml", true);     // Make read-only
fs.setExecutable("deploy.sh", true);     // Make executable (Unix)
```

### File Watching

```stash
enum WatchEventType {
    Created,
    Modified,
    Deleted,
    Renamed
}

struct WatchEvent {
    type: WatchEventType,  // The type of file system event
    path: string,          // Absolute path of the affected file/directory
    oldPath: string        // Only populated for Renamed events; null otherwise
}

struct WatchOptions {
    recursive: bool,    // Watch subdirectories (default: false)
    filter: string,     // Glob filter for filenames (default: "*" — all files)
    bufferSize: int,    // Internal buffer size in bytes (default: 8192)
    debounce: int       // Debounce window in milliseconds (default: 100; 0 = no debounce)
}
```

`fs.watch(path, callback, options?)` watches a file or directory for changes. It returns a `Watcher` handle used to stop watching via `fs.unwatch()`. The callback receives a `WatchEvent` struct for each file system event.

```stash
// Watch a directory for changes
let watcher = fs.watch("/var/log", (event) => {
    io.println($"[{event.type}] {event.path}");
});

// Watch with options
let watcher = fs.watch("./src", (event) => {
    if (event.type == fs.WatchEventType.Modified) {
        $(dotnet build);
    }
}, fs.WatchOptions { recursive: true, filter: "*.cs" });
```

**Callback execution:** Callbacks run in forked contexts (`ctx.Fork()`). Value-type variables from the parent scope are snapshotted — changes inside the callback are not visible outside. However, reference types (dicts, struct instances) are shared, so mutations to shared objects are visible in both directions:

```stash
let state = { configDirty: false };
fs.watch("/etc/app/config.toml", (event) => {
    state.configDirty = true;  // Mutates the shared dict — visible in parent
});
```

**Debouncing:** Events for the same `(path, type)` pair within the debounce window (default: 100ms) are collapsed into a single callback invocation. This prevents duplicate notifications when editors save files. Renamed events are never debounced. Set `debounce: 0` in `WatchOptions` to receive every raw OS event.

`fs.unwatch(watcher)` stops a watcher and releases OS resources. Calling `fs.unwatch()` on an already-stopped handle is a no-op. All active watchers are automatically disposed when the script exits.

```stash
let watcher = fs.watch("./logs", (event) => {
    io.println($"Changed: {event.path}");
});

// Later: stop watching
fs.unwatch(watcher);
```

---

## `path` — Path Manipulation

| Function                  | Description                                                        |
| ------------------------- | ------------------------------------------------------------------ |
| `path.abs(p)`             | Get absolute path                                                  |
| `path.dir(p)`             | Get directory portion of path                                      |
| `path.base(p)`            | Get filename with extension                                        |
| `path.name(p)`            | Get filename without extension                                     |
| `path.ext(p)`             | Get file extension (including `.`)                                 |
| `path.join(a, b)`         | Join two path segments                                             |
| `path.normalize(p)`       | Normalize path (resolve `.` and `..`, remove redundant separators) |
| `path.isAbsolute(p)`      | Return `true` if path is absolute                                  |
| `path.relative(from, to)` | Compute relative path from one path to another                     |
| `path.separator()`        | Return the platform path separator (`/` on Unix, `\` on Windows)   |

---

## `str` — String Operations

All `str` functions take the target string as the first argument. Strings are immutable — functions return new strings rather than modifying in place.

### Case & Whitespace

| Function            | Description                                    |
| ------------------- | ---------------------------------------------- |
| `str.upper(s)`      | Convert to uppercase                           |
| `str.lower(s)`      | Convert to lowercase                           |
| `str.trim(s)`       | Remove leading and trailing whitespace         |
| `str.trimStart(s)`  | Remove leading whitespace                      |
| `str.trimEnd(s)`    | Remove trailing whitespace                     |
| `str.capitalize(s)` | Capitalize first character, lowercase the rest |
| `str.title(s)`      | Convert to title case (capitalize each word)   |

### Search & Test

| Function                    | Description                                              |
| --------------------------- | -------------------------------------------------------- |
| `str.contains(s, sub)`      | Return `true` if `s` contains `sub`                      |
| `str.startsWith(s, prefix)` | Return `true` if `s` starts with `prefix`                |
| `str.endsWith(s, suffix)`   | Return `true` if `s` ends with `suffix`                  |
| `str.indexOf(s, sub)`       | Return index of first occurrence of `sub`, or `-1`       |
| `str.lastIndexOf(s, sub)`   | Return index of last occurrence of `sub`, or `-1`        |
| `str.count(s, sub)`         | Return the count of non-overlapping occurrences of `sub` |

### Character Tests

| Function            | Description                                                |
| ------------------- | ---------------------------------------------------------- |
| `str.isDigit(s)`    | Return `true` if all characters are digits                 |
| `str.isAlpha(s)`    | Return `true` if all characters are letters                |
| `str.isAlphaNum(s)` | Return `true` if all characters are alphanumeric           |
| `str.isUpper(s)`    | Return `true` if all letters are uppercase                 |
| `str.isLower(s)`    | Return `true` if all letters are lowercase                 |
| `str.isEmpty(s)`    | Return `true` if string is null, empty, or whitespace-only |

### Extraction & Transformation

| Function                           | Description                                                                          |
| ---------------------------------- | ------------------------------------------------------------------------------------ |
| `str.substring(s, start, end?)`    | Extract substring from `start` to `end` (exclusive); `end` defaults to string length |
| `str.replace(s, old, new)`         | Replace first occurrence of `old` with `new`                                         |
| `str.replaceAll(s, old, new)`      | Replace all occurrences of `old` with `new`                                          |
| `str.split(s, delimiter)`          | Split string into array by `delimiter`                                               |
| `str.repeat(s, count)`             | Repeat string `count` times                                                          |
| `str.reverse(s)`                   | Reverse the string                                                                   |
| `str.chars(s)`                     | Convert to array of single-character strings                                         |
| `str.padStart(s, len, fill?)`      | Pad start to `len` characters with `fill` (default `" "`)                            |
| `str.padEnd(s, len, fill?)`        | Pad end to `len` characters with `fill` (default `" "`)                              |
| `str.format(template, ...args)`    | Replace `{0}`, `{1}`, etc. placeholders with arguments                               |
| `str.lines(s)`                     | Split string into array of lines (handles `\r\n`, `\r`, `\n`)                        |
| `str.words(s)`                     | Split string into array of whitespace-separated words                                |
| `str.truncate(s, maxLen, suffix?)` | Truncate to `maxLen` characters with optional suffix (default `"..."`)               |
| `str.slug(s)`                      | Convert to URL-friendly slug (lowercase, hyphens, no special chars)                  |
| `str.wrap(s, width)`               | Word-wrap string to specified width, preserving paragraph breaks                     |

### Regex

| Function                             | Description                                                     |
| ------------------------------------ | --------------------------------------------------------------- |
| `str.match(s, pattern)`              | Return the first substring matching a regex pattern (or `null`) |
| `str.matchAll(s, pattern)`           | Return an array of all substrings matching a regex pattern      |
| `str.isMatch(s, pattern)`            | Return `true` if string contains a match for the regex pattern  |
| `str.replaceRegex(s, pattern, repl)` | Replace all regex matches with a replacement string             |

### Examples

```stash
let name = "  Hello, World!  ";

// Case conversion
io.println(str.upper(name));           // "  HELLO, WORLD!  "
io.println(str.lower(name));           // "  hello, world!  "

// Trimming
let trimmed = str.trim(name);          // "Hello, World!"

// Search
io.println(str.contains(trimmed, "World"));   // true
io.println(str.indexOf(trimmed, "World"));    // 7

// Extraction & transformation
io.println(str.substring(trimmed, 0, 5));     // "Hello"
io.println(str.replace(trimmed, "World", "Stash")); // "Hello, Stash!"

// Splitting & joining
let parts = str.split("a,b,c", ",");          // ["a", "b", "c"]
let repeated = str.repeat("ab", 3);           // "ababab"

// Padding
io.println(str.padStart("42", 5, "0"));       // "00042"
io.println(str.padEnd("hi", 6));              // "hi    "
```

---

## `arr` — Array Operations

All `arr` functions take the target array as the first argument. Functions that mutate the array do so **in-place**.

### Core Manipulation

| Function                          | Description                                               |
| --------------------------------- | --------------------------------------------------------- |
| `arr.push(array, value)`          | Add value to end of array                                 |
| `arr.pop(array)`                  | Remove and return last element (error if empty)           |
| `arr.peek(array)`                 | Return last element without removing (error if empty)     |
| `arr.insert(array, index, value)` | Insert value at index (shifts elements right)             |
| `arr.removeAt(array, index)`      | Remove and return element at index                        |
| `arr.remove(array, value)`        | Remove first occurrence of value; returns `true` if found |
| `arr.clear(array)`                | Remove all elements                                       |

### Searching

| Function                     | Description                                                          |
| ---------------------------- | -------------------------------------------------------------------- |
| `arr.contains(array, value)` | Return `true` if value exists in array                               |
| `arr.indexOf(array, value)`  | Return index of first occurrence, or `-1` if not found               |
| `arr.findIndex(array, fn)`   | Return index of first element where `fn(element)` is truthy, or `-1` |

### Transformation

| Function                       | Description                                                            |
| ------------------------------ | ---------------------------------------------------------------------- |
| `arr.slice(array, start, end)` | Return new sub-array from start (inclusive) to end (exclusive)         |
| `arr.concat(array1, array2)`   | Return new array combining both arrays                                 |
| `arr.join(array, separator)`   | Join elements into a string with separator                             |
| `arr.reverse(array)`           | Reverse array in-place                                                 |
| `arr.sort(array)`              | Sort array in-place (numbers and strings; error on mixed types)        |
| `arr.unique(array)`            | Return new array with duplicate values removed (first occurrence kept) |
| `arr.flat(array)`              | Flatten one level of nesting into a new array                          |
| `arr.flatMap(array, fn)`       | Map each element with `fn`, then flatten one level                     |

### Higher-Order Functions

| Function                                      | Description                                                                                                                                                     |
| --------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `arr.map(array, fn)`                          | Return new array with `fn(element)` applied to each element                                                                                                     |
| `arr.filter(array, fn)`                       | Return new array of elements where `fn(element)` is truthy                                                                                                      |
| `arr.forEach(array, fn)`                      | Call `fn(element)` for each element                                                                                                                             |
| `arr.parMap(array, fn, [maxConcurrency])`     | Like `arr.map()` but runs the mapping function on each element in parallel; results maintain original order. Optional `maxConcurrency` limits parallel threads. |
| `arr.parFilter(array, fn, [maxConcurrency])`  | Like `arr.filter()` but tests each element in parallel; results maintain original order. Optional `maxConcurrency` limits parallel threads.                     |
| `arr.parForEach(array, fn, [maxConcurrency])` | Like `arr.forEach()` but runs the callback on each element in parallel; returns `null`. Optional `maxConcurrency` limits parallel threads.                      |
| `arr.find(array, fn)`                         | Return first element where `fn(element)` is truthy, or `null`                                                                                                   |
| `arr.findIndex(array, fn)`                    | Return index of first truthy `fn(element)`, or `-1`                                                                                                             |
| `arr.reduce(array, fn, initial)`              | Fold array: calls `fn(accumulator, element)` for each element                                                                                                   |
| `arr.any(array, fn)`                          | Return `true` if any element satisfies `fn`                                                                                                                     |
| `arr.every(array, fn)`                        | Return `true` if all elements satisfy `fn`                                                                                                                      |
| `arr.count(array, fn)`                        | Return count of elements where `fn(element)` is truthy                                                                                                          |

### Aggregation & Grouping

| Function                 | Description                                                    |
| ------------------------ | -------------------------------------------------------------- |
| `arr.sortBy(array, fn)`  | Return new array sorted by `fn(element)` key (does not mutate) |
| `arr.groupBy(array, fn)` | Group elements into dict keyed by `fn(element)` result         |
| `arr.sum(array)`         | Sum all numeric elements                                       |
| `arr.min(array)`         | Return minimum numeric element                                 |
| `arr.max(array)`         | Return maximum numeric element                                 |

### Partitioning & Slicing

| Function                   | Description                                                              |
| -------------------------- | ------------------------------------------------------------------------ |
| `arr.zip(a, b)`            | Combine two arrays into array of `[a[i], b[i]]` pairs (stops at shorter) |
| `arr.chunk(array, size)`   | Split array into chunks of `size` elements (last chunk may be smaller)   |
| `arr.take(array, n)`       | Return new array with first `n` elements                                 |
| `arr.drop(array, n)`       | Return new array with first `n` elements removed                         |
| `arr.partition(array, fn)` | Split into `[matching, non-matching]` arrays based on predicate          |
| `arr.shuffle(array)`       | Randomly shuffle array in-place (Fisher-Yates algorithm)                 |

### Examples

```stash
let nums = [3, 1, 4, 1, 5];

// Core manipulation
arr.push(nums, 9);              // [3, 1, 4, 1, 5, 9]
let last = arr.pop(nums);       // last = 9, nums = [3, 1, 4, 1, 5]
arr.insert(nums, 0, 0);         // [0, 3, 1, 4, 1, 5]
arr.removeAt(nums, 0);          // [3, 1, 4, 1, 5]

// Searching
arr.contains(nums, 4);          // true
arr.indexOf(nums, 1);           // 1

// Transformation
let sub = arr.slice(nums, 1, 3);    // [1, 4]
let all = arr.concat(nums, [6, 7]); // [3, 1, 4, 1, 5, 6, 7]
arr.sort(nums);                     // [1, 1, 3, 4, 5]
let csv = arr.join(nums, ", ");     // "1, 1, 3, 4, 5"

// Higher-order functions
let doubled = arr.map(nums, (x) => x * 2);      // [2, 2, 6, 8, 10]
let big = arr.filter(nums, (x) => x > 2);        // [3, 4, 5]
let sum = arr.reduce(nums, (acc, x) => acc + x, 0); // 14
let found = arr.find(nums, (x) => x > 3);        // 4
arr.forEach(nums, (x) => io.println(x));          // prints each element

// Parallel higher-order functions
let pDoubled = arr.parMap(nums, (x) => x * 2);      // parallel map, unlimited concurrency
let pBig = arr.parFilter(nums, (x) => x > 2);       // parallel filter, unlimited concurrency
arr.parForEach(nums, (x) => io.println(x));          // parallel forEach, unlimited

// Limit concurrency with optional third argument
let limited = arr.parMap(urls, (url) => {
    return $(curl -s ${url}).stdout;
}, 4);  // max 4 parallel operations

// Predicates
let hasEven = arr.any(nums, (x) => x % 2 == 0);    // true
let allPos = arr.every(nums, (x) => x > 0);         // true
let evens = arr.count(nums, (x) => x % 2 == 0);     // 1
let idx = arr.findIndex(nums, (x) => x > 3);        // 2

// Deduplication and flattening
let unique = arr.unique([1, 2, 2, 3, 1]);          // [1, 2, 3]
let flat = arr.flat([[1, 2], [3, 4]]);             // [1, 2, 3, 4]
let expanded = arr.flatMap([1, 2, 3], (x) => [x, x * 10]); // [1, 10, 2, 20, 3, 30]

// Aggregation and grouping
let total = arr.sum([1, 2, 3, 4, 5]);              // 15
let smallest = arr.min([3, 1, 4, 1, 5]);            // 1
let largest = arr.max([3, 1, 4, 1, 5]);             // 5

let sorted = arr.sortBy(["banana", "apple", "cherry"], (x) => x);
// ["apple", "banana", "cherry"]

let people = [
    { name: "Alice", dept: "eng" },
    { name: "Bob", dept: "sales" },
    { name: "Charlie", dept: "eng" }
];
let byDept = arr.groupBy(people, (p) => p.dept);
// { "eng": [Alice, Charlie], "sales": [Bob] }
```

---

## `dict` — Dictionary Operations

All `dict` functions (except `dict.new` and `dict.merge`) take the target dictionary as the first argument. Functional operations (`map`, `filter`, `merge`) return **new** dictionaries — they do not mutate the original.

| Function                  | Description                                                            |
| ------------------------- | ---------------------------------------------------------------------- |
| `dict.new()`              | Create an empty dictionary                                             |
| `dict.get(d, key)`        | Get value for key, or `null` if not found                              |
| `dict.set(d, key, value)` | Set key-value pair (mutates dictionary)                                |
| `dict.has(d, key)`        | Return `true` if key exists                                            |
| `dict.remove(d, key)`     | Remove key; returns `true` if found                                    |
| `dict.clear(d)`           | Remove all entries                                                     |
| `dict.keys(d)`            | Return array of all keys                                               |
| `dict.values(d)`          | Return array of all values                                             |
| `dict.size(d)`            | Return number of entries                                               |
| `dict.pairs(d)`           | Return array of Pair structs (each with `.key` and `.value` fields)    |
| `dict.forEach(d, fn)`     | Call `fn(key, value)` for each entry                                   |
| `dict.map(d, fn)`         | Return new dictionary with values transformed by `fn(key, value)`      |
| `dict.filter(d, fn)`      | Return new dictionary keeping entries where `fn(key, value)` is truthy |
| `dict.merge(d1, d2)`      | Return new dictionary combining both (d2 wins on key conflicts)        |
| `dict.fromPairs(pairs)`   | Create dictionary from array of `[key, value]` pairs                   |
| `dict.pick(d, keys)`      | Return new dictionary with only the specified keys                     |
| `dict.omit(d, keys)`      | Return new dictionary excluding the specified keys                     |
| `dict.defaults(d, defs)`  | Return new dictionary with missing keys filled from defaults           |
| `dict.any(d, fn)`         | Return `true` if any entry satisfies `fn(key, value)`                  |
| `dict.every(d, fn)`       | Return `true` if all entries satisfy `fn(key, value)`                  |
| `dict.find(d, fn)`        | Return first value where `fn(key, value)` is truthy, or `null`         |

### Index Syntax

Dictionaries also support index access using `d[key]` and `d[key] = value`:

```stash
let d = dict.new();
d["name"] = "Alice";
d["age"] = 30;
let name = d["name"];       // "Alice"
let missing = d["nope"];    // null (no error)
```

### Examples

```stash
let config = dict.new();
config["host"] = "localhost";
config["port"] = 8080;
config["debug"] = true;

// Check and retrieve
if (dict.has(config, "host")) {
    io.println("Host: " + config["host"]);
}

// Iteration
for (let key in config) {
    io.println(key + " = " + config[key]);
}

// Pair struct iteration
let pairs = dict.pairs(config);
for (let pair in pairs) {
    io.println(pair.key + " = " + pair.value);
}

// Higher-order usage
dict.forEach(config, (k, v) => {
    io.println(k + " => " + v);
});

// Mapping — transform values
let prices = dict.new();
prices["apple"] = 2;
prices["banana"] = 3;

let doubled = dict.map(prices, (key, price) => price * 2);
// doubled: { "apple": 4, "banana": 6 }

// Filtering — keep matching entries
let expensive = dict.filter(prices, (key, price) => price > 2);
// expensive: { "banana": 3 }

// Merging
let defaults = dict.new();
defaults["timeout"] = 30;
defaults["retries"] = 3;

let merged = dict.merge(defaults, config);
// merged has all keys from both; config values take priority
// Note: dict.merge performs a shallow copy — nested arrays or
// dictionaries in values are shared, not cloned.
```

---

## `math` — Math Functions

### Core

| Function                      | Description                                                               |
| ----------------------------- | ------------------------------------------------------------------------- |
| `math.abs(value)`             | Return the absolute value of a number                                     |
| `math.ceil(value)`            | Round up to the nearest integer (type-preserving: float in → float out)   |
| `math.floor(value)`           | Round down to the nearest integer (type-preserving: float in → float out) |
| `math.round(value)`           | Round to the nearest integer (type-preserving: float in → float out)      |
| `math.sign(value)`            | Return the sign: `-1`, `0`, or `1`                                        |
| `math.min(a, b)`              | Return the smaller of two numbers                                         |
| `math.max(a, b)`              | Return the larger of two numbers                                          |
| `math.clamp(value, min, max)` | Constrain a number within a min/max range                                 |

### Powers, Roots, and Logarithms

| Function                   | Description                                         |
| -------------------------- | --------------------------------------------------- |
| `math.pow(base, exponent)` | Raise a number to a power                           |
| `math.sqrt(value)`         | Return the square root of a number                  |
| `math.exp(value)`          | Return _e_ raised to the given power                |
| `math.log(value)`          | Return the natural logarithm (base _e_) of a number |
| `math.log10(value)`        | Return the base-10 logarithm of a number            |
| `math.log2(value)`         | Return the base-2 logarithm of a number             |

### Trigonometry

| Function           | Description                                                        |
| ------------------ | ------------------------------------------------------------------ |
| `math.sin(value)`  | Return the sine of an angle (radians)                              |
| `math.cos(value)`  | Return the cosine of an angle (radians)                            |
| `math.tan(value)`  | Return the tangent of an angle (radians)                           |
| `math.asin(value)` | Return the arc sine (inverse sine) in radians                      |
| `math.acos(value)` | Return the arc cosine (inverse cosine) in radians                  |
| `math.atan(value)` | Return the arc tangent (inverse tangent) in radians                |
| `math.atan2(y, x)` | Return the angle in radians between the positive x-axis and (x, y) |

### Random Numbers

| Function                   | Description                                                       |
| -------------------------- | ----------------------------------------------------------------- |
| `math.random()`            | Return a random float between 0.0 (inclusive) and 1.0 (exclusive) |
| `math.randomInt(min, max)` | Return a random integer between min and max (inclusive)           |

### Constants

| Constant  | Value               | Description                                           |
| --------- | ------------------- | ----------------------------------------------------- |
| `math.PI` | `3.141592653589793` | Ratio of a circle's circumference to its diameter (π) |
| `math.E`  | `2.718281828459045` | Euler's number, base of natural logarithms            |

---

## `time` — Time & Date

| Function                      | Description                                                    |
| ----------------------------- | -------------------------------------------------------------- |
| `time.now()`                  | Return current Unix time in seconds (float)                    |
| `time.millis()`               | Return current Unix time in milliseconds                       |
| `time.sleep(seconds)`         | Pause execution for the given number of seconds                |
| `time.format(timestamp, fmt)` | Format a Unix timestamp using a format string                  |
| `time.parse(dateString, fmt)` | Parse a date string and return Unix timestamp                  |
| `time.date()`                 | Return current date as `YYYY-MM-DD` string                     |
| `time.clock()`                | Return high-resolution monotonic clock time (for benchmarking) |
| `time.iso()`                  | Return current time as ISO 8601 string with UTC timezone       |
| `time.year(ts?)`              | Return year component of timestamp (or current time)           |
| `time.month(ts?)`             | Return month (1-12) of timestamp (or current time)             |
| `time.day(ts?)`               | Return day of month (1-31) of timestamp (or current time)      |
| `time.hour(ts?)`              | Return hour (0-23) of timestamp (or current time)              |
| `time.minute(ts?)`            | Return minute (0-59) of timestamp (or current time)            |
| `time.second(ts?)`            | Return second (0-59) of timestamp (or current time)            |
| `time.dayOfWeek(ts?)`         | Return day name (e.g., `"Monday"`) of timestamp (or now)       |
| `time.add(ts, seconds)`       | Add seconds to a timestamp, return new timestamp               |
| `time.diff(ts1, ts2)`         | Return difference in seconds between two timestamps (ts1-ts2)  |

---

## `json` — JSON

| Function              | Description                                    |
| --------------------- | ---------------------------------------------- |
| `json.parse(text)`    | Parse a JSON string into Stash values          |
| `json.stringify(val)` | Serialize a Stash value to compact JSON string |
| `json.pretty(val)`    | Serialize a Stash value to pretty-printed JSON |
| `json.valid(text)`    | Return `true` if the string is valid JSON      |

### Type Mapping

| JSON type      | Stash type       |
| -------------- | ---------------- |
| string         | `string`         |
| number (int)   | `int` (long)     |
| number (float) | `float` (double) |
| true/false     | `bool`           |
| null           | `null`           |
| array          | `array`          |
| object         | `dict`           |

Values that can be serialized: `null`, `bool`, `int`, `float`, `string`, arrays, dictionaries, and struct instances.

---

## `ini` — INI Configuration

The `ini` namespace provides parsing and serialization of INI-format configuration files.

### `ini.parse(text)`

Parses an INI string into a nested dictionary. Sections become nested dictionaries; key-value pairs within a section become entries in that section's dictionary.

```stash
let text = "[database]\nhost = localhost\nport = 5432\n\n[logging]\nlevel = info";
let cfg = ini.parse(text);

io.println(cfg.database.host);   // "localhost"
io.println(cfg.database.port);   // 5432 (auto-coerced to int)
io.println(cfg.logging.level);   // "info"
```

#### Format Rules

- **Sections:** `[sectionName]` — creates a nested dictionary
- **Key-value pairs:** `key = value` — whitespace around `=` is trimmed
- **Comments:** Lines starting with `;` or `#` are ignored
- **Empty lines:** Skipped
- **Global keys:** Key-value pairs before any section go into the root dictionary
- **Quoted values:** `key = "value"` — surrounding double quotes are stripped

#### Value Coercion

Values are automatically coerced in this order:

| Raw value                           | Stash type       | Example                    |
| ----------------------------------- | ---------------- | -------------------------- |
| Integer string                      | `int` (long)     | `port = 143` → `143`       |
| Float string                        | `float` (double) | `ratio = 3.14` → `3.14`    |
| `true` / `false` (case-insensitive) | `bool`           | `enabled = true` → `true`  |
| Anything else                       | `string`         | `name = Alice` → `"Alice"` |

### `ini.stringify(dict)`

Serializes a dictionary back to INI format text.

```stash
let cfg = ini.parse("[server]\nhost = 10.0.0.1\nport = 8080");
cfg.server.port = 9090;
let output = ini.stringify(cfg);
// output:
// [server]
// host = 10.0.0.1
// port = 9090
```

**Serialization rules:**

- Top-level non-dict entries are written first (global keys)
- Top-level dict entries become `[section]` blocks
- Values with leading/trailing whitespace are quoted
- Sections are separated by blank lines
- Nested dicts deeper than 1 level are skipped (INI is inherently flat)
- Comments are not preserved through a parse → stringify round-trip

---

## `yaml` — YAML

The `yaml` namespace provides parsing and serialization of YAML (YAML Ain't Markup Language) documents. Supports YAML 1.2 including mappings, sequences, nested structures, multi-document streams, and scalar types.

| Function              | Description                               |
| --------------------- | ----------------------------------------- |
| `yaml.parse(text)`    | Parse a YAML string into a Stash value    |
| `yaml.stringify(val)` | Serialize a Stash value to a YAML string  |
| `yaml.valid(text)`    | Return `true` if the string is valid YAML |

### `yaml.parse(text)`

Parses a YAML string and returns the corresponding Stash value. Mappings become dictionaries, sequences become arrays, and scalar values are converted to their appropriate Stash types.

```stash
let text = "name: myapp\nversion: 2\nenabled: true";
let cfg = yaml.parse(text);

io.println(cfg.name);      // "myapp"
io.println(cfg.version);   // 2
io.println(cfg.enabled);   // true
```

#### Nested Structures

```stash
let yaml_text = "database:\n  host: localhost\n  port: 5432\n  replicas:\n    - db1\n    - db2";
let cfg = yaml.parse(yaml_text);

io.println(cfg.database.host);         // "localhost"
io.println(cfg.database.port);         // 5432
io.println(cfg.database.replicas[0]);  // "db1"
```

#### Type Mapping

| YAML type | Stash type       | Example                   |
| --------- | ---------------- | ------------------------- |
| Mapping   | `dict`           | `key: value` → dictionary |
| Sequence  | `array`          | `- item` → array          |
| Integer   | `int` (long)     | `port: 5432` → `5432`     |
| Float     | `float` (double) | `ratio: 3.14` → `3.14`    |
| Boolean   | `bool`           | `enabled: true` → `true`  |
| Null      | `null`           | `value: null` → `null`    |
| String    | `string`         | `name: Alice` → `"Alice"` |

### `yaml.stringify(value)`

Serializes a Stash value to a YAML-formatted string.

```stash
let cfg = {
    "server": {
        "host": "0.0.0.0",
        "port": 8080
    },
    "features": ["auth", "logging"]
};
let output = yaml.stringify(cfg);
io.println(output);
// server:
//   host: 0.0.0.0
//   port: 8080
// features:
//   - auth
//   - logging
```

Round-trip example:

```stash
let original = "name: myapp\nversion: 1";
let data = yaml.parse(original);
data.version = 2;
let updated = yaml.stringify(data);
io.println(updated);
```

### `yaml.valid(text)`

Returns `true` if the input string is valid YAML, `false` otherwise.

```stash
io.println(yaml.valid("key: value"));       // true
io.println(yaml.valid("key: [1, 2, 3]"));   // true
io.println(yaml.valid(": invalid: yaml:")); // false
```

---

## `toml` — TOML

The `toml` namespace provides parsing and serialization of TOML (Tom's Obvious Minimal Language) documents. Supports TOML 1.0 including tables, arrays, inline tables, and array of tables.

| Function               | Description                               |
| ---------------------- | ----------------------------------------- |
| `toml.parse(text)`     | Parse a TOML string into a dictionary     |
| `toml.stringify(dict)` | Serialize a dictionary to a TOML string   |
| `toml.valid(text)`     | Return `true` if the string is valid TOML |

### `toml.parse(text)`

Parses a TOML string and returns a dictionary. Tables become nested dictionaries, arrays become Stash arrays, and scalar values are converted to their appropriate types.

```stash
let text = "title = \"My App\"\n\n[database]\nhost = \"localhost\"\nport = 5432";
let cfg = toml.parse(text);

io.println(cfg.title);          // "My App"
io.println(cfg.database.host);  // "localhost"
io.println(cfg.database.port);  // 5432
```

#### Array of Tables

```stash
let text = "[[servers]]\nname = \"alpha\"\nip = \"10.0.0.1\"\n\n[[servers]]\nname = \"beta\"\nip = \"10.0.0.2\"";
let cfg = toml.parse(text);

io.println(cfg.servers[0].name);  // "alpha"
io.println(cfg.servers[1].ip);    // "10.0.0.2"
```

#### Type Mapping

| TOML type       | Stash type       | Example                                   |
| --------------- | ---------------- | ----------------------------------------- |
| Table           | `dict`           | `[section]` → nested dictionary           |
| Array           | `array`          | `arr = [1, 2]` → array                    |
| Array of Tables | `array` of dicts | `[[items]]` → array of dictionaries       |
| Integer         | `int` (long)     | `port = 5432` → `5432`                    |
| Float           | `float` (double) | `ratio = 3.14` → `3.14`                   |
| Boolean         | `bool`           | `enabled = true` → `true`                 |
| String          | `string`         | `name = "Alice"` → `"Alice"`              |
| Datetime        | `string`         | `dt = 2024-01-15T10:30:00Z` → `"2024..."` |

### `toml.stringify(dict)`

Serializes a dictionary to a TOML-formatted string. Only dictionaries can be serialized to TOML (TOML documents must be tables at the root level).

```stash
let cfg = {
    "title": "My App",
    "database": {
        "host": "localhost",
        "port": 5432
    }
};
let output = toml.stringify(cfg);
io.println(output);
// title = "My App"
//
// [database]
// host = "localhost"
// port = 5432
```

Round-trip example:

```stash
let text = "title = \"App\"\n\n[server]\nport = 8080";
let cfg = toml.parse(text);
cfg.server.port = 9090;
let updated = toml.stringify(cfg);
io.println(updated);
```

### `toml.valid(text)`

Returns `true` if the input string is valid TOML, `false` otherwise.

```stash
io.println(toml.valid("key = \"value\""));          // true
io.println(toml.valid("[section]\nk = 1"));          // true
io.println(toml.valid("[invalid"));                   // false
```

---

## `config` — Format-Agnostic Configuration

The `config` namespace provides a unified, format-agnostic API for reading and writing configuration files. It auto-detects the format from the file extension and delegates to the appropriate parser.

### Supported Formats

| Extension     | Format | Parser                                                               |
| ------------- | ------ | -------------------------------------------------------------------- |
| `.json`       | JSON   | Built-in JSON parser                                                 |
| `.ini`        | INI    | Built-in INI parser (see [`ini` namespace](#ini--ini-configuration)) |
| `.cfg`        | INI    | Same as `.ini`                                                       |
| `.conf`       | INI    | Same as `.ini`                                                       |
| `.properties` | INI    | Same as `.ini`                                                       |
| `.yaml`       | YAML   | Built-in YAML parser (see [`yaml` namespace](#yaml--yaml))           |
| `.yml`        | YAML   | Same as `.yaml`                                                      |
| `.toml`       | TOML   | Built-in TOML parser (see [`toml` namespace](#toml--toml))           |

### `config.read(path)` / `config.read(path, format)`

Reads and parses a configuration file, returning a (possibly nested) dictionary.

```stash
// Auto-detect format from extension
let cfg = config.read("/etc/myapp/config.ini");
io.println(cfg.database.host);
io.println(cfg.database.port);

// Explicit format (overrides extension)
let data = config.read("/etc/myapp/settings.txt", "ini");
```

Combine with `try` and `??` for safe loading:

```stash
let cfg = try config.read("/etc/myapp/config.ini") ?? dict.new();
```

### `config.write(path, data)` / `config.write(path, data, format)`

Serializes data and writes it to a file. Format is auto-detected from the file extension (or specified explicitly).

```stash
let cfg = config.read("app.ini");
cfg.database.port = 3306;
config.write("app.ini", cfg);

// Format conversion — read INI, write JSON
let legacy = config.read("old.ini");
config.write("new.json", legacy);
```

JSON output is pretty-printed with 2-space indentation.

### `config.parse(text, format)` / `config.stringify(data, format)`

String-level parsing and serialization without file I/O — useful when the configuration text comes from another source (environment variable, HTTP response, embedded string).

```stash
let iniText = fs.readFile("custom.ini");
let cfg = config.parse(iniText, "ini");

let jsonStr = config.stringify(cfg, "json");
io.println(jsonStr);
```

### Complete Example

```stash
#!/usr/bin/env stash

// Load a service configuration, modify it, write it back
let cfg = try config.read("/etc/myservice/config.ini");
if (cfg == null) {
    io.println("Error: " + lastError());
    process.exit(1);
}

// Access nested config values with dot notation
io.println("Current port: " + cfg.server.port);

// Modify values
cfg.server.port = 9090;
cfg.logging.level = "debug";

// Write back
config.write("/etc/myservice/config.ini", cfg);
io.println("Configuration updated.");
```

---

## `http` — HTTP Requests

| Function                   | Description                                           |
| -------------------------- | ----------------------------------------------------- |
| `http.get(url)`            | Send HTTP GET request and return response             |
| `http.post(url, body)`     | Send HTTP POST request with body and return response  |
| `http.put(url, body)`      | Send HTTP PUT request with body and return response   |
| `http.delete(url)`         | Send HTTP DELETE request and return response          |
| `http.request(options)`    | Send custom HTTP request with a dict of options       |
| `http.patch(url, body)`    | Send HTTP PATCH request with body and return response |
| `http.download(url, path)` | Download a file to disk (streaming, memory-efficient) |

### Response Object

All `http` functions return a response struct with:

| Field     | Type     | Description             |
| --------- | -------- | ----------------------- |
| `status`  | `int`    | HTTP status code        |
| `body`    | `string` | Response body as string |
| `headers` | `dict`   | Response headers        |

### `http.request(options)`

The `options` dict supports:

| Key       | Type     | Description                    |
| --------- | -------- | ------------------------------ |
| `url`     | `string` | Request URL (required)         |
| `method`  | `string` | HTTP method (default: `"GET"`) |
| `headers` | `dict`   | Request headers                |
| `body`    | `string` | Request body                   |

---

## `process` — Process Management

Stash provides built-in process management through the `process` namespace, enabling scripts to spawn background processes, track their lifecycle, communicate with them, and control their termination. This goes beyond the synchronous `$(...)` command execution to support long-running services, parallel workloads, and process orchestration.

### Philosophy

Synchronous command execution via `$(...)` is the right default — run a command, get the result. But scripting often requires launching a process that runs alongside the script: a development server, a file watcher, a background worker. The `process` namespace provides **explicit, tracked** background process management. Every spawned process is tracked by default and cleaned up on script exit unless explicitly detached.

### Quick Reference

| Function                         | Description                                                       |
| -------------------------------- | ----------------------------------------------------------------- |
| `process.exit(code)`             | Terminate the script with exit code                               |
| `process.spawn(cmd)`             | Launch a background process, returns a `Process` handle           |
| `process.wait(proc)`             | Block until a process exits, returns `CommandResult`              |
| `process.waitTimeout(proc, ms)`  | Wait with timeout; returns `CommandResult` or `null` if timed out |
| `process.kill(proc)`             | Send SIGTERM to a process                                         |
| `process.isAlive(proc)`          | Check if a process is still running (returns `bool`)              |
| `process.signal(proc, sig)`      | Send an arbitrary signal to a process                             |
| `process.pid(proc)`              | Get the OS process ID                                             |
| `process.detach(proc)`           | Detach a process so it survives script exit                       |
| `process.list()`                 | List all tracked (spawned) process handles                        |
| `process.read(proc)`             | Read available stdout from a running process (non-blocking)       |
| `process.write(proc, data)`      | Write to a running process's stdin                                |
| `process.onExit(proc, callback)` | Register a callback to run when a process exits                   |
| `process.daemonize(cmd)`         | Launch a command as a daemon (not tracked, survives script exit)  |
| `process.find(name)`             | Find system processes by name, returns array of `Process` handles |
| `process.exists(pid)`            | Check if a system process exists by PID (returns `bool`)          |
| `process.waitAll(procs)`         | Wait for all processes in an array to exit                        |
| `process.waitAny(procs)`         | Wait for the first of multiple processes to exit                  |
| `process.chdir(path)`            | Change the current working directory                              |
| `process.withDir(path, fn)`      | Run a function with a temporary working directory change          |

### `process.chdir(path)`

Changes the current working directory of the process. Accepts absolute or relative paths. Throws a runtime error if the directory does not exist.

```stash
// Save and restore working directory
let original = env.cwd();
process.chdir("/tmp");
io.println(env.cwd());     // "/tmp"
process.chdir(original);   // restore

// Change to a subdirectory for a build step
let cwd = env.cwd();
process.chdir("src/frontend");
let result = $(npm run build);
process.chdir(cwd);
```

### `process.withDir(path, fn)`

Runs a function with the working directory temporarily changed to the given path. The original directory is automatically restored when the function returns — even if it throws an error. Returns whatever the callback returns.

This is the recommended approach for short, self-contained directory changes. For cases where you need `return`, `break`, or `continue` to affect the enclosing function or loop, use `process.chdir()` instead.

```stash
// Run a build in a subdirectory — directory is restored automatically
process.withDir("src/frontend", () => {
    $(npm install);
    $(npm run build);
});

// Capture a return value from the block
let files = process.withDir("/var/log", () => {
    return fs.glob("*.log");
});

// Nesting works naturally
process.withDir("services/api", () => {
    $(docker compose build);
    process.withDir("migrations", () => {
        $(./run_migrations.sh);
    });
    // back to services/api
});
// back to original directory

// Expression-body lambda for one-liners
let config = process.withDir("config", () => fs.readFile("app.json"));
```

### The `Process` Handle

`Process` is a **built-in struct type** (like `CommandResult`) that represents a handle to a spawned process. It is returned by `process.spawn()` and accepted by all other process management functions.

#### Fields

| Field     | Type     | Description                          |
| --------- | -------- | ------------------------------------ |
| `pid`     | `int`    | OS process ID                        |
| `command` | `string` | The command string that was launched |

The `pid` and `command` fields are set at spawn time and do not change. To query live state (running/exited), use `process.isAlive()` — this is a function rather than a field because it queries the OS each time.

### Spawning Processes

```stash
let server = process.spawn("python3 -m http.server 8080");
io.println("Server PID: " + server.pid);      // e.g. 12345
io.println("Command: " + server.command);      // "python3 -m http.server 8080"
```

`process.spawn(cmd)` launches a process in the background and returns immediately with a `Process` handle. The process runs concurrently with the script. The command string is parsed into a program name and arguments, and the program is invoked directly — no system shell is involved.

The spawned process's stdout and stderr are captured in internal buffers (accessible via `process.read()`), and its stdin is available for writing via `process.write()`.

### Waiting for Processes

```stash
// Block until the process exits
let result = process.wait(server);
io.println("Exit code: " + result.exitCode);
io.println("Output: " + result.stdout);
```

`process.wait(proc)` blocks until the process exits and returns a `CommandResult` with `stdout`, `stderr`, and `exitCode` — identical to what `$(...)` returns for synchronous commands.

```stash
// Wait with a timeout (milliseconds)
let result = process.waitTimeout(server, 5000);
if (result == null) {
    io.println("Process did not exit within 5 seconds");
    process.kill(server);
}
```

`process.waitTimeout(proc, ms)` waits up to `ms` milliseconds. Returns a `CommandResult` if the process exited in time, or `null` if it is still running.

### Checking Process State

```stash
if (process.isAlive(server)) {
    io.println("Server is running");
} else {
    io.println("Server has exited");
}

let pid = process.pid(server);  // same as server.pid
```

`process.isAlive(proc)` returns `true` if the process is still running, `false` if it has exited.

`process.pid(proc)` returns the OS process ID as an integer. This is equivalent to accessing `proc.pid` directly but is provided for consistency with the functional style of the namespace.

### Killing and Signaling Processes

```stash
// Send SIGTERM (graceful shutdown)
process.kill(server);

// Send a specific signal
process.signal(server, process.SIGKILL);  // force kill
process.signal(server, process.SIGHUP);   // hangup
```

`process.kill(proc)` sends `SIGTERM` (signal 15) to the process. Returns `true` if the signal was sent, `false` if the process had already exited.

`process.signal(proc, sig)` sends an arbitrary signal. The signal is specified as an integer. Common signal constants are provided on the `process` namespace:

| Constant          | Value | Description             |
| ----------------- | ----- | ----------------------- |
| `process.SIGHUP`  | 1     | Hangup                  |
| `process.SIGINT`  | 2     | Interrupt (Ctrl+C)      |
| `process.SIGQUIT` | 3     | Quit                    |
| `process.SIGKILL` | 9     | Kill (cannot be caught) |
| `process.SIGTERM` | 15    | Terminate (graceful)    |
| `process.SIGUSR1` | 10    | User-defined signal 1   |
| `process.SIGUSR2` | 12    | User-defined signal 2   |

These are integer constants — `process.SIGTERM` is just `15`. Using `process.signal(proc, 15)` is equivalent.

**Cross-platform behavior:** On Unix, `process.signal()` sends signals via the POSIX `kill()` syscall. On Windows, common signals (SIGTERM, SIGKILL) are mapped to `Process.Kill()` with graceful/forceful options.

### Process I/O

```stash
let proc = process.spawn("bc -l");
process.write(proc, "2 + 3\n");
let answer = process.read(proc);  // "5\n"
process.write(proc, "scale=4; 22/7\n");
let pi = process.read(proc);      // "3.1428\n"
process.kill(proc);
```

`process.write(proc, data)` writes a string to the process's stdin. Returns `true` if the write succeeded, `false` if the process has exited or stdin is closed.

`process.read(proc)` reads currently available stdout from the process. Returns a string with the available data, or `null` if no data is available. This is **non-blocking** — it returns immediately with whatever is in the buffer.

### Detaching Processes

```stash
let daemon = process.spawn("my-daemon --config /etc/app.conf");
process.detach(daemon);
// daemon now survives script exit
// the Process handle becomes inert — further calls on it are no-ops
```

`process.detach(proc)` removes a process from the tracked process list. After detaching:

- The process will **not** be killed when the script exits.
- `process.isAlive()`, `process.kill()`, `process.signal()`, `process.wait()`, `process.read()`, and `process.write()` on the detached handle return `false`/`null` as appropriate without error.
- The `pid` and `command` fields remain accessible on the handle.

This is the mechanism for launching daemons or long-lived services that should outlive the script.

### Listing Tracked Processes

```stash
let procs = process.list();
for (let p in procs) {
    io.println(p.command + " (PID: " + p.pid + ") alive=" + process.isAlive(p));
}
```

`process.list()` returns an array of all currently tracked `Process` handles (spawned and not yet detached). This is useful for cleanup, monitoring, and debugging.

### Script Exit Cleanup

When a Stash script exits (normally or due to an error), all **tracked** processes receive `SIGTERM`. This prevents orphaned processes from accumulating. The cleanup sequence:

1. Send `SIGTERM` to all tracked processes that are still alive.
2. Wait up to 3 seconds for each process to exit gracefully.
3. Send `SIGKILL` to any process that is still alive after the grace period.

Processes that have been `detach()`-ed are excluded from cleanup.

`process.exit(code)` also triggers this cleanup before terminating the script.

### Complete Example

```stash
#!/usr/bin/env stash

// Launch a web server in the background
let server = process.spawn("python3 -m http.server 8080");
io.println("Started server (PID: " + server.pid + ")");

// Give it a moment to start
$(sleep 1);

// Health check
let health = $(curl -s -o /dev/null -w "%{http_code}" http://localhost:8080/);
if (health.stdout == "200") {
    io.println("Server is healthy");
} else {
    io.println("Server failed to start");
    process.exit(1);
}

// Run some tests against the server
let testResult = $(curl -s http://localhost:8080/);
io.println("Response length: " + len(testResult.stdout));

// Graceful shutdown
process.kill(server);
let finalResult = process.waitTimeout(server, 5000);
if (finalResult == null) {
    process.signal(server, process.SIGKILL);
    process.wait(server);
}

io.println("Server stopped");
```

### Daemonizing Processes

```stash
let daemon = process.daemonize("my-daemon --config /etc/app.conf");
io.println("Daemon PID: " + daemon.pid);
// daemon is NOT tracked — it survives script exit
// process.list() will not include it
```

`process.daemonize(cmd)` launches a command as a detached daemon process. Unlike `process.spawn()`, the returned `Process` handle is **not tracked** — the process will not be killed when the script exits, and it will not appear in `process.list()`. The handle retains `pid` and `command` fields for reference.

This is the preferred way to launch long-lived services or background workers that should outlive the script.

### Finding System Processes

```stash
let procs = process.find("nginx");
for (let p in procs) {
    io.println("Found nginx (PID: " + p.pid + ")");
}
```

`process.find(name)` searches for running system processes by name. Returns an array of `Process` handles with `pid` and `command` fields. Returns an empty array if no matching processes are found. The returned handles are **not tracked** — they represent external processes not spawned by the script.

### Checking Process Existence

```stash
if (process.exists(1234)) {
    io.println("Process 1234 is running");
} else {
    io.println("Process 1234 does not exist");
}
```

`process.exists(pid)` checks whether a system process with the given PID exists and is running. Returns `true` if the process exists, `false` otherwise. This works with any PID — not just processes spawned by the script.

### Waiting for Multiple Processes

```stash
let p1 = process.spawn("task1.sh");
let p2 = process.spawn("task2.sh");
let p3 = process.spawn("task3.sh");

// Wait for all to finish
let results = process.waitAll([p1, p2, p3]);
for (let r in results) {
    io.println("Exit code: " + r.exitCode);
}
```

`process.waitAll(procs)` blocks until every process in the array has exited. Returns an array of `CommandResult` objects in the same order as the input array, each containing `stdout`, `stderr`, and `exitCode`.

```stash
// Wait for the first to finish
let fastest = process.waitAny([p1, p2, p3]);
io.println("First result: " + fastest.stdout);
```

`process.waitAny(procs)` blocks until **any one** of the processes exits, then immediately returns that process's `CommandResult`. The remaining processes continue running. Requires a non-empty array.

### Exit Callbacks

```stash
let server = process.spawn("python3 -m http.server 8080");

process.onExit(server, (result) => {
    io.println("Server exited with code: " + result.exitCode);
    if (result.exitCode != 0) {
        io.println("Error: " + result.stderr);
    }
});

// ... continue doing other work ...

// Callbacks fire when process.wait() or process.waitAll() is called
let finalResult = process.wait(server);
```

`process.onExit(proc, callback)` registers a callback function to run when a process exits. The callback receives a `CommandResult` as its single argument. Multiple callbacks can be registered for the same process. Callbacks are fired synchronously on the main thread when the process result is collected via `process.wait()`, `process.waitTimeout()`, `process.waitAll()`, or `process.waitAny()`. Returns `null`.

If the process handle is detached via `process.detach()`, any registered callbacks are discarded.

---

## `tpl` — Templating

Stash provides a built-in Jinja2-style templating engine via the `tpl` namespace. Templates use `{{ }}` for output, `{% %}` for logic, and `{# #}` for comments. Expressions inside templates are evaluated by the Stash interpreter, giving templates access to the full expression language — arithmetic, ternary, null-coalescing, function calls, and namespace access.

For complete documentation — template syntax, all 19 built-in filters, conditionals, loops, includes, whitespace control, raw blocks, and architecture details — see [TPL — Templating Engine](TPL%20—%20Templating%20Engine.md).

### Quick Reference

| Function                     | Description                                                            |
| ---------------------------- | ---------------------------------------------------------------------- |
| `tpl.render(tmpl, data)`     | Render a template string (or compiled template) with a data dictionary |
| `tpl.renderFile(path, data)` | Render a template file; path supports `~` expansion                    |
| `tpl.compile(tmpl)`          | Pre-compile a template for repeated rendering                          |

### Template Syntax

| Delimiter       | Purpose                        | Example           |
| --------------- | ------------------------------ | ----------------- |
| `{{ expr }}`    | Output expression              | `{{ user.name }}` |
| `{% tag %}`     | Logic/control flow             | `{% if active %}` |
| `{# comment #}` | Comment (stripped from output) | `{# TODO #}`      |

### Built-in Filters

Filters transform values using pipe syntax: `{{ name | upper }}`. Filters can be chained: `{{ name | trim | upper | default("N/A") }}`.

| Filter              | Description                |
| ------------------- | -------------------------- |
| `upper`             | Uppercase string           |
| `lower`             | Lowercase string           |
| `trim`              | Strip whitespace           |
| `capitalize`        | Capitalize first letter    |
| `title`             | Title Case each word       |
| `length`            | Length of string or array  |
| `reverse`           | Reverse string or array    |
| `first`             | First element of array     |
| `last`              | Last element of array      |
| `sort`              | Sort array                 |
| `join(sep)`         | Join array to string       |
| `split(sep)`        | Split string into array    |
| `replace(old, new)` | Replace substring          |
| `default(val)`      | Fallback for null values   |
| `round`             | Round to nearest integer   |
| `abs`               | Absolute value             |
| `keys`              | Dictionary keys as array   |
| `values`            | Dictionary values as array |
| `json`              | JSON-encode value          |

### Conditionals

```
{% if user.isAdmin %}
  Welcome, admin!
{% elif user.isActive %}
  Welcome back, {{ user.name }}.
{% else %}
  Please log in.
{% endif %}
```

### Loops

```
{% for server in servers %}
  {{ server.host }}: {{ server.status }}
{% endfor %}
```

Loop metadata is available inside `{% for %}` blocks via the `loop` variable: `loop.index` (1-based), `loop.index0` (0-based), `loop.first`, `loop.last`, `loop.length`.

### Includes, Whitespace Control, and Raw Blocks

```
{% include "header.tpl" %}            {# include another template file #}
{%- for item in items -%}            {# whitespace-trimming markers #}
{% raw %}{{ not parsed }}{% endraw %} {# literal output, no processing #}
```

### Examples

```stash
// Simple render
let data = dict.new();
data["name"] = "Alice";
data["items"] = ["one", "two", "three"];

let result = tpl.render("""
Hello, {{ name }}!
Items: {{ items | join(", ") }}
""", data);

// File-based rendering
let output = tpl.renderFile("templates/report.tpl", data);

// Pre-compile for repeated use
let compiled = tpl.compile("{{ name | upper }}");
let r1 = tpl.render(compiled, data);
```

---

## `store` — In-Memory Store

A process-scoped in-memory key-value store. The store acts as a centralized data hub — any module or script running in the same interpreter shares the same store instance. Values persist for the lifetime of the process.

Keys must be strings. Values can be any Stash type (strings, numbers, booleans, arrays, dicts, structs, functions, null).

| Function              | Description                                                 |
| --------------------- | ----------------------------------------------------------- |
| `store.set(key, val)` | Set a key-value pair (overwrites if key exists)             |
| `store.get(key)`      | Get the value for a key, or `null` if not found             |
| `store.has(key)`      | Return `true` if the key exists                             |
| `store.remove(key)`   | Remove a key; returns `true` if found                       |
| `store.keys()`        | Return an array of all keys                                 |
| `store.values()`      | Return an array of all values                               |
| `store.size()`        | Return the number of entries                                |
| `store.all()`         | Return a dictionary copy of all entries                     |
| `store.scope(prefix)` | Return a dictionary of entries whose keys start with prefix |
| `store.clear()`       | Remove all entries                                          |

### Centralized Configuration

```stash
// config.stash — shared configuration module
store.set("app.name", "MyApp");
store.set("app.version", "1.0.0");
store.set("app.debug", false);
store.set("db.host", "localhost");
store.set("db.port", 5432);
```

```stash
// main.stash — reads config set by another module
import "config.stash";

io.println(store.get("app.name"));     // "MyApp"
io.println(store.get("app.version"));  // "1.0.0"
```

### Scoped Retrieval

Use `store.scope()` to retrieve groups of related entries by key prefix:

```stash
store.set("server.host", "10.0.0.1");
store.set("server.port", 8080);
store.set("server.name", "web-01");
store.set("db.host", "10.0.0.2");

let serverConfig = store.scope("server.");
// dict with: { "server.host": "10.0.0.1", "server.port": 8080, "server.name": "web-01" }

dict.forEach(serverConfig, (key, val) => {
    io.println(key + " = " + val);
});
```

### Shared State Across Modules

Because modules are cached per-interpreter, the store provides a natural way to share state without passing data through function arguments:

```stash
// counter.stash
fn increment(name) {
    let current = store.get(name) ?? 0;
    store.set(name, current + 1);
}

fn getCount(name) {
    return store.get(name) ?? 0;
}
```

```stash
// app.stash
import { increment, getCount } from "counter.stash";

increment("requests");
increment("requests");
increment("requests");
io.println(getCount("requests"));  // 3
```

---

## Argument Parsing

Stash provides the **`args.parse()`** function for declarative CLI argument parsing. Instead of manually parsing `args.list()`, scripts pass a **dict spec** describing expected flags, options, positional arguments, and subcommands, then call `args.parse()` to get a dict of parsed values. The interpreter handles parsing, validation, type coercion, and help generation automatically.

### Spec Format

The spec uses dict literals to declare expected arguments:

```stash
let parsed = args.parse({
    name: "deploy",
    version: "1.0.0",
    description: "A deployment tool",
    flags: {
        help:    { short: "h", description: "Show help" },
        verbose: { short: "v", description: "Enable verbose output" }
    },
    options: {
        port: { short: "p", type: "int", default: 8080, description: "Port to listen on" }
    },
    positionals: [
        { name: "target", type: "string", required: true, description: "Target host" }
    ],
    commands: {
        deploy: {
            description: "Deploy the application",
            flags: { force: { short: "f", description: "Force deployment" } },
            options: { timeout: { type: "int", default: 30, description: "Timeout in seconds" } }
        }
    }
});
```

#### Dict Spec Structure

| Top-Level Key | Type   | Description                                                        |
| ------------- | ------ | ------------------------------------------------------------------ |
| `name`        | string | Script name (used in help text)                                    |
| `version`     | string | Version string (triggers auto `--version`)                         |
| `description` | string | Script description                                                 |
| `flags`       | dict   | `{ name: { short, description } }` — boolean switches              |
| `options`     | dict   | `{ name: { short, type, default, description, required } }`        |
| `commands`    | dict   | `{ name: { description, flags, options, positionals } }`           |
| `positionals` | array  | `[{ name, type, default, description, required }]` — order matters |

**Flags** dict values accept: `short`, `description`

**Options** dict values accept: `short`, `type`, `default`, `description`, `required`

**Commands** dict values accept: `description`, plus nested `flags`/`options`/`positionals` using the same dict format (recursive)

**Positionals** array elements accept: `name` (required), `type`, `default`, `description`, `required`

### Flags

Flags are boolean switches that default to `false` and become `true` when present. Each flag is a key in the `flags` dict.

Usage: `--name` or `-short`

**Special flags:**

- A flag named `help` will automatically print formatted help text and exit when `--help` or its short form is passed.
- A flag named `version` will automatically print the `version` metadata value and exit when `--version` is passed (requires `version` to be set in the spec).

### Options

Options take a value and support type coercion. Each option is a key in the `options` dict.

Usage: `--name value`, `-s value`, or `--name=value`

**Type coercion:**

| Type     | Stash Type | Accepted Values                                    |
| -------- | ---------- | -------------------------------------------------- |
| `string` | string     | Any string (default if no `type` specified)        |
| `int`    | int (long) | Integer strings (e.g., `"42"`, `"-1"`)             |
| `float`  | float      | Decimal strings (e.g., `"3.14"`)                   |
| `bool`   | bool       | `"true"`, `"false"`, `"1"`, `"0"`, `"yes"`, `"no"` |

A runtime error is raised if the value cannot be parsed as the specified type.

### Positional Arguments

Positional arguments are captured in declaration order. Non-flag, non-option, non-command arguments fill positionals sequentially. Each positional is a dict in the `positionals` array.

Usage: `./script.stash myhost` — the first non-flag, non-option argument fills the first positional.

### Subcommands

Keys in the `commands` dict define named subcommands. Each command value is a dict containing the subcommand's own `flags`, `options`, and `positionals` (using the same format as the top level).

When a subcommand is matched, `args.command` is set to the command name as a string, and `args.<commandName>` contains the subcommand's parsed values:

```stash
if (args.command == "deploy") {
    io.println(args.deploy.force);    // bool
    io.println(args.deploy.timeout);  // int
}
```

### Accessing Parsed Values

All parsed values are accessible via dot notation on the variable returned by `args.parse()`:

```stash
let parsed = args.parse({
    flags:       { verbose: { short: "v", description: "" } },
    options:     { port: { type: "int", default: 8080, description: "" } },
    positionals: [{ name: "file", required: true, description: "" }]
});

io.println(parsed.verbose);    // false (or true if --verbose was passed)
io.println(parsed.port);       // 8080 (or user-provided value)
io.println(parsed.file);       // first positional argument value
io.println(parsed.command);    // name of matched subcommand, or null
io.println(parsed.deploy.force); // subcommand flag value
```

### Validation & Error Handling

`args.parse()` performs automatic validation:

- **Required options/positionals:** A runtime error is raised if a required argument is not provided.
- **Unknown arguments:** A runtime error is raised for unrecognized flags or options.
- **Type coercion failures:** A runtime error is raised if a value cannot be parsed as the declared type.
- **Missing option values:** A runtime error is raised if an option flag is provided without a corresponding value (e.g., `--port` at the end of the argument list).

### Auto-Generated Help

When a `help` flag is defined and triggered, the interpreter automatically generates formatted help text:

```
my-tool v1.0.0
A deployment tool

USAGE:
  my-tool [command] [options] <target>

COMMANDS:
  deploy    Deploy the application
  rollback  Rollback the deployment

ARGUMENTS:
  <target>  Target host (required)

OPTIONS:
  -v, --verbose        Enable verbose output
  -p, --port <int>     Port to listen on (default: 8080)
  -h, --help           Show help

COMMAND 'deploy':
  -f, --force          Force deployment
      --timeout <int>  Timeout in seconds (default: 30)
```

### Building CLI Arguments

The `args.build()` function is the **reverse of `args.parse()`** — it takes a spec and a values dict, producing an array of CLI argument strings. This enables a single spec format to work in both directions: parsing arguments your script receives AND building arguments for external tools.

```stash
let spec = {
    flags: {
        verbose: { short: "v", description: "Verbose output" }
    },
    options: {
        port: { short: "p", type: "int", description: "Port number" },
        env:  { short: "e", type: "map", description: "Environment variables" },
        tags: { type: "csv", description: "Comma-separated tags" }
    },
    positionals: [
        { name: "target", type: "string", description: "Target host" }
    ]
};

let tokens = args.build(spec, {
    verbose: true,
    port: 8080,
    env: { NODE_ENV: "production", DEBUG: "false" },
    tags: ["web", "api", "v2"],
    target: "deploy.example.com"
});
// tokens → ["-v", "-p", "8080", "-e", "NODE_ENV=production", "-e", "DEBUG=false", "--tags", "web,api,v2", "deploy.example.com"]
// Note: flag/option order follows spec declaration order; map entry order may vary
```

#### Return Value

`args.build()` returns an **array of strings** — individual CLI tokens, not a joined command string. Use `arr.join(tokens, " ")` to produce a command string for `$()`.

#### Option Types for Building

In addition to the standard types used by `args.parse()` (`string`, `int`, `float`, `bool`), `args.build()` supports compound types for output serialization:

| Type     | Value Type | Output Format                                            | Example                        |
| -------- | ---------- | -------------------------------------------------------- | ------------------------------ |
| `string` | string     | `--flag value`                                           | `["--name", "alice"]`          |
| `int`    | int        | `--flag value`                                           | `["--port", "8080"]`           |
| `float`  | float      | `--flag value`                                           | `["--ratio", "3.14"]`          |
| `bool`   | bool       | `--flag value`                                           | `["--enabled", "true"]`        |
| `list`   | array      | Repeated `--flag item` per element                       | `["-p", "8080", "-p", "9090"]` |
| `map`    | dict       | Repeated `--flag key=value` per entry                    | `["-e", "A=1", "-e", "B=2"]`   |
| `csv`    | array      | Single `--flag item1,item2,...` with comma-joined values | `["--tags", "web,api,v2"]`     |

#### Flag and Option Behavior

- **Flags** with value `true` are emitted; `false`, `null`, or missing values are skipped
- **Options** with `null` or missing values are skipped
- **Explicit flag string** via `flag` property overrides the default flag name (e.g., `{ flag: "-e", type: "map" }` emits `-e` instead of `--env`)
- **Short form** is used when `short` is specified (e.g., `{ short: "p" }` emits `-p` instead of `--port`)
- **Priority**: `flag` property > `short` property > `--{keyname}` default
- **Positionals** are emitted in spec declaration order, after flags and options
- **Commands** are emitted between top-level flags/options and subcommand args

#### Subcommand Support

When `values.command` is set and the spec has a matching `commands` entry, `args.build()` emits the command name followed by the subcommand's flags, options, and positionals:

```stash
let spec = {
    flags: { verbose: { short: "v" } },
    options: { config: { short: "c" } },
    commands: {
        start: {
            flags: { detach: { short: "d" } },
            options: { port: { short: "p", type: "int" } },
            positionals: [{ name: "service" }]
        }
    }
};

let tokens = args.build(spec, {
    verbose: true,
    config: "/etc/app.conf",
    command: "start",
    start: { detach: true, port: 3000, service: "web" }
});
// tokens → ["-v", "-c", "/etc/app.conf", "start", "-d", "-p", "3000", "web"]
// Note: flag/option order follows spec declaration order
```

#### Roundtrip Compatibility

A spec can be used with both `args.parse()` and `args.build()`:

```stash
// Parse incoming arguments
let parsed = args.parse(spec);

// Later, rebuild the argument tokens
let rebuilt = args.build(spec, parsed);

// Use with $() to invoke an external tool
let cmd = "mytool " + arr.join(rebuilt, " ");
$(cmd);
```

#### Error Handling

`args.build()` raises runtime errors for type mismatches:

- `"Option '--name' has type 'list' but value is not an array."`
- `"Option '--name' has type 'map' but value is not a dictionary."`
- `"Option '--name' has type 'csv' but value is not an array."`

---

## `log` — Logging

The `log` namespace provides structured logging with configurable levels, format strings, and file output. By default, log messages are written to standard error.

### Log Levels

Messages are filtered by the current log level. Setting a level suppresses all messages below it:

| Level   | Priority | Description                    |
| ------- | -------- | ------------------------------ |
| `debug` | 0        | Detailed debugging information |
| `info`  | 1        | General informational messages |
| `warn`  | 2        | Warning conditions             |
| `error` | 3        | Error conditions               |

The default level is `"info"`, meaning `debug` messages are suppressed.

### Functions

| Function              | Description                                                      |
| --------------------- | ---------------------------------------------------------------- |
| `log.debug(msg)`      | Log a debug-level message (suppressed unless level is `debug`)   |
| `log.info(msg)`       | Log an info-level message                                        |
| `log.warn(msg)`       | Log a warning-level message                                      |
| `log.error(msg)`      | Log an error-level message (always output)                       |
| `log.setLevel(level)` | Set minimum log level (`"debug"`, `"info"`, `"warn"`, `"error"`) |
| `log.setFormat(fmt)`  | Set log message format string                                    |
| `log.toFile(path)`    | Redirect log output to a file (pass `null` to revert to stderr)  |

### Format String

The default format is `"[{time}] [{level}] {msg}"`. Available placeholders:

| Placeholder | Description                             |
| ----------- | --------------------------------------- |
| `{time}`    | Current timestamp in ISO 8601 format    |
| `{level}`   | Uppercase log level (DEBUG, INFO, etc.) |
| `{msg}`     | The log message                         |

### Examples

```stash
// Basic logging
log.info("Application started");
log.warn("Disk space low");
log.error("Connection failed");

// Debug messages (suppressed by default)
log.setLevel("debug");
log.debug("Entering function parse()");

// Custom format
log.setFormat("{level}: {msg}");
log.info("Server ready");   // "INFO: Server ready"

// Log to file
log.toFile("/var/log/myapp.log");
log.info("This goes to the file");
log.toFile(null);  // revert to stderr
log.info("Back to stderr");
```

---

## Testing Infrastructure

For complete documentation on Stash's built-in testing primitives — `test()`, `describe()`, `captureOutput()`, the `assert` namespace, TAP output format, and the `ITestHarness` architecture — see [TAP — Testing Infrastructure](specs/TAP%20—%20Testing%20Infrastructure.md).

---

## `crypto` — Cryptography & Hashing

The `crypto` namespace provides cryptographic hash functions, HMAC signatures, UUID generation, and secure random byte generation. All hash functions return lowercase hexadecimal strings.

### Hash Functions

| Function              | Description                                   |
| --------------------- | --------------------------------------------- |
| `crypto.md5(data)`    | Compute MD5 hash of a string (hex string)     |
| `crypto.sha1(data)`   | Compute SHA-1 hash of a string (hex string)   |
| `crypto.sha256(data)` | Compute SHA-256 hash of a string (hex string) |
| `crypto.sha512(data)` | Compute SHA-512 hash of a string (hex string) |

```stash
let hash = crypto.sha256("hello");
io.println(hash);  // "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"

// MD5 for legacy compatibility
let md5 = crypto.md5("hello");
io.println(md5);   // "5d41402abc4b2a76b9719d911017c592"
```

### HMAC

| Function                       | Description                                        |
| ------------------------------ | -------------------------------------------------- |
| `crypto.hmac(algo, key, data)` | Compute HMAC with specified algorithm (hex string) |

The `algo` parameter accepts `"md5"`, `"sha1"`, `"sha256"`, or `"sha512"`.

```stash
let signature = crypto.hmac("sha256", "my-secret-key", "request-body");
io.println(signature);  // lowercase hex HMAC
```

### File Hashing

| Function                       | Description                                            |
| ------------------------------ | ------------------------------------------------------ |
| `crypto.hashFile(path, algo?)` | Hash a file's contents (default algorithm: `"sha256"`) |

```stash
let checksum = crypto.hashFile("deploy.tar.gz");
io.println("SHA-256: " + checksum);

// Explicit algorithm
let md5sum = crypto.hashFile("deploy.tar.gz", "md5");
io.println("MD5: " + md5sum);
```

### UUID & Random

| Function                | Description                                        |
| ----------------------- | -------------------------------------------------- |
| `crypto.uuid()`         | Generate a random UUID v4 string                   |
| `crypto.randomBytes(n)` | Generate `n` cryptographically secure random bytes |

`randomBytes` returns the bytes as a lowercase hexadecimal string (2 hex characters per byte).

```stash
let id = crypto.uuid();
io.println(id);   // "550e8400-e29b-41d4-a716-446655440000"

let token = crypto.randomBytes(32);
io.println(token);  // 64 hex characters of random data
```

---

## `encoding` — Encoding & Decoding

The `encoding` namespace provides Base64, URL, and hexadecimal encoding and decoding functions.

### Base64

| Function                   | Description               |
| -------------------------- | ------------------------- |
| `encoding.base64Encode(s)` | Encode a string to Base64 |
| `encoding.base64Decode(s)` | Decode a Base64 string    |

```stash
let encoded = encoding.base64Encode("Hello, World!");
io.println(encoded);   // "SGVsbG8sIFdvcmxkIQ=="

let decoded = encoding.base64Decode(encoded);
io.println(decoded);   // "Hello, World!"
```

### URL Encoding

| Function                | Description                                     |
| ----------------------- | ----------------------------------------------- |
| `encoding.urlEncode(s)` | URL-encode a string (RFC 3986 percent-encoding) |
| `encoding.urlDecode(s)` | Decode a URL-encoded string                     |

```stash
let encoded = encoding.urlEncode("hello world&key=value");
io.println(encoded);   // "hello%20world%26key%3Dvalue"

let decoded = encoding.urlDecode(encoded);
io.println(decoded);   // "hello world&key=value"
```

### Hexadecimal

| Function                | Description                                   |
| ----------------------- | --------------------------------------------- |
| `encoding.hexEncode(s)` | Encode a string's UTF-8 bytes as hexadecimal  |
| `encoding.hexDecode(s)` | Decode a hexadecimal string to a UTF-8 string |

```stash
let hex = encoding.hexEncode("hello");
io.println(hex);       // "68656c6c6f"

let text = encoding.hexDecode(hex);
io.println(text);      // "hello"
```

---

## `term` — Terminal Formatting

The `term` namespace provides ANSI terminal formatting, color output, and table rendering.

#### Color Constants

| Constant       | Value       |
| -------------- | ----------- |
| `term.BLACK`   | `"black"`   |
| `term.RED`     | `"red"`     |
| `term.GREEN`   | `"green"`   |
| `term.YELLOW`  | `"yellow"`  |
| `term.BLUE`    | `"blue"`    |
| `term.MAGENTA` | `"magenta"` |
| `term.CYAN`    | `"cyan"`    |
| `term.WHITE`   | `"white"`   |
| `term.GRAY`    | `"gray"`    |

### Text Formatting

| Function                  | Description                                                |
| ------------------------- | ---------------------------------------------------------- |
| `term.color(text, color)` | Wrap text in ANSI color using `term` color constants       |
| `term.bold(text)`         | Bold text                                                  |
| `term.dim(text)`          | Dim text                                                   |
| `term.underline(text)`    | Underlined text                                            |
| `term.style(text, opts)`  | Combined styles via dict `{ color: term.RED, bold: true }` |
| `term.strip(text)`        | Remove all ANSI escape codes from text                     |

### Terminal Info

| Function               | Description                                   |
| ---------------------- | --------------------------------------------- |
| `term.width()`         | Terminal width in columns (fallback: 80)      |
| `term.isInteractive()` | Whether stdin is a TTY (interactive terminal) |
| `term.clear()`         | Clear terminal screen                         |

### Table Rendering

| Function                     | Description                       |
| ---------------------------- | --------------------------------- |
| `term.table(rows, headers?)` | Format data as ASCII table string |

### Examples

```stash
// Colored output
io.println(term.color("ERROR: file not found", term.RED));
io.println(term.color("SUCCESS: deployed", term.GREEN));

// Bold and underline
io.println(term.bold("Important Notice"));
io.println(term.underline("https://example.com"));

// Combined styles
let styled = term.style("WARNING", { color: term.YELLOW, bold: true });
io.println(styled);

// Strip ANSI codes (useful for logging to file)
let colored = term.color("hello", term.RED);
let plain = term.strip(colored);   // "hello"

// Terminal info
let cols = term.width();
io.println("Terminal is " + conv.toStr(cols) + " columns wide");

if (term.isInteractive()) {
    io.println("Running in interactive mode");
}

// ASCII table
let data = [
    [1, "Alice", 95],
    [2, "Bob", 87],
    [3, "Charlie", 92]
];
io.println(term.table(data, ["ID", "Name", "Score"]));
// +----+---------+-------+
// | ID | Name    | Score |
// +----+---------+-------+
// | 1  | Alice   | 95    |
// | 2  | Bob     | 87    |
// | 3  | Charlie | 92    |
// +----+---------+-------+
```

---

## `sys` — System Information

The `sys` namespace provides functions for querying system-level information: CPU, memory, disk, network interfaces, and process metadata. These are read-only introspection functions useful for server monitoring, health checks, and capacity planning scripts.

| Function                        | Description                                                  |
| ------------------------------- | ------------------------------------------------------------ |
| `sys.cpuCount()`                | Number of logical CPU cores                                  |
| `sys.totalMemory()`             | Total physical RAM in bytes                                  |
| `sys.freeMemory()`              | Available free RAM in bytes                                  |
| `sys.uptime()`                  | System uptime in seconds                                     |
| `sys.loadAvg()`                 | CPU load averages as array `[1min, 5min, 15min]`             |
| `sys.diskUsage(path?)`          | Disk usage dict with `total`, `used`, `free` keys (in bytes) |
| `sys.pid()`                     | Current process ID                                           |
| `sys.tempDir()`                 | OS temporary directory path                                  |
| `sys.networkInterfaces()`       | Array of network interface dicts                             |
| `sys.which(name)`               | Find an executable in PATH; returns full path or `null`      |
| `sys.onSignal(signal, handler)` | Register a callback for a POSIX signal                       |
| `sys.offSignal(signal)`         | Remove a previously registered signal handler                |

### CPU & Memory

```stash
io.println(sys.cpuCount());      // e.g., 8
io.println(sys.totalMemory());   // e.g., 17179869184 (16 GB)
io.println(sys.freeMemory());    // e.g., 8589934592  (8 GB available)
```

### Uptime & Load

```stash
io.println(sys.uptime());  // e.g., 86432.5 (seconds since boot)

let load = sys.loadAvg();
io.println(load);  // e.g., [0.15, 0.10, 0.05]
```

> **Note:** `sys.loadAvg()` reads `/proc/loadavg` on Linux. On other platforms it returns `[0.0, 0.0, 0.0]`.

### Disk Usage

```stash
let disk = sys.diskUsage();          // root filesystem
io.println(disk["total"]);           // total bytes
io.println(disk["used"]);            // used bytes
io.println(disk["free"]);            // free bytes

let data = sys.diskUsage("/data");   // specific mount point
```

### Process & Temp Directory

```stash
io.println(sys.pid());       // e.g., 12345
io.println(sys.tempDir());   // e.g., "/tmp"
```

### Network Interfaces

```stash
let ifaces = sys.networkInterfaces();
for (let iface in ifaces) {
    io.println(iface["name"] + " (" + iface["status"] + ")");
    for (let addr in iface["addresses"]) {
        io.println("  " + addr);
    }
}
// Example output:
// lo (Up)
//   127.0.0.1
//   ::1
// eth0 (Up)
//   192.168.1.100
```

Each network interface dict contains:

| Field       | Type   | Description                                       |
| ----------- | ------ | ------------------------------------------------- |
| `name`      | string | Interface name (e.g., `"eth0"`, `"lo"`)           |
| `type`      | string | Interface type (e.g., `"Ethernet"`, `"Loopback"`) |
| `status`    | string | Operational status (e.g., `"Up"`, `"Down"`)       |
| `addresses` | array  | Array of IP address strings                       |

### Command Path Resolution

#### `sys.which(name)`

Searches the system `PATH` for an executable with the given name. Returns the full absolute path to the first match, or `null` if not found.

- On **Linux/macOS**: searches each `PATH` directory for a file with execute permission
- On **Windows**: additionally tries `PATHEXT` extensions (`.exe`, `.cmd`, `.bat`, `.com`, etc.)

```stash
let docker = sys.which("docker");
if (docker != null) {
    io.println("Docker found at: " + docker);
    $(docker compose up -d);
} else {
    io.println("Docker is not installed");
}

// Check multiple tools before starting
let tools = ["git", "node", "npm"];
for (let tool in tools) {
    if (sys.which(tool) == null) {
        throw "Required tool not found: " + tool;
    }
}
io.println("All tools available!");
```

### Signal Handling

Stash supports trapping POSIX signals for graceful shutdown, config reloading, and custom signal handling. Signal handlers are registered per-signal and run in an isolated execution context when the signal is delivered.

#### `sys.Signal` Enum

`sys.Signal` is a **built-in enum** listing the trappable POSIX signals.

| Member               | Signal    | Typical Use                        | Windows Support |
| -------------------- | --------- | ---------------------------------- | --------------- |
| `sys.Signal.SIGHUP`  | HUP (1)   | Config reload, terminal disconnect | ✅              |
| `sys.Signal.SIGINT`  | INT (2)   | Interrupt (Ctrl+C)                 | ✅              |
| `sys.Signal.SIGQUIT` | QUIT (3)  | Quit with core dump                | ✅              |
| `sys.Signal.SIGTERM` | TERM (15) | Graceful termination               | ✅              |
| `sys.Signal.SIGUSR1` | USR1 (10) | User-defined signal 1              | No-op           |
| `sys.Signal.SIGUSR2` | USR2 (12) | User-defined signal 2              | No-op           |

> **Note:** `SIGUSR1` and `SIGUSR2` are only supported on Linux and macOS. On Windows, registering handlers for these signals is a no-op — the handler is stored but never invoked.

#### `sys.onSignal(signal, handler)`

Registers a callback function to be invoked when the specified signal is received by the current process. If a handler was already registered for that signal, it is replaced.

The handler function takes no arguments and runs in an isolated forked context — it cannot modify variables in the main script scope.

```stash
// Graceful shutdown on SIGTERM
sys.onSignal(sys.Signal.SIGTERM, () => {
    log.info("Received SIGTERM — shutting down gracefully...");
    cleanup();
});

// Reload configuration on SIGHUP
sys.onSignal(sys.Signal.SIGHUP, () => {
    log.info("Received SIGHUP — reloading configuration...");
    config = config.read("/etc/myapp/config.toml");
});
```

#### `sys.offSignal(signal)`

Removes a previously registered signal handler, restoring the default OS behavior for that signal. If no handler was registered, this is a no-op.

```stash
sys.offSignal(sys.Signal.SIGTERM);  // Restore default SIGTERM behavior
```

#### Cross-Platform Behavior

| Platform | SIGHUP | SIGINT | SIGQUIT | SIGTERM | SIGUSR1 | SIGUSR2 |
| -------- | ------ | ------ | ------- | ------- | ------- | ------- |
| Linux    | ✅     | ✅     | ✅      | ✅      | ✅      | ✅      |
| macOS    | ✅     | ✅     | ✅      | ✅      | ✅      | ✅      |
| Windows  | ✅     | ✅     | ✅      | ✅      | No-op   | No-op   |

On Windows, all signals except SIGUSR1/SIGUSR2 are supported via .NET's `PosixSignalRegistration`. SIGINT corresponds to Ctrl+C, and SIGTERM maps to process termination requests. SIGUSR1 and SIGUSR2 have no Windows equivalent — handlers are silently stored but never triggered.

---

## `task` — Parallel Tasks

The `task` namespace provides lightweight parallelism for Stash scripts. Tasks run as .NET `Task<T>` instances on the thread pool — not OS threads. Each task receives a snapshot of the current environment at spawn time, isolating it from mutations in the caller. All task functions work with **`Future`** — the same type returned by `async fn` declarations — so tasks and async functions are fully interchangeable.

### Built-in Types

#### `task.Status` Enum

`task.Status` is a **built-in enum** available in the `task` namespace. It is used to check the current state of a task.

| Member                  | Description                            |
| ----------------------- | -------------------------------------- |
| `task.Status.Running`   | Task is currently executing            |
| `task.Status.Completed` | Task finished successfully             |
| `task.Status.Failed`    | Task threw an unhandled error          |
| `task.Status.Cancelled` | Task was cancelled via `task.cancel()` |

#### `Future` Type

All task functions work with **`Future`** — the same type returned by `async fn` declarations. A Future represents an asynchronous computation that may not have completed yet. Use `await` to block until it resolves.

| Check         | Result                   |
| ------------- | ------------------------ |
| `typeof(f)`   | `"Future"`               |
| `f is Future` | `true`                   |
| `str(f)`      | `<Future:Running>`, etc. |

### Functions

| Function                 | Description                                                               |
| ------------------------ | ------------------------------------------------------------------------- |
| `task.run(fn)`           | Spawn a parallel task; returns a `Future`                                 |
| `task.await(future)`     | Block until the Future resolves and return its result                     |
| `task.awaitAll(futures)` | Wait for all Futures to resolve; returns a list of results in input order |
| `task.awaitAny(futures)` | Wait for any Future to resolve; returns the result of the first resolved  |
| `task.status(future)`    | Return the Future's current status as a `task.Status` enum value          |
| `task.cancel(future)`    | Request cooperative cancellation of a running Future; returns `null`      |
| `task.all(futures)`      | Return a Future that resolves to an array of all results                  |
| `task.race(futures)`     | Return a Future that resolves to the first completed result               |
| `task.resolve(value)`    | Create an already-resolved Future wrapping the given value                |
| `task.delay(seconds)`    | Return a Future that resolves to `null` after the specified delay         |

### `task.run(fn)`

Spawns a parallel task that executes the given zero-argument function. Returns a `Future` that can be awaited with `await` or passed to `task.status()` or `task.cancel()`. The function's closure environment is snapshotted at spawn time — mutations to captured variables after `task.run()` are not visible to the task.

```stash
let future = task.run(() => {
    time.sleep(1);
    return 42;
});

// Use await keyword or task.await()
let result = await future;
io.println("Result: " + result);  // 42
```

### `task.await(future)`

Blocks until the Future resolves and returns its result. If the Future threw an error, the error is re-thrown. If the Future was cancelled, throws a `"Task was cancelled"` error.

```stash
let f = task.run(() => {
    return $(hostname).stdout;
});

let hostname = task.await(f);
io.println("Host: " + hostname);
```

### `task.awaitAll(futures)`

Takes a list of Futures and waits for all to resolve. Returns a list of results in the same order as the input futures.

> **Note:** `task.awaitAll` is **fault-tolerant** — failed or cancelled Futures produce `Error` values in the results array instead of throwing. Use `task.all` if you prefer fail-fast behavior.

```stash
let tasks = [
    task.run(() => $(curl -s https://api1.example.com/status).stdout),
    task.run(() => $(curl -s https://api2.example.com/status).stdout),
    task.run(() => $(curl -s https://api3.example.com/status).stdout)
];

let results = task.awaitAll(tasks);
for (let r in results) {
    io.println(r);
}
```

### `task.awaitAny(futures)`

Takes a list of Futures and waits for any one to resolve. Returns the result of the first resolved Future.

```stash
let tasks = [
    task.run(() => { time.sleep(3); return "slow"; }),
    task.run(() => { time.sleep(1); return "fast"; }),
    task.run(() => { time.sleep(2); return "medium"; })
];

let first = task.awaitAny(tasks);
io.println("First finished: " + first);  // "fast"
```

### `task.status(future)`

Returns the current status of a Future as a `task.Status` enum value.

| Value                   | Description                            |
| ----------------------- | -------------------------------------- |
| `task.Status.Running`   | Task is currently executing            |
| `task.Status.Completed` | Task finished successfully             |
| `task.Status.Failed`    | Task threw an unhandled error          |
| `task.Status.Cancelled` | Task was cancelled via `task.cancel()` |

```stash
let future = task.run(() => {
    time.sleep(5);
    return "done";
});

io.println(task.status(future));  // task.Status.Running
time.sleep(6);
io.println(task.status(future));  // task.Status.Completed

if (task.status(future) == task.Status.Completed) {
    io.println("Done!");
}
```

### `task.cancel(future)`

Requests cooperative cancellation of a running Future. Returns `null`. The Future may not cancel immediately — it cancels at the next statement boundary.

```stash
let t = task.run(() => {
    for (let i = 0; i < 1000; i++) {
        time.sleep(0.1);
    }
    return "done";
});

time.sleep(0.5);
task.cancel(t);
io.println(task.status(t));  // task.Status.Cancelled (soon after)
```

### `task.all(futures)`

Takes an array of Futures and returns a Future that resolves to an array of all results. Results are in the same order as the input array.

> **Note:** `task.all` is **fail-fast** — if any Future in the array fails, the combined Future also fails and the error propagates when awaited. Use `task.awaitAll` if you need fault-tolerant behavior where failed Futures become `Error` values in the results array.

```stash
async fn fetch(url) {
    return http.get(url);
}

let futures = [
    fetch("https://api1.example.com"),
    fetch("https://api2.example.com"),
    fetch("https://api3.example.com")
];

let results = await task.all(futures);
for (let r in results) {
    io.println(r);
}
```

### `task.race(futures)`

Takes an array of Futures and returns a Future that resolves to the result of whichever completes first.

```stash
async fn query(server, latency) {
    time.sleep(latency);
    return $"data from {server}";
}

let winner = await task.race([
    query("primary", 0.3),
    query("replica", 0.1),
    query("cache", 0.2)
]);
io.println(winner);  // "data from replica"
```

### `task.resolve(value)`

Creates an already-resolved Future wrapping the given value. Useful for creating uniform interfaces where a Future is expected but the value is already available.

```stash
let f = task.resolve(42);
io.println(typeof(f));  // "Future"
let result = await f;   // 42
```

### `task.delay(seconds)`

Returns a Future that resolves to `null` after the specified delay in seconds. Useful for timeouts, throttling, or timed operations.

```stash
io.println("Starting...");
await task.delay(1.5);
io.println("1.5 seconds later");
```

### `Future` Type

`Future` is a **built-in type** returned by async functions and `task.run()`, as well as by `task.all()`, `task.race()`, `task.resolve()`, and `task.delay()`. It represents an asynchronous computation that may not have completed yet.

| Property    | Description                           |
| ----------- | ------------------------------------- |
| `is Future` | Type check — `true` for Future values |
| `typeof(f)` | Returns `"Future"`                    |

Use `await` to block until a Future resolves and get its value. Errors thrown inside async functions propagate when the Future is awaited.

```stash
async fn compute() { return 42; }

let f = compute();       // Returns Future immediately
io.println(f is Future); // true
let result = await f;    // 42
```

## `net` — Networking

The `net` namespace provides networking utilities including subnet computation, DNS resolution, connectivity testing, and network interface discovery. Requires the **Network** capability.

### Subnet Information

| Function         | Signature | Returns      | Description                                                  |
| ---------------- | --------- | ------------ | ------------------------------------------------------------ |
| `net.subnetInfo` | `(ip)`    | `SubnetInfo` | Returns comprehensive subnet details for a CIDR IP           |
| `net.mask`       | `(ip)`    | `ip`         | Returns the subnet mask for a CIDR IP address                |
| `net.network`    | `(ip)`    | `ip`         | Returns the network address for a CIDR IP address            |
| `net.broadcast`  | `(ip)`    | `ip`         | Returns the broadcast address for a CIDR IP address          |
| `net.hostCount`  | `(ip)`    | `int`        | Returns the number of usable host addresses in a CIDR subnet |

All subnet functions require a CIDR IP address (with prefix length, e.g., `@192.168.1.0/24`). Passing an IP without a prefix throws a runtime error.

#### `SubnetInfo` Struct

| Field       | Type  | Description                        |
| ----------- | ----- | ---------------------------------- |
| `network`   | `ip`  | Network address (with CIDR prefix) |
| `broadcast` | `ip`  | Broadcast address                  |
| `mask`      | `ip`  | Subnet mask                        |
| `wildcard`  | `ip`  | Wildcard (inverse) mask            |
| `hostCount` | `int` | Number of usable host addresses    |
| `firstHost` | `ip`  | First usable host address          |
| `lastHost`  | `ip`  | Last usable host address           |

```stash
let info = net.subnetInfo(@192.168.1.100/24)
println(info.network)    // @192.168.1.0/24
println(info.broadcast)  // @192.168.1.255
println(info.mask)       // @255.255.255.0
println(info.wildcard)   // @0.0.0.255
println(info.hostCount)  // 254
println(info.firstHost)  // @192.168.1.1
println(info.lastHost)   // @192.168.1.254

// Convenience accessors
let mask = net.mask(@10.0.0.0/8)         // @255.0.0.0
let netAddr = net.network(@10.5.3.1/8)   // @10.0.0.0/8
let bcast = net.broadcast(@10.0.0.0/8)   // @10.255.255.255
let count = net.hostCount(@10.0.0.0/8)   // 16777214
```

Special prefix lengths:

- `/32` — Single host: `hostCount` = 1, `firstHost` and `lastHost` equal the address
- `/31` — Point-to-point link: `hostCount` = 2, both addresses usable
- `/0` — Entire address space

### DNS Resolution

| Function            | Signature    | Returns  | Description                                   |
| ------------------- | ------------ | -------- | --------------------------------------------- |
| `net.resolve`       | `(hostname)` | `ip`     | Resolves hostname to first IP address via DNS |
| `net.resolveAll`    | `(hostname)` | `array`  | Resolves hostname to all IP addresses via DNS |
| `net.reverseLookup` | `(ip)`       | `string` | Performs reverse DNS lookup for an IP address |

```stash
let addr = net.resolve("example.com")      // @93.184.216.34
let all = net.resolveAll("example.com")    // [@93.184.216.34, ...]
let name = net.reverseLookup(@8.8.8.8)    // "dns.google"
```

### Connectivity Testing

| Function         | Signature                | Returns      | Description                  |
| ---------------- | ------------------------ | ------------ | ---------------------------- |
| `net.ping`       | `(host)`                 | `PingResult` | Sends ICMP ping to host      |
| `net.isPortOpen` | `(host, port, ?timeout)` | `bool`       | Checks if a TCP port is open |

#### `PingResult` Struct

| Field     | Type    | Description                     |
| --------- | ------- | ------------------------------- |
| `alive`   | `bool`  | Whether the host responded      |
| `latency` | `float` | Round-trip time in milliseconds |
| `ttl`     | `int`   | Time-to-live from reply         |

```stash
let result = net.ping(@8.8.8.8)
if (result.alive) {
    println("Host up, latency: ${result.latency}ms, TTL: ${result.ttl}")
}

// Port checking — accepts IP or hostname string
let open = net.isPortOpen(@192.168.1.1, 22)
let webOpen = net.isPortOpen("example.com", 443, 5000)  // 5s timeout
```

> **Note**: On Linux, `net.ping` requires root privileges or the `CAP_NET_RAW` capability for raw ICMP sockets.

### Network Interfaces

| Function         | Signature | Returns         | Description                               |
| ---------------- | --------- | --------------- | ----------------------------------------- |
| `net.interfaces` | `()`      | `array`         | Returns info about all network interfaces |
| `net.interface`  | `(name)`  | `InterfaceInfo` | Returns info about a specific interface   |

#### `InterfaceInfo` Struct

| Field     | Type     | Description                                        |
| --------- | -------- | -------------------------------------------------- |
| `name`    | `string` | Interface name (e.g., "eth0", "wlan0")             |
| `ip`      | `ip?`    | Primary IPv4 address, or `null` if none            |
| `ipv6`    | `ip?`    | Primary IPv6 address, or `null` if none            |
| `mac`     | `string` | MAC address (e.g., "AA:BB:CC:DD:EE:FF")            |
| `gateway` | `ip?`    | Default gateway address, or `null` if none         |
| `subnet`  | `ip?`    | Subnet as CIDR network address, or `null` if none  |
| `status`  | `string` | Operational status ("Up", "Down")                  |
| `type`    | `string` | Interface type ("Ethernet", "Wireless80211", etc.) |
| `up`      | `bool`   | Whether the interface is operational               |

```stash
// List all interfaces
for (let iface in net.interfaces()) {
    if (iface.up) {
        println("${iface.name}: ${iface.ip} (${iface.type})")
    }
}

// Get specific interface
let eth0 = net.interface("eth0")
println("IP: ${eth0.ip}, Gateway: ${eth0.gateway}")
println("Subnet: ${eth0.subnet}")
```

## `ssh` — SSH Remote Execution

The `ssh` namespace provides functions for connecting to remote hosts via SSH and executing commands. Requires the **Network** capability.

| Function          | Signature          | Returns         | Description                            |
| ----------------- | ------------------ | --------------- | -------------------------------------- |
| `ssh.connect`     | `(options)`        | `SshConnection` | Connect to a remote host               |
| `ssh.exec`        | `(conn, command)`  | `CommandResult` | Execute a remote command               |
| `ssh.execAll`     | `(conn, commands)` | `array`         | Execute multiple commands sequentially |
| `ssh.shell`       | `(conn, commands)` | `string`        | Run commands in an interactive shell   |
| `ssh.close`       | `(conn)`           | `null`          | Close the connection                   |
| `ssh.isConnected` | `(conn)`           | `bool`          | Check if connection is active          |
| `ssh.tunnel`      | `(conn, options)`  | `SshTunnel`     | Create a local port forward            |
| `ssh.closeTunnel` | `(tunnel)`         | `null`          | Close a port forward                   |

### Connection Options

`ssh.connect` accepts a dict with the following keys:

| Key          | Type     | Required | Description                             |
| ------------ | -------- | -------- | --------------------------------------- |
| `host`       | `string` | Yes      | Remote hostname or IP address           |
| `port`       | `int`    | No       | SSH port (default: 22)                  |
| `username`   | `string` | Yes      | Login username                          |
| `password`   | `string` | No\*     | Password authentication                 |
| `privateKey` | `string` | No\*     | Path to private key file (supports `~`) |
| `passphrase` | `string` | No       | Passphrase for encrypted private keys   |

\* Must provide either `password` or `privateKey`.

### Return Types

**SshConnection** — `{ host: string, port: int, username: string }`

**SshTunnel** — `{ localPort: int, remoteHost: string, remotePort: int }`

**CommandResult** — `{ stdout: string, stderr: string, exitCode: int }` (reused from process namespace)

### Examples

```stash
// Connect with password
let conn = ssh.connect({
    "host": "192.168.1.100",
    "username": "admin",
    "password": "secret"
});

// Execute a command
let result = ssh.exec(conn, "uname -a");
println(result.stdout);

// Execute multiple commands
let results = ssh.execAll(conn, [
    "df -h",
    "free -m",
    "uptime"
]);

for r in results {
    println(r.stdout);
}

// Interactive shell (for sudo, etc.)
let output = ssh.shell(conn, [
    "sudo apt update",
    "sudo apt upgrade -y"
]);
println(output);

// SSH tunnel (port forward)
let tunnel = ssh.tunnel(conn, {
    "remoteHost": "127.0.0.1",
    "remotePort": 3306,
    "localPort": 3307
});
println("MySQL available on localhost:" + conv.toStr(tunnel.localPort));

// Clean up
ssh.closeTunnel(tunnel);
ssh.close(conn);
```

```stash
// Connect with private key
let conn = ssh.connect({
    "host": "prod-server.example.com",
    "username": "deploy",
    "privateKey": "~/.ssh/id_rsa"
});

let result = ssh.exec(conn, "docker ps");
println(result.stdout);

if result.exitCode != 0 {
    println("Error: " + result.stderr);
}

ssh.close(conn);
```

## `sftp` — SFTP File Transfer

The `sftp` namespace provides functions for transferring files and managing remote file systems over SFTP. Requires the **Network** capability.

| Function           | Signature                       | Returns          | Description                   |
| ------------------ | ------------------------------- | ---------------- | ----------------------------- |
| `sftp.connect`     | `(options)`                     | `SftpConnection` | Connect to a remote host      |
| `sftp.upload`      | `(conn, localPath, remotePath)` | `null`           | Upload a local file           |
| `sftp.download`    | `(conn, remotePath, localPath)` | `null`           | Download a remote file        |
| `sftp.readFile`    | `(conn, remotePath)`            | `string`         | Read remote file as string    |
| `sftp.writeFile`   | `(conn, remotePath, content)`   | `null`           | Write string to remote file   |
| `sftp.list`        | `(conn, remotePath)`            | `array`          | List directory entries        |
| `sftp.delete`      | `(conn, remotePath)`            | `null`           | Delete a remote file          |
| `sftp.mkdir`       | `(conn, remotePath)`            | `null`           | Create a remote directory     |
| `sftp.rmdir`       | `(conn, remotePath)`            | `null`           | Remove a remote directory     |
| `sftp.exists`      | `(conn, remotePath)`            | `bool`           | Check if path exists          |
| `sftp.stat`        | `(conn, remotePath)`            | `dict`           | Get file attributes           |
| `sftp.chmod`       | `(conn, remotePath, mode)`      | `null`           | Change file permissions       |
| `sftp.rename`      | `(conn, oldPath, newPath)`      | `null`           | Rename or move a file         |
| `sftp.close`       | `(conn)`                        | `null`           | Close the connection          |
| `sftp.isConnected` | `(conn)`                        | `bool`           | Check if connection is active |

### Connection Options

Same as `ssh.connect` — see [SSH Connection Options](#connection-options) above.

### Return Types

**SftpConnection** — `{ host: string, port: int, username: string }`

**sftp.list** returns an array of dicts:

```stash
[
    { "name": "file.txt", "size": 1024, "isDir": false, "modified": "2024-01-15T10:30:00Z" },
    { "name": "subdir",   "size": 4096, "isDir": true,  "modified": "2024-01-14T08:00:00Z" }
]
```

**sftp.stat** returns a dict:

```stash
{ "size": 1024, "isDir": false, "modified": "2024-01-15T10:30:00Z", "permissions": "644" }
```

### Examples

```stash
// Connect
let conn = sftp.connect({
    "host": "192.168.1.100",
    "username": "admin",
    "password": "secret"
});

// Upload and download files
sftp.upload(conn, "./local-config.yaml", "/etc/app/config.yaml");
sftp.download(conn, "/var/log/app.log", "./app.log");

// Read and write remote files
let config = sftp.readFile(conn, "/etc/app/config.yaml");
println(config);

sftp.writeFile(conn, "/tmp/hello.txt", "Hello from Stash!");

// List directory contents
let files = sftp.list(conn, "/var/log");
for f in files {
    let kind = f.isDir ? "DIR " : "FILE";
    println(kind + " " + f.name + " (" + conv.toStr(f.size) + " bytes)");
}

// File management
sftp.mkdir(conn, "/tmp/deploy");
sftp.rename(conn, "/tmp/old-name.txt", "/tmp/new-name.txt");
sftp.chmod(conn, "/tmp/script.sh", 755);

if sftp.exists(conn, "/tmp/deploy") {
    let info = sftp.stat(conn, "/tmp/deploy");
    println("Directory size: " + conv.toStr(info.size));
}

sftp.delete(conn, "/tmp/hello.txt");
sftp.rmdir(conn, "/tmp/deploy");

sftp.close(conn);
```

```stash
// Deploy workflow
let conn = sftp.connect({
    "host": "prod.example.com",
    "username": "deploy",
    "privateKey": "~/.ssh/deploy_key"
});

// Upload application files
sftp.mkdir(conn, "/opt/app/releases/v2.0");
sftp.upload(conn, "./dist/app.tar.gz", "/opt/app/releases/v2.0/app.tar.gz");
sftp.writeFile(conn, "/opt/app/releases/v2.0/version.txt", "2.0.0");

// Set permissions
sftp.chmod(conn, "/opt/app/releases/v2.0/app.tar.gz", 644);

sftp.close(conn);
```

## Threading Model

Stash's parallelism is based on **environment snapshotting** — each parallel task or operation receives a deep copy of the current scope chain at spawn time. This provides strong isolation without locks.

### What's Isolated (Safe)

Each parallel task or `arr.parMap/parFilter/parForEach` callback gets:

- **Local variables:** Deep-copied via `Environment.Snapshot()`. Mutations inside the task do not affect the caller, and vice versa.
- **Closure captures:** Variables captured by closures are snapshotted — the task sees their values at spawn time.
- **Execution state:** Each task has its own `ExecutionContext` (return values, loop control flow, call stack depth).

```stash
let count = 0;
let tasks = arr.map([1, 2, 3], (x) => {
    return task.run(() => {
        count = count + x;  // modifies a COPY — original `count` is unchanged
        return count;
    });
});
let results = task.awaitAll(tasks);
// count is still 0 — each task modified its own copy
```

### What's Shared (Read-Only Safe)

These are shared across tasks but are effectively immutable:

- **Built-in namespaces:** All 26 namespaces (`io`, `arr`, `str`, etc.) are frozen after registration. Safe to call from any task.
- **Global scope reference:** The global (outermost) environment is shared by reference for access to global functions and constants. This is safe for reads.
- **Struct and enum definitions:** Type definitions are immutable once created.

### What's Unsafe (Avoid)

- **Mutating global variables** from parallel tasks is a data race. Each task snapshots globals at spawn time, but writes go to the snapshot — they will be silently lost.
- **Shared mutable objects:** If a dictionary or struct instance is reachable from the global scope, multiple tasks may read stale copies. Do not rely on cross-task mutation.

```stash
// UNSAFE — do not do this
let shared = { value: 0 };
arr.parForEach([1, 2, 3, 4, 5], (x) => {
    shared.value = shared.value + x;  // race condition — each task has its own copy
});
// shared.value is still 0, NOT 15
```

### Concurrency Control

Use the optional `maxConcurrency` parameter on `arr.parMap`, `arr.parFilter`, and `arr.parForEach` to limit the number of parallel operations:

```stash
// Process at most 4 items at a time
let results = arr.parMap(urls, (url) => {
    return $(curl -s ${url}).stdout;
}, 4);
```

When omitted, the runtime uses all available processor cores. Set `maxConcurrency` to:

- **1** for sequential execution (useful for debugging)
- **A small number** when calling rate-limited APIs or managing resource-intensive operations
- **Omit** for CPU-bound work that benefits from full parallelism

### Summary Table

| Aspect              | Behavior                      | Safe? |
| ------------------- | ----------------------------- | ----- |
| Local variables     | Deep-copied per task          | Yes   |
| Closure captures    | Snapshotted at spawn          | Yes   |
| Built-in namespaces | Shared, immutable             | Yes   |
| Global functions    | Shared via global scope       | Yes   |
| Global variables    | Snapshotted — writes are lost | No    |
| Mutable objects     | Each task gets its own copy   | No\*  |

\* Mutations are safe within each task's copy, but changes are not visible to other tasks or the caller.

---

_This is a living document. Update as the standard library expands._
