# RFC: Single `checkpoint` CLI entrypoint (facade)

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-06-01
> **Slug:** checkpoint-cli
> **Milestone:** —

## Summary

Today, every checkpoint-workflow operation is a separate `.stash` file you invoke by path: `stash scripts/checkpoint/next-phase.stash <slug>`, `stash scripts/checkpoint/promote-done.stash <slug>`, and so on across ~14 user-facing tools. That surface is fine for an agent reading the workflow doc, but it is hostile to a human exploring the system for the first time — there is no single command to type, no `--help` to pull up, no grouped list of what exists.

This feature introduces a single `checkpoint` dispatcher (`scripts/checkpoint/checkpoint.stash`) as a **facade** over the existing tools. Each subcommand resolves to its underlying script and re-execs it with byte-transparent passthrough — same stdout, same stderr, same exit code. The implementations stay split across one-file-per-tool (per-tool `--selftest`s, the `_common.stash` chokepoint, the pure-core/IO-shell split, all preserved). The deliverable collapses the **interface**, not the file count.

End state: `checkpoint --help` lists every subcommand grouped by lifecycle / worktree / milestone with one-liners; `checkpoint <sub> --help` shows that tool's own usage; everything that used to read `stash scripts/checkpoint/<tool>.stash …` in docs, agents, commands, and intra-script calls now reads `checkpoint <sub> …`. The facade becomes the new single source of truth for "what subcommands exist."

## Motivation

The user-facing pain is twofold.

**Discovery is broken for humans.** The workflow has ~14 subcommands. A first-time reader cannot list them without `ls scripts/checkpoint/`, and even then the file names alone do not say what each does. There is no `--help`. The user's request was explicit: *"It should provide a decent --help output in case something is not obvious even people can use it, not just agents."* This is also a quality-of-life improvement for agents: a stable `checkpoint <sub>` invocation is one bounded surface to learn instead of ~14 file-paths to memorize.

**`scripts/checkpoint/<tool>.stash` leaks an implementation path.** Today every doc, agent, and slash-command teaches the *layout* of the tools (a directory, file names with extensions). That layout is correct today but it is not the contract — the contract is "the workflow has these named operations." A facade lets the layout move (rename, split, merge) without rewriting docs.

The cost of doing nothing: the workflow continues to ship as a directory tour rather than a CLI, every new tool added forces another `scripts/checkpoint/foo.stash` reference into ~5 docs, and the human onramp stays at "read `.claude/WORKFLOW.md` first."

## Goals

- Single `checkpoint` entrypoint with a discoverable, grouped `--help`.
- Per-subcommand help: `checkpoint <sub> --help` shows that tool's own usage and exits 0.
- **Byte-transparent passthrough** to the existing tools: stdout, stderr, and exit code are identical to invoking the underlying `.stash` directly. Strict-passthrough exec, no capture-and-reprint.
- The existing tools stay one-file-per-tool. Per-tool `--selftest`s keep working unchanged via `checkpoint <sub> --selftest`.
- Every live operational doc and agent file migrates from `stash scripts/checkpoint/<tool>.stash` to `checkpoint <sub>`. Historical/immutable trees (`.kanban/4-done/`, `.kanban/0-backlog/`) are deliberately not migrated.
- The two real intra-script calls — `promote-done` → `promote-gate`, `verify-phase` → `verify-phase-scope` — migrate where the callee has a subcommand form; stay raw where the callee is internal-only.
- The subcommand set is a named bounded domain (one const registry); no inlined subcommand-name literals at use sites.
- A `Detect`-level guard (extending `lint_common_imports.stash` or a sibling lint) asserts both invariants over the **live operational surface** and ships a fail-path self-test plus a pinned exemption list.

## Non-Goals

- **No semantic changes to any existing tool.** The dispatcher routes; it does not reinterpret arguments, rewrite flags, or fold logic. `checkpoint next-phase <slug> 3` does literally what `stash scripts/checkpoint/next-phase.stash <slug> 3` does today.
- **No merge of implementations.** The 14 tools do not collapse into one file. The per-tool isolation (each with its own selftest with non-vacuity floors) is the asset we are preserving.
- **No new subcommands.** The set is exactly today's tools; renames are optional (see Decision Log).
- **No installer / packaging / PATH plumbing.** The dispatcher ships as `scripts/checkpoint/checkpoint.stash`, invoked as `stash scripts/checkpoint/checkpoint.stash <sub> …`. A convenience `bin/checkpoint` shim is an Open Question, not required.
- **No migration of historical / immutable trees.** `.kanban/4-done/` (immutable per `.kanban/CLAUDE.md`) and `.kanban/0-backlog/` (historical bug stubs narrating the original port) are deliberately exempt.
- **No replacement of `lint_common_imports`'s existing checks.** It already enforces the `_common.stash` chokepoint; we extend its scope, we do not replace it.

## Design

The facade is a `checkpoint.stash` dispatcher that reads `cli.argv[0]` as the subcommand, looks it up in a named const registry, and shells out to the matching `scripts/checkpoint/<file>.stash` with strict-passthrough exec — propagating exit code, stdout, and stderr verbatim. Top-level `--help`/`-h`/no-args prints the grouped subcommand list. `checkpoint <sub> --help` is intercepted by the dispatcher (it cannot be passthrough — see Semantics below) and prints the subcommand's stored usage synopsis.

The migration is incremental and **differentially validated**: each subcommand's existing `--selftest` is run via both the raw path and the dispatcher; the two outputs (stdout/stderr/exit) must be byte-identical.

### Surface

```text
checkpoint [-h | --help]
checkpoint <subcommand> [args...] [-h | --help]

Lifecycle:
  bootstrap-feature      Create feature dir from templates
  validate-spec          Validate plan.yaml + heal checkpoint
  next-phase             Print the next ready phase(s) as YAML
  verify-phase           Run a phase's verify commands + scope check
  advance-checkpoint     Advance phase state (pending -> in_progress -> done)
  assert-phase-landed    Confirm a phase commit landed on the branch
  status                 Print compact status for /resume
  review-findings        Parse review.md findings (--open, --count, --json)
  promote-gate           Run the promotion gate (phases + open findings)
  promote-done           Final acceptance + move to 4-done/
  feature-diff-range     Compute feature review diff boundary
  run-verify             Run a command, parse test summary, write report

Worktree:
  worktree-start         Create ../stash-<slug> on feature/<slug>
  check-parallel-safety  Warn on subsystem overlap with sibling worktrees
  worktree-finish        Merge --no-ff, re-verify on main, remove worktree

Milestone:
  milestone-status       Print a milestone's derived ledger

Meta:
  lint                   Run the _common.stash chokepoint lint + sub-cmd guards

Use 'checkpoint <subcommand> --help' for per-subcommand usage.
```

### Semantics

**Dispatch.** `cli.argv[0]` is the subcommand name. If empty or `-h`/`--help`, print the grouped top-level help (exit 0). Otherwise look up the name in the const subcommand registry; if not found, print `error: unknown subcommand: <name>` to stderr followed by the top-level help on stderr (exit 2).

**Per-subcommand help.** `checkpoint <sub> --help` cannot be passthrough. Verified at `next-phase.stash:63-66`: a single non-digit positional is treated as a slug, so `stash next-phase.stash --help` would die with `feature directory not found: --help`. Every slug-positional tool has the same shape. The dispatcher therefore intercepts a top-level `--help`/`-h` token *before* shelling out, and prints the subcommand's stored usage synopsis (held in the registry alongside the path).

**Byte-transparent passthrough.** All other invocations shell out via Stash's strict-passthrough command expression `$!>(stash ${path} ${args...})` — the same primitive `verify-phase.stash` uses to call `verify-phase-scope.stash` and `promote-done.stash` uses to call `promote-gate.stash`. The dispatcher propagates the exit code exactly; stdout and stderr stream live (no capture-and-reprint, which can mangle trailing newlines and re-order interleaved streams). Argument vector is passed through verbatim — including `--selftest`, exotic flags, and free-form strings for verify commands.

**Definition of "byte-identical":** for any subcommand `S` and arg vector `V`:

```text
diff <(checkpoint S V 2>err1; echo "exit=$?") <(stash scripts/checkpoint/S.stash V 2>err2; echo "exit=$?")
diff err1 err2
```

both produce empty diff. This is the load-bearing property of the facade and is an explicit Acceptance Criterion.

**Subcommand registry — single source of truth, shared by dispatcher and lint.** The registry is a single named const table, but it does NOT live in `checkpoint.stash`. It lives in a new pure-module `_registry.stash` so it can be `import`ed by both the dispatcher (which acts on it) and the lint (which asserts coverage against it) — eliminating a text-parse drift seam:

```text
// scripts/checkpoint/_registry.stash — importable pure module
export const SUBCOMMANDS = [
  { name: "bootstrap-feature", path: "scripts/checkpoint/bootstrap-feature.stash",
    group: "lifecycle", one_liner: "Create feature dir from templates",
    usage: "checkpoint bootstrap-feature <slug> [title]" },
  ...
];
export const INTERNAL_TOOLS = ["_common.stash", "_registry.stash",
                               "verify-phase-scope.stash", "checkpoint.stash",
                               "lint_common_imports.stash"];
```

The dispatcher `import`s `SUBCOMMANDS` and routes on it. The lint `import`s `SUBCOMMANDS` and `INTERNAL_TOOLS` and asserts the filesystem ↔ registry mapping is total. Because Stash executes top-level statements on import, `_registry.stash` must have **no top-level side effects** — only `export const` declarations. That makes `_registry.stash` itself the one and only file that legitimately holds raw subcommand paths as *data*; it goes on a separate, permanent `RAWPATH_SCAN_EXCLUSIONS` const in the lint (distinct from the temporary `RAWPATH_EXEMPTIONS` migration list, which is shrunk to empty by phase 5A).

The bounded domains are: the set of subcommand `name`s and the set of `group`s (`lifecycle`, `worktree`, `milestone`, `meta`). Both live in `_registry.stash`; no use site inlines a subcommand name.

**Tool-vs-internal boundary.** A `.stash` file in `scripts/checkpoint/` is a **subcommand** iff it is invoked as an entrypoint by something *outside* `scripts/checkpoint/` (a slash command, an agent doc, the user, a doc instruction). It is **internal** iff it is invoked only *intra-script* (callee of another tool) or as a library (`import` only).

| File | Classification | Reasoning |
| --- | --- | --- |
| `_common.stash` | library | imported only; never invoked |
| `_registry.stash` (new) | library (registry data home) | imported only by `checkpoint.stash` and the lint; pure module (no top-level side effects); on the permanent `RAWPATH_SCAN_EXCLUSIONS` list because it legitimately holds raw subcommand paths as data |
| `lint_common_imports.stash` | meta-subcommand (`checkpoint lint`) | useful manual invocation + future CI; promoted to a subcommand for discoverability |
| `verify-phase-scope.stash` | internal | callee of `verify-phase` only; no external caller |
| `promote-gate.stash` | subcommand (dual-use) | `/feature-review` calls it `--phases-only` (external entrypoint) and `promote-done` calls it (intra-script) |
| `checkpoint.stash` (new) | the facade itself | not a subcommand of itself; itself path-free (imports the registry from `_registry.stash`) |
| all others | subcommand | invoked by docs/agents/commands |

This single rule drives **both** the registry membership **and** the omission-guard's exemption list.

### Implementation Path

Dispatcher + registry + per-sub `--help` (built in phase 1) -> omission guard goes up RED-with-exemptions covering every un-migrated reference (phase 2; the guard never lands last) -> intra-script call migration (phase 3) -> live-doc migration in batches, exemption list shrinks each batch (phases 4A/4B/4C) -> guard reaches empty exemptions (phase 5) -> `final_verify` runs the dispatcher's selftest, parity check, every tool's selftest, the guard's normal + self-test, and the full `dotnet test` suite.

The plan is fail-safe at every boundary: the dispatcher works without any docs migrated (phase 1 alone is shippable); the guard never goes red without exemptions for what is still unmigrated; the intra-script and doc migrations are pure text changes verified by the guard.

### Cross-Cutting Concerns

Two cross-cutting invariants. One — registry coverage — is Construct via the dispatcher's design plus a filesystem-vs-registry lint. One — no raw path in live docs — is Detect because no Stash construct can express "a markdown file does not contain this substring." Detect is honestly the ceiling here, not a settled-for fallback.

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| Every tool-classified-as-subcommand is reachable via `checkpoint <sub>` (invariant **a**: registry coverage) | `SUBCOMMANDS` const in `_registry.stash` (importable pure module — both dispatcher and lint import it; no text-parse drift seam) | **Construct (fail-closed) + Detect supplement.** Runtime: a missing registry entry surfaces as `error: unknown subcommand: <name>` at exec, never a silent pass. Lint: the new guard `import`s `SUBCOMMANDS` and `INTERNAL_TOOLS` from `_registry.stash` and cross-checks against the filesystem — every `scripts/checkpoint/*.stash` not on `INTERNAL_TOOLS` MUST appear in `SUBCOMMANDS`, and every entry in `SUBCOMMANDS` MUST point at a file that exists. Adding a new file forces a registry edit or an explicit `INTERNAL_TOOLS` entry — there is no third option. Green from phase 2A onward (everything registered at build). |
| No live operational doc or intra-script call references a raw `scripts/checkpoint/<file-with-subcommand-form>.stash` path (invariant **b**: no raw-path leak) | Same lint (`lint_common_imports.stash` extended, or sibling `lint_checkpoint_cli.stash` — phase 2A chooses) | **Detect with teeth.** Scope is the *live operational surface only*: scans `.claude/**/*.md`, `CLAUDE.md`, `.claude/repo.md`, `.kanban/_templates/**/*.md`, `.kanban/milestones/**/*.md`, `scripts/checkpoint/*.stash`. Explicitly **excludes** `.kanban/4-done/**` (immutable per `.kanban/CLAUDE.md`) and `.kanban/0-backlog/**` (historical narrative). The guard only flags references to files that have a subcommand form (per the registry); references to *internal* tools (`verify-phase-scope`, `_common`, `_registry`, `checkpoint`) are correct and stay raw. **Two distinct named const lists in the lint:** `RAWPATH_SCAN_EXCLUSIONS` is a *permanent* exclusion (currently `_registry.stash` only — it legitimately holds raw paths as registry data) and is NOT shrunk; `RAWPATH_EXEMPTIONS` is the *temporary* migration list (files still pending text rewrites) and IS shrunk to empty across phases 3A-4C. The lint's `--self-test` ships TWO fixtures under `lint_common_imports.fixtures/`: a positive (raw subcommand path → must fire) and a negative (raw internal-tool path → must NOT fire) — both checked in one self-test run. Tampering with `RAWPATH_SCAN_EXCLUSIONS` to silence a real leak breaks the positive fixture (the fixture is *not* in `RAWPATH_SCAN_EXCLUSIONS`). The guard goes up RED-with-exemptions in phase 2A (not phase last) and shrinks `RAWPATH_EXEMPTIONS` to empty across phases 3A-4C. *Why not Construct:* the invariant spans free-form prose across many markdown files; no Stash compile-time mechanism expresses "this string does not appear in any of these files." Detect-with-teeth is the ceiling. |

The guard's normal run AND its `--self-test` AND every per-tool `--selftest` AND the dispatcher's parity-selftest AND the full `dotnet test` suite are all in `final_verify` from phase 1 onward. None are excluded from any gate.

## Acceptance Criteria

- **Discovery:** `stash scripts/checkpoint/checkpoint.stash --help` (and `-h`, and no-args) prints the grouped subcommand list with one-liners, exits 0.
- **Per-sub help:** for every subcommand `S` in the registry, `checkpoint S --help` and `checkpoint S -h` print a usage line and exit 0 *without* invoking the underlying tool. Verified by the dispatcher's `--selftest` against the full registry.
- **Byte-transparent passthrough:** for every subcommand `S` that has a `--selftest` or `--self-test`, `checkpoint S <flag>` and `stash scripts/checkpoint/S.stash <flag>` produce byte-identical stdout, byte-identical stderr, and identical exit codes. (Note: the existing tools mix `--selftest` and `--self-test`; passthrough must propagate either form verbatim.) For tools *without* a selftest flag, a **side-effect-free usage-error probe** substitutes — e.g. an invocation that hits the script's `usage:` error path (deterministic exit code + stderr, no filesystem writes, no git mutations, no `mv`, no `fs.createDir`). Parity-validating a *mutating* invocation is forbidden because `final_verify` runs the dispatcher selftest in-tree. This is the differential-validation criterion.
- **Unknown subcommand:** `checkpoint nope` exits 2, prints `error: unknown subcommand: nope` to stderr, and prints the top-level help to stderr.
- **Registry coverage (Construct):** every `scripts/checkpoint/*.stash` that is not on the `INTERNAL_TOOLS` list appears in `SUBCOMMANDS` (imported from `_registry.stash`); every entry in `SUBCOMMANDS` points at a file that exists. The lint imports the registry (no text-parse) and asserts both directions, failing on mismatch.
- **No raw-path leak in live surface (Detect):** after phase 5A, the lint reports zero exemptions and zero violations across the live operational surface defined in Cross-Cutting Concerns.
- **Intra-script migration:** `promote-done.stash`'s call to `promote-gate.stash` and `bootstrap-feature.stash`/`worktree-start.stash`'s `io.println` hint strings use the `checkpoint <sub>` form. `verify-phase.stash`'s call to `verify-phase-scope.stash` (internal) stays raw — and the guard does *not* flag it (its companion self-test fixture proves this).
- **Lint has teeth:** `checkpoint lint --self-test` exits 0 only when the guard correctly fires on the "raw subcommand path" fixture AND does NOT fire on the "raw internal-tool path" fixture. Tampered guard (e.g. exemption-list bypass) trips the self-test red.
- **No semantic regressions:** every per-tool `--selftest` is green; the full `dotnet test` suite is green; the existing `lint_common_imports.stash` normal scan AND its `--self-test` are green at every phase boundary.

## Phases

The phase list lives in `plan.yaml`. Summary:

1. **1A** — Dispatcher + registry + per-sub `--help` interception + dispatcher `--selftest` (with byte-parity cases). Tools and docs unchanged. End state: `checkpoint <sub>` works for every subcommand byte-identically.
2. **2A** — Extend the lint with invariant (a) registry-coverage (always-green, Construct supplement) and invariant (b) no-raw-path (RED with exemption list covering every un-migrated live file/script). Add fail-path self-test fixtures for both branches of (b). Defines `LIVE_DOC_GLOBS`, `INTERNAL_TOOLS`, `RAWPATH_EXEMPTIONS` named const lists. Register `lint` as a subcommand.
3. **3A** — Intra-script migration: `promote-done.stash` switches `promote-gate` call to `checkpoint promote-gate`; `bootstrap-feature.stash` and `worktree-start.stash` rewrite their hint strings. `verify-phase.stash`'s call to internal `verify-phase-scope` stays raw. Exemption list shrinks (these `.stash` files removed). Guard normal + self-test green.
4. **4A** — Doc migration batch 1: `.claude/commands/*.md` (the slash commands — highest-traffic surface). Exemption list shrinks.
5. **4B** — Doc migration batch 2: `.claude/agents/*.md` + `.claude/WORKFLOW.md` + `CLAUDE.md` + `.claude/repo.md`. Exemption list shrinks.
6. **4C** — Doc migration batch 3: `.kanban/_templates/*.md` + `.kanban/milestones/*.md` (residual live surface). Exemption list shrinks to empty.
7. **5A** — Final acceptance: guard runs with zero exemptions; `final_verify` lists every per-tool selftest + dispatcher parity selftest + guard normal+self-test + full `dotnet test`. Brief / plan / repo.md tidied.

## Open Questions

- **`checkpoint` PATH shim.** Should phase 5A add a tiny shell shim at `bin/checkpoint` (or symlink) so users can type `checkpoint …` instead of `stash scripts/checkpoint/checkpoint.stash …`? Out-of-scope by default; revisit if all-else green and trivial.
- **Subcommand renames.** Per the optional opportunity, none proposed — current names are already grouped and self-evident. Defer renames to a future RFC if discoverability complaints persist.
- **Should `lint_common_imports.stash` rename to a `lint`-suffixed file?** Phase 2A registers it as the `lint` subcommand without renaming the file (keeps grep continuity); a future cleanup could rename if there is appetite.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-01 | **Facade, not merge.** | Per-tool selftest isolation + the `_common` chokepoint guard + the pure-core/IO-shell split are the assets. Collapsing implementations would destroy all three for one fewer file. The user-visible problem is the *interface*, not the file count. |
| 2026-06-01 | **Shell-out (passthrough), not import-and-return.** | Stash executes top-level statements on module import (Language Spec §Source Files and Modules, no main-guard idiom). Import-and-return would require splitting every tool into a top-level-pure module + relocating its runnable entry, *and* re-routing direct `stash <tool>.stash` invocation — a structural change to ~14 green files for near-zero benefit on a once-per-turn cold path. Shell-out leaves the tools byte-identical and the migration becomes string substitution. Cost: re-execs the interpreter per call (acceptable; not a hot loop) and gives up structured returns (not needed; the contract is exit code + streams, which we propagate exactly). |
| 2026-06-01 | **Byte-transparent passthrough is an Acceptance Criterion, not an implementation note.** | Several tools (`next-phase` especially) have TIGHT byte-equal contracts: stderr discriminator phrases (`PHRASE_ALL_DONE`, `PHRASE_BLOCKED`), exact-bytes stdout YAML consumed by downstream parsers, exit-code semantics. Capture-and-reprint would silently break trailing-newline preservation and stderr/stdout interleaving. Strict-passthrough exec + verbatim exit-code propagation is the *only* correct shell-out; making it an Acceptance Criterion makes a regression auto-fail at verify. |
| 2026-06-01 | **`--help` is dispatcher-intercepted, not passthrough.** | Verified: `next-phase.stash:63-66` (and every slug-positional tool) treats `--help` as a feature slug and dies "feature directory not found: --help". Passthrough is unsafe; the registry stores a usage synopsis per subcommand, and `checkpoint <sub> --help` prints that without invoking the tool. The richer registry shape (`name -> {path, group, one_liner, usage}`) still satisfies the bounded-domain rule (one named const, no inlined names). |
| 2026-06-01 | **Migration scope = live operational surface; immutable trees exempt.** | `.kanban/4-done/` is declared immutable in `.kanban/CLAUDE.md` ("Reference-only — never re-edit"); `.kanban/0-backlog/` stubs *narrate* the original port. Of 106 markdown hits repo-wide, 50 live in these trees. Migrating them violates immutability; letting the guard scan them locks invariant (b) at perma-red. Live surface is 56 references across 19 files. The principled boundary: **migrate invocation guidance, preserve historical narrative.** |
| 2026-06-01 | **Invariant (b) flags only "raw path that has a subcommand form".** | `verify-phase.stash:39` calls `verify-phase-scope.stash` by raw path — this is permanent and correct because `verify-phase-scope` is internal. The guard must check `path -> subcommand-name ∈ registry`, not `path matches scripts/checkpoint/*.stash`. The tool-vs-internal boundary defined in Design drives both the registry and these exemptions. The fail-path self-test asserts both branches: fires on a fixture that names a subcommand file, does NOT fire on a fixture that names an internal file. |
| 2026-06-01 | **Construct supplement to Detect for invariant (a).** | Registry coverage *is* expressible at lint time: extend the lint to enforce that the filesystem ↔ registry mapping is total. Combined with the dispatcher's runtime fail-closed `error: unknown subcommand`, this is genuinely Construct for (a). Invariant (b) stays Detect — no Stash mechanism expresses "this string is absent from a markdown file." Naming each invariant's level honestly satisfies the architect contract's Construct-preference. |
| 2026-06-01 | **Guard goes up RED-with-exemptions in phase 2A, not phase last.** | Per the architect doctrine: "Never schedule [a Detect guard] as a final phase — then every prior phase merges with the invariant unenforced." Phase 2A builds the guard with a complete exemption list; phases 3A–4C shrink it. The guard is always running, never absent. |
| 2026-06-01 | **No subcommand renames in this RFC.** | The opportunity exists; the cost is touching the same call sites twice (once for facade, again for rename), and the current names are already self-evident and grouped. Defer to a future RFC if real discoverability complaints arise. |
| 2026-06-01 | **`checkpoint.stash` is self-excluded from its own coverage guard but **not** from `lint_common_imports`'s `_common` import chokepoint.** | The dispatcher is the facade, not a subcommand — it must not appear in `SUBCOMMANDS`. But it lives in `scripts/checkpoint/*.stash` and so falls under the existing `_common` import rule; it imports `die` (or another `_common` symbol) by design. Both self-exclusions live in named const lists in the lint, alongside the others. |
| 2026-06-01 | **Registry const lives in `_registry.stash` (pure module), not in `checkpoint.stash`.** | Two payoffs: (i) the lint `import`s `SUBCOMMANDS` (and `INTERNAL_TOOLS`) directly from `_registry.stash`, eliminating a text-parse drift seam between dispatcher and guard (a regex against `checkpoint.stash` could diverge from what the dispatcher actually routes on); (ii) it relocates the permanent "raw paths as data" exclusion to exactly one tiny file, leaving `checkpoint.stash` itself path-free and able to flow through the same RAWPATH guard as any other live file. Constraint: `_registry.stash` MUST have zero top-level side effects (Stash executes top-level on import) — only `export const` declarations. The dispatcher being the authoritative *consumer* satisfies "dispatcher owns the registry"; it does not require the literal data to be inlined in `checkpoint.stash`. |
| 2026-06-01 | **Two distinct exclusion lists: `RAWPATH_SCAN_EXCLUSIONS` (permanent) vs `RAWPATH_EXEMPTIONS` (temporary).** | A single "exemption" list muddles two different semantics: "this file should never be scanned because it legitimately holds the data" (permanent, structural) vs "this file is a migration backlog item" (temporary, shrinks to empty). Conflating them makes phase 5A's "empty exemptions" unsatisfiable (the registry data file can never leave). Two lists, distinct names, distinct lifecycles. Tampering with `RAWPATH_SCAN_EXCLUSIONS` to silence a real leak is caught by the positive fail-path fixture (which is not, and must never be, on the permanent list). |
| 2026-06-01 | **No-selftest parity probes target the side-effect-free usage-error path only.** | The dispatcher selftest runs in `final_verify` against the real tree. A probe of `checkpoint bootstrap-feature` (with valid args) would create a feature directory during the test; `checkpoint promote-done` would mv a directory; `checkpoint worktree-start` would create a worktree. The probe must instead hit each script's deterministic `usage:` error path — exit code + stderr-bytes parity is what we are verifying anyway, and that path is reachable without mutation. Recorded explicitly because "representative-args parity" is dangerously ambiguous otherwise. |
