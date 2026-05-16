#!/usr/bin/env bash
# verify-phase.sh <slug> <phase-id>
#
# Runs a phase's verify commands AND checks that the working tree's modified
# files (vs HEAD or vs the merge-base with main, whichever has fewer commits)
# stay within the phase's declared `files` glob list.
#
# Exit codes:
#   0  verification passed
#   1  a verify command failed
#   2  out-of-scope file modification detected
#   3  bad arguments / setup
set -euo pipefail

if [ $# -ne 2 ]; then
  echo "usage: $0 <slug> <phase-id>" >&2
  exit 3
fi

slug="$1"
phase_id="$2"
repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
script_dir="$(cd "$(dirname "$0")" && pwd)"

cd "$repo_root"

# Pull phase metadata as JSON via Python so we can read it cleanly in bash.
phase_json="$(python3 - <<PY
import json, sys
sys.path.insert(0, "$script_dir")
from _common import load_plan, phase_by_id  # type: ignore
plan = load_plan("$slug")
ph = phase_by_id(plan, "$phase_id")
print(json.dumps({
    "files": ph.get("files") or [],
    "verify": ph.get("verify") or [],
    "default_verify": plan.get("default_verify") or [],
    "scope": plan.get("scope") or [],
}))
PY
)"

mapfile -t allowed < <(jq -r '.files[]' <<<"$phase_json")
mapfile -t scope   < <(jq -r '.scope[]' <<<"$phase_json")

# --- Scope check ---
# Compare working tree against last commit. Uncommitted edits are the implementer's
# pending work; we want to flag any out-of-scope edits before they're committed.
changed="$(git diff --name-only HEAD 2>/dev/null; git ls-files --others --exclude-standard 2>/dev/null)"
out_of_scope=()
while IFS= read -r f; do
  [ -z "$f" ] && continue
  matched=0
  for pat in "${allowed[@]}" "${scope[@]}"; do
    # Use bash extglob-aware matching via case + globstar
    shopt -s globstar nullglob extglob
    # shellcheck disable=SC2053
    if [[ "$f" == $pat ]]; then matched=1; break; fi
    # Also allow when the pattern expands to include the file (slower path)
    for expanded in $pat; do
      if [ "$f" = "$expanded" ]; then matched=1; break 2; fi
    done
  done
  if [ "$matched" -eq 0 ]; then
    # Always allow the feature's own kanban dir (checkpoint/notes get touched here).
    if [[ "$f" != .kanban/2-in-progress/$slug/* ]]; then
      out_of_scope+=("$f")
    fi
  fi
done <<<"$changed"

if [ "${#out_of_scope[@]}" -gt 0 ]; then
  echo "verify-phase: out-of-scope file modifications detected:" >&2
  for f in "${out_of_scope[@]}"; do echo "  - $f" >&2; done
  echo "phase $phase_id declares these files:" >&2
  for p in "${allowed[@]}"; do echo "  + $p" >&2; done
  exit 2
fi

# --- Verify commands ---
run_cmd() {
  echo "+ $1"
  bash -c "$1"
}

mapfile -t default_verify < <(jq -r '.default_verify[]' <<<"$phase_json")
mapfile -t phase_verify   < <(jq -r '.verify[]'         <<<"$phase_json")

for cmd in "${default_verify[@]}" "${phase_verify[@]}"; do
  [ -z "$cmd" ] && continue
  if ! run_cmd "$cmd"; then
    echo "verify-phase: command failed: $cmd" >&2
    exit 1
  fi
done

echo "verify-phase: $slug/$phase_id OK"
