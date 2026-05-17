#!/usr/bin/env python3
"""validate-spec.py <slug?>

Validates the structure of plan.yaml + checkpoint.yaml for an active feature.
Exits non-zero on any structural problem. Run by the architect before
declaring a feature ready for `/next-phase`, and by other slash commands as
a precondition check.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import (  # noqa: E402
    die,
    feature_dir,
    load_checkpoint,
    load_plan,
    resolve_feature,
    save_checkpoint,
)

REQUIRED_PLAN_KEYS = {"schema_version", "feature", "title", "phases"}
REQUIRED_PHASE_KEYS = {"id", "title", "deps", "files", "verify"}


def main(argv: list[str]) -> int:
    slug = resolve_feature(argv[1] if len(argv) > 1 else None)
    plan = load_plan(slug)
    cp = load_checkpoint(slug)
    fdir = feature_dir(slug)

    problems: list[str] = []

    # --- plan.yaml ---
    missing = REQUIRED_PLAN_KEYS - plan.keys()
    if missing:
        problems.append(f"plan.yaml missing keys: {sorted(missing)}")

    if plan.get("feature") != slug:
        problems.append(f"plan.yaml `feature` ({plan.get('feature')!r}) does not match directory name ({slug!r})")

    brief_rel = plan.get("brief") or plan.get("spec")
    if not brief_rel:
        problems.append("plan.yaml must declare `brief: ./brief.md`")
        brief_path = fdir / "brief.md"
    else:
        brief_path = fdir / brief_rel
        if not brief_path.is_file():
            problems.append(f"brief file not found: {brief_path}")

    phases = plan.get("phases") or []
    if not phases:
        problems.append("plan.yaml has no phases")

    seen_ids: set[str] = set()
    for i, ph in enumerate(phases):
        if not isinstance(ph, dict):
            problems.append(f"phase #{i} is not a mapping")
            continue
        missing_p = REQUIRED_PHASE_KEYS - ph.keys()
        if missing_p:
            problems.append(f"phase {ph.get('id', f'#{i}')} missing keys: {sorted(missing_p)}")
        pid = ph.get("id")
        if pid in seen_ids:
            problems.append(f"duplicate phase id: {pid}")
        seen_ids.add(pid)
        for dep in ph.get("deps", []) or []:
            if dep not in {p.get("id") for p in phases}:
                problems.append(f"phase {pid} deps on unknown phase {dep}")
        if not ph.get("files"):
            problems.append(f"phase {pid} has empty files list (every phase must declare its scope)")
        if not ph.get("verify"):
            problems.append(f"phase {pid} has empty verify list (every phase must declare how to verify)")
        if plan.get("brief") and not ph.get("done_when"):
            problems.append(f"phase {pid} has empty done_when list (state the end-to-end behavior that proves the phase)")
        est = ph.get("est_tokens")
        if est is not None:
            if not isinstance(est, int) or est < 5000:
                problems.append(f"phase {pid} est_tokens looks too small ({est})")
            if isinstance(est, int) and est > 80000:
                problems.append(f"phase {pid} est_tokens={est} exceeds 80k — consider splitting")

    # --- checkpoint.yaml ---
    if cp.get("feature") != slug:
        problems.append(f"checkpoint.yaml `feature` does not match directory name {slug!r}")

    cp_phases = cp.setdefault("phases", {})
    plan_ids = {p.get("id") for p in phases if p.get("id")}
    cp_ids = set(cp_phases.keys())
    # Auto-heal: missing checkpoint entries become pending. This lets the architect
    # add phases to plan.yaml without separately editing checkpoint.yaml.
    healed: list[str] = []
    for pid in sorted(plan_ids - cp_ids):
        cp_phases[pid] = {
            "status": "pending", "commit": None, "verified": False,
            "started": None, "completed": None, "attempts": 0, "notes": "",
        }
        healed.append(pid)
    # Stale entries (in checkpoint but not in plan) are flagged but not deleted.
    if cp_ids - plan_ids:
        problems.append(f"checkpoint.yaml has entries for unknown phases: {sorted(cp_ids - plan_ids)} (remove manually if intended)")
    if healed:
        save_checkpoint(slug, cp)
        print(f"healed checkpoint: added pending entries for {healed}", file=sys.stderr)

    if problems:
        print(f"plan/checkpoint validation FAILED for '{slug}':", file=sys.stderr)
        for p in problems:
            print(f"  - {p}", file=sys.stderr)
        return 1

    print(f"plan/checkpoint OK for '{slug}': {len(phases)} phases")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
