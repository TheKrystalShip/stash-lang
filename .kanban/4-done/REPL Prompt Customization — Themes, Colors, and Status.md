# REPL Prompt Customization — Themes, Colors, and Status

> **Status:** Design (in-progress, revised 2026-04-29) — pre-implementation, awaiting final approval
> **Created:** 2026-04-29
> **Author:** Spec Architect (with user)
> **Companion:** [`Bare Command Execution — Shell Mode for the REPL.md`](../4-done/Bare%20Command%20Execution%20%E2%80%94%20Shell%20Mode%20for%20the%20REPL.md) — this spec extends the shell-mode REPL with a customizable prompt.

> **Major revision (2026-04-29):** Architecture inverted. Themes, default prompt fn, palette type, and starter prompts are now **implemented in pure Stash** and shipped as bundled scripts. The C# side provides only the primitives (set/reset/render/context, palette getter, error handling, OSC 133 wrapping). See Decision Log entries 14–19.

---

## 1. Purpose

Make the Stash REPL prompt **customizable**, **colorful**, and **theme-aware** so the language is pleasant for daily interactive use as a shell. **The implementation philosophy is "C# provides the tools, Stash provides the policy"** — every default the user sees (default prompt fn, themes, starter prompts) is itself a Stash script, fully readable, copyable, and overridable.

Users opt into richer prompts through:

1. A **user-defined `fn prompt(ctx) -> string`** (registered by naming convention in the rc file, or programmatically via `prompt.set(fn)` so themes can be swapped mid-session).
2. A **palette-based theme system** (`prompt.theme.use(name)`) shipping a small curated set of popular themes (Default, Nord, Catppuccin Mocha, Monokai, Dracula, Gruvbox).
3. **Bundled starter prompts** (`prompt.use(name)`) covering common layouts (minimal, bash-classic, pure, developer, pwsh-style, powerline-lite).
4. A **lightweight `PromptContext` struct** (C#-defined, for LSP autocomplete) that exposes everything a prompt usually wants (cwd, user, host, exit code, time, git state).

The C# implementation surface is intentionally narrow: ~10 functions in a new `prompt` namespace, REPL renderer changes in `Stash.Cli/`, three small extensions to `term.*`, and zero changes to the lexer/parser/AST/VM. Everything else is Stash code shipped as embedded resources.

## 2. Non-Goals (v1)

- **Tab completion in the prompt** (handled separately if/when completion lands).
- **Right-side prompt (RPROMPT)** — single-line left prompt only in v1.
- **Transient prompt** (Powerlevel10k-style collapse on Enter).
- **Pre-prompt blank line** baked in. Users can return `"\n" + ...` from their fn.
- **Per-mode prompts** for Stash vs Shell mode. Mode classification (§4 of the shell spec) happens _after_ the user types, so the prompt cannot show a useful mode indicator ahead of input. Same prompt always.
- **Background async git polling.** Git state is computed lazily and synchronously when the user's prompt fn touches it (see §6.4).
- **Persistent prompt config** outside the rc file. There is no `~/.stash_prompt` that gets re-read on every prompt.
- **Truecolor (24-bit RGB) detection.** All built-in themes use the 256-color palette; users wanting RGB call `term.color(text, "#RRGGBB")` directly.
- **Shipping themes as separate package downloads.** All built-in themes are baked into the binary. Downloadable themes are _just `.stash` scripts_ the user runs, which call `prompt.set(...)` and `prompt.theme.register(...)`.

## 3. Activation & Scope

The prompt subsystem is active when **shell mode is active** (per `--shell`, `STASH_SHELL=1`, or RC file presence — see shell-mode spec §3). When shell mode is off, the existing minimal `stash> ` prompt remains and `prompt.*` calls still work but have no visible effect until the next shell-mode REPL session.

> **Note on REPL vs. shell mode:** The REPL is the read-eval-print loop. Shell mode is a configuration of the REPL that activates the bare-command classifier. They are stacked, not parallel. This spec adds a customizable prompt to the REPL renderer; whether shell-mode classification is on changes only the value of `ctx.mode` (see §5.1). All other behavior is identical.

`prompt.set(fn)` and `prompt.theme.use(name)` are **callable from any context** (rc files, scripts that execute in the REPL session, the REPL itself). This explicitly enables the workflow:

```
stash> $(curl https://example.com/cool-theme.stash | stash -)
# script calls prompt.set(...) and prompt.theme.use(...)
# next prompt looks different
```

> **Decision:** `prompt.set` is session-scoped and never persisted. Re-running Stash starts from the default. Persistence is the user's job (put it in the rc file).
>
> **Rationale:** Avoids a hidden mutable state file that could surprise users. Aligns with the "rc file is the only persistence" model from the shell spec.

## 4. Discovery & Registration

Two equivalent ways to set the prompt function:

### 4.1 Convention

If the user's rc file (or any script that runs during REPL startup) declares a top-level `fn prompt(ctx) { ... }`, the REPL picks it up automatically after the rc completes. Same for `fn prompt_continuation(ctx) { ... }`.

```stash
// ~/.stashrc
fn prompt(ctx) {
    return "${ctx.cwd} ➜ "
}
```

The REPL queries `vm.HasReplGlobal("prompt")` at the moment of rendering each prompt, so re-defining `fn prompt(...)` mid-session immediately takes effect.

### 4.2 Explicit registration

```stash
prompt.set(fn (ctx) { return "$ " })
prompt.setContinuation(fn (ctx) { return "  " })
prompt.reset()                  // restore defaults
prompt.resetContinuation()
```

`prompt.set(fn)` takes precedence over the convention. If both are present, the most recent `prompt.set` call wins. `prompt.reset()` re-enables the convention lookup (so the rc-defined `fn prompt` is in effect again).

> **Decision:** Both convention AND explicit registration ship in v1.
>
> **Alternatives rejected:**
>
> - _Convention-only:_ limits dynamic theme loading from a curl'd script.
> - _`prompt.set`-only:_ every rc file must call a stdlib function for the most basic case; uncomfortable for newcomers.
>
> **Risk:** Two ways to do the same thing can confuse users. Mitigated by docs that show convention as the default path and `prompt.set` as the "for theme scripts" path.

### 4.3 Discovery order, per render

```
1. Was prompt.set(fn) ever called this session, with no later prompt.reset()?
   → use that fn.
2. Else, is there a top-level `prompt` symbol in the REPL globals
   AND is it a function value?
   → use it.
3. Else, fall back to the built-in default prompt fn (§7).
```

## 5. The `PromptContext` Struct

A new built-in struct, registered in `StdlibRegistry.Types.cs` like the existing built-in error types (`ValueError`, `TypeError`, etc.). The struct is **immutable from Stash code** — fields are read-only — and is **constructed by the REPL** before each call.

### 5.1 Fields

```stash
struct PromptContext {
    // — Where —
    cwd: string,              // process.cwd() with $HOME prefix replaced by "~"
    cwdAbsolute: string,      // process.cwd() raw (no ~ substitution)

    // — Who —
    user: string,             // env.get("USER") on POSIX, env.get("USERNAME") on Windows
    host: string,             // short hostname (no domain)
    hostFull: string,         // full hostname (includes domain if available)

    // — When —
    time: StashDateTime,      // current local time at prompt render

    // — Last command —
    lastExitCode: int,        // process.lastExitCode (0 if no command has run yet)
    lineNumber: int,          // 1-based REPL line counter, increments per logical line

    // — Mode —
    mode: string,             // "shell" or "stash" — describes session activation, not next-line classification

    // — Visual hints (computed by Stash, free for the user to ignore) —
    hostColor: string,        // deterministic 256-color name for `host` (stable across sessions)

    // — Lazy: only fetched if accessed —
    git: PromptGit?,          // see §6.4
}
```

`StashDateTime` is the existing time type used elsewhere in the stdlib. Users format it via `time.format(ctx.time, "HH:mm:ss")`.

### 5.2 The continuation context

`fn prompt_continuation(ctx)` receives the same `PromptContext` plus two additional fields populated by the multi-line reader:

```stash
struct PromptContinuationContext {
    ...all fields of PromptContext...
    continuationDepth: int,   // 1 for first continuation line, 2 for second, ...
    continuationReason: string, // "open-paren", "open-bracket", "open-brace",
                                // "open-string", "trailing-backslash", "trailing-pipe"
}
```

`PromptContext` is a **subtype** of `PromptContinuationContext` only conceptually; in implementation they are separate structs and a user fn that types its parameter as `PromptContext` continues to work for the continuation prompt because Stash's struct-call semantics ignore extra fields.

### 5.3 Why a struct, not a dict

Per project convention (memory: "Stash Packages — Structs over Dicts"), structured data passed between the runtime and user code uses real structs. This also gives LSP/hover/completion working autocomplete inside `prompt(ctx)`. The PromptContext type is registered as a built-in struct (analogous to `ValueError`) so `is PromptContext` and `typeof(ctx)` work correctly.

> **Decision:** Pass a `PromptContext` struct; do NOT require users to import data from `process.*` or `env.*`.
>
> **Rationale:** Discoverability. The `ctx.` autocomplete is the documentation. Centralizes the snapshot — every field is consistent for the duration of one prompt render (no race between reading `process.cwd()` and `process.lastExitCode`).

## 6. Built-in Capabilities

### 6.1 The `prompt` namespace — C# primitives

These are the only `prompt.*` functions implemented in C#. Everything else (`prompt.theme.*`, `prompt.use`, the default fn, palette struct definition) is implemented in Stash and ships as bundled resources (see §6.5).

| Function                                  | Behavior                                                                                                                                                  |
| ----------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `prompt.set(fn)`                          | Register the prompt function. `fn` must accept exactly one argument. Throws `TypeError` if `fn` is not callable.                                          |
| `prompt.setContinuation(fn)`              | Register the continuation prompt function. Same shape rules as `prompt.set`.                                                                              |
| `prompt.reset()`                          | Forget the explicit `prompt.set` registration; fall back to convention or bundled default.                                                                |
| `prompt.resetContinuation()`              | Same, for the continuation prompt.                                                                                                                        |
| `prompt.context() -> PromptContext`       | Return a fresh `PromptContext` snapshot. Useful for testing prompt fns interactively.                                                                     |
| `prompt.render() -> string`               | Force a single prompt render (using whichever fn is active) and return the resulting string. Useful for tests and debugging.                              |
| `prompt.palette() -> Palette`             | Return the currently active palette. Reads from a C#-side slot that the bundled `prompt.theme.use` mutates. Returns `null` before the bootstrap has run.  |
| `prompt.setPalette(palette)`              | Low-level setter. The bundled `prompt.theme.use` calls this. User code rarely calls it directly — they use `prompt.theme.use` or `prompt.theme.register`. |
| `prompt.resetBootstrap()`                 | Re-extract the bundled prompt scripts from the embedded resources. See §6.5.                                                                              |
| `prompt.bootstrapDir() -> string`         | Return the absolute path of the extracted bootstrap directory (e.g., `~/.config/stash/prompt/`).                                                          |

### 6.2 The Stash-side `prompt.*` API (defined in bundled scripts)

These functions live in `bootstrap.stash` and use the C# primitives above. They are documented here because they form the user-facing API even though they are not C# code:

| Function                                | Behavior                                                                                                                              |
| --------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| `prompt.theme.use(name: string)`        | Apply a theme by name. Built-in names listed in §6.5. Throws `ValueError` if unknown. Calls `prompt.setPalette(...)` under the hood.  |
| `prompt.theme.register(name, palette)`  | Register a custom theme. `palette` is a `Palette` struct (§6.5). Replaces any existing theme with the same name.                      |
| `prompt.theme.current() -> string`      | Return the active theme name.                                                                                                         |
| `prompt.theme.list() -> array<string>`  | Return all registered theme names. Reads `bootstrapDir()/themes/` directory listing for available-but-not-yet-loaded themes.          |
| `prompt.use(name: string)`              | Apply a bundled starter prompt by name. Calls `prompt.set(...)` with the starter's fn. Throws `ValueError` if unknown.                |
| `prompt.register(name, fn)`             | Register a custom starter prompt that can later be activated by name.                                                                 |
| `prompt.list() -> array<string>`        | Return all registered starter prompt names.                                                                                           |

> **Decision:** Stash-implemented APIs are a thin wrapper around C# primitives. C# never sees "themes" or "starters" as concepts — only `setPalette` and `set`. This means the registry of themes/starters lives in Stash module-level dicts, not in C# state.
>
> **Risk:** A bug in `bootstrap.stash` could leave the user without `prompt.theme.use`. Mitigated by the C# fallback prompt (§7 step 4) and `STASH_NO_PROMPT_BOOTSTRAP=1` opt-out (§6.6).

### 6.3 Color helpers (existing `term`, lightly extended)

The existing `term.color(text, fg, bg?)`, `term.bold`, `term.dim`, `term.underline`, and `term.style` cover the common cases. We add two small extensions:

| Function                       | Behavior                                                                                                                                                                        |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `term.zeroWidth(text: string)` | Mark `text` as zero-width for prompt-width math. For OSC sequences and other non-SGR escapes (§9).                                                                              |
| `term.colorsEnabled() -> bool` | Returns `false` if `NO_COLOR` is set or the output isn't a TTY. Existing `term.color` already short-circuits, but exposing this lets prompt fns choose simpler rendering paths. |

> **Decision:** No `term.palette()`. The palette is a prompt-subsystem concern and lives at `prompt.palette()` (§6.1). Users compose `term.color(text, prompt.palette().accent)` to apply a palette color.
>
> **Decision:** No `prompt.style()` shortcut. It would be a thin alias for `term.color` and add no value.

### 6.4 Status indicator

Three independent mechanisms; each can be enabled/disabled by the prompt fn or theme:

| Indicator                     | Where it lives                                                                                                                                                             | Default                                                                                         |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| **OSC 133 prompt markers**    | Emitted by the REPL renderer wrapping every prompt (`OSC 133;A` before, `OSC 133;B` between prompt and command, `OSC 133;C` before output, `OSC 133;D;<exit-code>` after). | **Always emitted** unless `STASH_NO_OSC133=1` or the terminal is detected as not supporting it. |
| **Prompt char color**         | The default prompt fn renders the trailing `>` in `palette.success` if `lastExitCode == 0`, `palette.error` otherwise.                                                     | On in built-in default. User-defined fns choose their own.                                      |
| **Numeric exit code segment** | The default prompt fn shows `[N]` (in `palette.error`) only when `lastExitCode != 0`.                                                                                      | On in built-in default.                                                                         |

> **Decision:** OSC 133 is on by default with an opt-out env var. Most modern terminals (VS Code, iTerm2, WezTerm, Kitty, Windows Terminal recent builds) understand it, and unrecognized terminals silently ignore unknown OSC sequences — they will not display garbled text.
>
> **Risk:** Older terminals or `screen`/`tmux` configurations may pass through the escape literally. Mitigated by `STASH_NO_OSC133=1`. Auto-detection is best-effort: skip if `TERM=dumb`, `TERM=linux`, or `TERM` starts with `screen` (heuristic — tmux 3.2+ supports it, but older versions don't).

### 6.5 Bundled Stash scripts (`bootstrap.stash` and friends)

All user-facing defaults — the `Palette` struct, the default prompt fn, the 6 themes, the 6 starter prompts, and the `prompt.theme.*` / `prompt.use` / `prompt.register` Stash API — ship as embedded resources inside the `Stash.Cli` binary. On first shell-mode session activation, they are extracted to the user's config directory.

#### 6.5.1 Storage and extraction

- **Embedded path (in repo):** `Stash.Cli/Resources/prompt/`
- **Extracted path:** `~/.config/stash/prompt/` on Linux, macOS, **and Windows** (uses `USERPROFILE` on Windows for cross-platform consistency).
- **Extraction trigger:** First time shell mode activates AND the target directory is missing or stale (see §6.5.3). Non-shell-mode `stash` invocations and plain Stash REPL sessions never trigger extraction.
- **Banner:** Every extracted file begins with:

  ```stash
  // ============================================================
  // AUTO-GENERATED FROM EMBEDDED RESOURCES — DO NOT EDIT
  // This file will be overwritten on every Stash version upgrade.
  // To customize, put overrides in your ~/.stashrc file.
  // Documentation: https://stash-lang.dev/docs/prompt
  // ============================================================
  ```

- **Comment density:** Heavily commented. The extracted files double as inline documentation — reading `~/.config/stash/prompt/themes/nord.stash` is the canonical way to learn how a theme is built. Roughly 30% of lines are comments.

#### 6.5.2 File layout

```
~/.config/stash/prompt/
├── bootstrap.stash             // Entry point. Imports palette + default-prompt.
│                                // Defines prompt.theme.{use,register,current,list}
│                                // and prompt.{use,register,list} on top of C# primitives.
├── palette.stash               // struct Palette { fg, bg, accent, ... }
├── default-prompt.stash        // The default fn prompt(ctx) and fn prompt_continuation(ctx)
├── themes/
│   ├── default.stash
│   ├── nord.stash
│   ├── catppuccin-mocha.stash
│   ├── monokai.stash
│   ├── dracula.stash
│   └── gruvbox-dark.stash
└── starters/
    ├── minimal.stash
    ├── bash-classic.stash
    ├── pure.stash
    ├── developer.stash
    ├── pwsh-style.stash
    └── powerline-lite.stash
```

#### 6.5.3 Bootstrap execution timing

Bootstrap runs **once per process, at shell-mode initialization, BEFORE the user's `~/.stashrc` is loaded**. This ordering is critical: `~/.stashrc` may legitimately call `prompt.theme.use("nord")`, which is a Stash-defined function only available after `bootstrap.stash` has run.

Execution order:

1. Shell mode activates (CLI flag, env var, or rc presence detected).
2. C# checks if `~/.config/stash/prompt/` exists and matches the embedded version (compare a single `VERSION` file). If missing or stale, extract.
3. C# parses + executes `bootstrap.stash`. This registers the `Palette` struct, the default theme palette, the default prompt/continuation fns, and the `prompt.theme.*` / `prompt.use` / `prompt.register` Stash functions.
4. C# loads `~/.stashrc` (existing shell-mode behavior).
5. REPL begins.

#### 6.5.4 Lazy theme and starter loading

`bootstrap.stash` itself does NOT parse the per-theme or per-starter files. Instead:

- `prompt.theme.use("nord")` is implemented in Stash as: read `themes/nord.stash`, eval it (which defines a local palette), pass the palette to `prompt.setPalette(...)` and store it under `"nord"` in the in-memory theme registry. Subsequent calls to `use("nord")` hit the cache.
- `prompt.use("bash-classic")` follows the same pattern for `starters/bash-classic.stash`.
- `prompt.theme.list()` and `prompt.list()` enumerate by reading the directory listing (cheap), so users see all available names without paying parse cost up front.

This keeps first-shell-prompt latency to roughly: parse-bootstrap + parse-default-prompt + parse-default-theme. Adding more bundled themes does not slow first prompt.

#### 6.5.5 Version drift and reset

The extracted directory is treated as **read-only documentation that happens to be code**. On every Stash version upgrade, the bootstrap is re-extracted unconditionally (overwriting any local edits). This is documented behavior (the banner says so).

Version check: `bootstrap.stash` carries a `// VERSION: <stash-version>` comment on line 2. C# extracts a sibling `VERSION` file containing the same value. On shell-mode init, if the on-disk `VERSION` differs from the embedded one (or is missing), the entire `~/.config/stash/prompt/` directory is wiped and re-extracted.

Manual reset paths:

- **CLI:** `stash --reset-prompt` re-extracts unconditionally and exits.
- **REPL:** `prompt.resetBootstrap()` re-extracts and reloads `bootstrap.stash`. Useful when a user has been experimenting with edits and wants to start clean without restarting the shell.

#### 6.5.6 Opt-out

`STASH_NO_PROMPT_BOOTSTRAP=1` skips the entire bootstrap subsystem. The REPL falls back to the C# hardcoded floor:

- Prompt: literal `stash> `
- Continuation: literal `... `
- `prompt.set(fn)` still works; the user can supply their own fn entirely from `~/.stashrc`.
- `prompt.theme.*` and `prompt.use` are undefined (calling them throws `ReferenceError`). Users opting out of the bootstrap are explicitly opting out of the bundled API.

This is the cleanest escape hatch for users who want a totally bare slate, and the safety net for users whose bootstrap got corrupted somehow.

### 6.6 Git integration (lazy)

`ctx.git` is a **lazy property**. It returns `null` unless accessed; on first access in a single prompt render, the REPL runs `git` (in the cwd) and caches the result for the rest of that render. If git fails (no `git` binary, not in a repo, command times out), `ctx.git` resolves to `null`.

#### 6.6.1 Implementation note

Stash structs don't natively have lazy properties. Three options to implement:

1. **Eager-but-fast:** always run `git status` before each render. Rejected — even fast `git status` is ~10–50ms; users who don't want git pay the cost.
2. **Property-via-method:** expose `ctx.git()` as a method instead of a field. Rejected — feels inconsistent with the other fields and breaks the "all data in one place" pitch.
3. **Marker field + REPL hook:** the REPL constructs `PromptContext` with a synthetic `git: __PROMPT_GIT_THUNK__` sentinel. The VM intercepts field access on `PromptContext` for the `git` field and runs the lookup the first time. Rejected — too magical.
4. **Two-phase render with re-entry:** the REPL renders with `ctx.git = null`. If the rendered output contains a special marker (`%PROMPT_GIT_NEEDED%`), git is fetched and the prompt fn is invoked again with the populated value. Rejected — invokes user fn twice, side effects double.
5. **Just call it eagerly when the prompt fn declares it.** Provide `ctx.git` always, but populate it eagerly with a 150ms timeout. Simple, predictable.

**Resolution:** ship **option 5** in v1 (eager with timeout) but document `ctx.git` as nullable. Re-evaluate in v2 if perf complaints emerge — the "true lazy" version requires VM-level support for property accessors which is a larger investment than this spec warrants.

#### 6.6.2 `PromptGit` struct

```stash
struct PromptGit {
    isInRepo: bool,           // false → all other fields are meaningful zero/empty
    branch: string,           // current branch name, or short SHA (7 chars) if HEAD is detached
    isDirty: bool,            // staged + unstaged + untracked > 0
    stagedCount: int,         // git diff --cached --name-only | wc -l
    unstagedCount: int,       // git diff --name-only | wc -l
    untrackedCount: int,      // git ls-files --others --exclude-standard | wc -l
    ahead: int,               // commits in HEAD not in upstream (0 if no upstream)
    behind: int,              // commits in upstream not in HEAD
}
```

If git is not on PATH, every field is zero/empty/false. If `git` is on PATH but the cwd is not in a repo, `isInRepo: false` and the rest is zero. If git times out, the entire field is `null`.

The git query is implemented as a single invocation of `git status --porcelain=v2 --branch --untracked-files=normal` (one process, parsed locally), not multiple `git` shells. Timeout is configurable via `STASH_PROMPT_GIT_TIMEOUT_MS` (default `150`).

### 6.7 Themes

A theme is a `Palette` struct **defined in Stash** (`palette.stash`):

```stash
// palette.stash
//
// The Palette struct. All theme files return one of these.
// Fields are intentionally permissive (string) so themes can mix
// ANSI names, 256-color indexes, and hex colors freely.

struct Palette {
    fg: string,           // primary foreground (e.g. "white", "#dcdfe4")
    bg: string,           // primary background (typically "" / unset for transparent)
    accent: string,       // headings, prompt char
    muted: string,        // secondary info (host, timestamp)
    success: string,      // exit-code 0 prompt char
    error: string,        // exit-code != 0 prompt char + [N] segment
    warning: string,      // optional — used by starter prompts for git ahead/behind
    info: string,         // optional — used by starter prompts for branch name
    git_clean: string,    // green-ish
    git_dirty: string,    // yellow-ish or orange
    git_conflict: string, // red-ish (rebase/merge in progress)
    user: string,         // user@host segment
    host: string,         // user@host segment (often same as user but distinguishable)
    cwd: string,          // working directory color
}
```

Color values are passed straight to `term.color`, so any of the existing accepted forms work: ANSI names (`"red"`), bright variants (`"brightcyan"`), 256-color indexes (`"38;5;81"`), `#RRGGBB` hex, or empty string for "no color."

Bundled themes (one file each under `themes/`):

| Theme              | File                          | Style                                            |
| ------------------ | ----------------------------- | ------------------------------------------------ |
| `default`          | `themes/default.stash`        | Minimal: cyan accents, default fg, ANSI palette. |
| `nord`             | `themes/nord.stash`           | Cool blues/cyans (#88c0d0, #5e81ac, #a3be8c).    |
| `catppuccin-mocha` | `themes/catppuccin-mocha.stash` | Pastel-on-dark (#cba6f7, #f38ba8, #94e2d5).    |
| `monokai`          | `themes/monokai.stash`        | Warm-on-dark (#f92672, #a6e22e, #66d9ef).        |
| `dracula`          | `themes/dracula.stash`        | High contrast (#bd93f9, #ff79c6, #50fa7b).       |
| `gruvbox-dark`     | `themes/gruvbox-dark.stash`   | Earthy retro (#fb4934, #b8bb26, #fabd2f).        |

Each theme file is a small Stash script that returns a `Palette` instance. Adding a theme = drop a new `themes/whatever.stash` into `Stash.Cli/Resources/prompt/themes/` and rebuild.

Example (`themes/nord.stash`):

```stash
// Nord theme — https://www.nordtheme.com/
// Cool, arctic-inspired palette by Arctic Ice Studio.

Palette {
    fg:           "#d8dee9",
    bg:           "",
    accent:       "#88c0d0",  // frost.1 — primary cyan
    muted:        "#4c566a",  // polar-night.4 — dim gray
    success:      "#a3be8c",  // aurora.4 — green
    error:        "#bf616a",  // aurora.1 — red
    warning:      "#ebcb8b",  // aurora.3 — yellow
    info:         "#81a1c1",  // frost.2 — blue
    git_clean:    "#a3be8c",
    git_dirty:    "#ebcb8b",
    git_conflict: "#bf616a",
    user:         "#88c0d0",
    host:         "#81a1c1",
    cwd:          "#5e81ac",  // frost.3 — deep blue
}
```

> **Decision:** Themes are **palette-only**, not full prompt presets. The user's prompt fn (or the bundled default fn) reads from `prompt.palette()` and decides layout. This way themes compose with any prompt — switching from `nord` to `catppuccin` doesn't reset the user's chosen layout.
>
> **Decision:** `Palette` is a Stash struct (defined in `palette.stash`), not a C# record. This makes the schema user-extensible: starter prompts that need additional fields can `extend Palette` with their own conventions.

### 6.8 Default prompt fn (bundled, in Stash)

Lives in `default-prompt.stash`. Rendered when no user fn is registered. Result:

```
~/projects/foo > _
```

The actual file (heavily commented):

```stash
// default-prompt.stash
//
// The default Stash REPL prompt. Utilitarian: shows the working directory,
// a colored mark indicating last-command success/failure, and a numeric
// exit code segment when the last command failed.
//
// To customize, copy this file's content into ~/.stashrc and modify, then
// register with prompt.set(fn). For richer layouts see prompt.use("bash-classic")
// or any of the other bundled starters: minimal, pure, developer,
// pwsh-style, powerline-lite.

fn prompt(ctx) {
    let p = prompt.palette()
    let cwd = term.color(ctx.cwd, p.cwd)
    let mark_color = ctx.lastExitCode == 0 ? p.success : p.error
    let mark = term.color(">", mark_color)
    let exit_seg = ctx.lastExitCode == 0
        ? ""
        : " " + term.color("[" + str(ctx.lastExitCode) + "]", p.error)
    return cwd + exit_seg + " " + mark + " "
}

fn prompt_continuation(ctx) {
    let p = prompt.palette()
    return term.color("... ", p.muted)
}
```

> **Decision:** Default is utilitarian — cwd, mark char, optional `[N]` on failure. No time, no user, no host. Bash-like layouts ship as the `bash-classic` starter (`prompt.use("bash-classic")`).

### 6.9 Starter prompts (bundled, in Stash)

Six starter layouts ship under `starters/`. Each file defines a single named prompt fn that `prompt.use(name)` activates:

| Starter           | File                             | Layout                                                      |
| ----------------- | -------------------------------- | ----------------------------------------------------------- |
| `minimal`         | `starters/minimal.stash`         | Mark char only: `> ` (green) or `> ` (red on failure).      |
| `bash-classic`    | `starters/bash-classic.stash`    | `[HH:MM:SS] user@host:cwd$ ` colored à la traditional bash. |
| `pure`            | `starters/pure.stash`            | Sindre Sorhus's pure prompt: cwd in blue, `❯` in green/red. |
| `developer`       | `starters/developer.stash`       | cwd + git branch + dirty marker + ahead/behind.             |
| `pwsh-style`      | `starters/pwsh-style.stash`      | `PS C:\Users\Foo>` for PowerShell-familiar users.           |
| `powerline-lite`  | `starters/powerline-lite.stash`  | Pseudo-powerline using box-drawing chars + palette colors.  |

Users activate one with `prompt.use("bash-classic")`. Each file is fully commented for copy-paste customization.

## 7. Rendering Pipeline

Per render, before issuing the OS prompt:

```
1. Build PromptContext snapshot:
   • cwd  := process.cwd()  with $HOME/$USERPROFILE prefix replaced by "~"
   • user := env.get("USER") or env.get("USERNAME") or "unknown"
   • host := os.hostname() short form
   • time := time.now()
   • lastExitCode := process.lastExitCode
   • lineNumber += 1
   • git := (lazily-eager, see §6.6)

2. Resolve prompt fn:
   • prompt.set value, else `prompt` global, else bundled default fn (from default-prompt.stash),
     else hardcoded C# floor ("stash> ") if bootstrap failed or STASH_NO_PROMPT_BOOTSTRAP=1.

3. Invoke fn(ctx) inside a guarded VM call:
   • Caught: any RuntimeError or non-string return.
   • Timeout: 1000ms hard cap (configurable via STASH_PROMPT_TIMEOUT_MS).
   • Re-entry blocked: if a prompt render is already in flight (e.g. user fn calls
     prompt.render() recursively), abort with the literal "stash> " fallback.

4. If failure (§8): render fallback default + emit one-shot warning.

5. Wrap output with OSC 133 markers (if enabled, see §6.4).

6. Compute visible-width (§9), pass to LineEditor.

7. Print prompt + read input.
```

## 8. Error Handling

When step 3 above fails:

| Failure                                | First occurrence                                                                                       | Subsequent same-session occurrences    |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------ | -------------------------------------- |
| Prompt fn throws                       | Print warning to stderr: `prompt: error in user prompt fn — falling back to default. <ErrType>: <msg>` | Silent fallback (no repeated warning). |
| Prompt fn returns non-string           | Print warning: `prompt: user prompt fn returned <type>, expected string — falling back.`               | Silent.                                |
| Prompt fn times out (>1000ms)          | Print warning: `prompt: user prompt fn exceeded 1000ms — falling back.`                                | Silent.                                |
| OSC 133 emit failure (write-to-stderr) | Already best-effort; never fails the render.                                                           | —                                      |

A single `_promptWarningShown` flag lives on the REPL state. The user can clear it with `prompt.reset()` (which also re-enables the next failure to print the warning). After 5 consecutive failures, `prompt.set` is auto-reset to clear the broken fn.

> **Decision:** Catch + fallback + one-shot warning. Never propagate the exception — a thrown prompt would brick the REPL.
>
> **Risk:** Silent failures after the first warning could mask bugs. Mitigated by the `[5]` auto-reset and by `prompt.render()` (which DOES throw, exposing the exception, for users debugging their fn).

## 9. ANSI Width Computation

Line editors track cursor column position. The `prompt` string returned by the user's fn contains both visible characters AND zero-width SGR escapes (`\x1b[31m`, etc.). The renderer must report the **visible** width to the line editor.

### 9.1 SGR auto-detection

The renderer scans the prompt string for the regex `\x1b\[[\d;]*m` and excludes those byte ranges from width math. The remaining text is width-counted using the existing terminal-width utilities (which already understand combining characters and East-Asian wide chars in `term.*` helpers).

### 9.2 Non-SGR escapes

For OSC sequences, hyperlinks, custom escapes, etc., users wrap the invisible content in `term.zeroWidth("...")`. Internally `term.zeroWidth` brackets the text with private-use markers (`\x01` and `\x02` per the bash convention) that the width computer strips before display. The OSC 133 wrappers in §6.3 use `term.zeroWidth` automatically.

> **Decision:** Auto-detect SGR + explicit `term.zeroWidth` for everything else.
>
> **Rejected:** _Explicit-only_ — too easy to forget; new users would see broken cursor positioning.
>
> **Risk:** Auto-detect may miss future SGR-like sequences. Limited to the documented `CSI ... m` family which is stable.

## 10. Cross-Cutting Concerns

### 10.1 NO_COLOR

If `NO_COLOR` is set (any non-empty value, per [no-color.org](https://no-color.org/)) OR stdout is not a TTY, all `term.color`/`term.bold`/etc. calls return their input unchanged. The default prompt fn therefore degrades gracefully to plain text. `prompt.theme.use(...)` still works (palette swaps still happen) but the colors are inert.

`STASH_FORCE_COLOR=1` overrides `NO_COLOR` for users who want colors in non-TTY contexts (e.g., pipes to `less -R`).

### 10.2 Terminal resize (SIGWINCH)

On POSIX, the REPL already reacts to `SIGWINCH` for line-wrap math. We add a single side effect: invalidate any cached prompt-width state and re-render the current prompt at the new width. The user's prompt fn is NOT re-invoked on every resize (its result is cached for the current input session); only the wrapping/cursor-position recomputation runs.

### 10.3 Windows

- **ANSI:** Windows Terminal, modern Powershell, and Windows 10+ console support ANSI by default. Legacy `cmd.exe` will show raw escapes — users are expected to upgrade or disable colors via `NO_COLOR=1`.
- **OSC 133:** Windows Terminal supports it from build 1.18+. Older builds will see no ill effect (sequences ignored).
- **Hostname:** `os.hostname()` returns `COMPUTERNAME`.
- **Username:** `env.get("USERNAME")`.

### 10.4 Performance budget

Per-render budget: **< 5ms** for the default prompt with no git. Measured on the existing benchmark harness once implementation lands. The `git` segment is bounded by §6.4's 150ms timeout. If git is fast (<10ms typical for small repos) the user pays roughly that. The 1000ms total prompt timeout (§7 step 3) is a safety net, not an expected envelope.

### 10.5 Deterministic host coloring

`ctx.hostColor` is computed as: `format("38;5;%d", (fnv1a32(ctx.host) % 6) * 36 + 17)`. This maps to one of the 256-color indices that lands in the visually distinct mid-saturation band, avoiding the very dark (0–16) and very light (>240) ranges. Same hostname always renders the same color across sessions — useful when SSHed into multiple machines.

## 11. Documentation & Examples

A new docs page: `docs/Prompt — Customizing the REPL Prompt.md`. Contents:

1. Quick start — three-line `~/.stashrc` example with cwd + git branch.
2. The `PromptContext` reference table.
3. The `prompt` namespace reference (C# primitives + Stash-side API).
4. The bundled bootstrap — what's in `~/.config/stash/prompt/`, banner explanation, version-drift behavior.
5. The theme catalog with screenshots/ANSI samples. Each entry links to its `themes/<name>.stash` source.
6. The starter prompt gallery — each entry is a `prompt.use("<name>")` invocation plus a link to the `starters/<name>.stash` source.
7. Theming guide — writing a custom palette + `prompt.theme.register(...)`.
8. Custom-prompt guide — writing a `fn prompt(ctx)` from scratch, using `prompt.palette()` and `term.color`.
9. OSC 133 explainer — what the gutter dot is and how to disable it.
10. Opt-out — `STASH_NO_PROMPT_BOOTSTRAP=1` and what changes.

## 12. Implementation Plan

### 12.1 New files

**C# code:**

```
Stash.Stdlib/BuiltIns/PromptBuiltIns.cs           // C# primitive functions only
Stash.Stdlib/Models/PromptContext.cs              // record + factory (LSP autocomplete)
Stash.Stdlib/Models/PromptGit.cs                  // record
Stash.Cli/Repl/PromptRenderer.cs                  // builds context, invokes fn, applies OSC 133, computes width
Stash.Cli/Repl/PromptWidthCalculator.cs           // SGR-aware width math
Stash.Cli/Repl/GitStatusProbe.cs                  // single git invocation + parse
Stash.Cli/Repl/BootstrapExtractor.cs              // embedded resource → ~/.config/stash/prompt/
Stash.Cli/Repl/BootstrapLoader.cs                 // parses + executes bootstrap.stash
Stash.Tests/Stdlib/PromptBuiltInsTests.cs
Stash.Tests/Cli/PromptRendererTests.cs
Stash.Tests/Cli/PromptWidthCalculatorTests.cs
Stash.Tests/Cli/BootstrapExtractorTests.cs
docs/Prompt — Customizing the REPL Prompt.md
```

**Embedded Stash resources** (under `Stash.Cli/Resources/prompt/`, listed as embedded resources in `Stash.Cli.csproj`):

```
bootstrap.stash                         // entry; defines prompt.theme.* and prompt.use
palette.stash                           // struct Palette { ... }
default-prompt.stash                    // default fn prompt(ctx) and prompt_continuation
VERSION                                  // single line: stash version this bundle was built with
themes/default.stash
themes/nord.stash
themes/catppuccin-mocha.stash
themes/monokai.stash
themes/dracula.stash
themes/gruvbox-dark.stash
starters/minimal.stash
starters/bash-classic.stash
starters/pure.stash
starters/developer.stash
starters/pwsh-style.stash
starters/powerline-lite.stash
```

### 12.2 Modified files

| File                                            | Change                                                                                            |
| ----------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `Stash.Stdlib/Registry/StdlibRegistry.cs`       | Register `prompt` namespace (C# primitives only).                                                 |
| `Stash.Stdlib/Registry/StdlibRegistry.Types.cs` | Register `PromptContext`, `PromptGit` as built-in structs. (Palette is a Stash-side struct now.)  |
| `Stash.Stdlib/BuiltIns/TermBuiltIns.cs`         | Add `term.zeroWidth(text)`, `term.colorsEnabled()`. (No `term.palette` — moved to `prompt.palette`.) |
| `Stash.Cli/Program.cs` (`RunRepl`)              | Replace hardcoded `"stash> "` with `PromptRenderer.Render()` call. Same for `"... "`. Wire bootstrap loader before rc loader when shell mode activates. |
| `Stash.Cli/Program.cs` (CLI args)               | Add `--reset-prompt` flag.                                                                        |
| `Stash.Cli/Stash.Cli.csproj`                    | Mark `Resources/prompt/**/*` as embedded resources.                                               |
| `Stash.Cli/Repl/MultiLineReader.cs`             | Pass continuation context to renderer.                                                            |
| `Stash.Cli/Shell/RcFileLoader.cs`               | No code changes — bootstrap loader runs first; rc convention discovery is per-render.             |
| `docs/Stash — Standard Library Reference.md`    | Add `prompt` namespace section (both C# and Stash-side APIs).                                     |
| `docs/Shell — Interactive Shell Mode.md`        | Cross-link to the new prompt doc.                                                                 |
| `CHANGELOG.md`                                  | Note new namespace + REPL behavior.                                                               |

### 12.3 Phase breakdown

1. **C# foundations** — `PromptContext`, `PromptGit` record types; `term.zeroWidth()`, `term.colorsEnabled()`; the C# `prompt.*` primitives (`set`, `reset`, `palette`, `setPalette`, `render`, `context`, `bootstrapDir`, `resetBootstrap`).
2. **Renderer** — `PromptRenderer`, `PromptWidthCalculator`. Hardcoded floor prompt (`stash> `) wired into `RunRepl`. No user-fn or bootstrap dispatch yet.
3. **User function dispatch** — `prompt.set`, `prompt.reset`, convention lookup in REPL state. `prompt.render()`, `prompt.context()`.
4. **Bootstrap extractor** — `BootstrapExtractor`. Embed all `Resources/prompt/**` files. On shell-mode init, extract to `~/.config/stash/prompt/` if missing or version mismatch. `--reset-prompt` CLI flag.
5. **Bootstrap loader** — `BootstrapLoader`. Parse + execute `bootstrap.stash`, `palette.stash`, `default-prompt.stash` before rc loads. Implement Stash-side `prompt.theme.*` and `prompt.use` / `prompt.register` in `bootstrap.stash`. Lazy theme/starter loading.
6. **Bundled themes** — write the 6 theme files. Each is ~25 lines including comments.
7. **Bundled starters** — write the 6 starter files. Each is ~30–60 lines including comments.
8. **Git probe** — `GitStatusProbe`, populate `ctx.git` eagerly with timeout. `STASH_PROMPT_GIT_TIMEOUT_MS` env var.
9. **OSC 133** — wrap renders in markers, terminal-detection heuristic, `STASH_NO_OSC133` opt-out.
10. **Continuation prompt** — `prompt.setContinuation`, `prompt_continuation` convention, `PromptContinuationContext`.
11. **Error handling** — guarded invocation, one-shot warning, auto-reset after 5 failures, prompt-timeout enforcement, `STASH_NO_PROMPT_BOOTSTRAP=1` opt-out.
12. **Cross-platform polish** — Windows hostname/username, SIGWINCH re-render, NO_COLOR/STASH_FORCE_COLOR.
13. **Docs** — new prompt doc, starter gallery, screenshots.
14. **Final review** — performance benchmark (must hit <5ms per render budget), move spec to 4-done.

## 13. Static Analysis

No new analysis rules required for v1. Two are worth considering as follow-ups:

- **SA08xx (Prompt) — Prompt fn returns non-string at compile time.** If the user's `fn prompt(ctx) { return 5 }` can be detected statically (return-type inference), warn at parse time rather than at render time.
- **SA08xx (Prompt) — Color arg in non-TTY context.** Out of scope; would require flow analysis of `term.colorsEnabled()`.

Both are tracked as future follow-ups, not blockers for v1.

## 14. LSP & Tooling

- **Completion:** when `ctx.` is typed inside a function whose parameter is annotated `PromptContext`, the LSP offers field completions automatically (the type is registered in `StdlibRegistry.Types.cs`, which the LSP reads).
- **Hover:** built-in struct hover already works via `StdlibRegistry`; add docstrings for each `PromptContext` field.
- **Doc strings:** the C# `prompt.*` primitives carry JSDoc-style comments registered in the namespace metadata. Stash-defined `prompt.theme.*` and `prompt.use` get hover docs from doc-comments in `bootstrap.stash`.
- **`Palette` LSP:** because `Palette` is a Stash-defined struct in `bootstrap.stash`, the LSP picks up its definition automatically when analyzing files in the workspace. Hover and completion work for `palette.accent` etc., scoped to whatever the LSP has indexed. Users editing prompt fns inside a project where `bootstrap.stash` is not in scope get no completion — acceptable trade-off; they can use `prompt.palette()` to get a runtime value.
- **DAP:** no impact; prompt rendering happens outside debug sessions.

## 15. Test Scenarios

Minimum coverage for the test suite:

### 15.1 PromptRenderer

- Returns the built-in default prompt when no user fn is registered.
- Returns user-fn output when `prompt.set` was called.
- Returns convention `prompt` global when no `prompt.set`.
- `prompt.set` takes precedence over convention.
- `prompt.reset()` re-enables convention.
- User fn throws → fallback default + one-shot warning + flag set.
- User fn throws second time → silent fallback.
- User fn returns non-string → fallback + warning.
- User fn exceeds 1000ms → fallback + warning. (Mocked timeout for determinism.)
- 5 consecutive failures → `prompt.set` auto-cleared.
- `prompt.render()` invoked recursively from within the prompt fn → re-entry guard returns literal `stash> `.
- OSC 133 markers wrap the output when enabled; absent when `STASH_NO_OSC133=1` or `TERM=dumb`.

### 15.2 PromptWidthCalculator

- Plain ASCII width = char count.
- ANSI SGR escapes excluded from width.
- `term.zeroWidth(...)` content excluded.
- East-Asian wide chars count as 2.
- Combining chars count as 0.
- Edge: prompt is empty.
- Edge: prompt is only escape sequences.
- Edge: malformed escape (truncated `\x1b[`) is treated as literal.

### 15.3 PromptBuiltInsTests

- `prompt.set` registers; `prompt.render()` returns the result.
- `prompt.set(5)` (non-callable) throws TypeError.
- `prompt.set(fn(a, b) {})` (wrong arity) throws TypeError.
- `prompt.context()` returns a populated PromptContext.
- `prompt.palette()` returns the palette set by the bundled bootstrap (or `null` if `STASH_NO_PROMPT_BOOTSTRAP=1`).
- `prompt.setPalette(palette)` updates `prompt.palette()`.
- `prompt.bootstrapDir()` returns the resolved path on each platform.

### 15.4 BootstrapExtractorTests

- Fresh extraction: empty target dir → all embedded files written, `VERSION` file matches embedded version.
- Stale extraction: `VERSION` mismatch → entire dir wiped and re-extracted.
- Up-to-date: `VERSION` matches → no files touched.
- Banner present: every extracted `.stash` file starts with the AUTO-GENERATED banner.
- `prompt.resetBootstrap()` re-extracts unconditionally.
- `--reset-prompt` CLI flag re-extracts and exits.
- `STASH_NO_PROMPT_BOOTSTRAP=1` skips extraction entirely.
- Windows path resolves to `%USERPROFILE%\.config\stash\prompt\` (cross-platform consistency).
- Permission failure on extraction → emit warning to stderr, fall back to hardcoded floor prompt, REPL still launches.

### 15.5 BootstrapLoaderTests (Stash-side API)

- After bootstrap runs: `prompt.theme.use`, `prompt.theme.register`, `prompt.theme.current`, `prompt.theme.list`, `prompt.use`, `prompt.register`, `prompt.list` are all defined.
- After bootstrap runs: `Palette` struct is defined and constructible.
- `prompt.theme.use("nord")` parses `themes/nord.stash` lazily, calls `prompt.setPalette(...)`, and `prompt.theme.current()` returns `"nord"`.
- `prompt.theme.use("nonexistent")` throws ValueError.
- `prompt.theme.register("custom", palette)` adds to the in-memory registry.
- `prompt.theme.list()` includes both bundled and registered themes (uses directory listing for bundled).
- `prompt.use("bash-classic")` parses `starters/bash-classic.stash` lazily and calls `prompt.set(...)`.
- `prompt.use("nonexistent")` throws ValueError.
- `STASH_NO_PROMPT_BOOTSTRAP=1`: `prompt.theme.*` and `prompt.use` are undefined; calling throws ReferenceError.
- Bootstrap with intentional syntax error → C# catches, emits warning, falls back to floor prompt; REPL still launches.

### 15.6 GitStatusProbe

- Outside a git repo → `isInRepo: false`.
- Inside a clean repo → `isDirty: false`, all counts 0.
- With staged file → `stagedCount: 1`, `isDirty: true`.
- With unstaged + untracked → matches.
- Detached HEAD → branch is short SHA.
- No upstream → `ahead: 0, behind: 0`.
- Git binary missing → `null`.
- Git times out (mocked) → `null`.
- Operation in progress (`.git/rebase-merge` exists) → branch annotated `(rebasing)`. (Optional v1 nice-to-have; can defer.)

### 15.7 Integration

- End-to-end: rc file declaring `fn prompt(ctx)` is picked up on REPL start.
- End-to-end: `prompt.set(fn)` mid-session takes effect on next prompt.
- End-to-end: rc file calling `prompt.theme.use("nord")` works (proves bootstrap-before-rc ordering).
- End-to-end: rc file calling `prompt.use("bash-classic")` works.
- End-to-end: `STASH_NO_PROMPT_BOOTSTRAP=1` + rc file calling `prompt.theme.use(...)` → ReferenceError surfaced cleanly.
- Cross-platform: prompt renders on Linux, macOS, Windows (Github Actions matrix).
- Cross-platform: bootstrap extracts to `~/.config/stash/prompt/` on all three (Windows uses `USERPROFILE`).

## 16. Migration & Compatibility

- **Breaking:** none. The current REPL shows `stash> `; the new default shows `<cwd> > ` with a colored mark. Users on `NO_COLOR=1` see plain `<cwd> > `. Anyone scripting against the prompt string (unlikely) gets a one-line note in the changelog.
- **Forward-compat:** `PromptContext` adding new fields later is non-breaking — Stash struct destructuring tolerates extra fields. The bundled `Palette` struct adding new fields is non-breaking — user prompt fns ignore unrecognized palette fields.
- **Bootstrap upgrade behavior:** On every Stash version upgrade, the `~/.config/stash/prompt/` directory is wiped and re-extracted. Local edits are destroyed. The banner on every file warns about this. Users who want persistent customization use `~/.stashrc`.

## 17. Decision Log

| #   | Decision                                                                         | Rejected alternative            | Why                                                                                                                                                 |
| --- | -------------------------------------------------------------------------------- | ------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | Function-only configuration model                                                | Format string with placeholders | Maximum flexibility; user explicitly preferred this. Format strings limit expressiveness for things like git/conditionals.                          |
| 2   | Both convention and `prompt.set(fn)`                                             | Convention only                 | Free-text request enabled `prompt.set` for theme-script workflow.                                                                                   |
| 3   | `PromptContext` struct argument                                                  | No-arg + pull from namespaces   | Discoverability via autocomplete; consistent snapshot per render.                                                                                   |
| 4   | Eager git probe with 150ms timeout                                               | True lazy property              | True lazy needs property-accessors on structs — separate larger spec. Eager+timeout is good enough for v1 and matches typical sub-50ms git latency. |
| 5   | Palette-only themes + starter prompt examples                                    | Full prompt presets             | Themes compose with any prompt; users keep layout when switching colors.                                                                            |
| 6   | Utilitarian default prompt                                                       | Bash-like full default          | User free-text said "utilitarian, give freedom"; multi-choice contradicted. Free-text wins. Bash-like ships as `bash-classic` starter example.      |
| 7   | Auto-detect SGR widths + explicit `term.zeroWidth`                               | Explicit-only                   | Auto-detect handles 99% of cases; explicit wrapper covers OSC and exotic escapes.                                                                   |
| 8   | OSC 133 on by default + opt-out env var                                          | Off by default + opt-in         | Most modern terminals support it; unrecognized terminals silently ignore; opt-out covers `screen`/legacy.                                           |
| 9   | Same prompt for shell + Stash modes                                              | Per-mode prompts                | Mode is decided after the user types; can't pre-show.                                                                                               |
| 10  | No RPROMPT/transient/pre-newline in v1                                           | Include them                    | Scope discipline. Each is a follow-up if desired.                                                                                                   |
| 11  | `prompt.set` is session-scoped, never persisted                                  | Persist to a config file        | Avoids a hidden mutable state file; rc is the single source of truth.                                                                               |
| 12  | Catch all prompt errors + one-shot warning + auto-reset after 5                  | Let the error propagate         | Otherwise a broken prompt fn bricks the REPL.                                                                                                       |
| 13  | 6 baked themes (default, nord, catppuccin-mocha, monokai, dracula, gruvbox-dark) | More or fewer                   | Cover the most popular requested + leave room for `prompt.theme.register` for the rest.                                                             |
| 14  | C# provides primitives only; themes/default fn/starters are pure Stash           | Original C#-baked themes        | Maximum user agency. Users can read every default, copy any of it, override with one rc-file line. Avoids API asymmetry between built-in and user-registered themes. Performance impact (~10–20ms parse cost) absorbed by lazy loading and bootstrap-on-shell-mode-only. |
| 15  | `Palette` is a Stash struct in `palette.stash`, not a C# record                  | C# struct in `StdlibRegistry`   | User-extensible: starter prompts can `extend Palette` with their own fields. Trades LSP completion (the C# struct gave free hover/autocomplete) for schema flexibility. |
| 16  | `PromptContext` stays a C#-registered built-in struct                            | Plain dict from C#              | LSP autocomplete on `ctx.cwd` etc. is worth the 30 lines of C#. PromptContext shape is API and rarely changes; Palette shape is data and changes often. Different stability profiles → different implementation choices. |
| 17  | Bundled scripts extract to `~/.config/stash/prompt/`; READ-ONLY (banner warns)   | Live as embedded only OR fully editable | Embedded-only loses discoverability (`cat ~/.config/...` is the tutorial). Fully editable means version upgrades silently break user customizations. Read-only-with-banner threads the needle: users see the canonical implementation, edits go through `~/.stashrc`. |
| 18  | Bootstrap runs at shell-mode init (BEFORE rc), not lazily on first prompt        | Lazy on first prompt render     | `~/.stashrc` may legitimately call `prompt.theme.use("nord")`, which is Stash-defined. Bootstrap must precede rc. Cost is still zero for non-shell-mode invocations (which is what "lazy" was protecting). |
| 19  | Lazy per-theme/per-starter loading                                               | Eager load all themes upfront   | First-prompt latency stays low regardless of how many themes ship. `prompt.theme.list()` reads directory listing for discovery without parsing. |
| 20  | Bundled scripts re-extracted on every version bump (overwriting local edits)     | Never overwrite, or hash-based merge | Honest model: "these are docs that happen to be code." Banner warns. `--reset-prompt` and `prompt.resetBootstrap()` are escape hatches when state goes weird. Hash-based merges are clever but opaque. |
| 21  | `STASH_NO_PROMPT_BOOTSTRAP=1` is the single opt-out                              | Granular per-subsystem opt-outs | One env var, one mental model. Users debugging weirdness flip one switch. C# falls back to the hardcoded floor prompt. |
| 22  | No `prompt.style()`; users compose `term.color(text, prompt.palette().accent)`   | Add a thin alias                | Pure overlap with `term.color`. No API duplication. Slightly more typing in user prompt fns is fine — the explicit composition makes the palette-vs-raw distinction visible. |
| 23  | Palette getter is `prompt.palette()`, not `term.palette()`                       | Original spec's `term.palette`  | Palette is a prompt-subsystem concern. Putting it on `term.*` is a layering inversion (`term` is low-level terminal capability; palette is policy). |
