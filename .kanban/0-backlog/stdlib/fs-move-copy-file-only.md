# `fs.move` / `fs.copy` are file-only — no directory variant

**Status:** Backlog — Stdlib gap
**Created:** 2026-06-01
**Discovery context:** Surfaced by the `checkpoint-bash-retirement` milestone (A.6) porting `promote-done.sh` → `.stash`. The script's final step is `mv .kanban/2-in-progress/<slug> .kanban/4-done/<slug>` — a **directory** move. `fs.move(src, dst, true)` threw `IOError: ... Could not find file '<dir>'` even though the directory exists: it resolves the path but only handles files.

---

## Problem

`fs.move(src, dst, ...overwrite)` and `fs.copy(src, dst, ...overwrite)` operate on **files only**. There is no directory-capable variant in the `fs` namespace (`fs.createDir` exists for creation; there is no `fs.moveDir`/`fs.copyDir`/recursive form). Moving or copying a directory tree from Stash currently requires shelling out to `mv`/`cp`.

This mirrors the already-known `fs.exists` file-only behavior (use `fs.dirExists` for directories) — the `fs` namespace splits file vs directory operations, but `move`/`copy` only got the file half.

## Reproduction

```stash
fs.createDir("a/b");
fs.move("a/b", "a/c", true);   // IOError: Could not find file 'a/b'
```

## Blast radius

Low for shell-integrated Stash: `$!(mv ${src} ${dst})` is the clean, idiomatic workaround (calling the `mv` tool, not reimplementing it). But it's a papercut for any non-shell or hermetic/embedded context where shelling out isn't available.

## Suggested fix

Either make `fs.move`/`fs.copy` handle directories recursively (most ergonomic), or add explicit `fs.moveDir`/`fs.copyDir` and document the file-only constraint on `fs.move`/`fs.copy` (parallels `fs.exists` vs `fs.dirExists`).

## Verification

A `fs.move` of a directory succeeds (or a new `fs.moveDir` does), and a unit test moves a populated directory tree and asserts contents + absence of the source.

## Related

- Worked around in `scripts/checkpoint/promote-done.stash` via `$!(mv ${src} ${dst})`.
- Sibling file-vs-dir split: `fs.exists` (file) vs `fs.dirExists` (directory).
