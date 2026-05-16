---
description: Dispatch one Implementer turn for the next pending phase of a checkpoint-driven feature.
argument-hint: [slug]
---

You are about to dispatch one **implementer** turn for the next pending phase of a feature.

## Slug from the user

$ARGUMENTS

## Pre-flight (run in main conversation, NOT inside the implementer)

Run these steps in order. Stop on first failure and report to the user.

### 1. Resolve slug

```bash
SLUG="$ARGUMENTS"
if [ -z "$SLUG" ]; then
  # If no slug given, infer from the single active feature
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

### 2. Validate spec + auto-heal checkpoint

```bash
python3 scripts/checkpoint/validate-spec.py "$SLUG"
```

Fix any reported problems before continuing. If validation fails, **do not invoke the implementer** — tell the user the spec needs architect attention.

### 3. Check git state

```bash
git status --porcelain
```

If the tree is dirty, that's a red flag. The previous phase might not have committed. **Stop** and tell the user to run `/resume <slug>` first.

### 4. Get the next phase

```bash
python3 scripts/checkpoint/next-phase.py "$SLUG"
```

Capture the YAML output. If exit code is 2:
- "all phases done" → tell the user to run `/feature-review <slug>`.
- "in_progress" → tell the user to run `/resume <slug>`.
- "blocked" → tell the user some prior phase is `failed`; investigate.

If exit code is 0, the YAML is the implementer's brief data. Note the `id`, `title`, `files`, `verify`, `non_goals`, `est_tokens`, `_brief.attempts`.

### 5. Sanity-check phase size

If `est_tokens > 80000`, refuse to dispatch — the architect should split the phase first. Tell the user.

If `_brief.attempts >= 2`, this is at least a third try. Warn the user — perhaps the phase needs to be split, the brief enriched, or there's a deeper bug to address first.

### 6. Mark phase in_progress

```bash
python3 scripts/checkpoint/advance-checkpoint.py "$SLUG" "<phase-id>" in_progress
```

## Dispatch the implementer

Invoke the `implementer` agent via the `Agent` tool with `subagent_type: "implementer"`. The prompt **must** contain, in this order:

1. **Hard scope reminder** — quote the phase's `non_goals` verbatim, and emphasize that only files in `phase.files` may be modified.
2. **Phase brief** — the full YAML from `next-phase.py` (id, title, deps, files, verify, non_goals, notes).
3. **Pointers**:
   - Spec: `.kanban/2-in-progress/<slug>/spec.md` (read sections relevant to this phase)
   - Context: `.kanban/2-in-progress/<slug>/context.md` (read all of it — it's small)
   - Per-phase notes (if exists): `.kanban/2-in-progress/<slug>/notes/<id>.md`
4. **Verification contract** — the implementer must run `bash scripts/checkpoint/verify-phase.sh <slug> <id>` (not just the bare verify commands — the script also enforces scope).
5. **Commit contract** — the exact commit message format from `.claude/agents/implementer.md`. Verify must pass before commit.
6. **Advance contract** — after a green commit:
   ```bash
   python3 scripts/checkpoint/advance-checkpoint.py <slug> <id> done \
       --commit "$(git rev-parse HEAD)" --verified true \
       --notes "<one-line summary>"
   ```
7. **Failure protocol** — if verify cannot be made to pass within scope, run:
   ```bash
   python3 scripts/checkpoint/advance-checkpoint.py <slug> <id> failed \
       --notes "<reason>"
   ```
   and report back without committing.

## After the implementer returns

1. Read the implementer's report.
2. Confirm with `git log -1 --oneline` and `python3 scripts/checkpoint/status.py "$SLUG"` that the state advanced as expected.
3. Tell the user:
   - On success: phase id, commit SHA, the next phase id (or "all phases done → `/feature-review <slug>`")
   - On failure: phase id, failure reason, suggested next action (architect amends plan, or user investigates manually)
