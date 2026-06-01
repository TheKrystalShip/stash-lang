---
description: Final acceptance — run final_verify, promote feature to .kanban/4-done/, archive into repo.md. No agent invocation.
argument-hint: [slug]
---

You are promoting a fully-reviewed feature to `4-done/`. **Do not invoke any agent.** This command is script-only.

## Slug from the user

$ARGUMENTS

## Steps (run in main conversation)

### 1. Resolve slug

```bash
SLUG="$ARGUMENTS"
if [ -z "$SLUG" ]; then
  count=$(ls -1d .kanban/2-in-progress/*/ 2>/dev/null | wc -l)
  if [ "$count" -eq 1 ]; then
    SLUG=$(basename "$(ls -1d .kanban/2-in-progress/*/)")
  else
    echo "error: pass a slug — active: $(ls -1 .kanban/2-in-progress/ 2>/dev/null | tr '\n' ' ')" >&2
    exit 1
  fi
fi
```

### 2. Refuse if tree dirty

```bash
[ -z "$(git status --porcelain)" ] || { echo "tree dirty; commit or stash first" >&2; exit 1; }
```

### 3. Run promotion script

```bash
stash scripts/checkpoint/promote-done.stash "$SLUG"
```

This script will:
- Verify every phase is `done` (refuses otherwise)
- Verify all review findings are fixed (refuses otherwise)
- Run `plan.yaml` `final_verify` commands (must all pass)
- `mv .kanban/2-in-progress/<slug> .kanban/4-done/<slug>`

If any step fails, the directory stays where it is — investigate and try again.

### 4. Archive into `.claude/repo.md`

Prepend an entry to "Recent Completed Work" using the date and slug:

```
- YYYY-MM-DD | <slug> — <title> | <one-line summary> | .kanban/4-done/<slug>/
```

And remove the pointer from "Active Multi-Phase Work" (if present).

### 5. Commit the kanban move + repo.md update

```bash
git add .kanban/4-done/$SLUG .claude/repo.md
git rm -r --cached .kanban/2-in-progress/$SLUG 2>/dev/null || true
git commit -m "$(cat <<MSG
chore($SLUG): promote to 4-done

Feature shipped. All phases verified, review resolved.
MSG
)"
```

### 6. Report to the user

- The new path under `4-done/`
- The commit SHA of the promotion commit
- A short summary: phase count, finding count fixed, total commits

That's it — the feature is shipped.
