# `stash pkg outdated` — Query Registry, Don't Just Compare to Manifest

> **Status:** Backlog
> **Created:** 2026-05-05
> **Priority:** High
> **Discovery context:** Stash package registry audit — finding **F7**.

## Background

`stash pkg outdated` is supposed to tell the user which dependencies have a newer published version than what's installed — the same role as `npm outdated`, `cargo outdated`, etc. Today it does something almost useless: it compares the lock file's pinned version to the manifest's version constraint, and reports a dep as "outdated" only when the lock and the manifest disagree. That state is rare (`stash pkg install` keeps them in sync) and has nothing to do with whether a newer version was published upstream.

The user's intent ("is there a newer version available?") requires querying the registry. The data is there: `GET /packages/{name}` returns all versions and the `Latest` pointer. The CLI just doesn't ask.

## Scope

**Files:**
- `Stash.Cli/Pkg/Commands/OutdatedCommand.cs` — full rewrite of the comparison logic.
- `Stash.Cli/Pkg/RegistryClient.cs` — confirm a method exists to fetch package metadata (versions list); use or add.

**Changes:**

1. **New algorithm:**
   - For each direct dependency in `stash.json`:
     - Read the **installed version** from `stash.lock`.
     - Query the registry: `GET /packages/{name}` → versions list and `Latest`.
     - Compute **wanted version**: highest version in the list that satisfies the manifest's constraint (e.g., `^1.2.0` → highest `1.x.y` `>= 1.2.0`).
     - Compute **latest version**: the registry's `Latest` (which after spec 02 lands is the highest stable semver).
     - If `installed != wanted` or `installed != latest`, include in the output.
2. **Output format:**
   - Tabular: `Package`, `Current`, `Wanted`, `Latest` (match `npm outdated` columns).
   - Color/style: `Wanted` highlighted when `Current != Wanted` (in-range update available); `Latest` highlighted when `Wanted != Latest` (out-of-range major update available).
   - Non-TTY output: tab-separated, no color, machine-readable.
3. **Transitive deps:** Direct only by default. Add `--all` later if needed (separate spec). Match npm's default.
4. **Network failure handling:** If the registry is unreachable, exit non-zero with a clear error. Do not silently fall back to the old (useless) behavior.
5. **Performance:** Issue registry queries in parallel (bounded concurrency, e.g., 8 at a time). Direct deps tend to be 5-30; sequential queries make this command slow for no reason.

## Acceptance Criteria

- [ ] With all direct deps at their latest, the command reports nothing and exits 0.
- [ ] With one dep behind a patch release (e.g., `^1.2.0` installed `1.2.0`, registry has `1.2.1`), `Wanted: 1.2.1` is reported.
- [ ] With one dep behind a major release (e.g., `^1.2.0` installed `1.2.5`, registry has `2.0.0`), `Wanted: 1.2.5` and `Latest: 2.0.0` are reported.
- [ ] Network failure exits non-zero with a clear error.
- [ ] Registry queries run in parallel (manual timing test: 10 deps complete in ~1× single-query latency, not 10×).
- [ ] xUnit tests cover the four scenarios above with a mocked registry client.

## Risk / Notes

- Constraint parsing must reuse the existing semver-range matcher used by the resolver — do not reimplement.
- If a dep is in `stash.json` but not in `stash.lock` (manifest edited but no install yet), the command should report it as "missing" or skip with a note — pick one and document.

## Out of Scope

- Auto-updating manifest constraints (`stash pkg update`).
- Transitive dependency reporting (`--all` flag).
- Filtering by package name.
