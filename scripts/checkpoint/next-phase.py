#!/usr/bin/env python3
"""next-phase.py <slug?>

Prints the next pending phase whose dependencies are all done. Output is a
YAML document on stdout containing the phase entry plus a `_brief` block
that aggregates the context the implementer needs:

  - `_brief.spec_path`, `_brief.context_path` (relative to repo root)
  - `_brief.feature`, `_brief.title`
  - `_brief.default_verify`, `_brief.scope` (from plan.yaml)
  - `_brief.attempts` (from checkpoint.yaml, so a retry can be detected)

Exit code:
  0 — printed a phase
  2 — no phase is ready (either all done, or in_progress, or blocked)
"""
from __future__ import annotations

import sys
from pathlib import Path

import yaml  # type: ignore

sys.path.insert(0, str(Path(__file__).parent))
from _common import (  # noqa: E402
    INPROGRESS_DIR,
    load_checkpoint,
    load_plan,
    resolve_feature,
)


def main(argv: list[str]) -> int:
    slug = resolve_feature(argv[1] if len(argv) > 1 else None)
    plan = load_plan(slug)
    cp = load_checkpoint(slug)

    phases = plan.get("phases") or []
    cp_phases = cp.get("phases") or {}

    # A phase is dispatchable if status==pending AND all deps are done.
    chosen = None
    for ph in phases:
        pid = ph["id"]
        state = cp_phases.get(pid, {})
        if state.get("status") != "pending":
            continue
        deps = ph.get("deps") or []
        if not all(cp_phases.get(d, {}).get("status") == "done" for d in deps):
            continue
        chosen = ph
        break

    if chosen is None:
        # Distinguish "all done" from "blocked / in_progress" for clearer UX.
        if all(cp_phases.get(p["id"], {}).get("status") == "done" for p in phases):
            print("# all phases done", file=sys.stderr)
        else:
            in_progress = [p["id"] for p in phases if cp_phases.get(p["id"], {}).get("status") == "in_progress"]
            if in_progress:
                print(f"# phase {in_progress[0]} is in_progress (use /resume to inspect)", file=sys.stderr)
            else:
                print("# no phase ready (all remaining are blocked on failed/pending deps)", file=sys.stderr)
        return 2

    feature_path = INPROGRESS_DIR / slug
    chosen_state = cp_phases.get(chosen["id"], {})
    out = dict(chosen)
    out["_brief"] = {
        "feature": slug,
        "title": plan.get("title"),
        "feature_dir": str(feature_path.relative_to(Path.cwd())) if feature_path.is_relative_to(Path.cwd()) else str(feature_path),
        "spec_path": str((feature_path / plan["spec"]).resolve().relative_to(Path.cwd())) if (feature_path / plan["spec"]).resolve().is_relative_to(Path.cwd()) else str((feature_path / plan["spec"]).resolve()),
        "context_path": str((feature_path / plan.get("context", "context.md")).resolve()) if (feature_path / plan.get("context", "context.md")).is_file() else None,
        "default_verify": plan.get("default_verify") or [],
        "scope": plan.get("scope") or [],
        "attempts": chosen_state.get("attempts", 0),
    }
    yaml.safe_dump(out, sys.stdout, sort_keys=False, default_flow_style=False, width=100)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
