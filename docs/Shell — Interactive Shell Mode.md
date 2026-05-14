# Stash - Interactive Shell Mode

> **Status:** Stable v1 shell reference
> **Audience:** shell users, tool authors, and implementers
> **Purpose:** normative reference for the Stash interactive shell experience: bare commands, line classification, expansion, pipelines, redirects, aliases, completion, startup files, and session state.

Shell mode lets the Stash REPL behave like an interactive command shell while still
accepting normal Stash code. It is a REPL feature only; script files always parse as
Stash programs.

**Companion documents:**

- [Language Specification](Stash%20%E2%80%94%20Language%20Specification.md) - source-language syntax and semantics
- [Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md) - `env`, `process`, `shell`, `alias`, `complete`, and `prompt` APIs
- [Prompt - Customizing the REPL Prompt](Prompt%20%E2%80%94%20Customizing%20the%20REPL%20Prompt.md) - prompt themes and custom prompt functions

---

## Contents

1. [Overview](#overview)
2. [Activation](#activation)
3. [Line Classification](#line-classification)
4. [Argument Expansion](#argument-expansion)
5. [Command Execution](#command-execution)
6. [Pipelines and Redirects](#pipelines-and-redirects)
7. [Shell Built-ins](#shell-built-ins)
8. [Session State](#session-state)
9. [Startup and History](#startup-and-history)
10. [Completion](#completion)
11. [Aliases](#aliases)
12. [Diagnostics and Errors](#diagnostics-and-errors)
13. [Platform Behavior](#platform-behavior)

---

## Overview

In ordinary Stash code, external commands are written with command literals such as
`$(git status)`. In shell mode, a command can be typed directly at the prompt.

```text
$ git status --short
$ ls -la | head -20
$ cd ~/projects/stash
```

Stash code remains valid at the same prompt.

```stash
let branch = "main";
io.println("deploying ${branch}");
if shell.lastExitCode() != 0 {
    io.eprintln("previous command failed");
}
```

Each physical or continued input is classified as either Stash input or shell input
before evaluation. Shell input is expanded, resolved, and executed as a process,
alias, or shell built-in.

## Activation

Shell mode is opt-in unless an initialization file is present.

| Trigger         | Effect                                             |
| --------------- | -------------------------------------------------- |
| `stash --shell` | Starts an interactive REPL with shell mode enabled |
| `STASH_SHELL=1` | Enables shell mode for the interactive REPL        |
| RC file present | Enables shell mode unless `--no-shell` is passed   |

`stash --no-shell` forces a Stash-only REPL even when an RC file exists.

Shell mode is never active when running a script file, when Stash is embedded by a
host that does not enable shell behavior, or when `--no-shell` is passed.

```bash
stash --shell
stash --no-shell
stash deploy.stash     # script mode, never shell mode
```

## Line Classification

The REPL classifies each input as Stash mode or shell mode before lexing/evaluation.
Classification is based on the first token and the current REPL bindings.

### Stash-First Forms

The following forms are always Stash input:

- Stash declarations and statements such as `let`, `const`, `fn`, `if`, `for`,
  `while`, `try`, `defer`, `struct`, `enum`, `interface`, `import`, and `unset`
- literals such as `42`, `"text"`, `true`, `false`, and `null`
- inputs beginning with `(`, `[`, or `{`
- command literals such as `$(...)`, `$!(...)`, `$>(...)`, `$!>(...)`, `$<(...)`,
  and `$!<(...)`
- a bare identifier followed by Stash syntax such as `=`, `(`, `[`, `.`, `?.`,
  arithmetic assignment, `??`, or end of input

### Bare Identifier Resolution

For a first token such as `git` or `ls`, resolution proceeds in this order:

1. If the name is a visible Stash binding or built-in namespace, the line is Stash
   input.
2. If the name is an alias, shell built-in, or executable on `PATH`, the line is
   shell input.
3. Otherwise the line is Stash input and normal Stash diagnostics apply.

Stash bindings therefore shadow commands.

```text
shell> let ls = [1, 2, 3]
shell> ls
[1, 2, 3]
shell> \ls -la
# invokes the PATH executable
```

Use `unset name` to remove an accidental REPL binding and allow PATH lookup again.

### Force and Strict Prefixes

`\name` forces shell mode and bypasses Stash symbol resolution. The leading `\` is
not passed to the command.

```text
shell> \git status
shell> \./deploy.sh
```

`!name` runs a shell command in strict mode. A non-zero exit code raises
`CommandError`, matching `$!(...)`.

```text
shell> !make test
```

If the identifier after `!` is a declared Stash symbol, `!` is parsed as Stash
logical-not instead.

```stash
let ok = true;
!ok;             // Stash expression: false
```

`!\name` combines strict execution with forced shell resolution.

```text
shell> !\ls -la
```

## Argument Expansion

Shell-mode command arguments are expanded in a fixed order.

```text
raw argument text
  -> Stash interpolation
  -> brace expansion
  -> tilde expansion
  -> word splitting
  -> glob expansion
  -> argv
```

### Stash Interpolation

`${expr}` evaluates a Stash expression in the current REPL scope and inserts its
string value.

```text
shell> let host = "prod.example.com"
shell> ssh ${host}
shell> mkdir -p ${env.get("HOME")}/projects/app
```

Bare `$VAR` environment expansion is not supported. Use `${env.get("VAR")}`.

### Brace Expansion

Comma-separated alternatives inside braces expand to multiple words. Multiple brace
groups form a cross product.

```text
shell> touch {jan,feb,mar}-{2025,2026}.csv
shell> mkdir -p src/{lib,bin,tests}
```

Braces inside quotes are not expanded.

### Tilde Expansion

A leading `~` or `~/` expands to the current user's home directory.

```text
shell> cd ~/projects
```

Named user expansion such as `~alice/src` is not part of v1 shell mode.

### Word Splitting and Quoting

After interpolation, brace expansion, and tilde expansion, unquoted whitespace
separates arguments. Quoted text remains one argument.

```text
shell> echo hello world       # two arguments
shell> echo "hello world"     # one argument
```

Both single and double quotes use Stash interpolation rules. This intentionally
differs from POSIX shell quoting.

```text
shell> echo "Home is ${env.get("HOME")}"
shell> echo 'Cost: \$5'
```

Use `\$` for a literal dollar sign.

### Glob Expansion

Unquoted `*`, `?`, `[abc]`, and `**` patterns are matched against the filesystem.

| Pattern | Meaning                              |
| ------- | ------------------------------------ |
| `*`     | any sequence within one path segment |
| `?`     | any single character                 |
| `[abc]` | one character from the set           |
| `**`    | zero or more path segments           |

```text
shell> ls *.stash
shell> find **/*.json
```

Dotfiles are not matched by `*` unless the pattern explicitly begins with `.`. A
glob with no matches raises `CommandError` instead of passing the literal pattern to
the command.

Quote a pattern to pass it literally.

```text
shell> find . -name "*.log"
shell> echo '*.stash'
```

Glob expansion also applies inside Stash command literals such as `$(...)`. To
preserve literal behavior, quote the pattern.

## Command Execution

Shell-mode execution resolves the command name, builds an argv array, and invokes
the target process or built-in.

Resolution order:

1. shell aliases
2. protected shell built-ins such as `cd`, `pwd`, `exit`, `quit`, and `history`
3. executables on `PATH`

The force prefix `\` bypasses aliases and Stash symbol lookup and goes directly to
shell executable resolution.

Normal shell commands stream stdout and stderr to the REPL's current standard
streams. The most recent command exit code is stored in `shell.lastExitCode()` and
is also available as `$?` in the REPL.

Strict shell commands raise `CommandError` on non-zero exit. Non-strict commands do
not raise solely because of exit status.

## Pipelines and Redirects

### Pipelines

`|` connects stdout from one command to stdin of the next command.

```text
shell> ps aux | grep nginx | awk '{print $2}'
shell> cat access.log | grep ERROR | wc -l
```

Pipeline stages run concurrently using OS-level streaming. Intermediate output is
not buffered in Stash memory.

The exit code of a non-strict pipeline is the last stage's exit code. In strict
mode, any stage failure raises `CommandError`.

A line ending with `|` continues on the next physical line.

```text
shell> cat access.log |
...    grep "POST /api" |
...    awk '{print $7}' |
...    sort | uniq -c | sort -rn
```

The same pipeline behavior applies inside command literals.

```stash
let count = $(journalctl -n 1000 | grep ERROR | wc -l);
```

### Redirects

Redirects route command output to files.

| Syntax | Meaning                      |
| ------ | ---------------------------- |
| `>`    | stdout, overwrite            |
| `>>`   | stdout, append               |
| `2>`   | stderr, overwrite            |
| `2>>`  | stderr, append               |
| `&>`   | stdout and stderr, overwrite |
| `&>>`  | stdout and stderr, append    |

```text
shell> ls -la > files.txt
shell> make 2> build-errors.log
shell> make &> build.log
shell> grep -r "TODO" src/ | sort > todos.txt
```

For a pipeline, redirection applies to the final stage unless a command literal
specifies otherwise.

## Shell Built-ins

Shell built-ins are dispatched through Stash runtime behavior rather than through a
separate shell language. Errors and cleanup therefore follow normal Stash semantics.

### `cd`

`cd` changes the REPL working directory through `env.chdir`.

```text
shell> cd ~/projects/stash
shell> cd
shell> cd -
```

`cd` with no argument changes to the user's home directory. `cd -` pops the
directory stack and prints the restored directory. More than one argument raises
`CommandError`.

### `pwd`

`pwd` prints the current REPL working directory.

```text
shell> pwd
/home/alice/projects/stash
```

Extra arguments raise `CommandError`.

### `exit` and `quit`

`exit` terminates the REPL with an optional integer exit code. `quit` is an alias
for `exit 0`.

```text
shell> exit
shell> exit 1
shell> quit
```

Exit is defer-aware and catch-immune. Pending `defer` blocks run during unwinding,
but `try/catch` does not catch `env.exit`.

### `history`

`history` prints or clears the in-memory and persisted REPL history.

```text
shell> history
shell> history 20
shell> history -c
shell> history | grep git
```

`history N` prints the last `N` entries. `history -c` clears history.

## Session State

### Directory Stack

The REPL maintains a directory stack through the `env` namespace.

| Function              | Behavior                                                 |
| --------------------- | -------------------------------------------------------- |
| `env.chdir(path)`     | validates and pushes a new current directory             |
| `env.cwd()`           | returns the current directory                            |
| `env.popDir()`        | pops the current directory and restores the previous one |
| `env.dirStack()`      | returns the stack, oldest first                          |
| `env.dirStackDepth()` | returns the number of stack entries                      |

The initial stack contains the inherited working directory. The stack is capped at
256 entries; when full, the oldest entry is dropped. `env.popDir()` never removes
the final entry.

### Last Exit Code

`shell.lastExitCode()` returns the most recent command exit code. Before any command
has run, it returns `0`.

In the REPL, `$?` is preprocessed into `shell.lastExitCode()` before lexing.

```text
shell> false
shell> $?
1
shell> if $? != 0 { io.println("failed"); }
failed
```

`$?` is REPL-only. It is not valid in `.stash` scripts and is not expanded inside
strings or comments.

## Startup and History

### RC File

On startup, Stash loads the first initialization file found at these locations:

| Priority | Path                                |
| -------- | ----------------------------------- |
| 1        | `$XDG_CONFIG_HOME/stash/init.stash` |
| 2        | `~/.config/stash/init.stash`        |
| 3        | `~/.stashrc`                        |

The RC file is evaluated through the same REPL evaluator as interactive input. It
may contain Stash declarations, shell-mode commands, and continued multi-line input.
Errors are reported as startup warnings and do not prevent the REPL from starting.

```stash
const PROJECTS = "${env.get("HOME")}/projects";
cd ${PROJECTS}

fn g(msg: string) {
    $(git commit -m ${msg});
}
```

Presence of an RC file enables shell mode unless `--no-shell` is passed.

### Persistent History

Interactive REPL sessions keep command history in memory and persist it to disk
unless disabled. Script execution does not write REPL history.

History file resolution:

| Platform    | Resolution order                                                                                           |
| ----------- | ---------------------------------------------------------------------------------------------------------- |
| Linux/macOS | `$STASH_HISTORY_FILE`, `$XDG_STATE_HOME/stash/history`, `~/.local/state/stash/history`, `~/.stash_history` |
| Windows     | `%STASH_HISTORY_FILE%`, `%LOCALAPPDATA%\stash\history`, `%USERPROFILE%\.stash_history`                     |

If the chosen path cannot be opened, persistence is disabled for the session and a
warning is printed to stderr. In-memory history still works.

Configuration:

| Setting                | Effect                                                                     |
| ---------------------- | -------------------------------------------------------------------------- |
| `STASH_HISTORY_FILE=`  | empty value disables persistence                                           |
| `STASH_HISTORY_SIZE=N` | maximum stored entries; `0` disables persistence; negative means unlimited |
| `--no-history`         | disables persistence for this session                                      |

History rules:

- empty lines are not stored
- lines beginning with whitespace are executed but not stored
- consecutive duplicate entries are collapsed
- multi-line inputs are stored as one entry
- the size cap is enforced when history is loaded

The history file is UTF-8 with LF line endings. Entries are separated by blank
lines; multi-line entries keep their internal newlines.

Programmatic access is available through `process.historyList()`,
`process.historyClear()`, and `process.historyAdd(line)`.

## Completion

Tab completion is available in interactive REPL sessions unless disabled with
`STASH_NO_COMPLETION=1`.

The completion interaction follows a bash-classic model:

| Trigger                        | Behavior                               |
| ------------------------------ | -------------------------------------- |
| first Tab, no candidates       | bell                                   |
| first Tab, one candidate       | insert the candidate                   |
| first Tab, multiple candidates | insert the longest common prefix       |
| second consecutive Tab         | print candidates and redraw the prompt |

When more than 100 candidates are available, the REPL asks before displaying them.
Directory completions append a trailing `/`.

Completion sources:

| Context                   | Candidates                                                   |
| ------------------------- | ------------------------------------------------------------ |
| shell command position    | PATH executables, shell built-ins, aliases, callable globals |
| shell argument position   | file paths                                                   |
| Stash identifier position | keywords, globals, namespaces, visible bindings              |
| after `.`                 | namespace members, constants, and accessible fields          |
| inside `${...}`           | Stash identifier completion                                  |

Matching is smart-case: lowercase prefixes match case-insensitively; prefixes with
uppercase letters match case-sensitively. Tokens containing glob or brace pattern
syntax do not receive completions.

Custom command completers can be registered with `complete.register`.

```stash
complete.register("git", (ctx) => {
    let sub = ["add", "checkout", "commit", "diff", "log", "status"];
    return arr.filter(sub, (s) => str.startsWith(s, ctx.current));
});
```

A custom completer replaces default file-path completion for that command. Call
`complete.paths(ctx)` from the custom completer to include path candidates.

## Aliases

Aliases define shell-mode names that expand before PATH lookup.

### Template Aliases

Template aliases store a command string with argument placeholders.

```text
alias gs = "git status ${args}"
alias glog = "git log --oneline --graph ${args}"
alias gco = "git checkout ${args[0]}"
```

| Placeholder  | Meaning                                      |
| ------------ | -------------------------------------------- |
| `${args}`    | all arguments, shell-quoted and space-joined |
| `${args[N]}` | argument at zero-based index `N`             |
| `${argv}`    | Stash array literal of raw argument strings  |

### Function Aliases

Function aliases store a Stash callable.

```text
alias mkcd = (dir: string) => {
    fs.createDir(dir);
    env.chdir(dir);
}
```

### Alias Resolution and Bypass

Aliases resolve before PATH commands. An alias can therefore shadow an executable.

```text
shell> alias gs = "git status ${args}"
shell> gs --short
```

Use `\name` to bypass the alias registry and invoke a command from PATH. Use
`!name` for strict PATH execution.

### Managing Aliases

```text
alias              # list aliases
alias gs           # show one alias
unalias gs         # remove one alias
unalias --all      # remove user-defined aliases
```

Passing `--help` as the first argument to an alias prints alias metadata instead of
executing it. Use a bypass prefix to pass `--help` to the underlying command.

Programmatic alias management is provided by the `alias` namespace.

```stash
alias.define("deploy", "kubectl apply -f deployment.yaml", AliasOptions {
    description: "Deploy to the active cluster",
    confirm: "Deploy to production?",
});

alias.save();
alias.load();
```

Built-in aliases for `cd`, `pwd`, `exit`, `quit`, and `history` are protected. To
replace one, pass `AliasOptions { override: true }`.

Aliases may be persisted in `aliases.stash`.

| Platform | Default location                                                          |
| -------- | ------------------------------------------------------------------------- |
| Linux    | `$XDG_CONFIG_HOME/stash/aliases.stash` or `~/.config/stash/aliases.stash` |
| macOS    | `~/Library/Application Support/stash/aliases.stash`                       |
| Windows  | `%APPDATA%\stash\aliases.stash`                                           |

## Diagnostics and Errors

Shell-mode failures use `CommandError` unless otherwise noted.

| Failure                       | Error behavior                                     |
| ----------------------------- | -------------------------------------------------- |
| command not found             | `command not found: <name>`                        |
| permission denied             | `permission denied: <path>`                        |
| glob has no matches           | `glob pattern '<pattern>' did not match any files` |
| strict command exits non-zero | `command exited with status <code>: <command>`     |
| `cd` target missing           | `no such directory: <path>`                        |
| directory stack at root       | `directory stack is at root`                       |
| too many built-in arguments   | `<name>: too many arguments`                       |
| invalid `exit` argument       | `exit: numeric argument required`                  |
| interpolation failure         | original Stash error with shell context            |
| pipeline stage spawn failure  | `pipeline stage <n> failed to spawn: <error>`      |

Static diagnostics:

| Code   | Severity | Meaning                                               |
| ------ | -------- | ----------------------------------------------------- |
| SA0820 | warning  | unquoted glob in a command literal                    |
| SA0821 | info     | Stash binding shadows a PATH executable in shell mode |
| SA0850 | error    | invalid alias name passed to `alias.define`           |
| SA0851 | warning  | empty alias confirmation prompt                       |

## Platform Behavior

### Linux and macOS

Shell mode is supported on Linux and macOS. Executable lookup follows POSIX
executable permissions. Tilde expansion uses `$HOME`. Glob matching is
case-sensitive on Linux and follows the mounted filesystem's case behavior on macOS.

### Windows

Shell mode is gated on Windows in v1. Passing `--shell` or setting
`STASH_SHELL=1` reports that shell mode is not yet supported and starts a
Stash-only REPL.

Windows-aware behavior exists for future enablement:

- `PATHEXT` executable resolution
- drive-letter path classification such as `C:\Tools\app.exe`
- `%USERPROFILE%` home resolution
- case-insensitive glob matching on common Windows filesystems
- `.exe`, `.bat`, and `.cmd` command discovery

### Compatibility Notes

Stash shell mode intentionally differs from POSIX shells in a few places:

- Stash symbols shadow PATH commands unless `\` is used.
- Single and double quotes both allow Stash interpolation.
- Bare `$VAR` expansion is not supported.
- Glob no-match raises `CommandError`.
- `$?` is REPL-only sugar for `shell.lastExitCode()`.
