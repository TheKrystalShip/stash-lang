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
14. [`config` — Format-Agnostic Configuration](#config--format-agnostic-configuration)
15. [`http` — HTTP Requests](#http--http-requests)
16. [`process` — Process Management](#process--process-management)
17. [`tpl` — Templating](#tpl--templating)
18. [`store` — In-Memory Store](#store--in-memory-store)
19. [Argument Parsing](#argument-parsing)

---

## Overview

Stash organizes built-in functions into **namespaces** accessed via dot notation (e.g., `fs.readFile(path)`). A small set of fundamental functions remain global (`typeof`, `len`, `lastError`); everything else lives in a namespace.

Namespaces are first-class values — `typeof(fs)` returns `"namespace"`. Assignment to namespace members is not permitted.

### Global Functions

| Function       | Description                                                              |
| -------------- | ------------------------------------------------------------------------ |
| `typeof(val)`  | Return the type of a value as string                                     |
| `len(val)`     | Length of a string or array                                              |
| `lastError()`  | Last error message (string) or null                                      |

---

## `io` — Standard I/O

| Function               | Description                                           |
| ---------------------- | ----------------------------------------------------- |
| `io.println(val)`      | Print value followed by newline                       |
| `io.print(val)`        | Print value without newline                           |
| `io.readLine(prompt?)` | Read a line from standard input, with optional prompt |

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

| Function                       | Description                                    |
| ------------------------------ | ---------------------------------------------- |
| `fs.readFile(path)`            | Read file contents as string                   |
| `fs.writeFile(path, content)`  | Write string to file (creates or overwrites)   |
| `fs.appendFile(path, content)` | Append string to file                          |
| `fs.readLines(path)`           | Read file as array of lines                    |
| `fs.exists(path)`              | Check if a file exists (returns boolean)       |
| `fs.dirExists(path)`           | Check if a directory exists (returns boolean)  |
| `fs.pathExists(path)`          | Check if a file or directory exists            |
| `fs.isFile(path)`              | Check if path is a file (returns boolean)      |
| `fs.isDir(path)`               | Check if path is a directory (returns boolean) |
| `fs.isSymlink(path)`           | Check if path is a symbolic link               |
| `fs.createDir(path)`           | Create a directory (including parents)         |
| `fs.delete(path)`              | Delete a file or directory (recursive)         |
| `fs.copy(src, dst)`            | Copy a file (overwrites destination)           |
| `fs.move(src, dst)`            | Move/rename a file (overwrites destination)    |
| `fs.size(path)`                | Get file size in bytes                         |
| `fs.listDir(path)`             | List entries in a directory (returns array)    |
| `fs.glob(pattern)`             | Find files matching a glob pattern             |
| `fs.walk(path)`                | Recursively list all files under a directory   |
| `fs.tempFile()`                | Create a temporary file, returns its path      |
| `fs.tempDir()`                 | Create a temporary directory, returns its path |
| `fs.modifiedAt(path)`          | Get last modified time as Unix timestamp       |

---

## `path` — Path Manipulation

| Function          | Description                        |
| ----------------- | ---------------------------------- |
| `path.abs(p)`     | Get absolute path                  |
| `path.dir(p)`     | Get directory portion of path      |
| `path.base(p)`    | Get filename with extension        |
| `path.name(p)`    | Get filename without extension     |
| `path.ext(p)`     | Get file extension (including `.`) |
| `path.join(a, b)` | Join two path segments             |

---

## `str` — String Operations

All `str` functions take the target string as the first argument. Strings are immutable — functions return new strings rather than modifying in place.

### Case & Whitespace

| Function           | Description                            |
| ------------------ | -------------------------------------- |
| `str.upper(s)`     | Convert to uppercase                   |
| `str.lower(s)`     | Convert to lowercase                   |
| `str.trim(s)`      | Remove leading and trailing whitespace |
| `str.trimStart(s)` | Remove leading whitespace              |
| `str.trimEnd(s)`   | Remove trailing whitespace             |

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

| Function                        | Description                                                                          |
| ------------------------------- | ------------------------------------------------------------------------------------ |
| `str.substring(s, start, end?)` | Extract substring from `start` to `end` (exclusive); `end` defaults to string length |
| `str.replace(s, old, new)`      | Replace first occurrence of `old` with `new`                                         |
| `str.replaceAll(s, old, new)`   | Replace all occurrences of `old` with `new`                                          |
| `str.split(s, delimiter)`       | Split string into array by `delimiter`                                               |
| `str.repeat(s, count)`          | Repeat string `count` times                                                          |
| `str.reverse(s)`                | Reverse the string                                                                   |
| `str.chars(s)`                  | Convert to array of single-character strings                                         |
| `str.padStart(s, len, fill?)`   | Pad start to `len` characters with `fill` (default `" "`)                            |
| `str.padEnd(s, len, fill?)`     | Pad end to `len` characters with `fill` (default `" "`)                              |
| `str.format(template, ...args)` | Replace `{0}`, `{1}`, etc. placeholders with arguments                               |

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

| Function                         | Description                                                   |
| -------------------------------- | ------------------------------------------------------------- |
| `arr.map(array, fn)`             | Return new array with `fn(element)` applied to each element   |
| `arr.filter(array, fn)`          | Return new array of elements where `fn(element)` is truthy    |
| `arr.forEach(array, fn)`         | Call `fn(element)` for each element                           |
| `arr.find(array, fn)`            | Return first element where `fn(element)` is truthy, or `null` |
| `arr.findIndex(array, fn)`       | Return index of first truthy `fn(element)`, or `-1`           |
| `arr.reduce(array, fn, initial)` | Fold array: calls `fn(accumulator, element)` for each element |
| `arr.any(array, fn)`             | Return `true` if any element satisfies `fn`                   |
| `arr.every(array, fn)`           | Return `true` if all elements satisfy `fn`                    |
| `arr.count(array, fn)`           | Return count of elements where `fn(element)` is truthy        |

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

// Predicates
let hasEven = arr.any(nums, (x) => x % 2 == 0);    // true
let allPos = arr.every(nums, (x) => x > 0);         // true
let evens = arr.count(nums, (x) => x % 2 == 0);     // 1
let idx = arr.findIndex(nums, (x) => x > 3);        // 2

// Deduplication and flattening
let unique = arr.unique([1, 2, 2, 3, 1]);          // [1, 2, 3]
let flat = arr.flat([[1, 2], [3, 4]]);             // [1, 2, 3, 4]
let expanded = arr.flatMap([1, 2, 3], (x) => [x, x * 10]); // [1, 10, 2, 20, 3, 30]
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
```

---

## `math` — Math Functions

### Core

| Function                      | Description                                |
| ----------------------------- | ------------------------------------------ |
| `math.abs(value)`             | Return the absolute value of a number      |
| `math.ceil(value)`            | Round a number up to the nearest integer   |
| `math.floor(value)`           | Round a number down to the nearest integer |
| `math.round(value)`           | Round a number to the nearest integer      |
| `math.sign(value)`            | Return the sign: `-1`, `0`, or `1`         |
| `math.min(a, b)`              | Return the smaller of two numbers          |
| `math.max(a, b)`              | Return the larger of two numbers           |
| `math.clamp(value, min, max)` | Constrain a number within a min/max range  |

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

---

## `json` — JSON

| Function              | Description                                    |
| --------------------- | ---------------------------------------------- |
| `json.parse(text)`    | Parse a JSON string into Stash values          |
| `json.stringify(val)` | Serialize a Stash value to compact JSON string |
| `json.pretty(val)`    | Serialize a Stash value to pretty-printed JSON |

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

### Future Extensions

- **YAML support** (`.yaml`, `.yml`)
- **TOML support** (`.toml`)

---

## `http` — HTTP Requests

| Function                | Description                                          |
| ----------------------- | ---------------------------------------------------- |
| `http.get(url)`         | Send HTTP GET request and return response            |
| `http.post(url, body)`  | Send HTTP POST request with body and return response |
| `http.put(url, body)`   | Send HTTP PUT request with body and return response  |
| `http.delete(url)`      | Send HTTP DELETE request and return response         |
| `http.request(options)` | Send custom HTTP request with a dict of options      |

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

| Function                        | Description                                                       |
| ------------------------------- | ----------------------------------------------------------------- |
| `process.exit(code)`            | Terminate the script with exit code                               |
| `process.spawn(cmd)`            | Launch a background process, returns a `Process` handle           |
| `process.wait(proc)`            | Block until a process exits, returns `CommandResult`              |
| `process.waitTimeout(proc, ms)` | Wait with timeout; returns `CommandResult` or `null` if timed out |
| `process.kill(proc)`            | Send SIGTERM to a process                                         |
| `process.isAlive(proc)`         | Check if a process is still running (returns `bool`)              |
| `process.signal(proc, sig)`     | Send an arbitrary signal to a process                             |
| `process.pid(proc)`             | Get the OS process ID                                             |
| `process.detach(proc)`          | Detach a process so it survives script exit                       |
| `process.list()`                | List all tracked (spawned) process handles                        |
| `process.read(proc)`            | Read available stdout from a running process (non-blocking)       |
| `process.write(proc, data)`     | Write to a running process's stdin                                |

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

### Future Extensions

- **`process.onExit(proc, callback)`** — Register a lambda callback for when a process exits.
- **`process.daemonize(cmd)`** — Launch as a proper daemon (double-fork, detach from terminal).
- **`process.find(name)`** — Find system processes by name.
- **`process.exists(pid)`** — Check if an arbitrary system process exists by PID.
- **`process.waitAll(procs)`** — Wait for multiple processes to all exit.
- **`process.waitAny(procs)`** — Wait for the first of multiple processes to exit.

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
| `flags`       | dict   | `{ name: { short, description } }` — boolean switches             |
| `options`     | dict   | `{ name: { short, type, default, description, required } }`       |
| `commands`    | dict   | `{ name: { description, flags, options, positionals } }`          |
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

---

## Testing Infrastructure

For complete documentation on Stash's built-in testing primitives — `test()`, `describe()`, `captureOutput()`, the `assert` namespace, TAP output format, and the `ITestHarness` architecture — see [TAP — Testing Infrastructure](specs/TAP%20—%20Testing%20Infrastructure.md).

---

_This is a living document. Update as the standard library expands._
