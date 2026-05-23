# Tab Completion — Shell and REPL Mode

> **Status:** Design (backlog) — not yet approved for implementation
> **Created:** 2026-04-30
> **Author:** Spec Architect (with user)
> **Companion:** [`Bare Command Execution — Shell Mode for the REPL.md`](Bare%20Command%20Execution%20%E2%80%94%20Shell%20Mode%20for%20the%20REPL.md)

---

## 1. Purpose

Make `Tab` a useful key at the Stash interactive prompt. Today `Tab` inserts a literal tab character, which is virtually never what the user wants. After this spec, `Tab` triggers context-aware completion of:

- **Command names** in shell mode (PATH executables, shell-sugar names like `cd`/`pwd`/`exit`, declared Stash callables).
- **File paths** in shell-mode argument positions, redirect targets, and inside quoted strings.
- **Stash identifiers** in REPL Stash mode (globals, stdlib namespaces, global functions, keywords).
- **Stash namespace members** after a dot (e.g. `fs.<Tab>` lists all `fs.*` functions).
- **User-registered custom completers** for arbitrary commands, registered via a new `complete.*` stdlib API.

Inside `${expr}` interpolations in shell mode, the Stash identifier completer applies — so `cp ${env.HO<Tab>}` completes to `${env.HOME}`.

The interaction model is **bash-classic**: first `Tab` inserts the longest common prefix; second consecutive `Tab` lists candidates below the prompt. No menu UI, no cycling, no fuzzy matching. These can come later.

## 2. Non-Goals (v1)

- **Menu / dropdown UI.** No fish-style multi-column inline picker, no zsh-style cycling. Bash-classic only.
- **Fuzzy matching.** Smart-case prefix only. `gst` does **not** match `git status`.
- **Auto-suggestions while typing.** Completion fires on `Tab` only.
- **History-based suggestions.** No fish-style ghost-text from history.
- **Hostname completion** for `ssh`/`scp` from `/etc/hosts` or `~/.ssh/known_hosts`.
- **Environment-variable name completion** (e.g. `${env.get("PA<Tab>` → `PATH`). Comes back later.
- **Pre-registered command completers.** The `complete.register` API ships, but no `git`, `docker`, `kubectl`, etc. completers are bundled. Users register their own from the RC file.
- **Bash/zsh completion-script import.** Not an attempt to parse `_completion` scripts from other shells.
- **Custom keybindings.** `Tab` is hardcoded. No `complete.bind(key, action)` API.
- **Tooling completion.** This spec does not change the LSP `CompletionHandler` for editors. Some logic from there is _reused_ by the REPL completer (see §6.4).

## 3. Activation

Tab completion is **on by default** for both shell mode and REPL Stash mode whenever the user is at an interactive prompt. There is no per-mode flag.

It is **disabled** when:

- The environment variable `STASH_NO_COMPLETION=1` is set on REPL startup. `Tab` then inserts a literal tab character, restoring pre-spec behavior.
- Stdin is not a TTY (script piped into `stash`, redirected input). The line editor is already bypassed in that case; this is mentioned only for completeness.

> **Decision (on by default):** Completion is the universal expectation for shell users. Hiding it behind a flag would make Stash feel broken out of the box.
>
> **Alternative rejected:** Opt-in via `--complete` / `STASH_COMPLETE=1`. Adds friction for the 99% case.

## 4. The `Tab` Interaction Model (Bash-Classic)

This is the central UX rule. Pressing `Tab` runs the **completion procedure** described in §5 to obtain a `CompletionResult` with a list of candidates and a longest-common-prefix.

State machine, where `lastKeyWasTab` is a `LineEditor` field reset to `false` on any non-`Tab` key:

| Trigger                              | Result count | Action                                                                                                                                                                                                 |
| ------------------------------------ | ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| First `Tab`, no candidates           | 0            | Bell (`\x07`) to `Console.Error`. Set `lastKeyWasTab = true`.                                                                                                                                          |
| First `Tab`, exactly one candidate   | 1            | Replace the token at cursor with the candidate. Set `lastKeyWasTab = false` (success — no need to remember).                                                                                           |
| First `Tab`, multiple candidates     | N>1          | Insert the longest-common-prefix (extending the token toward the candidates). If no progress was made (cursor token already equals the common prefix), do nothing visible. Set `lastKeyWasTab = true`. |
| Second consecutive `Tab`, N>1        | N>1          | Print the candidate list below the prompt (see §7), then redraw the prompt + buffer. Set `lastKeyWasTab = false`.                                                                                      |
| Second consecutive `Tab`, N=1 or N=0 | —            | Identical to first-Tab behavior; the engine just runs again.                                                                                                                                           |
| Any non-`Tab` key                    | —            | Resets `lastKeyWasTab = false` (so user must double-Tab again to relist).                                                                                                                              |

**No "unique completion gets a trailing space"** (per user decision). After completing `vim READ` to `vim README.md`, the cursor sits flush against the `d`. The user types space themselves before the next argument.

**Exception for directories**: when the unique completion is a directory, append `/` (no trailing space). This lets the user keep typing into the path: `cd /usr/<Tab>` → `cd /usr/local/`, then continues to type `bin`.

## 5. The Completion Procedure

Given the buffer string and the cursor index, the completion procedure runs these phases in order. Each phase can short-circuit by returning a `CompletionResult`.

### 5.1 Phase 1 — Mode classification

Call `ShellLineClassifier.Classify(buffer)` (existing API). The result determines which completer family applies:

- `LineMode.Shell` / `ShellForced` / `ShellStrict` → **shell-mode completion** (§6.1, §6.2).
- `LineMode.Stash` → **Stash-mode completion** (§6.3).

This is _modulo_ §5.2 — even in shell mode, the cursor may sit inside `${expr}`, in which case we fall through to Stash-mode completion for the contents of the braces.

### 5.2 Phase 2 — Cursor-context probe

A new utility, `TokenAtCursor`, walks the buffer from `0` to `cursor`, tracking minimal lexer state to answer:

- **Are we inside an unbalanced `${`?** If yes, switch to Stash-mode completion scoped to the substring inside the braces. The replacement region is bounded by the `${` and the cursor.
- **Are we inside an unterminated single- or double-quoted string?** If yes, treat the inside-string text as a path-or-content token (quotes allow spaces in paths). Quote handling stays minimal — we don't try to track nested quoting.
- **Are we inside a `//` line comment or `/* */` block comment?** If yes, return an empty `CompletionResult` (no completion in comments).
- **What is the token-at-cursor?** Walk backward from `cursor` over the current word, respecting:
  - Shell mode: word boundary is unquoted whitespace, `|`, `;`, `<`, `>`, `&`, or `(`/`)`. Inside quotes, only the matching quote ends the token. `\` escape preserves the next character. `${expr}` substrings are treated as opaque single units (the cursor being _inside_ one is handled by the `${`-case above).
  - Stash mode: word boundary is anything not `[A-Za-z_0-9.]`. A leading `.` is consumed as part of a dotted prefix.
- **What is the token's start index?** Needed for replacement (`replace_start`).

Return a `CursorContext { mode, replace_start, replace_end, token_text, in_quote, quote_char, in_substitution, prior_args }`.

`prior_args` is populated only in shell mode: it's the list of previously expanded args in the current pipeline stage, useful for custom completers (§9). In v1 these are passed through `ArgExpander.Expand` if the args are syntactically complete; if expansion fails, `prior_args` is the empty list and the completer falls back to defaults.

### 5.3 Phase 3 — Glob and brace tokens skip completion

If the token-at-cursor contains any of `*`, `?`, `[`, `{`, the completer returns an empty `CompletionResult`. Reason: the user is writing a pattern that will be expanded by `ArgExpander` at run time. Trying to "complete" mid-pattern produces surprising results.

**Exception**: a literal `~` or `~/` at the start of an otherwise-non-pattern token does NOT count as a glob.

### 5.4 Phase 4 — Dispatch to completer

Based on `CursorContext`, dispatch to one completer (§6). The completer returns a list of `Candidate { display, insert, kind }` records:

- `display`: the string shown in the candidate list (may include trailing `/`, color codes, etc.).
- `insert`: the string actually inserted in place of `token_text` (no color codes, no annotation).
- `kind`: one of `File`, `Directory`, `Executable`, `Sugar`, `StashGlobal`, `StashNamespace`, `StashFunction`, `StashKeyword`, `StashMember`, `Custom`. Used for grouping/coloring in the list display (§7.4); the completer does not have to set anything sophisticated here.

### 5.5 Phase 5 — Smart-case prefix filter

The completer may return all candidates without filtering, or may pre-filter by prefix. The engine then runs a final smart-case prefix filter against `token_text`:

- If `token_text` contains any uppercase character → case-sensitive prefix match.
- Otherwise → case-insensitive prefix match.

This keeps the completer implementations simple (they can return everything) while ensuring uniform matching.

### 5.6 Phase 6 — Compute longest common prefix

Across the surviving candidates' `insert` fields, compute the longest common prefix using the same case-sensitivity rule as §5.5. If `token_text` is already longer than or equal to the common prefix (i.e. no progress is possible), the engine still returns the candidates so a double-Tab can list them.

### 5.7 Phase 7 — Return `CompletionResult`

```
CompletionResult {
    replace_start: int           // from CursorContext
    replace_end:   int           // from CursorContext
    candidates:    Candidate[]   // post-filter, sorted alphabetically
    common_prefix: string        // for first-Tab insertion
}
```

The `LineEditor` consumes this per §4.

## 6. The Completers

Five completers live in `Stash.Cli/Completion/Completers/`. Each implements `ICompleter` with a single method `Complete(CursorContext ctx, CompletionDeps deps) -> Candidate[]`. `CompletionDeps` carries injected dependencies (the `VirtualMachine`, `PathExecutableCache`, registered custom completer table) so the completers stay testable without owning global state.

### 6.1 `CommandCompleter` — shell mode, first token of a stage

Activated when:

- `ctx.mode` is shell.
- `ctx.token_text` is the program name of the current pipeline stage (the cursor is in the first whitespace-separated token after any `\`/`!` prefix or after the most recent `|`).
- `ctx.token_text` is **not** path-like (does not start with `/`, `./`, `../`, `~/`, or `\` on Windows). Path-like tokens fall through to `PathCompleter` per §6.2.

Candidates are unioned from three sources (deduped by `insert`):

1. **Shell-sugar names**: the literals `cd`, `pwd`, `exit`, `quit`. `kind = Sugar`.
2. **PATH executables**: enumerated via a new `PathExecutableCache.GetAllExecutables() -> IEnumerable<string>` method (§10). On Windows, executable names are presented _with_ their extension stripped when `PATHEXT` would have matched (matching how shell mode resolves them today). `kind = Executable`.
3. **REPL globals that are callable** (functions, lambdas, struct constructors): enumerated by iterating `vm.Globals` and checking the value kind. `kind = StashGlobal`. We include callables here because shell mode treats `let foo = fn() { ... }; foo` as a valid command-position invocation. Non-callable globals are NOT offered at command position because `let x = 5; x` runs Stash mode anyway, not shell mode — but they are offered in the next case below if mode-classification is ambiguous and the line could route either way.

Note that **stdlib namespace names** (`fs`, `path`, etc.) are _not_ offered at command position. They aren't callables and they're already shadowed by the classifier rule "namespace name first token → Stash mode."

### 6.2 `PathCompleter` — shell mode, argument positions, redirects, quotes

Activated when:

- `ctx.mode` is shell, AND any of:
  - `ctx.token_text` is past the first token of the stage, OR
  - `ctx.token_text` is path-like (starts with `/`, `./`, `../`, `~/`), OR
  - `ctx.in_quote` is true (we're inside `"…"` or `'…'`), OR
  - `ctx` is on a redirect target (the parser sees the previous token is `>`, `>>`, `2>`, `2>>`, `&>`, `&>>`, `<`).

**Behavior:**

1. **Tilde handling**: if `token_text` starts with `~/`, expand to `<home>/<rest>` for filesystem matching, but preserve `~/` in `display` and `insert` (the user typed it; we don't surprise them by replacing it). Bare `~` (no slash) completes to `~/` with `kind = Directory`.
2. **Split** `token_text` into `dir_part` and `name_part` at the last `/`. Default `dir_part = "."` if no slash is present. Resolve `dir_part` to a real directory.
3. **Enumerate** `Directory.EnumerateFileSystemEntries(dir_part)`. For each entry:
   - Skip if `entry_name` doesn't smart-case-prefix `name_part`.
   - **Dotfile rule**: if `name_part` does not start with `.` and `entry_name` does, skip.
   - Build `display = name + ('/' if directory else '')`; `insert = original_dir_part + name + ('/' if directory else '')`.
   - `kind = Directory` if directory, else `File`.
4. **Empty prefix in a quoted string**: do enumerate (let the user see what's in the directory).

**Cross-platform notes:**

- Windows: case-insensitive filesystem, but smart-case match still applies to the typed prefix. Paths use `/` or `\` interchangeably for matching; output uses whatever separator the user already typed (default `/`).
- `Directory.EnumerateFileSystemEntries` may throw `UnauthorizedAccessException` mid-iteration. Wrap in a try/catch that swallows and returns the partial list; surface a single bell, no error message (we don't want completion to spam the prompt with permission errors).

**Tilde-user form** (`~user/`): not supported in v1. The token containing `~user/` is treated as starting with a literal `~` and produces no useful completion. Documented as a known limitation.

### 6.3 `StashIdentifierCompleter` — Stash mode and `${…}` substitutions

Activated when:

- `ctx.mode` is Stash, OR `ctx.in_substitution` is true.
- `ctx.token_text` does not contain a `.` (otherwise dispatch to `DottedMemberCompleter`).

Candidates are unioned from:

1. **Stash keywords** from `StdlibRegistry.Keywords`. `kind = StashKeyword`.
2. **Stash global functions** from `StdlibRegistry.Functions` (e.g. `println`, `print`, `readLine`). `kind = StashFunction`.
3. **Stdlib namespace names** from `StdlibRegistry.NamespaceNames`. `kind = StashNamespace`.
4. **REPL globals** from `vm.Globals.Keys`. `kind = StashGlobal`. (Constants too; we don't distinguish at completion time.)

We **do not** complete user-defined struct field names or local variables in v1. Struct-field completion needs type inference at the cursor, which is the LSP's job; the REPL doesn't run the analysis engine on every keystroke. If users want this they should use the editor.

### 6.4 `DottedMemberCompleter` — after a `.` in Stash or `${…}`

Activated when `ctx.token_text` contains a `.` (and dispatched after Phase 4 selects `StashIdentifierCompleter` would otherwise apply).

Split `token_text` at the last `.` into `prefix` and `suffix`. Then:

1. If `prefix` matches a `StdlibRegistry.NamespaceNames` entry, return `StdlibRegistry.GetNamespaceMembers(prefix)` and `GetNamespaceConstants(prefix)`. Kind `= StashMember`.
2. Otherwise return no candidates. (No type inference for arbitrary expressions in v1; that's an LSP concern.)

The `replace_start` is positioned **after** the last `.` so that only the `suffix` portion is replaced.

### 6.5 `CustomCompleterDispatch` — user-registered completers

Activated when:

- `ctx.mode` is shell.
- `ctx` is in argument position (not the first token of a stage).
- The first-token program name (the _resolved_ command after stripping prefixes) matches a key registered via `complete.register(name, fn)`.

The dispatcher invokes the registered Stash callable with a single argument: a `CompletionContext` struct (Stash-side, see §9.2). The callable returns either:

- An array of strings → each becomes a `Candidate { display = s, insert = s, kind = Custom }`.
- An array of dicts/structs with `display` and `insert` fields → used as-is.
- `null` or an empty array → fall back to `PathCompleter`.

If the callable throws, the engine catches the error, logs it via `Console.Error.WriteLine($"completer for '{name}' failed: {message}")` _once per session per command name_ (idempotency follows the SA0821 pattern from shell-mode), and falls back to `PathCompleter`. We do not crash the prompt on a buggy user completer.

> **Decision (replace-not-augment):** A registered completer fully replaces the default `PathCompleter` for that command. If the user wants to also offer files, they call `complete.paths(ctx)` (a helper exposed in the `complete` namespace) inside their function and merge results.
>
> **Alternative rejected:** Default-augmented (file completions always added). Surprising — users who write `complete.register("git", ...)` typically want exhaustive control over what shows up.

## 7. List Display (Second-Tab Behavior)

When the second consecutive `Tab` triggers the listing of N>1 candidates:

### 7.1 Layout

- **Multi-column layout** sized to terminal width via `Console.WindowWidth` (with a 1-line cushion to avoid wrapping). Falls back to single-column if `WindowWidth` is unavailable or the longest candidate exceeds the width.
- Column width = longest `display` + 2 spaces of padding.
- Number of columns = `floor(window_width / column_width)`, clamped to `≥1`.
- Candidates are sorted alphabetically (case-insensitive).
- Each column is filled top-to-bottom, then left-to-right (the standard `ls` layout).

### 7.2 Pager prompt for >100 candidates

If `candidates.Count > 100`, before printing, the engine writes:

```
Display all 247 possibilities? (y or n)
```

then reads a single key:

- `y`, `Y`, `Space`, `Enter` → print the list.
- Anything else → cancel; redraw the prompt + buffer; do not print.

The threshold `100` is **hardcoded in v1**. No env var to tune it. (We can add `STASH_COMPLETION_PAGER_THRESHOLD` later if anyone asks.)

### 7.3 Redraw

After printing (or canceling), the engine:

1. Writes a blank line.
2. Calls `LineEditor.Render()` to redraw the prompt and current buffer with the cursor in its original position.

### 7.4 Coloring (optional, deferred)

Coloring candidates by `kind` (executable green, directory blue, etc.) is **not** implemented in v1. The candidate list prints in the terminal's default color. A follow-up can add coloring by inspecting `kind` and emitting ANSI SGR codes; the data is already there.

## 8. Multi-line Input Interactions

`Tab` works on the **current physical line only**. The completion engine receives the buffer string from `LineEditor`, which is the contents of the current line. `MultiLineReader` does not concatenate prior continuation lines into the buffer presented to the editor.

**Implications:**

- After typing `let x = (` and pressing Enter (Stash mode continuation), then on the second line typing `pri<Tab>`, the engine sees only `pri` as the buffer. Mode classification will treat `pri` as a bare identifier — this is fine; it'll be classified as Stash mode (lookahead sees end-of-input → Stash) and the Stash identifier completer will offer `print`, `println`, etc. ✅
- After typing `cat foo.txt |` and Enter (shell continuation), then `gr<Tab>`: the buffer is `gr`. Classification calls `ShellLineClassifier.Classify("gr")` which sees `gr` is on PATH → shell mode → command position → completes to `grep`. ✅
- Edge case: `cat foo |` continuation, then `gre<Tab>` where the user wanted Stash-side completion. Not supported — no way to disambiguate without seeing the prior line. Acceptable v1 limitation; shell-after-pipe is the common case.

## 9. The `complete.*` Stdlib API

A new namespace, `complete`, lives in `Stash.Stdlib/BuiltIns/CompleteBuiltIns.cs`.

### 9.1 Functions

| Function                                           | Returns                   | Behavior                                                                                                                                                          |
| -------------------------------------------------- | ------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `complete.register(name: string, fn: any)`         | `null`                    | Register `fn` as the custom completer for command `name`. Re-registering replaces. `fn` must be callable (function or lambda).                                    |
| `complete.unregister(name: string)`                | `bool`                    | Returns `true` if a completer was registered and removed; `false` otherwise.                                                                                      |
| `complete.registered() -> array<string>`           | `array<string>`           | Lexicographically sorted list of currently registered command names.                                                                                              |
| `complete.suggest(line: string, cursor: int = -1)` | `CompletionResult` struct | Programmatically run the completion engine. `cursor = -1` means end-of-line. Used for testing and for Stash scripts that build their own UI.                      |
| `complete.paths(ctx: CompletionContext)`           | `array<string>`           | Helper for custom completers — runs the default `PathCompleter` on `ctx` and returns the candidate strings. Used to augment file completion in custom completers. |

### 9.2 Struct types

Two new built-in structs (registered in `StdlibRegistry.Types.cs`, mirroring how `StashError` is registered):

```
struct CompletionContext {
    command: string         // resolved program name of the current stage
    args:    array<string>  // already-typed args before the cursor token (best-effort, may be empty if not parseable)
    current: string         // partial text of the token at the cursor
    position: int           // 1-based index of the current token within args (1 = first arg after the command)
    mode:    string         // "shell" | "stash" | "substitution"
}

struct CompletionResult {
    replace_start: int            // byte offset in line where replacement begins
    replace_end:   int            // byte offset where replacement ends
    candidates:    array<string>  // proposed completion strings (insert form, sorted)
    common_prefix: string         // longest common prefix among candidates
}
```

### 9.3 Cross-layer wiring

`Stash.Stdlib` does not (and must not) depend on `Stash.Cli`. The pattern from `PromptBuiltIns` (per the repo memory) applies here: `CompleteBuiltIns` exposes static delegate slots that `Stash.Cli` populates at startup.

```csharp
// Stash.Stdlib/BuiltIns/CompleteBuiltIns.cs
public static Func<string, int, CompletionResult>? SuggestHandler;
public static Func<CompletionContext, string[]>? PathHelperHandler;
public static Action<string, IStashCallable>? RegisterHandler;
public static Func<string, bool>? UnregisterHandler;
public static Func<string[]>? RegisteredHandler;
```

`Stash.Cli/Program.cs` (or a new `Stash.Cli/Completion/CompletionWiring.cs`) sets these on REPL startup, before any user code runs, by pointing them at methods on the `CompletionEngine` and a `CustomCompleterRegistry` table.

When `complete.register` is called from a **non-REPL context** (e.g. a script run via `stash myfile.stash`), the handlers are null and the call is a no-op (returns `null` for register, `false` for unregister, empty array for `registered()`). This is documented behavior — completion is a REPL-only feature.

### 9.4 Storage of registered completers

The registry is a simple `Dictionary<string, IStashCallable>` owned by `Stash.Cli`. **Scoped to the REPL session** — not persisted across restarts. The user re-registers from their RC file on every start. This matches how all other REPL state works.

## 10. New Public Surface in Existing Components

### 10.1 `PathExecutableCache.GetAllExecutables() -> IReadOnlyList<string>`

Add a public method that returns all executable names found across all PATH directories. Reuses the existing 60-second TTL cache; the first call after invalidation does a full enumeration. Sorted alphabetically and deduplicated (PATH precedence preserved — first occurrence wins for `display`).

### 10.2 `VirtualMachine.EnumerateGlobals() -> IEnumerable<(string name, StashValue value, bool isConst)>`

The VM today exposes `Globals` as a public dictionary (per the explore report) and `HasReplGlobal(name)`. We add an enumeration helper that also reports the const-ness, so `CommandCompleter` can filter by callable-ness without exposing the raw dictionary internals to consumers.

### 10.3 `LineEditor` changes

- New private field `bool _lastKeyWasTab`.
- New `case ConsoleKey.Tab` in the key-dispatch switch:
  - Skips completion if `STASH_NO_COMPLETION=1` or if the engine reference is `null`. Falls through to the literal-tab insertion.
  - Otherwise calls `_completionEngine.Complete(_buffer.ToString(), _cursor)`.
  - Applies the result per §4 (insert common prefix, list, or bell).
- New constructor parameter `CompletionEngine? completionEngine = null` so the editor can be constructed without completion (preserves existing tests).
- New helper `WriteCandidateList(Candidate[] candidates)` that prints the multi-column list (§7), invoking `_completionEngine.PromptYesNo` for the >100 case (the engine owns the prompt to keep editor logic minimal).

### 10.4 `MultiLineReader` changes

- Pass through the `CompletionEngine` to its inner `LineEditor`.

## 11. Files Changed / Added

### New files

- `Stash.Cli/Completion/CompletionEngine.cs` — orchestrates phases §5.1–§5.7, owns the registered-completer table, exposes `Complete(buffer, cursor)` and `PromptYesNo(message)`.
- `Stash.Cli/Completion/CompletionContext.cs` — internal record (also bridged to a Stash-side struct via a converter in `CompleteBuiltIns`).
- `Stash.Cli/Completion/CompletionResult.cs` — internal record.
- `Stash.Cli/Completion/Candidate.cs` — record + `CandidateKind` enum.
- `Stash.Cli/Completion/TokenAtCursor.cs` — Phase 2 logic (§5.2).
- `Stash.Cli/Completion/SmartCaseMatcher.cs` — `Matches(prefix, candidate)` and `LongestCommonPrefix(strings)`.
- `Stash.Cli/Completion/CompletionMenu.cs` — multi-column rendering (§7) and pager prompt.
- `Stash.Cli/Completion/Completers/ICompleter.cs`
- `Stash.Cli/Completion/Completers/CommandCompleter.cs`
- `Stash.Cli/Completion/Completers/PathCompleter.cs`
- `Stash.Cli/Completion/Completers/StashIdentifierCompleter.cs`
- `Stash.Cli/Completion/Completers/DottedMemberCompleter.cs`
- `Stash.Cli/Completion/Completers/CustomCompleterDispatch.cs`
- `Stash.Cli/Completion/CustomCompleterRegistry.cs` — `Dictionary<string, IStashCallable>`.
- `Stash.Cli/Completion/CompletionWiring.cs` — populates static delegate slots on `CompleteBuiltIns`.
- `Stash.Stdlib/BuiltIns/CompleteBuiltIns.cs` — the `complete.*` namespace and static delegate slots.
- `Stash.Tests/Cli/Completion/SmartCaseMatcherTests.cs`
- `Stash.Tests/Cli/Completion/TokenAtCursorTests.cs`
- `Stash.Tests/Cli/Completion/CommandCompleterTests.cs`
- `Stash.Tests/Cli/Completion/PathCompleterTests.cs`
- `Stash.Tests/Cli/Completion/StashIdentifierCompleterTests.cs`
- `Stash.Tests/Cli/Completion/DottedMemberCompleterTests.cs`
- `Stash.Tests/Cli/Completion/CompletionEngineTests.cs` — phase routing, common-prefix, smart-case integration.
- `Stash.Tests/Cli/Completion/CompletionMenuTests.cs` — multi-column layout, pager threshold.
- `Stash.Tests/Stdlib/CompleteBuiltInsTests.cs` — `complete.register`, `complete.suggest`, struct round-trip.
- `Stash.Tests/Cli/Completion/CompletionIntegrationTests.cs` — end-to-end via `complete.suggest` against a real VM.

### Modified files

- `Stash.Cli/LineEditor.cs` — add `Tab` handling and `_lastKeyWasTab` state (§10.3).
- `Stash.Cli/MultiLineReader.cs` — pass `CompletionEngine` to inner editor (§10.4).
- `Stash.Cli/Program.cs` — construct `CompletionEngine`, `CustomCompleterRegistry`, `PathExecutableCache`; honor `STASH_NO_COMPLETION`; call `CompletionWiring.Wire(...)`.
- `Stash.Cli/Shell/PathExecutableCache.cs` — add `GetAllExecutables()` (§10.1).
- `Stash.Bytecode/VM/VirtualMachine.cs` — add `EnumerateGlobals()` (§10.2).
- `Stash.Stdlib/Registry/StdlibRegistry.Types.cs` — register `CompletionContext` and `CompletionResult` as built-in structs.
- `Stash.Stdlib/BuiltIns/GlobalBuiltIns.cs` — instantiate the new built-in structs at runtime.
- `docs/Stash — Standard Library Reference.md` — document the `complete.*` namespace.
- `docs/Shell — Interactive Shell Mode.md` — add a "Tab Completion" section linking to the spec.
- `CHANGELOG.md` — entry for tab completion + the new `complete.*` namespace.

## 12. Cross-Platform Considerations

| Concern                           | v1 behavior                                                                                                         |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| PATH executable enumeration       | Reuses existing `PathExecutableCache` (already cross-platform — POSIX `+x`, Windows `PATHEXT`).                     |
| Filesystem entry enumeration      | `Directory.EnumerateFileSystemEntries` works everywhere. Permission errors swallowed (§6.2).                        |
| Path separator                    | Match accepts both `/` and `\`; output uses whichever the user already typed; default `/`.                          |
| Smart-case on case-insensitive FS | Smart-case still applied — even on Windows, an uppercase letter in the prefix forces case-sensitive match.          |
| Tilde expansion                   | `~/` expands via `env.get("HOME")` on POSIX, `env.get("USERPROFILE")` on Windows. Same as shell-mode arg expansion. |
| Console width detection           | `Console.WindowWidth`. May throw `IOException` if no console attached → fall back to single-column layout.          |
| Bell character                    | `Console.Error.Write('\x07')`. Modern terminals handle this; some may flash instead. Acceptable.                    |

Windows runtime support for shell mode itself is gated off in v1 (per the shell-mode spec). Tab completion in REPL Stash mode **is** available on Windows from day one (since Stash mode has no Windows gate).

## 13. Performance

Completion fires synchronously on `Tab` and must feel instantaneous. Soft budget: under 50ms for 95% of cases.

| Operation                                                  | Expected cost                                                         |
| ---------------------------------------------------------- | --------------------------------------------------------------------- |
| `ShellLineClassifier.Classify`                             | O(line length), fast (existing measurement: μs).                      |
| `TokenAtCursor` walk                                       | O(line length).                                                       |
| `PathExecutableCache.GetAllExecutables` (cold)             | One PATH directory walk per dir; ~10–50ms typical. Cached for 60s.    |
| `Directory.EnumerateFileSystemEntries` for path completion | O(entries in dir); usually <5ms.                                      |
| `vm.EnumerateGlobals`                                      | O(globals); typically <100 entries → microseconds.                    |
| `StdlibRegistry.NamespaceNames` / `GetNamespaceMembers`    | Frozen dictionaries → microseconds.                                   |
| Smart-case prefix filter + sort                            | O(N log N) where N = total candidates; fine even for 5000-entry PATH. |
| Multi-column layout + render                               | O(N) for printing; bounded by pager threshold.                        |

The **pathological case** is `Tab` on an empty buffer in shell mode → returns thousands of PATH executables + sugar names. The pager prompt (§7.2) guards the user from a screen-full; the engine still computes and sorts, which is bounded by the PATH cache (~10k items max in practice). Acceptable.

If profiling later shows the empty-buffer case is too slow, a follow-up can short-circuit "empty token + first position" to skip enumeration and just bell. Out of scope for v1.

## 14. Static Analysis

No new diagnostic descriptors. Tab completion is a runtime/REPL feature; static analysis of script files is unaffected.

## 15. Test Scenarios

### 15.1 Unit — `SmartCaseMatcher`

- `"foo"` matches `"foobar"` (lower → case-insensitive prefix).
- `"FOO"` does NOT match `"foobar"` (upper → case-sensitive).
- `"Foo"` does NOT match `"FOOBAR"`.
- `"Foo"` matches `"FooBar"`.
- Longest-common-prefix of `["foo", "foobar", "foobaz"]` = `"foo"`.
- Longest-common-prefix of `["Foo", "foo"]` with smart-case lower input = `"foo"`.

### 15.2 Unit — `TokenAtCursor`

- Buffer `"git ch"`, cursor=6, mode=shell → token `"ch"`, replace_start=4, replace_end=6.
- Buffer `"echo ${env.HO}"`, cursor=13 → in_substitution=true, token `"env.HO"`, replace_start=7.
- Buffer `'ls "/usr/lo'`, cursor=11 → in_quote=true, quote_char='"', token `"/usr/lo"`, replace_start=4.
- Buffer `"// comment xy"`, cursor=13 → empty result (no completion in comment).
- Buffer `"cp file.{txt"`, cursor=12 → token contains `{` → Phase 3 short-circuits to empty.
- Buffer `"cd ~/"`, cursor=5 → token=`"~/"`, classified as path-like.

### 15.3 Unit — `PathCompleter`

- Setup tmp dir with `Alpha.txt`, `beta.md`, `.hidden`, `subdir/`.
- Token `""` → `Alpha.txt`, `beta.md`, `subdir/` (no `.hidden`).
- Token `"."` → `.hidden` is now in.
- Token `"al"` → `Alpha.txt` (smart-case lower).
- Token `"AL"` → no match (smart-case upper).
- Token `"sub"` → `subdir/`.
- Token `"~/"` → user's home dir contents, displayed with `~/` prefix preserved.
- Permission-denied dir → empty result, no exception.

### 15.4 Unit — `CommandCompleter`

- VM with global `let foo = fn() {}`. Token `"fo"` → includes `foo`.
- VM with global `let bar = 5`. Token `"ba"` → does NOT include `bar` (not callable).
- Token `"c"` → includes `cd` (sugar) plus PATH execs starting with `c`.
- Token `"git"` (assuming git is installed) → includes `git`.
- Token `"\\g"` → backslash prefix stripped for matching, preserved in insert; matches PATH execs starting with `g`.

### 15.5 Unit — `StashIdentifierCompleter`

- Token `"pri"` → `print`, `println`.
- Token `"f"` → includes `fs`, `for`, `false`, `fn`, plus any user globals starting with `f`.
- Empty token → returns _everything_ (the engine then leaves it to the caller; double-Tab will list with pager).

### 15.6 Unit — `DottedMemberCompleter`

- Token `"fs."` → all `fs.*` functions.
- Token `"fs.exi"` → `fs.existsFile`, `fs.existsDir`, etc. (smart-case).
- Token `"foo.bar"` where `foo` is a user variable → empty (no type inference in v1).
- Token `"math."` → includes `math.PI`, `math.E` (constants too).

### 15.7 Integration — `complete.suggest`

- `complete.suggest("git che", -1)` → result includes empty candidates (no per-command completer registered) and falls back to PathCompleter, which has no matching files unless a file `che*` exists.
- After `complete.register("git", fn(ctx) => ["status", "checkout", "commit"])`, `complete.suggest("git che", -1)` → candidates `["checkout"]`, common_prefix `"checkout"`.
- `complete.suggest("fs.", -1)` → returns all `fs.*` members.
- `complete.suggest("", 0)` → in Stash mode, returns large set.

### 15.8 Integration — `LineEditor` Tab

These tests spawn the actual `stash --shell` binary and inject a key sequence:

- Type `g`, `i`, `t`, `Space`, `Tab` → cursor moves but no candidates inserted unless test fs has matching files.
- Type `pri`, `Tab` → buffer becomes `print` (common prefix of `print`, `println`).
- Type `pri`, `Tab`, `Tab` → list `print  println` printed below; prompt redraws.
- Type `xyz_no_match`, `Tab` → bell, no buffer change.
- Type letter, `Tab`, `Tab`, then any non-Tab key → `_lastKeyWasTab` resets.
- With `STASH_NO_COMPLETION=1`, `Tab` inserts literal `\t`.

### 15.9 Integration — Pager prompt

- Mock VM with 200 globals, single-column simulation. `Tab` on empty buffer in Stash mode → "Display all 200+ possibilities? (y or n)" prompt fires. Press `n` → no list, prompt restored. Press `y` → list printed.

### 15.10 Integration — Custom completer error handling

- Register `complete.register("foo", fn(ctx) => { throw "kaboom" })`. Type `foo bar`, Tab → bell, error logged once to stderr; second Tab does not re-log.

### 15.11 Cross-platform

- All Linux/macOS tests in CI on both platforms.
- Windows: Stash-mode completion tests run; shell-mode completion tests skipped (shell mode itself is gated off).
- Path completion handles forward and backward slashes interchangeably on Windows.

## 16. Migration & Breaking Changes

### 16.1 `Tab` is no longer a literal tab

Previously, `Tab` at the prompt inserted `\t` into the buffer. After this spec, `Tab` triggers completion. Users who actually want a literal tab can:

- Set `STASH_NO_COMPLETION=1` to disable completion for the whole session.
- Use `Ctrl+V` followed by `Tab` to insert a verbatim tab — _but_ `Ctrl+V` quoted-insert is **not implemented** in `LineEditor` today and is not added by this spec. Users who need literal tabs interactively are expected to set the env var.

This is a **minor breaking change**. Documented in the changelog. Negligible real-world impact (literal tabs at a shell prompt are virtually never wanted).

### 16.2 No other breakages

The `complete.*` namespace is new; no existing API changes.

## 17. Future Work

Tracked here so v1 doesn't foreclose:

- **Menu UI** (fish-style) layered over the bash-classic engine. The completion engine already returns structured `Candidate[]` records with `kind`; rendering can switch from list-printing to inline-menu drawing without changing the engine.
- **Fuzzy matching** as an opt-in via `STASH_COMPLETION_FUZZY=1` or `complete.set_matcher("fuzzy")`.
- **Async / streaming completion** for slow custom completers (e.g. one that calls out to `git`). Today the dispatch is synchronous; a future version could spawn the completer with a 200ms budget and bell+abort if it doesn't return.
- **History-based suggestions** (ghost text while typing).
- **Shipped command completers** for `git`, `docker`, `kubectl`, `ssh`, etc., bundled in the bootstrap.
- **Hostname completion** for `ssh`/`scp` from `/etc/hosts` and `~/.ssh/known_hosts`.
- **Environment-variable name completion** inside `${env.get("…` and `env.X`.
- **Struct field / method completion** in REPL Stash mode (requires running the analysis engine on the in-progress buffer).
- **Quoted-insert key** (`Ctrl+V Tab` to insert a literal tab without disabling completion).

## 18. Decision Log Summary

| #   | Decision                                                                                     | Alternatives rejected                                                                    |
| --- | -------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| 1   | Bash-classic UX (Tab = common-prefix; Tab-Tab = list)                                        | Zsh menu-complete (cycling); fish inline menu                                            |
| 2   | Both shell mode and REPL Stash mode + inside `${…}`                                          | Shell-only; both-but-not-`${…}`                                                          |
| 3   | Completer set: command names, file paths, Stash identifiers, namespace members               | + env vars; + history-based; + hostnames                                                 |
| 4   | `complete.register` API exists in v1; **no** pre-registered command completers shipped       | No API at all; API + bundled `git`/`docker`/etc.                                         |
| 5   | Smart-case prefix matching                                                                   | Strict prefix; case-insensitive; fuzzy subsequence                                       |
| 6   | Append `/` for unique directory completion; **no** trailing space for unique file completion | Bash-style trailing space for files                                                      |
| 7   | Hide dotfiles unless prefix starts with `.`                                                  | Always show; per-flag                                                                    |
| 8   | Tilde-expand for matching, preserve `~/` in output                                           | Replace `~/` with the expanded home path                                                 |
| 9   | Multi-column layout + pager prompt for >100 candidates                                       | Single-column; multi-column with no pager                                                |
| 10  | Custom completers fully **replace** default path completion                                  | Default-augmented (always merge file completion)                                         |
| 11  | Tab on empty buffer enumerates everything (subject to pager)                                 | Short-circuit and bell                                                                   |
| 12  | Glob/brace tokens skip completion (no mid-pattern completion)                                | Try to complete the prefix portion only                                                  |
| 13  | `STASH_NO_COMPLETION=1` disables completion (Tab → literal tab)                              | No off-switch; `--no-completion` CLI flag (we add the env var only; flag can come later) |
| 14  | Cross-layer wiring via static delegate slots on `CompleteBuiltIns`                           | `Stash.Stdlib` referencing `Stash.Cli` (forbidden); event/reflection-based plumbing      |
| 15  | Custom completers: throw → log once + fall back to PathCompleter                             | Crash the prompt; silently swallow                                                       |
| 16  | Pager threshold hardcoded to 100 in v1                                                       | Env var; configurable via `complete.*` API                                               |
| 17  | Multi-line: completion sees current physical line only                                       | Concatenate continuation lines for classification (more complex; small benefit)          |
| 18  | No struct field / method / type-driven completion in REPL                                    | Run analysis engine on-the-fly; partial type inference                                   |
| 19  | Coloring of candidate list deferred to follow-up                                             | Color from day one (small but adds platform polish work)                                 |
| 20  | Tab key not configurable in v1                                                               | `complete.bind(key, action)` API                                                         |

---

## Open Items for Spec Sign-Off

None remaining; all branching decisions resolved with the user. Once approved, this spec moves to `1-todo/` for the Orchestrator to pick up.
