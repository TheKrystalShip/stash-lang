# Bare Command Execution — Shell Mode for the REPL

> **Status:** Design (backlog) — not yet approved for implementation
> **Created:** 2026-04-29
> **Author:** Spec Architect (with user)
> **Companion:** [`Can Stash Become a Shell? — Architectural Analysis.md`](Can%20Stash%20Become%20a%20Shell%20%E2%80%94%20Architectural%20Analysis.md)

---

## 1. Purpose

Allow Stash to be used as an interactive login shell by accepting **bare command invocations** (e.g. `ls -la`, `git status | head -5`) directly at the REPL prompt, without requiring the `$(…)` wrapper. Scripts (`.stash` files invoked non-interactively) are **untouched** — they continue to use the existing language with `$(…)` for command execution.

This is **Approach B** from the companion analysis, narrowed to REPL-only scope. The goal is to make `stash` usable as a daily shell while keeping the language pure and the implementation surface area bounded.

## 2. Non-Goals (v1)

The following are explicitly **out of scope** for this spec. Each could become a follow-up spec.

- **Bare commands in `.stash` script files.** Scripts continue to require `$(…)`.
- **Job control.** No `&`, `bg`, `fg`, `jobs`, `Ctrl+Z`, no process groups, no terminal-ownership transfer.
- ~~**Tab completion** for paths, commands, or Stash symbols.~~ Shipped
- ~~**Custom prompt** (`PS1`-equivalent). Hardcoded prompt remains.~~ Shipped - see [Prompt — Customizing the REPL Prompt.md](docs/Prompt%20—%20Customizing%20the%20REPL%20Prompt.md)
- ~~**Persistent history file.**~~ Shipped — see [Persistent REPL History — File Storage and `history` Built-in](.kanban/4-done/Persistent%20REPL%20History%20%E2%80%94%20File%20Storage%20and%20history%20Built-in.md).
- **Heredocs** (`<<EOF`).
- **Process substitution** (`<(cmd)`, `>(cmd)`).
- **Backtick command substitution** (`` `cmd` ``).
- **Aliases / `alias` built-in.**
- **`source` / `.` for in-place script loading.**
- **`umask`, `pushd`/`popd`, `which`/`type`, `history`, `export`/`unset` built-ins.** (Only `cd`, `pwd`, `exit`/`quit` ship as sugar — see §11. Future shell built-ins will follow the same sugar-over-stdlib pattern.)
- **`$VAR` bash-style env interpolation.** Only Stash's `${expr}` is supported.
- **Bidirectional Stash↔command pipe mixing** (see §13 for the planned future-work design).
- **Windows runtime support.** Spec is Windows-aware (PATHEXT, `\` paths, `%USERPROFILE%`) but only Linux + macOS are required to ship and pass CI in v1.

## 3. Activation

Shell mode is **opt-in** in v1 and gated behind one of:

| Trigger                  | Effect                                     |
| ------------------------ | ------------------------------------------ |
| `stash --shell` CLI flag | Enable shell mode for this REPL session    |
| `STASH_SHELL=1` env var  | Enable shell mode for this REPL session    |
| RC file (when present)   | Implicitly enabled if any RC file is found |

Shell mode is **never active** when:

- A script file is being interpreted (`stash myfile.stash`).
- Stash is being used embedded (Stash.Bytecode hosted by another process).
- `stash --no-shell` is passed (overrides RC-based auto-enable).

> **Decision:** Experimental flag in v1; promotes to stable-default in a later release after real-world feedback.
> **Alternatives rejected:** (a) on-by-default — too risky for a feature this invasive without field testing; (b) separate `stash-sh` binary — splits the user community and complicates packaging.

## 4. The Disambiguation Rule

This is the central semantic rule. Every REPL line is classified into exactly one of: **Stash mode**, **shell mode**, or **stash-mode-with-error** (treated as Stash, will produce a normal undefined-identifier error).

### 4.1 Decision algorithm

After tokenization of the line, look at the first **non-trivia** token:

| First token category                                                                                              | Mode                                                           |
| ----------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------- |
| Any Stash keyword (`let`, `const`, `if`, `while`, `for`, `fn`, `struct`, `import`, `match`, `try`, `defer`, etc.) | **Stash mode**                                                 |
| Any literal (number, string, `true`, `false`, `null`)                                                             | **Stash mode**                                                 |
| Any opening delimiter (`(`, `[`, `{`)                                                                             | **Stash mode**                                                 |
| Any operator that can lead an expression (`-`, `+`, `!` followed by Stash symbol — see §4.4)                      | **Stash mode**                                                 |
| `$(…)`, `$>(…)`, `$!(…)`, `$!>(…)` command literals                                                               | **Stash mode** (existing behavior)                             |
| `\` (backslash) followed by an identifier or path-like token                                                      | **Shell mode (forced)** — see §4.2                             |
| `!` (bang) followed by an identifier or path-like token, where the identifier is NOT a declared Stash symbol      | **Shell mode (strict)** — see §4.3                             |
| Token containing `/` (POSIX) or starting with `./`, `../`, `~/`, or beginning with `\` followed by another char   | **Shell mode** (path-like — first token is an executable path) |
| Bare identifier `foo`                                                                                             | See §4.4                                                       |

### 4.2 The `\` escape prefix

`\foo` always forces **shell mode** for the line, bypassing Stash symbol resolution. This is the unambiguous escape hatch when a Stash binding shadows a binary the user wants to invoke.

```stash
const ls = "hello"
ls           // Stash mode → prints "hello"
\ls -la      // Shell mode → invokes /usr/bin/ls
```

The `\` is consumed by the line classifier and not passed to the command. `\` at end-of-line remains the line-continuation marker (§9); the two cannot collide because escape `\` must be followed immediately by an identifier/path character with no intervening whitespace or newline.

### 4.3 The `!` strict prefix

`!foo` is the bare-command equivalent of the existing `$!(…)` strict syntax: the command runs, and a non-zero exit code raises `CommandError`.

`!\foo` combines strict + force-PATH (when a Stash binding shadows the binary the user wants to invoke strictly). The reverse ordering `\!foo` is **not** supported — `!` must come first if both are present.

`!ident` is ambiguous with Stash's logical-not operator. The disambiguation: **if `ident` is a declared Stash symbol → logical-not; else if PATH-resolvable → strict bare command; else fall through to Stash**, which will produce a normal undefined-identifier error from logical-not on the unknown symbol. This rule is internally consistent with §4.4.

### 4.4 Bare identifier as first token

When the first token is a bare identifier `foo`:

1. **If `foo` is a Stash keyword** → Stash mode. (Already handled in the table above; listed here for completeness.)
2. **Peek the next token:**
   - If it is one of `=`, `(`, `[`, `.`, `+=`, `-=`, `*=`, `/=`, `%=`, `**=`, `&&=`, `||=`, `??=`, `?.`, `?:`, `??` → **Stash mode** (assignment, call, index, member access, compound assignment, optional chaining).
   - If end-of-input or `;` → **Stash mode** (bare identifier expression — looks up the symbol).
3. **Else** (next token is whitespace + something that looks like another word/flag/string/glob):
   - If `foo` resolves as a currently declared Stash symbol (any scope visible at the REPL top level) → **Stash mode**. The line will then likely produce a parse or type error, which is the correct behavior — declared symbols always win, and the user can use `\foo` to escape.
   - Else if `foo` resolves on `PATH` (or is a shell built-in: `cd`, `pwd`, `exit`, `quit`) → **Shell mode**.
   - Else → **Stash mode** with the expectation that it will produce an undefined-identifier error. The error message should hint: _"Unknown identifier 'foo'. If this is a command, ensure it is on PATH or use `\foo` to invoke it explicitly."_

### 4.5 Why "Stash symbols always win"

> **Decision:** Declared Stash symbols always shadow PATH executables; users must use `\cmd` to invoke a shadowed binary.
>
> **Alternatives rejected:**
>
> - _PATH wins (commands shadow symbols):_ unsafe — a stray `let ls = "..."` becomes a silent semantic surprise next time the user runs `ls`. The shadow is invisible.
> - _Context-dependent:_ impossible to specify rigorously; produces inconsistent behavior between shell and script mode.
>
> **Rationale:** Symbol resolution is uniform between REPL shell mode and script mode. Stash is a programming language first; the shell facade must not corrupt that. The `\` prefix gives a single-character escape that is intuitive for shell users (matching the shell tradition of `\` as "treat the next thing literally").
>
> **Risk:** Heavy shell users who declare lots of variables may be annoyed by needing `\` for shadowed commands. Mitigated by the fact that most variable names (`x`, `result`, `count`, `cfg`) don't clash with PATH binaries.

## 5. Lexing & Parsing

### 5.1 Architecture

A new component, **`ShellLineClassifier`** (in `Stash.Cli/`), runs **between** the LineEditor and the Lexer. Its job is to decide whether to:

- Hand the line to the existing Lexer + Parser (Stash mode), or
- Hand the line to a new **`ShellLineLexer`** + **`ShellCommandParser`** (shell mode).

This keeps `Stash.Core/Lexing/Lexer.cs` and `Stash.Core/Parsing/Parser.cs` **untouched**. All shell-mode logic lives in `Stash.Cli/`.

### 5.2 Classification flow

```
REPL line (string)
   │
   ▼
ShellLineClassifier.Classify(line, replSymbolTable)
   │
   ├── peek-tokenize the first token (lightweight scan, no full lex)
   │
   ├── apply §4 rules to decide: Stash | Shell-Normal | Shell-Strict | Shell-Forced
   │
   ▼
   ┌─────────────────┬─────────────────────────────────────────────┐
   │ Stash           │ existing Lexer.ScanTokens + Parser          │
   │ Shell-*         │ ShellLineLexer + ShellCommandParser         │
   └─────────────────┴─────────────────────────────────────────────┘
```

### 5.3 The peek-tokenizer

A small dedicated scanner reads only enough characters to:

1. Skip leading whitespace.
2. Recognize prefix characters: `\`, `!`, `$(`, `$>(`, `$!(`, `$!>(`.
3. Capture the first identifier/keyword (alphanumeric + `_` characters), OR detect a path-like first token (`/`, `./`, `../`, `~/`).
4. Skip whitespace and capture the _kind_ of the next token (operator class, alphanumeric, EOL).

This is **not** the full Stash lexer. It is intentionally minimal to keep classification fast (well under 1ms even for long lines) and to avoid throwing parse errors during classification.

### 5.4 Symbol-table lookup at classification time

The classifier needs read-only access to the REPL's currently declared symbols. The VM exposes a method:

```csharp
public bool TryLookupReplGlobal(string name, out StashValue value);
```

This already exists for REPL state restoration; we expose a name-only variant that does not materialize the value:

```csharp
public bool HasReplGlobal(string name);
```

Symbols include user-declared `let`/`const`/`fn`/`struct`/`enum` names AND all stdlib namespace names (`fs`, `path`, `env`, etc.) — so `fs ...` would never accidentally invoke a binary named `fs`.

### 5.5 PATH resolution and caching

A new component **`PathExecutableCache`** (in `Stash.Cli/`):

- On REPL start, splits `PATH` and lazily indexes executable filenames per directory.
- `IsExecutable(name)` returns `true` if `name` exists as an executable in any PATH directory.
- **Cache invalidation:**
  - When a `cd` shell built-in runs (cwd-relative executables may differ).
  - When `env.set("PATH", …)` is called.
  - When the cache age exceeds a TTL (default: 60 seconds, to catch external `PATH` changes).
- **Cross-platform:**
  - POSIX: file is "executable" if the user has `+x` on it.
  - Windows: file is executable if its extension matches `PATHEXT` (`.EXE;.BAT;.CMD;.COM;…`).
- **Path-containing first tokens** (`/usr/bin/foo`, `./script`, `~/bin/x`) skip the cache entirely — they're treated as shell mode based on syntax alone, and the existence/executability check happens at command run time.

### 5.6 Shell-mode line lexer

`ShellLineLexer` produces a `ShellCommandLine` AST:

```
ShellCommandLine
  ├── IsStrict: bool                  // true if `!` prefix
  ├── IsForced: bool                  // true if `\` prefix
  ├── Stages: List<ShellStage>        // pipe-separated
  └── Redirects: List<RedirectClause> // applied to last stage

ShellStage
  ├── Program: string                 // command name (or path)
  └── RawArgs: string                 // rest-of-stage, raw text

RedirectClause
  ├── Stream: Stdout | Stderr | Both
  ├── Append: bool
  └── Target: string                  // raw text (resolved later)
```

The lexer:

- Splits the line on **top-level** `|` (not inside quotes).
- For each stage, splits off **trailing** redirects (`>`, `>>`, `2>`, `2>>`, `&>`, `&>>`).
- Captures the program name (first whitespace-separated token) and stores everything else as a single `RawArgs` string.
- Preserves quotes, `${…}` interpolation markers, and globs as raw text.
- Handles trailing `\` (line continuation) and trailing `|` (pipe continuation) — see §9.

> **Decision:** Rest-of-line capture for arguments. The runtime expander (§6) handles interpolation, quoting, and globbing.
>
> **Alternative rejected:** Word-by-word `ArgWord` tokens. While this would produce a richer AST for tooling, the tooling impact is minimal in REPL-only mode (no LSP, no formatter for shell lines), and word-by-word lexing is significantly more complex (POSIX vs bash quoting, escape handling, etc.).

## 6. Argument Expansion Pipeline

When a `ShellCommandLine` runs, each stage's `RawArgs` is expanded in this fixed order:

1. **Interpolation** — `${expr}` substrings are evaluated against the REPL's current scope. The expression is parsed by the regular Stash parser and compiled to a temporary chunk. Result is stringified via `RuntimeOps.Stringify`. (`$ident` is **not** recognized — only `${…}`. See §4.5 for rationale.)
2. **Brace expansion** — `{a,b,c}` patterns expand to multiple words: `cp file.{txt,bak}` → `cp file.txt file.bak`. Cross-product when multiple braces appear: `{a,b}-{1,2}` → `a-1 a-2 b-1 b-2`. Quoted braces are not expanded.
3. **Tilde expansion** — Leading `~/` and bare `~` expand to the user's home directory. `~user/path` is NOT supported in v1 (call out as future work).
4. **Word splitting** — The resulting string is split into words on unquoted whitespace. Quoted regions (single or double) become single words.
5. **Glob expansion** — Each unquoted word containing `*`, `?`, `[…]`, or `**` is matched against the filesystem.
   - **Dotfiles excluded by default.** `*` does not match leading-dot filenames.
   - **No-match behavior: throw `CommandError`** with message `"glob pattern '<pat>' did not match any files"`. (Zsh-style, safer than bash's silent pass-through.)
   - **`**` is recursive\*\* across directory separators.
   - **Quoted patterns are not expanded** (`echo '*.txt'` passes `*.txt` literally).

> **Decision (glob no-match):** Throw rather than pass-through. `rm *.tmp` failing loudly when no `.tmp` files exist is safer than `rm` running with literal `*.tmp` and possibly trying to delete a file actually named `*.tmp`.
>
> **Risk:** Some users may want bash-style pass-through. Mitigated by future-work option to add a `set -o nullglob`-equivalent setting if requested.

> **Decision (interpolation):** `${expr}` only, no `$VAR`.
>
> **Alternatives rejected:** `$VAR` for env vars feels familiar but conflicts visually with `$(…)`; `${env.HOME}` reading env via the `env` namespace is explicit and consistent with the rest of Stash.

> **Decision (quoting):** Both `"…"` and `'…'` follow current Stash string semantics — both interpolate `${…}` — and the user must escape `\$` to write a literal `$`.
>
> **Alternative rejected:** Bash-style (`'…'` literal). Diverging from Stash's string rules just inside shell mode would create two mental models for one syntax. The `\$` escape is a small, well-known cost.

### 6.1 Auto-glob in `$(…)` — intentional breaking change

Per the user's decision, **glob auto-expansion applies in both bare commands and `$(…)`** for consistency. Today, `$(rm *.tmp)` passes `*.tmp` literally to `rm`. After this spec ships, it will glob.

**Migration path:**

- Document this prominently in the changelog and migration guide.
- The fix for any script relying on the old behavior is to quote: `$(rm "*.tmp")`.
- Static analyzer rule (proposed): `SA0820 — Unquoted glob pattern in command literal`. Warns when `$(…)` content contains an unquoted `*`, `?`, or `[`. Suppress with `// stash:ignore[SA0820]` when intentional.

## 7. Streaming Pipes

Today, `$(a) | $(b) | $(c)` reads `a`'s entire stdout into memory, then runs `b`, etc. (Sort of — `ExecutePipelineStreaming` already starts processes concurrently, but stdin is fed via `StandardInput.Write()` of the previously-captured output. Not true streaming.)

This spec migrates **both bare commands and `$(…)`** to **OS-level streaming pipes**.

### 7.1 Implementation

In `Stash.Bytecode/VM/VirtualMachine.Process.cs`:

- Replace `ExecPipelineStreaming` with `ExecPipelineOSPipes`.
- For an N-stage pipeline:
  - Stage 0: `RedirectStandardOutput = true`, stdin inherits (or is `Null` if from a non-terminal source).
  - Stages 1..N-1: `RedirectStandardInput = true`, `RedirectStandardOutput = true`.
  - Stage N (last): `RedirectStandardInput = true`, `RedirectStandardOutput` depends on whether output is captured (`$(…)`, bare-passthrough, or piped further).
- Connect stages by spawning each process with its input/output streams wired to the previous/next stage. Use `Process.StandardOutput.BaseStream.CopyToAsync(nextStdin)` running on background tasks.
- **SIGPIPE handling:** when a downstream consumer (e.g. `head -5`) closes its stdin early, upstream processes will receive `SIGPIPE` (POSIX) or a broken-pipe IOException (.NET). The pipe runner catches `IOException` from `CopyToAsync` and gracefully shuts down upstream stages by sending SIGTERM after a short grace period.
- **Exit codes:** all stages' exit codes are collected. The pipeline's overall exit code is the last stage's (matching bash's default `pipefail`-off behavior). Strict mode (`!cmd1 | cmd2`) throws if **any** stage has non-zero exit.

### 7.2 Captured vs passthrough

When the pipeline is the result of `$(…)`:

- Last stage's stdout is captured into a `CommandResult` struct.
- Other stages stream to `/dev/null` for stdout (already wired to next stage's stdin).

When the pipeline is a bare command line in shell mode:

- Last stage's stdout streams **directly to the terminal** (passthrough).
- Stderr of every stage streams to the terminal's stderr.
- No `CommandResult` is produced; only `process.lastExitCode` is updated (§8).

### 7.3 Compatibility

Existing scripts using `$(…)` get streaming for free, mostly transparent:

- **Compatible:** small/medium outputs work identically.
- **Compatible:** `$(big_cmd)` followed by usage of stdout — still buffers the _final_ output (only intermediate stages stream).
- **Behavioral change:** `$(tail -f log | grep error)` now actually works. Previously it hung forever waiting for `tail -f` to finish.
- **Behavioral change:** A pipeline where downstream closes early no longer waits for upstream to finish writing — upstream is killed gracefully.

## 8. REPL Display and Exit Codes

### 8.1 Bare-command display

When a shell-mode line runs, the REPL:

- Streams stdout and stderr of the final stage **directly to the terminal**.
- Does **not** print a `CommandResult` struct value.
- Does **not** print a trailing newline beyond what the command itself produces.
- Updates `process.lastExitCode` (Stash global) with the final stage's exit code.

### 8.2 `$?` REPL sugar

Inside the REPL only, the special token `$?` is recognized as syntactic sugar for `process.lastExitCode`. It is desugared by the line preprocessor before lexing (similar to how Stash already handles a few REPL-only conveniences).

```
$ ls /nonexistent
ls: cannot access '/nonexistent': No such file or directory
$ $?
2
$ if $? != 0 { echo "failed" }
failed
```

> **Decision:** No `_` (last-stdout) variable for bare commands. To capture stdout, users use `let x = $(cmd)`. This avoids doubling memory usage for every passthrough command and keeps the bare-command code path lean.
>
> **Alternative rejected:** Always-capture `_` would let users post-process command output without re-running, but at the cost of memory pressure for every command and complicates streaming (we'd need to tee output to memory).

### 8.3 Exit code behavior

- **Non-strict bare commands** (default): non-zero exit codes are silent — only `process.lastExitCode` is updated.
- **Strict bare commands** (`!cmd`): non-zero exit codes raise `CommandError` (existing error type, same shape as `$!(…)`).
- **Process spawn failures** (e.g. command not found, permission denied): always raise `CommandError` regardless of strictness, with message `"command not found: <name>"` or `"permission denied: <path>"`.

## 9. Multi-line Input

Today the REPL evaluates each line independently. This spec adds multi-line input support for both Stash and shell modes.

### 9.1 Continuation triggers

The REPL keeps reading additional lines (showing a `... ` continuation prompt) when:

| Trigger                                            | Mode                                        | Behavior                                |
| -------------------------------------------------- | ------------------------------------------- | --------------------------------------- | -------------------------------------------------- |
| Trailing `\` on the input line                     | Both                                        | Strip the `\`, append a space, continue |
| Unbalanced `(`, `[`, `{` or unterminated `"` / `'` | Stash                                       | Continue until balanced                 |
| Trailing `                                         | ` (after stripping whitespace and comments) | Shell                                   | Continue; the next line is the next pipeline stage |

Empty lines do **not** terminate continuation in v1 — only one of the trigger conditions resolving will end it. (Avoids the bash quirk of empty lines in multi-line strings being awkward.)

### 9.2 Implementation

- A new `MultiLineReader` wraps `LineEditor`. It returns a **complete logical line** rather than a single physical line.
- For Stash mode, the lexer is run incrementally; if the resulting token stream has unbalanced delimiters or open strings, more input is requested. (This is the canonical approach used by GHCi, IPython, etc.)
- For shell mode, the `ShellLineClassifier` is extended with `IsIncomplete()` that returns true when the line ends with `\` (after whitespace strip) or `|` (after stripping trailing comments — but per §10 there are no comments).
- The continuation prompt is configurable later (default: `... `).

## 10. Comments in Shell Mode

> **Decision:** No `#` comments in shell-mode lines. `echo # hello` passes `#` and `hello` as literal arguments to `echo`.
>
> **Rationale:** Stash uses `//` and `/* */` for comments. Adding `#` only inside shell mode creates an inconsistent syntax. Users who want to comment can simply not type the line, or wrap it in a `//` Stash comment on a separate line.
>
> **Risk:** Bash users will be surprised. Documented prominently. If feedback shows this is a real pain point, revisit in a follow-up.

## 11. Shell Built-ins as Stdlib Sugar

The shell-mode runner does **not** maintain a separate set of "shell built-in" functions. Instead, the names `cd`, `pwd`, `exit`, and `quit` are **syntactic sugar** that desugar to existing or new stdlib calls in the `process` namespace. The same primitives are therefore available to regular Stash scripts, and the shell mode is a thin presentation layer over real language features.

This is the same philosophy used for `$?` (sugar over `process.lastExitCode`).

### 11.1 Stdlib additions to the `process` namespace

A small **directory stack** is added to `process`. The stack is the single source of truth for the interpreter's working directory; `process.chdir` pushes onto it, the top is always the actual current directory, and pops walk back through history.

| Function                              | Behavior                                                                                                                                                              |
| ------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `process.chdir(dir: string)`          | Validate `dir` exists and is accessible; if so, push it onto the directory stack and set `Environment.CurrentDirectory`. Atomic — fails before mutating on bad input. |
| `process.cwd() -> string`             | Return the top of the directory stack (the current working directory). Existing function; semantics unchanged externally.                                             |
| `process.popDir() -> string`          | Pop the top of the stack, set the new top as cwd, return the popped value. Throws `CommandError("directory stack is at root")` if the stack has only one entry.       |
| `process.dirStack() -> array<string>` | Return a copy of the stack, oldest-first.                                                                                                                             |
| `process.dirStackDepth() -> int`      | Convenience for `process.dirStack().length`.                                                                                                                          |
| `process.exit(code: int = 0)`         | Terminate the interpreter with `code`. Defer-aware, catch-immune (see §11.3). Replaces no existing API.                                                               |

**Stack rules:**

- Initial state on interpreter startup: stack contains a single entry — the cwd inherited from the parent process.
- The stack is **scoped to the interpreter session**. It is not persisted across REPL restarts and is not shared across child processes.
- The stack is **capped at 256 entries**. When `process.chdir` would push to a full stack, the **eldest entry (index 0) is dropped** to make room. This keeps long shell sessions from growing unbounded while preserving recent history.
- `process.chdir` validates atomically — if the target is missing, unreadable, or not a directory, it throws `CommandError("no such directory: <path>")` (or platform-equivalent) and the stack is unchanged.
- `process.popDir` never empties the stack. The minimum stack size is 1 (the initial cwd).
- The existing `VMContext.PreviousWorkingDirectory` field from earlier drafts is **removed** — it is fully subsumed by the stack.

### 11.2 Desugaring of shell-mode names

When the shell runner encounters a stage whose `Program` (after the §6 expansion of trailing whitespace) is `cd`, `pwd`, `exit`, or `quit`, and the stage is **not piped or redirected** (these names only desugar at the top level of a line — `pwd | grep foo` desugars `pwd` first, then pipes its stdout), the runner constructs a Stash `CallExpr` AST and routes it through the existing compiler + VM.

| Shell input                 | Desugared Stash call                                                           |
| --------------------------- | ------------------------------------------------------------------------------ |
| `cd <args>` (1 arg)         | `process.chdir(<expanded-arg>)`                                                |
| `cd` (no args)              | `process.chdir(env.get("HOME"))` (Linux/macOS); `env.get("USERPROFILE")` (Win) |
| `cd -` (single literal `-`) | `process.popDir()` followed by an `io.println(process.cwd())`                  |
| `pwd` (no args)             | `io.println(process.cwd())`                                                    |
| `exit <args>` (≤1 arg)      | `process.exit(int.parse(<expanded-arg>))` (no arg → `process.exit(0)`)         |
| `quit <args>`               | Alias of `exit` — same desugaring.                                             |

**Argument handling:**

- Each argument is run through the full §6 expansion pipeline (interpolation, brace, tilde, word split, glob) **before** desugaring. This means `cd ${env.get("PROJECTS")}` works, `cd ~/code` works, and `cd $(git root)` works (command substitution is already a Stash expression and gets expanded normally).
- **Arity is checked at the sugar layer**, before desugaring, with shell-style messages:
  - `cd a b c` → `CommandError("cd: too many arguments")`
  - `exit 1 2` → `CommandError("exit: too many arguments")`
  - `pwd anything` → `CommandError("pwd: too many arguments")`
- For `exit`/`quit`, after expansion the single argument is parsed as an integer via `int.parse`. A non-integer raises `CommandError("exit: numeric argument required")`. This matches bash's behavior.
- `cd -` is recognized as the literal single argument `-` after expansion. `cd "-"` and `cd \-` (escaped) also work as the same case (consistent with how shells handle this).

### 11.3 `process.exit()` semantics

> **Decision:** `process.exit(code)` is **defer-aware and catch-immune.** It runs all pending `defer` blocks on the way out (in reverse declaration order, walking up the call stack), but is **not** intercepted by `try/catch`.
>
> **Alternatives rejected:**
>
> - _Uncatchable / immediate_ (POSIX `_exit`): would skip `defer`, breaking guarantees scripts rely on for resource cleanup.
> - _Fully catchable_: makes `exit` unreliable; a misplaced `try/catch` could swallow it.

Implementation: `process.exit(code)` raises a special `ExitException` (in `Stash.Bytecode/Runtime/`) carrying the exit code. The VM's frame-unwind path:

- Treats `ExitException` like any other exception for the purpose of running `defer` blocks (existing `RunFrameDefers` logic walks the frame stack and runs deferred closures).
- **Skips** the `try/catch` matching path entirely — no catch clause matches `ExitException`. The exception propagates past every `try` block, runs all defers, and is finally caught at the interpreter's top level.
- At the top level, the interpreter terminates with the carried status code.

If a `defer` block itself throws while unwinding from `ExitException`, the secondary error is recorded in `ExitException.SuppressedErrors` (mirroring the existing `RuntimeError.SuppressedErrors` mechanism) and the original exit code is preserved.

In the **REPL**, `process.exit` walks defers, prints any suppressed errors, and exits the host process — same behavior as in a script. There is no "soft exit" that returns to the prompt.

### 11.4 Why dispatch through the AST

> **Decision:** AST-level desugaring (build a `CallExpr` and feed it to the existing compiler/VM).
>
> **Alternatives rejected:**
>
> - _String-level rewrite_: brittle around quoting and complex args.
> - _Direct VM call_: introduces a second code path for `cd`/`pwd`/`exit` and bypasses normal error reporting, defer handling, and stack frames.
>
> **Benefit:** error messages, stack traces, and `defer` behavior for `cd`/`exit` look identical to direct stdlib calls. The shell runner is just a different parser front-end; the runtime is unified.

## 12. RC File

### 12.1 Path resolution

On REPL start, attempt to load (in order, first match wins):

1. `$XDG_CONFIG_HOME/stash/init.stash` (if `XDG_CONFIG_HOME` is set)
2. `~/.config/stash/init.stash`
3. `~/.stashrc`

If none exist, no RC file is loaded. If multiple exist, only the first is loaded.

### 12.2 Processing

The RC file is **fed line-by-line through the REPL evaluator**, including the `ShellLineClassifier`. This means:

- Shell-mode lines work in the RC file (e.g. `cd ~/projects`, `export PATH=...:$PATH` if/when `export` is added in a follow-up).
- Stash declarations work and persist into the REPL (e.g. `const PROJECTS = "~/projects"`).
- Errors in the RC file print a warning but do not prevent the REPL from starting.
- Multi-line continuations (§9) work as in interactive use.

> **Decision:** RC fed through REPL evaluator, not as a normal script.
>
> **Alternative rejected:** Plain Stash script — would prevent users from putting `cd ~/projects` in their RC.
>
> **Alternative rejected:** Script + small allowed shell built-ins — adds inconsistent semantics. Either the RC is "the same as typing at the prompt" or it's not.

### 12.3 RC file and shell-mode activation

The presence of an RC file at any of the candidate paths **implicitly enables shell mode** for the REPL session, as if `--shell` were passed. Users who want their RC sourced but shell mode disabled can pass `--no-shell` (overrides RC-based activation).

## 13. Future Work: Pipe Mixing (Bidirectional Stash↔Shell)

**Out of scope for v1**, but documented here so the v1 implementation does not foreclose this design.

### 13.1 Vision

Allow pipelines that mix bare commands and Stash expressions:

```stash
ls *.log | filter(name => name.contains("error")) | wc -l
arr.range(1, 100) | xargs -n 1 echo
$(curl example.com) | json.parse | filter(item => item.active)
```

### 13.2 Design sketch (for future spec)

- **Command → Stash:** the command's stdout is converted to an iterable. Default split on `\n`, lazily yielding each line as a string. The Stash function/lambda is invoked per item or as a single call receiving the iterable.
- **Stash → command:** the Stash value is converted to stdin lines:
  - `string` → written as-is.
  - `array<string>` → joined with `\n`.
  - `iterable` → each element stringified and written as a line.
  - other types → error.
- **Type tagging:** use an `IPipeable` protocol (parallel to existing `IVMIterable`) so domain types can opt into pipe behavior.

### 13.3 Constraints v1 must respect

- The bare-command pipe runner must not assume both ends are external processes — leave a hook in `ExecPipelineOSPipes` where a "Stash-side stage" could be inserted.
- The `ShellCommandLine` AST must not be the _only_ representation for a pipeline — when v2 lands, pipelines will mix `ShellStage` and `StashStage` nodes.

## 14. Cross-Platform Considerations

### 14.1 v1 platform support

| Platform | v1 support  | Notes                                                                                                                                    |
| -------- | ----------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| Linux    | ✅ Required | Full functionality, CI gates                                                                                                             |
| macOS    | ✅ Required | Full functionality, CI gates                                                                                                             |
| Windows  | ⚠️ Disabled | Spec is Windows-aware, code paths exist but `--shell` errors with `"shell mode not yet supported on Windows"`; enablement is a follow-up |

### 14.2 Windows-aware design points

The spec keeps these Windows specifics in scope so the follow-up enablement is mechanical:

- **PATHEXT honored** by `PathExecutableCache`.
- **`\` as a path separator** detected by the path-like classifier (e.g. `.\foo.exe` triggers shell mode).
- **Tilde expansion** uses `%USERPROFILE%` when running on Windows.
- **Glob expansion** is case-insensitive on Windows by default (matches NTFS).
- **`cd`** uses Windows path normalization (`Path.GetFullPath`).
- **Streaming pipes** rely on .NET's `Process` which abstracts most platform differences. SIGPIPE handling becomes broken-pipe IOException handling.

## 15. Error Handling

| Failure mode                          | Error          | Message                                                                  |
| ------------------------------------- | -------------- | ------------------------------------------------------------------------ |
| Command not found on PATH             | `CommandError` | `"command not found: <name>"`                                            |
| Permission denied                     | `CommandError` | `"permission denied: <path>"`                                            |
| Glob with no matches                  | `CommandError` | `"glob pattern '<pat>' did not match any files"`                         |
| Strict command (`!cmd`) non-zero exit | `CommandError` | `"command exited with status <n>: <cmdline>"` (existing message)         |
| `cd` to nonexistent directory         | `CommandError` | `"no such directory: <path>"` (raised by `process.chdir`)                |
| `cd -` when stack is at root          | `CommandError` | `"directory stack is at root"` (raised by `process.popDir`)              |
| `cd`/`pwd`/`exit` with too many args  | `CommandError` | `"<name>: too many arguments"` (raised by sugar layer)                   |
| `exit` with non-integer arg           | `CommandError` | `"exit: numeric argument required"` (raised by sugar layer)              |
| Interpolation expression error        | propagated     | The expression's own error (parse/runtime), with a wrapping context note |
| Pipeline stage spawn failure          | `CommandError` | `"pipeline stage <i> failed to spawn: <inner>"`                          |

`CommandError` already exists in `StashErrorTypes.CommandError`. No new error types are introduced.

## 16. Stash.Tap Integration

Bare commands at the REPL run **outside** the test framework, but to allow integration testing of the shell-mode runtime:

- `Stash.Tests/Cli/ShellModeTests.cs` — spawns the actual `stash --shell` binary as a subprocess, feeds it lines, asserts on stdout/stderr/exit code.
- `Stash.Tests/Cli/ShellLineClassifierTests.cs` — pure unit tests against the classifier with a stubbed symbol table and PATH cache.
- `Stash.Tests/Cli/ShellArgExpansionTests.cs` — unit tests for the §6 expansion pipeline (interpolation, brace, tilde, word split, glob) with an in-memory filesystem.
- `Stash.Tests/Bytecode/StreamingPipeTests.cs` — `$(yes | head -5)` style tests that hang under buffered semantics.

## 17. Static Analysis

A small number of new diagnostic descriptors:

| Code   | Severity | Title                                                    |
| ------ | -------- | -------------------------------------------------------- |
| SA0820 | Warning  | Unquoted glob pattern in `$(…)` command literal          |
| SA0821 | Info     | Bare identifier may shadow PATH executable in shell mode |

SA0821 is **REPL-only** — emitted by the classifier when it sees a declared symbol that also resolves on PATH, to surface the shadow. The user can suppress it after a one-time acknowledgment. Implementation: emit through `Stash.Cli`'s diagnostic channel, not through the analysis engine (since scripts aren't affected).

## 18. Files Changed / Added

### New files

- `Stash.Cli/Shell/ShellLineClassifier.cs` — line classification logic (§5)
- `Stash.Cli/Shell/PeekTokenizer.cs` — minimal first-token scanner (§5.3)
- `Stash.Cli/Shell/PathExecutableCache.cs` — PATH lookup + caching (§5.5)
- `Stash.Cli/Shell/ShellLineLexer.cs` — shell-mode line tokenizer (§5.6)
- `Stash.Cli/Shell/ShellCommandParser.cs` — produces `ShellCommandLine` AST
- `Stash.Cli/Shell/ShellCommandLine.cs` — AST types
- `Stash.Cli/Shell/ShellRunner.cs` — orchestrates expansion + dispatch + pipe execution
- `Stash.Cli/Shell/ArgExpander.cs` — §6 expansion pipeline
- `Stash.Cli/Shell/GlobExpander.cs` — globbing with the §6 rules
- `Stash.Cli/Shell/BraceExpander.cs` — `{a,b,c}` expansion
- `Stash.Cli/Shell/ShellSugarDesugarer.cs` — recognizes `cd`/`pwd`/`exit`/`quit` and rewrites the stage to a Stash `CallExpr` AST (§11.2)
- `Stash.Bytecode/Runtime/ExitException.cs` — defer-aware, catch-immune exception used by `process.exit` (§11.3)
- `Stash.Cli/Shell/RcFileLoader.cs` — §12 RC loading
- `Stash.Cli/MultiLineReader.cs` — §9 multi-line wrapper around `LineEditor`
- `Stash.Tests/Cli/ShellModeTests.cs`
- `Stash.Tests/Cli/ShellLineClassifierTests.cs`
- `Stash.Tests/Cli/ShellArgExpansionTests.cs`
- `Stash.Tests/Bytecode/StreamingPipeTests.cs`

### Modified files

- `Stash.Cli/Program.cs` — `RunRepl` integrates `ShellLineClassifier`, `MultiLineReader`, RC loader; new `--shell` / `--no-shell` flags; `STASH_SHELL` env var
- `Stash.Cli/LineEditor.cs` — supports continuation prompt
- `Stash.Bytecode/VM/VirtualMachine.Process.cs` — replace pipeline runner with `ExecPipelineOSPipes` (§7)
- `Stash.Bytecode/VM/VirtualMachine.Strings.cs` — invoke new arg-expander for `$(…)` glob auto-expansion
- `Stash.Bytecode/Runtime/VMContext.cs` — add `DirStack : List<string>` (capped at 256); expose `HasReplGlobal(string)`
- `Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs` — extend `process.chdir` to push the stack atomically; add `process.popDir`, `process.dirStack`, `process.dirStackDepth`, `process.exit`
- `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs` — top-level frame-unwind path recognizes `ExitException` and bypasses catch matching while still running defers
- `Stash.Analysis/Models/DiagnosticDescriptors.cs` — add SA0820, SA0821
- `Stash.Analysis/Visitors/SemanticValidator.cs` — emit SA0820 for unquoted globs in `CommandExpr`
- `docs/Stash — Language Specification.md` — add **REPL Shell Mode** chapter
- `docs/PKG — Package Manager CLI.md` — note `--shell` flag (or new `Shell — Interactive Shell Mode.md` doc)
- `CHANGELOG.md` — entry for shell mode + breaking change for `$(…)` auto-glob

## 19. Migration & Breaking Changes

### 19.1 `$(…)` glob auto-expansion (§6.1)

Existing scripts that pass literal glob patterns to commands via `$(…)` will see different behavior:

```stash
// Before this spec:
let p = "*.tmp"
$(rm ${p})           // rm receives literal "*.tmp"

// After this spec:
$(rm ${p})           // expands to all matching files; CommandError if none

// Migration:
$(rm "${p}")         // quoted → no expansion, literal "*.tmp"
```

`SA0820` will help users find affected lines after upgrading.

### 19.2 Streaming pipes (§7.3)

Most code is unaffected. Two observable changes:

1. `$(producer | consumer)` where `consumer` exits early no longer blocks waiting for `producer` to finish.
2. `$(tail -f log | head -1)` previously hung forever; now returns immediately.

These are bug fixes more than breaking changes, but worth calling out.

### 19.3 No other language-level breakages

The Stash language itself (lexer, parser, AST, semantics in script files) is unchanged. All shell-mode behavior is in `Stash.Cli/`.

## 20. Test Scenarios

### 20.1 Disambiguation (unit)

- `let ls = "x"; ls` → prints `x` (declared symbol wins)
- `let ls = "x"; \ls /tmp` → invokes `/usr/bin/ls /tmp` (escape)
- `let ls = "x"; ls -la` → the line classifier picks Stash (declared symbol); parser produces an error
- `git status` → shell mode (PATH)
- `git = 5` → Stash mode (assignment, lookahead)
- `git()` → Stash mode (call, lookahead) — error since `git` undeclared
- `./script.sh arg` → shell mode (path-like)
- `/usr/bin/env python` → shell mode (path-like)
- `~/bin/foo` → shell mode (path-like, after tilde expansion)
- `!foo` where `foo` is a Stash symbol → logical-not
- `!foo` where `foo` is on PATH and not a Stash symbol → strict bare command
- `!\foo` → strict + force PATH
- `\!foo` → not allowed; falls to Stash mode (parser error)
- `5 + 3` → Stash mode (literal first token)
- `if x { y }` → Stash mode (keyword)
- `(ls)` → Stash mode (`(` first token); user wrote `(ls)` as parenthesized expression

### 20.2 Argument expansion (unit)

- `cd ~` → home dir
- `cd ~/projects` → home + projects
- `cp file.{txt,bak} /tmp/` → two files
- `echo ${1 + 2}` → `3`
- `echo "${1 + 2}"` → `3` (interpolation in double quotes)
- `echo '${1 + 2}'` → `3` (interpolation in single quotes — Stash semantics)
- `echo \${literal}` → `${literal}`
- `ls *.cs` (in dir with .cs files) → expands
- `ls *.xyz` (no matches) → throws CommandError
- `ls "*.xyz"` (quoted) → passes literal
- `ls **/*.cs` → recursive
- `ls .*` → matches dotfiles when explicit

### 20.3 Streaming pipes (integration)

- `$(yes | head -5)` → returns within ms with 5 lines (would hang under buffered)
- `tail -f /tmp/log | head -1` → returns after first line, kills tail
- `seq 1000000 | wc -l` → constant memory, fast
- 3-stage pipeline with intermediate stage exiting non-zero in non-strict mode → final exit code is last stage's
- Same in strict mode (`!seq 10 | grep nope | wc -l`) → throws

### 20.4 Multi-line (integration)

- `echo foo \` <newline> `bar` → `foo bar`
- `let x = (` <newline> `  1 +` <newline> `  2)` → `x = 3`
- `cat /etc/passwd |` <newline> `grep root` → works
- Open string then continue → works

### 20.5 RC file (integration)

- `~/.stashrc` containing `cd ~/projects` → REPL starts in `~/projects`
- XDG path takes precedence over `~/.stashrc`
- Missing RC → REPL starts normally
- RC error → warning printed, REPL still starts
- `--no-shell` overrides RC-induced shell mode

### 20.6 Shell built-in sugar (and underlying stdlib)

**Sugar (shell-mode integration tests):**

- `cd /tmp` → cwd is `/tmp`; `process.dirStack()` reflects the push
- `cd` (no arg) → cwd is home
- `cd /tmp; cd /var; cd -` → cwd is `/tmp` (pop); a second `cd -` → original home
- `cd -` with stack at root → `CommandError`
- `cd /nonexistent` → `CommandError`, stack unchanged (atomic)
- `cd a b` → `CommandError("cd: too many arguments")`
- `cd ${env.get("HOME")}/projects` → expansion + chdir
- `pwd` → prints cwd
- `pwd anything` → `CommandError("pwd: too many arguments")`
- `exit 7` → REPL exits with status 7
- `exit ${1+1}` → exits with status 2
- `exit foo` → `CommandError("exit: numeric argument required")`
- `quit` → exits with status 0

**Stdlib unit tests (script-callable, no shell mode required):**

- `process.chdir("/tmp")`; `process.cwd() == "/tmp"`; `process.dirStackDepth() == 2`
- `process.popDir()` returns the popped dir and updates cwd
- 256 successive `process.chdir` calls cap the stack at 256, eldest dropped
- `process.chdir("/nonexistent")` throws; stack unchanged
- `process.exit(3)` from a script terminates with code 3 and runs `defer` blocks first
- `try { process.exit(1) } catch (e) { ... }` does **not** intercept (catch-immune); script still exits with code 1
- `defer io.println("bye")` followed by `process.exit(0)` prints "bye" before exit

### 20.7 Cross-platform

- All Linux/macOS tests in CI on both platforms
- Path-like detection on macOS handles symlinks correctly
- `~user` → not supported, friendly error message

## 21. Implementation Order (Suggested)

A reasonable ordering for the Orchestrator agent. Each phase ends in a runnable, tested state.

1. **Foundation** — `MultiLineReader` and continuation prompts (independent of shell mode; benefits Stash REPL too).
2. **Streaming pipes** — replace `ExecPipelineStreaming` with OS-pipe streaming. Test via `$(…)` only at first.
3. **Glob auto-expansion in `$(…)`** — wire `ArgExpander` + `GlobExpander` into the existing command runtime. SA0820. Migration docs.
4. **Shell-mode classifier and runner skeleton** — `ShellLineClassifier`, `PathExecutableCache`, `ShellLineLexer`, `ShellCommandParser`, `ShellRunner`. Wire into `Program.cs` behind `--shell`. Ship with bare commands working but no built-ins, no RC, no escape prefix.
5. **`\` and `!` prefixes** — escape and strict syntax.
6. **Stdlib additions** — extend `process.chdir` with stack semantics (cap 256, atomic validation); add `process.popDir`, `process.dirStack`, `process.dirStackDepth`. Add `process.exit` + `ExitException` with defer-aware, catch-immune unwind. Tested in scripts before any shell sugar exists.
7. **Shell sugar for `cd`/`pwd`/`exit`/`quit`** — `ShellSugarDesugarer` builds `CallExpr` ASTs; arity check; `cd -` → `popDir` + print.
8. **RC file** — XDG + `~/.stashrc` loading.
9. **`$?` REPL sugar.**
10. **Brace expansion.**
11. **SA0821 + classifier diagnostic channel.**
12. **Cross-platform polish + CI gates.**

## 22. Decision Log Summary

| #   | Decision                                                                                                                                                                             | Alternatives rejected                                                                                        |
| --- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------ |
| 1   | REPL-only scope (and RC file)                                                                                                                                                        | Scripts opt-in; everywhere; separate binary                                                                  |
| 2   | First-token disambiguation; declared symbols always win; `\` to escape                                                                                                               | PATH-wins; context-dependent; explicit-prefix-only                                                           |
| 3   | `\` as escape prefix; `!` as strict prefix; combine as `!\cmd`                                                                                                                       | `=` (Oil-style); `:`; `,`; `;`; `?`                                                                          |
| 4   | Rest-of-line shell capture for args                                                                                                                                                  | Word-by-word `ArgWord` tokens; strict Stash literals                                                         |
| 5   | Streaming pipes for both bare and `$(…)`                                                                                                                                             | Streaming bare-only; defer streaming                                                                         |
| 6   | Auto-glob everywhere (incl. `$(…)`)                                                                                                                                                  | Auto-glob shell-mode only; never                                                                             |
| 7   | `${expr}` only for interpolation                                                                                                                                                     | `$VAR` bash-style; mixed `$ident`+`${expr}`                                                                  |
| 8   | Stash quoting semantics + `\$` escape                                                                                                                                                | Bash-style `'…'` literal in shell mode; raw-string syntax                                                    |
| 9   | Glob no-match → throw CommandError                                                                                                                                                   | Pass-through (bash); empty result                                                                            |
| 10  | Passthrough display + `$?` only (no `_`)                                                                                                                                             | Capture-and-print; capture `_` always                                                                        |
| 11  | Silent exit codes (strict via `!cmd`)                                                                                                                                                | Strict by default; configurable                                                                              |
| 12  | No mixing pipes in v1; bidirectional in future spec                                                                                                                                  | Full bidirectional in v1; one-way only                                                                       |
| 13  | RC file fed through REPL evaluator                                                                                                                                                   | Plain script; script + small allowed built-ins                                                               |
| 14  | RC path: XDG → `~/.stashrc` fallback                                                                                                                                                 | XDG only; `~/.stashrc` only; both loaded                                                                     |
| 15  | No `#` comments in shell mode                                                                                                                                                        | `#` is comment                                                                                               |
| 16  | Shell sugar desugars to stdlib calls (`cd`→`process.chdir`, `pwd`→`io.println(process.cwd())`, `exit`/`quit`→`process.exit`); script-callable APIs are the source of truth           | Hidden interpreter built-ins; dedicated `shell.*` namespace                                                  |
| 16a | `cd`/`cd -` use a directory stack (cap 256, drop-eldest); `cd new` pushes, `cd -` pops; stack lives in `VMContext.DirStack`; exposed via `process.popDir`/`dirStack`/`dirStackDepth` | Single previous-cwd slot (bash-like swap); separate `pushd`/`popd` namespace; defer the stack to a follow-up |
| 16b | `process.exit(code)` is defer-aware and catch-immune via `ExitException`                                                                                                             | Uncatchable POSIX-style; fully catchable via `try/catch`                                                     |
| 16c | Shell sugar dispatches by building a Stash `CallExpr` AST (AST-level desugar); arity is checked at the sugar layer for shell-style errors                                            | String-level rewrite; direct VM call bypassing the compiler                                                  |
| 17  | Experimental opt-in via `--shell` / `STASH_SHELL`                                                                                                                                    | Stable on by default; behind two flags                                                                       |
| 18  | Linux + macOS only in v1, Windows-ready spec, Windows enablement later                                                                                                               | All three in v1; Windows entirely deferred                                                                   |
| 19  | Sugar names in v1: `cd`, `pwd`, `exit`/`quit` only                                                                                                                                   | Full set (export/unset/alias/which/source/umask/pushd/popd/history)                                          |
| 20  | No background jobs / job control                                                                                                                                                     | Detach-only `&`; full POSIX job control                                                                      |

---

## Open Items for Spec Sign-Off

None remaining; all branching decisions resolved with the user. Once approved, this spec moves to `1-todo/` for the Orchestrator to pick up.
