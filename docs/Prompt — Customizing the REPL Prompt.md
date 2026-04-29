# Stash — Customizing the REPL Prompt

> **Status:** Stable (v1 — Linux, macOS; Windows with caveats)
> **Created:** April 2026
> **Purpose:** Reference for customizing the Stash REPL prompt via the `prompt` namespace, themes, starters, and the bundled bootstrap.
>
> **Companion documents:**
>
> - [Shell — Interactive Shell Mode](Shell%20%E2%80%94%20Interactive%20Shell%20Mode.md) — bare commands, pipelines, RC file
> - [Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md) — all built-in namespace functions

---

## Table of Contents

1. [Quick Start](#1-quick-start)
2. [PromptContext Reference](#2-promptcontext-reference)
3. [PromptGit Reference](#3-promptgit-reference)
4. [`prompt` Namespace Reference](#4-prompt-namespace-reference)
5. [Theme & Starter Globals](#5-theme--starter-globals)
6. [The Bundled Bootstrap](#6-the-bundled-bootstrap)
7. [Theme Catalog](#7-theme-catalog)
8. [Starter Gallery](#8-starter-gallery)
9. [Theming Guide](#9-theming-guide)
10. [Custom Prompt Guide](#10-custom-prompt-guide)
11. [Continuation Prompt](#11-continuation-prompt)
12. [OSC 133 Markers](#12-osc-133-markers)
13. [Status Indicators](#13-status-indicators)
14. [Color Helpers](#14-color-helpers)
15. [Error Handling](#15-error-handling)
16. [Performance](#16-performance)
17. [Cross-Platform Notes](#17-cross-platform-notes)
18. [Opt-Out](#18-opt-out)

---

## 1. Quick Start

Place the following in your `~/.stashrc` (or `~/.config/stash/init.stash`) to show the current directory and git branch:

```stash
prompt.set((ctx) => {
    let p = prompt.palette();
    let g = ctx.git != null && ctx.git.isInRepo ? " (" + ctx.git.branch + ")" : "";
    return term.color(ctx.cwd, p.cwd) + term.color(g, p.info) + " > ";
})
```

Result: `~/projects/stash (main) > `

The prompt function receives a `PromptContext` struct and must return a string. If it returns a non-string or throws, Stash falls back to `stash> ` and prints a one-shot warning to stderr.

---

## 2. PromptContext Reference

The context struct passed to every prompt function.

| Field          | Type         | Description                                                                                                            |
| -------------- | ------------ | ---------------------------------------------------------------------------------------------------------------------- |
| `cwd`          | `string`     | Current working directory, tilde-collapsed (e.g. `~/projects/stash`)                                                   |
| `cwdAbsolute`  | `string`     | Current working directory, absolute path (e.g. `/home/alice/projects/stash`)                                           |
| `user`         | `string`     | Current username (`USER` / `LOGNAME` env var; falls back to `"unknown"`)                                               |
| `host`         | `string`     | Short hostname, without domain (e.g. `mybox`)                                                                          |
| `hostFull`     | `string`     | Fully-qualified hostname (e.g. `mybox.example.com`; may equal `host` if FQDN is unavailable)                           |
| `time`         | `string`     | Wall-clock time at render time, formatted `HH:mm:ss`                                                                   |
| `lastExitCode` | `int`        | Exit code of the most recent command (same as `process.lastExitCode()`)                                                |
| `lineNumber`   | `int`        | 1-based count of REPL inputs evaluated in this session                                                                 |
| `mode`         | `string`     | `"stash"` or `"shell"` — the active line-classification mode                                                           |
| `hostColor`    | `string`     | A stable color string (`term.*`) derived from the hostname hash — useful for multi-host prompts                        |
| `git`          | `PromptGit?` | Git status at the current directory, or `null` if `git` is unavailable or timed out (see [§3](#3-promptgit-reference)) |

---

## 3. PromptGit Reference

`ctx.git` is `null` when the `git` binary is not on `PATH`, when the git probe times out (default 150 ms — see [§16](#16-performance)), or when the call is made outside a git repository with `isInRepo: false`.

When `ctx.git` is not `null`:

| Field            | Type     | Description                                                    |
| ---------------- | -------- | -------------------------------------------------------------- |
| `isInRepo`       | `bool`   | `true` if the cwd is inside a git repository                   |
| `branch`         | `string` | Active branch name, or `"HEAD"` when detached                  |
| `isDirty`        | `bool`   | `true` if there are any staged, unstaged, or untracked changes |
| `stagedCount`    | `int`    | Number of staged files                                         |
| `unstagedCount`  | `int`    | Number of unstaged (modified-but-not-staged) files             |
| `untrackedCount` | `int`    | Number of untracked files                                      |
| `ahead`          | `int`    | Commits ahead of the upstream branch (0 if no upstream)        |
| `behind`         | `int`    | Commits behind the upstream branch (0 if no upstream)          |

> **Note:** When the cwd is not inside a git repository, `ctx.git` has `isInRepo: false` and all other fields set to their zero values (`""`, `false`, `0`). Check `ctx.git != null && ctx.git.isInRepo` before accessing branch or status fields.

---

## 4. `prompt` Namespace Reference

These are the primitive C# built-in functions in the `prompt` namespace. Theme and starter helpers are provided by the bundled bootstrap as Stash-level globals — see [§5](#5-theme--starter-globals).

| Function                                 | Returns         | Description                                                                                                                                                         |
| ---------------------------------------- | --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `prompt.set(fn)`                         | `null`          | Set the active prompt function. `fn` must accept one `PromptContext` argument and return a `string`. Takes precedence over the `fn prompt(ctx)` convention.         |
| `prompt.setContinuation(fn)`             | `null`          | Set the continuation prompt function (shown for multi-line input). `fn` receives the same `PromptContext` plus `continuationDepth` and `continuationReason` fields. |
| `prompt.reset()`                         | `null`          | Reset the prompt to the default (`stash> ` or the cwd-based prompt when bootstrap is active).                                                                       |
| `prompt.resetContinuation()`             | `null`          | Reset the continuation prompt to the default (`... `).                                                                                                              |
| `prompt.context() -> PromptContext`      | `PromptContext` | Return a freshly-computed `PromptContext` struct reflecting the current REPL state.                                                                                 |
| `prompt.render() -> string`              | `string`        | Render the prompt string using the currently active prompt function, without displaying it. Useful for testing your prompt function.                                |
| `prompt.palette() -> Palette`            | `Palette`       | Return the active color palette as a `Palette` struct.                                                                                                              |
| `prompt.setPalette(palette)`             | `null`          | Replace the active palette. Accepts a `Palette` struct. Throws `TypeError` if the argument is not a `Palette`.                                                      |
| `prompt.themeRegister(name, palette)`    | `null`          | Register a named theme. `name` is a string; `palette` is a `Palette` struct. Overwrites an existing theme with the same name.                                       |
| `prompt.themeUse(name)`                  | `null`          | Activate a registered theme by name. Throws `ValueError` if the theme is not registered.                                                                            |
| `prompt.themeCurrent() -> string`        | `string`        | Return the name of the currently active theme, or `"custom"` if the palette was set directly with `prompt.setPalette`.                                              |
| `prompt.themeList() -> array<string>`    | `array<string>` | Return an array of all registered theme names.                                                                                                                      |
| `prompt.registerStarter(name, fn)`       | `null`          | Register a named starter. `fn` is a function that, when called, configures the prompt (typically calls `prompt.set`).                                               |
| `prompt.useStarter(name)`                | `null`          | Apply a registered starter by name. Throws `ValueError` if the starter is not found.                                                                                |
| `prompt.listStarters() -> array<string>` | `array<string>` | Return an array of all registered starter names.                                                                                                                    |
| `prompt.bootstrapDir() -> string`        | `string`        | Return the absolute path to the bootstrap directory (e.g. `~/.config/stash/prompt/`).                                                                               |
| `prompt.resetBootstrap()`                | `null`          | Re-extract the bundled bootstrap scripts to the bootstrap directory, overwriting existing files. Useful after a Stash upgrade.                                      |

### Error Reference

| Situation                                    | Error thrown                                                         |
| -------------------------------------------- | -------------------------------------------------------------------- |
| `prompt.set` called with a non-function      | `TypeError: prompt.set: expected a function, got '<type>'`           |
| `prompt.themeUse` — theme not registered     | `ValueError: prompt.themeUse: unknown theme '<name>'`                |
| `prompt.useStarter` — starter not found      | `ValueError: prompt.useStarter: unknown starter '<name>'`            |
| `prompt.setPalette` — wrong argument type    | `TypeError: prompt.setPalette: expected a Palette struct`            |
| `prompt.resetBootstrap` — cannot write files | `IOError: prompt.resetBootstrap: failed to write '<path>': <reason>` |

---

## 5. Theme & Starter Globals

The bundled bootstrap (see [§6](#6-the-bundled-bootstrap)) defines two top-level global dictionaries, `theme` and `starter`, which provide a friendly API for managing themes and prompt starters.

> **Deviation from original spec:** The original design called for `prompt.theme.use(...)` and `prompt.use(...)` as nested namespace calls. Stash does not support nested namespaces as a language feature, so the bootstrap exposes these as ordinary top-level dicts instead. The functionality is identical; only the syntax differs.

### `theme` global

| Call                              | Description                                                                       |
| --------------------------------- | --------------------------------------------------------------------------------- |
| `theme.use("name")`               | Activate a registered theme. Throws `ValueError` if not found.                    |
| `theme.register("name", palette)` | Register a new theme with the given `Palette`. Overwrites if name already exists. |
| `theme.current()`                 | Return the currently active theme name (`string`).                                |
| `theme.list()`                    | Return an array of all registered theme names.                                    |

These are thin wrappers over the corresponding `prompt.theme*` primitives.

### `starter` global

| Call                           | Description                                                                     |
| ------------------------------ | ------------------------------------------------------------------------------- |
| `starter.use("name")`          | Apply a registered starter. Configures `prompt.set` and optionally the palette. |
| `starter.register("name", fn)` | Register a new starter function.                                                |
| `starter.list()`               | Return an array of all registered starter names.                                |

### `theme` and `starter` availability

`theme` and `starter` are defined by the bootstrap script. If `STASH_NO_PROMPT_BOOTSTRAP=1` is set (or the bootstrap fails to load), these globals are undefined and calling them raises `"Undefined variable: theme"`. Check with `typeof(theme) != "null"` before calling if your RC file runs with bootstrap disabled.

---

## 6. The Bundled Bootstrap

When the REPL starts (and `STASH_NO_PROMPT_BOOTSTRAP=1` is not set), Stash automatically extracts a set of prompt scripts to `~/.config/stash/prompt/` if they are missing or out of date:

```
~/.config/stash/prompt/
    bootstrap.stash       ← entry point; defines theme/starter globals
    themes/
        default.stash
        nord.stash
        catppuccin-mocha.stash
        monokai.stash
        dracula.stash
        gruvbox-dark.stash
    starters/
        minimal.stash
        bash-classic.stash
        pure.stash
        developer.stash
        pwsh-style.stash
        powerline-lite.stash
```

The bootstrap is loaded before the RC file runs. This means the `theme` and `starter` globals are always available by the time `~/.stashrc` executes.

### Banner

On first extraction, Stash prints a one-time banner to stderr:

```
[stash] Prompt bootstrap extracted to ~/.config/stash/prompt/
[stash] Run 'starter.use("developer")' in your RC to activate a starter prompt.
[stash] Disable with STASH_NO_PROMPT_BOOTSTRAP=1
```

The banner is suppressed on subsequent starts once the files exist and match the bundled version.

### Version drift

When Stash is upgraded and the bundled bootstrap version is newer than what is on disk, the new files are **not** automatically overwritten — your customizations are preserved. To re-extract the latest bundled scripts:

```bash
stash --reset-prompt            # CLI flag (re-extracts and exits)
```

or from inside the REPL:

```stash
prompt.resetBootstrap();         # re-extracts; does not exit
```

Only the files that **haven't been modified by the user** are overwritten. Files that differ from both the previous and current bundled version are left alone.

### `STASH_NO_PROMPT_BOOTSTRAP=1`

Setting this environment variable disables the entire bootstrap. The REPL falls back to the literal `stash> ` prompt and `... ` continuation prompt. The `theme` and `starter` globals are undefined. See [§18](#18-opt-out) for full details.

---

## 7. Theme Catalog

All themes are defined as `Palette` structs in `~/.config/stash/prompt/themes/`. Activate any theme with `theme.use("name")` from your RC file.

| Theme              | Description                                                      | Activation                      |
| ------------------ | ---------------------------------------------------------------- | ------------------------------- |
| `default`          | Blue/green tones; designed to work on dark and light backgrounds | `theme.use("default")`          |
| `nord`             | Arctic, north-bluish color palette (Nord color scheme)           | `theme.use("nord")`             |
| `catppuccin-mocha` | Warm, pastel tones from the Catppuccin Mocha palette             | `theme.use("catppuccin-mocha")` |
| `monokai`          | Vivid, high-contrast Monokai palette                             | `theme.use("monokai")`          |
| `dracula`          | Dark purple/pink Dracula palette                                 | `theme.use("dracula")`          |
| `gruvbox-dark`     | Retro groove, warm amber tones on dark background                | `theme.use("gruvbox-dark")`     |

Source files live at `~/.config/stash/prompt/themes/<name>.stash`.

---

## 8. Starter Gallery

Starters are complete prompt implementations. Each starter calls `prompt.set` (and optionally `prompt.setContinuation` and `theme.use`) to configure the full prompt experience. Apply one from your RC file:

| Starter          | Description                                                                             | Activation                      |
| ---------------- | --------------------------------------------------------------------------------------- | ------------------------------- |
| `minimal`        | Just the `>` sigil; no colors, no extras — maximum speed                                | `starter.use("minimal")`        |
| `bash-classic`   | `user@host:cwd$ ` — familiar Bash-style prompt                                          | `starter.use("bash-classic")`   |
| `pure`           | Two-line prompt: git info on line 1, `❯` sigil on line 2 (inspired by zsh Pure)         | `starter.use("pure")`           |
| `developer`      | Cwd + git branch + staged/unstaged counts + exit status mark                            | `starter.use("developer")`      |
| `pwsh-style`     | PowerShell-style `PS cwd>` with ANSI coloring                                           | `starter.use("pwsh-style")`     |
| `powerline-lite` | Powerline-inspired segments with separator arrows (uses only ASCII fallback by default) | `starter.use("powerline-lite")` |

Source files live at `~/.config/stash/prompt/starters/<name>.stash`.

---

## 9. Theming Guide

A `Palette` struct holds named color strings. All fields accept values from the `term.*` color constants (`term.RED`, `term.BLUE`, etc.) or `null` to use the terminal default.

### Palette struct fields

| Field       | Type      | Used for                                     |
| ----------- | --------- | -------------------------------------------- |
| `cwd`       | `string?` | Current working directory segment            |
| `user`      | `string?` | Username segment                             |
| `host`      | `string?` | Hostname segment                             |
| `time`      | `string?` | Time segment                                 |
| `info`      | `string?` | General informational text (e.g. git branch) |
| `success`   | `string?` | Exit-code success mark (exit code 0)         |
| `error`     | `string?` | Exit-code failure mark (exit code ≠ 0)       |
| `branch`    | `string?` | Git branch name                              |
| `staged`    | `string?` | Staged file count                            |
| `unstaged`  | `string?` | Unstaged file count                          |
| `untracked` | `string?` | Untracked file count                         |
| `ahead`     | `string?` | Ahead-of-upstream count                      |
| `behind`    | `string?` | Behind-upstream count                        |
| `separator` | `string?` | Segment separator (e.g. `"❯"` or `"$"`)      |

### Writing a custom palette

```stash
let myPalette = Palette {
    cwd:       term.CYAN,
    user:      term.BLUE,
    host:      term.MAGENTA,
    time:      term.GRAY,
    info:      term.YELLOW,
    success:   term.GREEN,
    error:     term.RED,
    branch:    term.YELLOW,
    staged:    term.GREEN,
    unstaged:  term.RED,
    untracked: term.GRAY,
    ahead:     term.CYAN,
    behind:    term.MAGENTA,
    separator: term.WHITE
};

// Register as a theme for reuse
theme.register("mytheme", myPalette);
theme.use("mytheme");

// Or apply directly
prompt.setPalette(myPalette);
```

---

## 10. Custom Prompt Guide

### Convention-based discovery

If your RC file (or any script loaded at startup) defines a top-level function named `prompt` that accepts one argument, Stash automatically uses it as the prompt function — no explicit call to `prompt.set` needed:

```stash
fn prompt(ctx) {
    return ctx.cwd + " $ ";
}
```

### Explicit registration

`prompt.set(fn)` is the explicit form and takes precedence over the convention-based discovery. Use it when you want to register an anonymous function or update the prompt after startup:

```stash
prompt.set((ctx) => {
    let p = prompt.palette();
    let exitMark = ctx.lastExitCode == 0
        ? term.color("✓", p.success)
        : term.color("✗", p.error);
    let cwd = term.color(ctx.cwd, p.cwd);
    return exitMark + " " + cwd + " > ";
})
```

### Using `prompt.palette()`

`prompt.palette()` returns the active `Palette`. Always read the palette at render time (inside the prompt function) rather than capturing it at registration time — this lets the palette change via `theme.use()` without requiring the prompt function to be re-registered.

```stash
prompt.set((ctx) => {
    let p = prompt.palette();          // read at render time
    let git = ctx.git != null && ctx.git.isInRepo
        ? term.color(" (" + ctx.git.branch + ")", p.branch)
        : "";
    return term.color(ctx.cwd, p.cwd) + git + " > ";
})
```

### Testing your prompt

Use `prompt.render()` to preview the prompt string in your RC without waiting for the next input:

```stash
io.println(prompt.render());
```

---

## 11. Continuation Prompt

The continuation prompt is shown when the user has opened a block that spans multiple lines (e.g. an unclosed `{`, an incomplete pipeline, or a trailing `\` continuation).

### Convention-based

Define `fn prompt_continuation(ctx)` in your RC:

```stash
fn prompt_continuation(ctx) {
    return "... ";
}
```

### Explicit registration

```stash
prompt.setContinuation((ctx) => {
    let indent = str.repeat("  ", ctx.continuationDepth);
    return indent + "... ";
})
```

### Extra context fields for continuation

The `PromptContext` passed to continuation prompts includes two additional fields:

| Field                | Type     | Description                                                                       |
| -------------------- | -------- | --------------------------------------------------------------------------------- |
| `continuationDepth`  | `int`    | Nesting depth of the open block (1 for the first unclosed `{`, etc.)              |
| `continuationReason` | `string` | Why continuation was triggered: `"block"`, `"pipeline"`, or `"line-continuation"` |

Reset to the default `... ` continuation:

```stash
prompt.resetContinuation();
```

---

## 12. OSC 133 Markers

Stash emits **OSC 133** prompt markers by default when running in an interactive TTY. These are invisible escape sequences that integrate with terminal emulators and shells supporting shell-integration features (command navigation, semantic zones, etc.).

Terminals that benefit: **VS Code integrated terminal**, **iTerm2**, **WezTerm**, **Kitty**, **Ghostty**.

### What the markers do

| Marker           | Sent                            | Purpose                                      |
| ---------------- | ------------------------------- | -------------------------------------------- |
| `OSC 133 ; A ST` | Before prompt is printed        | Marks start of prompt zone                   |
| `OSC 133 ; B ST` | After prompt, before user input | Marks start of input zone                    |
| `OSC 133 ; C ST` | When user presses Enter         | Marks end of input / start of command output |
| `OSC 133 ; D ST` | After command output completes  | Marks end of command output                  |

These markers enable features like:

- Jump between prompts with keyboard shortcuts
- Semantic selection of command output
- Automatic scrolling to last prompt on focus
- Command success/failure status reporting

### Disabling OSC 133

```bash
STASH_NO_OSC133=1 stash    # env var — disables markers for this session
```

Or set permanently in your shell profile.

### Auto-detection

OSC 133 is automatically disabled (regardless of `STASH_NO_OSC133`) when Stash detects an incompatible terminal:

| Condition                     | Why disabled                   |
| ----------------------------- | ------------------------------ |
| `TERM=dumb`                   | Dumb terminal; no ANSI support |
| `TERM=linux`                  | Linux virtual console          |
| `TERM` starts with `"screen"` | GNU screen multiplexer         |
| stdin / stdout is not a TTY   | Piped or non-interactive usage |

---

## 13. Status Indicators

When the bundled bootstrap is active (or any prompt function that uses the palette), the success/failure of the last command is communicated via:

### Colored mark

A `✓` (success, `p.success` color) or `✗` (failure, `p.error` color) is rendered before the directory segment in the `developer` and `pure` starters. The minimal and bash-classic starters omit this mark.

### Exit code segment

For non-zero exit codes, the exit code itself is shown in brackets: `[1]`, `[127]`, etc. This segment is rendered in the `p.error` color. Example (developer starter):

```text
✗ [127] ~/projects/stash (main) >
```

---

## 14. Color Helpers

The `term` namespace provides all color and styling primitives used in prompt construction.

| Function / Constant         | Description                                                                                                                                                                                                                                                                                                                 |
| --------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `term.color(text, fg, bg?)` | Wrap `text` in ANSI SGR foreground color. Optional `bg` sets background. Returns a string.                                                                                                                                                                                                                                  |
| `term.bold(text)`           | Return `text` wrapped in ANSI bold.                                                                                                                                                                                                                                                                                         |
| `term.dim(text)`            | Return `text` wrapped in ANSI dim.                                                                                                                                                                                                                                                                                          |
| `term.underline(text)`      | Return `text` wrapped in ANSI underline.                                                                                                                                                                                                                                                                                    |
| `term.style(text, opts)`    | Apply multiple styles at once via dict `{ color: term.RED, bold: true, dim: false }`.                                                                                                                                                                                                                                       |
| `term.zeroWidth(text)`      | Mark `text` as zero-width for prompt length calculation. Use this to wrap any non-SGR escape sequences (hyperlinks, OSC codes) that you embed manually in your prompt string — the line editor uses visible character width for cursor positioning, so sequences not recognized as SGR must be wrapped in `term.zeroWidth`. |
| `term.colorsEnabled()`      | Return `true` if ANSI color output is enabled. Respects `NO_COLOR` and `STASH_FORCE_COLOR`.                                                                                                                                                                                                                                 |

### Using `term.zeroWidth`

The REPL's line editor is ANSI-aware: it uses **visible character width** (not byte count) for cursor positioning. SGR sequences (`\e[...m`) are automatically excluded from width accounting. However, if you embed other escape sequences — such as OSC hyperlinks or custom terminal commands — you must wrap them:

```stash
fn mkLink(url, label) {
    // OSC 8 hyperlink — not an SGR sequence, must be zero-width wrapped
    let open  = term.zeroWidth("\e]8;;" + url + "\e\\");
    let close = term.zeroWidth("\e]8;;\e\\");
    return open + label + close;
}

prompt.set((ctx) => {
    let p = prompt.palette();
    let link = mkLink("file://" + ctx.cwdAbsolute, ctx.cwd);
    return term.color(link, p.cwd) + " > ";
});
```

### `NO_COLOR` and `STASH_FORCE_COLOR`

| Variable              | Effect                                                                           |
| --------------------- | -------------------------------------------------------------------------------- |
| `NO_COLOR=1`          | Disables all ANSI output; `term.color`, `term.bold`, etc. return plain text      |
| `STASH_FORCE_COLOR=1` | Forces ANSI output even when stdout is not a TTY (e.g. piped to a file or `tee`) |

When colors are disabled, `term.colorsEnabled()` returns `false`. Prompt functions should use this to render a simpler plain-text fallback if needed.

---

## 15. Error Handling

### What happens when a prompt function fails

| Situation                                   | Behavior                                                                                     |
| ------------------------------------------- | -------------------------------------------------------------------------------------------- |
| Prompt function returns a non-string        | One-shot warning printed to stderr; falls back to `stash> ` for this render                  |
| Prompt function throws an exception         | One-shot warning printed to stderr with the error message; falls back to `stash> `           |
| 5 consecutive failures                      | Auto-reset: `prompt.reset()` is called internally; a warning is printed; default prompt used |
| Prompt function takes longer than 5 seconds | Timed out; fallback used; one-shot warning printed                                           |

The "one-shot" warnings are suppressed after the first occurrence so they do not spam the terminal on every keystroke.

### Example warning output

```
[prompt] Error in prompt function: Cannot read field 'branch' of null
         Falling back to default prompt. Fix the error and call prompt.reset() to restore.
```

After seeing this warning, you can fix your prompt function and re-register it without restarting:

```stash
prompt.set((ctx) => { ... });   // re-register the fixed version
```

---

## 16. Performance

### Target

The total time to render a prompt (including git probe) should be **under 5 ms** for a typical repository. Starters in the gallery are all tuned to meet this target.

### Git probe timeout

The `ctx.git` probe is bounded by the `STASH_PROMPT_GIT_TIMEOUT_MS` environment variable (default: **150 ms**). If `git status --porcelain` does not complete within this timeout, `ctx.git` is set to `null` for that render. No error is shown.

```bash
# Increase for very large repositories
STASH_PROMPT_GIT_TIMEOUT_MS=500 stash

# Disable git probe entirely (ctx.git is always null)
STASH_PROMPT_GIT_TIMEOUT_MS=0 stash
```

### Tips for fast prompts

- Call `prompt.palette()` inside the function (cheap struct copy), not external globals that might involve re-computation.
- Avoid calling `$(...)` command literals inside the prompt function — they spawn subprocesses and are expensive.
- Use `ctx.git` instead of running `$(git status)` yourself.
- Keep string concatenation minimal; prefer template literals `${}` for readability.

---

## 17. Cross-Platform Notes

### Windows

- ANSI color sequences require Windows 10 (build 1607+) or newer, where the virtual terminal processor is enabled automatically by Stash at startup. On older Windows, `term.colorsEnabled()` returns `false`.
- The bootstrap directory uses `%APPDATA%\stash\prompt\` on Windows (not `~/.config/stash/prompt/`). `prompt.bootstrapDir()` returns the platform-correct path.
- `ctx.user` uses the `USERNAME` environment variable.
- `ctx.hostFull` is obtained from `System.Net.Dns.GetHostEntry` — may be slow on systems with broken DNS; it is bounded by a 200 ms timeout.

### `NO_COLOR` / `STASH_FORCE_COLOR`

These follow the [NO_COLOR standard](https://no-color.org/). See [§14](#14-color-helpers) for full details.

### Hostname source

| Platform | Source                                 |
| -------- | -------------------------------------- |
| Linux    | `/proc/sys/kernel/hostname` or `uname` |
| macOS    | `sysctl kern.hostname`                 |
| Windows  | `Environment.MachineName`              |

---

## 18. Opt-Out

### Full opt-out: `STASH_NO_PROMPT_BOOTSTRAP=1`

```bash
STASH_NO_PROMPT_BOOTSTRAP=1 stash
```

Effects:

- The bootstrap scripts are **not** extracted or loaded.
- The REPL uses the literal `stash> ` prompt and `... ` continuation prompt.
- The `theme` and `starter` globals are **not** defined.
- `prompt.themeRegister`, `prompt.themeUse`, etc. still work — but no built-in themes are pre-registered.
- OSC 133 markers are still emitted (disable separately with `STASH_NO_OSC133=1`).
- `--reset-prompt` is a no-op.

This is useful in CI environments, non-interactive scripts that source an RC file, and minimalist setups.

### Disabling OSC 133 only

```bash
STASH_NO_OSC133=1 stash
```

The prompt is still customizable; only the terminal-integration markers are suppressed.

### Disabling git probe

```bash
STASH_PROMPT_GIT_TIMEOUT_MS=0 stash
```

`ctx.git` is always `null`. Prompt functions that guard with `ctx.git != null` require no other changes.
