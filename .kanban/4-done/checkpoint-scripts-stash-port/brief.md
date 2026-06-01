# RFC: Checkpoint scripts: faithful Stash port (Milestone A)

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-05-31
> **Slug:** checkpoint-scripts-stash-port
> **Milestone:** —  <!-- pilot landed standalone; a milestone doc can wrap this later -->

## Summary

Port the deterministic checkpoint-workflow scripts from bash+Python to Stash, as
dogfooding. The pilot `advance-checkpoint.py → advance-checkpoint.stash` is
already in tree (with a 16/16 differential oracle); this feature ports the
remaining DATA+GLOB scripts (`_common.py`, `validate-spec.py`, `next-phase.py`,
`status.py`, `check-parallel-safety.py`, `milestone-status.py`) and the
file-scope glob check inside `verify-phase.sh`. Behaviour is frozen per-script;
internals become idiomatic Stash. Flow redesign is explicitly out of scope.

The cutover is coexistence: `.py`/`.sh` originals stay load-bearing until each
script's `.stash` twin is proven byte-equivalent on a differential oracle, then
the `.claude/commands/*.md` and `.claude/agents/*.md` call-sites flip from
`python3 …` to `stash …`. The slash-command dispatch flip is the final phase.

## Motivation

- **Dogfood Stash.** The checkpoint workflow runs dozens of times per feature;
  porting it exercises stdlib breadth (yaml, fs, time, cli, path.match, process,
  json) and surfaces ergonomic gaps under continuous use, not under a benchmark.
- **Cut the Python toolchain off our critical orchestration path.** Today
  Stash development requires `python3` + `PyYAML` + `jq` installed just to drive
  features. Native Stash scripts let the project bootstrap from `dotnet` +
  `stash` alone.
- **Generalize the pilot's verification machinery.** The pilot's
  `difftest_advance_checkpoint.py` (isolated fixture trees, timestamp-masked
  parsed-structure compare, soft stderr) is the right shape for every ported
  script. Building a reusable harness once means every later script-phase
  acquires real equivalence proof for free.

## Goals

- Every script in scope has a `.stash` twin that is **behaviour-equivalent** to
  the `.py`/`.sh` original on the per-script tight/loose contract recorded in
  `Cross-Cutting Concerns`.
- A single `_common.stash` module owns YAML I/O, ISO timestamp formatting,
  feature-dir resolution, plan/checkpoint loaders, and atomic save — every
  ported script imports it (Construct chokepoint).
- A generalized differential oracle (`difftest_runner.stash` driven by a
  per-script case table) reports per-case pass/fail with exit code, machine-read
  stdout, parsed file-mutation deep-equal, and timestamp shape — soft stderr.
  Every script phase's `done_when` requires its differential cases pass.
- Bash/sh wrappers that genuinely orchestrate git (`bootstrap-feature.sh`,
  `promote-done.sh`, `worktree-start.sh`, `worktree-finish.sh`,
  `verify-phase.sh`) keep their shell skin but swap embedded `python3` calls for
  `stash` calls.
- `.claude/commands/*.md` and `.claude/agents/*.md` invocation lines flip from
  `python3 scripts/checkpoint/<x>.py` to `stash scripts/checkpoint/<x>.stash`
  only after that script's oracle passes.
- `stash` is invoked via the installed binary on `$PATH`, not `dotnet run …`.

## Non-Goals

- **No flow redesign.** No new commands, no rebalanced state machine, no
  altered slash-command UX. A faithful port preserves the contract verbatim.
- **No deletion of `.py`/`.sh` originals in this feature.** They stay load-bearing
  alongside the `.stash` twins for the duration of this feature; a follow-up
  feature retires them once the workflow has run cleanly on Stash for a release
  cycle.
- **No port of genuine git orchestration into Stash.** `bootstrap-feature.sh`,
  `promote-done.sh`, `worktree-start.sh`, `worktree-finish.sh` stay bash. The
  `verify-phase.sh` shell remains bash for its `bash -c "$cmd"` runner; only its
  scope/glob/metadata-extraction block ports to a Stash helper that bash calls.
- **No re-spec of the pilot.** `advance-checkpoint.stash` is committed; this
  feature does not touch it (but generalizes its oracle).
- **No Stash language work.** If a doc gap surfaces a missing primitive (e.g. a
  yet-undiscovered `path.match` corner), file a backlog bug under
  `0-backlog/stdlib/` and work around it; do not extend the language here.

## Design

### Surface

For each ported script, the **invocation surface stays identical** down to argv
shape and exit codes. Only the executable changes:

| Script (today) | Twin (after this feature) | Invoked by |
| --- | --- | --- |
| `python3 scripts/checkpoint/validate-spec.py <slug?>` | `stash scripts/checkpoint/validate-spec.stash <slug?>` | `/next-phase`, `/continue`, architect |
| `python3 scripts/checkpoint/next-phase.py <slug?> <count?>` | `stash scripts/checkpoint/next-phase.stash <slug?> <count?>` | `/next-phase` |
| `python3 scripts/checkpoint/status.py <slug?>` | `stash scripts/checkpoint/status.stash <slug?>` | `/continue`, `/next-phase` post-flight |
| `python3 scripts/checkpoint/check-parallel-safety.py <slug?>` | `stash scripts/checkpoint/check-parallel-safety.stash <slug?>` | parallel-features workflow |
| `python3 scripts/checkpoint/milestone-status.py <slug?>` | `stash scripts/checkpoint/milestone-status.stash <slug?>` | `/milestone` |
| `bash scripts/checkpoint/verify-phase.sh <slug> <id>` | (same path; internal `python3` → `stash` for scope/glob check) | implementer, `/continue` advice |

`_common.stash` and `difftest_runner.stash` are internal modules — no external
invocation surface; everyone imports them.

### Semantics

**Frozen, per-script.** The Python/bash source is the spec. The `Cross-Cutting
Concerns` table below names exactly which axes are TIGHT (machine-read; exact
equality required) and which are LOOSE (informational equivalence; reported but
not asserted).

Notable preserved-behaviour traps the implementer must respect:

- **`next-phase.py` paths emit from `2-in-progress/<slug>` only**, with no
  `4-done/` fallback (line 113, `INPROGRESS_DIR / slug`). Its loaders DO use the
  `_common.feature_dir` fallback. The port must follow this asymmetry — emitted
  `feature_dir`/`brief_path`/`spec_path` must NOT route through the fallback
  resolver, or it diverges post-`/done`.
- **`validate-spec.py` mutates `checkpoint.yaml` when auto-healing** missing
  per-phase entries. Its differential case is not just exit+stderr — the
  resulting `checkpoint.yaml` is part of the tight contract and the oracle
  diffs it (timestamp-masked).
- **`check-parallel-safety.py` and `milestone-status.py` shell out to `git
  worktree list`.** The oracle's fixture trees must be real `git init` repos so
  both implementations see the same `git` answers.
- **`next-phase` arg parsing** has an "1-arg = digit → count else slug"
  ambiguity (`raw[0].isdigit()`). It does not map cleanly to `cli.schema`
  positionals — expect a small bespoke parser, with an oracle case for the
  count-as-first-arg path.
- **`next-phase` stderr discriminator phrases** drive caller branching. The
  exact substrings `all phases done`, `in_progress`, `blocked` are TIGHT
  contract — they appear verbatim in `.claude/commands/next-phase.md` step 4.
  The full sentences (e.g. `# phase P2 is in_progress (use /resume to inspect)`)
  must be byte-identical to the originals.
- **`validate-spec` stderr / stdout** — the success line `plan/checkpoint OK
  for '<slug>': N phases` is read by callers (visible in `.md` documentation)
  and is TIGHT; the per-problem list is LOOSE-formatted but exit-status is the
  TIGHT discriminator.
- **`status.py`** stdout is purely human-facing → LOOSE; no caller parses it.
- **`milestone-status.py`** stdout is purely human-facing → LOOSE.
- **`verify-phase.sh` glob check**: the bash original uses `[[ "$f" == $pat ]]`
  with `globstar nullglob extglob` set, plus a fallback expansion loop. The
  Stash port uses `path.match(f, pat)` (confirmed bash `[[ ]]` globstar
  semantics, `*` crosses `/`). The on-disk-expansion fallback (lines 70–72) is
  redundant with `path.match` for the inputs it sees in practice (git-tracked
  paths against directory globs), but the oracle MUST include cases that
  exercise both `*`-within-segment and `**`-cross-segment patterns, the leading
  `./` strip on patterns, and the always-allow `.kanban/2-in-progress/<slug>/*`
  carve-out.

### Implementation Path

```
phase 1  _common.stash chokepoint built first  ───►  every later phase imports it
phase 2  difftest_runner.stash generalized from pilot
        (per-script config: exit/stdout/file-mutation tight, stderr soft)
              │
              ├─►  validate-spec.stash       + oracle cases  (phase 3)
              ├─►  next-phase.stash          + oracle cases  (phase 4)
              ├─►  verify-phase.sh glob port + oracle cases  (phase 5)
              ├─►  status.stash              + smoke cases   (phase 6, LOOSE)
              ├─►  check-parallel-safety.stash + oracle cases (phase 7)
              └─►  milestone-status.stash    + smoke cases   (phase 8, LOOSE)
phase 9  cutover: flip `.claude/commands/*.md` + `.claude/agents/*.md`
        invocations from `python3 …` to `stash …`; run the workflow end-to-end
        (a real `/next-phase` self-dispatch) to prove the swap.
```

Each script-phase is self-contained: it adds the `.stash` twin, registers its
case table with the differential runner, runs `difftest_runner.stash <script>`
green, commits. No cutover happens until phase 9.

### Cross-Cutting Concerns

Two concerns span every phase. Both are addressed at the **Construct** level
where the type system / architecture supports it, with a **Detect** safety net.

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| Shared helpers — YAML load/save, ISO timestamp, feature-dir resolution, plan/checkpoint loaders, atomic save, dispatch-coverage of legal state machines | `scripts/checkpoint/_common.stash` | **Construct** — built in phase 1 before any script-phase. Every ported script imports it (`import { now_iso, load_plan, … } from "_common.stash";`). A script that re-inlines a helper instead of importing diverges visibly: phase verify runs `scripts/checkpoint/lint_common_imports.stash` which scans every `scripts/checkpoint/*.stash` (excluding `_common.stash` and `difftest_runner.stash`) and **fails** if any imports zero symbols from `_common.stash`, OR if any defines a function whose name matches a `_common.stash` export (a duplicated helper). Append-only allow-list for genuine exceptions; both lists pinned in the lint. |
| Differential equivalence — every script proves behaviour-preservation against the `.py`/`.sh` original on a per-script tight/loose contract | `scripts/checkpoint/difftest_runner.stash` + a per-script case table (`difftest_<script>_cases.stash`) | **Construct** — the runner is built in phase 2 as the only path to declaring a script ported. Every script-phase's `done_when` requires `stash scripts/checkpoint/difftest_runner.stash <script>` exit 0, and the runner is part of `final_verify`. **Detect** safety net: a meta-check (`difftest_coverage_check.stash`, called by `final_verify`) enumerates every ported `.stash` script under `scripts/checkpoint/` (excluding `_common.stash`, `difftest_runner.stash`, helpers) and **fails** if any lacks a registered case table — appending a script without an oracle is the omission this guards. Pinned allow-list for the bash-skinned `verify-phase.sh` (whose oracle keys on the script name `verify-phase-scope`). The coverage check ships a fail-path self-test asserting it trips on a fixture stripped of its case table. |

The TIGHT/LOOSE matrix below is the verification single source of truth. Each
script-phase's `done_when` references its row.

| Script | Exit code | stdout | stderr (discriminator phrases) | file mutation |
| --- | --- | --- | --- | --- |
| `advance-checkpoint` (pilot) | TIGHT | — | LOOSE | `checkpoint.yaml` TIGHT (timestamp-masked) |
| `validate-spec` | TIGHT | `plan/checkpoint OK for '<slug>': N phases` TIGHT | LOOSE problem list | `checkpoint.yaml` TIGHT on auto-heal path |
| `next-phase` | TIGHT (0 vs 2) | YAML TIGHT — keys `id`, `title`, `files`, `verify`, `done_when`, `_brief.*`, batch keys `batch`/`requested_count`/`selected_count`/`phase_ids`/`phases` | TIGHT phrases: `all phases done`, `in_progress`, `blocked` (substring match suffices for caller branching; full line is byte-equal to the original) | none |
| `verify-phase` (scope/glob portion) | TIGHT (0/1/2/3) | LOOSE | TIGHT — the out-of-scope file list (one path per line, prefixed `  - `) | none |
| `status` | TIGHT (0) | LOOSE (human-only) | LOOSE | none |
| `check-parallel-safety` | TIGHT (0/2/3) | LOOSE (human-only) | LOOSE | none |
| `milestone-status` | TIGHT (0/1) | LOOSE (human-only) | LOOSE | none |

"TIGHT" = oracle asserts exact equality (or for phrases, exact substring as
defined). "LOOSE" = reported in the oracle output, never gates pass/fail.

## Acceptance Criteria

- `stash scripts/checkpoint/difftest_runner.stash --all` is exit 0 and reports
  pass for every ported script's case table.
- Phase 1's `_common.stash` is imported by every other `.stash` file in
  `scripts/checkpoint/` (Construct check passes).
- The lint (`lint_common_imports.stash`) and coverage check
  (`difftest_coverage_check.stash`) ship fail-path self-tests proving they
  trip on a stripped-down fixture; both meta-checks are wired into
  `final_verify`.
- End-to-end dogfood after phase 9: a real `/next-phase
  checkpoint-scripts-stash-port` self-dispatch (or a subsequent feature)
  completes successfully with `.claude/commands/*.md` calling `stash`, not
  `python3`. Captured as a passing log line in the phase 9 commit message.
- Every `.claude/commands/*.md` and `.claude/agents/*.md` line that today
  invokes `python3 scripts/checkpoint/<x>.py` is, after phase 9, invoking
  `stash scripts/checkpoint/<x>.stash` — `grep -rn "python3 scripts/checkpoint"
  .claude/` returns no hits in slash-command paths (the bash wrappers may still
  use python3 transitionally, but slash commands and agents do not).
- The `.py` originals remain present, executable, and unbroken (no in-place
  edits) — proven by re-running the pilot's `difftest_advance_checkpoint.py`
  green at every commit on this feature.

## Phases

See `plan.yaml`. Each phase's `done_when` names its observable behaviour.

## Open Questions

- **YAML round-trip fidelity for `checkpoint.yaml`.** The pilot proved that
  `yaml.parse` + `yaml.stringify` round-trip the pilot's checkpoint shape;
  validate-spec's auto-heal path writes a fresh per-phase mapping which is
  shape-simple. If a richer checkpoint produced by the bash workflow trips a
  serialization difference (e.g. flow vs block style), the oracle will catch it
  and the implementer files a stdlib bug rather than papering over it.
- **`process.exec` / git invocation semantics.** The pilot did not exercise
  subprocesses from Stash. Phases 7–8 (`check-parallel-safety`,
  `milestone-status`) are the first that shell out. If `process.exec`'s working
  directory / environment defaults differ from Python's `subprocess.check_output`
  in a load-bearing way, the oracle will surface it.
- **Should we delete the `.py` originals in a follow-up?** Out of scope here.
  Decision deferred to a "checkpoint scripts: retire Python originals" feature
  one release after this lands cleanly.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-05-31 | Faithful port; freeze observable contract per script | Differential oracle is the verification spine — preserving behaviour is what makes it possible. Redesign would invalidate the oracle and conflate two concerns. |
| 2026-05-31 | Build `_common.stash` first as the single source of truth for helpers | Construct beats Detect: one definition, every importer routes through it. Duplicated helpers across N scripts would re-introduce the closed-set duplication defect. |
| 2026-05-31 | Generalize the oracle in phase 2 before porting any script | The oracle is the chokepoint that makes "done" verifiable. Building per-script ad-hoc oracles would amortize-cost-shift the work into every phase and let drift in. |
| 2026-05-31 | Bash skin retained for `verify-phase.sh`; port only its scope/glob/metadata-extraction block | The bash `bash -c "$cmd"` runner is genuine shell orchestration; replacing it would expand scope. The Python-extraction block is pure data work, ports cleanly to Stash, and removes the `jq` dependency. |
| 2026-05-31 | Cutover (`.claude/commands/*.md` flip) is the final phase, not interleaved | Coexistence keeps the workflow runnable throughout; a single late flip means a clear before/after and a clean rollback point. |
| 2026-05-31 | `next-phase`'s emitted feature/brief paths must NOT use the `4-done/` fallback | Source-fidelity: the Python original takes the path directly from `INPROGRESS_DIR / slug` (no fallback), even though its loaders use the fallback. Diverging here would change post-`/done` behaviour. |
| 2026-05-31 | Invoke via installed `stash` on `$PATH`, never `dotnet run` | Decided in pilot; `dotnet run` adds seconds-of-startup per invocation, which is fatal for the workflow scripts that run dozens of times per feature. The pilot's harness already resolves `shutil.which("stash") or ~/.local/bin/stash`. |
