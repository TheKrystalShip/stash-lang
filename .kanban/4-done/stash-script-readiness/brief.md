# RFC: Stash Script Readiness — `path.match` glob predicate

> **Status:** Draft
> **Owner:** cristian.moraru@live.com
> **Created:** 2026-05-31
> **Slug:** stash-script-readiness
> **Milestone:** —

## Summary

Add a pure, ungated `path.match(path: string, pattern: string) -> bool` stdlib
function that tests a path **string** against a glob **pattern** using
bash-`[[ ]]`-compatible semantics (with `shopt -s globstar`), and gate the
feature on a rebuilt+installed `stash` binary at `~/.local/bin/stash`. This is
the **single blocking primitive** for rewriting `scripts/checkpoint/*.{py,sh}`
in Stash (feature 2 of this pair); the install gate is the explicit ordering
boundary between the two features.

`path.match` is side-effect free, requires no capability, and never touches the
filesystem — the path argument may not exist on disk.

## Motivation

`scripts/checkpoint/verify-phase.sh` (the heart of phase verification) enforces
file-scope by matching every changed-file path string from
`git diff --name-only` against the phase's `files`/`scope` glob patterns
**without touching the filesystem**. The changed file may have been deleted, and
the patterns are gitignore-style, not FS-expansion queries.

Stash has no primitive that expresses this today:

- `fs.glob(pattern)` expands a pattern against the real filesystem
  (`Directory.GetFiles(..., AllDirectories)`); it cannot test an arbitrary
  (possibly non-existent) path string.
- `path.*` is pure string manipulation (`dir`/`base`/`ext`/`join`/…); no matcher.
- `re.test` is regex, not glob; callers would have to hand-roll glob→regex and
  risk diverging from bash semantics.

A regen of the checkpoint scripts in Stash cannot proceed until this primitive
exists in a published binary.

## Goals

- A new `path.match(path, pattern) -> bool` function in the `path` namespace,
  ungated and pure.
- Behavior pinned to bash `[[ string == pattern ]]` with `globstar` (the exact
  matcher used by `scripts/checkpoint/verify-phase.sh:64-72`), validated against
  a fixture of **real `(path, pattern)` pairs harvested from
  `.kanban/4-done/*/plan.yaml`** and the parked `.kanban/2-in-progress/*/plan.yaml`
  files.
- A rebuilt `stash` binary installed to `~/.local/bin/stash` such that
  `stash -c 'io.println(path.match("a/b.cs","a/**"))'` prints `true` — the
  ordering boundary that lets feature 2 proceed.
- Full `language-changes.md` compliance: stdlib metadata + regenerated docs +
  LSP completion + meta-tests + xUnit tests + example `.stash`.

## Non-Goals

- The checkpoint-script rewrite itself (feature 2 of 2). Not designed or specced
  here; only an Open-Questions note that it follows this feature.
- `yaml.stringify` `width` option. Explicitly deferred; see
  `.kanban/0-backlog/stdlib/yaml-stringify-width-option.md` — Stash's emitter
  leaves long scalars unfolded; one-time cosmetic reformat on cutover is
  acceptable.
- Bash extglob constructs (`@(...)`, `!(...)`, `+(...)`, `?(...)`, `*(...)`).
  A scan of every plan.yaml in-repo confirms zero usage; supporting them is
  YAGNI for *parity*. Hitting one must produce a clear `RuntimeError`, not
  silent fallthrough.
- Filesystem expansion (the `for expanded in $pat; do ...` fallback at
  `verify-phase.sh:70-72`). Only the `[[ ]]` string-match path is reproduced;
  the FS-fallback is a no-op when patterns don't expand to deleted files, which
  is the only case the scope check uses it for. (See Decision Log.)
- Glob→regex conversion exposed to user code. Internal implementation detail.
- Case-insensitive matching. Bash is case-sensitive on the patterns we care
  about; `path.match` is case-sensitive.
- A separate "match many patterns" variant. Callers loop in Stash; the
  primitive matches one pattern.

## Design

### Surface

```stash
// path.match(path: string, pattern: string) -> bool
//   Returns true iff `path` matches `pattern` under bash [[ ]] globstar semantics.
//   Pure. No capability. Does not touch the filesystem.
io.println(path.match("a/b.cs", "a/**"));      // true
io.println(path.match("a/b.cs", "a/*.cs"));    // true  (* crosses '/')
io.println(path.match("a", "a/**"));           // false (** requires >= 1 segment)
io.println(path.match("Stash.Core/Foo.cs", "Stash.Core/**"));  // true
```

C# authoring shape (in `Stash.Stdlib/BuiltIns/PathBuiltIns.cs`, per the
source-generator convention):

```csharp
/// <summary>Returns true iff <paramref name="path"/> matches the glob
/// <paramref name="pattern"/> under bash [[ ]] globstar semantics.</summary>
/// <param name="path">The path string to test (need not exist).</param>
/// <param name="pattern">The glob pattern.</param>
/// <returns>Whether the path matches the pattern.</returns>
[StashFn]
public static bool Match(string path, string pattern) => PathGlob.Matches(path, pattern);
```

Helper lives in `Stash.Stdlib/BuiltIns/PathGlobImpl.cs` (not Stash-visible).

### Semantics (pinned to bash, NOT to .NET globbers)

| Construct       | Behavior                                                            | Bash verdict reproduced |
| --------------- | ------------------------------------------------------------------- | ----------------------- |
| `*`             | Matches zero or more of **any** character, **including `/`**.       | Yes                     |
| `**`            | Same as `*` in `[[ ]]` (no special segment semantics).              | Yes                     |
| `?`             | Matches exactly one character (including `/`).                      | Yes                     |
| `[abc]`         | Character class: matches one of the listed characters.              | Yes                     |
| `[a-z]`         | Character class range.                                              | Yes                     |
| `[!abc]`/`[^…]` | Negated class. `[^…]` is the bash form; `[!…]` is also accepted.    | Yes                     |
| literal         | Verbatim character match, case-sensitive.                           | Yes                     |
| `\x`            | Escapes the following metacharacter (`*`, `?`, `[`, `\`).           | Yes                     |
| extglob         | **Rejected** with a `RuntimeError("path.match: …")` (Non-Goal).     | n/a                     |

**Key empirical findings** (verified in this session against `bash` with
`shopt -s globstar extglob`):

- `[[ "Stash.Analysis/Visitors/Sub/Foo.cs" == Stash.Analysis/Visitors/*.cs ]]`
  is **true** in bash — `*` crosses `/`. (`Microsoft.Extensions.FileSystemGlobbing`
  and `ProjectConfig.GlobMatches` both return **false** here. Reuse would be
  a silent scope regression.)
- `[[ "a" == a/** ]]` is **false** — `**` requires at least one segment.
- Bash is case-sensitive. `ProjectConfig.GlobMatches` uses
  `RegexOptions.IgnoreCase`. Reuse would be a silent scope regression.

These findings rule out reusing `Stash.Analysis/Models/ProjectConfig.GlobMatches`
(also a backwards-layer dep: `Stash.Stdlib` → `Stash.Analysis` is not allowed).
**Implementation is a fresh glob→regex translator** in
`Stash.Stdlib/BuiltIns/PathGlobImpl.cs`, structured as: walk the pattern char by
char emitting regex tokens, anchor with `^…$`, match case-sensitively. The
translation rules: `*` and `**` → `.*`; `?` → `.` (any single char including
`/`); `[…]` → `[…]` with leading `!` rewritten to `^`; `\x` →
`Regex.Escape(x)`; every other metacharacter escaped. The fixture is the
oracle for any remaining edge case.

### The bash-decision parity fixture (load-bearing)

Phase P1 builds a fixture file
`Stash.Tests/Stdlib/Fixtures/path-match-bash-parity.tsv` populated by a one-shot
bash harness that:

1. Greps every `.kanban/4-done/*/plan.yaml` and parked
   `.kanban/2-in-progress/*/plan.yaml` for every `files:` / `scope:` entry,
   collects the unique patterns.
2. Pairs each pattern with a representative path set (the literal pattern
   "stripped of metachars" plus realistic siblings — synthesizing matches and
   non-matches).
3. For every `(path, pattern)` pair, computes the bash verdict as the OR of
   `[[ "$f" == $pat ]]` and the for-expansion loop (`verify-phase.sh:64-72`)
   under `shopt -s globstar nullglob extglob`.
4. Writes one TSV row per case: `pattern\tpath\texpected_bool`.

This fixture is **the oracle**. The `PathMatchBashParityTests` xUnit class
reads it as a `[Theory]` and asserts `PathBuiltIns.Match(path, pattern) ==
expected` for every row. A regression here means scope decisions change for
real plans.

### Implementation Path

`PathGlobImpl` glob-to-regex translator (Stash.Stdlib) →
`PathBuiltIns.Match` `[StashFn]` wired through source generator →
stdlib metadata regenerates `docs/Stash — Standard Library Reference.md` →
`CompletionSurfaceSnapshotTests` re-baselined (LSP completion surface picks up
`path.match`) → `Wave1ThrowsCoverageTests` notes `path.match` either gains
throws metadata (for extglob rejection) or joins the `NoThrowAllowList` →
xUnit `PathMatchTests` (unit cases) + `PathMatchBashParityTests` (fixture-driven
oracle) → example `examples/path_match.stash` → final phase rebuilds the CLI
via `build.stash`, installs to `~/.local/bin/stash`, and asserts the smoke
command prints `true`.

### Cross-Cutting Concerns

This is a stdlib-only feature; the language/runtime layers are untouched.
However the **`language-changes.md` checklist** is itself a cross-cutting
omission concern: stdlib additions historically silently skipped docs
regeneration, LSP surfacing, or throws-metadata. The single source of truth
for each is its enforcement meta-test; each has a self-test proving its scan
has teeth, and `final_verify` keeps them included.

| Concern                                  | Single source of truth                                              | Omission prevented by                                                                                                                                                                                                                                                                          |
| ---------------------------------------- | ------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Stdlib API in the user-facing reference  | `docs/Stash — Standard Library Reference.md` (generated)            | **Detect** — `StandardLibraryReferenceTests` diffs the generated doc against `StdlibDefinitions` metadata. A new `[StashFn]` without `dotnet run --project Stash.Docs/` re-run will fail this test. Baked into phase P2's `verify`.                                                             |
| LSP completion surface                   | `Stash.Tests/Lsp/Snapshots/*.txt` (embedded snapshots)              | **Detect** — `CompletionSurfaceSnapshotTests` snapshots completion sets at canonical cursor positions; un-baselined snapshot diff fails. Re-baseline with `STASH_SNAPSHOT_REGEN=1`. Baked into phase P2's `verify`.                                                                             |
| Throws-coverage metadata                 | `Wave1ThrowsCoverageTests.NoThrowAllowList` (test-pinned list)      | **Detect** — every Wave-1 function must either have `<exception>` metadata or sit in `NoThrowAllowList`. The allow-list is the pinned exemption set: adding a member forces a test edit. Baked into phase P2's `verify`. (`path.match` joins the allow-list — see Decision Log.) |
| Bash-decision parity for scope matching  | `Stash.Tests/Stdlib/Fixtures/path-match-bash-parity.tsv`            | **Detect** — `PathMatchBashParityTests` is a `[Theory]` over the fixture. Drift between `path.match` and bash on the real corpus fails the test. The fixture-regenerator script is committed in P1 so re-derivation is reproducible; new plan.yaml patterns extend the corpus.                  |
| Binary install ordering boundary         | `~/.local/bin/stash` invoking `path.match`                          | **Construct** — the final phase's `done_when` requires `stash -c '…path.match…'` to print `true`. If the install is skipped or the build fails, the script literally errors with `path: function 'match' not found`. Feature 2 cannot begin until `/done` accepts this gate.                  |

## Acceptance Criteria

- `path.match` is callable from any Stash script with no `--allow-*` flags
  (ungated), e.g. `stash -c 'io.println(path.match("a/b.cs","a/**"))'` prints
  `true`.
- **Bash-decision parity:** `PathMatchBashParityTests` is green over the full
  fixture harvested from real `plan.yaml` files. Every `(path, pattern)`
  decision matches the bash oracle.
- **Unit coverage:** `PathMatchTests` exercises `*`, `**`, `?`, character
  classes (including negated forms), escapes, literal matches, empty-string
  edges, and the extglob-rejection error path.
- **Docs regenerated:** `docs/Stash — Standard Library Reference.md` lists
  `path.match` under the `path` namespace with the correct signature and
  summary; `StandardLibraryReferenceTests` green.
- **LSP surface:** `path.match` appears in completion at a cursor inside a
  `path.` member access; `CompletionSurfaceSnapshotTests` re-baselined and
  green.
- **Throws-coverage:** `Wave1ThrowsCoverageTests` green (`path` namespace's
  allow-list updated with `match`, or throws metadata recorded — see
  Decision Log).
- **Example script:** `examples/path_match.stash` exists, runs, and produces
  the expected output.
- **Install gate (terminal):** running `bash build.stash` (or its documented
  equivalent) produces a new `stash` at `~/.local/bin/stash`, and
  `stash -c 'io.println(path.match("a/b.cs","a/**"))'` prints exactly `true`
  followed by a newline. The previous 0.5.0 binary is overwritten in place.

## Phases

Phase list lives in `plan.yaml`. Summary:

- **P1 — Parity fixture & harness.** Generate the bash-truth TSV from real
  plan.yaml patterns; commit the harness and the fixture; write the
  `PathMatchBashParityTests` skeleton (initially `[Fact(Skip="…")]` because
  the implementation lands in P2 — turns green in P2).
- **P2 — `path.match` implementation + stdlib checklist.** Implement
  `PathGlobImpl` + `PathBuiltIns.Match` with metadata, regenerate docs, update
  the LSP completion snapshot, update `Wave1ThrowsCoverageTests` allow-list,
  add unit tests, turn the parity test green.
- **P3 — Example script.** Add `examples/path_match.stash` demonstrating
  realistic scope-matching against a fabricated changed-files list.
- **P4 — Install gate.** Rebuild via `build.stash`, install to
  `~/.local/bin/stash`, prove `stash -c '…'` prints `true`. Terminal phase;
  feature 2 cannot begin until this is green.

## Open Questions

- **Feature 2 (checkpoint-script rewrite) is intentionally out of scope.**
  Once `/done stash-script-readiness` lands, spec the rewrite as its own
  feature. It will: (a) replace `verify-phase.sh`'s scope loop with
  `path.match` calls, (b) port `validate-spec.py` / `status.py` /
  `advance-checkpoint.py` to Stash, (c) verify **semantically** (parsed-YAML
  structural equality + exit-code equality), **not** byte-diff, because
  Stash's YAML emitter differs cosmetically from PyYAML (see the readiness
  ledger guardrail 3). Carry forward all three guardrails from
  `.kanban/0-backlog/stdlib/path-match-predicate.md`.
- **Should `path.match` throw on a malformed pattern (unclosed `[`,
  trailing `\`) or return `false`?** Bash treats malformed patterns as
  literal-string match. We mirror bash: malformed patterns are treated as
  literals (never throw on malformed pattern shape); only extglob constructs
  `@(`/`!(`/`+(`/`?(`/`*(` raise.

## Decision Log

| Date       | Decision                                                                                                                                              | Rationale                                                                                                                                                                                                                                                                                                                                                                |
| ---------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 2026-05-31 | **Fresh matcher in `Stash.Stdlib`, not reuse of `ProjectConfig.GlobMatches`.**                                                                        | Empirically verified in this session: bash `[[ ]]` lets `*` cross `/` (true: `Stash.Analysis/Visitors/Sub/Foo.cs == Stash.Analysis/Visitors/*.cs`); `ProjectConfig.GlobMatches` maps `*` to `[^/]*` (false). Bash is case-sensitive; `ProjectConfig.GlobMatches` uses `RegexOptions.IgnoreCase`. Reuse would silently change which files count as in-scope. Also `Stash.Stdlib → Stash.Analysis` is a backwards layer dep. |
| 2026-05-31 | **Acceptance = bash-decision parity over real plan.yaml patterns, not generic glob correctness.**                                                     | Per readiness-spike guardrail 1. A "correct" generic matcher that disagrees with bash on edge cases is a scope regression for the checkpoint scripts.                                                                                                                                                                                                                    |
| 2026-05-31 | **No extglob support; `@(`/`!(`/`+(`/`?(`/`*(` raise `RuntimeError`.**                                                                                 | Zero usage in any plan.yaml in the repo (grepped). Supporting full bash extglob is YAGNI for parity. Loud rejection is safer than silent fallthrough.                                                                                                                                                                                                                    |
| 2026-05-31 | **`path.match` joins `NoThrowAllowList` despite the extglob-rejection path.**                                                                          | The "infallible read" property the allow-list represents is for *legal inputs*. Extglob is documented as out of scope (a programmer error, not a normal-path exception). Documented alternative: give `path.match` `<exception cref="RuntimeError">` for extglob and stay off the allow-list — revisit if the wave-1 convention drifts.                                    |
| 2026-05-31 | **Malformed patterns (unclosed `[`, trailing `\`) match as literals, never throw.**                                                                    | Matches bash behavior. Keeps `path.match` a true predicate; reduces surprise.                                                                                                                                                                                                                                                                                            |
| 2026-05-31 | **Install gate is the terminal phase, not just a `done_when` on P2.**                                                                                  | The gate is the ordering boundary between this feature and feature 2. Terminal placement means `/done stash-script-readiness` cannot pass without it, and feature 2 cannot begin without `/done` — physical interlock.                                                                                                                                                  |
| 2026-05-31 | **FS-expansion fallback at `verify-phase.sh:70-72` is not reproduced.**                                                                                | That branch only matters when a pattern expands to a file on disk that the `[[ ]]` form would have missed; for the *changed file* (which may have been deleted), it is a no-op. `path.match` is the pure string predicate; if a future caller needs FS expansion it composes `fs.glob` + `path.match`.                                                                   |
| 2026-05-31 | **Feature 2 not specced here.**                                                                                                                       | Hard ordering boundary. Feature 2 (checkpoint-script rewrite) requires the installed binary; specifying it now risks designing against a binary that doesn't exist. Spec opens when `/done stash-script-readiness` is green.                                                                                                                                              |
| 2026-05-31 | **P4 builds+installs the CLI only (build.stash's "documented equivalent"), not full `build.stash`.** (orchestrator correction, during P4) | Full `build.stash` runs `buildExtension()` → `npx tsc`, which fails in this env (the extension's `node_modules` was never installed; `npx` resolves a foreign `tsc`). That failure is unrelated to `path.match` and, because `deploy()` runs before `buildExtension()`, would fail the gate *after* the binary was already installed. The Acceptance Criteria already allowed "`bash build.stash` (or its documented equivalent)". The CLI carries `path.match` via its `Stash.Stdlib` dependency, so `dotnet publish Stash.Cli/ --self-contained` + `install` the AOT binary achieves the gate's true intent and is strictly more conservative (does not overwrite the user's `stash-lsp`/`stash-dap`/`stash-registry`/`stash-check`/`stash-format` binaries). Functional smoke unchanged. |
| 2026-05-31 | **P4 gate proves "new binary installed" FUNCTIONALLY, not by version string; `Version` const NOT bumped.** (orchestrator correction, pre-implementation) | `stash --version` is a hardcoded literal `0.5.0` in `Stash.Cli/Program.cs:51` with no sha/timestamp component, so the original `done_when` "version strictly greater than 0.5.0 (or differing build tag)" was *unsatisfiable* without an out-of-P4-scope source bump. The functional smoke (`path.match` resolves from the installed binary — the 0.5.0 binary lacks the function and errors) is strictly stronger proof and keeps P4 a pure observation gate. Also fixed P4's `build.stash` verify command which hardcoded `cd /home/heisen/stash-lang` (main checkout) — would have built main's `path.match`-less source in the worktree; now cwd-relative (worktree-portable). |
