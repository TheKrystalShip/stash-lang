#!/usr/bin/env python3
"""verify-phase-scope.py <slug> <phase-id> — differential-oracle REFERENCE.

This is the Python reference implementation of the file-scope glob check that
the bash verify-phase.sh used to perform inline. It exists ONLY so the
differential oracle (difftest_runner.stash) has a faithful reference to diff the
Stash port (verify-phase-scope.stash) against. The real verify-phase.sh delegates
to the .stash twin, NOT to this file.

Faithfulness anchor: glob matching is delegated to real bash `[[ "$f" == $pat ]]`
under `shopt -s globstar`, i.e. the exact construct the original verify-phase.sh
used. That makes this reference an honest stand-in for the original bash logic
(no reimplemented matcher to drift), so the Stash port's path.match is tested
against genuine bash semantics.

Contract (mirrors verify-phase-scope.stash):
  stdout — verify commands (default_verify then phase verify), one per line.
  stderr — header, "Done when:" echoes, out-of-scope block + hint.
  exit   — 0 ok, 2 out-of-scope, 3 bad args.
"""
import sys

# Don't write __pycache__/*.pyc — set BEFORE importing _common. In the oracle's
# fixture trees (no .gitignore) a stray .pyc would surface as an untracked file
# and pollute the scope-classification set, diverging from the stash side.
sys.dont_write_bytecode = True

import os  # noqa: E402
import subprocess  # noqa: E402

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import load_plan, phase_by_id  # type: ignore  # noqa: E402

EXIT_OK = 0
EXIT_OUT_OF_SCOPE = 2
EXIT_BAD_ARGS = 3
OOS_PREFIX = "  - "
ALLOW_PREFIX = "  + "
INPROGRESS_DIR = ".kanban/2-in-progress"


def bash_match(f: str, pat: str) -> bool:
    """Ground truth: bash [[ "$f" == $pat ]] under globstar (bare * crosses /)."""
    script = 'shopt -s globstar extglob; [[ "$1" == $2 ]]'
    return subprocess.run(["bash", "-c", script, "bash", f, pat]).returncode == 0


def main() -> None:
    argv = sys.argv[1:]
    if len(argv) != 2:
        print("usage: verify-phase-scope.py <slug> <phase-id>", file=sys.stderr)
        raise SystemExit(EXIT_BAD_ARGS)
    slug, phase_id = argv

    plan = load_plan(slug)
    phase = phase_by_id(plan, phase_id)  # SystemExit(1) if not found, like the stash side

    title = phase.get("title") or ""
    allowed = phase.get("files") or []
    scope = plan.get("scope") or []
    done_when = phase.get("done_when") or []
    default_verify = plan.get("default_verify") or []
    phase_verify = phase.get("verify") or []

    print(f"verify-phase: {slug}/{phase_id} {title}", file=sys.stderr)
    if done_when:
        print("Done when:", file=sys.stderr)
        for item in done_when:
            print(f"{OOS_PREFIX}{item}", file=sys.stderr)

    diff = subprocess.run(
        ["git", "diff", "--name-only", "HEAD"], capture_output=True, text=True
    ).stdout
    others = subprocess.run(
        ["git", "ls-files", "--others", "--exclude-standard"], capture_output=True, text=True
    ).stdout
    changed = [ln for ln in (diff + others).splitlines() if ln]

    patterns = list(allowed) + list(scope)
    carve_out = f"{INPROGRESS_DIR}/{slug}/*"

    out_of_scope = []
    for f in changed:
        matched = False
        for pat0 in patterns:
            pat = pat0[2:] if pat0.startswith("./") else pat0
            if bash_match(f, pat):
                matched = True
                break
        if not matched and not bash_match(f, carve_out):
            out_of_scope.append(f)

    if out_of_scope:
        print("verify-phase: out-of-scope file modifications detected:", file=sys.stderr)
        for f in out_of_scope:
            print(f"{OOS_PREFIX}{f}", file=sys.stderr)
        print(f"phase {phase_id} declares these files:", file=sys.stderr)
        for p in allowed:
            print(f"{ALLOW_PREFIX}{p}", file=sys.stderr)
        print(
            "If the plan is stale, make the smallest justified correction to plan.yaml and rerun.",
            file=sys.stderr,
        )
        raise SystemExit(EXIT_OUT_OF_SCOPE)

    for c in default_verify:
        if c:
            print(c)
    for c in phase_verify:
        if c:
            print(c)
    raise SystemExit(EXIT_OK)


if __name__ == "__main__":
    main()
