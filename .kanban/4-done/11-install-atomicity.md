# `stash pkg install <dep>` — Atomicity, Don't Mutate Manifest Before Success

> **Status:** Backlog
> **Created:** 2026-05-05
> **Priority:** Medium
> **Discovery context:** Stash package registry audit — finding **F2**.

## Background

`stash pkg install <dep>` (the form that adds a new dependency, not the form that installs from an existing manifest) currently does this:

1. Parse `<dep>`, resolve to a version constraint.
2. **Write the new dep into `stash.json`.**
3. Run dependency resolution.
4. Download and extract.
5. Update `stash.lock`.

If any of steps 3-5 fails (network down, registry returns 5xx, integrity mismatch, disk full, missing peer dep, conflicting version constraint), the manifest is already mutated. The user is left with a `stash.json` that lists a dep that isn't installed and isn't in the lock. The next `stash pkg install` (no args) tries to install the broken state, fails the same way, and the user has to manually edit `stash.json` to recover.

The fix: either (a) defer the manifest write until after the install succeeds, or (b) snapshot the manifest before mutating and roll back on failure.

## Scope

**Files:**
- `Stash.Cli/Pkg/Commands/InstallCommand.cs` — the `install <dep>` code path.

**Changes:**

Pick one of two approaches. **Recommended: Option A (defer)** — simpler, no rollback bookkeeping, no risk of leaving a `.bak` file on crash.

### Option A — Defer manifest write (RECOMMENDED)

1. Parse `<dep>` and compute the new manifest **in memory**.
2. Run resolution / download / extract / lock-file update against the in-memory manifest.
3. Only after all of the above succeed, write the manifest to disk.

This requires the resolver and downloader to accept a manifest object, not always read from disk. Audit the call signatures — likely already takes a manifest object since `install` (no args) reads from disk and passes it on.

### Option B — Snapshot and rollback (FALLBACK)

1. Read `stash.json` into memory before mutation.
2. Write the new dep to `stash.json`.
3. On any failure in steps 3-5 of the original flow, rewrite the original snapshot back to `stash.json` and surface the error.
4. Use a `try/finally` or explicit error-path to guarantee the rollback runs even on Ctrl-C / process termination — caveat: SIGKILL won't run finalizers; this is a known gap.

### Concurrent install safety

The audit did not flag this, but document it: if two `stash pkg install` invocations run simultaneously against the same project, both will read the same manifest and one will overwrite the other's write. A file lock (`.stash.lock` sentinel file or OS file lock) on the manifest is out of scope for this spec but should be a follow-up.

## Acceptance Criteria

- [ ] After a failed `stash pkg install <dep>` (force a failure with an unreachable registry or a non-existent package), `stash.json` is byte-identical to its pre-command state.
- [ ] After a successful `stash pkg install <dep>`, `stash.json` contains the new dep.
- [ ] `stash.lock` is consistent with `stash.json` in both success and failure cases (no lock entry without a manifest entry; no manifest entry without a lock entry on success).
- [ ] xUnit tests cover: install success, install failure due to unreachable registry, install failure due to missing package, install failure due to integrity mismatch — all four leave `stash.json` consistent with reality.

## Risk / Notes

- If extraction succeeds but lock-file write fails (very rare — disk full mid-write), the package files are on disk but not recorded. Decide policy: orphaned `node_modules`-style cleanup is out of scope; a follow-up `stash pkg verify` command can detect drift.
- Option A is preferred but requires confirming the resolver's call surface accepts an in-memory manifest. If that refactor is large, fall back to Option B.

## Out of Scope

- Concurrent install safety / file locking — separate spec.
- Atomic file replacement (`File.Replace`) for the manifest write — implementation detail; pick whatever is portable.
- The `install` (no args) form — that one has no manifest mutation, only lock updates, and is already reasonably atomic.
