#!/usr/bin/env bash
# verify-phase.sh <slug> <phase-id>
#
# Runs a phase's verify commands AND checks that the working tree's modified
# files stay within the current phase plan. Implementers may correct plan.yaml
# first when the original plan is stale; this script enforces the updated plan.
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
    "title": ph.get("title"),
    "files": ph.get("files") or [],
    "verify": ph.get("verify") or [],
    "done_when": ph.get("done_when") or [],
    "default_verify": plan.get("default_verify") or [],
    "scope": plan.get("scope") or [],
}))
PY
)"

phase_title="$(jq -r '.title // ""' <<<"$phase_json")"
mapfile -t allowed < <(jq -r '.files[]' <<<"$phase_json")
mapfile -t scope   < <(jq -r '.scope[]' <<<"$phase_json")
mapfile -t done_when < <(jq -r '.done_when[]' <<<"$phase_json")

echo "verify-phase: $slug/$phase_id ${phase_title}"
if [ "${#done_when[@]}" -gt 0 ]; then
  echo "Done when:"
  for item in "${done_when[@]}"; do echo "  - $item"; done
fi

# --- Scope check ---
# Compare working tree against last commit. Uncommitted edits are the implementer's
# pending work; flag anything outside the current plan before it is committed.
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
  echo "If the plan is stale, make the smallest justified correction to plan.yaml and rerun." >&2
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
