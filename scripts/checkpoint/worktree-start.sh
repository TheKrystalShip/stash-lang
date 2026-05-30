#!/usr/bin/env bash
# worktree-start.sh <slug> [base-branch]
#
# Creates a sibling git worktree on a fresh feature/<slug> branch so a feature
# can progress in isolation from main (the parallel-features workflow — see
# .claude/WORKFLOW.md "Running Features in Parallel").
#
# Naming is the single source of truth here so every agent does it identically:
#   worktree path = <parent-of-repo>/stash-<slug>
#   branch        = feature/<slug>
# The new branch is based on the committed <base-branch> ref (default: main),
# so a dirty primary working tree does NOT leak into the new worktree.
#
# Run from the main checkout. The whole feature lifecycle (including /spec) then
# happens inside the new worktree.
set -euo pipefail

if [ $# -lt 1 ]; then
  echo "usage: $0 <slug> [base-branch]" >&2
  exit 2
fi

slug="$1"
base="${2:-main}"

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
parent_dir="$(dirname "$repo_root")"
worktree_path="$parent_dir/stash-$slug"
branch="feature/$slug"

cd "$repo_root"

if ! git rev-parse --git-dir >/dev/null 2>&1; then
  echo "error: not inside a git repository" >&2
  exit 1
fi
if ! git show-ref --verify --quiet "refs/heads/$base"; then
  echo "error: base branch '$base' does not exist" >&2
  exit 1
fi
if git show-ref --verify --quiet "refs/heads/$branch"; then
  echo "error: branch '$branch' already exists" >&2
  exit 1
fi
if [ -e "$worktree_path" ]; then
  echo "error: worktree path already exists: $worktree_path" >&2
  exit 1
fi

# Non-blocking heads-up: what subsystems are already in flight in sibling
# worktrees? The real per-feature overlap check runs post-spec via
# check-parallel-safety.py (this feature has no plan.yaml yet).
others="$(git worktree list --porcelain | awk '/^branch refs\/heads\/feature\// {sub("branch refs/heads/","",$0); print "  - "$0}')"
if [ -n "$others" ]; then
  echo "in-flight feature worktrees (consider subsystem overlap):" >&2
  echo "$others" >&2
fi

git worktree add -b "$branch" "$worktree_path" "$base"

echo "created worktree: $worktree_path  (branch $branch off $base)"
echo "next: cd \"$worktree_path\" && run /spec <topic>"
echo "then: python3 scripts/checkpoint/check-parallel-safety.py $slug   # after spec, before implementing"
echo "done: from the main checkout run  bash scripts/checkpoint/worktree-finish.sh $slug"
