#!/usr/bin/env python3
"""next-phase.py <slug?> <count?>

Prints the next pending phase(s) whose dependencies are all done or included
earlier in the selected batch. With no count, output is the legacy single phase
YAML document. With count > 1, output is a batch document with `phases:`.

  - `_brief.brief_path` (relative to repo root)
  - `_brief.feature`, `_brief.title`
  - `_brief.default_verify`, `_brief.scope` (from plan.yaml)
  - `_brief.attempts` (from checkpoint.yaml, so a retry can be detected)

Exit code:
  0 — printed phase(s)
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


def parse_args(argv: list[str]) -> tuple[str, int]:
    raw = argv[1:]
    count = 1
    slug_arg: str | None = None

    if len(raw) == 1:
        if raw[0].isdigit():
            count = int(raw[0])
        else:
            slug_arg = raw[0]
    elif len(raw) >= 2:
        slug_arg = raw[0]
        if not raw[1].isdigit():
            print(f"error: count must be an integer, got {raw[1]!r}", file=sys.stderr)
            raise SystemExit(1)
        count = int(raw[1])

    if count < 1:
        print("error: count must be >= 1", file=sys.stderr)
        raise SystemExit(1)
    if count > 5:
        print("error: refusing phase batch > 5; split it into smaller batches", file=sys.stderr)
        raise SystemExit(1)

    return resolve_feature(slug_arg), count


def shared_brief(slug: str, plan: dict, feature_path: Path) -> dict:
    brief_rel = plan.get("brief") or plan.get("spec") or "brief.md"
    brief_path = (feature_path / brief_rel).resolve()
    context_path = (feature_path / plan.get("context", "context.md")).resolve()
    return {
        "feature": slug,
        "title": plan.get("title"),
        "feature_dir": str(feature_path.relative_to(Path.cwd())) if feature_path.is_relative_to(Path.cwd()) else str(feature_path),
        "brief_path": str(brief_path.relative_to(Path.cwd())) if brief_path.is_relative_to(Path.cwd()) else str(brief_path),
        "spec_path": str(brief_path.relative_to(Path.cwd())) if brief_path.is_relative_to(Path.cwd()) else str(brief_path),
        "context_path": str(context_path.relative_to(Path.cwd())) if context_path.is_file() and context_path.is_relative_to(Path.cwd()) else (str(context_path) if context_path.is_file() else None),
        "default_verify": plan.get("default_verify") or [],
        "scope": plan.get("scope") or [],
    }


def main(argv: list[str]) -> int:
    slug, count = parse_args(argv)
    plan = load_plan(slug)
    cp = load_checkpoint(slug)

    phases = plan.get("phases") or []
    cp_phases = cp.get("phases") or {}

    # A phase is dispatchable if status==pending AND all deps are done or were
    # selected earlier in this batch.
    chosen: list[dict] = []
    chosen_ids: set[str] = set()
    for ph in phases:
        if len(chosen) >= count:
            break
        pid = ph["id"]
        state = cp_phases.get(pid, {})
        if state.get("status") != "pending":
            continue
        deps = ph.get("deps") or []
        if not all(cp_phases.get(d, {}).get("status") == "done" or d in chosen_ids for d in deps):
            continue
        chosen.append(ph)
        chosen_ids.add(pid)

    if not chosen:
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
    brief = shared_brief(slug, plan, feature_path)
    for ph in chosen:
        phase_state = cp_phases.get(ph["id"], {})
        ph["_brief"] = {
            **brief,
            "attempts": phase_state.get("attempts", 0),
            "previous_summary": phase_state.get("notes", ""),
        }

    if count == 1:
        out = dict(chosen[0])
    else:
        out = {
            "batch": True,
            "requested_count": count,
            "selected_count": len(chosen),
            "phase_ids": [ph["id"] for ph in chosen],
            "phases": chosen,
            "_brief": brief,
        }
    yaml.safe_dump(out, sys.stdout, sort_keys=False, default_flow_style=False, width=100)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
