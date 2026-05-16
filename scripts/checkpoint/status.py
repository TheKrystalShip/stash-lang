#!/usr/bin/env python3
"""status.py <slug?>

Prints a compact status report for an active feature. Used by `/resume` to
decide what advice to give the user. No agent invocation, no LLM calls.
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import INPROGRESS_DIR, load_checkpoint, load_plan, resolve_feature  # noqa: E402


def git(*args: str) -> str:
    return subprocess.check_output(["git", *args], text=True).strip()


def main(argv: list[str]) -> int:
    slug = resolve_feature(argv[1] if len(argv) > 1 else None)
    plan = load_plan(slug)
    cp = load_checkpoint(slug)
    fdir = INPROGRESS_DIR / slug

    print(f"Feature: {slug}  ({plan.get('title')})")
    print(f"Dir:     {fdir}")
    print(f"Updated: {cp.get('updated')}")
    print()

    # Phase table
    print(f"{'Phase':<6} {'Status':<12} {'Attempts':<10} {'Verified':<10} {'Commit':<12} Notes")
    print("-" * 80)
    for ph in plan.get("phases") or []:
        pid = ph["id"]
        s = (cp.get("phases") or {}).get(pid, {})
        print(f"{pid:<6} {s.get('status', 'pending'):<12} "
              f"{s.get('attempts', 0)!s:<10} {s.get('verified', False)!s:<10} "
              f"{(s.get('commit') or '-')[:10]:<12} {s.get('notes', '')[:40]}")

    print()
    cur = cp.get("current")
    if cur:
        print(f"current phase: {cur}")
    else:
        print("current phase: (none — between phases)")

    # Git state
    try:
        branch = git("branch", "--show-current")
        dirty = git("status", "--porcelain")
        last = git("log", "-1", "--oneline")
        print(f"branch: {branch}")
        print(f"last commit: {last}")
        print(f"working tree: {'dirty' if dirty else 'clean'}")
        if dirty:
            for line in dirty.splitlines()[:10]:
                print(f"  {line}")
            extra = len(dirty.splitlines()) - 10
            if extra > 0:
                print(f"  ... and {extra} more files")
    except subprocess.CalledProcessError:
        print("git: not a repo or error")

    # Review state
    rv = cp.get("review") or {}
    if rv.get("status") and rv["status"] != "not_started":
        print()
        print(f"review: {rv.get('status')}  open={rv.get('findings_open', 0)}  fixed={rv.get('findings_fixed', 0)}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
