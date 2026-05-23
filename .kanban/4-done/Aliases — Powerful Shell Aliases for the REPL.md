# Aliases — Powerful Shell Aliases for the REPL

> **Status:** Backlog (design)
> **Created:** 2026-05-01
> **Purpose:** Add user-defined aliases to Stash's interactive shell mode that go meaningfully beyond bash aliases, while preserving Stash's "explicit intent" philosophy. Aliases are a stdlib-first feature: the `alias` namespace is the source of truth; the shell-mode `alias <name> = <body>` syntax is sugar that translates to `alias.define(...)`.

## Table of Contents

1. [Motivation](#1-motivation)
2. [Design Philosophy & Non-Goals](#2-design-philosophy--non-goals)
3. [Concept Model](#3-concept-model)
4. [Stdlib API — `alias` namespace](#4-stdlib-api--alias-namespace)
5. [Shell-Mode Sugar Syntax](#5-shell-mode-sugar-syntax)
6. [Resolution & Dispatch](#6-resolution--dispatch)
7. [Argument Model](#7-argument-model)
8. [Pipeline & I/O Behavior](#8-pipeline--io-behavior)
9. [Hooks (`before`, `after`, `confirm`)](#9-hooks-before-after-confirm)
10. [Persistence](#10-persistence)
11. [Tab Completion](#11-tab-completion)
12. [Help Text & Introspection](#12-help-text--introspection)
13. [Built-in Aliases (`cd`, `pwd`, `exit`, `quit`, `history`)](#13-built-in-aliases)
14. [Lazy Parsing (Performance)](#14-lazy-parsing-performance)
15. [Errors & Diagnostics](#15-errors--diagnostics)
16. [Cross-Platform Considerations](#16-cross-platform-considerations)
17. [Implementation Plan](#17-implementation-plan)
18. [Test Plan](#18-test-plan)
19. [Out of Scope / Deferred](#19-out-of-scope--deferred)
20. [Decision Log](#20-decision-log)

---

## 1. Motivation

Bash aliases are a glorified `sed` pass — `alias gst='git status'` and nothing more. They don't take typed parameters, don't compose, don't chain safely, don't offer help text, don't drive tab completion, and can't run pre/post hooks. Stash already has `fn` declarations in `.stashrc`, but `fn ll() { \ls -la }` requires the user to type `ll()` at the prompt — losing the entire ergonomic point of an interactive shell.

This spec introduces **aliases** as a distinct, registered, introspectable, bare-word-callable mechanism that fills the ergonomic gap left by `fn` while staying within Stash's "explicit intent" philosophy. Aliases live exclusively in the shell-line dispatch path; they do nothing in pure Stash code.

## 2. Design Philosophy & Non-Goals

### Principles

- **Stdlib-first.** The `alias` namespace is the canonical mechanism. Shell-mode `alias g = git` is sugar that produces an `alias.define(...)` call. Anything you can do at the REPL prompt you can do programmatically.
- **No implicit magic.** Argument forwarding is explicit (`${args}` placeholder or declared params). Auto-appending was rejected — it's exactly the bash misfeature we want to avoid.
- **Explicit shell-mode boundary.** Aliases are bare-word callable **only** inside lines the classifier already routes to `ShellRunner`. Stash code is unaffected.
- **PATH-only shadowing.** Aliases beat PATH executables; they never beat Stash keywords, declared symbols, or stdlib namespaces.
- **One mental model, two surface forms.** Every alias is conceptually a function; the string-template form is a documented sugar over the function form.

### Non-Goals (v1)

- Aliases that work in `.stash` scripts (scripts must remain unambiguous Stash).
- Macro / AST-rewriting aliases.
- Network-installable alias bundles.
- Auto-suggested aliases ("you ran this 5 times, want to alias it?").
- Per-directory `.stashrc.local` (deferred to follow-up spec).
- Subcommand alias namespaces (`alias g.s = git status` invoked as `g s`) — deferred.
- Conditional / OS-gated aliases via `when:` predicate — deferred (users can use `if sys.os() == ...` around `alias.define` calls).
- History-expansion-display control — deferred.

## 3. Concept Model

An **alias** is a registered entry in a process-global registry, identified by name. Each entry has:

```
AliasSpec {
  name:        string                        // "g", "gst", etc.
  kind:        "template" | "function"       // how the body is evaluated
  body:        string | function             // the rule
  params:      array of ParamSpec | null     // for function aliases
  description: string | null                 // user-facing help text
  before:      function | null               // pre-hook
  after:       function | null               // post-hook
  confirm:     string | null                 // confirmation prompt text
  source:      string                        // "rc" | "repl" | "saved" | "builtin"
  override:    bool                          // may shadow a built-in alias?
  sourceLoc:   { file: string, line: int } | null
}
```

The registry is held inside the VM (so subagents/scripts spawned by the REPL inherit nothing — aliases are session-local unless persisted to disk).

### "Template" vs "Function" alias

| Aspect                      | Template                                | Function                                              |
| --------------------------- | --------------------------------------- | ----------------------------------------------------- |
| Body type                   | string with `${...}` interpolations     | Stash function value (closure)                        |
| Argument access             | `${args}`, `${args[0]}`, `${args[1]}`   | Declared params (positional, named, rest)             |
| Compiled?                   | Lazily parsed as a shell line on demand | Compiled to bytecode at definition time               |
| Closures over RC scope      | No (only access to alias args)          | Yes (full lexical capture)                            |
| Stash code in body          | No (string is parsed as a shell line)   | Yes (any Stash including `$(...)` shell calls)        |
| Use case                    | "I want a shorter name for this command" | "I want a real function with shell ergonomics"        |

## 4. Stdlib API — `alias` namespace

New file: `Stash.Stdlib/BuiltIns/AliasBuiltIns.cs` (registered under namespace `alias`).

```stash
// Define a template alias.
// `body` is a string parsed as a shell line at invocation time.
alias.define(name: string, body: string, opts: AliasOptions? = null);

// Define a function alias.
// `body` is any Stash function value (lambda, fn reference, closure).
alias.define(name: string, body: function, opts: AliasOptions? = null);

// Inspect.
alias.list() -> array of AliasInfo            // all registered aliases
alias.names() -> array of string              // just the names (for completion, etc.)
alias.get(name: string) -> AliasInfo | null   // single entry, or null if missing
alias.exists(name: string) -> bool

// Mutate.
alias.remove(name: string) -> bool            // true if removed; false if missing
alias.clear() -> int                          // remove ALL non-builtin aliases; returns count removed

// Execute (programmatic invocation, identical semantics to bare-word call).
alias.exec(name: string, args: array of string) -> int   // returns exit code (0 for function aliases unless they explicitly return one)

// Resolve to a printable, expanded form (no execution).
// For template aliases: returns the body with ${args} substituted.
// For function aliases: returns "<function alias `name`>" (no expansion possible).
alias.expand(name: string, args: array of string) -> string

// Persistence.
alias.save(name: string? = null) -> string    // writes one or all aliases to managed file; returns path written
alias.load(path: string? = null) -> int       // re-loads from managed file; returns count loaded
```

### Supporting structs (declared in stdlib, accessible to users)

```stash
struct AliasOptions {
    description: string? = null
    before:      function? = null      // fn(name: string, args: array of string) -> bool   ; return false to abort
    after:       function? = null      // fn(name: string, args: array of string, exitCode: int) -> nil
    confirm:     string? = null        // if set, prompt user with this text; abort on "n"
    override:    bool    = false       // allow shadowing a built-in alias of the same name
}

struct AliasInfo {
    name:        string
    kind:        string                // "template" | "function"
    body:        string                // for template; "<function>" placeholder for function aliases
    params:      array of ParamInfo    // empty for template aliases
    description: string?
    hasBefore:   bool
    hasAfter:    bool
    confirm:     string?
    source:      string                // "rc" | "repl" | "saved" | "builtin"
    sourceLoc:   SourceLoc?
}

struct ParamInfo {
    name:    string
    type:    string?                   // type annotation if present
    rest:    bool                      // true if ...args
    default: any?                      // default value if present
}

struct SourceLoc {
    file: string
    line: int
}
```

## 5. Shell-Mode Sugar Syntax

The sugar exists **only** in lines that the `ShellLineClassifier` routes to `ShellRunner` (not in Stash-classified lines, not in scripts). It is implemented inside `ShellSugarDesugarer` — same mechanism that handles `cd`, `pwd`, etc.

### 5.1 Defining a template alias

```
alias <name> = <single-token-or-string>
```

Examples:

```
alias gst = "git status"
alias ll  = "ls -la"
alias g   = "git ${args}"           // forwards all args explicitly
alias gco = "git checkout ${args[0]}"
```

**Grammar (informal):**

```
alias-template-stmt  := "alias" IDENT "=" (STRING | BARE-COMMAND-WORD) (NEWLINE | ";")
BARE-COMMAND-WORD    := one shell token without spaces (e.g., "git", "/usr/bin/python")
```

`alias gst = git status` (without quotes) is a **parse error** — multi-word bodies must be quoted strings to disambiguate from "alias as a target with args". Users with a multi-word body are nudged toward quotes, which is also where `${args}` placeholders go.

### 5.2 Defining a function alias

```
alias <name>(<params>) = <expression>           // expression body
alias <name>(<params>) { <statements> }         // block body
```

Examples:

```
alias g(msg: string) = $(git commit -m ${msg})
alias gco(branch: string = "main") {
    $(git fetch origin ${branch});
    $(git checkout ${branch});
    $(git pull origin ${branch});
}
alias deploy(...args) = $(./deploy.sh ${args})
```

**Grammar (informal):**

```
alias-function-stmt  := "alias" IDENT "(" PARAM-LIST? ")" ALIAS-BODY
ALIAS-BODY           := "=" EXPR (NEWLINE | ";")  |  BLOCK
PARAM-LIST            := same as fn / lambda parameter list (typed, defaults, rest)
```

The parameter syntax is **identical** to existing `fn`/lambda parameter syntax — no new parser machinery beyond recognizing the `alias` introducer.

### 5.3 Defining options (hooks, description, etc.)

The shell sugar does not directly support options; for options the user calls `alias.define(...)` directly:

```stash
alias.define("rm", "rm -i", AliasOptions {
    description: "Interactive rm — prompts before deletion",
    confirm: "Delete files? Use \\rm to skip confirmation."
})
```

This keeps the sugar minimal. Power users get full control via the namespace API; casual users get a one-liner.

### 5.4 Removing / inspecting at the REPL

```
unalias <name>           # sugar for alias.remove("name")
unalias --all            # sugar for alias.clear()
alias                    # no args → list all aliases (sugar for alias.list() pretty-printed)
alias <name>             # sugar for alias.get("name") pretty-printed (or shows it doesn't exist)
alias --help             # prints documentation
```

`unalias` is desugared the same way `alias` is — not a Stash keyword, just a shell-mode sugar.

## 6. Resolution & Dispatch

Aliases insert themselves into the existing identifier-resolution order used by the shell line classifier. **The classifier itself is unchanged**; the *dispatch* of a shell-classified bare-word command is what gains an extra step.

### 6.1 New shell-line dispatch order

When `ShellRunner` receives a bare-word command name (after the classifier has already chosen `LineMode.Shell`):

1. **Built-in shell sugar** (cd, pwd, exit, quit, history, alias, unalias) — handled by `ShellSugarDesugarer`. *(After this spec lands, these are themselves implemented as built-in aliases — see §13.)*
2. **Alias registry lookup** — if `alias.exists(name)` returns true, dispatch through the alias system (§6.3).
3. **PATH executable lookup** — existing behavior.
4. **Error.**

### 6.2 What the classifier still does

The classifier already routes lines like `g status` to shell mode because `g` is not a declared Stash symbol. **No classifier change is needed** — aliases sit inside the shell-line dispatch step, not the classification step.

The one subtlety: when a function alias is defined, it ALSO registers a normal Stash function under the alias name (so it can be called as `g("msg")` from Stash code). This means after `alias g(msg) = $(git commit -m ${msg})`, the symbol `g` IS declared in Stash scope, which makes the classifier route `g status` to **Stash mode** instead of shell mode!

**Resolution:** the classifier must check the alias registry first. Modified rule:

> If a bare identifier is registered as an alias, the classifier emits `LineMode.Shell` regardless of whether a same-named Stash symbol exists. Stash code (with parens, dot access, assignment, etc.) is unaffected because those tokens already trigger Stash classification before this check fires.

**Bypass:** `\g` and `!g` continue to mean "force shell PATH lookup" / "shell strict" respectively, bypassing the alias registry. This gives users an escape hatch identical to bash's `\name` for "the real command, not the alias."

### 6.3 Alias execution path

For a template alias:

1. Look up the body string.
2. Substitute `${args}`, `${args[N]}`, and any other `${...}` Stash interpolations against the alias's argument list.
3. Re-feed the resulting string into the shell line lexer + classifier.
4. Execute the result. **Cycle guard:** maintain a per-invocation set of alias names being expanded; abort with `AliasError("recursive alias expansion: g → gst → g")` if re-encountered.
5. Return the exit code of the executed command(s).

For a function alias:

1. Bind arguments to declared params (using existing function-call binding logic).
2. Invoke the function via the VM.
3. Return the function's `LastExitCode` (since `$(...)` calls update it) — or `0` if the function returns normally without running shell commands.

### 6.4 Alias chaining & cycle detection

Aliases CAN reference other aliases. The cycle guard in §6.3 prevents infinite loops. Maximum chain depth: **32** (configurable later if needed). Exceeding it raises `AliasError`.

## 7. Argument Model

Argument forwarding is **always explicit**. Two mechanisms, depending on alias kind:

### 7.1 Template aliases

The body is a string with `${...}` interpolations. The interpolation receives the alias's argument list as a special `args` binding:

| Placeholder    | Meaning                                                                  |
| -------------- | ------------------------------------------------------------------------ |
| `${args}`      | Joined-by-space, properly-quoted arguments (`["a b", "c"]` → `"a b" c`) |
| `${args[N]}`   | Single argument N (0-indexed); error if out of range                     |
| `${argv}`      | Array literal of the arguments (used for advanced cases)                 |

If a template alias's body contains **no** `${args}` and the user passes arguments, the alias either:

- (default) **errors** with `AliasError("alias 'gst' takes no arguments; use template with ${args} placeholder")`
- (opt-in) appends them silently if the spec sets `appendArgs: true` in `AliasOptions` *(deferred — not in v1)*.

This is the strict interpretation that matches the chosen "no implicit magic" policy.

### 7.2 Function aliases

Use the standard Stash function parameter system: positional, defaults, rest (`...args`). Type annotations are validated. Examples:

```stash
alias g(msg: string) = $(git commit -m ${msg})
alias gco(branch: string = "main") = $(git checkout ${branch})
alias rgrep(...patterns) = $(grep -r ${patterns} .)
```

Argument errors (wrong count, wrong type) produce the same diagnostics as a normal function call.

### 7.3 Quoting from shell input

When the user types `g "fix: bug" --amend` at the shell, the shell line lexer parses three tokens: `g`, `"fix: bug"`, `--amend`. These become `args = ["fix: bug", "--amend"]`. Quoting/escaping in the input follows existing shell-line lexer rules (no new rules needed).

## 8. Pipeline & I/O Behavior

Aliases are first-class participants in pipelines:

```
cat foo.json | j .users[]                # j is `alias j = "jq ${args}"`
gst | grep modified                       # gst is `alias gst = "git status"`
g "fix" && gpush                          # both are aliases
```

### 8.1 Template aliases in pipelines

The expanded body is dropped into the pipeline at the alias's position. So `cat foo.json | j .users[]` with `alias j = "jq ${args}"` becomes `cat foo.json | jq .users[]`. The pipe wiring is handled by the existing pipeline machinery.

### 8.2 Function aliases in pipelines

A function alias in a pipeline stage receives `io.stdin` connected to the previous stage's output, and its `io.println`/`io.print`/`$(...)` output goes to the next stage. This is identical to how a Stash `fn` would behave if invoked in a pipeline (which requires existing pipeline-and-Stash-functions integration — already supported per current shell mode docs).

### 8.3 Exit code

`shell.lastExitCode()` reflects the alias's effective exit code:

- Template alias: exit code of the resulting expanded command(s).
- Function alias: `LastExitCode` after function returns, OR an explicit `return N` value if the function returns an integer.

## 9. Hooks (`before`, `after`, `confirm`)

Hooks are set via `AliasOptions` when calling `alias.define(...)`. The shell sugar does NOT expose hooks (deliberate — keeps the sugar simple). Examples:

```stash
alias.define("deploy", "./deploy.sh ${args}", AliasOptions {
    description: "Deploy to production",
    confirm: "Deploy to PROD? Type 'yes' to continue.",
    before: (name, args) => {
        log.info("deploying with args: ${args}");
        return true;          // return false to abort silently
    },
    after: (name, args, exitCode) => {
        if exitCode != 0 {
            log.error("deploy failed: exit ${exitCode}");
        }
    }
})
```

### 9.1 Execution order

For each invocation:

1. If `confirm` is set: prompt user; abort with exit code 130 if they decline.
2. If `before` is set: invoke it; abort with exit code 1 if it returns `false`.
3. Execute the alias body (template expansion or function call).
4. If `after` is set: invoke it (always, regardless of body exit code).
5. Return the body's exit code.

Hook failures (exceptions) propagate as `AliasError` and abort the alias. The `after` hook is NOT called if a `before` hook throws.

### 9.2 Hook parameters

- `before(name: string, args: array of string) -> bool`
- `after(name: string, args: array of string, exitCode: int) -> nil`
- `confirm` is a string prompt; the user types `y`/`yes` to accept, anything else to abort.

### 9.3 Recursion safety

Hooks run in normal Stash scope. If a hook itself invokes the same alias (`$(deploy ...)` inside a `before` hook for `deploy`), the cycle guard in §6.4 catches it.

## 10. Persistence

### 10.1 Default: ephemeral

Aliases defined at the REPL prompt or via `alias.define(...)` from any context exist for the current session only. Closing the REPL discards them.

### 10.2 Save flag

Two equivalent ways to persist:

```
alias --save g = "git ${args}"
```

```stash
alias.save("g")               // save one alias
alias.save()                  // save all aliases with source != "builtin"
```

`alias.save(...)` writes to a managed file: **`<config-dir>/aliases.stash`** (NOT `init.stash` / `.stashrc`, to avoid touching hand-edited files).

The managed file uses the same XDG-aware path resolution as `.stashrc`:

| Platform | Managed file path                                      |
| -------- | ------------------------------------------------------ |
| Linux    | `$XDG_CONFIG_HOME/stash/aliases.stash` or `~/.config/stash/aliases.stash` |
| macOS    | Same as Linux                                          |
| Windows  | `%APPDATA%/stash/aliases.stash`                        |

### 10.3 Load order on REPL startup

1. Load `init.stash` / `.stashrc` (existing behavior).
2. If `aliases.stash` exists, source it (it's plain Stash code containing `alias.define(...)` calls).

This means user-edited aliases in `.stashrc` (via the sugar) are loaded first; persisted runtime aliases override or augment them.

### 10.4 Managed file format

```stash
// Generated by Stash. Edit at your own risk; `alias --save` rewrites this file.
// Last updated: 2026-05-01T14:23:00Z

alias.define("g", "git ${args}");
alias.define("gst", "git status");
alias.define("deploy", "./deploy.sh ${args}", AliasOptions {
    confirm: "Deploy to PROD?"
});
```

### 10.5 Removing persisted aliases

```
unalias --save g       # remove from registry AND from managed file
```

Without `--save`, `unalias` only removes from the in-memory registry. If `aliases.stash` re-defines it on next startup, it comes back.

## 11. Tab Completion

Auto-derived completion for aliases is the killer Tier 3 feature. Rules:

### 11.1 Completing the alias name

The existing `complete` namespace gains an automatic source: when completing the first word of a shell-mode line, alias names are merged with PATH executables. (Implementation: `CompleteBuiltIns` queries `alias.names()`.)

### 11.2 Completing alias arguments

For **template aliases**, the system inspects the expanded form to determine what completions to offer:

- `alias g = "git ${args}"`: completing `g <TAB>` is treated as completing `git <TAB>` — the underlying command's completions apply.
- `alias gco = "git checkout ${args[0]}"`: same — completions for `git checkout`.

For **function aliases**, the system uses any registered completion via the existing `complete` namespace API. If none is registered, no argument completion is offered (just Stash's default file-completion).

### 11.3 Completion of `alias` and `unalias` themselves

- `alias <TAB>` → list of alias names.
- `unalias <TAB>` → list of alias names.
- `alias <name> = <TAB>` → list of PATH executables (likely first word of body).

## 12. Help Text & Introspection

### 12.1 `--help` flag

Every alias accepts `--help` (intercepted before invocation):

```
$ g --help
alias g — git
  body:        "git ${args}"
  description: (no description)
  defined in:  ~/.stashrc:42
```

For function aliases:

```
$ deploy --help
alias deploy — function alias
  signature:   deploy(env: string, ...flags)
  description: Deploy to production
  hooks:       confirm, before, after
  defined in:  <repl>
```

The `--help` flag is intercepted by the alias dispatcher BEFORE invoking the body. If a user genuinely wants to pass `--help` to the underlying command, they use the bypass: `\g --help` runs the real `git --help`.

### 12.2 `alias` with no args

Pretty-prints all aliases grouped by source (`builtin`, `rc`, `repl`, `saved`):

```
$ alias
[builtin]
  cd      — change directory
  pwd     — print working directory
  exit    — exit the shell
  quit    — alias for exit
  history — show command history

[rc]
  g       = "git ${args}"
  gst     = "git status"
  ll      = "ls -la"

[repl]
  tmpdeploy = "./deploy.sh staging"

[saved]
  deploy  = function alias (deploy.sh) [confirm, before, after]
```

### 12.3 `alias <name>`

Shows a single alias's definition.

## 13. Built-in Aliases

The 5 hardcoded shell sugars (`cd`, `pwd`, `exit`, `quit`, `history`) are reimplemented as built-in aliases registered at VM startup with `source: "builtin"`. They appear in `alias` output, can be inspected via `alias cd`, and CAN be overridden by user aliases — but only with `override: true` in `AliasOptions`:

```stash
// At REPL: this fails with "cannot override built-in alias 'cd' without override: true"
alias.define("cd", "pushd ${args[0]}");

// This succeeds:
alias.define("cd", "pushd ${args[0]}", AliasOptions { override: true });
```

`unalias cd` is rejected with `AliasError("cannot remove built-in alias 'cd'; use 'unalias --force cd' to disable for the session")`. The `--force` flag disables but does not delete (a session-level flag); next REPL startup restores it.

This solves a real problem: power users want `cd` to push to a directory stack, but novices should not accidentally break it.

## 14. Lazy Parsing (Performance)

Template alias bodies are stored as raw strings and parsed (lexed + classified) on first invocation. The parsed result is cached in the registry entry. This means:

- A `.stashrc` defining 200 aliases costs 200 string copies at startup, not 200 parses.
- First use of each alias pays the parse cost (~µs).
- Subsequent uses hit the cache.
- `alias.remove(...)` invalidates the cache for that name.
- `alias.define(...)` always invalidates and re-caches lazily.

Function aliases are compiled to bytecode at definition time (no laziness; the compiler runs immediately). This is consistent with how `fn` declarations work today.

## 15. Errors & Diagnostics

New error type: `AliasError` (extends `RuntimeError`, becomes a `StashError` when caught).

| Trigger                                              | Error                                                                                |
| ---------------------------------------------------- | ------------------------------------------------------------------------------------ |
| Recursive alias chain                                | `AliasError("recursive alias expansion: a → b → a")`                                 |
| Chain depth exceeds 32                               | `AliasError("alias chain too deep (max 32): a → b → c → ...")`                       |
| Template body has no `${args}` but args were passed  | `AliasError("alias 'gst' takes no arguments; add ${args} placeholder")`              |
| `${args[N]}` out of range                            | `AliasError("alias 'gco' references args[0] but no arguments were provided")`        |
| Override built-in without `override: true`           | `AliasError("cannot override built-in alias 'cd' without AliasOptions.override = true")` |
| `unalias` on built-in                                | `AliasError("cannot remove built-in alias 'cd'; use 'unalias --force' to disable")`  |
| Function alias signature mismatch                    | (delegated to existing function-call diagnostics)                                    |
| `confirm` declined                                   | (no error; exit code 130, alias body skipped)                                        |
| `before` returned false                              | (no error; exit code 1, alias body skipped)                                          |
| `before` / `after` hook threw                        | `AliasError("hook 'before' for alias 'deploy' threw: <inner>")`                      |

Static analyzer (`Stash.Analysis`) gains diagnostics for:

- `alias.define` called with a name that contains spaces or non-identifier characters → `STASH_ALIAS_001`.
- Template body referencing `${args[N]}` for N > declared params → not statically checkable for templates; runtime-only.
- `AliasOptions.confirm` set to empty string → `STASH_ALIAS_002` warning.

(Each diagnostic gets a `DiagnosticDescriptor` per the static-analysis instructions.)

## 16. Cross-Platform Considerations

- **Path resolution** for `aliases.stash` mirrors the existing `.stashrc` discovery (XDG on POSIX, `%APPDATA%` on Windows).
- **Windows v1 caveat:** Shell mode itself is disabled on Windows in v1 (per existing shell mode docs). The alias namespace is still available programmatically on Windows; it just has no shell-line dispatch path to hook into. This is consistent with the rest of shell mode.
- **PATH executable resolution** for shadow-detection uses the same cross-platform PATH walker the classifier already uses.
- **Line endings** in `aliases.stash`: written with the platform's native line ending; read tolerantly (LF or CRLF).

## 17. Implementation Plan

### 17.1 New files

| File                                                   | Purpose                                                |
| ------------------------------------------------------ | ------------------------------------------------------ |
| `Stash.Stdlib/BuiltIns/AliasBuiltIns.cs`               | `alias` namespace registration and dispatch logic      |
| `Stash.Stdlib/Models/AliasSpec.cs`                     | `AliasSpec`, `AliasOptions`, `AliasInfo`, `ParamInfo` records |
| `Stash.Bytecode/Runtime/AliasRegistry.cs`              | Process-global alias registry on the VM                |
| `Stash.Cli/Shell/AliasShellSugar.cs`                   | Desugars `alias name = ...`, `unalias name`, `alias --help`, etc. |
| `Stash.Cli/Shell/AliasDispatcher.cs`                   | New step in shell-line dispatch (between sugar and PATH) |
| `Stash.Cli/Shell/AliasPersistence.cs`                  | `aliases.stash` read/write                             |
| `Stash.Tests/AliasBuiltInsTests.cs`                    | xUnit tests for stdlib API                             |
| `Stash.Tests/AliasShellSugarTests.cs`                  | xUnit tests for shell sugar parsing                    |
| `Stash.Tests/AliasDispatchTests.cs`                    | xUnit tests for shell-line dispatch + cycle detection  |

### 17.2 Modified files

| File                                                   | Change                                                  |
| ------------------------------------------------------ | ------------------------------------------------------- |
| `Stash.Cli/Shell/ShellLineClassifier.cs`               | Bare-identifier resolution checks alias registry        |
| `Stash.Cli/Shell/ShellRunner.cs`                       | Insert alias dispatch step before PATH lookup           |
| `Stash.Cli/Shell/ShellSugarDesugarer.cs`               | Reimplement existing 5 sugars as registered builtins    |
| `Stash.Cli/Shell/RcFileLoader.cs`                      | After RC load, source `aliases.stash` if present        |
| `Stash.Stdlib/BuiltIns/CompleteBuiltIns.cs`            | Merge alias names into completions; delegate to underlying command for arg completions |
| `Stash.Bytecode/Compiler/Compiler.cs` (or partial)     | NO change expected — function aliases use existing function compilation path |
| `Stash.Analysis/Rules/`                                | Two new diagnostics (`STASH_ALIAS_001`, `STASH_ALIAS_002`) |
| `docs/Shell — Interactive Shell Mode.md`               | New §11 "Aliases"                                       |
| `docs/Stash — Standard Library Reference.md`           | New `alias` namespace section                           |

### 17.3 Phased rollout

1. **Phase A — stdlib core.** `alias.define` / `list` / `get` / `remove` / `exists` / `expand` / `exec`. No shell sugar yet. Tests for the registry + execution semantics.
2. **Phase B — shell-line dispatch.** Hook into `ShellRunner`, implement cycle detection, add `\name` bypass.
3. **Phase C — shell sugar.** `alias name = ...`, `alias name(...) = ...`, `unalias`, `alias --help`. Tests for desugaring.
4. **Phase D — built-in aliases.** Migrate `cd`/`pwd`/`exit`/`quit`/`history` to registered builtins. Verify backwards compatibility.
5. **Phase E — hooks.** `before`, `after`, `confirm`. Tests including hook recursion.
6. **Phase F — persistence.** `alias.save` / `alias.load`, `aliases.stash` file management, REPL startup integration.
7. **Phase G — tab completion.** Auto-derived completions, `alias`/`unalias` name completion.
8. **Phase H — analyzer rules + docs.** Static diagnostics, doc updates, examples.

### 17.4 Visitor updates

No new AST node is introduced (the shell sugar layer never produces an AST — it desugars textually to a `alias.define(...)` call before parsing). Therefore **no visitor changes are required**. This is a deliberate design win of the "stdlib-first" approach.

## 18. Test Plan

### 18.1 Stdlib API tests (`AliasBuiltInsTests.cs`)

- Define template alias; verify `list`, `get`, `exists`.
- Define function alias; verify call from Stash code returns expected value.
- `define` with duplicate name overrides previous (last-wins).
- `remove` returns true for existing, false for missing.
- `clear` removes user aliases but leaves builtins.
- `expand` substitutes `${args}`, `${args[0]}`, errors on out-of-range.
- `exec` returns correct exit code from underlying command.
- `define` rejects invalid identifier (`alias.define("g.s", ...)` → `AliasError`).

### 18.2 Shell sugar tests (`AliasShellSugarTests.cs`)

- `alias g = "git"` → produces `alias.define("g", "git")` call.
- `alias g(msg: string) = $(git commit -m ${msg})` → produces correct function alias.
- `alias gst = git status` (unquoted multi-word) → parse error.
- `unalias g` → `alias.remove("g")`.
- `alias --help` → prints help.
- `alias` with no args → list all.

### 18.3 Dispatch tests (`AliasDispatchTests.cs`)

- Bare-word `g status` invokes alias `g` with `args = ["status"]`.
- `\g status` bypasses alias, runs real `g` from PATH (or errors if not present).
- `!g status` bypasses alias, runs real `g` strictly.
- Cycle: `alias a = b; alias b = a; a` → `AliasError("recursive alias expansion: a → b → a")`.
- Chain: `alias a = b; alias b = c; alias c = "echo hi"; a` → echoes "hi".
- Pipeline: `cat foo | j` with `alias j = "jq ${args}"` works.

### 18.4 Hook tests

- `confirm` accepted → body runs.
- `confirm` declined → body skipped, exit 130.
- `before` returns true → body runs; returns false → body skipped, exit 1.
- `after` runs even on body failure.
- `after` does NOT run if `before` throws.
- Hook recursion triggers cycle guard.

### 18.5 Persistence tests

- `alias --save g = "git"` writes `aliases.stash`.
- Restart simulation: load `aliases.stash`, verify alias is present.
- `unalias --save g` removes from file.
- Saving alias with hooks serializes function references via `fn` fixtures (or rejects with clear message — see decision in §20).

### 18.6 Built-in alias tests

- `alias cd` shows builtin definition.
- `unalias cd` rejected.
- `alias.define("cd", ..., override: true)` succeeds.
- After override, `cd /tmp` uses the override.
- `unalias --force cd` (session) re-enables; restart restores builtin.

### 18.7 Cross-platform tests

- POSIX: `aliases.stash` discovered at all three XDG candidate paths in priority order.
- Windows: `alias.define` works programmatically; shell-line dispatch is no-op (consistent with shell-mode-disabled-on-Windows).

## 19. Out of Scope / Deferred

The following are explicitly deferred to follow-up specs (not closed off — just not v1):

| Feature                           | Why deferred                                       |
| --------------------------------- | -------------------------------------------------- |
| Subcommand aliases (`g.s`)        | Adds parsing/dispatch complexity; revisit when there's demand |
| Per-directory `.stashrc.local`    | Security model needs design (auto-execute on cwd change is risky) |
| Conditional `when:` predicates    | User can wrap `alias.define` in `if` blocks today   |
| Network alias bundles             | Couple with package manager work                    |
| History expansion display control | Rare ask; revisit if users complain                 |
| `appendArgs: true` opt-in for templates | Wait for real-world demand                    |
| Macro / AST aliases               | Hard veto                                          |

## 20. Decision Log

| # | Decision                                                                                              | Alternatives rejected                                          | Rationale                                                                                                       |
| - | ----------------------------------------------------------------------------------------------------- | -------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| 1 | One `alias` keyword unifying string-template AND function aliases                                     | String-only; function-only; two separate keywords              | Brevity for trivial case; full power for complex case; one mental model                                         |
| 2 | Aliases bare-word callable ONLY in shell-line dispatch                                                | Bare-word everywhere (incl. Stash code); parens always required | Preserves Stash's "explicit intent" in language code while giving shell mode the ergonomics of bash             |
| 3 | PATH-only shadowing                                                                                   | Full shadow (incl. Stash symbols/keywords); no shadowing       | Aliases can never break the language; users still get bash-like shadow over PATH                                |
| 4 | Template strings with **mandatory** `${args}` placeholders                                            | Implicit `$@` append; bash `$1 $@`; Stash `${1}`               | "No implicit magic" — Stash should be explicit. Forces users to declare arg-handling intent                     |
| 5 | Aliases live in shell mode + REPL only, NOT scripts                                                   | Everywhere; scripts via `--shell` opt-in                       | Scripts must be unambiguous Stash; shell mode is the explicit "implicit-zone"                                   |
| 6 | Ephemeral by default; `--save` flag persists to managed `aliases.stash`                               | Always ephemeral; always persistent; separate `aliasdef` keyword | Predictable default; explicit opt-in for persistence; managed file avoids touching user-edited `.stashrc`     |
| 7 | v1 Tier 3 features: tab completion + help text + hooks                                                | Conditional, subcommand, per-dir, history-display              | Three highest-leverage features; defer the rest pending real demand                                              |
| 8 | "Lazy aliases" = deferred parsing only (perf), no user-facing concept                                 | Memoizing `lazy:` flag                                         | Memoization is dangerous for shell commands (state changes); pure perf optimization is a clean win              |
| 9 | Pipeline support for both alias kinds                                                                 | Template-only; first-stage-only                                | Real shells let aliases work anywhere in pipelines; Stash already supports functions in pipelines               |
| 10| Alias chaining with cycle detection (max depth 32)                                                    | One-level expansion; no chaining                               | Bash users expect chaining; cycle guard + depth cap make it safe                                                |
| 11| Existing 5 sugars (`cd`, etc.) reimplemented as user-overridable built-in aliases                     | Leave hardcoded; reimplement as un-removable                   | Single mechanism (eats own dogfood); `override: true` flag prevents accidents; can't fully `unalias`            |
| 12| Hybrid syntax — `alias <name> = ...` is shell-mode sugar; Stash code uses `alias.define(...)`         | Reserved keyword; builtin function only                        | No language change; preserves stdlib-first; no new AST nodes; no visitor updates needed                          |
| 13| Function-alias body: both expression and block (mirroring lambda syntax)                              | Expression-only; block-only                                    | Matches existing Stash conventions; one-liners stay clean, multi-statement is possible                          |
| 14| Hooks (`before`/`after`/`confirm`) configured via `AliasOptions` only, NOT shell sugar                | Add hook syntax to sugar                                       | Keeps sugar minimal; power users go through `alias.define(...)`; sugar remains a one-liner                      |
| 15| Built-in aliases removable only via `--force` (session-disable, not delete)                           | Fully removable; fully un-removable                            | Lets advanced users disable for testing without breaking next session; preserves discoverability                |

### Open question (to revisit during Phase F)

**Persistence of function aliases with closures/hooks.** A function alias whose body captures REPL-defined variables can't be cleanly serialized to `aliases.stash`. Options:

- (a) Reject `alias.save` for function aliases that have non-trivial closures; emit a clear error.
- (b) Serialize only the source text of the function body (works if no captures are referenced; fails on first invocation if they are).
- (c) Require persisted function aliases to be defined as top-level `fn`s; `aliases.stash` only stores `alias.define("name", funcRef)`.

Provisional answer: **(c)**, since it forces clean separation between transient experimentation and persistent definitions. Final call: defer to Phase F.
