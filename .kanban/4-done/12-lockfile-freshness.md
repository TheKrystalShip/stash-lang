# Lock-File Freshness — Detect Orphans and Constraint Mismatch

> **Status:** Backlog
> **Created:** 2026-05-05
> **Priority:** Medium
> **Discovery context:** Stash package registry audit — finding **I1**.

## Background

`IsLockFileUpToDate` (in the install/resolve flow) is supposed to short-circuit `stash pkg install` when nothing has changed since the last install. Today it compares manifest deps to lock entries by **presence only** — if every manifest dep has a lock entry, the lock is declared "up to date". This misses two cases:

- **Orphan lock entries:** A dep was removed from `stash.json` but the lock still has its entry. `IsLockFileUpToDate` reports up-to-date; install skips work; the orphan stays installed forever (clutters `node_modules`-equivalent, contributes to disk usage, may shadow a transitive dep with the same name).
- **Constraint version mismatch:** A dep's version constraint changed in `stash.json` (e.g., `^1.2.0` → `^2.0.0`) but the lock still pins the old in-range version (`1.2.5`). The presence check passes; install does nothing; the user keeps running the old major version despite editing the manifest to ask for the new one.

Both reduce trust in the lock file. The fix is to extend `IsLockFileUpToDate` to compare constraints and to detect orphans.

## Scope

**Files:**
- `Stash.Cli/Pkg/LockFile.cs` (or wherever `IsLockFileUpToDate` lives — likely the resolver).
- `Stash.Cli/Pkg/Commands/InstallCommand.cs` — the call site that decides whether to skip work.

**Changes:**

1. **Detect orphan lock entries:**
   - For each entry in the lock that has no corresponding direct dep in the manifest:
     - If it's a transitive dep of a remaining direct dep, leave it (it'll be reconciled by the resolver).
     - If it's not reachable from any direct dep, it's an orphan — `IsLockFileUpToDate` returns false.
   - Reachability check: walk the dep tree from direct deps via the lock's recorded sub-dependencies.
2. **Detect constraint mismatch:**
   - For each direct dep in the manifest, check that the lock's pinned version satisfies the manifest's current constraint.
   - If not, `IsLockFileUpToDate` returns false.
3. **On stale lock detection in `install` (no args):**
   - Run a full resolve (don't just refuse). Remove orphans from the lock and extracted directory. Re-resolve direct deps whose constraints changed.
   - The user-visible behavior is "install just works after they edit the manifest" — same as `npm install` after `npm uninstall`'s artifact is left behind.
4. **Diagnostic output:**
   - Print a brief summary of what changed: `Removing orphan: foo@1.2.3`, `Updating bar: 1.2.5 → 2.0.1 (constraint changed)`. Stderr; non-fatal informational.

## Acceptance Criteria

- [ ] Removing a dep from `stash.json` and running `stash pkg install` removes the dep from the lock and from the installed packages directory.
- [ ] Changing `^1.2.0` → `^2.0.0` in `stash.json` and running `stash pkg install` updates the installed version to a `2.x` release.
- [ ] If a removed dep is still reachable transitively, it stays in the lock (no false-positive orphan).
- [ ] `IsLockFileUpToDate` returns true when nothing has actually changed (no spurious work).
- [ ] xUnit tests cover: orphan removal, transitive-still-reachable, constraint upgrade, no-op when truly up-to-date.

## Risk / Notes

- Reachability via transitive deps requires the lock to record sub-dependency edges. Confirm the lock format includes this; if not, reading sub-deps from cached tarball manifests is the fallback (slower but correct).
- The "remove orphan from disk" step interacts with whatever extraction layout the project uses. Audit: if extraction is content-addressed (hash-named directories), removal is safe; if it's name-keyed, ensure no other lock entry depends on the same path.

## Out of Scope

- A separate `stash pkg prune` command — fold the behavior into `install` for now.
- Detecting drift in the on-disk extracted files vs. lock (file tampering) — that's spec 03's territory (integrity verification).
- Automatic lock-file format upgrades.
