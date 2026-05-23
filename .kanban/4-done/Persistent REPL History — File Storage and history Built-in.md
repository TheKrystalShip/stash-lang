# Persistent REPL History — File Storage and `history` Built-in

> **Status:** Design (backlog) — not yet approved for implementation
> **Created:** 2026-04-30
> **Author:** Spec Architect (with user)
> **Parent:** [`Bare Command Execution — Shell Mode for the REPL.md`](Bare%20Command%20Execution%20%E2%80%94%20Shell%20Mode%20for%20the%20REPL.md) (this spec removes one item from its non-goals list)

---

## 1. Purpose

Make the REPL's command history survive across sessions. Today, `LineEditor._history` is an in-memory `List<string>` that is discarded when the REPL exits. After this spec, every interactive REPL session reads its history from a file on startup and appends to that file as the user types. A small `history` shell built-in plus three new `process.*` functions expose the same data to scripts, following the established sugar-over-stdlib pattern (§11 of the parent spec).

The feature is intentionally minimal: no fuzzy search, no incremental cross-session sync, no secret-pattern redaction, no time-based filters. Those are explicit non-goals (§2) and candidate follow-ups.

## 2. Non-Goals (v1)

The following are out of scope. Each could become a follow-up spec.

- **Live cross-session sync.** Two concurrent REPLs do not see each other's commands until the next startup. (Bash default behavior.)
- **History expansion.** No `!!`, `!42`, `!str`, `!?str?` bash-style recall. Up-arrow is the recall mechanism.
- **Reverse incremental search.** No `Ctrl+R` / fuzzy fzf-style picker. Pure linear up-arrow walk only.
- **Pattern-based secret redaction.** No `HISTORY_IGNORE`-equivalent env var or RC config. The leading-space rule (§5.4) is the manual escape hatch.
- **Per-directory history.** A single global file. No per-cwd partitioning.
- **Timestamps in v1.** Each entry is a command line, nothing more. Bash's extended format with `: <ts>:0;<cmd>` may land in v1.1.
- **Secure-erase / shred.** `history -c` truncates the file but does not overwrite blocks. Users who care should use disk encryption.
- **Editing the history file at runtime.** Stash reads on startup and appends thereafter; manual edits to the file made while a REPL is running will be silently overwritten on trim.
- **Non-interactive script mode.** `stash file.stash` does not read or write history. The feature only activates when the REPL prompt is shown.

## 3. Activation

History persistence is **always on** for any interactive REPL session — including plain Stash REPL (no shell mode). It is opt-out, not opt-in.

| Setting                  | Effect                                                                                                                               |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| `STASH_HISTORY_FILE=…`   | Override the default file path. Empty string (`STASH_HISTORY_FILE=`) **disables** persistence entirely for this session.             |
| `STASH_HISTORY_SIZE=…`   | Override the entry cap. `0` disables persistence; negative values mean unlimited (with a documented warning that the file may grow). |
| `--no-history` CLI flag  | Disable persistence for this session. Equivalent to `STASH_HISTORY_FILE=`.                                                           |
| Embedded mode            | Always disabled (Stash hosted by another process has no concept of "the user's REPL").                                               |
| Non-interactive (script) | Always disabled.                                                                                                                     |

> **Decision:** Always-on for any interactive REPL.
>
> **Alternative rejected:** _Shell-mode-only._ Tying history persistence to the line classifier feels arbitrary — a Stash user who declares `let x = computeBigThing(...)` wants up-arrow recall after restart just as much as a shell user who ran `git status`. Bash, zsh, Python, Node, and ghci all persist regardless of "mode."

## 4. File Location

Resolved on REPL startup, in this order. The first existing-or-creatable path wins.

### POSIX (Linux, macOS)

1. `$STASH_HISTORY_FILE` if set and non-empty.
2. `$XDG_STATE_HOME/stash/history` if `XDG_STATE_HOME` is set and non-empty.
3. `~/.local/state/stash/history`.
4. `~/.stash_history` (final fallback for minimal systems where `~/.local/state` cannot be created).

### Windows

1. `%STASH_HISTORY_FILE%` if set and non-empty.
2. `%LOCALAPPDATA%\stash\history` if `LOCALAPPDATA` is set and non-empty.
3. `%USERPROFILE%\.stash_history` (final fallback).

### Resolution rules

- The directory is created on first append (`Directory.CreateDirectory` is idempotent — no error if it already exists).
- If the chosen path cannot be opened for append (permission denied, read-only filesystem, etc.), persistence is **silently disabled** for the session and a one-line warning is written to `stderr`: `stash: history disabled — cannot write <path>: <reason>`. The in-memory history continues to work.
- Symbolic links are followed. The user is trusted to point `STASH_HISTORY_FILE` wherever they like.

> **Decision:** XDG-aware path with `~/.stash_history` final fallback.
>
> **Alternatives rejected:**
>
> - _`~/.stash_history` only (bash-style)._ Clutters `~`, ignores XDG, frustrates users who keep clean home directories.
> - _`~/.config/stash/history` (matches RC file location)._ XDG specifies `state` for runtime data and `config` for configuration; history is state, not config. The RC file (init.stash) is configuration and correctly lives under `XDG_CONFIG_HOME`. This separation is what XDG was designed for.
>
> **Risk:** Two paths to maintain (config vs state). Mitigated by isolating each in its own loader (`RcFileLoader` for config, new `HistoryFileLoader` for state).

## 5. File Format

### 5.1 On-disk layout

Plain UTF-8 text, LF line endings, no BOM. Optional first-line header (created on first write):

```
# stash history v1
```

Each entry is one logical command line. Multiple entries are separated by **a blank line**. Inside an entry, embedded newlines are preserved literally (multi-line commands from §9 of the parent spec are stored as-is across multiple physical lines).

#### Example file

```
# stash history v1
ls -la

let x = 42

let result = fn(a, b) {
    return a + b
}

git status

cd ~/projects
```

### 5.2 Why this format

> **Decision:** Plain text, blank-line-separated, multi-line entries kept whole.
>
> **Alternatives rejected:**
>
> - _One-physical-line-per-entry (bash-style)._ Forces multi-line commands to either be flattened (losing readability if recalled then re-edited) or split into fragments that each up-arrow press only reveals one piece of. Both bad for a language whose REPL frequently spans many lines.
> - _JSON Lines._ Structured, but unfriendly for `grep`, `vim`, and `cat`. Corrupts on partial-write more visibly than text. Adds a serialization cost per line.
> - _Bash extended format with `: <ts>:0;<cmd>`._ Useful but adds parsing complexity and a partial-state problem (some lines have timestamps, some don't, when format changes). Defer to v1.1.
>
> **Rationale:** Users edit history files. Greppable text wins.

### 5.3 Reader behavior

On REPL startup, the file is read into memory in one pass:

1. If the file does not exist or is empty, in-memory history starts empty. No error.
2. The header line (if present and matches `# stash history v<n>`) is consumed. Unknown header versions are tolerated — the reader still tries to parse entries below it but logs a one-line warning to `stderr` so future format additions don't silently drop data on older binaries.
3. The remaining bytes are split on `\n\n` (blank line). Each non-empty chunk is one entry. Trailing whitespace on the chunk is trimmed; internal whitespace is preserved.
4. Entries are loaded into `LineEditor._history` in file order (oldest first). Up-arrow walks them newest-first as today.
5. If the file is larger than the cap, the oldest entries are dropped during this load and the file is rewritten atomically (§5.6).

### 5.4 Writer behavior

A line is appended to the file when:

- The user pressed `Enter` on a non-empty input.
- The line does **not** begin with whitespace (leading-space-skips-history rule, §6.2).
- Persistence is enabled for this session.
- The line is not identical to the immediately previous in-memory entry (consecutive-dup collapse, §6.1).

Append semantics (POSIX):

- Open with `O_WRONLY | O_APPEND | O_CREAT`, mode `0600`.
- Write the entry bytes followed by `\n\n` in **a single `write(2)` call** (POSIX guarantees atomicity for writes ≤ `PIPE_BUF` and best-effort for larger; entries longer than `PIPE_BUF` may interleave under heavy concurrent write from multiple stash processes — acceptable for an interactive-shell history).
- Close immediately. No long-lived file handle.

Append semantics (Windows):

- `FileMode.Append`, `FileShare.Read | FileShare.Write`. .NET serializes its own writes; cross-process atomicity is not guaranteed but interleaving is rare in practice. Documented limitation.

> **Decision:** Append-on-each-line, no in-memory buffering between commands.
>
> **Alternatives rejected:**
>
> - _Buffer in memory, rewrite on exit._ Simple, but loses history if the REPL crashes or is killed (`kill -9`), and loses commands when two shells run in parallel (last-writer-wins).
> - _File lock around every write._ Overkill, hurts performance, adds a stale-lock failure mode (and we have `FileLockHandle` already, but using it here is using a sledgehammer for a tack).
>
> **Risk:** Two concurrent shells writing the same long entry could interleave bytes. Mitigated by the fact that interactive-shell entries are almost always far below `PIPE_BUF` (4096 on Linux). A multi-line `let x = fn(...)` with 4 KB of body is the rare case; if it ever causes corruption in practice, we add a fcntl advisory lock around the write.

### 5.5 Cap and trimming

Default cap: **10,000 entries**. Override via `STASH_HISTORY_SIZE`. `0` disables persistence; negative means unlimited.

- The cap is checked **on startup only**, never on append. This avoids reading-then-rewriting the file on every keystroke.
- When startup detects the file exceeds the cap, the oldest `(count - cap)` entries are dropped and the file is rewritten atomically (§5.6).
- The in-memory `LineEditor._history` is also bounded by the cap during the session — once the in-memory buffer reaches the cap, oldest is evicted on each new append.

> **Decision:** Default 10,000.
>
> **Alternatives rejected:**
>
> - _500 (bash) / 1000 (zsh)._ Both date from the era when 1 MB of disk was meaningful. Modern users hit them in a week of normal use. Stash defaults to a generous cap that effectively disappears as a constraint for most users.
> - _Unbounded by default._ Risk of users losing track of file size, and of a rogue script (e.g., `for i in 0..1000000 { ... }` typed at the REPL with each iteration becoming a recorded line) blowing it up. The cap is a safety net.

### 5.6 Atomic rewrite

When trimming on startup, the rewrite uses temp-file-and-rename:

1. Write the trimmed contents (header + kept entries + blank-line separators) to `<file>.tmp.<random>` in the same directory.
2. `fflush` and `fsync` (POSIX) the temp file's fd.
3. `rename(2)` over the original (POSIX) / `File.Move(..., overwrite: true)` (.NET).

The rename is atomic on POSIX. On Windows, the API is best-effort but has been atomic in practice since NTFS. If rename fails (e.g., target on a different filesystem), the rewrite is abandoned, the temp file is unlinked, and a one-line `stderr` warning is printed; the over-cap file persists but in-memory truncation still takes effect for the session.

## 6. Behavioral Rules

### 6.1 Consecutive-duplicate collapse

If the just-pressed line is byte-identical to the most recent in-memory entry, **it is not appended** (neither to the in-memory list nor to the file). Non-adjacent duplicates are kept.

This matches `LineEditor`'s current in-memory behavior (`_history[^1] != result`) — we extend it to apply to the file write as well.

### 6.2 Leading-space-skips-history

If the input line's first character is a space (`U+0020`), the line is **not added to history at all** (in-memory or file). Tab is not treated as whitespace for this rule — only ASCII space.

This is the user-controlled secret-redaction escape hatch:

```
$ <space>export DATABASE_PASSWORD="hunter2"   # not stored
$ ls                                          # stored
$ <space>aws-vault exec prod -- terraform plan # not stored
```

The leading space is **not stripped from the line that runs** — it's only the trigger for the skip. So `<space>echo hello` runs `<space>echo hello` (which `echo` will silently absorb the leading space from). This matches bash with `HISTCONTROL=ignorespace`.

### 6.3 Empty input

A line consisting only of whitespace, or just `Enter`, is never recorded. The REPL re-prompts.

### 6.4 Errored lines are recorded

Lines that fail to parse, throw a runtime error, hit a `command not found`, or exit non-zero are still recorded. Recall-and-fix is the whole point of history.

### 6.5 Multi-line input

A multi-line block produced by §9 of the parent spec (continuation triggers) is recorded as **one entry** containing embedded `\n` characters. Up-arrow recalls the entire block; the `LineEditor` renders it across multiple visual lines and the cursor can be moved within it.

The in-memory representation is unchanged — `List<string>` where the string may contain `\n`. The on-disk representation uses physical newlines inside the entry (as in the §5.1 example).

### 6.6 `history -c` semantics

Truncates the file to just the header line and clears the in-memory list. Other concurrently-running REPLs are unaffected for the rest of their session but will see the truncated file on their next startup.

## 7. Stdlib API: `process.history*`

Three new functions are added to the `process` namespace (gated on `StashCapabilities.Process`, like the rest):

| Function                                   | Behavior                                                                                                                                                                                                             |
| ------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `process.historyList() -> array<string>`   | Returns a shallow copy of the current in-memory history, oldest-first. Each element is one entry (may contain `\n`). Empty array if persistence is disabled or history is empty.                                     |
| `process.historyClear() -> null`           | Clears the in-memory list **and** truncates the on-disk file (preserving the header line). Returns `null`. No-op (returns `null`) if persistence is disabled.                                                        |
| `process.historyAdd(line: string) -> null` | Adds `line` to history programmatically: appends to the in-memory list and to the file (subject to the same dedup and leading-space rules as user input). Throws `ValueError` if `line` is empty or whitespace-only. |

### 7.1 Availability

These functions are registered unconditionally in the `process` namespace. In non-interactive script mode (where persistence is disabled), `historyList` returns `[]`, `historyClear` is a no-op, and `historyAdd` accepts the call but stores nothing (so scripts can probe-and-feed without branching on whether they are running in an interactive REPL).

### 7.2 Why expose to scripts

The whole sugar-over-stdlib philosophy of the parent spec demands it. A script that wants to:

- Print the user's last 10 commands as a startup motd.
- Pre-seed the new REPL session with a project-specific history (e.g., RC file calls `process.historyAdd("just build")` so up-arrow on a fresh shell already shows the project's common command).
- Audit / analyze command usage patterns.

…can do so without a separate API surface.

## 8. Shell Sugar: `history`

The bare command `history` in shell mode desugars to a Stash call following the §11.2 pattern of the parent spec. It is **only** desugared when the stage is not piped or redirected — `history | grep git` desugars `history` first, then pipes the result.

| Shell input           | Desugared Stash                                                                                              |
| --------------------- | ------------------------------------------------------------------------------------------------------------ | ------- |
| `history` (no args)   | `for entry in process.historyList() { io.println(entry) }` — prints all entries, one per line, oldest-first. |
| `history <N>` (1 arg) | Same loop but slices the last `N` entries. `N` must parse as a positive integer; otherwise `CommandError`.   |
| `history -c`          | `process.historyClear()`.                                                                                    |
| Anything else         | `CommandError("history: usage: history [N                                                                    | -c]")`. |

> **Decision:** Numbered listing (`  1  ls`, `  2  cd ~`, …) is **not** part of v1. Plain entries only. Reasoning: numbers are only useful for `!N` recall, and we explicitly punt on history expansion. If `!N` lands later, numbered output ships with it.
>
> **Alternative rejected:** _Match bash's `history` output exactly._ More work for a feature (history expansion) we deliberately don't have.

> **Decision:** No `-w` (write), `-r` (read), `-a` (append), `-n` (read-new). The append-on-each-line model means the user-visible behavior of these flags is already automatic. Document this.

## 9. Architecture

### 9.1 New components in `Stash.Cli/`

```
Stash.Cli/
└── History/
    ├── HistoryFileLoader.cs   ← path resolution + read + atomic rewrite
    ├── HistoryFileWriter.cs   ← append-on-each-line, dedup + leading-space rules
    └── LineEditor.cs          ← unchanged interface; gains an injected IHistorySink
```

`IHistorySink` is a tiny internal interface that decouples `LineEditor` from file I/O:

```csharp
internal interface IHistorySink
{
    void Append(string entry);
    IReadOnlyList<string> Initial { get; }
}
```

Two implementations:

- `FileHistorySink` — wraps `HistoryFileWriter`; constructed by `Program.cs` when persistence is enabled.
- `NullHistorySink` — no-op; used when persistence is disabled (embedded mode, `--no-history`, `STASH_HISTORY_FILE=`).

`LineEditor` keeps its existing `_history` list (now seeded from `Initial`), and on every accepted line calls `_sink.Append(line)` after the existing dedup check.

### 9.2 New stdlib in `Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs`

Three new functions registered alongside the existing `process.*` set. They wire to **delegate slots** on the `ProcessBuiltIns` class (same cross-layer pattern used by `PromptBuiltIns` per the existing memory note):

```csharp
public static class ProcessBuiltIns
{
    // Set by Stash.Cli on REPL startup. Null means persistence disabled.
    public static Func<IReadOnlyList<string>>? HistoryListProvider;
    public static Action? HistoryClearHandler;
    public static Action<string>? HistoryAddHandler;
}
```

When `HistoryListProvider` is null, `process.historyList()` returns an empty array. When the handlers are null, `historyClear` is a no-op and `historyAdd` accepts but discards. This matches the §7.1 availability rule and keeps `Stash.Stdlib` from referencing `Stash.Cli` (the stdlib has zero CLI dependencies).

### 9.3 Wiring

In `Stash.Cli/Program.cs`, between RC loading and entering the REPL loop:

1. Resolve the history file path (§4).
2. Construct `HistoryFileLoader` and read entries.
3. Construct `HistoryFileWriter`.
4. Set the three `ProcessBuiltIns` delegate slots to point at the writer's `Snapshot`/`Clear`/`Append` methods.
5. Pass a `FileHistorySink` to `LineEditor`.

For non-interactive script invocations (`stash file.stash`), step 1–5 are skipped; the delegates remain null; `process.history*` returns/no-ops as documented.

### 9.4 Shell desugaring

Add `history` to the set of recognized desugar names in `ShellSugarDesugarer.cs` alongside `cd`, `pwd`, `exit`, `quit`. The desugarer constructs the appropriate `CallExpr` AST per the §8 table.

## 10. Cross-Platform Notes

| Concern               | POSIX (Linux/macOS)                                                                   | Windows                                                                                 |
| --------------------- | ------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| Default file location | `$XDG_STATE_HOME/stash/history` → `~/.local/state/stash/history` → `~/.stash_history` | `%LOCALAPPDATA%\stash\history` → `%USERPROFILE%\.stash_history`                         |
| File mode             | `0600` (user read/write only)                                                         | Default ACLs (inherited from parent dir)                                                |
| Append atomicity      | Single `write(2)` ≤ `PIPE_BUF` is atomic                                              | .NET serializes own writes; cross-process not guaranteed                                |
| Rename atomicity      | `rename(2)` is atomic if same filesystem                                              | `File.Move(overwrite: true)` is best-effort; atomic in practice on NTFS                 |
| Line endings          | LF                                                                                    | LF (we deliberately do not use CRLF — matches the rest of Stash's text I/O conventions) |
| Path separator        | `/`                                                                                   | `\` — `Path.Combine` handles this                                                       |

Windows shell mode is still gated off per the parent spec, but plain Stash REPL on Windows benefits from history persistence as soon as this ships.

## 11. Static Analysis

No new diagnostics. The feature is runtime-only and adds no new syntax.

## 12. Test Scenarios

Tests live in `Stash.Tests/Cli/HistoryFileLoaderTests.cs` and `Stash.Tests/Cli/HistoryFileWriterTests.cs`, plus integration tests in `Stash.Tests/Stdlib/ProcessHistoryTests.cs` for the `process.history*` functions.

### 12.1 File format

- Empty file → empty in-memory list.
- File with only header → empty in-memory list.
- File with N entries separated by blank lines → N in-memory entries in oldest-first order.
- File with multi-line entry (embedded `\n`) → single entry with `\n` preserved.
- File with unrecognized header → entries still parsed; warning written to captured stderr.
- Trailing blank lines → not treated as empty entry.
- Missing trailing blank line on last entry → still parsed.

### 12.2 Path resolution

- `STASH_HISTORY_FILE=/custom/path` honored.
- `STASH_HISTORY_FILE=` (empty) disables persistence.
- `XDG_STATE_HOME` honored when set.
- Falls through to `~/.local/state/stash/history` when XDG unset.
- Falls through to `~/.stash_history` when `~/.local/state` cannot be created (simulated by directory permission).
- Permission-denied on chosen path → persistence disabled, warning printed, in-memory still works.

### 12.3 Append behavior

- Single line appended → file ends with `<line>\n\n`.
- Two consecutive identical lines → only one stored.
- Two non-consecutive identical lines → both stored.
- Leading-space line → not appended to file or in-memory.
- Empty / whitespace-only line → not appended.
- Multi-line entry → physical newlines preserved in file, single entry in-memory.

### 12.4 Cap and trim

- File above cap on startup → trimmed to cap, oldest dropped, file rewritten atomically.
- `STASH_HISTORY_SIZE=0` → persistence disabled.
- `STASH_HISTORY_SIZE=-1` → unlimited (no trim ever).
- Trim rewrite failure (e.g., disk full simulation) → over-cap file remains, warning printed, in-memory truncated for session.

### 12.5 Concurrency

- Two writer instances appending alternating short lines to same file → all lines appear, no corruption.
- Long entry (>4 KB) appended from two writers concurrently → may interleave (documented limitation; test asserts no crash, not no-interleave).

### 12.6 `process.history*`

- `process.historyList()` returns shallow copy in oldest-first order.
- Mutating the returned array does not affect history (proves it is a copy).
- `process.historyClear()` empties in-memory and truncates file to header.
- `process.historyAdd("foo")` adds entry; subsequent `process.historyList()` includes it.
- `process.historyAdd("")` and `process.historyAdd("   ")` throw `ValueError`.
- `process.historyAdd(" leading space")` is accepted but stored nowhere (leading-space rule).
- All three are no-ops / return empty when persistence is disabled.

### 12.7 `history` shell built-in

- `history` prints all entries.
- `history 5` prints the last 5.
- `history 0` prints nothing.
- `history -1` → `CommandError`.
- `history -c` clears.
- `history foo` → `CommandError`.
- `history | grep git` → desugars `history`, pipes its stdout to `grep`.

## 13. Migration & Compatibility

This is a new feature with no existing behavior to preserve. Once shipped:

- First REPL run after upgrade creates the history file. No data migration needed.
- Users who relied on history-being-volatile (e.g., shared machines) can opt out via `--no-history`, `STASH_HISTORY_FILE=`, or `STASH_HISTORY_SIZE=0`.
- The parent spec's non-goals list should be updated to **remove** "Persistent history file" and add a forward-link to this spec.

## 14. Future Work

- **History expansion** (`!!`, `!N`, `!str`) — deserves its own spec; would also bring numbered output to `history`.
- **Reverse incremental search** (`Ctrl+R`) — interactive UX change in `LineEditor`; significant scope.
- **Optional timestamps** — bash extended format. Opt-in via env var to keep the default file greppable.
- **`STASH_HISTORY_IGNORE_PATTERNS`** — pattern-based redaction for users who want stronger guarantees than the leading-space rule.
- **Per-project history** — RC file could call `STASH_HISTORY_FILE=$(pwd)/.stash_history` before launching, scoping history to the project. Already possible with the env var; document as a recipe.

## 15. Decision Log

| #   | Decision                                                                | Date       |
| --- | ----------------------------------------------------------------------- | ---------- |
| 1   | Always-on for any interactive REPL (not shell-mode-only).               | 2026-04-30 |
| 2   | XDG-aware path with `~/.stash_history` final fallback.                  | 2026-04-30 |
| 3   | Plain text format, blank-line-separated entries, multi-line kept whole. | 2026-04-30 |
| 4   | Default cap 10,000 entries, env-overridable.                            | 2026-04-30 |
| 5   | Consecutive-dup collapse + leading-space-skips-history.                 | 2026-04-30 |
| 6   | Append-on-each-line; trim only on startup.                              | 2026-04-30 |
| 7   | Errored lines are recorded (recall-and-fix is the point).               | 2026-04-30 |
| 8   | Ship `history` built-in + `process.history*` stdlib in this same spec.  | 2026-04-30 |
| 9   | No pattern-based secret redaction in v1.                                | 2026-04-30 |
