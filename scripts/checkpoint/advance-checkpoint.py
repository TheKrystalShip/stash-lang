#!/usr/bin/env python3
"""advance-checkpoint.py <slug> <phase-id> <status> [--commit SHA] [--notes "..."]
                       [--verified true|false] [--review-status STATUS]

Atomically updates checkpoint.yaml for a phase. Drives the state machine:

  pending -> in_progress   (set started, attempts++)
  in_progress -> done      (set completed, verified, commit; clear `current`)
  in_progress -> failed    (set notes; leave `current` set)
  failed -> in_progress    (retry; attempts++)

For review state, pass `--review-status started|in_progress|resolved` (no phase-id needed,
pass `-` as phase-id).
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import (  # noqa: E402
    die,
    load_checkpoint,
    now_iso,
    phase_by_id,
    load_plan,
    save_checkpoint,
)

VALID = {"pending", "in_progress", "done", "failed"}
TRANSITIONS = {
    ("pending", "in_progress"),
    ("in_progress", "done"),
    ("in_progress", "failed"),
    ("failed", "in_progress"),
    ("done", "done"),          # idempotent
    ("pending", "pending"),    # idempotent
}


def main(argv: list[str]) -> int:
    p = argparse.ArgumentParser()
    p.add_argument("slug")
    p.add_argument("phase_id", help="phase id, or '-' when only updating review state")
    p.add_argument("status", nargs="?", default=None, help=f"one of {sorted(VALID)} (omit for review-only updates)")
    p.add_argument("--commit", default=None)
    p.add_argument("--notes", default=None)
    p.add_argument("--verified", choices=["true", "false"], default=None)
    p.add_argument("--review-status", choices=["not_started", "in_progress", "resolved"], default=None)
    args = p.parse_args(argv[1:])

    cp = load_checkpoint(args.slug)

    if args.phase_id != "-":
        if args.status is None or args.status not in VALID:
            die(f"status must be one of {sorted(VALID)}")
        plan = load_plan(args.slug)
        phase_by_id(plan, args.phase_id)  # validates id exists in plan

        state = cp.setdefault("phases", {}).setdefault(args.phase_id, {
            "status": "pending", "commit": None, "verified": False,
            "started": None, "completed": None, "attempts": 0, "notes": "",
        })
        cur = state.get("status", "pending")
        if (cur, args.status) not in TRANSITIONS:
            die(f"illegal transition {cur} -> {args.status} for phase {args.phase_id}")

        if cur != "in_progress" and args.status == "in_progress":
            state["started"] = now_iso()
            state["attempts"] = (state.get("attempts") or 0) + 1
            cp["current"] = args.phase_id
        if args.status == "done":
            state["completed"] = now_iso()
            if cp.get("current") == args.phase_id:
                cp["current"] = None
        if args.status == "failed":
            # Keep `current` pointing at the failed phase so /resume notices it.
            cp["current"] = args.phase_id

        state["status"] = args.status
        if args.commit is not None:
            state["commit"] = args.commit
        if args.verified is not None:
            state["verified"] = (args.verified == "true")
        if args.notes is not None:
            state["notes"] = args.notes

    if args.review_status is not None:
        review = cp.setdefault("review", {})
        review["status"] = args.review_status
        if args.review_status == "in_progress" and not review.get("started"):
            review["started"] = now_iso()
        if args.review_status == "resolved":
            review["resolved"] = now_iso()

    save_checkpoint(args.slug, cp)
    print(f"checkpoint updated: {args.slug}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
