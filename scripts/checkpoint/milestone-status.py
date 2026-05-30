#!/usr/bin/env python3
"""milestone-status.py <milestone-slug?>

Prints the DERIVED ledger for a long-term milestone: which child features are
done and which are in flight. Completion is computed by scanning feature dirs
for a `milestone: <slug>` tag in their plan.yaml — never read from the milestone
doc itself, which is a living (and therefore drift-prone) artifact.

Scans EVERY git worktree, not just the current checkout: a milestone unit is
often developed in a sibling worktree on its own feature/<slug> branch (the
parallel-features workflow), so it is invisible to the current tree until it
merges. Without the cross-worktree scan, `/milestone` run from main would
under-report in-flight units. Results are deduped by feature slug.

No agent invocation, no LLM calls. Used by `/milestone`.

A milestone lives at .kanban/milestones/<slug>/MILESTONE.md. A child feature
opts in by carrying `milestone: <slug>` at the top level of its plan.yaml.
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path
from typing import Any

import yaml  # type: ignore

sys.path.insert(0, str(Path(__file__).parent))
from _common import MILESTONES_DIR, REPO_ROOT, die  # noqa: E402


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


def _worktrees() -> list[tuple[Path, str | None, bool]]:
    """(path, branch, is_current) for every git worktree. Falls back to the
    current checkout alone if git is unavailable."""
    try:
        raw = subprocess.check_output(
            ["git", "worktree", "list", "--porcelain"], text=True, cwd=REPO_ROOT,
        )
    except (subprocess.CalledProcessError, FileNotFoundError):
        return [(REPO_ROOT, None, True)]
    out: list[tuple[Path, str | None, bool]] = []
    path: Path | None = None
    branch: str | None = None
    for line in raw.splitlines():
        if line.startswith("worktree "):
            path, branch = Path(line[len("worktree "):]), None
        elif line.startswith("branch "):
            branch = line[len("branch "):].removeprefix("refs/heads/")
        elif line == "" and path is not None:
            out.append((path, branch, path.resolve() == REPO_ROOT.resolve()))
            path, branch = None, None
    if path is not None:
        out.append((path, branch, path.resolve() == REPO_ROOT.resolve()))
    return out or [(REPO_ROOT, None, True)]


def _members(stage_rel: str, slug: str) -> list[tuple[str, str, str]]:
    """(feature-slug, title, location) for features tagged with this milestone in
    `stage_rel` (e.g. ".kanban/2-in-progress") across ALL worktrees, deduped by
    slug. `location` is "" for the current checkout, else "(branch)" — so a unit
    being built in a sibling worktree is visible and labelled."""
    seen: dict[str, tuple[str, str, str]] = {}
    for path, branch, is_current in _worktrees():
        stage_dir = path / stage_rel
        if not stage_dir.is_dir():
            continue
        for d in sorted(stage_dir.iterdir()):
            plan = d / "plan.yaml"
            if not d.is_dir() or not plan.is_file():
                continue
            data = _safe_load(plan)
            if data.get("milestone") != slug:
                continue
            # Prefer the current checkout's copy of a slug (no label); otherwise
            # the first sibling that has it. A slug only truly lives in one tree;
            # this guards inherited committed dirs appearing in several.
            if d.name in seen and not is_current:
                continue
            loc = "" if is_current else (f"({branch})" if branch else "(worktree)")
            seen[d.name] = (d.name, str(data.get("title") or ""), loc)
    return sorted(seen.values())


def _fmt(rows: list[tuple[str, str, str]]) -> None:
    for fslug, title, loc in rows:
        suffix = f"  {loc}" if loc else ""
        print(f"  {fslug:<40} {title}{suffix}")


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

    done = _members(".kanban/4-done", slug)
    done_slugs = {f[0] for f in done}
    # A unit that's done in one tree shouldn't also count as in-flight in another.
    inflight = [f for f in _members(".kanban/2-in-progress", slug) if f[0] not in done_slugs]

    print(f"Milestone: {slug}")
    print(f"Charter:   {mdir}/MILESTONE.md")
    print()
    print("Ledger (derived from feature dirs across all worktrees — not from the charter):")
    print(f"  done:      {len(done)}")
    print(f"  in-flight: {len(inflight)}")
    print()

    if done:
        print("DONE (in 4-done/):")
        _fmt([(s, t, loc) for s, t, loc in done])
        print()
    if inflight:
        print("IN-FLIGHT (in 2-in-progress/):")
        _fmt(inflight)
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
