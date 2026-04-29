# Stash — Interactive Shell Mode

> **Status:** Stable (v1 — Linux and macOS)
> **Created:** April 2026
> **Purpose:** Reference for using the Stash REPL as an interactive shell, including bare command execution, argument expansion, pipelines, redirects, built-in sugar, and the RC file.
>
> **Companion documents:**
>
> - [Language Specification](Stash%20—%20Language%20Specification.md) — language syntax, type system, interpreter architecture
> - [Standard Library Reference](Stash%20—%20Standard%20Library%20Reference.md) — built-in namespace functions (including `process.*`)

---

## Table of Contents

1. [Overview](#1-overview)
2. [Line Classification](#2-line-classification)
3. [Argument Expansion Pipeline](#3-argument-expansion-pipeline)
4. [Pipelines](#4-pipelines)
5. [Redirects](#5-redirects)
6. [Shell Built-in Sugar](#6-shell-built-in-sugar)
7. [Directory Stack](#7-directory-stack)
8. [`process.exit` Semantics](#8-processexit-semantics)
9. [`$?` REPL Sugar](#9--repl-sugar)
10. [RC File](#10-rc-file)
11. [Error Reference](#11-error-reference)
12. [Static Analysis](#12-static-analysis)
13. [Cross-Platform Notes](#13-cross-platform-notes)
14. [Customizing the Prompt](#14-customizing-the-prompt)

---

## 1. Overview

Shell mode lets you use the Stash REPL as a daily interactive shell. Instead of wrapping every command in `$(…)`, you can type bare commands directly at the prompt:

```text
$ ls -la
total 48
drwxr-xr-x  8 alice alice 4096 Apr 29 10:00 .
drwxr-xr-x 32 alice alice 4096 Apr 28 09:00 ..
-rw-r--r--  1 alice alice 1234 Apr 29 09:55 deploy.stash
$ git status
On branch main
nothing to commit, working tree clean
$ git log --oneline | head -5
abc1234 feat: add archive namespace
def5678 fix: glob no-match throws correctly
```

Stash code works exactly as before — every Stash statement, expression, and `$(…)` command literal is still valid at the prompt.

### 1.1 Activating Shell Mode

Shell mode is **opt-in** in v1 and activates via any of these triggers:

| Trigger                  | Effect                                            |
| ------------------------ | ------------------------------------------------- |
| `stash --shell`          | Enable shell mode for this REPL session           |
| `STASH_SHELL=1` env var  | Enable shell mode for this REPL session           |
| RC file present          | Implicitly enables shell mode (see [§10](#10-rc-file)) |

Pass `--no-shell` to override RC-based auto-enable:

```bash
stash --no-shell    # Stash-only REPL even if ~/.stashrc exists
```

### 1.2 When Shell Mode Is Never Active

- Running a script file: `stash myscript.stash`
- Stash used embedded (hosted by another process via `Stash.Bytecode`)
- `--no-shell` flag passed

Script files **always** parse as Stash. Shell mode is a REPL-only feature.

### 1.3 Platform Support

| Platform | v1 Support  | Notes                                                                   |
| -------- | ----------- | ----------------------------------------------------------------------- |
| Linux    | ✅ Full     | All features available; CI-gated                                        |
| macOS    | ✅ Full     | All features available; CI-gated                                        |
| Windows  | ⚠️ Disabled | `--shell` prints `"shell mode not yet supported on Windows"` and falls back to Stash-only mode. Windows-aware code paths exist for a future mechanical re-enable (see [§13](#13-cross-platform-notes)). |

---

## 2. Line Classification

Every line entered at the REPL is **classified** into exactly one mode before evaluation: **Stash mode** or **shell mode**. The classifier (`ShellLineClassifier`) peeks at the first token to decide.

### 2.1 Classification Rules

| First token or prefix                                         | Mode               | Notes                                                                 |
| ------------------------------------------------------------- | ------------------ | --------------------------------------------------------------------- |
| Stash keyword (`let`, `const`, `if`, `while`, `for`, `fn`, `struct`, `import`, `match`, `try`, `defer`, etc.) | **Stash** | Always Stash |
| Literal (`42`, `"hello"`, `true`, `false`, `null`)            | **Stash**          |                                                                       |
| Opening delimiter (`(`, `[`, `{`)                             | **Stash**          |                                                                       |
| `$(…)`, `$>(…)`, `$!(…)`, `$!>(…)` command literals          | **Stash**          | Existing behavior unchanged                                           |
| `\` followed by identifier or path (no space before)         | **Shell (forced)** | See [§2.3](#23-the--escape-prefix)                                    |
| `!` followed by identifier not declared in Stash scope        | **Shell (strict)** | See [§2.4](#24-the--strict-prefix)                                    |
| Token containing `/`, or starting with `./`, `../`, `~/`      | **Shell**          | Path-like first token — treated as executable path                   |
| Bare identifier `foo`                                         | **See §2.2**       |                                                                       |

### 2.2 Bare Identifier Resolution

When the first token is a bare identifier `foo`:

1. **Peek the next token.** If it is `=`, `(`, `[`, `.`, `+=`, `-=`, `*=`, `/=`, `%=`, `**=`, `&&=`, `||=`, `??=`, `?.`, `?:`, `??`, or end-of-input → **Stash mode** (assignment, call, index, member access).
2. **If `foo` is a declared Stash symbol** (any scope visible at the REPL top level, including stdlib namespaces like `fs`, `path`, `env`) → **Stash mode**. Stash symbols always win; use `\foo` to bypass (see [§2.3](#23-the--escape-prefix)).
3. **Else if `foo` resolves on `PATH`** (or is a shell built-in: `cd`, `pwd`, `exit`, `quit`) → **Shell mode**.
4. **Else → Stash mode** with an error hint: *"Unknown identifier 'foo'. If this is a command, ensure it is on PATH or use `\foo` to invoke it explicitly."*

```stash
// Example: Stash symbol shadows PATH binary
const ls = [1, 2, 3]
ls           // Stash mode → [1, 2, 3] (Stash list)
ls -la       // Stash mode → "ls" resolved as symbol, "- la" is a subtraction expression → parse error
\ls -la      // Shell mode (forced) → invokes /usr/bin/ls
```

### 2.3 The `\` Escape Prefix

`\foo` **always forces shell mode**, bypassing Stash symbol resolution. The `\` is consumed by the classifier and not passed to the command.

```stash
const git = "my-git-wrapper"
git status        // Stash mode → "git" is a symbol → evaluates the string + "status" → parse error
\git status       // Shell mode → invokes /usr/bin/git
\./deploy.sh      // Shell mode → runs local script
\~/bin/my-tool    // Shell mode → runs ~/bin/my-tool
```

> **Note:** `\` at the **end** of a line is the line-continuation marker (§4 of this doc, and the language spec). The two cannot collide: the escape prefix `\` must be followed immediately by an identifier or path character with no intervening whitespace.

### 2.4 The `!` Strict Prefix

`!foo` runs `foo` in **strict mode**: a non-zero exit code raises `CommandError`. This mirrors the existing `$!(…)` strict syntax.

```text
$ !false
CommandError: command exited with status 1: false
```

`!` is **ambiguous** with Stash's logical-not operator. Disambiguation rule: if the identifier after `!` is a **declared Stash symbol** → logical-not (Stash mode); else if it resolves on PATH → strict shell command; else → Stash mode (undefined-identifier error from logical-not).

```stash
let ok = true
!ok              // Stash mode → logical-not → false

!git status      // Shell mode (strict) → git status; throws on non-zero exit
```

### 2.5 Combining `!` and `\`

`!\cmd` or `!\path/cmd` combines strict mode with force-PATH. The `!` must come first:

```stash
!\ls -la         // strict + forced shell → invokes ls; throws on non-zero exit
```

`\!foo` is **not** supported — `!` must precede `\`.

---

## 3. Argument Expansion Pipeline

When a shell-mode line runs, each command's raw argument string is expanded in this fixed order:

```
raw args string
   │
   ▼  1. ${expr} interpolation
   ▼  2. {a,b,c} brace expansion
   ▼  3. ~  tilde expansion
   ▼  4. word splitting (on unquoted whitespace)
   ▼  5. glob expansion  (* ? [...] **)
   │
   ▼
final argv array
```

### 3.1 Interpolation (`${expr}`)

`${…}` substrings are evaluated as Stash expressions in the current REPL scope. The result is stringified via the normal Stash string-conversion rules.

```text
$ let host = "prod.example.com"
$ ssh ${host}
# expands to: ssh prod.example.com

$ mkdir -p ${env.get("HOME")}/projects/new-app
# expands to the full home-dir path

$ git log --since="${time.format(time.now(), "yyyy-MM-dd")}"
# dynamic date in the argument
```

Only `${expr}` is recognized. Bare `$VAR` is **not** expanded (no bash-style env var interpolation — use `${env.get("VAR")}` explicitly).

### 3.2 Brace Expansion (`{a,b,c}`)

Comma-separated alternatives inside `{…}` expand to multiple words. Braces inside quotes are not expanded.

```text
$ cp file.{txt,bak} /tmp/
# → cp file.txt /tmp/ && cp file.bak /tmp/

$ mkdir -p src/{lib,bin,tests}
# → creates all three directories

$ echo {a,b}-{1,2}
a-1 a-2 b-1 b-2
```

Cross-product expansion occurs when multiple brace groups appear in the same word:

```text
$ touch {jan,feb,mar}-{2025,2026}.csv
# → 6 files: jan-2025.csv jan-2026.csv feb-2025.csv feb-2026.csv mar-2025.csv mar-2026.csv
```

### 3.3 Tilde Expansion (`~`)

A leading `~` or `~/` expands to the user's home directory.

```text
$ cd ~/projects
# → cd /home/alice/projects

$ ls ~
# lists home directory
```

`~user/path` (named tilde expansion) is **not** supported in v1.

### 3.4 Word Splitting

After interpolation, brace expansion, and tilde expansion, the resulting string is split into words on **unquoted whitespace**. Quoted regions (single or double quotes) become single words regardless of whitespace inside them.

```text
$ echo "hello world"     # one argument: "hello world"
$ echo hello world       # two arguments: "hello", "world"
$ echo "  spaces  "      # one argument with leading/trailing spaces preserved
```

### 3.5 Glob Expansion (`*`, `?`, `[…]`, `**`)

Each unquoted word containing `*`, `?`, `[…]`, or `**` is matched against the filesystem.

| Pattern | Matches                                                              |
| ------- | -------------------------------------------------------------------- |
| `*`     | Any sequence of characters (not crossing `/`, not matching dotfiles) |
| `?`     | Any single character (not `.` when leading)                          |
| `[abc]` | Any character in the set                                             |
| `**`    | Zero or more path segments (recursive)                               |

```text
$ ls *.stash          # all .stash files in current directory
$ rm logs/app-*.log   # delete matching log files
$ find **/*.json      # all JSON files recursively
```

**Dotfiles excluded by default.** `*` does not match filenames beginning with `.`. Use `.*` or `.[!.]` explicitly.

**No-match throws `CommandError`.** If a glob pattern matches zero files, a `CommandError` is raised:

```text
$ rm *.tmp
CommandError: glob pattern '*.tmp' did not match any files
```

This is zsh-style behavior — safer than bash's silent pass-through, which could accidentally pass a literal `*.tmp` to `rm`.

**Quoted patterns are not expanded.** To pass a literal glob to a command:

```text
$ find . -name "*.log"     # passes *.log literally to find
$ echo '*.stash'           # prints *.stash
```

### 3.6 Quoting Rules

Both `"…"` and `'…'` follow Stash string semantics — both **interpolate** `${…}` expressions. To write a literal `$`, use `\$`:

```text
$ echo "Home is ${env.get("HOME")}"
Home is /home/alice

$ echo 'Cost: \$5'
Cost: $5
```

> This differs from bash, where `'…'` is always literal. The reason: Stash has one quoting model throughout the language; diverging inside shell mode would create two mental models.

---

## 4. Pipelines

Commands can be piped together using `|`. The OS provides the pipe connection — output from each stage streams directly to the next without buffering in memory.

```text
$ ps aux | grep nginx | awk '{print $2}'
12345
12346

$ cat /var/log/syslog | grep error | wc -l
47
```

### 4.1 Streaming

Stash uses OS-level streaming pipes (not buffered). Intermediate stages start immediately and run concurrently. This means:

- `tail -f /var/log/app.log | grep ERROR` works correctly — `tail` streams lines as they arrive.
- Large files pipe efficiently without loading them into memory.
- A downstream consumer that closes early (e.g. `head -5`) causes upstream stages to receive SIGPIPE and terminate gracefully.

### 4.2 Exit Codes

The pipeline's overall exit code is the **last stage's exit code** (matching bash's default `pipefail`-off behavior). With strict mode (`!cmd1 | cmd2`), any stage's non-zero exit raises `CommandError`.

### 4.3 Multi-line Pipelines

A line ending with `|` (after stripping trailing whitespace) signals that the pipeline continues on the next line. The REPL shows a `... ` continuation prompt:

```text
$ cat access.log |
...   grep "POST /api" |
...   awk '{print $7}' |
...   sort | uniq -c | sort -rn | head -10
```

### 4.4 Pipelines in `$(…)`

The streaming pipeline also applies to `$(…)` captures. Intermediate stages stream to the next stage; only the final stage's stdout is buffered into the returned `CommandResult`:

```stash
let errors = $(journalctl -n 1000 | grep ERROR | wc -l);
io.println(errors.stdout.trim());   // e.g. "23"
```

---

## 5. Redirects

Redirects route a command's stdin, stdout, or stderr to files. They apply to the **last stage** of a pipeline.

| Syntax | Meaning                                          |
| ------ | ------------------------------------------------ |
| `>`    | Redirect stdout (overwrite)                      |
| `>>`   | Redirect stdout (append)                         |
| `2>`   | Redirect stderr (overwrite)                      |
| `2>>`  | Redirect stderr (append)                         |
| `&>`   | Redirect stdout **and** stderr (overwrite)       |
| `&>>`  | Redirect stdout **and** stderr (append)          |

```text
$ ls -la > files.txt          # save listing to file
$ echo "entry" >> log.txt     # append to log
$ make 2> build-errors.log    # capture errors only
$ make &> build.log           # capture all output
$ find / -name "*.conf" > confs.txt 2> /dev/null  # stdout to file, errors discarded
```

Redirects can be combined with pipelines:

```text
$ grep -r "TODO" src/ | sort > todos.txt
# grep → sort pipeline; sort's output redirected to todos.txt
```

---

## 6. Shell Built-in Sugar

The shell runner recognizes `cd`, `pwd`, `exit`, and `quit` as special names and **desugars** them to `process.*` stdlib calls. The stdlib calls are real Stash code — errors, stack traces, and `defer` blocks all behave identically to direct calls.

### 6.1 `cd`

| Input             | Desugared call                                                            |
| ----------------- | ------------------------------------------------------------------------- |
| `cd <dir>`        | `process.chdir(<expanded-dir>)`                                           |
| `cd`              | `process.chdir(env.get("HOME"))` on Linux/macOS; `env.get("USERPROFILE")` on Windows |
| `cd -`            | `process.popDir()` + `io.println(process.cwd())`                          |

```text
$ cd ~/projects/stash
$ pwd
/home/alice/projects/stash

$ cd /tmp
$ cd -
/home/alice/projects/stash    ← prints the restored directory
```

`cd` with more than one argument raises `CommandError: cd: too many arguments`.

Argument expansion runs before desugaring, so interpolation, brace expansion, and tilde all work:

```text
$ cd ${env.get("PROJECTS")}/stash
$ cd ~/code/{frontend,backend}     # expands to two args → error: too many arguments
$ cd ~/code/frontend               # ok
```

### 6.2 `pwd`

```text
$ pwd
/home/alice/projects/stash
```

Desugars to `io.println(process.cwd())`. Arguments are not allowed; `pwd extra` raises `CommandError: pwd: too many arguments`.

### 6.3 `exit` / `quit`

```text
$ exit        # exits with code 0
$ exit 1      # exits with code 1
$ quit        # alias for exit
```

Desugars to `process.exit(<code>)`. The argument, if provided, must be an integer. `exit abc` raises `CommandError: exit: numeric argument required`.

`exit` is **defer-aware and catch-immune** — see [§8](#8-processexit-semantics).

---

## 7. Directory Stack

`process.chdir` maintains a **directory stack** inside the REPL session. The stack is the single source of truth for the working directory.

| Function                          | Description                                                                   |
| --------------------------------- | ----------------------------------------------------------------------------- |
| `process.chdir(dir: string)`      | Validate `dir`, push it onto the stack, and update the actual cwd. Atomic — fails before mutating if the target is inaccessible. |
| `process.cwd() -> string`         | Returns the top of the stack (the current working directory).                 |
| `process.popDir() -> string`      | Pops the top of the stack, restores the new top as cwd, returns the popped path. Throws `CommandError("directory stack is at root")` when only one entry remains. |
| `process.dirStack() -> array`     | Returns a copy of the stack, **oldest entry first**.                          |
| `process.dirStackDepth() -> int`  | Returns the number of entries in the stack.                                   |

### Stack Semantics

- **Initial state:** the stack has one entry — the cwd inherited from the parent process at REPL startup.
- **`cd -`** pops the top and prints the new cwd (equivalent to many shells' "pop and go back").
- **Stack cap:** the stack is capped at **256 entries**. When `process.chdir` would push to a full stack, the **oldest entry (index 0) is dropped** to make room. Long sessions stay bounded.
- **Session-scoped:** the stack is not persisted across REPL restarts and not shared with child processes.
- **Minimum depth:** `process.popDir` never empties the stack — minimum depth is 1.

```stash
// Inspect the stack from within Stash code at the REPL
io.println(process.dirStack());      // ["/home/alice", "/home/alice/projects", "/tmp"]
io.println(process.dirStackDepth()); // 3
process.popDir();                    // pops "/tmp", restores "/home/alice/projects"
```

---

## 8. `process.exit` Semantics

`process.exit(code: int = 0)` terminates the interpreter with the given exit code.

**Defer-aware:** all pending `defer` blocks are run in reverse declaration order, walking up the call stack. Resource cleanup declared with `defer` is guaranteed to execute.

```stash
fn cleanup() {
    defer io.println("deferred cleanup");
    process.exit(1);
    // "deferred cleanup" is still printed before the process exits
}
cleanup();
```

**Catch-immune:** no `try/catch` clause matches `process.exit`. The exit propagates through every `try` block, executing their `defer` blocks along the way, and terminates the interpreter at the top level.

```stash
try {
    process.exit(0);
    // This CANNOT be caught — the catch block is not reached
} catch (e) {
    io.println("This never runs");
}
```

This is intentional: a misplaced `try/catch` cannot accidentally swallow an exit. If a `defer` block itself throws during exit unwinding, the secondary error is recorded as a suppressed error (mirroring the existing `defer` suppressed-error mechanism) and the original exit code is preserved.

In the REPL, `process.exit` exits the REPL host process — there is no "soft exit" that returns to the prompt.

---

## 9. `$?` REPL Sugar

Inside the REPL only, the token `$?` is syntactic sugar for `process.lastExitCode()`. The REPL preprocessor rewrites it before lexing.

`process.lastExitCode()` returns the exit code of the most recent bare command (or `$(…)` command). Defaults to `0` before any command runs.

```text
$ ls /nonexistent
ls: cannot access '/nonexistent': No such file or directory

$ $?
2

$ if $? != 0 { io.println("last command failed"); }
last command failed

$ let code = $?
$ io.println("exit code was: " + code)
exit code was: 2
```

`$?` is recognized **only** at the REPL — it is not valid in `.stash` scripts. In scripts, use `process.lastExitCode()` directly.

`$?` is not recognized inside string literals or comments:

```text
$ io.println("exit code: $?")   # NOT expanded — prints literally "exit code: $?"
$ // $? is a comment            # NOT expanded
```

---

## 10. RC File

On REPL start, Stash looks for an initialization file at these paths (first match wins):

| Priority | Path                                     | Condition                          |
| -------- | ---------------------------------------- | ---------------------------------- |
| 1        | `$XDG_CONFIG_HOME/stash/init.stash`      | If `XDG_CONFIG_HOME` env var is set |
| 2        | `~/.config/stash/init.stash`             | Always checked second              |
| 3        | `~/.stashrc`                             | Always checked third               |

If none exist, no RC file is loaded. If multiple exist, only the first is loaded.

### 10.1 How Lines Are Processed

The RC file is fed **line-by-line through the REPL evaluator**, using the same classifier as interactive input. This means:

- **Shell-mode lines work** in the RC file: `cd ~/projects`, bare commands for setup, etc.
- **Stash declarations work** and persist into the REPL session: `const PROJECTS = "~/projects"`, `fn greet(name) { … }`.
- **Multi-line blocks work** using the same continuation rules as interactive input.
- **Errors print a warning** but do not abort REPL startup. The REPL starts regardless.

```stash
// Example ~/.stashrc

// Declare useful constants
const PROJECTS = "${env.get("HOME")}/projects"
const DOTFILES = "${env.get("HOME")}/.dotfiles"

// Change to the projects directory on startup
cd ${PROJECTS}

// Define a helper function
fn g(msg: string) {
    $(git commit -m ${msg});
}
```

### 10.2 Implicit Shell-Mode Activation

The presence of an RC file at any candidate path **implicitly enables shell mode** for the REPL session, equivalent to passing `--shell`. To source the RC file without enabling shell mode:

```bash
stash --no-shell
```

---

## 11. Error Reference

All shell-mode errors are `CommandError` values (the existing built-in error type). No new error types are introduced.

| Failure mode                           | Message                                                            |
| -------------------------------------- | ------------------------------------------------------------------ |
| Command not found on PATH              | `"command not found: <name>"`                                      |
| Permission denied executing file       | `"permission denied: <path>"`                                      |
| Glob with no matches                   | `"glob pattern '<pat>' did not match any files"`                   |
| Strict command non-zero exit           | `"command exited with status <n>: <cmdline>"`                      |
| `cd` to nonexistent directory          | `"no such directory: <path>"` (from `process.chdir`)               |
| `cd -` when stack is at root           | `"directory stack is at root"` (from `process.popDir`)             |
| `cd` / `pwd` / `exit` — too many args  | `"<name>: too many arguments"`                                     |
| `exit` with non-integer argument       | `"exit: numeric argument required"`                                |
| Interpolation expression error         | The expression's own error, with a context note                    |
| Pipeline stage spawn failure           | `"pipeline stage <i> failed to spawn: <inner>"`                    |
| Shell mode on Windows                  | `"shell mode not yet supported on Windows"` (not a `CommandError`) |

---

## 12. Static Analysis

Two diagnostic rules cover shell-mode concerns:

| Code   | Severity | Title                                                      |
| ------ | --------- | ---------------------------------------------------------- |
| SA0820 | Warning   | Unquoted glob pattern in `$(…)` command literal            |
| SA0821 | Info      | Bare identifier may shadow PATH executable in shell mode   |

### SA0820 — Unquoted glob in `$(…)`

**Background:** Before this feature shipped, `$(rm *.tmp)` passed `*.tmp` literally to `rm` (no glob expansion). After shell mode ships, glob auto-expansion applies inside `$(…)` too — a **breaking change** for any script that relied on the old literal behavior.

`SA0820` warns when a `$(…)` expression contains an unquoted `*`, `?`, or `[` pattern.

```stash
let result = $(find . -name *.log);   // SA0820: unquoted glob in command literal
```

**Migration:** quote the glob to preserve old (literal) behavior:

```stash
let result = $(find . -name "*.log"); // ok — *.log passed literally to find
```

Suppress intentionally with `// stash:ignore[SA0820]` when you want the new glob expansion.

### SA0821 — PATH shadow in shell mode

Emitted by the REPL classifier (not the analysis engine) when a declared Stash symbol also resolves on PATH. Informs the user that `\name` is needed to invoke the binary.

```text
$ let ls = [1, 2, 3]
$ ls
[SA0821] 'ls' is declared as a Stash symbol and shadows the PATH binary '/usr/bin/ls'. Use \ls to invoke the binary.
```

SA0821 is REPL-only — it does not apply to scripts.

---

## 13. Cross-Platform Notes

### 13.1 Windows Status

Shell mode is **gated on Windows** in v1. The `--shell` flag and `STASH_SHELL=1` env var produce the message `"shell mode not yet supported on Windows"` and the REPL starts in Stash-only mode.

Windows-aware code paths are in place for a future mechanical re-enable:

- **PATHEXT honored** by `PathExecutableCache` — `foo` resolves `foo.exe`, `foo.bat`, `foo.cmd`, etc.
- **`C:\`-style drive paths** are classified as path-like first tokens (shell mode), alongside `./`, `../`, `~/`.
- **Tilde expansion** uses `%USERPROFILE%` on Windows.
- **Glob expansion** uses case-insensitive matching on Windows (NTFS semantics).
- **`cd` (no args)** uses `env.get("USERPROFILE")` on Windows.
- **Streaming pipes** use .NET's `Process` API, which handles SIGPIPE as a broken-pipe `IOException`.

### 13.2 POSIX-Specific Notes

- **Executable check:** a file is executable if the current user has `+x` permission.
- **Tilde expansion** uses `$HOME`.
- **`cd` (no args)** uses `env.get("HOME")`.
- **Glob expansion** is case-sensitive on Linux; case-insensitive on macOS (HFS+/APFS case-insensitive mounts).

### 13.3 `$(…)` Glob Auto-Expansion — Breaking Change

Prior to this feature, `$(…)` command literals did not glob-expand arguments. After this feature ships, **glob auto-expansion applies inside `$(…)` for both bare-command pipelines and script-mode command literals**.

**Impact:** scripts that relied on passing unquoted `*` literally to commands (e.g. `$(rm *.tmp)`) will now have the glob expanded before the command runs. If no files match, a `CommandError` is thrown.

**Migration:**
1. Quote the glob pattern: `$(rm "*.tmp")` — `"*.tmp"` is not glob-expanded.
2. Or use the static analyzer: `SA0820` will flag all unquoted globs in `$(…)` expressions.

---

*See also: [Standard Library Reference — `process` namespace](Stash%20—%20Standard%20Library%20Reference.md#process--process-management) for the full API of `process.chdir`, `process.popDir`, `process.dirStack`, `process.dirStackDepth`, `process.exit`, and `process.lastExitCode`.*

---

## 14. Customizing the Prompt

Shell-mode REPL prompts are fully customizable via Stash code. See [Prompt — Customizing the REPL Prompt](Prompt%20%E2%80%94%20Customizing%20the%20REPL%20Prompt.md) for the full guide on themes, starters, and writing your own `fn prompt(ctx)`.
