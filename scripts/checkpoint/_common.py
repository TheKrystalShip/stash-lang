"""Shared helpers for checkpoint scripts. Imported by sibling .py files.

Layout convention: every active feature lives under
  .kanban/2-in-progress/<slug>/
with these files:
  brief.md, plan.yaml, checkpoint.yaml, review.md (optional)
"""
from __future__ import annotations

import datetime as _dt
import pathlib as _pl
import sys as _sys
from typing import Any, Iterable

import yaml  # type: ignore

REPO_ROOT = _pl.Path(__file__).resolve().parents[2]
INPROGRESS_DIR = REPO_ROOT / ".kanban" / "2-in-progress"
TEMPLATES_DIR = REPO_ROOT / ".kanban" / "_templates"
DONE_DIR = REPO_ROOT / ".kanban" / "4-done"
MILESTONES_DIR = REPO_ROOT / ".kanban" / "milestones"


def now_iso() -> str:
    return _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def die(msg: str, code: int = 1) -> None:
    print(f"error: {msg}", file=_sys.stderr)
    _sys.exit(code)


def feature_dir(slug: str) -> _pl.Path:
    d = INPROGRESS_DIR / slug
    if not d.is_dir():
        # Fall back to 4-done so post-promotion callers resolve the same slug instead
        # of false-failing — e.g. worktree-finish.sh re-runs final_verify (which may
        # include validate-spec.py) AFTER /done has moved the feature to 4-done/.
        # In-progress wins when both exist, so active-feature behavior is unchanged.
        done = DONE_DIR / slug
        if done.is_dir():
            return done
        die(f"feature directory not found in 2-in-progress/ or 4-done/: {slug}")
    return d


def resolve_feature(slug: str | None) -> str:
    """If slug given, validate. Otherwise infer the single active feature."""
    if slug:
        feature_dir(slug)
        return slug
    candidates = [p.name for p in INPROGRESS_DIR.iterdir() if p.is_dir()] if INPROGRESS_DIR.is_dir() else []
    if not candidates:
        die("no active feature in .kanban/2-in-progress/; pass a slug explicitly")
    if len(candidates) > 1:
        die(f"multiple active features ({', '.join(candidates)}); pass a slug explicitly")
    return candidates[0]


def load_yaml(path: _pl.Path) -> dict[str, Any]:
    if not path.is_file():
        die(f"file not found: {path}")
    with path.open("r", encoding="utf-8") as f:
        data = yaml.safe_load(f)
    if not isinstance(data, dict):
        die(f"{path} did not parse to a mapping")
    return data


def save_yaml(path: _pl.Path, data: dict[str, Any]) -> None:
    tmp = path.with_suffix(path.suffix + ".tmp")
    with tmp.open("w", encoding="utf-8") as f:
        yaml.safe_dump(data, f, sort_keys=False, default_flow_style=False, width=100)
    tmp.replace(path)


def load_plan(slug: str) -> dict[str, Any]:
    return load_yaml(feature_dir(slug) / "plan.yaml")


def load_checkpoint(slug: str) -> dict[str, Any]:
    return load_yaml(feature_dir(slug) / "checkpoint.yaml")


def save_checkpoint(slug: str, data: dict[str, Any]) -> None:
    data["updated"] = now_iso()
    save_yaml(feature_dir(slug) / "checkpoint.yaml", data)


def phase_by_id(plan: dict[str, Any], pid: str) -> dict[str, Any]:
    for ph in plan.get("phases", []):
        if ph.get("id") == pid:
            return ph
    die(f"phase {pid} not found in plan.yaml")
    raise AssertionError  # for type checkers


def expand_globs(patterns: Iterable[str]) -> list[_pl.Path]:
    out: list[_pl.Path] = []
    for pat in patterns:
        out.extend(REPO_ROOT.glob(pat))
    return out
