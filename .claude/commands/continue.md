---
description: Diagnostic — print checkpoint state, git status, last verify result, and suggest the next command. No agent invocation.
argument-hint: [slug]
---

You are diagnosing the current state of a checkpoint-driven feature. **Do not invoke any agent.** This command is purely informational.

## Slug from the user

$ARGUMENTS

## Steps (run in main conversation, no agent dispatch)

### 1. Resolve slug

```bash
SLUG="$ARGUMENTS"
if [ -z "$SLUG" ]; then
  count=$(ls -1d .kanban/2-in-progress/*/ 2>/dev/null | wc -l)
  if [ "$count" -eq 0 ]; then
    echo "no active feature in .kanban/2-in-progress/"
    exit 0
  elif [ "$count" -eq 1 ]; then
    SLUG=$(basename "$(ls -1d .kanban/2-in-progress/*/)")
  else
    echo "multiple active features:"; ls -1 .kanban/2-in-progress/
    exit 1
  fi
fi
echo "slug: $SLUG"
```

### 2. Print status

```bash
stash scripts/checkpoint/status.stash "$SLUG"
```

### 3. Validate (auto-heals checkpoint if needed)

```bash
stash scripts/checkpoint/validate-spec.stash "$SLUG"
```

### 4. Decide what's next

Read the status output and pick the right next-action recommendation:

| Situation | Recommend |
| --- | --- |
| Working tree clean, `current` is null, some phases still `pending` with deps satisfied | `/next-phase <slug>` or `/next-phase <slug> N` for an explicit small batch |
| Working tree clean, all phases `done`, review `not_started` | `/feature-review <slug>` |
| Working tree clean, review has open findings | `/resolve <slug> <Fxx> [Fyy...]` for the first open finding or an explicit related batch |
| Working tree clean, all phases done, review `resolved` (or no findings) | `/done <slug>` |
| Working tree DIRTY, checkpoint shows phase `in_progress` | Run the phase's verify command directly to see if the prior implementer finished but didn't commit: `bash scripts/checkpoint/verify-phase.sh <slug> <phase-id>`. If green, advise the user to commit and run `advance-checkpoint.py <slug> <phase-id> done`. If red, advise running `/next-phase <slug>` again — it will re-dispatch with the diff visible. |
| Working tree dirty, checkpoint clean | Investigate — the dirty tree is unrelated work. Don't auto-advance. |
| Phase status `failed` | Tell the user the phase failed; suggest either re-dispatching via `/next-phase <slug>` (after manual investigation) or asking the architect to amend the plan. |

Print:

- A 2-3 line summary of state
- The recommended next command
- Any anomalies (out-of-scope edits, dirty tree without active phase, etc.)
