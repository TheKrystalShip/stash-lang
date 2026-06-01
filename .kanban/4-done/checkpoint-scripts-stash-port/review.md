# checkpoint-scripts-stash-port — Review

> Produced by `/feature-review`. One finding per H2 section.

**Scope reviewed:** commits `c3f2f8f..0205738c` on branch `main`
**Brief:** ../brief.md
**Generated:** 2026-06-01

**Verification run:** `stash scripts/checkpoint/difftest_runner.stash --all` → 56/56 pass.
`lint_common_imports.stash` (normal + --self-test) → OK with teeth.
`difftest_coverage_check.stash` (normal + --self-test) → OK (but see F02 re: teeth).
`grep -rn "python3 scripts/checkpoint/" .claude/commands/ .claude/agents/` → 0 matches.

The feature is in good shape — TIGHT contracts hold on every ported script, no
C# source was touched, oracle teeth are real on the comparator axes that matter,
and the slash-command cutover is grep-clean. Findings below are minor cleanups
and one teeth-weakness in the Detect guard.

---

## F01 — [MEDIUM] Stale `.py` references in slash-command/agent prose survived P9 cutover

**Status:** fixed (54d25d2)
**Files:** `.claude/commands/continue.md:55`, `.claude/commands/next-phase.md:115`, `.claude/agents/implementer.md:13`
**Phase:** P9
**Commit:** 22c7e917

### Observation

P9 flipped every `python3 scripts/checkpoint/<x>.py` invocation in
`.claude/commands/` and `.claude/agents/` to `stash …stash`, and the grep gate
(`! grep -rn "python3 scripts/checkpoint/" …`) passes. But three prose mentions
of the now-stale `.py` filenames survived because they don't carry the
`python3 scripts/checkpoint/` prefix:

- `.claude/commands/continue.md:55` — operator guidance:
  > advise the user to commit and run `advance-checkpoint.py <slug> <phase-id> done`

  This is **executable advice**, not narration. Post-cutover this command is
  still present (the `.py` originals are intentionally retained), but the
  documented invocation contradicts the new convention and skips the
  newly-asserted `stash`-on-`$PATH` entry-point.

- `.claude/commands/next-phase.md:115` — describes the phase brief as "the full
  YAML from `next-phase.py`". The producer is now `next-phase.stash`.

- `.claude/agents/implementer.md:13` — `The phase YAML from next-phase.py`,
  same issue.

### Why this matters

The P9 done_when says "Every `python3 scripts/checkpoint/<x>.py` invocation …
is rewritten to `stash scripts/checkpoint/<x>.stash` (same argv shape
preserved)" — strictly satisfied by the grep. But the brief frames P9 as the
*final dispatch flip*: callers, including operator advice strings inside the
slash-command docs, should reference the new entry points. `continue.md:55`
in particular tells a human user to run a Python script that has been
deprecated as the workflow's preferred path. After the `.py` follow-up
retirement feature (already named in the brief's Non-Goals: "checkpoint
scripts: retire Python originals"), this advice silently breaks.

### Suggested fix

Replace:

- `continue.md:55`:
  `advance-checkpoint.py <slug> <phase-id> done` →
  `stash scripts/checkpoint/advance-checkpoint.stash <slug> <phase-id> done`

- `next-phase.md:115`:
  `the full YAML from next-phase.py` →
  `the full YAML from next-phase.stash`

- `implementer.md:13`:
  `The phase YAML from next-phase.py` →
  `The phase YAML from next-phase.stash`

Prose-only changes; no behaviour change.

### Verify

```
! grep -rn "advance-checkpoint\.py\|next-phase\.py\|status\.py\|validate-spec\.py\|milestone-status\.py\|check-parallel-safety\.py" .claude/commands/ .claude/agents/
```

---

## F02 — [LOW] `difftest_coverage_check.stash --self-test` is vacuous — does not exercise the scan loop

**Status:** fixed (54d25d2)
**Files:** `scripts/checkpoint/difftest_coverage_check.stash:97-116`
**Phase:** P2
**Commit:** 1e4d6833

### Observation

The Detect guard's self-test reads in full:

```
let fake_name = "fake_ported_script_selftest_zz";
let violation = check_coverage(fake_name, SDIR);
if (violation == null) { … FAIL …; env.exit(1); }
io.println("PASS self-test: …"); env.exit(0);
```

This calls the leaf predicate `check_coverage()` directly against a
known-non-existent table name. It proves only that
`check_coverage(name_with_no_table)` returns a non-null string — which is
trivially true by inspection of the function (3 lines: allow-list check,
`fs.exists()` check, return string).

It does **not** exercise:

- the normal-mode `fs.glob("scripts/checkpoint/*.stash")` scan loop,
- the `is_excluded()` filter,
- the `is_case_table()` prefix/suffix filter,
- the `.stash` extension strip (`str.substring(base, 0, base.length - 6)`),
- the accumulate-and-fail aggregation.

A regression where (e.g.) `is_case_table()` accidentally returned `true` for
every basename would make the normal scan report
`"0 script(s) checked, 0 missing case tables"` — vacuously green — while
`--self-test` still passes because it bypasses the loop entirely.

The brief explicitly contracted: "`difftest_coverage_check.stash` ships a
fail-path self-test asserting it trips on a fixture stripped of its case
table" (Cross-Cutting Concerns table, Detect row; reiterated in P2's
`done_when`).

### Why this matters

This is the Detect-half of the Construct + Detect doctrine for the differential
oracle coverage concern. Construct (the runner registry + per-phase
`done_when`) is the strong guard; Detect is the backstop for a developer who
adds a new `.stash` script and forgets to register it. If Detect drifts to
vacuous, the backstop is gone and the failure mode is silent — exactly what
the doctrine is designed to prevent.

(`lint_common_imports.stash --self-test`, by contrast, calls `lint_file()`
against a real fixture file whose contents the prod-mode loop would also
read; the difference is illustrative.)

### Suggested fix

Make the self-test exercise the loop end-to-end via a fixture directory:

1. Create `scripts/checkpoint/difftest_coverage_check.fixtures/script_no_table.stash`
   (an empty placeholder; content irrelevant — its mere existence is the trip).
2. In `--self-test` mode, swap `SDIR` to the fixture dir and run the same
   `fs.glob(SDIR + "/*.stash")` loop. Assert the loop reports a violation for
   `script_no_table.stash`. The fixture file lives in a subdir so the normal
   scan (`scripts/checkpoint/*.stash`) does not see it.

This mirrors the lint self-test pattern and gives the Detect guard real teeth
against scan-loop regressions.

### Verify

```
stash scripts/checkpoint/difftest_coverage_check.stash --self-test    # still PASS
# Negative control: stub is_case_table() to always return true; self-test should now FAIL.
```

---

## F03 — [LOW] Stale comment in `lint_common_imports.stash` claims `difftest_coverage_check.stash` is in SCAN_EXCLUSIONS

**Status:** fixed (54d25d2)
**Files:** `scripts/checkpoint/lint_common_imports.stash:33-37`
**Phase:** P1
**Commit:** cb44f3ef

### Observation

The comment above `SCAN_EXCLUSIONS` (lines 28-32) enumerates:

> `difftest_coverage_check.stash — coverage guard; imports from _common`

…but the `SCAN_EXCLUSIONS` array itself contains only three entries:
`_common.stash`, `difftest_runner.stash`, `lint_common_imports.stash`.
`difftest_coverage_check.stash` is **not excluded** — it is scanned and passes
the rule because it does `import { expand_globs } from "_common.stash"` (line 21
of that file). The comment is documentation drift, not a defect, but it
misleads a reader auditing the exclusion list.

### Why this matters

The exclusion set is a bounded domain — single source of truth (the named
const) is what the project convention requires. A comment that disagrees with
the const it documents is the same kind of drift bug the convention exists to
prevent. Cheap to fix; cheap-to-overlook is the reason to fix it.

### Suggested fix

Drop the misleading bullet from the comment (lines 32):
> `//   difftest_coverage_check.stash — coverage guard; imports from _common`

The remaining bullets accurately describe `_common.stash`,
`difftest_runner.stash`, and `lint_common_imports.stash`.

### Verify

```
stash scripts/checkpoint/lint_common_imports.stash             # still OK
stash scripts/checkpoint/lint_common_imports.stash --self-test # still PASS
```

---

## F04 — [INFO] `difftest_status_cases.stash` is a parity smoke-set with no observability beyond exit code

**Status:** acknowledged (INFO — no fix required; by-design per brief TIGHT/LOOSE matrix)
**Files:** `scripts/checkpoint/difftest_status_cases.stash`
**Phase:** P6
**Commit:** 03a1d218

### Observation

All three `status` cases declare only `argv` + `seed_cp` (and the implicit
`file_mutation: false`). `status` always exits 0, so the oracle reduces to
`0 == 0` for every case. The brief's TIGHT/LOOSE matrix declares this is
the intended contract (`status`: exit TIGHT, stdout LOOSE, no file mutation,
stderr LOOSE), so this is **by design** and the P6 `done_when` is satisfied.

The consequence: a regression that emptied `status.stash`'s stdout entirely,
or stopped printing the phase table, or printed garbage — would not be
caught by `difftest_runner.stash status`. Only an exit-code change would
trip the oracle.

### Why this matters

Informational only. The brief explicitly accepted LOOSE-only verification
for human-only output and documented the consequence ("stdout is reported
but does not gate pass/fail"). Flagging here so a future reader doesn't
assume the oracle covers status semantics. If a future change wants
stronger verification, adding `stdout_must_contain` phrases for the table
header (`"Phase"`, `"Status"`, `"current phase:"`) would be a 5-line tighten
without escalating to TIGHT.

### Suggested fix

No action required — INFO. If desired, append `stdout_must_contain:
["Phase", "Status", "current phase:"]` to each case to ground the oracle on
the stable header strings. Same applies to `milestone-status` cases that
don't already declare `stdout_must_contain`.

### Verify

N/A (no fix proposed).
