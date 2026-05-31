#!/usr/bin/env python3
"""Differential equivalence harness: advance-checkpoint.py  vs  advance-checkpoint.stash

For each case we build TWO isolated fixture trees from one seed, run the Python
reference in one and the Stash port in the other, then compare:
  - exit code            (HARD contract)
  - stderr               (SOFT — reported, not asserted)
  - resulting checkpoint.yaml, parsed + timestamp-masked, deep-equal (HARD)

Timestamps (updated / per-phase started,completed / review.started,resolved) are
the only nondeterminism: the two runs happen seconds apart. We assert each is
both-absent-or-both-ISO-Z-shaped, then blank it before the deep compare. `attempts`
is deterministic and compared EXACTLY.
"""
from __future__ import annotations
import os, re, shutil, subprocess, sys, tempfile
from pathlib import Path
import yaml

def _find_repo_root(start: Path) -> Path:
    """Walk up from this file to the repo root (the dir containing .git)."""
    d = start.resolve()
    for p in (d, *d.parents):
        if (p / ".git").exists():
            return p
    return d.parents[2]  # fallback: scripts/checkpoint/<file> -> repo root

REPO = _find_repo_root(Path(__file__))
PY_COMMON = REPO / "scripts/checkpoint/_common.py"
PY_SCRIPT = REPO / "scripts/checkpoint/advance-checkpoint.py"
STASH_SCRIPT = REPO / "scripts/checkpoint/advance-checkpoint.stash"
STASH_BIN = shutil.which("stash") or os.path.expanduser("~/.local/bin/stash")

ISO_Z = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$")
TS_PHASE = ("started", "completed")
TS_REVIEW = ("started", "resolved")

SLUG = "difftest"

PLAN = {
    "schema_version": 1,
    "feature": SLUG,
    "title": "Diff Test",
    "brief": "./brief.md",
    "phases": [
        {"id": "P1", "title": "First",  "deps": [],     "files": ["x"], "verify": ["true"]},
        {"id": "P2", "title": "Second", "deps": ["P1"], "files": ["y"], "verify": ["true"]},
    ],
}

def phase(status="pending", attempts=0, commit=None, verified=False, notes="",
          started=None, completed=None):
    return {"status": status, "commit": commit, "verified": verified,
            "started": started, "completed": completed, "attempts": attempts, "notes": notes}

def cp(phases=None, current=None, review=None, extra=None):
    d = {"schema_version": 1, "feature": SLUG, "current": current,
         "updated": "2026-01-01T00:00:00Z", "phases": phases if phases is not None else {}}
    if review is not None:
        d["review"] = review
    if extra:
        d.update(extra)
    return d

# (name, seed_checkpoint, argv-after-script)
CASES = [
    ("pending->in_progress",
     cp({"P1": phase("pending")}),
     ["difftest", "P1", "in_progress"]),
    ("in_progress->done (current==phase, +commit/verified/notes)",
     cp({"P1": phase("in_progress", attempts=1, started="2026-01-01T00:00:01Z")}, current="P1"),
     ["difftest", "P1", "done", "--commit", "abc123", "--verified", "true", "--notes", "did it"]),
    ("in_progress->done (current!=phase -> current NOT cleared)",
     cp({"P1": phase("in_progress", attempts=1), "P2": phase("pending")}, current="P2"),
     ["difftest", "P1", "done"]),
    ("in_progress->failed",
     cp({"P1": phase("in_progress", attempts=1)}, current="P1"),
     ["difftest", "P1", "failed", "--notes", "boom"]),
    ("failed->in_progress (retry, attempts bumps again)",
     cp({"P1": phase("failed", attempts=1, notes="boom")}, current="P1"),
     ["difftest", "P1", "in_progress"]),
    ("done->done idempotent (no attempts bump)",
     cp({"P1": phase("done", attempts=1, completed="2026-01-01T00:00:02Z")}),
     ["difftest", "P1", "done"]),
    ("pending->pending idempotent (no attempts bump)",
     cp({"P1": phase("pending")}),
     ["difftest", "P1", "pending"]),
    ("illegal transition done->in_progress (exit 1)",
     cp({"P1": phase("done", attempts=1)}),
     ["difftest", "P1", "in_progress"]),
    ("verified false",
     cp({"P1": phase("in_progress", attempts=1)}, current="P1"),
     ["difftest", "P1", "done", "--verified", "false"]),
    ("setdefault: phase absent from checkpoint",
     cp({}),  # no P1 entry; script must create default then ->in_progress
     ["difftest", "P1", "in_progress"]),
    ("setdefault: phases key absent entirely",
     {"schema_version": 1, "feature": SLUG, "current": None, "updated": "2026-01-01T00:00:00Z"},
     ["difftest", "P1", "in_progress"]),
    ("review-only in_progress (phase=-)",
     cp({"P1": phase("done", attempts=1)}),
     ["difftest", "-", "--review-status", "in_progress"]),
    ("review-only resolved (started already set)",
     cp({"P1": phase("done", attempts=1)}, review={"status": "in_progress", "started": "2026-01-01T00:00:03Z"}),
     ["difftest", "-", "--review-status", "resolved"]),
    ("review-only not_started",
     cp({"P1": phase("done", attempts=1)}),
     ["difftest", "-", "--review-status", "not_started"]),
    ("combined phase done + review resolved",
     cp({"P1": phase("in_progress", attempts=1)}, current="P1"),
     ["difftest", "P1", "done", "--review-status", "resolved"]),
    ("status missing for real phase (exit 1)",
     cp({"P1": phase("pending")}),
     ["difftest", "P1"]),
]


def seed_tree(root: Path, seed_cp: dict, with_py: bool):
    fdir = root / ".kanban/2-in-progress" / SLUG
    fdir.mkdir(parents=True, exist_ok=True)
    (fdir / "brief.md").write_text("# brief\n")
    with (fdir / "plan.yaml").open("w") as f:
        yaml.safe_dump(PLAN, f, sort_keys=False)
    with (fdir / "checkpoint.yaml").open("w") as f:
        yaml.safe_dump(seed_cp, f, sort_keys=False)
    sdir = root / "scripts/checkpoint"
    sdir.mkdir(parents=True, exist_ok=True)
    if with_py:
        shutil.copy(PY_COMMON, sdir / "_common.py")
        shutil.copy(PY_SCRIPT, sdir / "advance-checkpoint.py")
    else:
        shutil.copy(STASH_SCRIPT, sdir / "advance-checkpoint.stash")
    return fdir


def run(root: Path, argv: list[str], stash: bool):
    if stash:
        cmd = [STASH_BIN, "scripts/checkpoint/advance-checkpoint.stash", *argv]
    else:
        cmd = [sys.executable, "scripts/checkpoint/advance-checkpoint.py", *argv]
    p = subprocess.run(cmd, cwd=root, capture_output=True, text=True)
    return p.returncode, p.stdout, p.stderr


def mask(d):
    """Shape-check + blank timestamp fields. Returns (masked_copy, shape_errors)."""
    errs = []
    if not isinstance(d, dict):
        return d, errs
    import copy
    d = copy.deepcopy(d)

    def check_ts(container, key, where):
        # only relevant if EITHER side has it; comparison of presence is done at top level
        v = container.get(key)
        if v is None:
            return
        if not (isinstance(v, str) and ISO_Z.match(v)):
            errs.append(f"{where}.{key} is not ISO-Z shaped: {v!r}")
        container[key] = "<TS>"

    check_ts(d, "updated", "root")
    for pid, st in (d.get("phases") or {}).items():
        if isinstance(st, dict):
            for k in TS_PHASE:
                check_ts(st, k, f"phases.{pid}")
    if isinstance(d.get("review"), dict):
        for k in TS_REVIEW:
            check_ts(d["review"], k, "review")
    return d, errs


def ts_presence(d):
    """Map of timestamp-field -> bool(present&non-null), to compare shape across sides."""
    pres = {}
    if not isinstance(d, dict):
        return pres
    pres["root.updated"] = bool(d.get("updated"))
    for pid, st in (d.get("phases") or {}).items():
        if isinstance(st, dict):
            for k in TS_PHASE:
                pres[f"phases.{pid}.{k}"] = bool(st.get(k))
    if isinstance(d.get("review"), dict):
        for k in TS_REVIEW:
            pres[f"review.{k}"] = bool(d["review"].get(k))
    return pres


def main():
    print(f"stash binary: {STASH_BIN}")
    passed = failed = 0
    for name, seed, argv in CASES:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            py_root = root / "py"; st_root = root / "st"
            py_fdir = seed_tree(py_root, seed, with_py=True)
            st_fdir = seed_tree(st_root, seed, with_py=False)

            rc_py, _, err_py = run(py_root, argv, stash=False)
            rc_st, _, err_st = run(st_root, argv, stash=True)

            problems = []
            if rc_py != rc_st:
                problems.append(f"EXIT differs: py={rc_py} stash={rc_st}")

            # On a die/exit-nonzero path the file may be unmodified; still compare it.
            py_cp = yaml.safe_load((py_fdir / "checkpoint.yaml").read_text())
            st_cp = yaml.safe_load((st_fdir / "checkpoint.yaml").read_text())

            # timestamp-presence shape must match
            pp, sp = ts_presence(py_cp), ts_presence(st_cp)
            allkeys = set(pp) | set(sp)
            for k in sorted(allkeys):
                if pp.get(k, False) != sp.get(k, False):
                    problems.append(f"TS presence differs at {k}: py={pp.get(k)} stash={sp.get(k)}")

            mpy, epy = mask(py_cp)
            mst, est = mask(st_cp)
            problems += [f"py shape: {e}" for e in epy]
            problems += [f"stash shape: {e}" for e in est]

            if mpy != mst:
                problems.append("DATA differs after masking:")
                problems.append(f"   py   = {mpy}")
                problems.append(f"   stash= {mst}")

            # stderr is soft — report only
            soft = ""
            if err_py.strip() != err_st.strip():
                soft = f"  [stderr soft-differs] py={err_py.strip()!r} stash={err_st.strip()!r}"

            if problems:
                failed += 1
                print(f"\n✗ {name}")
                for pr in problems:
                    print(f"    {pr}")
                if soft:
                    print(soft)
            else:
                passed += 1
                print(f"✓ {name}{soft}")

    print(f"\n{'='*60}\n{passed} passed, {failed} failed  of {passed+failed} cases")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
