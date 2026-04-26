# Changelog

All notable changes to Stash are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- **`archive` namespace** — ZIP, TAR, and GZIP support for sysadmin scripts
  - `archive.zip` / `archive.unzip` — Create and extract ZIP archives
  - `archive.tar` / `archive.untar` — Create and extract TAR archives (with optional gzip)
  - `archive.gzip` / `archive.gunzip` — Compress and decompress individual files
  - `archive.list` — List archive contents without extracting
  - `ArchiveOptions` struct with `compressionLevel`, `overwrite`, `preservePaths`, `filter` fields
  - `ArchiveEntry` struct with `name`, `size`, `isDirectory`, `lastModified` fields
- **`csv` namespace** — RFC 4180 compliant CSV parsing and writing
  - `csv.parse` / `csv.stringify` — Parse and serialize CSV strings
  - `csv.parseFile` / `csv.writeFile` — File-based CSV I/O
  - `CsvOptions` struct with `delimiter`, `quote`, `escape`, `header`, `columns` fields
  - Handles quoted fields, embedded commas, embedded newlines, doubled-quote escaping
  - `header: true` mode returns array of dicts with first row as keys
- **`log` namespace** — Structured logging with levels, timestamps, and output targets
  - `log.debug` / `log.info` / `log.warn` / `log.error` — Level-filtered logging
  - `log.setLevel` — Set minimum log level threshold (`debug`, `info`, `warn`, `error`)
  - `log.setFormat` — Set output format: `text` or `json`
  - `log.setOutput` — Set output target: `stdout`, `stderr`, or a file path
  - `log.withFields` — Return a scoped logger with preset fields merged into every entry
  - Text format: `[YYYY-MM-DD HH:mm:ss.fff] LEVEL message key=value`
  - JSON format with proper type handling via `Utf8JsonWriter`
- **`crypto.encrypt` / `crypto.decrypt` / `crypto.generateKey`** — AES-256-GCM symmetric encryption
  - `crypto.generateKey(bits?)` — Generate a cryptographically secure key (128/192/256 bits)
  - `crypto.encrypt(data, key)` — AES-256-GCM encrypt; returns `{ ciphertext, iv, tag }` dict
  - `crypto.decrypt(ciphertext, key)` — AES-256-GCM decrypt with authentication tag verification
- **CI/CD Pipeline** — GitHub Actions workflows
  - `ci.yml` — Cross-platform build and test matrix (Linux, macOS, Windows)
  - AOT binary size verification for `Stash.Cli`, `Stash.Check`, `Stash.Format`
  - Managed build verification for `Stash.Lsp` and `Stash.Dap`
  - `release.yml` — Automated release pipeline for `v*.*.*` tags
  - Publishes native binaries for linux-x64, osx-x64, osx-arm64, win-x64
  - SHA-256 checksums included with each release
- **`xml` namespace** — XML parsing, serialization, querying, and validation
  - `xml.parse(text, options?)` — Parses an XML string into an `XmlNode` tree
  - `xml.stringify(node, options?)` — Serializes an `XmlNode` back to an XML string
  - `xml.valid(text)` — Returns `true` if the string is well-formed XML
  - `xml.query(root, xpath)` — Queries an `XmlNode` tree using an XPath expression
  - `XmlNode` struct with `tag`, `attrs`, `text`, `children` fields
  - `XmlParseOptions` struct with `preserveWhitespace` field
  - `XmlStringifyOptions` struct with `indent`, `declaration`, `encoding` fields
- **`config` namespace** — now supports all 6 config formats: JSON, YAML, TOML, INI, CSV, XML
  - `config.read` / `config.write` / `config.parse` / `config.stringify` now handle `"csv"` and `"xml"` format strings
  - Auto-detection extended: `.csv` → `csv`, `.xml` → `xml`
- **`io.readPassword(prompt?)`** — Reads a password from the terminal with character masking; returns a `secret` value
  - Falls back to plain `readLine` when stdin is not a TTY (pipes, CI environments)
- **`http.head(url, options?)`** — Sends an HTTP HEAD request; returns status, headers, and an empty body
- **`fs.chown(path, uid, gid)`** — Changes file ownership by UID/GID on Unix; throws a descriptive error on Windows
  - Pass `-1` for `uid` or `gid` to leave that value unchanged
- **`test.only(name, fn)`** — Marks a test as exclusive; when any `test.only` test is present, all `test.it` tests are skipped
- **`assert.deepEqual(actual, expected)`** — Deep structural equality assertion for nested arrays, dicts, and structs; includes path-aware failure messages (e.g. `at [2].name`)
- **`assert.closeTo(actual, expected, delta)`** — Numeric proximity assertion with a tolerance `delta`
- **Runtime error message improvements** — Arithmetic and comparison errors now include type names and conversion hints
  - Example: `"Operands must be numbers or strings, got 'bool' and 'int'. Convert with conv.toInt() or conv.toFloat() first."`
  - Constant assignment error now names the variable: `"Cannot assign to constant 'myVar'."`

---

## [0.9.0] — Pre-release

### Language Core
- 54 AST node types, recursive-descent parser
- Bytecode VM with 94 opcodes and register-based dispatch
- 14 runtime value types: null, bool, int, float, string, secret, byte, list, dict, range, struct instance, enum value, function, future
- Full control flow: `if/else`, `while`, `for/in`, `loop`, `break`, `continue`, `return`
- Error handling: `try/catch/finally`, `throw`, first-class `StashError` values
- Closures, higher-order functions, lambda expressions
- Interfaces: `interface` declarations with method requirements, struct `implements`
- Enums with typed values and method definitions
- Pattern matching with `match` expression (value, type, range, enum, wildcard)
- `defer` statements for guaranteed cleanup
- `retry` expression for automatic retry with backoff
- `timeout` expression for time-limited operations
- Async/await: `async fn`, `await`, structured concurrency with `task.all` / `task.race`
- Spread operator (`...arr`) and rest parameters (`fn(a, ...rest)`)
- UFCS (Uniform Function Call Syntax): `value.method(args)` → `method(value, args)`
- Bitwise operators: `&`, `|`, `^`, `~`, `<<`, `>>`
- Template literals with `${}` interpolation
- Multi-line strings with triple quotes `"""`
- Native `secret` type for sensitive values (redacted in output, never logged)
- Duration literals (`5s`, `100ms`, `2min`) and byte size literals (`1KB`, `4MB`)
- IP address literals (`192.168.1.1`, `::1`)
- Shell command execution with `$(cmd)` syntax and `CommandResult` type
- `elevate` keyword for privilege escalation on Unix/Windows

### Standard Library — 35 Namespaces, 500+ Functions
- **`io`** — Terminal I/O: `println`, `print`, `readLine`, `confirm`
- **`conv`** — Type conversion: `toStr`, `toInt`, `toFloat`, `toHex`, `fromHex`, `toBool`
- **`arr`** — 37 array functions: `map`, `filter`, `reduce`, `sort`, `groupBy`, `chunk`, `flatten`, `unique`, and more
- **`dict`** — 21 dictionary functions: `merge`, `pick`, `omit`, `map`, `filter`, `defaults`, and more
- **`str`** — 38 string functions: `split`, `replace`, `match`, `format`, `slug`, `wrap`, `padStart`, and more
- **`math`** — Mathematical functions: `abs`, `ceil`, `floor`, `round`, `sqrt`, `pow`, trig functions, `random`, `clamp`
- **`time`** — Date and time: `now`, `parse`, `format`, `add`, `diff`, `year`, `month`, `day`
- **`json`** — JSON parse/stringify with pretty printing
- **`yaml`** — YAML parse/stringify
- **`toml`** — TOML parse/stringify
- **`ini`** — INI file parse/stringify
- **`config`** — Multi-format config read/write (JSON, YAML, TOML, INI, CSV, XML)
- **`fs`** — 27 filesystem functions: `readFile`, `writeFile`, `glob`, `walk`, `stat`, `chmod`, and more
- **`path`** — Path manipulation: `abs`, `dir`, `base`, `ext`, `join`, `normalize`
- **`env`** — Environment: `get`, `set`, `all`, `cwd`, `home`, `hostname`, `loadFile`, `saveFile`
- **`http`** — HTTP client: `get`, `post`, `put`, `patch`, `delete`, `download`
- **`process`** — Process management: `exec`, `spawn`, `wait`, `kill`, `pid`, `signal`
- **`args`** — CLI argument parsing: `list`, `count`, `parse`, `build`
- **`crypto`** — Cryptography: `md5`, `sha1`, `sha256`, `sha512`, `hmac`, `hashFile`, `uuid`, `randomBytes`
- **`encoding`** — Encoding: `base64Encode`, `base64Decode`, `urlEncode`, `urlDecode`, `hexEncode`, `hexDecode`
- **`term`** — Terminal: colors, styles, `table`, `clear`, `width`, `height`
- **`sys`** — System info: `cpuCount`, `diskUsage`, `uptime`, `loadAvg`, `networkInterfaces`
- **`net`** — DNS and networking: `resolve`, `resolveMx`, `resolveTxt`, `ping`, TCP connect/listen
- **`ssh`** — SSH client: `connect`, `exec`, `uploadFile`, `downloadFile`
- **`sftp`** — SFTP client: `connect`, `list`, `readFile`, `writeFile`, `delete`
- **`buf`** — Byte buffer manipulation
- **`tpl`** — Template rendering with Handlebars-style syntax
- **`task`** — Async task utilities: `sleep`, `timeout`, `all`, `race`, `retry`
- **`pkg`** — Package management: `install`, `list`, `remove`, `update`
- **`scheduler`** — Cross-platform service management (systemd, launchd, Task Scheduler)
- **`test` / `assert`** — TAP-compatible test framework

### Tooling
- **LSP** — Language Server with 27 handlers: hover, completions, go-to-definition, references, signature help, semantic tokens, diagnostics, formatting, rename, code actions, and more
- **DAP** — Debug Adapter with 18 handlers: breakpoints, stepping, variable inspection, watch expressions, call stack, REPL evaluation
- **Static Analysis** — 68 analysis rules: unused variables, unreachable code, type mismatches, namespace validation, and more
- **Formatter** — Full-fidelity code formatter covering all syntax constructs
- **REPL** — Interactive shell with multiline input and history
- **TAP Runner** — Test runner with `--filter`, `--watch`, `--output` options and timing output
- **Package Manager CLI** — Install, publish, search, and manage packages
- **Package Registry** — ASP.NET Core registry server with JWT auth, package uploads, version management
- **Browser Playground** — Blazor WASM interactive playground with Monaco editor
- **VS Code Extension** — Syntax highlighting, LSP/DAP integration, TAP test runner, debug launch configurations

### Build System
- Native AOT compilation for CLI, Check, Format (Linux, macOS, Windows)
- Reflection-based managed builds for LSP, DAP
- WebAssembly build for Playground (Emscripten)

[Unreleased]: https://github.com/your-org/stash-lang/compare/v0.9.0...HEAD
[0.9.0]: https://github.com/your-org/stash-lang/releases/tag/v0.9.0
