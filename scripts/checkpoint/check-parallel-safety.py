#!/usr/bin/env python3
"""check-parallel-safety.py <slug?>

Advisory check for the parallel-features workflow (see .claude/WORKFLOW.md
"Running Features in Parallel"). Compares this feature's plan.yaml file-globs
against the features in flight in OTHER git worktrees and warns when they share
a source subsystem — the signal that two branches will collide on the same hot
files at integration time (e.g. two language features both touching
Stash.Core / Stash.Bytecode and the six visitors).

Run AFTER /spec, once this feature has a plan.yaml. Worktree isolation means the
in-worktree .kanban/ only shows THIS branch's feature, so siblings are read from
their on-disk worktree paths via `git worktree list`.

Exit codes:
  0  no overlap (safe to run in parallel) or no sibling worktrees
  2  usage / cannot determine this feature's globs
  3  subsystem overlap found (non-blocking WARNING — judgment call to serialize)

Heuristic: a glob is reduced to its top-level path segment (Stash.Bytecode/VM/**
-> Stash.Bytecode). Noisy shared dirs that almost every feature touches are
excluded so they don't drown the real signal; this means same-FILE collisions
inside Stash.Tests are not flagged here — the WORKFLOW.md convention covers those.
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import REPO_ROOT, resolve_feature  # noqa: E402

import yaml  # type: ignore  # noqa: E402

# Dirs touched by nearly every feature — excluded so they don't manufacture
# false overlaps. Real conflicts live in the source projects below them.
NOISE = {
    "Stash.Tests", ".kanban", "docs", "examples", "benchmarks", ".vscode",
    "scripts", ".claude",
}


def subsystems_of(globs: list[str]) -> set[str]:
    """Reduce file-globs to the set of top-level source subsystems they touch."""
    out: set[str] = set()
    for g in globs or []:
        g = g.strip()
        if g.startswith("./"):  # strip a leading "./" PREFIX (not a char-class — keep dotfiles)
            g = g[2:]
        if not g:
            continue
        seg = g.split("/", 1)[0]
        if not seg or "*" in seg or seg in NOISE:
            continue
        out.add(seg)
    return out


def feature_globs(plan_path: Path) -> list[str]:
    if not plan_path.is_file():
        return []
    plan = yaml.safe_load(plan_path.read_text(encoding="utf-8")) or {}
    globs: list[str] = []
    for ph in plan.get("phases") or []:
        globs.extend(ph.get("files") or [])
    return globs


def worktrees() -> list[tuple[Path, str | None]]:
    """List (path, branch) for every git worktree."""
    out: list[tuple[Path, str | None]] = []
    try:
        raw = subprocess.check_output(
            ["git", "worktree", "list", "--porcelain"], text=True, cwd=REPO_ROOT,
        )
    except subprocess.CalledProcessError:
        return out
    path: Path | None = None
    branch: str | None = None
    for line in raw.splitlines():
        if line.startswith("worktree "):
            path = Path(line[len("worktree "):])
            branch = None
        elif line.startswith("branch "):
            ref = line[len("branch "):]
            branch = ref.removeprefix("refs/heads/")
        elif line == "" and path is not None:
            out.append((path, branch))
            path, branch = None, None
    if path is not None:
        out.append((path, branch))
    return out


def main(argv: list[str]) -> int:
    slug = resolve_feature(argv[1] if len(argv) > 1 else None)
    mine = subsystems_of(feature_globs(REPO_ROOT / ".kanban" / "2-in-progress" / slug / "plan.yaml"))
    if not mine:
        print(f"error: no source subsystems found for '{slug}' — run after /spec writes plan.yaml", file=sys.stderr)
        return 2

    print(f"feature '{slug}' touches: {', '.join(sorted(mine)) or '(none)'}")

    overlaps: list[tuple[str, str, set[str]]] = []
    siblings = 0
    for path, branch in worktrees():
        if path.resolve() == REPO_ROOT.resolve():
            continue  # this worktree
        inprog = path / ".kanban" / "2-in-progress"
        if not inprog.is_dir():
            continue
        for fdir in sorted(p for p in inprog.iterdir() if p.is_dir()):
            siblings += 1
            theirs = subsystems_of(feature_globs(fdir / "plan.yaml"))
            shared = mine & theirs
            label = f"{fdir.name} ({branch})" if branch else fdir.name
            if shared:
                overlaps.append((label, "", shared))
            print(f"  vs {label}: {'OVERLAP ' + ', '.join(sorted(shared)) if shared else 'disjoint'}"
                  f"{'  touches ' + ', '.join(sorted(theirs)) if not shared and theirs else ''}")

    if siblings == 0:
        print("clean: no in-flight features in sibling worktrees")
        return 0
    if overlaps:
        print()
        print("WARN: shared subsystem(s) with in-flight feature(s):")
        for label, _, shared in overlaps:
            print(f"  - {label}: {', '.join(sorted(shared))}")
        print("→ collision on hot files likely at integration. Consider serializing,")
        print("  or accept that last-to-merge pays the conflict cost.")
        return 3
    print("clean: no subsystem overlap with in-flight features")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
