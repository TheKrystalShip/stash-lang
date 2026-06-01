---
description: Dispatch one Resolver turn to fix exactly the selected review finding(s).
argument-hint: [slug] <Fxx> [Fyy...]
---

You are about to dispatch one **resolver** turn to fix the selected finding(s) from `review.md`.

The user chooses the batch size. One finding is normal; several related or small findings are allowed. The resolver must fix **only** the selected findings.

## Args from the user

$ARGUMENTS

## Pre-flight (run in main conversation)

### 1. Parse args

```bash
set -- $ARGUMENTS
if [ "$#" -lt 1 ]; then
  echo "usage: /resolve [slug] <Fxx> [Fyy...]" >&2
  exit 1
fi

first="$1"
shift

if [[ "$first" =~ ^F[0-9][0-9]$ ]]; then
  count=$(ls -1d .kanban/2-in-progress/*/ 2>/dev/null | wc -l)
  if [ "$count" -ne 1 ]; then
    echo "usage: /resolve <slug> <Fxx> [Fyy...] — slug is required when active feature count is not 1" >&2
    exit 1
  fi
  SLUG=$(basename "$(ls -1d .kanban/2-in-progress/*/)")
  FIDS=("$first" "$@")
else
  SLUG="$first"
  FIDS=("$@")
fi

if [ "${#FIDS[@]}" -lt 1 ]; then
  echo "usage: /resolve [slug] <Fxx> [Fyy...]" >&2
  exit 1
fi

for fid in "${FIDS[@]}"; do
  if ! [[ "$fid" =~ ^F[0-9][0-9]$ ]]; then
    echo "invalid finding id: $fid" >&2
    exit 1
  fi
done

printf 'slug: %s  findings:' "$SLUG"
printf ' %s' "${FIDS[@]}"
printf '\n'
```

### 2. Batch-size sanity check

```bash
if [ "${#FIDS[@]}" -gt 5 ]; then
  echo "refusing batch of ${#FIDS[@]} findings; split it into smaller explicit batches" >&2
  exit 1
fi
if [ "${#FIDS[@]}" -ge 4 ]; then
  echo "warning: resolving ${#FIDS[@]} findings in one batch; keep this for related or small fixes" >&2
fi
```

### 3. Refuse if tree is dirty

```bash
[ -z "$(git status --porcelain)" ] || { echo "tree dirty; commit or stash first" >&2; exit 1; }
```

### 4. Extract selected finding sections verbatim

The review template uses `## Fxx — [SEVERITY] title` as the section header. Extract exactly the selected blocks from `review.md`:

```bash
REVIEW=".kanban/2-in-progress/$SLUG/review.md"
[ -f "$REVIEW" ] || { echo "no review.md found" >&2; exit 1; }
OUT="/tmp/findings-$$.md"
: > "$OUT"

for fid in "${FIDS[@]}"; do
  tmp="/tmp/finding-$fid-$$.md"
  awk -v fid="^## ${fid} " '
    $0 ~ fid {in_section=1; print; next}
    in_section && /^## F/ {exit}
    in_section {print}
  ' "$REVIEW" > "$tmp"
  if [ ! -s "$tmp" ]; then
    echo "finding $fid not found in $REVIEW" >&2
    exit 1
  fi
  if grep -q '\*\*Status:\*\* fixed' "$tmp"; then
    echo "finding $fid is already fixed" >&2
    exit 1
  fi
  cat "$tmp" >> "$OUT"
  printf '\n---\n\n' >> "$OUT"
done

cat "$OUT"
```

## Dispatch the resolver

Invoke the `resolver` agent via the `Agent` tool with `subagent_type: "resolver"`. The prompt **must** contain:

1. **Slug:** `$SLUG`
2. **Selected finding ids:** all IDs in `${FIDS[@]}`
3. **Selected finding sections verbatim** — paste the full text extracted above
4. **Pointers:**
   - Brief: `.kanban/2-in-progress/<slug>/brief.md` (read only if a selected finding cites a requirement; for older features, use `spec.md`)
   - Plan: `.kanban/2-in-progress/<slug>/plan.yaml`
   - Review file: `.kanban/2-in-progress/<slug>/review.md`
5. **Hard rules** (reiterate):
   - Fix exactly the selected finding(s), and no unselected findings.
   - If selected findings conflict or are not coherent as a batch, stop and report.
   - Run the union of all selected findings' `Verify` commands before commit.
   - Commit message for one finding: `fix(<slug>): <Fxx> — <short title>`.
   - Commit message for multiple findings: `fix(<slug>): resolve Fxx Fyy Fzz`.
   - Update each selected finding in `review.md`: change `**Status:** open` → `**Status:** fixed`, append `**Fixed in:** <sha>` below.
6. **Failure protocol** — if the selected batch cannot be resolved cleanly, stop before committing and report what should be split or corrected.
7. **Checkpoint advance after success:**
   ```bash
   stash scripts/checkpoint/advance-checkpoint.stash <slug> - --review-status in_progress
   ```

## After the resolver returns

1. Verify the commit landed: `git log -1 --oneline`.
2. List remaining open findings with the shared parser — the SAME one the promotion gate uses, so the loop and the gate can never disagree about what is still open:
   ```bash
   stash scripts/checkpoint/review-findings.stash "$SLUG" --open
   ```
3. If there are still open findings, suggest the next explicit batch: `/resolve <slug> Fxx [Fyy...]`.
4. If all findings are fixed, update the checkpoint and tell the user `/done <slug>` is ready:
   ```bash
   stash scripts/checkpoint/advance-checkpoint.stash <slug> - --review-status resolved
   ```
