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
git fetch origin main 2>/dev/null || true
BASE=$(git merge-base HEAD origin/main 2>/dev/null || git merge-base HEAD main)
echo "base: $BASE"
echo "head: $(git rev-parse HEAD)"
git log --oneline "$BASE"..HEAD --grep="feat($SLUG)" --grep="fix($SLUG)"
git diff --stat "$BASE"..HEAD
```

If no commits match the feature grep, the user may not have committed phases (or used a different prefix). Investigate before dispatching.

### 4. Run the full test suite once as a baseline

```bash
dotnet test 2>&1 | tail -50
```

Note any pre-existing flakies (cross-reference `.claude/repo.md` Known Issues).

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

1. Read `review.md` (or at least the headers — `grep "^## F" .kanban/2-in-progress/$SLUG/review.md`).
2. Tell the user:
   - Counts by severity
   - Next action:
     - If 0 findings: `/done <slug>`
     - Else: `/resolve <slug> F01` (or an explicit small related batch such as `/resolve <slug> F01 F02`)
