---
description: Dispatch one Resolver turn to fix exactly one review finding.
argument-hint: <slug> <Fxx>
---

You are about to dispatch one **resolver** turn to fix exactly one finding from `review.md`.

## Args from the user

$ARGUMENTS

## Pre-flight (run in main conversation)

### 1. Parse args

```bash
read -r SLUG FID <<< "$ARGUMENTS"
if [ -z "$SLUG" ] || [ -z "$FID" ]; then
  # If only one arg, maybe user passed only the finding id and there's one active feature
  if [ -z "$FID" ] && [ -n "$SLUG" ]; then
    count=$(ls -1d .kanban/2-in-progress/*/ 2>/dev/null | wc -l)
    if [ "$count" -eq 1 ]; then
      FID="$SLUG"
      SLUG=$(basename "$(ls -1d .kanban/2-in-progress/*/)")
    fi
  fi
  if [ -z "$SLUG" ] || [ -z "$FID" ]; then
    echo "usage: /resolve <slug> <Fxx>" >&2
    exit 1
  fi
fi
echo "slug: $SLUG  finding: $FID"
```

### 2. Refuse if tree is dirty

```bash
[ -z "$(git status --porcelain)" ] || { echo "tree dirty; commit or stash first" >&2; exit 1; }
```

### 3. Extract the finding section verbatim

The review template uses `## Fxx — [SEVERITY] title` as the section header. Extract exactly that block from `review.md`:

```bash
REVIEW=".kanban/2-in-progress/$SLUG/review.md"
[ -f "$REVIEW" ] || { echo "no review.md found" >&2; exit 1; }
awk -v fid="^## $FID " '
  $0 ~ fid {in_section=1; print; next}
  in_section && /^## F/ {exit}
  in_section {print}
' "$REVIEW" > /tmp/finding-$$.md
if [ ! -s /tmp/finding-$$.md ]; then
  echo "finding $FID not found in $REVIEW" >&2
  exit 1
fi
cat /tmp/finding-$$.md
```

### 4. Check status

If the extracted section contains `**Status:** fixed`, refuse — tell the user that finding is already resolved.

## Dispatch the resolver

Invoke the `resolver` agent via the `Agent` tool with `subagent_type: "resolver"`. The prompt **must** contain:

1. **Slug:** `$SLUG`
2. **Finding id:** `$FID`
3. **Finding section verbatim** — paste the full text extracted above
4. **Pointers:**
   - Spec: `.kanban/2-in-progress/<slug>/spec.md` (read only if the finding cites a spec requirement)
   - Plan: `.kanban/2-in-progress/<slug>/plan.yaml`
   - Review file: `.kanban/2-in-progress/<slug>/review.md`
5. **Hard rules** (reiterate):
   - Fix only the named files. If you must touch others, stop and report.
   - Run the finding's `Verify` command before commit.
   - Commit message: `fix(<slug>): <Fxx> — <short title>` with body referencing `review.md`.
   - Update `review.md`: change `**Status:** open` → `**Status:** fixed`, append `**Fixed in:** <sha>` below.
6. **Failure protocol** — if the suggested fix doesn't work as written, stop and report; do not improvise scope.
7. **Checkpoint advance after success:**
   ```bash
   python3 scripts/checkpoint/advance-checkpoint.py <slug> - --review-status in_progress
   ```

## After the resolver returns

1. Verify the commit landed: `git log -1 --oneline`.
2. Re-grep `review.md` for remaining open findings: `grep -B1 "Status:\*\* open" .kanban/2-in-progress/$SLUG/review.md`.
3. If there are still open findings, suggest the next one: `/resolve <slug> Fxx`.
4. If all findings are fixed, update the checkpoint and tell the user `/done <slug>` is ready:
   ```bash
   python3 scripts/checkpoint/advance-checkpoint.py <slug> - --review-status resolved
   ```
