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
# Feature boundary = the PARENT OF THE FIRST FEATURE COMMIT on this branch.
# Do NOT use `git merge-base HEAD origin/main`: origin/main is frequently stale
# (it lags local main), which silently widens the base and pollutes the review
# diff with hundreds of unrelated files. This computation is robust against both
# a stale origin/main AND a local main that has advanced (merge-base would still
# be correct there, but parent-of-first-commit is tighter and model-agnostic).
#
# Feature commits are tagged "<type>($SLUG)" — feat/fix/test/docs/chore — so match
# the slug, not just feat(). --reverse|head -1 = the oldest such commit (P1).
FIRST=$(git log --reverse --format=%H --grep="($SLUG)" main..HEAD 2>/dev/null | head -1)
if [ -n "$FIRST" ]; then
  BASE=$(git rev-parse "$FIRST^")
else
  # Fallback (e.g. --here mode, or unconventional commit messages): the local
  # fork point. Prefer LOCAL main over origin/main — origin can be stale.
  BASE=$(git merge-base HEAD main 2>/dev/null || git merge-base HEAD origin/main)
fi
echo "base: $BASE   (parent of first $SLUG commit)"
echo "head: $(git rev-parse HEAD)"
git log --oneline "$BASE"..HEAD --grep="($SLUG)"
echo "--- diff stat (should be ONLY this feature's files) ---"
git diff --stat "$BASE"..HEAD | tail -40
```

If no commits match the feature grep, the user may not have committed phases (or used a
different prefix) — investigate before dispatching. **Sanity-check the diff stat**: if it lists
files far outside the feature's `scope:` globs (hundreds of unrelated files), the base resolved
wrong — recompute from the first feature commit's parent before handing the range to the reviewer.

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
4. **Diff range:** `BASE..HEAD` (paste the actual SHAs)
5. **Phase commits:** the `git log --grep` output
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
