# Process Namespace Decomposition — Architectural Options

> **Status:** Backlog → ready for promotion to `1-todo/` once a final review pass is done.
> **Created:** 2026-04-30
> **Last revised:** 2026-04-30 (open questions in §6 resolved)
> **Purpose:** Decide how to relieve the `process.*` namespace of accumulated
> shell- and current-process responsibilities, without painting the rest of the
> standard library into a corner. The decision sets a precedent for how Stash
> partitions a built-in namespace whenever the same thing happens again
> (`fs`, `env`, `term`…).

---

## 1. The Problem

`process.*` (see [Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs](Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs))
currently mixes three distinct responsibilities:

| Responsibility               | Functions / Constants                                                                                                                                                                                                          | Notes                                                                                                       |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------- |
| **A. Child processes**       | `spawn`, `exec`, `wait`, `waitTimeout`, `waitAll`, `waitAny`, `kill`, `signal`, `isAlive`, `pid`, `read`, `write`, `onExit`, `daemonize`, `detach`, `list`, `find`, `exists`, `SIG*` consts, structs `Process`/`CommandResult` | The original purpose of the namespace. ~20 functions.                                                       |
| **B. Current process state** | `exit`, `chdir`, `popDir`, `dirStack`, `dirStackDepth`, `withDir`                                                                                                                                                              | Operates on **this** process. `chdir` etc. were added with shell mode but are also useful in plain scripts. |
| **C. Shell mode state**      | `lastExitCode`                                                                                                                                                                                                                 | Only meaningful when a bare command pipeline has run. Exists to back `$?`.                                  |

Three independent observations sharpen the smell:

1. **`process.exit` is already duplicated.** A global `exit()` exists in
   [GlobalBuiltIns.cs](Stash.Stdlib/BuiltIns/GlobalBuiltIns.cs) (line 183).
   Keeping both is just historical clutter.
2. **Current-process state is already split.** `env.cwd()` reads the working
   directory, but `process.chdir()` writes it. This split is incoherent —
   reading and writing the same OS state should live in the same namespace.
3. **Shell-only state pollutes scripting docs.** A user writing a backup
   script who scans `process.*` in the LSP completion list sees `lastExitCode`,
   which is meaningless outside the REPL shell.

The shape of the smell: **`process.*` answers three different questions
("how do I run a child?", "how do I manipulate myself?", "what did the shell
just do?") with one bucket.** Each new shell feature widens the bucket.

---

## 2. Constraints That Shape the Solution

These constraints rule out some of the obvious answers; they need to be
explicit because future maintainers will be tempted to revisit them.

- **C1. Stash has no nested namespaces.** `prompt.theme.use(...)` was originally
  spec'd and had to be flattened to top-level `theme.use(...)` because the
  language has no syntactic or semantic support for `ns.subns.fn`. Adding
  nested namespaces is a _language change_ that ripples through the parser,
  resolver, all six visitors, the bytecode compiler's namespace-member
  dispatch, the LSP completion/hover providers, the formatter, and the doc
  generator. (See repo memory: _REPL Prompt Customization_.)
- **C2. Capability-gating is per-namespace.** `process.*` is gated on
  `StashCapabilities.Process`. Any split must preserve the ability to gate
  the relevant pieces — embedded hosts that disable Process should not get
  child-process spawning, but may still want `chdir` (via `env`) and `exit`.
- **C3. Stash already has self-state precedents in `env.*`** — `env.cwd`,
  `env.home`, `env.hostname`, `env.loadFile`, `env.saveFile`. The `env`
  namespace is _already_ the home of "things about the running process'
  environment".
- **C4. Anything we do here sets the precedent** for `fs.*` (which is also
  starting to balloon — 27 functions), `term.*`, `arr.*` (37 functions),
  `str.*` (38 functions). The chosen pattern must scale.
- **C5. Renames are breaking changes for users and packages.** `@stash/*`
  packages and example scripts already call `process.spawn`. Mass renames
  cost goodwill; we should pay for the move only if it yields a clearly
  better long-term shape.

---

## 3. Options

Five concrete options, plus honest tradeoffs. Options are not mutually
exclusive — the recommended outcome combines a few.

### Option A — Status quo, just sweep clutter

Move `process.exit` to be a thin alias of the global `exit()` (or remove it
entirely since `exit()` is already global). Leave everything else where it is.

- **Upside:** Zero churn. No breaking changes.
- **Downside:** Doesn't fix the actual smell. Future shell features will keep
  landing in `process.*` for lack of a better home.
- **Verdict:** Insufficient. But the `process.exit` cleanup should happen
  regardless of which option wins.

---

### Option B — Add nested namespaces to the language

Allow `process.dir.push("/tmp")`, `process.shell.lastExitCode()` etc.

- **Upside:** Hierarchical organization without inventing new top-level names.
  Solves the problem here _and_ gives `fs`, `arr`, `str`, `term` a way to
  organize themselves later.
- **Downside:** This is a **language change**, not a stdlib change. It touches:
  parser (member-access chains on namespace identifiers), resolver, all six
  visitors, the bytecode compiler (namespace dispatch is currently a single
  identifier lookup), LSP completion (must walk a tree, not a flat list),
  hover, the formatter, the doc generator. Repo memory shows this was already
  considered and rejected once for `prompt.theme.*`.
- **Hidden cost:** Sets up an organizational arms race. Once nested namespaces
  exist, _every_ large namespace will sprout sub-namespaces, often
  arbitrarily. `arr.sort.by`, `str.case.upper`, `fs.read.binary` — debates
  forever.
- **Verdict:** Real solution to a slightly different problem. Disproportionate
  cost for the immediate goal. Defer unless we hit two or three more
  namespaces with the same smell.

---

### Option C — Self vs. other split (`proc` + `process`)

Coin a new `proc.*` namespace for the current process; keep `process.*` (or
rename to `child.*` / `subprocess.*`) for spawning.

- **Upside:** Clean conceptual line. Matches Python (`os` self / `subprocess`
  child), Go (`os` self / `os/exec` child), Ruby (`Process` self / `Process`
  child but with a clear boundary).
- **Downside:** `proc` vs. `process` is _visually_ terrible — one letter apart,
  guaranteed typo source. Renaming `process.spawn` → `child.spawn` is a hard
  break across every existing script. Embedded host capability gating gets
  awkward (do you split `Process` into `SelfProcess` and `ChildProcess`?).
- **Verdict:** Conceptually clean but ergonomically painful. The naming
  collision alone disqualifies it.

---

### Option D — Repatriate self-state into existing namespaces (recommended)

Acknowledge that `env.*` is already the home of current-process state, and
move the misplaced functions there. Add a tiny `shell.*` namespace for the
genuinely shell-only bits.

**Concrete moves:**

| Current                     | New                                       | Justification                                                                                                            |
| --------------------------- | ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `process.chdir(path)`       | `env.chdir(path)`                         | Symmetric with existing `env.cwd()`. Reading and writing CWD now live together.                                          |
| `process.popDir()`          | `env.popDir()`                            | Directory stack is environment state.                                                                                    |
| `process.dirStack()`        | `env.dirStack()`                          | Same.                                                                                                                    |
| `process.dirStackDepth()`   | `env.dirStackDepth()`                     | Same.                                                                                                                    |
| `process.withDir(path, fn)` | `env.withDir(path, fn)`                   | Same.                                                                                                                    |
| `process.exit(code?)`       | `env.exit(code?)` (global `exit()` sugar) | Canonical impl in `env.*`; the global `exit()` becomes a thin wrapper, mirroring the established pattern for shell sugar. |
| `process.lastExitCode()`    | `shell.lastExitCode()`                    | Genuinely shell/REPL-only — only meaningful after a bare-command pipeline has run.                                       |
| `process.SIG*` constants    | global `Signal` enum                      | Decouples signal *values* from the namespace that *consumes* them; users can pass `Signal.Term` anywhere.                |
| Everything else in Table A  | *stays in `process.*`*                    | This is what the namespace is for.                                                                                       |

**New `shell.*` namespace** covers everything that only makes sense inside
the interactive REPL/shell — bare-command exit code, command history, prompt
state hooks, aliases, completion plumbing, etc. We deliberately do **not**
create a separate `repl.*` namespace: the interactive REPL and shell mode
share ~90% of their state (command history is the immediate example — it's
the same buffer in both modes), so splitting them would mean every new
feature triggers a "which namespace?" debate with no principled answer.
One namespace, one home.

- **Upside:**
  - No language change.
  - Fixes the read/write asymmetry of CWD (`env.cwd` + `env.chdir`).
  - Hides shell-only API from non-shell users (LSP completions in scripts
    don't show `lastExitCode`).
  - Sets a clean, repeatable precedent: **"current-process state lives in
    `env`; subprocess management lives in `process`; shell-mode state lives
    in `shell`."**
  - Preserves every function — it's pure relocation, not deletion.
- **Downside:**
  - Breaking rename. Every script using `process.chdir` etc. must update.
    The deprecation strategy in §5 mitigates this.
  - `shell.*` is a small namespace (one function initially). That's fine —
    `args.*` is also small. Smallness is not a reason to refuse a clean
    boundary.
  - `env.withDir` reads slightly oddly compared to `process.withDir`.
    Counter: `env.cwd` already reads naturally, so `env.chdir` and
    `env.withDir` follow consistently.
- **Verdict:** Best fit for current pain, follows existing Stash precedent,
  scales to future shell features.

---

### Option E — Promote `cwd.*` / `dir.*` as its own namespace

Spin off the directory-stack story into a dedicated `dir.*` namespace
(`dir.change`, `dir.push`, `dir.pop`, `dir.stack`, `dir.with`, `dir.current`).

- **Upside:** Single-purpose namespace, very discoverable. Mirrors Ruby's
  `Dir` class.
- **Downside:** `env.cwd` already exists and returns the same data — moving
  it again is yet another rename. Three small namespaces (`dir`, `shell`,
  `env`) for what is conceptually one bag of "current-process facts" feels
  like over-fragmentation. The `args.*` precedent shows Stash tolerates
  small namespaces, but creating multiple at once is harder to justify.
- **Verdict:** Defensible if `env.*` were not already in the picture. Given
  it is, Option D is tighter.

---

## 4. Recommendation

**Adopt Option D**, with Option A's `process.exit` cleanup folded in.

Net effect:

- `process.*` shrinks to ~20 functions, all about child processes. Easier to
  document, easier to scan in LSP completions, no shell-mode noise.
- `env.*` becomes the canonical home for current-process state — read and
  write live together.
- `shell.*` is born as the bucket for shell-mode-only state. It's empty
  enough today that we can spec the contract upfront ("only registered when
  shell mode is reachable") before it accretes anything else.
- The pattern **"if you find yourself adding self/state behavior to `process.*`,
  ask whether it belongs in `env.*` or `shell.*`"** is now written down and
  defensible.

This decision **explicitly defers Option B (nested namespaces)** until at
least two other namespaces hit the same kind of smell. When that happens,
revisit nested namespaces as a language feature rather than as a one-off
workaround.

---

## 5. Migration & Deprecation Strategy

Rename-and-deprecate, not rename-and-break.

1. **Add new locations first.** `env.chdir`, `env.popDir`, `env.dirStack`,
   `env.dirStackDepth`, `env.withDir`, `env.exit`, `shell.lastExitCode`,
   and the global `Signal` enum all start working in release N.
2. **Keep old names as deprecated aliases.** `process.chdir`, `process.exit`,
   `process.lastExitCode`, `process.SIG*` etc. continue to work in release
   N, but emit a new analysis diagnostic
   (e.g., `SA08xx: 'process.chdir' is deprecated — use 'env.chdir'`) that
   points users to the new name. Implementation is trivial — the registered
   function in `process.*` becomes a thin forward to the canonical impl, so
   semantics are identical and there is no behavior drift.
3. **Global `exit()` keeps working unchanged** — it just forwards to
   `env.exit()` internally. No deprecation needed; sugar is the point.
4. **Update repo-bundled examples and packages.** Every `examples/*.stash`
   and `@stash/*` package gets updated in release N+0 (or N+1 at the latest).
5. **Remove deprecated aliases in release N+2.** Two minor versions of
   notice is consistent with how Stash has handled prior renames.

---

## 6. Resolved Design Decisions

Resolved 2026-04-30 (see §8 Decision Log).

### 6.1 `shell.*` is capability-gated

A new `StashCapabilities.Shell` is introduced. `shell.*` is registered only
when that capability is enabled. Rationale: Stash's interpreter is embeddable
(the Playground project ships it as Blazor WASM, and other hosts may follow);
shell-mode functionality is meaningless in those contexts and should be
hidden, not just inert. Capability gating is the existing mechanism for this
(see `process.*` gated on `Capabilities.Process`).

The CLI enables `Capabilities.Shell` by default. Embedded hosts opt in.

### 6.2 One `shell.*` namespace, no `repl.*`

The shell and the REPL share ~90% of their state — command history being the
immediate motivating example. Splitting into two namespaces would force a
"which namespace?" decision for almost every new feature, with no principled
answer. We therefore use a single `shell.*` namespace as the home for both
shell-mode and interactive-REPL state. If the surface ever genuinely
bifurcates (some future feature is unambiguously REPL-only and unrelated to
shell-mode), we revisit. Until then, one bucket.

### 6.3 `exit` lives in `env.*`; global `exit()` is sugar

`env.exit(code?)` is the canonical implementation. The existing global
`exit()` (and any future global aliases like `quit()`) becomes a thin wrapper
that forwards to it. This mirrors the established pattern in shell mode where
sugars (`cd`, `pwd`, `exit`, `quit`) all desugar into stdlib calls
(see `Stash.Cli/Shell/ShellSugarDesugarer.cs`). The implementation has one
home; convenience names are wrappers.

Note: this means `process.exit` *is* removed (replaced by `env.exit` with
the deprecation alias path described in §5).

### 6.4 `Signal` becomes a global enum

The `process.SIGHUP|SIGINT|SIGQUIT|SIGKILL|SIGUSR1|SIGUSR2|SIGTERM` integer
constants are replaced by a top-level `Signal` enum (registered the same way
as the existing global `Backoff` enum in `GlobalBuiltIns.cs`):

```stash
enum Signal {
    Hup,    // 1
    Int,    // 2
    Quit,   // 3
    Kill,   // 9
    Usr1,   // 10
    Usr2,   // 12
    Term,   // 15
}
```

`process.signal(handle, sig)` accepts both `Signal` enum members and raw
`int` (for forward compatibility with platform-specific signal numbers not
in the enum). This decouples signal *values* from the namespace that
*consumes* them and lets future APIs (e.g., a hypothetical
`signal.onReceive(Signal.Term, handler)`) reuse the same vocabulary without
any awkward `process.SIGTERM` cross-namespace reference.

**Open sub-question for the Orchestrator:** the underlying signal numbers
are POSIX-only. On Windows, `process.signal(h, Signal.Term)` and
`Signal.Kill` map to `Process.Kill(...)`; other members are best-effort
(also `Process.Kill`). This matches the current behavior — see
`ProcessBuiltIns.cs` lines ~290-310 — and is documented as such.

---

## 7. Implementation Sketch (for the Orchestrator agent)

### 7.1 Capability

- Add `StashCapabilities.Shell` to the capability flags (location: same
  enum/flags type as `Process`).
- CLI (`Stash.Cli`) enables `Shell` by default; Playground and other
  embedded hosts leave it off unless explicitly requested.

### 7.2 Stdlib changes

- **New file** `Stash.Stdlib/BuiltIns/ShellBuiltIns.cs` registering
  namespace `shell` with `RequiresCapability(StashCapabilities.Shell)`.
  Initial functions: `lastExitCode()`. This file is the future home of
  command-history built-ins, alias APIs, prompt-state hooks, etc.
- **Extend `EnvBuiltIns.cs`** with `chdir`, `popDir`, `dirStack`,
  `dirStackDepth`, `withDir`, `exit`. Implementations move verbatim from
  `ProcessBuiltIns.cs` — they already use `IInterpreterContext.DirStack`,
  `EmitExit`, `CleanupTrackedProcesses`, `ExpandTilde`.
- **Extend `GlobalBuiltIns.cs`** with the `Signal` enum (model the call
  after the existing `b.Enum("Backoff", [...])`).
- **Rewire global `exit()`** in `GlobalBuiltIns.cs` to forward to the same
  implementation `env.exit` calls. Extract the body into a static helper
  shared by both registrations.
- **`process.signal()`** accepts both `Signal` enum members and `int`. The
  existing range check (1..64) applies to the resolved integer.
- **Deprecate in `ProcessBuiltIns.cs`:** `chdir`, `popDir`, `dirStack`,
  `dirStackDepth`, `withDir`, `exit`, `lastExitCode`, and each `SIG*`
  constant. Bodies become one-line forwards. Each registration is tagged
  with the new SA08xx diagnostic at analysis time.

### 7.3 Analysis

- New `DiagnosticDescriptor` SA08xx in
  `Stash.Analysis/Models/DiagnosticDescriptors.cs`:
  `'<old>' is deprecated — use '<new>' instead`. Severity: Info (or Warning;
  Orchestrator decides based on existing deprecation precedent).
- Resolver/validator emits the diagnostic on any access to a deprecated
  member. The deprecation metadata can ride on the `BuiltInFunction` /
  `BuiltInConstant` model rather than being hand-coded per name.

### 7.4 Documentation

- `docs/Stash — Standard Library Reference.md`:
  - Move `process.chdir|popDir|dirStack|dirStackDepth|withDir|exit`
    sections into §env.
  - Move `process.lastExitCode` section into a new §shell.
  - Replace the `process.SIG*` constants table with a §Signal enum entry
    in the global types section.
  - Add a deprecation note at the top of the affected `process.*` entries.
- `docs/Shell — Interactive Shell Mode.md`: update all references to
  `process.lastExitCode`, `process.chdir`, `process.popDir`, etc.
- `docs/Stash — Language Specification.md`: if it documents global enums
  (`Backoff`), add `Signal` alongside.
- Update the language-changes checklist artifacts per
  `.github/instructions/language-changes.instructions.md` (TextMate grammar,
  Monaco tokenizer, tree-sitter grammar — the new `Signal` enum members
  may need adding to highlighting word lists if those lists enumerate
  built-in enums).

### 7.5 Code-base sweeps

- Update every `examples/*.stash` calling the deprecated names.
- Update every `@stash/*` package in this repo. Run a `grep -r` over
  `process.\(chdir\|popDir\|dirStack\|dirStackDepth\|withDir\|exit\|lastExitCode\|SIG\)`
  before and after to confirm zero regressions.
- Update `Stash.Cli/Shell/ShellSugarDesugarer.cs` if it desugars `exit`/`quit`
  into `process.exit` — it should now desugar into `env.exit` (or just
  global `exit()`).
- Update `Stash.Cli/Shell/ReplLinePreprocessor.cs` if it references
  `process.lastExitCode()` for `$?` expansion — switch to
  `shell.lastExitCode()`.

### 7.6 Tests

- Add `Stash.Tests/Stdlib/EnvBuiltInsTests.cs` cases mirroring existing
  `process.chdir|popDir|dirStack|dirStackDepth|withDir|exit` tests.
- Add `Stash.Tests/Stdlib/ShellBuiltInsTests.cs` for `lastExitCode` (plus
  capability-gating: assert `shell.*` is unavailable when `Shell`
  capability is off).
- Add a `SignalEnumTests.cs` covering `Signal.Term` integer value, mixed
  enum/int acceptance in `process.signal`, and Windows mapping behavior.
- Keep one regression test per deprecated alias proving it still works
  (until removal in N+2).
- Add analysis tests for the new SA08xx deprecation diagnostic.
- Update the existing `process.exit`/`process.chdir` tests to assert the
  deprecation diagnostic is emitted (and switch their happy-path coverage
  to the new location).

### 7.7 Order of operations (to keep the build green at every commit)

1. Add `StashCapabilities.Shell`.
2. Land new `env.*` and `shell.*` registrations + `Signal` enum.
3. Rewire global `exit()` to share the `env.exit` body.
4. Convert old `process.*` registrations to forwards (no diagnostic yet).
5. Add SA08xx descriptor + emission.
6. Sweep examples, packages, shell desugarer, REPL preprocessor.
7. Update docs.
8. (Release N+2) Delete the deprecated `process.*` registrations and the
   regression tests for them.

---

## 8. Decision Log

| Date       | Decision                                                                                                             | By               |
| ---------- | -------------------------------------------------------------------------------------------------------------------- | ---------------- |
| 2026-04-30 | Spec drafted, Option D recommended, Options B/C/E rejected with rationale above.                                     | Architect        |
| 2026-04-30 | §6 resolved: `shell.*` is gated on new `Capabilities.Shell`; embeddable hosts (Playground/WASM) opt in.              | User + Architect |
| 2026-04-30 | §6 resolved: single `shell.*` namespace, no separate `repl.*` — REPL and shell mode share ~90% of state.             | User + Architect |
| 2026-04-30 | §6 resolved: `exit` becomes `env.exit`; global `exit()` is sugar wrapping it (matches existing shell-sugar pattern). | User + Architect |
| 2026-04-30 | §6 resolved: `process.SIG*` constants replaced by global `Signal` enum (modeled after `Backoff`).                    | User + Architect |

**Spec is now implementation-ready.** Recommend moving from `0-backlog/` to
`1-todo/` and assigning to an Orchestrator.
