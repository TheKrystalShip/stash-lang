---
description: Dispatch one Reviewer turn over a checkpoint-driven feature. Writes review.md with structured findings.
argument-hint: [slug]
---

You are about to dispatch one **reviewer** turn for a feature whose phases are all done.

## Slug from the user

$ARGUMENTS

## Pre-flight (run in main conversation)

### 1. Resolve slug

```bash
SLUG="$ARGUMENTS"
if [ -z "$SLUG" ]; then
  count=$(ls -1d .kanban/2-in-progress/*/ 2>/dev/null | wc -l)
  if [ "$count" -eq 1 ]; then
    SLUG=$(basename "$(ls -1d .kanban/2-in-progress/*/)")
  else
    echo "error: pass a slug — active features: $(ls -1 .kanban/2-in-progress/ 2>/dev/null | tr '\n' ' ')"
    exit 1
  fi
fi
echo "slug: $SLUG"
```

### 2. Refuse if phases not all done

```bash
stash scripts/checkpoint/promote-gate.stash "$SLUG" --phases-only
```

If this fails, tell the user which phases are pending and to run `/next-phase` for them.

### 3. Compute the diff range

```bash
stash scripts/checkpoint/feature-diff-range.stash "$SLUG"
```

This is the **single source of truth** for the feature boundary — it prints `base`/`head`/`range`,
the feature commit list, and the diff stat. BASE is the parent of the first feature-tagged
(`<type>($SLUG)`) commit on `main..HEAD`; it deliberately does **not** use `git merge-base HEAD
origin/main` (origin/main lags local main and silently widens the base, polluting the diff with
unrelated files — this is the computation that mis-resolved by hand more than once). Capture just
the range for the reviewer prompt with:

```bash
RANGE=$(stash scripts/checkpoint/feature-diff-range.stash "$SLUG" --range)
```

If it **warns** that no commits matched `($SLUG)`, the phases may not have been committed (or used a
different prefix) — investigate before dispatching. **Sanity-check the diff stat**: if it lists
files far outside the feature's `scope:` globs, the base resolved wrong — investigate before handing
the range to the reviewer.

### 4. Run the full test suite once as a baseline

```bash
stash scripts/checkpoint/run-verify.stash "dotnet test" --name baseline
```

`run-verify` streams the run live and prints a structured, fail-closed verdict (`PASS`/`FAIL`, failed/passed/skipped counts, and any failing-test names) instead of leaving you to eyeball `tail`. **For a cold build, invoke it under the Bash tool's `run_in_background` and poll** — it streams a *warm* build but cannot stream a silent cold build, so it does **not** by itself eliminate the stream-idle timeout. The suite is green — a `FAIL` verdict is a real regression, not noise to excuse.

## Dispatch the reviewer

Invoke the `reviewer` agent via the `Agent` tool with `subagent_type: "reviewer"`. The prompt **must** contain:

1. **Slug** and feature dir: `.kanban/2-in-progress/<slug>/`
2. **Brief path:** `.kanban/2-in-progress/<slug>/brief.md` — must be read fully. If this is an older feature with only `spec.md`, pass that instead.
3. **Plan path:** `.kanban/2-in-progress/<slug>/plan.yaml` — phase scope and `done_when`
4. **Diff range:** the `range` from Step 3 (`feature-diff-range.stash "$SLUG" --range`) — paste the actual `BASE..HEAD` SHAs
5. **Phase commits:** the feature commit list from Step 3's report
6. **Baseline test summary:** test pass/fail counts before review begins
7. **Review template:** `.kanban/_templates/review-template.md` — copy this to `.kanban/2-in-progress/<slug>/review.md`
8. **Hard rules** (reiterate):
   - Do NOT fix any issues. Findings only.
   - Do NOT move the feature directory.
   - Do NOT touch source files (only `review.md`, `repo.md`, possibly a new `.kanban/0-backlog/` stub for out-of-scope bugs).
   - Findings format is STRICT: `## Fxx — [SEVERITY] <title>` with the fields documented in the template.
9. **Update checkpoint** when done:
   ```bash
   stash scripts/checkpoint/advance-checkpoint.stash <slug> - --review-status <in_progress|resolved>
   ```
   `resolved` only if zero findings; otherwise `in_progress`.

## After the reviewer returns

1. List the findings with the shared parser — `stash scripts/checkpoint/review-findings.stash "$SLUG"` (id, severity, status, title; the same parse `/resolve` and the promotion gate use). Add `--json` if you want the structured form.
2. Tell the user:
   - Counts by severity
   - Next action:
     - If 0 findings: `/done <slug>`
     - Else: `/resolve <slug> F01` (or an explicit small related batch such as `/resolve <slug> F01 F02`)
