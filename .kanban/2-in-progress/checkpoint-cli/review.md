# checkpoint-cli — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `c7d7125e..HEAD` on branch `main`
**Brief:** ../brief.md
**Generated:** 2026-06-02

## Summary

Two findings — one MEDIUM design-scope gap (the `LIVE_DOC_GLOBS` `.md`-only restriction
silently re-seeds the legacy invocation form into every new feature's `plan.yaml` via the
unscanned `plan-template.yaml`), one LOW brief/code text drift (the brief's Semantics
section still describes a strict-passthrough primitive that the dispatcher does not — and
correctly does not — use).

The feature is otherwise well-executed. Verified during review:

- Dispatcher selftest is fully green (17/17 byte-parity).
- Lint normal scan is green (0 violations, 0 exemptions); lint `--self-test` is green
  across all three fixtures (positive + two negative).
- Top-level `--help` / `-h` / no-args produce byte-identical grouped help on stdout (exit 0);
  unknown subcommand routes both error and help to stderr with exit 2.
- Per-sub `--help` is dispatcher-intercepted; raw invocation of e.g.
  `next-phase.stash --help` would die ("feature directory not found: --help") confirming
  the brief's Decision-Log claim that passthrough is unsafe for `--help`.
- All 17 parity probes verified non-mutating by source read AND by empirical check:
  `git status --porcelain` is clean both before and after a full `--selftest` run.
- Migration completeness: zero raw `scripts/checkpoint/<subcommand>.stash` references
  in `.claude/**/*.md`, `CLAUDE.md`, or `.kanban/_templates/**/*.md` (the lint's scanned
  surface). The only remaining `verify-phase-scope.stash` raw call in
  `verify-phase.stash:39` is correct (internal tool — pinned by the negative fixture).
- Bounded-domain compliance: the dispatcher uses no inlined subcommand-name or group-key
  literals; it iterates `SUBCOMMANDS`/`GROUPS` exclusively. Registry entries inline group
  keys as string values, but only at the definition site (the analogue of an enum value
  declaration) — not a use-site violation.

The two findings are below.

---

## F01 — [MEDIUM] LIVE_DOC_GLOBS `.md`-only exclusion lets `plan-template.yaml` re-seed the legacy form into every new feature

**Status:** open
**Files:** `.kanban/_templates/plan-template.yaml:5`, `.kanban/_templates/checkpoint-template.yaml:3-4`, `scripts/checkpoint/lint_common_imports.stash:84-91` (the `LIVE_DOC_GLOBS` definition)
**Phase:** 2A (LIVE_DOC_GLOBS shape) / 4C (templates batch)
**Commit:** e581ef7c (2A guard), 964a4971 (4C)

### Observation

`scripts/checkpoint/lint_common_imports.stash:84-91` defines `LIVE_DOC_GLOBS` as `.md`-only
within `.kanban/_templates/**` (a deliberate choice — the brief's Cross-Cutting Concerns
table notes "`.kanban/_templates/**/*.md` is .md only — .yaml templates are out of scope").
Three raw subcommand-path references live in YAML templates that the guard therefore never
sees:

1. `.kanban/_templates/plan-template.yaml:5` —
   `# Validate with: stash scripts/checkpoint/validate-spec.stash <feature>`
2. `.kanban/_templates/checkpoint-template.yaml:3` —
   `# Created by ... scripts/checkpoint/bootstrap-feature.stash`
3. `.kanban/_templates/checkpoint-template.yaml:4` —
   `# Updated atomically by scripts/checkpoint/advance-checkpoint.stash`

The first is qualitatively different from the other two: `bootstrap-feature.stash` copies
`plan-template.yaml` into `.kanban/2-in-progress/<slug>/plan.yaml` verbatim (via `sed`,
which substitutes only the `feature:` / `title:` / `created:` fields — the validate hint
line passes through untouched). Evidence the propagation has already occurred:

```
$ grep -n "validate-spec.stash" .kanban/2-in-progress/checkpoint-cli/plan.yaml
5:# Validate with: stash scripts/checkpoint/validate-spec.stash checkpoint-cli
```

This feature's own `plan.yaml` carries the legacy form. Every future feature created by
`bootstrap-feature` will inherit it. The guard cannot catch this because:

- `LIVE_DOC_GLOBS` excludes `*.yaml` in `_templates/` (the seed).
- `LIVE_DOC_GLOBS` excludes `.kanban/2-in-progress/**` entirely (the destination).

The leak therefore has a self-propagating path that bypasses the guard end-to-end.

The other two references (checkpoint-template.yaml lines 3-4) are weaker — descriptive
comments naming the implementation files, not runnable commands. They're equivalent in
shape to the WORKFLOW.md "Scripts" table (which 4B explicitly allowed to keep file names
for grep-continuity). Mention them for completeness but the primary defect is the
runnable command in `plan-template.yaml`.

### Why this matters

The feature's stated motivation (brief.md:23, 25) is precisely *to stop seeding raw
subcommand paths into new artifacts*:

> every new tool added forces another `scripts/checkpoint/foo.stash` reference into ~5 docs

`plan-template.yaml` is the seed for every new feature's plan, and it still teaches the
old form. The end state (zero `RAWPATH_EXEMPTIONS`, lint green) is correct for files the
guard scans, but the brief's `.md`-only glob choice means the dispatch surface that the
*workflow itself* generates is unprotected. A user who reads their freshly bootstrapped
`plan.yaml` is told to validate with `stash scripts/checkpoint/validate-spec.stash …` —
i.e. the runnable form the migration deliberately retired.

This is a design-scope gap, not an implementation bug: the implementation faithfully
executed the brief's chosen glob set. But the brief's deliberate `.yaml` exclusion has a
consequence the Cross-Cutting Concerns table never reckoned with — the templates are the
source of every future feature's text, not a static historical artifact.

### Suggested fix

Two options, pick one:

1. **Migrate the template strings and leave the guard alone** (minimal change).
   - In `.kanban/_templates/plan-template.yaml:5`, replace
     `stash scripts/checkpoint/validate-spec.stash <feature>` with
     `stash scripts/checkpoint/checkpoint.stash validate-spec <feature>`.
   - In `.kanban/_templates/checkpoint-template.yaml:3-4`, replace the two
     `scripts/checkpoint/{bootstrap-feature,advance-checkpoint}.stash` references with the
     `checkpoint <sub>` form (these are comments; either the runnable form or a bare
     `\`checkpoint <sub>\`` mention is fine).
   - Also fix this feature's own `.kanban/2-in-progress/checkpoint-cli/plan.yaml:5` so the
     in-progress dir doesn't look post-migration but pre-form.

2. **Migrate the template strings AND extend the guard** (durable).
   - Same text changes as option 1, plus
   - Add `.kanban/_templates/**/*.yaml` to `LIVE_DOC_GLOBS` in
     `lint_common_imports.stash:84-91`. This costs one glob expansion and turns "future
     template drift" from a Detect gap into a Detect-enforced invariant.
   - Optionally also add `.kanban/2-in-progress/**/plan.yaml` for symmetry, though the
     more pressing fix is upstream at the template.

Option 2 is the more principled fix (Detect-with-teeth across the actual seed/destination
surface, consistent with the architect-doctrine "Construct > Detect" preference); option 1
is the cheaper short-term resolution.

### Verify

```
# Both options
grep -n "scripts/checkpoint/" .kanban/_templates/*.yaml | grep -v "checkpoint.stash"
# expect: no output, except possibly registry-data refs

# Option 2 only — verify glob coverage
stash scripts/checkpoint/lint_common_imports.stash
# expect: 0 violations, scans yaml templates too
```

---

## F02 — [LOW] Brief Semantics text says `$!>` (strict) but the dispatcher uses `$>` (non-strict) — code is correct, brief is stale

**Status:** open
**Files:** `.kanban/2-in-progress/checkpoint-cli/brief.md:93`, `scripts/checkpoint/checkpoint.stash:189`
**Phase:** 1A
**Commit:** 50e54557

### Observation

The brief's Semantics section (line 93) specifies the passthrough primitive as
strict-passthrough:

> All other invocations shell out via Stash's strict-passthrough command expression
> `$!>(stash ${path} ${args...})` — the same primitive `verify-phase.stash` uses to call
> `verify-phase-scope.stash` and `promote-done.stash` uses to call `promote-gate.stash`.

The dispatcher (`checkpoint.stash:189`) instead uses **non-strict** passthrough:

```
let result = $>(stash ${entry.path} ${...rest});
env.exit(result.exitCode);
```

The dispatcher's docstring (lines 11-18) explicitly documents that this is the correct
choice — `$!>` would throw on non-zero exits, complicating exact exit-code propagation;
`$>` returns a `CommandResult` for *all* exit codes, after which `env.exit(result.exitCode)`
forwards the code verbatim. The brief's quoted "strict-passthrough" wording is now stale.

The dispatcher's selftest (17/17 byte-parity, validated this review) confirms the code's
choice produces byte-identical stdout / stderr / exit code for every registered
subcommand.

### Why this matters

Low impact — runtime behavior matches the brief's *intent* (byte-transparent passthrough,
exit-code fidelity), only the textual primitive named in the brief is now stale. A future
reader cross-referencing brief.md:93 against checkpoint.stash:189 will see a divergence
and may be tempted to "fix" the code (back to `$!>`) in a way that would actually break
the contract.

### Suggested fix

Either:

1. Update `.kanban/2-in-progress/checkpoint-cli/brief.md:93` to describe `$>` (non-strict
   passthrough) and note that `env.exit(result.exitCode)` is what propagates the code.
   Add a one-line note to the Decision Log noting the primitive choice during 1A (`$>` +
   `env.exit` over `$!>` + try/catch, because `$!>` throws on non-zero).
2. Leave the brief alone and accept that the implementation refined the primitive choice
   during phase 1A; the dispatcher source already documents it.

Option 1 is preferred (keeps the brief authoritative for future readers). Either is fine.

### Verify

```
# Option 1 only
grep -n "\\$!>(stash" .kanban/2-in-progress/checkpoint-cli/brief.md
# expect: no match in the Semantics section (the brief may still mention $!> elsewhere
#         when describing the historical primitive used by verify-phase / promote-done)
```
