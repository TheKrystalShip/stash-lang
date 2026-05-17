---
description: Dispatch one Implementer turn for the next pending phase(s) of a checkpoint-driven feature.
argument-hint: [slug] [count]
---

You are about to dispatch one **implementer** turn for the next pending phase or explicit phase batch of a feature.

Default batch size is 1. The user may pass a count, e.g. `/next-phase readable-disassembly 3`, to let one implementer turn complete up to that many ready phases.

## Slug from the user

$ARGUMENTS

## Pre-flight (run in main conversation, NOT inside the implementer)

Run these steps in order. Stop on first failure and report to the user.

### 1. Resolve slug and batch size

```bash
set -- $ARGUMENTS
SLUG=""
COUNT=1

if [ "$#" -eq 1 ]; then
  if [[ "$1" =~ ^[0-9]+$ ]]; then
    COUNT="$1"
  else
    SLUG="$1"
  fi
elif [ "$#" -ge 2 ]; then
  SLUG="$1"
  COUNT="$2"
fi

if ! [[ "$COUNT" =~ ^[0-9]+$ ]] || [ "$COUNT" -lt 1 ]; then
  echo "error: count must be a positive integer" >&2
  exit 1
fi
if [ "$COUNT" -gt 5 ]; then
  echo "error: refusing phase batch > 5; split it into smaller batches" >&2
  exit 1
fi
if [ "$COUNT" -ge 4 ]; then
  echo "warning: batching $COUNT phases; keep this for small, linear, well-scoped phases" >&2
fi

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
echo "slug: $SLUG  count: $COUNT"
```

### 2. Validate plan + auto-heal checkpoint

```bash
python3 scripts/checkpoint/validate-spec.py "$SLUG"
```

Fix any reported problems before continuing. If validation fails, **do not invoke the implementer** — tell the user the brief/plan needs architect attention.

### 3. Check git state

```bash
git status --porcelain
```

If the tree is dirty, that's a red flag. The previous phase might not have committed. **Stop** and tell the user to run `/resume <slug>` first.

### 4. Get the next phase or phase batch

```bash
python3 scripts/checkpoint/next-phase.py "$SLUG" "$COUNT"
```

Capture the YAML output. If exit code is 2:
- "all phases done" → tell the user to run `/feature-review <slug>`.
- "in_progress" → tell the user to run `/resume <slug>`.
- "blocked" → tell the user some prior phase is `failed`; investigate.

If exit code is 0, the YAML is the implementer's brief data.

For count 1, note the single phase's `id`, `title`, `files`, `verify`, `done_when`, `_brief.brief_path`, and `_brief.attempts`.

For count > 1, note `phase_ids`, `selected_count`, and each item under `phases`. If `selected_count` is less than requested count, continue with the smaller ready batch.

### 5. Sanity-check phase shape

For every selected phase:

- If `done_when` is missing or empty, refuse to dispatch — the architect should state the observable behavior that proves the phase.
- If `est_tokens > 80000`, refuse to dispatch — the architect should split the phase first. `est_tokens` is optional in the simplified workflow; only check it when present.
- If `_brief.attempts >= 2`, warn the user — perhaps the phase needs to be split, the brief enriched, or there's a deeper bug to address first.

### 6. Mark first phase in_progress

Only mark the first selected phase before dispatch. For a batch, the implementer marks each later phase `in_progress` immediately before starting it, then verifies, commits, and advances it before moving on.

```bash
python3 scripts/checkpoint/advance-checkpoint.py "$SLUG" "<first-phase-id>" in_progress
```

## Dispatch the implementer

Invoke the `implementer` agent via the `Agent` tool with `subagent_type: "implementer"`. The prompt **must** contain, in this order:

1. **Plan trust reminder** — tell the implementer to trust the plan as the default route, but allow small documented corrections when a file path, symbol location, signature, or verify command is stale. If `non_goals` exists on any selected phase, quote it verbatim.
2. **Phase brief** — the full YAML from `next-phase.py`. For batches, preserve the `phases` order exactly.
3. **Pointers**:
   - Brief: `.kanban/2-in-progress/<slug>/brief.md` (read summary, design path, acceptance criteria, and sections relevant to this phase)
   - Legacy spec/context paths if `_brief` reports them for an older feature
4. **Batch contract** — the implementer must process selected phases sequentially. For each phase: mark it `in_progress` if it is not already, implement only that phase's intent, run `verify-phase.sh`, commit, advance it to `done`, then continue to the next selected phase.
5. **Verification contract** — for each phase, the implementer must run `bash scripts/checkpoint/verify-phase.sh <slug> <id>` (not just the bare verify commands — the script also enforces scope).
6. **Commit contract** — one commit per phase, using the exact commit message format from `.claude/agents/implementer.md`. Verify must pass before each commit.
7. **Advance contract** — after each green phase commit:
   ```bash
   python3 scripts/checkpoint/advance-checkpoint.py <slug> <id> done \
       --commit "$(git rev-parse HEAD)" --verified true \
       --notes "<one-line summary>"
   ```
8. **Failure protocol** — if a selected phase cannot be made to pass after bounded plan corrections, run:
   ```bash
   python3 scripts/checkpoint/advance-checkpoint.py <slug> <id> failed \
       --notes "<reason>"
   ```
   and stop the batch. Do not start later selected phases after a failure.

## After the implementer returns

1. Read the implementer's report.
2. Confirm with `git log -1 --oneline` and `python3 scripts/checkpoint/status.py "$SLUG"` that the state advanced as expected.
3. Tell the user:
   - On success: completed phase ids, commit SHAs, the next phase id (or "all phases done → `/feature-review <slug>`")
   - On failure: failed phase id, any completed phase ids, failure reason, suggested next action
