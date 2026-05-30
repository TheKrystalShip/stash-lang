#!/usr/bin/env python3
"""milestone-status.py <milestone-slug?>

Prints the DERIVED ledger for a long-term milestone: which child features are
done and which are in flight. Completion is computed by scanning feature dirs
for a `milestone: <slug>` tag in their plan.yaml — never read from the milestone
doc itself, which is a living (and therefore drift-prone) artifact.

No agent invocation, no LLM calls. Used by `/milestone`.

A milestone lives at .kanban/milestones/<slug>/MILESTONE.md. A child feature
opts in by carrying `milestone: <slug>` at the top level of its plan.yaml.
"""
from __future__ import annotations

import sys
from pathlib import Path
from typing import Any

import yaml  # type: ignore

sys.path.insert(0, str(Path(__file__).parent))
from _common import DONE_DIR, INPROGRESS_DIR, MILESTONES_DIR, die  # noqa: E402


def _safe_load(path: Path) -> dict[str, Any]:
    """Tolerant load — a single malformed plan.yaml must not abort the scan."""
    try:
        with path.open("r", encoding="utf-8") as f:
            data = yaml.safe_load(f)
        return data if isinstance(data, dict) else {}
    except Exception:
        return {}


def _list_milestones() -> list[str]:
    if not MILESTONES_DIR.is_dir():
        return []
    return sorted(p.name for p in MILESTONES_DIR.iterdir()
                  if p.is_dir() and (p / "MILESTONE.md").is_file())


def _members(stage_dir: Path, slug: str) -> list[tuple[str, str]]:
    """(feature-slug, title) for features in stage_dir tagged with this milestone."""
    if not stage_dir.is_dir():
        return []
    out: list[tuple[str, str]] = []
    for d in sorted(stage_dir.iterdir()):
        plan = d / "plan.yaml"
        if not d.is_dir() or not plan.is_file():
            continue
        data = _safe_load(plan)
        if data.get("milestone") == slug:
            out.append((d.name, str(data.get("title") or "")))
    return out


def main(argv: list[str]) -> int:
    slug = argv[1] if len(argv) > 1 else None
    if not slug:
        found = _list_milestones()
        if not found:
            print("no milestones in .kanban/milestones/")
            return 0
        if len(found) > 1:
            print("multiple milestones; pass a slug explicitly:")
            for m in found:
                print(f"  {m}")
            return 1
        slug = found[0]

    mdir = MILESTONES_DIR / slug
    if not (mdir / "MILESTONE.md").is_file():
        die(f"milestone not found: {mdir}/MILESTONE.md")

    done = _members(DONE_DIR, slug)
    inflight = _members(INPROGRESS_DIR, slug)

    print(f"Milestone: {slug}")
    print(f"Charter:   {mdir}/MILESTONE.md")
    print()
    print("Ledger (derived from feature dirs — not from the charter):")
    print(f"  done:      {len(done)}")
    print(f"  in-flight: {len(inflight)}")
    print()

    if done:
        print("DONE (in 4-done/):")
        for fslug, title in done:
            print(f"  [x] {fslug:<40} {title}")
        print()
    if inflight:
        print("IN-FLIGHT (in 2-in-progress/):")
        for fslug, title in inflight:
            print(f"  [~] {fslug:<40} {title}")
        print()

    if not done and not inflight:
        print("No child features tagged `milestone: " + slug + "` yet.")
        print()

    # Pending units are intentionally NOT enumerated here — the road is built as
    # we go. "What's next" is the charter's job, not a derived fact.
    print("Next up: read the 'Rough order & next up' section of the charter.")
    print("To start the next unit:  /spec <next-unit-slug>")
    print("Remember to add  `milestone: " + slug + "`  to that feature's plan.yaml.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
