# Prompt - Customizing the REPL Prompt

> **Status:** Stable user and maintainer reference
> **Audience:** Stash REPL users, shell-mode users, and contributors changing prompt behavior.
> **Purpose:** Defines how prompt rendering, prompt context, bootstrap themes, starters, terminal markers, and failure fallback work.
>
> **Companion documents:**
>
> - [Shell - Interactive Shell Mode](Shell%20%E2%80%94%20Interactive%20Shell%20Mode.md) - REPL behavior, RC loading, shell mode, aliases, and completion.
> - [Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md) - authoritative `prompt` and `term` API signatures.
> - [Language Specification](Stash%20%E2%80%94%20Language%20Specification.md) - functions, closures, structs, strings, and error handling.

The Stash REPL prompt is rendered by a Stash callback. A prompt callback receives a `PromptContext` value and returns the string that should be shown before the next input line.

This document describes prompt behavior as a user-facing contract. The generated standard-library reference remains authoritative for exact namespace API signatures.

## 1. Quick Start

Put this in `~/.stashrc` or another script loaded by the REPL:

```stash
prompt.set((ctx) => {
    let p = prompt.palette();
    let git = ctx.git != null && ctx.git.isInRepo
        ? " (" + ctx.git.branch + ")"
        : "";
    return term.color(ctx.cwd, p.cwd) + term.color(git, p.info) + " > ";
});
```

The callback must accept one argument and return a string. If it throws or returns a non-string value, the REPL prints a one-shot warning and falls back to `stash> ` for that render.

## 2. Rendering Contract

The primary prompt is resolved in this order:

1. A callback registered with `prompt.set(fn)`.
2. A top-level callable named `prompt`, if present in the REPL globals.
3. The built-in fallback string `stash> `.

The continuation prompt is resolved in this order:

1. A callback registered with `prompt.setContinuation(fn)`.
2. A top-level callable named `prompt_continuation`, if present in the REPL globals.
3. The built-in fallback string `... `.

`prompt.set` and `prompt.setContinuation` accept callables that can receive one argument. Variadic callables are accepted. A callback with incompatible arity produces a `TypeError`.

## 3. PromptContext

Every prompt callback receives a `PromptContext` struct snapshot.

| Field          | Type         | Meaning                                                                             |
| -------------- | ------------ | ----------------------------------------------------------------------------------- |
| `cwd`          | `string`     | Current working directory with the home directory collapsed to `~` when applicable. |
| `cwdAbsolute`  | `string`     | Absolute current working directory.                                                 |
| `user`         | `string`     | `USER`, `USERNAME`, or `unknown`.                                                   |
| `host`         | `string`     | Short hostname.                                                                     |
| `hostFull`     | `string`     | Full hostname returned by the platform.                                             |
| `time`         | `float`      | Unix timestamp in seconds at context creation time.                                 |
| `lastExitCode` | `int`        | Exit code from the most recent REPL command.                                        |
| `lineNumber`   | `int`        | Monotonically increasing prompt render count for the process.                       |
| `mode`         | `string`     | `shell` when shell mode is active; otherwise `stash`.                               |
| `hostColor`    | `string`     | Stable 256-color SGR fragment derived from the hostname.                            |
| `git`          | `PromptGit?` | Git status, `null` when no git data is available.                                   |

Continuation callbacks receive the same context plus:

| Field                | Type     | Meaning                                 |
| -------------------- | -------- | --------------------------------------- |
| `continuationDepth`  | `int`    | Continuation nesting depth.             |
| `continuationReason` | `string` | Current implementation value is `open`. |

## 4. PromptGit

When git probing succeeds, `ctx.git` is a `PromptGit` struct.

| Field            | Type     | Meaning                                                   |
| ---------------- | -------- | --------------------------------------------------------- |
| `isInRepo`       | `bool`   | Whether the current directory is inside a git repository. |
| `branch`         | `string` | Branch name, or a short detached-HEAD object id.          |
| `isDirty`        | `bool`   | True when staged, unstaged, or untracked files exist.     |
| `stagedCount`    | `int`    | Number of staged entries.                                 |
| `unstagedCount`  | `int`    | Number of unstaged entries.                               |
| `untrackedCount` | `int`    | Number of untracked files.                                |
| `ahead`          | `int`    | Commits ahead of upstream.                                |
| `behind`         | `int`    | Commits behind upstream.                                  |

The git probe runs `git status --porcelain=v2 --branch --untracked-files=normal`. It returns `null` when `git` is unavailable, the probe times out, or an unexpected error occurs. Outside a repository, the probe returns a `PromptGit` value with `isInRepo: false`.

Guard prompt code accordingly:

```stash
let git = ctx.git != null && ctx.git.isInRepo
    ? " (" + ctx.git.branch + ")"
    : "";
```

## 5. `prompt` Namespace

The `prompt` namespace provides the primitive operations.

| Function                              | Behavior                                                                         |
| ------------------------------------- | -------------------------------------------------------------------------------- |
| `prompt.set(fn)`                      | Registers the primary prompt callback.                                           |
| `prompt.setContinuation(fn)`          | Registers the continuation prompt callback.                                      |
| `prompt.reset()`                      | Clears the primary callback.                                                     |
| `prompt.resetContinuation()`          | Clears the continuation callback.                                                |
| `prompt.context()`                    | Returns a fresh `PromptContext`.                                                 |
| `prompt.render()`                     | Renders the current prompt without displaying it.                                |
| `prompt.palette()`                    | Returns the current palette value, or `null`.                                    |
| `prompt.setPalette(palette)`          | Stores any value as the active palette. No palette type validation is performed. |
| `prompt.bootstrapDir()`               | Returns the prompt bootstrap directory path.                                     |
| `prompt.resetBootstrap()`             | Invokes the CLI bootstrap reset handler when available.                          |
| `prompt.themeRegister(name, palette)` | Registers a named palette value.                                                 |
| `prompt.themeUse(name)`               | Activates a registered palette by name.                                          |
| `prompt.themeCurrent()`               | Returns the active theme name, or an empty string when none is active.           |
| `prompt.themeList()`                  | Returns sorted registered theme names.                                           |
| `prompt.registerStarter(name, fn)`    | Registers a named starter prompt callback.                                       |
| `prompt.useStarter(name)`             | Activates a registered starter callback.                                         |
| `prompt.listStarters()`               | Returns sorted registered starter names.                                         |

Common error cases:

| Case                                    | Result       |
| --------------------------------------- | ------------ |
| Missing or non-callable prompt callback | `TypeError`  |
| Callback cannot accept one argument     | `TypeError`  |
| Unknown theme name                      | `ValueError` |
| Unknown starter name                    | `ValueError` |

## 6. Bootstrap

The CLI bundles prompt scripts as embedded resources. On REPL startup, unless `STASH_NO_PROMPT_BOOTSTRAP=1` is set, Stash extracts them to:

```text
~/.config/stash/prompt/
```

The path is based on the current user's home directory. `prompt.bootstrapDir()` returns the path used by the runtime.

The bootstrap contains:

| Kind       | Count | Directory                                                             |
| ---------- | ----: | --------------------------------------------------------------------- |
| Core files |     4 | `VERSION`, `palette.stash`, `bootstrap.stash`, `default-prompt.stash` |
| Themes     |    17 | `themes/*.stash`                                                      |
| Starters   |    14 | `starters/*.stash`                                                    |

Extraction happens when the directory is missing, `VERSION` is missing, or the on-disk version differs from the embedded version. Extraction recreates the bootstrap directory from embedded resources. Keep personal prompt customizations in `~/.stashrc` or another user-owned file rather than editing extracted bootstrap files.

`prompt.resetBootstrap()` asks the CLI to re-extract the embedded bootstrap scripts. In hosts that do not install a reset handler, it has no effect.

## 7. Theme and Starter Globals

`bootstrap.stash` defines two convenience globals:

| Global    | Calls                                                                                 |
| --------- | ------------------------------------------------------------------------------------- |
| `theme`   | `theme.use(name)`, `theme.register(name, palette)`, `theme.current()`, `theme.list()` |
| `starter` | `starter.use(name)`, `starter.register(name, fn)`, `starter.list()`                   |

These are Stash-level dictionaries wrapping the flat `prompt.theme*` and `prompt.*Starter` primitives.

When the bootstrap is disabled or fails to load, `theme` and `starter` are not defined. The low-level `prompt.themeRegister`, `prompt.themeUse`, `prompt.registerStarter`, and related functions still exist.

Bundled themes:

```text
ayu-dark, catppuccin-latte, catppuccin-mocha, default, dracula,
github-light, gruvbox-dark, gruvbox-light, monochrome, monokai,
nord, one-dark, rose-pine, solarized-dark, solarized-light,
synthwave, tokyo-night
```

Bundled starters:

```text
arrow, bash-classic, bracket, compact, developer, emoji, fish-style,
minimal, powerline-lite, pure, pwsh-style, robbyrussell, status, two-line
```

Activate a starter in your RC file:

```stash
starter.use("developer");
```

## 8. Palette Shape

The bundled `palette.stash` defines:

```stash
struct Palette {
    fg, bg, accent, muted,
    success, error, warning, info,
    git_clean, git_dirty, git_conflict,
    user, host, cwd
}
```

Theme files construct `Palette` values and register them with `prompt.themeRegister`.

Color values may be ANSI color names, 256-color SGR fragments such as `"38;5;81"`, hex colors such as `"#89b4fa"`, or `""` for the terminal default.

## 9. Custom Prompt Examples

Convention-based prompt:

```stash
fn prompt(ctx) {
    return ctx.cwd + " $ ";
}
```

Explicit registration:

```stash
prompt.set((ctx) => {
    let p = prompt.palette();
    let mark = ctx.lastExitCode == 0
        ? term.color(">", p.success)
        : term.color(">", p.error);
    return term.color(ctx.cwd, p.cwd) + " " + mark + " ";
});
```

Custom continuation prompt:

```stash
prompt.setContinuation((ctx) => {
    return str.repeat("  ", ctx.continuationDepth) + "... ";
});
```

Preview the active prompt:

```stash
io.println(prompt.render());
```

## 10. Terminal Markers

The REPL emits OSC 133 shell-integration markers when eligible:

| Marker                      | Emitted                                |
| --------------------------- | -------------------------------------- |
| `OSC 133 ; A ST`            | Before prompt text.                    |
| `OSC 133 ; B ST`            | After prompt text, before input.       |
| `OSC 133 ; C ST`            | Immediately before command evaluation. |
| `OSC 133 ; D ; exitCode ST` | After command evaluation.              |

OSC markers are disabled when stdout is redirected, `TERM` is `dumb` or `linux`, `TERM` starts with `screen`, or `STASH_NO_OSC133=1` is set.

Prompt A/B markers are wrapped in `\x01...\x02` zero-width regions so the line editor can measure visible prompt width correctly.

## 11. Color and Width

Prompt functions normally use the `term` namespace:

| API                         | Use                                                |
| --------------------------- | -------------------------------------------------- |
| `term.color(text, fg, bg?)` | ANSI foreground/background color.                  |
| `term.bold(text)`           | Bold text.                                         |
| `term.dim(text)`            | Dim text.                                          |
| `term.underline(text)`      | Underlined text.                                   |
| `term.style(text, opts)`    | Multiple style options.                            |
| `term.zeroWidth(text)`      | Marks non-printing escape sequences as width zero. |
| `term.colorsEnabled()`      | Returns whether color output is enabled.           |

SGR color sequences are handled by the prompt width calculator. Wrap non-SGR escapes, such as OSC hyperlinks, with `term.zeroWidth`.

## 12. Failure Behavior

The REPL catches prompt callback failures so a broken prompt cannot break the REPL.

| Failure                                             | Behavior                                           |
| --------------------------------------------------- | -------------------------------------------------- |
| Callback returns non-string                         | One-shot warning, fallback prompt for that render. |
| Callback throws `RuntimeError`                      | One-shot warning, fallback prompt for that render. |
| Callback throws another exception                   | One-shot warning, fallback prompt for that render. |
| Five consecutive primary failures                   | Registered primary callback is reset.              |
| Five consecutive continuation failures              | Registered continuation callback is reset.         |
| Prompt callback calls `prompt.render()` recursively | Re-entry guard returns `stash> `.                  |

After fixing a prompt, call `prompt.set(...)` again or reload your RC file.

## 13. Performance

Prompt callbacks run before every input line. Keep them cheap:

- Use `ctx.git` instead of spawning `git`.
- Avoid shell commands inside prompt functions.
- Read `prompt.palette()` inside the callback so theme changes apply immediately.
- Keep I/O out of prompt callbacks.

Git probing is bounded by `STASH_PROMPT_GIT_TIMEOUT_MS`, defaulting to 150 ms. Set it to `0` to disable git probing.

```bash
STASH_PROMPT_GIT_TIMEOUT_MS=0 stash
STASH_PROMPT_GIT_TIMEOUT_MS=500 stash
```

## 14. Opt-Out

| Variable                        | Effect                                                     |
| ------------------------------- | ---------------------------------------------------------- |
| `STASH_NO_PROMPT_BOOTSTRAP=1`   | Disables extraction and loading of bundled prompt scripts. |
| `STASH_NO_OSC133=1`             | Disables terminal shell-integration markers.               |
| `STASH_PROMPT_GIT_TIMEOUT_MS=0` | Disables git probing.                                      |

When the bootstrap is disabled, the literal prompt fallback is `stash> ` and the continuation fallback is `... `.

## 15. Change Rules

When changing prompt behavior:

- Update the standard-library metadata for `prompt` and `term` APIs.
- Update this document when rendering order, context fields, bootstrap files, environment variables, or fallback behavior changes.
- Add or update tests in `Stash.Tests/Cli` or `Stash.Tests/Stdlib`.
- Keep extracted bootstrap files treated as generated resources; user customizations should live in RC files.
