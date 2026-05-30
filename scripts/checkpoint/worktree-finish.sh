#!/usr/bin/env bash
# worktree-finish.sh <slug>
#
# Integrates a finished feature branch back into main and cleans up its worktree
# (the parallel-features workflow — see .claude/WORKFLOW.md "Running Features in
# Parallel"). Encodes the three merge-time rules:
#   1. merge --no-ff           -> feature lands as one labeled boundary commit
#   2. re-run final_verify     -> green-on-branch does NOT imply green-on-merged-main
#   3. clean up only if green  -> worktree + branch removed only after main verifies
#
# MUST be run from the main checkout (you cannot merge into main from inside the
# feature worktree, where main is not the checked-out branch). The feature must
# already be promoted by /done (its dir lives under .kanban/4-done/ on the branch).
set -euo pipefail

if [ $# -ne 1 ]; then
  echo "usage: $0 <slug>" >&2
  exit 2
fi

slug="$1"
branch="feature/$slug"
repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
script_dir="$(cd "$(dirname "$0")" && pwd)"
cd "$repo_root"

main_branch="${MAIN_BRANCH:-main}"
current="$(git branch --show-current)"
if [ "$current" != "$main_branch" ]; then
  echo "error: run from the $main_branch checkout (currently on '$current' in $repo_root)" >&2
  echo "       the feature worktree cannot merge into $main_branch — main is checked out elsewhere" >&2
  exit 1
fi
if [ -n "$(git status --porcelain)" ]; then
  echo "error: working tree is dirty; commit or stash before integrating" >&2
  exit 1
fi
if ! git show-ref --verify --quiet "refs/heads/$branch"; then
  echo "error: branch '$branch' does not exist" >&2
  exit 1
fi
# Feature must be promoted on its branch (/done moved it to 4-done).
if ! git cat-file -e "$branch:.kanban/4-done/$slug/checkpoint.yaml" 2>/dev/null; then
  echo "error: feature '$slug' is not promoted on '$branch'" >&2
  echo "       run /done on the feature branch first (it must reach .kanban/4-done/$slug/)" >&2
  exit 1
fi

# Locate the worktree bound to this branch (robust against custom paths).
worktree_path="$(git worktree list --porcelain | awk -v b="refs/heads/$branch" '
  /^worktree / { wt=$2 }
  $0 == "branch " b { print wt }
')"

echo "+ git merge --no-ff $branch"
if ! git merge --no-ff "$branch" -m "merge: $branch"; then
  echo "error: merge conflict integrating '$branch' into $main_branch" >&2
  echo "       resolve the conflicts (last-to-merge pays the cost), commit, then" >&2
  echo "       re-run this script — it will skip the merge and continue with verify." >&2
  exit 1
fi

# Re-run final_verify against the MERGED main, reading plan.yaml from its
# now-promoted location.
mapfile -t cmds < <(python3 - "$slug" <<'PY'
import sys, pathlib, yaml
slug = sys.argv[1]
root = pathlib.Path.cwd()
plan_path = root / ".kanban" / "4-done" / slug / "plan.yaml"
if not plan_path.is_file():
    print(f"error: {plan_path} not found after merge", file=sys.stderr)
    sys.exit(1)
plan = yaml.safe_load(plan_path.read_text(encoding="utf-8")) or {}
for c in plan.get("final_verify") or []:
    print(c)
PY
)

verify_failed=0
ran_any=0
for cmd in "${cmds[@]}"; do
  [ -z "$cmd" ] && continue
  ran_any=1
  echo "+ $cmd"
  if ! bash -c "$cmd"; then
    verify_failed=1
    break
  fi
done

# Fail closed: an empty list, or a failure inside the <(...) extraction (which
# `set -e` does NOT catch), must NOT be read as "verified". The whole point of
# this script is that re-verifying merged main is unskippable.
if [ "$ran_any" -eq 0 ]; then
  echo "error: no final_verify commands ran — refusing to tear down unverified" >&2
  echo "       check that .kanban/4-done/$slug/plan.yaml has a 'final_verify:' list." >&2
  echo "       The merge is preserved; worktree and branch left in place." >&2
  exit 1
fi

if [ "$verify_failed" -ne 0 ]; then
  echo "error: final_verify FAILED on merged $main_branch — likely a semantic conflict" >&2
  echo "       between this feature and work already on $main_branch." >&2
  echo "       The merge is preserved. Fix forward on $main_branch; do NOT abort." >&2
  echo "       Worktree and branch left in place: $worktree_path  ($branch)" >&2
  exit 1
fi

# Green on merged main — safe to clean up.
if [ -n "$worktree_path" ] && [ -d "$worktree_path" ]; then
  echo "+ git worktree remove $worktree_path"
  git worktree remove "$worktree_path"
fi
echo "+ git branch -d $branch"
git branch -d "$branch"

echo "integrated: $branch -> $main_branch (final_verify green); worktree and branch removed"
