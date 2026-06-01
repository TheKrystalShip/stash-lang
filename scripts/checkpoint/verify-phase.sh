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

cd "$repo_root"

# Metadata load + scope/glob check are delegated to the Stash port. It prints the
# verify commands (default_verify then phase verify) on stdout for us to run, and
# all human output (header, "Done when:" echoes, out-of-scope block) on stderr.
# Exit codes propagate verbatim: 0 ok, 2 out-of-scope, 3 bad args.
#   (Replaces the former embedded Python heredoc + JSON-filter parsing + bash glob loop.)
set +e
cmds="$(stash scripts/checkpoint/verify-phase-scope.stash "$slug" "$phase_id")"
rc=$?
set -e
if [ "$rc" -ne 0 ]; then
  exit "$rc"
fi

# --- Verify commands ---
run_cmd() {
  echo "+ $1"
  bash -c "$1"
}

while IFS= read -r cmd; do
  [ -z "$cmd" ] && continue
  if ! run_cmd "$cmd"; then
    echo "verify-phase: command failed: $cmd" >&2
    exit 1
  fi
done <<<"$cmds"

echo "verify-phase: $slug/$phase_id OK"
