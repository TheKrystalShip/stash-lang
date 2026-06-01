#!/usr/bin/env bash
# promote-done.sh <slug>
#
# Final acceptance: runs plan.yaml `final_verify`, then moves the feature
# directory from .kanban/2-in-progress/ to .kanban/4-done/.
# Called by the `/done` slash command after the reviewer is satisfied.
set -euo pipefail

if [ $# -ne 1 ]; then
  echo "usage: $0 <slug>" >&2
  exit 2
fi

slug="$1"
repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
src="$repo_root/.kanban/2-in-progress/$slug"
dst="$repo_root/.kanban/4-done/$slug"

if [ ! -d "$src" ]; then
  echo "error: $src not found" >&2
  exit 1
fi
if [ -e "$dst" ]; then
  echo "error: $dst already exists" >&2
  exit 1
fi

cd "$repo_root"

# Refuse to promote if any phase is not done or any review finding open.
# The phase gate + review.md open-finding gate (with drift note) live in
# promote-gate.stash; it exits non-zero with an explanatory stderr message.
stash scripts/checkpoint/promote-gate.stash "$slug"

# Run final_verify commands from plan.yaml (one command per stdout line).
mapfile -t cmds < <(stash scripts/checkpoint/emit-final-verify.stash "$slug")

for cmd in "${cmds[@]}"; do
  [ -z "$cmd" ] && continue
  echo "+ $cmd"
  bash -c "$cmd"
done

mv "$src" "$dst"
echo "promoted: $src -> $dst"
echo "remember to: prepend a one-line entry to .claude/repo.md under 'Recent Completed Work'"
