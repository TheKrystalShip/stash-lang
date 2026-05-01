# Changelog

All notable changes to Stash are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added

#### Shell Aliases

- **Shell mode:** New `alias` sugar at the REPL prompt — `alias name = "body"` (template), `alias name(params) = body` (function), and `unalias name` (remove). Aliases are resolved before PATH executables in bare-word line classification.
- **Stdlib:** New `alias` namespace with full programmatic API:
  - `alias.define(name, body, opts?)` — register a template or function alias
  - `alias.list()` — return all aliases as `AliasInfo` structs
  - `alias.names()` — return sorted array of alias names
  - `alias.get(name)` — return `AliasInfo` for a single alias
  - `alias.exists(name)` — test whether an alias is registered
  - `alias.remove(name)` — remove an alias
  - `alias.clear()` — remove all user-defined aliases
  - `alias.exec(name, args?)` — programmatically invoke an alias
  - `alias.expand(name, args?)` — expand a template alias to a string without executing
  - `alias.save(name?)` — persist one or all aliases to `aliases.stash`
  - `alias.load(path?)` — load aliases from `aliases.stash`
- **Built-in aliases:** `cd`, `pwd`, `exit`, `quit`, `history` are pre-registered at shell startup and map to their Stash stdlib equivalents. They can be overridden with `AliasOptions { override: true }`.
- **Hooks:** `AliasOptions.before` and `AliasOptions.after` callables run immediately before and after alias execution.
- **Confirm prompts:** `AliasOptions.confirm` shows an interactive prompt before execution; accepted automatically in non-interactive mode.
- **Persistence:** Aliases survive REPL restarts via `aliases.stash` in the platform config directory (`~/.config/stash/` on Linux, `~/Library/Application Support/stash/` on macOS, `%APPDATA%\stash\` on Windows). Use `alias name --save` or `alias.save()` to persist.
- **Tab completion:** Alias names appear in shell-mode completion at command position, interleaved with PATH executables and callable Stash globals.
- **New error type:** `AliasError` (fields: `message`, `aliasName?`, `detail?`) thrown for invalid alias names, missing aliases, and override conflicts.
- **Static analysis:**
  - **SA0850** (Error, Aliases) — `alias.define` called with a name that is not a valid identifier (contains spaces, `.`, `/`, starts with a digit, or is empty).
  - **SA0851** (Warning, Aliases) — `AliasOptions.confirm` is an empty string; the user will see no prompt text.

- **Language:** `unset name1, name2, …;` statement removes one or more top-level bindings (`let`, `fn`, `struct`, `enum` globals; `const` in REPL only). Unsetting an unknown name is a runtime no-op (SA0840 warning). Built-ins, imports, and script `const` bindings are rejected at analysis time (SA0841–SA0844). In shell mode, `unset ls` un-shadows an accidentally-declared Stash symbol so the PATH binary becomes reachable again. See [Language Specification — §7i](docs/Stash%20—%20Language%20Specification.md#7i-unset-statement).
- **Stdlib:** `env.unset(name: str) -> bool` removes an environment variable from the current process environment. Returns `true` if the variable existed. Throws `TypeError` for non-string names and `ValueError` for names that are empty, contain `=`, or contain a null byte. See [Standard Library Reference — env](docs/Stash%20—%20Standard%20Library%20Reference.md#env--environment-variables).
- **Bytecode:** new `UnsetGlobal` opcode; `.stashc` files compiled before this change will fail to load with an "unknown opcode" error and must be recompiled.

#### Stdlib Namespace Audit — New Namespaces

Six new top-level namespaces extracted from overloaded namespaces. All old names remain as **deprecated aliases** (SA0830 warning) so existing scripts continue to work without modification.

**`re` — Regular Expressions** (from `str`):
- `re.match`, `re.matchAll`, `re.test` (was `str.isMatch`), `re.replace` (was `str.replaceRegex`), `re.capture`, `re.captureAll`
- `RegexMatch` and `RegexGroup` structs are available in both `re` and `str`

**`tcp` — TCP Sockets** (from `net`):
- `tcp.connect`, `tcp.send`, `tcp.recv`, `tcp.close`, `tcp.listen`
- `tcp.connectAsync`, `tcp.sendAsync`, `tcp.sendBytesAsync`, `tcp.recvAsync`, `tcp.recvBytesAsync`, `tcp.closeAsync`, `tcp.listenAsync`, `tcp.serverClose`, `tcp.isOpen`, `tcp.state`
- `TcpConnection`, `TcpConnectOptions`, `TcpRecvOptions`, `TcpServer`, `TcpConnectionState` available in both `tcp` and `net`

**`udp` — UDP Datagrams** (from `net`):
- `udp.send`, `udp.recv`
- `UdpMessage` available in both `udp` and `net`

**`ws` — WebSockets** (from `net`):
- `ws.connect`, `ws.send`, `ws.sendBinary`, `ws.recv`, `ws.close`, `ws.state`, `ws.isOpen`
- `WsConnection`, `WsMessage`, `WsConnectionState` available in both `ws` and `net`

**`dns` — DNS Resolution** (from `net`):
- `dns.resolve`, `dns.resolveAll`, `dns.reverseLookup`, `dns.resolveMx`, `dns.resolveTxt`
- `MxRecord` available in both `dns` and `net`

**`signal` — Signal Handling** (from `sys`):
- `signal.on` (was `sys.onSignal`), `signal.off` (was `sys.offSignal`)
- `Signal` is now a **global enum** with shorter member names: `Signal.Hup`, `Signal.Int`, `Signal.Quit`, `Signal.Kill`, `Signal.Term`, `Signal.Usr1`, `Signal.Usr2` (was `sys.Signal.SIGHUP` etc.)

### Changed

#### Function Renames (Deprecated Aliases Provided)

| Old name | New name | Reason |
| -------- | -------- | ------ |
| `str.match` | `re.match` | Moved to `re` namespace |
| `str.matchAll` | `re.matchAll` | Moved to `re` namespace |
| `str.isMatch` | `re.test` | Moved to `re` namespace; name aligns with JS `RegExp.test` |
| `str.replaceRegex` | `re.replace` | Moved to `re` namespace |
| `str.capture` | `re.capture` | Moved to `re` namespace |
| `str.captureAll` | `re.captureAll` | Moved to `re` namespace |
| `arr.new` | `arr.create` | `new` is a soft keyword; `create` is unambiguous |
| `conv.charCode` | `str.charCode` | Character ↔ code-point belongs in `str` |
| `conv.fromCharCode` | `str.fromCharCode` | Character ↔ code-point belongs in `str` |
| `sys.onSignal` | `signal.on` | Moved to `signal` namespace |
| `sys.offSignal` | `signal.off` | Moved to `signal` namespace |
| `sys.Signal.*` | `Signal.*` | Global enum with shortened member names |
| `net.tcp*` | `tcp.*` | Moved to `tcp` namespace |
| `net.udp*` | `udp.*` | Moved to `udp` namespace |
| `net.ws*` | `ws.*` | Moved to `ws` namespace |
| `net.resolve*` | `dns.*` | Moved to `dns` namespace |

All old names emit SA0830 (`DeprecatedBuiltInMember`) when used. See [Stdlib Namespace Audit spec](.kanban/2-in-progress/Stdlib%20Namespace%20Audit%20—%20Mixed-Responsibility%20Cleanup.md).

### Fixed

- **Editor (TextMate grammar):** Identifiers, namespace members, struct types, and dot-property access now highlight correctly inside string interpolations (`$"{expr}"`, `"text ${expr}"`, triple-quoted variants, and `$(cmd ${expr})` command interpolations). Two independent bugs were fixed: (1) the TextMate grammar wrapped interpolation contents in a `string.*` parent scope, suppressing both inner scopes and semantic tokens — interpolation content now uses `meta.interpolation.expression.stash` and falls through to `$self`; (2) the LSP `SemanticTokensHandler` iterated only the outer token list, never visiting the inner tokens nested inside `InterpolatedString` / `CommandLiteral` tokens — it now recurses into them so identifiers inside `${…}` / `{…}` receive their semantic colors. **Theme migration:** the legacy `meta.interpolation.stash` and `meta.interpolation.command.stash` scopes are removed; themes that targeted them should switch to `meta.interpolation.expression.stash`.

### Added

#### Persistent REPL History

- **Persistent command history** across REPL sessions. Every command typed at the interactive REPL is appended to a history file so that up-arrow recall works after restart. History is always on for any interactive REPL session; always off for non-interactive script execution.
- **Default history file location:**
  - POSIX: `$XDG_STATE_HOME/stash/history` → `~/.local/state/stash/history` → `~/.stash_history`
  - Windows: `%LOCALAPPDATA%\stash\history` → `%USERPROFILE%\.stash_history`
- **`STASH_HISTORY_FILE`** env var — override the path; set to empty string to disable persistence for the session.
- **`STASH_HISTORY_SIZE`** env var — cap on stored entries (default: `10000`; `0` disables; negative = unlimited).
- **`--no-history`** CLI flag — disable persistence for the session (equivalent to `STASH_HISTORY_FILE=`).
- **`history` shell built-in** — `history` (all entries), `history N` (last N entries), `history -c` (clear). Output is pipeable: `history | grep git`.
- **`process.historyList() -> array<string>`** — return the in-memory history, oldest-first.
- **`process.historyClear()`** — clear in-memory history and truncate the file.
- **`process.historyAdd(line: string)`** — append a line, applying the same leading-space and dedup rules as interactive input.
- **Behavioral rules:** leading-space lines not stored (secret-redaction escape hatch), empty/whitespace-only lines never stored, consecutive duplicates collapsed, multi-line entries kept whole, cap enforced only at startup.

#### Tab Completion

- **Tab completion in the interactive REPL** (shell mode and Stash mode). Bash-classic UX: first `Tab` inserts the longest common prefix; second consecutive `Tab` lists candidates in a multi-column layout with a pager prompt for more than 100 results. Covers PATH executables, file paths, Stash keywords and globals, namespace members (e.g. `fs.<Tab>`), and custom user-registered completers. Disable with `STASH_NO_COMPLETION=1`.
- **`complete.*` stdlib namespace** for registering custom command completers: `complete.register`, `complete.unregister`, `complete.registered`, `complete.suggest`, `complete.paths`. New built-in struct types `CompletionContext` and `CompletionResult`. See [Standard Library Reference — `complete`](docs/Stash%20—%20Standard%20Library%20Reference.md#complete--tab-completion) and [Shell — Tab Completion](docs/Shell%20—%20Interactive%20Shell%20Mode.md#15-tab-completion).

#### Shell Mode — Interactive REPL Shell

Stash can now be used as an interactive login shell. Enable with `--shell`, `STASH_SHELL=1`, or by placing a `~/.stashrc` (or `~/.config/stash/init.stash`) file. Full documentation: [docs/Shell — Interactive Shell Mode.md](docs/Shell%20—%20Interactive%20Shell%20Mode.md).

- **Bare command execution** — type commands directly at the REPL prompt without `$(…)`:
  ```text
  $ ls -la
  $ git status | head -5
  $ ./deploy.sh --env prod
  ```
- **Line classifier** (`ShellLineClassifier`) — distinguishes Stash code from shell commands by inspecting the first token. Stash keywords, literals, and declared symbols always parse as Stash; PATH-resolvable identifiers route to the shell runner.
- **`\cmd` — force shell execution** — bypasses Stash symbol lookup to invoke the PATH binary directly (e.g. `\ls` when `ls` is a declared Stash variable).
- **`!cmd` — strict mode** — raises `CommandError` on non-zero exit; mirrors existing `$!(…)` semantics. `!\cmd` combines both.
- **Brace expansion** — `{a,b,c}` patterns expand to multiple words; cross-product when multiple brace groups appear: `{a,b}-{1,2}` → `a-1 a-2 b-1 b-2`.
- **`${expr}` interpolation** — evaluates any Stash expression in the REPL scope before passing arguments to the command. Bare `$VAR` is not supported; use `${env.get("VAR")}`.
- **Tilde expansion** — leading `~` and `~/` expand to the home directory.
- **Glob expansion** — `*`, `?`, `[…]`, `**` are expanded against the filesystem. No-match throws `CommandError` (zsh-style safe default).
- **OS-level streaming pipelines** — `cmd1 | cmd2 | cmd3` uses true OS pipes; stages run concurrently. Downstream early-close (e.g. `head -5`) triggers graceful upstream shutdown.
- **Redirects** — `>`, `>>`, `2>`, `2>>`, `&>`, `&>>` on the last pipeline stage.
- **Multi-line pipelines** — a trailing `|` continues the pipeline on the next line with a `... ` prompt.
- **Shell built-in sugar** — `cd`, `pwd`, `exit`, `quit` desugar to `process.*` stdlib calls, so all Stash error handling, stack traces, and `defer` blocks work normally:
  - `cd <dir>` → `process.chdir(<dir>)`
  - `cd` → home directory
  - `cd -` → `process.popDir()` + print new cwd
  - `pwd` → `io.println(process.cwd())`
  - `exit [code]` / `quit [code]` → `process.exit(code)`
- **`$?` REPL sugar** — the token `$?` is desugared to `process.lastExitCode()` before lexing. REPL-only; not valid in scripts.
- **RC file** — `$XDG_CONFIG_HOME/stash/init.stash` → `~/.config/stash/init.stash` → `~/.stashrc` (first match). Lines are processed through the REPL evaluator (including the classifier). RC file presence implicitly enables shell mode.
- **Directory stack** — `process.chdir` now maintains a directory stack (capped at 256 entries):
  - `process.popDir() -> string` — pop + restore previous cwd
  - `process.dirStack() -> array<string>` — oldest entry first
  - `process.dirStackDepth() -> int` — stack depth
- **`process.exit(code: int = 0)`** — defer-aware (runs all `defer` blocks on exit), catch-immune (no `try/catch` can intercept it).
- **`process.lastExitCode() -> int`** — exit code of the most recent `$(…)` or bare command.
- **Cross-platform polish** — Windows is gate-blocked (`"shell mode not yet supported on Windows"`) with Windows-aware code paths ready for future re-enable: PATHEXT lookup, drive-path classifier, `OrdinalIgnoreCase` PATH cache, `%USERPROFILE%` tilde expansion.

#### REPL Prompt Customization

Full prompt customization via Stash code. Full documentation: [docs/Prompt — Customizing the REPL Prompt](docs/Prompt%20%E2%80%94%20Customizing%20the%20REPL%20Prompt.md).

- **`prompt` namespace** — 17 primitive built-in functions for REPL prompt customization: `prompt.set`, `prompt.setContinuation`, `prompt.reset`, `prompt.resetContinuation`, `prompt.render`, `prompt.context`, `prompt.palette`, `prompt.setPalette`, theme registry (`prompt.themeRegister`, `prompt.themeUse`, `prompt.themeCurrent`, `prompt.themeList`), starter registry (`prompt.registerStarter`, `prompt.useStarter`, `prompt.listStarters`), `prompt.bootstrapDir`, `prompt.resetBootstrap`.
- **`PromptContext`** built-in struct — `cwd`, `cwdAbsolute`, `user`, `host`, `hostFull`, `time`, `lastExitCode`, `lineNumber`, `mode`, `hostColor`, `git` fields.
- **`PromptGit`** built-in struct — `isInRepo`, `branch`, `isDirty`, `stagedCount`, `unstagedCount`, `untrackedCount`, `ahead`, `behind` fields. Set to `null` on timeout or missing `git` binary.
- **`term.zeroWidth(text)`** — marks a string as zero-width for prompt length calculation; use when embedding non-SGR escape sequences (OSC codes, hyperlinks) in prompt strings.
- **`term.colorsEnabled()`** — returns `true` when ANSI color output is active; respects `NO_COLOR` and `STASH_FORCE_COLOR`.
- **Bundled prompt bootstrap** — shipped at `~/.config/stash/prompt/` (Windows: `%APPDATA%\stash\prompt\`); defines `theme` and `starter` top-level global dictionaries plus:
  - 6 bundled themes: `default`, `nord`, `catppuccin-mocha`, `monokai`, `dracula`, `gruvbox-dark`
  - 6 bundled starter prompts: `minimal`, `bash-classic`, `pure`, `developer`, `pwsh-style`, `powerline-lite`
- **Default REPL prompt** — when shell mode is active and the bootstrap is loaded, the default prompt is `<cwd> > ` with colored success (`✓`) / failure (`✗`) mark.
- **OSC 133 prompt markers** — emitted by default in interactive TTYs for VS Code, iTerm2, WezTerm, and other shell-integration-aware terminals. Auto-disabled for dumb terminals (`TERM=dumb`, `TERM=linux`, screen multiplexers, non-TTY). Opt out with `STASH_NO_OSC133=1`.
- **`--reset-prompt` CLI flag** — re-extracts the bundled bootstrap scripts to the bootstrap directory and exits. Useful after a Stash upgrade when you want to pick up updated themes/starters.
- **`STASH_NO_PROMPT_BOOTSTRAP=1`** — full opt-out: bootstrap is not loaded; REPL falls back to `stash> ` / `... `; `theme` and `starter` globals are undefined.
- **`STASH_PROMPT_GIT_TIMEOUT_MS`** — controls the `ctx.git` probe timeout in milliseconds (default: `150`). Set to `0` to disable the git probe entirely.

### Changed

- **`LineEditor` cursor positioning** — now ANSI-aware: uses visible character width (excluding SGR sequences) for cursor positioning, fixing off-by-one errors in prompts that use color codes.
- **`MultiLineReader` prompt providers** — now accepts `Func<string>` and `Func<int, string>` delegates instead of fixed prompt strings, enabling dynamic prompts per continuation depth.
- **REPL VM global slot allocation** — the REPL VM now persists its global slot allocator (`VirtualMachine.ReplGlobalAllocator`) across REPL inputs, fixing a latent bug where global indices could collide between independently-compiled REPL inputs when many globals were declared.

- **`$(…)` glob auto-expansion** (**BREAKING**) — Glob patterns (`*`, `?`, `[…]`, `**`) inside `$(…)` command literals are now **expanded against the filesystem** before being passed to the command. Previously, patterns were passed literally.

  **Impact:** any script that relied on passing an unquoted glob literally (e.g. `$(rm *.tmp)`, `$(find . -name *.log)`) will now have the glob expanded. If no files match, `CommandError` is thrown.

  **Migration — quote the glob pattern:**
  ```stash
  // Before (was literal, now globs):
  let result = $(find . -name *.log);

  // After (quote to preserve literal behavior):
  let result = $(find . -name "*.log");
  ```

  The static analyzer rule SA0820 flags all unquoted globs in `$(…)` to help locate affected code.

### Static Analysis

- **SA0820** (Warning) — Unquoted glob pattern in `$(…)` command literal. Warns when `$(…)` content contains an unquoted `*`, `?`, or `[`. Suppress with `// stash:ignore[SA0820]` when glob expansion is intentional.
- **SA0821** (Info) — Bare identifier may shadow PATH executable in shell mode. Emitted by the REPL classifier when a declared Stash symbol also resolves on PATH. Not emitted for scripts.

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

### Changed

#### Process Namespace Decomposition

- **stdlib**: `process.chdir`, `process.popDir`, `process.dirStack`, `process.dirStackDepth`, `process.withDir`, and `process.exit` moved to the `env` namespace. The old names still work but emit a new deprecation warning (SA0830) and will be removed in a future minor release.
- **stdlib**: `process.lastExitCode` moved to a new `shell` namespace gated on `StashCapabilities.Shell` (enabled by default in the CLI, opt-in for embedded hosts). The old name still works (deprecated, SA0830).
- **stdlib**: `process.SIGHUP`, `process.SIGINT`, `process.SIGQUIT`, `process.SIGKILL`, `process.SIGUSR1`, `process.SIGUSR2`, `process.SIGTERM` integer constants replaced by a global `Signal` enum (`Signal.Hup`, `Signal.Int`, `Signal.Quit`, `Signal.Kill`, `Signal.Usr1`, `Signal.Usr2`, `Signal.Term`). The old constants still work (deprecated, SA0830). `process.signal(handle, sig)` now accepts both `Signal` enum members and raw integers.
- **analysis**: New diagnostic SA0830 ("Deprecated built-in member"), Warning severity, Category: Deprecations.
- **runtime**: New capability flag `StashCapabilities.Shell` for shell/REPL-only built-ins.

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
