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
script_dir="$(cd "$(dirname "$0")" && pwd)"
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
python3 - <<PY
import re, sys
sys.path.insert(0, "$script_dir")
from _common import load_checkpoint, load_plan, feature_dir  # type: ignore
cp = load_checkpoint("$slug")
plan = load_plan("$slug")
bad = []
for ph in plan.get("phases") or []:
    s = (cp.get("phases") or {}).get(ph["id"], {}).get("status")
    if s != "done":
        bad.append(f"{ph['id']}={s}")
if bad:
    print("refusing to promote: incomplete phases: " + ", ".join(bad), file=sys.stderr)
    sys.exit(1)
# Review gate: derive the authoritative open-finding count from review.md itself
# (the source of truth), NOT the checkpoint's `findings_open` counter. That counter
# is not auto-maintained by the workflow scripts and silently drifts; review.md's
# per-finding `**Status:** open|fixed` headers are always current.
rv = cp.get("review") or {}
review_md = feature_dir("$slug") / "review.md"
if review_md.is_file():
    open_findings = re.findall(r"^\*\*Status:\*\*\s*open\b",
                               review_md.read_text(encoding="utf-8"), re.MULTILINE)
    if open_findings:
        stored = rv.get("findings_open")
        drift = "" if stored == len(open_findings) else f" [checkpoint counter says {stored} — drift, ignored]"
        print(f"refusing to promote: {len(open_findings)} open review finding(s) in review.md{drift}",
              file=sys.stderr)
        sys.exit(1)
elif rv.get("findings_open"):
    # No review.md on disk — fall back to the stored counter.
    print(f"refusing to promote: {rv['findings_open']} open review findings", file=sys.stderr)
    sys.exit(1)
PY

# Run final_verify commands from plan.yaml.
mapfile -t cmds < <(python3 - <<PY
import sys, json
sys.path.insert(0, "$script_dir")
from _common import load_plan  # type: ignore
plan = load_plan("$slug")
for c in plan.get("final_verify") or []:
    print(c)
PY
)

for cmd in "${cmds[@]}"; do
  [ -z "$cmd" ] && continue
  echo "+ $cmd"
  bash -c "$cmd"
done

mv "$src" "$dst"
echo "promoted: $src -> $dst"
echo "remember to: prepend a one-line entry to .claude/repo.md under 'Recent Completed Work'"
