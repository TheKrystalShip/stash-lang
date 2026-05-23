# Stdlib QoL — Missing Optional Arguments Audit

**Status:** Backlog — Design
**Created:** 2025-07-15
**Scope:** All 24+ standard library namespaces
**Motivation:** `math.round(n)` lacks a precision argument that every developer expects. An audit of the entire stdlib revealed this is a systemic pattern — ~85% of functions accept no optional parameters at all.

---

## 1. Executive Summary

Stash's standard library is functionally complete — the namespaces cover the right surface area for a sysadmin scripting language. But the functions themselves are **rigid**: most accept only required positional arguments with no optional parameters for common variations.

This creates friction. Users who expect `math.round(3.14159, 2)` to return `3.14` (as in Python, Ruby, Lua, Go) hit a wall. Users who want `str.split("a,b,c", ",", 2)` to limit splits must write manual workarounds. Users who need `dict.get(d, "key", "default")` must add null checks.

**Every language in Stash's competitive set (Python, Ruby, PowerShell, JavaScript) provides these optional arguments.** Their absence in Stash makes the stdlib feel like a prototype rather than a polished tool.

### Scope of the Problem

| Namespace | Total Functions | Functions with Optional Args | % with Optional Args |
| --------- | --------------- | ---------------------------- | -------------------- |
| math      | 28 + 2 consts   | 0                            | 0%                   |
| str       | 40              | 6                            | 15%                  |
| arr       | 40              | 3                            | 8%                   |
| dict      | 21              | 0                            | 0%                   |
| time      | 17              | 7                            | 41%                  |
| json      | 4               | 0                            | 0%                   |
| conv      | 13              | 0                            | 0%                   |
| fs        | 33              | 1                            | 3%                   |
| path      | 10              | 0                            | 0%                   |
| crypto    | 8               | 1                            | 13%                  |
| encoding  | 6               | 0                            | 0%                   |
| term      | ~13             | 1                            | 8%                   |
| http      | 7               | 0                            | 0%                   |
| io        | 6               | 2                            | 33%                  |
| sys       | 12              | 1                            | 8%                   |
| env       | 14              | 1                            | 7%                   |
| global    | 8               | 1                            | 13%                  |

---

## 2. Proposed Changes by Namespace

### Legend

- **Priority** — P0 (must-have, users will hit this daily), P1 (high value, common use case), P2 (nice-to-have, less frequent)
- **Prior art** — Which major languages provide this parameter
- **Breaking** — Whether this change could break existing code (adding optional params never breaks existing calls)

---

### 2.1 `math` — Numeric Operations

| Function    | Current Signature     | Proposed Signature      | Priority | Prior Art                                                                                                 |
| ----------- | --------------------- | ----------------------- | -------- | --------------------------------------------------------------------------------------------------------- |
| `round`     | `round(n)`            | `round(n, precision?)`  | **P0**   | Python `round(n, ndigits=None)`, Ruby `Float#round(ndigits)`, Go `math.Round` (no), Lua `math.round` (no) |
| `min`       | `min(a, b)`           | `min(a, b, ...args)`    | **P1**   | Python `min(*args)`, JS `Math.min(...args)`, Ruby `[].min`                                                |
| `max`       | `max(a, b)`           | `max(a, b, ...args)`    | **P1**   | Python `max(*args)`, JS `Math.max(...args)`, Ruby `[].max`                                                |
| `log`       | `log(n)`              | `log(n, base?)`         | **P1**   | Python `math.log(x, base=e)`                                                                              |
| `randomInt` | `randomInt(min, max)` | `randomInt(min?, max?)` | **P2**   | Python `random.randint(a, b)`, Ruby `rand(max)`                                                           |

**`math.round(n, precision?)` — Detailed Design:**

```stash
math.round(3.14159)       // → 3       (existing behavior, unchanged)
math.round(3.14159, 2)    // → 3.14    (NEW: round to 2 decimal places)
math.round(3.14159, 0)    // → 3       (explicit zero precision)
math.round(1234.5, -2)    // → 1200    (negative precision rounds to tens/hundreds)
```

Implementation: When `precision` is omitted or 0, use existing `Math.Round(n, MidpointRounding.AwayFromZero)`. When provided, use `Math.Round(n, precision, MidpointRounding.AwayFromZero)`. For negative precision: `Math.Round(n / Math.Pow(10, -precision)) * Math.Pow(10, -precision)`.

**`math.min` / `math.max` — variadic:**

```stash
math.min(3, 1)            // → 1       (existing, unchanged)
math.min(3, 1, 4, 1, 5)   // → 1       (NEW: variadic)
```

Implementation: Change from 2 required params to `isVariadic: true` with minimum 2 args. Loop through args taking min/max.

**`math.log(n, base?)` — optional base:**

```stash
math.log(math.E)          // → 1.0     (existing: natural log)
math.log(100, 10)         // → 2.0     (NEW: log base 10)
math.log(8, 2)            // → 3.0     (NEW: log base 2)
```

Implementation: When base omitted, use `Math.Log(n)`. When provided, use `Math.Log(n, base)`.

---

### 2.2 `str` — String Operations

| Function      | Current Signature       | Proposed Signature                   | Priority | Prior Art                                                                                                           |
| ------------- | ----------------------- | ------------------------------------ | -------- | ------------------------------------------------------------------------------------------------------------------- |
| `split`       | `split(s, delim)`       | `split(s, delim, limit?)`            | **P0**   | Python `str.split(sep, maxsplit)`, JS `String.split(sep, limit)`, Ruby `String#split(pat, limit)`                   |
| `replace`     | `replace(s, old, new)`  | `replace(s, old, new, count?)`       | **P1**   | Python `str.replace(old, new, count)`, Ruby has `sub` (first) vs `gsub` (all)                                       |
| `contains`    | `contains(s, sub)`      | `contains(s, sub, ignoreCase?)`      | **P1**   | C# `String.Contains(value, StringComparison)`, PowerShell `-contains` is case-insensitive by default                |
| `startsWith`  | `startsWith(s, prefix)` | `startsWith(s, prefix, ignoreCase?)` | **P1**   | C# `String.StartsWith(value, StringComparison)`                                                                     |
| `endsWith`    | `endsWith(s, suffix)`   | `endsWith(s, suffix, ignoreCase?)`   | **P1**   | C# `String.EndsWith(value, StringComparison)`                                                                       |
| `indexOf`     | `indexOf(s, sub)`       | `indexOf(s, sub, startIndex?)`       | **P1**   | JS `String.indexOf(searchValue, fromIndex)`, Python `str.index(sub, start)`, C# `String.IndexOf(value, startIndex)` |
| `lastIndexOf` | `lastIndexOf(s, sub)`   | `lastIndexOf(s, sub, startIndex?)`   | **P1**   | JS `String.lastIndexOf(searchValue, fromIndex)`, C# `String.LastIndexOf(value, startIndex)`                         |
| `trim`        | `trim(s)`               | `trim(s, chars?)`                    | **P2**   | Python `str.strip(chars)`, Ruby `String#strip`, C# `String.Trim(params char[])`                                     |
| `trimStart`   | `trimStart(s)`          | `trimStart(s, chars?)`               | **P2**   | Python `str.lstrip(chars)`, C# `String.TrimStart(params char[])`                                                    |
| `trimEnd`     | `trimEnd(s)`            | `trimEnd(s, chars?)`                 | **P2**   | Python `str.rstrip(chars)`, C# `String.TrimEnd(params char[])`                                                      |

**`str.split(s, delim, limit?)` — Detailed Design:**

```stash
str.split("a,b,c,d", ",")      // → ["a", "b", "c", "d"]  (existing)
str.split("a,b,c,d", ",", 2)   // → ["a", "b,c,d"]        (NEW: split at most 2 times)
str.split("a,b,c,d", ",", -1)  // → ["a", "b", "c", "d"]  (-1 = unlimited, same as no limit)
```

Semantics: `limit` specifies the maximum number of splits (not result pieces). `str.split("a,b,c", ",", 2)` returns 3 elements (2 splits → 3 parts). A limit of 0 or negative means no limit.

Implementation: C#'s `String.Split` takes a `count` parameter for max result pieces, so pass `limit + 1`.

**`str.replace(s, old, new, count?)` — Detailed Design:**

```stash
str.replace("aaa", "a", "b")      // → "bbb"  (existing: replace ALL)
str.replace("aaa", "a", "b", 1)   // → "baa"  (NEW: replace first N)
str.replace("aaa", "a", "b", 2)   // → "bba"  (NEW: replace first 2)
```

Semantics: When `count` is omitted, replace all occurrences (existing behavior). When provided, replace at most `count` occurrences left-to-right.

**`str.contains(s, sub, ignoreCase?)` — Detailed Design:**

```stash
str.contains("Hello", "hello")          // → false  (existing)
str.contains("Hello", "hello", true)    // → true   (NEW: case-insensitive)
```

Implementation: Use `StringComparison.OrdinalIgnoreCase` when `ignoreCase` is true. Same pattern for `startsWith`, `endsWith`, `indexOf`, `lastIndexOf`.

---

### 2.3 `arr` — Array Operations

| Function      | Current Signature           | Proposed Signature                       | Priority | Prior Art                                                                              |
| ------------- | --------------------------- | ---------------------------------------- | -------- | -------------------------------------------------------------------------------------- |
| `sort`        | `sort(array)`               | `sort(array, comparator?)`               | **P0**   | Python `sorted(list, key=fn)`, JS `Array.sort(compareFn)`, Ruby `Array#sort {block}`   |
| `flat`        | `flat(array)`               | `flat(array, depth?)`                    | **P1**   | JS `Array.flat(depth=1)`, Ruby `Array#flatten(depth)`                                  |
| `join`        | `join(array, separator)`    | `join(array, separator?)`                | **P1**   | JS `Array.join(separator=",")`, Python `str.join(iterable)`, Ruby `Array#join(sep="")` |
| `indexOf`     | `indexOf(array, value)`     | `indexOf(array, value, startIndex?)`     | **P1**   | JS `Array.indexOf(value, fromIndex)`, C# `Array.IndexOf(array, value, startIndex)`     |
| `lastIndexOf` | `lastIndexOf(array, value)` | `lastIndexOf(array, value, startIndex?)` | **P1**   | JS `Array.lastIndexOf(value, fromIndex)`                                               |
| `unique`      | `unique(array)`             | `unique(array, fn?)`                     | **P2**   | Ruby `Array#uniq {block}`, Lodash `_.uniqBy(array, iteratee)`                          |
| `includes`    | `includes(array, value)`    | `includes(array, value, startIndex?)`    | **P2**   | JS `Array.includes(value, fromIndex)`                                                  |

**`arr.sort(array, comparator?)` — Detailed Design:**

```stash
arr.sort([3, 1, 2])                          // → [1, 2, 3]  (existing)
arr.sort(["banana", "apple"], (a, b) => {     // (NEW: custom comparator)
    return str.compare(a, b)
})
arr.sort(users, (a, b) => a.age - b.age)      // (NEW: sort by field)
```

Semantics: When `comparator` is omitted, use existing default sort. When provided, the comparator receives two elements and must return a negative number (a < b), zero (a == b), or positive number (a > b). This is the universal comparator contract (C, C++, Java, JS, Ruby, Python's `functools.cmp_to_key`).

Implementation: Check if second arg is callable. If so, use it as the comparison delegate in `List<T>.Sort()`.

**`arr.flat(array, depth?)` — Detailed Design:**

```stash
arr.flat([[1, 2], [3, [4]]])        // → [1, 2, 3, [4]]       (existing: depth=1)
arr.flat([[1, [2, [3]]]], 2)        // → [1, 2, 3]            (NEW: depth=2)
arr.flat([[1, [2, [3]]]], -1)       // → [1, 2, 3]            (NEW: -1 = fully recursive)
```

Semantics: Default depth is 1 (preserves existing behavior). Positive integers flatten that many levels. -1 flattens completely (like Ruby's `Array#flatten`).

**`arr.join(array, separator?)` — Detailed Design:**

```stash
arr.join(["a", "b", "c"], ",")     // → "a,b,c"  (existing)
arr.join(["a", "b", "c"])          // → "a,b,c"  (NEW: default separator is ",")
arr.join(["a", "b", "c"], "")      // → "abc"    (explicit empty separator)
```

Semantics: Default separator is `","`. This matches JS convention and is the most common use case for sysadmin scripts (CSV-like joining).

---

### 2.4 `dict` — Dictionary Operations

| Function | Current Signature | Proposed Signature      | Priority | Prior Art                                                                                               |
| -------- | ----------------- | ----------------------- | -------- | ------------------------------------------------------------------------------------------------------- |
| `get`    | `get(d, key)`     | `get(d, key, default?)` | **P0**   | Python `dict.get(key, default=None)`, Ruby `Hash#fetch(key, default)`, Go `map[key]` returns zero value |
| `merge`  | `merge(d1, d2)`   | `merge(d1, d2, deep?)`  | **P2**   | Lodash `_.merge` (deep), Ruby `Hash#merge` with block, Python `{**d1, **d2}` (shallow only)             |

**`dict.get(d, key, default?)` — Detailed Design:**

```stash
dict.get(config, "port")              // → null if missing  (existing)
dict.get(config, "port", 8080)        // → 8080 if missing  (NEW)
dict.get(config, "host", "localhost") // → "localhost" if missing  (NEW)
```

Semantics: When `default` is omitted, return `null` for missing keys (preserves existing behavior). When provided, return `default` instead of `null`.

This is arguably **the single most impactful change in this entire spec**. Every scripting language provides this. The pattern `dict.get(d, "key") ?? "default"` works today but is verbose and doesn't compose well in expressions.

---

### 2.5 `json` — Serialization

| Function    | Current Signature | Proposed Signature        | Priority | Prior Art                                                                        |
| ----------- | ----------------- | ------------------------- | -------- | -------------------------------------------------------------------------------- |
| `stringify` | `stringify(val)`  | `stringify(val, indent?)` | **P1**   | JS `JSON.stringify(val, replacer, space)`, Python `json.dumps(obj, indent=None)` |
| `pretty`    | `pretty(val)`     | `pretty(val, indent?)`    | **P2**   | Currently hardcoded to 2 spaces                                                  |

**`json.stringify(val, indent?)` — Detailed Design:**

```stash
json.stringify(data)        // → compact JSON  (existing)
json.stringify(data, 2)     // → pretty JSON with 2-space indent (NEW)
json.stringify(data, 4)     // → pretty JSON with 4-space indent (NEW)
json.stringify(data, "\t")  // → pretty JSON with tab indent (NEW, stretch goal)
```

Semantics: When `indent` is omitted or 0, produce compact JSON (existing). When a positive integer, produce indented JSON.

> **Decision:** Accept both integer and string for indent? Python and JS both accept either. For simplicity, start with integer only. String indent (tabs) can be added later.

**`json.pretty(val, indent?)` — Detailed Design:**

```stash
json.pretty(data)           // → pretty JSON, 2-space indent (existing)
json.pretty(data, 4)        // → pretty JSON, 4-space indent (NEW)
```

Note: With `json.stringify(val, indent?)` gaining indent support, `json.pretty` becomes syntactic sugar for `json.stringify(val, indent ?? 2)`. This is fine — keep both for discoverability.

---

### 2.6 `conv` — Type Conversion

| Function | Current Signature | Proposed Signature   | Priority | Prior Art                                                                          |
| -------- | ----------------- | -------------------- | -------- | ---------------------------------------------------------------------------------- |
| `toInt`  | `toInt(val)`      | `toInt(val, base?)`  | **P1**   | Python `int(val, base=10)`, JS `parseInt(string, radix)`, Ruby `String#to_i(base)` |
| `toHex`  | `toHex(n)`        | `toHex(n, padding?)` | **P2**   | Python `format(n, '08x')`, C# `ToString("X8")`                                     |

**`conv.toInt(val, base?)` — Detailed Design:**

```stash
conv.toInt("42")          // → 42      (existing)
conv.toInt("ff", 16)      // → 255     (NEW: hex parsing)
conv.toInt("0xff", 16)    // → 255     (NEW: hex with prefix)
conv.toInt("101", 2)      // → 5       (NEW: binary parsing)
conv.toInt("77", 8)       // → 63      (NEW: octal parsing)
```

Semantics: Default base is 10 (preserves existing). Supported bases: 2, 8, 10, 16. Error on unsupported base.

Implementation: C#'s `Convert.ToInt32(string, int fromBase)` supports bases 2, 8, 10, 16 natively.

**`conv.toHex(n, padding?)` — Detailed Design:**

```stash
conv.toHex(255)           // → "ff"        (existing)
conv.toHex(255, 4)        // → "00ff"      (NEW: zero-padded to 4 chars)
conv.toHex(255, 8)        // → "000000ff"  (NEW: zero-padded to 8 chars)
```

Implementation: Use `n.ToString($"x{padding}")` in C#.

---

### 2.7 `fs` — File System

| Function    | Current Signature          | Proposed Signature                    | Priority | Prior Art                                                                                                        |
| ----------- | -------------------------- | ------------------------------------- | -------- | ---------------------------------------------------------------------------------------------------------------- |
| `readFile`  | `readFile(path)`           | `readFile(path, encoding?)`           | **P1**   | Python `open(f, encoding='utf-8')`, Node.js `fs.readFileSync(path, encoding)`, Ruby `File.read(path, encoding:)` |
| `writeFile` | `writeFile(path, content)` | `writeFile(path, content, encoding?)` | **P2**   | Same as above                                                                                                    |
| `copy`      | `copy(src, dst)`           | `copy(src, dst, overwrite?)`          | **P1**   | C# `File.Copy(src, dst, overwrite)`, PowerShell `Copy-Item -Force`                                               |
| `move`      | `move(src, dst)`           | `move(src, dst, overwrite?)`          | **P1**   | PowerShell `Move-Item -Force`                                                                                    |
| `walk`      | `walk(path)`               | `walk(path, options?)`                | **P2**   | Python `os.walk(top, topdown, followlinks)`, Node.js varies                                                      |
| `listDir`   | `listDir(path)`            | `listDir(path, filter?)`              | **P2**   | Python `os.listdir` + `glob`, PowerShell `Get-ChildItem -Filter`                                                 |

**`fs.readFile(path, encoding?)` — Detailed Design:**

```stash
enum StringEncoding {
  UTF8,
  Latin1,
  ASCII
}

fs.readFile("data.txt")                     // → string (UTF-8, existing)
fs.readFile("data.txt", Encoding.UTF8)      // → string (explicit UTF-8)
fs.readFile("data.txt", Encoding.Latin1)    // → string (Latin-1 encoding)
fs.readFile("data.txt", Encoding.ASCII)     // → string (ASCII encoding)
```

Supported encodings: `"utf-8"` (default), `"ascii"`, `"latin1"` / `"iso-8859-1"`, `"utf-16"`, `"utf-32"`. Map to .NET `Encoding` objects.

**`fs.copy(src, dst, overwrite?)` — Detailed Design:**

```stash
fs.copy("a.txt", "b.txt")           // → overwrites if exists (EXISTING behavior)
fs.copy("a.txt", "b.txt", true)     // → overwrites (explicit)
fs.copy("a.txt", "b.txt", false)    // → throws if b.txt exists (NEW)
```

> **Decision:** The current default is to overwrite (because C# `File.Copy` with overwrite=true is used). The `overwrite` parameter defaults to `true` to preserve existing behavior. Setting it to `false` enables safe copy.

---

### 2.8 `path` — Path Manipulation

| Function | Current Signature | Proposed Signature  | Priority | Prior Art                                                                                                          |
| -------- | ----------------- | ------------------- | -------- | ------------------------------------------------------------------------------------------------------------------ |
| `join`   | `join(a, b)`      | `join(...segments)` | **P0**   | Python `os.path.join(*paths)`, Node.js `path.join(...paths)`, Go `filepath.Join(elem...)`, Ruby `File.join(*args)` |

**`path.join(...segments)` — Detailed Design:**

```stash
path.join("/usr", "local")                  // → "/usr/local"  (existing)
path.join("/usr", "local", "bin")           // → "/usr/local/bin"  (NEW: 3+ args)
path.join("/usr", "local", "bin", "stash")  // → "/usr/local/bin/stash"  (NEW: 4+ args)
```

This is a **P0 fix**. Every other language's `path.join` is variadic. The current binary-only restriction forces ugly nesting:

```stash
// Current workaround for 3+ segments:
path.join(path.join("/usr", "local"), "bin")
// vs. what every developer expects:
path.join("/usr", "local", "bin")
```

Implementation: Change to `isVariadic: true`, minimum 2 args. Loop through args calling `Path.Combine(accumulated, next)`.

---

### 2.9 `http` — HTTP Client

| Function   | Current Signature     | Proposed Signature              | Priority | Prior Art                                                                                               |
| ---------- | --------------------- | ------------------------------- | -------- | ------------------------------------------------------------------------------------------------------- |
| `get`      | `get(url)`            | `get(url, options?)`            | **P1**   | Node.js `fetch(url, options)`, Python `requests.get(url, **kwargs)`, Ruby `Net::HTTP.get(url, headers)` |
| `post`     | `post(url, body)`     | `post(url, body, options?)`     | **P1**   | Same as above                                                                                           |
| `put`      | `put(url, body)`      | `put(url, body, options?)`      | **P1**   | Same                                                                                                    |
| `patch`    | `patch(url, body)`    | `patch(url, body, options?)`    | **P1**   | Same                                                                                                    |
| `delete`   | `delete(url)`         | `delete(url, options?)`         | **P1**   | Same                                                                                                    |
| `download` | `download(url, path)` | `download(url, path, options?)` | **P2**   | curl `--connect-timeout`, wget `--timeout`                                                              |

**`http.get(url, options?)` — Detailed Design:**

```stash
// Existing (unchanged)
let resp = http.get("https://api.example.com/data")

// NEW: with headers
let resp = http.get("https://api.example.com/data", {
    headers: { "Authorization": "Bearer token123" },
    timeout: 10000
})
```

> **Note on struct vs dict for options:** Per project coding preferences, we prefer structs over anonymous dicts. However, HTTP options are inherently open-ended (arbitrary headers, varying options). Two approaches:
>
> 1. **Struct approach:** Define `HttpOptions { headers: dict, timeout: int, followRedirects: bool }` — typed but rigid
> 2. **Dict approach:** Accept a dict with known keys — flexible but untyped
>
> **Decision:** Use a **struct** `HttpOptions` with well-defined fields. HTTP options are finite and well-known. This aligns with Stash's struct-first philosophy. Users who need raw flexibility already have `http.request()`.

```stash
struct HttpOptions {
    headers: dict       // default: {}
    timeout: int        // default: 30000 (ms)
    followRedirects: bool  // default: true
}
```

Implementation: Check if last arg exists and is a struct/dict. Parse known fields. Pass headers to the `HttpClient` request, set `Timeout`.

> **Open question:** Currently `http.get/post/put/delete` hardcode a 30-second timeout and no custom headers. The full-featured `http.request(options)` exists but requires building the entire request manually. The convenience methods should accept lightweight options without requiring the user to drop down to `http.request()`.

---

### 2.10 `env` — Environment Variables

| Function | Current Signature | Proposed Signature    | Priority | Prior Art                                                                                                                              |
| -------- | ----------------- | --------------------- | -------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `get`    | `get(name)`       | `get(name, default?)` | **P0**   | Python `os.environ.get(key, default)`, Ruby `ENV.fetch(key, default)`, Go `os.Getenv` (returns ""), PowerShell `$env:VAR ?? "default"` |

**`env.get(name, default?)` — Detailed Design:**

```stash
env.get("HOME")                  // → "/home/user" or null  (existing)
env.get("PORT", "8080")          // → "8080" if PORT not set  (NEW)
env.get("DEBUG", "false")        // → "false" if DEBUG not set  (NEW)
```

Semantics: When `default` is omitted, return `null` for unset vars (preserves existing behavior). When provided, return `default` instead of `null`.

This is identical in spirit to `dict.get(d, key, default?)` and equally important. Sysadmin scripts are _dominated_ by `env.get("VAR") ?? "fallback"` patterns.

---

### 2.11 `time` — Date/Time

| Function | Current Signature | Proposed Signature           | Priority | Prior Art                                                                                                  |
| -------- | ----------------- | ---------------------------- | -------- | ---------------------------------------------------------------------------------------------------------- |
| `format` | `format(ts, fmt)` | `format(ts, fmt, timezone?)` | **P2**   | Python `datetime.strftime` with `tz`, JS `Intl.DateTimeFormat` with `timeZone`, Go `time.In(loc).Format()` |
| `parse`  | `parse(str, fmt)` | `parse(str, fmt, timezone?)` | **P2**   | Same as above                                                                                              |

> **Note:** Timezone support is a significant feature with its own complexity (IANA database, DST rules, etc.). This spec flags the gap but does **not** propose a full timezone implementation. That deserves its own spec.

---

### 2.12 `crypto` — Cryptography

| Function      | Current Signature | Proposed Signature          | Priority | Prior Art                                          |
| ------------- | ----------------- | --------------------------- | -------- | -------------------------------------------------- |
| `randomBytes` | `randomBytes(n)`  | `randomBytes(n, encoding?)` | **P2**   | Node.js `crypto.randomBytes(n).toString(encoding)` |

**`crypto.randomBytes(n, encoding?)` — Detailed Design:**

```stash
enum CryptoEncoding {
    Hex,
    Base64,
    Raw
}

crypto.randomBytes(16)                          // → "a3f1..." (hex string, existing)
crypto.randomBytes(16, CryptoEncoding.Hex)      // → "a3f1..." (explicit hex)
crypto.randomBytes(16, CryptoEncoding.Base64)   // → "o/E=..." (NEW: base64)
crypto.randomBytes(16, CryptoEncoding.Raw)      // → raw bytes as string (NEW)
```

---

### 2.13 `encoding` — Encoding Utilities

| Function       | Current Signature | Proposed Signature          | Priority | Prior Art                                                                    |
| -------------- | ----------------- | --------------------------- | -------- | ---------------------------------------------------------------------------- |
| `base64Encode` | `base64Encode(s)` | `base64Encode(s, urlSafe?)` | **P2**   | Python `base64.urlsafe_b64encode`, Node.js Buffer + manual replace, RFC 4648 |
| `base64Decode` | `base64Decode(s)` | `base64Decode(s, urlSafe?)` | **P2**   | Same                                                                         |

**`encoding.base64Encode(s, urlSafe?)` — Detailed Design:**

```stash
encoding.base64Encode("hello")          // → "aGVsbG8="  (existing)
encoding.base64Encode("hello", true)    // → "aGVsbG8="  (url-safe: + → -, / → _, no padding)
```

URL-safe base64 is used extensively in JWT tokens, URL parameters, and API keys. Currently users must manually replace characters after encoding.

---

### 2.14 `term` — Terminal Output

| Function | Current Signature    | Proposed Signature             | Priority | Prior Art                                                                                         |
| -------- | -------------------- | ------------------------------ | -------- | ------------------------------------------------------------------------------------------------- |
| `color`  | `color(text, color)` | `color(text, color, bgColor?)` | **P2**   | ANSI supports fg+bg, PowerShell `Write-Host -ForegroundColor -BackgroundColor`, Python `colorama` |

```stash
enum TermColor {
    Red,
    Yellow,
    Blue,
    Green
}

term.color("Error", TermColor.Red)                      // → red text  (existing)
term.color("Error", TermColor.Red, TermColor.Yellow)    // → red text on yellow bg  (NEW)
```

---

### 2.15 `io` — Input/Output

| Function  | Current Signature | Proposed Signature          | Priority | Prior Art                                                                    |
| --------- | ----------------- | --------------------------- | -------- | ---------------------------------------------------------------------------- |
| `confirm` | `confirm(prompt)` | `confirm(prompt, default?)` | **P2**   | npm `inquirer` default option, Python `input()` patterns, Ruby `TTY::Prompt` |

```stash
io.confirm("Continue?")              // → waits for y/n  (existing)
io.confirm("Continue?", true)        // → [Y/n] — Enter defaults to yes  (NEW)
io.confirm("Continue?", false)       // → [y/N] — Enter defaults to no  (NEW)
```

---

### 2.16 `sys` — System Information

| Function | Current Signature | Proposed Signature  | Priority | Prior Art                                                    |
| -------- | ----------------- | ------------------- | -------- | ------------------------------------------------------------ |
| `which`  | `which(name)`     | `which(name, all?)` | **P2**   | `which -a` on Unix, `where.exe` on Windows lists all matches |

```stash
sys.which("python")              // → "/usr/bin/python"  (existing: first match)
sys.which("python", true)        // → ["/usr/bin/python", "/usr/local/bin/python"]  (NEW: all matches)
```

---

## 3. Implementation Priority

### Phase 1 — P0 (Critical, daily friction)

These should be implemented first. They represent the most common "paper cut" frustrations:

1. **`math.round(n, precision?)`** — The original motivating example
2. **`dict.get(d, key, default?)`** — Eliminates the most common null-check pattern
3. **`env.get(name, default?)`** — Eliminates the most common env-var pattern
4. **`path.join(...segments)`** — Binary-only join is a daily annoyance
5. **`arr.sort(array, comparator?)`** — Sorting without a comparator is crippling for real use
6. **`str.split(s, delim, limit?)`** — Parsing config files, log lines, etc.

### Phase 2 — P1 (High value)

7. **`str.replace(s, old, new, count?)`**
8. **`str.contains/startsWith/endsWith(s, sub, ignoreCase?)`**
9. **`str.indexOf/lastIndexOf(s, sub, startIndex?)`**
10. **`arr.flat(array, depth?)`**
11. **`arr.join(array, separator?)`**
12. **`arr.indexOf/lastIndexOf(array, value, startIndex?)`**
13. **`math.min/max` variadic**
14. **`math.log(n, base?)`**
15. **`conv.toInt(val, base?)`**
16. **`json.stringify(val, indent?)`**
17. **`fs.readFile(path, encoding?)`**
18. **`fs.copy/move(src, dst, overwrite?)`**
19. **`http.get/post/put/patch/delete(url, [body,] options?)`**

### Phase 3 — P2 (Nice-to-have)

20. **`conv.toHex(n, padding?)`**
21. **`arr.unique(array, fn?)`**
22. **`arr.includes(array, value, startIndex?)`**
23. **`str.trim/trimStart/trimEnd(s, chars?)`**
24. **`json.pretty(val, indent?)`**
25. **`fs.writeFile(path, content, encoding?)`**
26. **`fs.walk(path, options?)`**, **`fs.listDir(path, filter?)`**
27. **`encoding.base64Encode/Decode(s, urlSafe?)`**
28. **`crypto.randomBytes(n, encoding?)`**
29. **`term.color(text, color, bgColor?)`**
30. **`io.confirm(prompt, default?)`**
31. **`sys.which(name, all?)`**
32. **`time.format/parse` timezone** (separate spec)
33. **`math.randomInt` optional args**
34. **`dict.merge(d1, d2, deep?)`**

---

## 4. Cross-Cutting Concerns

### 4.1 Backward Compatibility

**All changes are fully backward compatible.** Every proposed change adds an optional parameter with a default that preserves existing behavior. No existing call sites will break.

### 4.2 Bytecode VM Impact

Each function that gains an optional parameter needs its arity check updated in the VM. The pattern is well-established: check `args.Count`, branch on whether the optional arg was provided. No new opcodes needed.

### 4.3 Static Analysis Impact

The `Stash.Analysis` project needs parameter count updates for functions that change arity. The `BuiltInRegistry` already handles variadic functions, so no architectural changes needed.

### 4.4 LSP Impact

- **Signature help:** Parameter info needs updating for all modified functions
- **Hover:** Documentation strings need updating
- **Completion:** No changes needed (function names unchanged)

All of these are auto-derived from the `BuiltInRegistry` metadata, so updating the registration is sufficient.

### 4.5 Documentation Impact

- `docs/Stash — Standard Library Reference.md` — Every modified function needs its signature and examples updated
- Playground examples may benefit from updated stdlib

### 4.6 Test Strategy

Each new optional parameter needs tests for:

1. **Existing behavior preserved** — call without the new arg, verify unchanged results
2. **New parameter happy path** — call with valid new arg, verify expected results
3. **Edge cases** — boundary values (0, negative, empty string), type mismatches
4. **Error cases** — invalid arg types, out-of-range values

Estimated test count: ~3-5 tests per modified function × 34 functions = **~100-170 new tests**.

---

## 5. Alternatives Considered

### 5.1 "Options dict" pattern instead of positional optional args

**Rejected.** An `options` dict works for functions with many options (like `http.request`) but is overkill for adding one optional parameter to `math.round`. `math.round(n, { precision: 2 })` is worse than `math.round(n, 2)` in every way.

### 5.2 Method chaining / builder pattern

**Rejected.** Stash's stdlib uses namespace functions (`str.split(s, ...)`) not method calls (`s.split(...)`). A builder pattern doesn't fit this model. UFCS provides method-call syntax but doesn't change the underlying function signatures.

### 5.3 Do nothing — let users compose workarounds

**Rejected.** The workarounds exist but are verbose and non-discoverable:

- `math.round(n * 100) / 100` instead of `math.round(n, 2)`
- `dict.has(d, k) ? d[k] : default` instead of `dict.get(d, k, default)`
- `path.join(path.join(a, b), c)` instead of `path.join(a, b, c)`

These workarounds are what every other language eliminated 20+ years ago.

### 5.4 Implement all at once vs. phased rollout

**Decision: Phased rollout (Phase 1 → 2 → 3).** The P0 changes are independent and can ship immediately. P1 and P2 can follow in subsequent batches. Each function is independently implementable — there are no dependencies between them.

---

## 6. Risks

| Risk                                                      | Likelihood | Mitigation                                                                                                |
| --------------------------------------------------------- | ---------- | --------------------------------------------------------------------------------------------------------- |
| Argument ambiguity in variadic functions                  | Low        | Each variadic change has clear type signatures (e.g., `arr.sort` — if arg is callable, it's a comparator) |
| Performance regression from optional arg checks           | Very Low   | A single `if (args.Count > N)` branch is negligible                                                       |
| Scope creep — adding too many options to simple functions | Medium     | This spec defines the ceiling. Don't add options not listed here without separate analysis                |
| Breaking changes from default value choices               | Zero       | All defaults preserve existing behavior by design                                                         |

---

## 7. Decision Log

| Date       | Decision                                                            | Rationale                                                                 |
| ---------- | ------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| 2025-07-15 | Start with positional optional args, not options dicts              | Positional is simpler, more readable for 1-2 optional params              |
| 2025-07-15 | `arr.join` default separator is `","` not `""`                      | Matches JS convention; comma-joining is the most common sysadmin use case |
| 2025-07-15 | `json.stringify` indent accepts integer only (not string) initially | Simplicity first; tab indent can be added later                           |
| 2025-07-15 | HTTP convenience methods use struct-based options, not dict         | Aligns with Stash's struct-first philosophy per coding preferences        |
| 2025-07-15 | `fs.copy/move` default `overwrite` to `true`                        | Preserves existing behavior where copy/move silently overwrite            |
| 2025-07-15 | Timezone support flagged but deferred to separate spec              | Timezone is complex enough to warrant dedicated design                    |
| 2025-07-15 | `math.round` with negative precision rounds to powers of 10         | Matches Python's `round(1234, -2) → 1200` behavior                        |
| 2025-07-15 | `str.split` limit = max splits (not max result pieces)              | Matches Python semantics; result count = limit + 1                        |
