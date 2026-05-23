# Latest Version Correctness — Highest Semver, Not Most Recent Publish

> **Status:** Backlog
> **Created:** 2026-05-05
> **Priority:** Critical
> **Discovery context:** Stash package registry audit — findings **B3**, **B4**, **B7**.

## Background

The package registry tracks a `Latest` pointer per package, intended to represent the version that `stash pkg install <pkg>` (without a version) and `stash pkg info <pkg>` should resolve to.

Two bugs make `Latest` unreliable:

- **B3 — `Latest` set to most-recently-published, not highest semver.** When publishing version `1.5.0` after `2.0.0` has already been published (e.g., a back-port or hotfix to the older line), `Latest` is overwritten to `1.5.0`. Users installing without a version pin get an older, lower release. The correct behavior is `Latest = max(versions, by: semver)`.
- **B4 — `Latest` not recomputed on unpublish.** When a package version is unpublished/deleted, `Latest` is not re-derived. If the unpublished version was the latest, the `Latest` pointer dangles or points to a now-missing version. It must be recomputed as `max(remaining versions, by: semver)`.
- **B7 — Duplicate version returns 400 instead of 409.** When publishing a version that already exists, `PackageService.Publish` returns `400 Bad Request`. RFC 9110 says the correct status is `409 Conflict` (the request is well-formed but conflicts with current resource state). npm and crates.io both return 409. CLIs that branch on status code (e.g., to retry vs. abort) can't distinguish a duplicate version from a malformed request today.

## Scope

**Files (server side, in `Stash.Registry`):**
- `Stash.Registry/Services/PackageService.cs` — `Publish` and `Unpublish` methods (where `Latest` is currently assigned).
- `Stash.Registry/Database/...` — wherever the `Package` entity stores `Latest` and version list.
- A semver comparator (likely `Stash.Registry/Services/SemverComparer.cs` or in `Stash.Stdlib`'s semver helpers — audit confirmed a comparator exists; reuse it).

**Changes:**

1. **`Publish` — recompute `Latest`:**
   - After the new version is added, compute `Latest = package.Versions.Where(v => !v.Unpublished).Max(by: semver)`.
   - Pre-release versions (e.g. `1.0.0-rc.1`) must not be selected as `Latest` if any non-prerelease version exists. Match npm's `dist-tags.latest` semantics: prereleases are only `Latest` if no stable release exists.
2. **`Unpublish` — recompute `Latest`:**
   - After marking the version as unpublished/removing it, run the same recomputation. If no versions remain, `Latest` is null/empty (and the package row should likely be deleted or marked tombstoned — match existing behavior; do not invent new policy here).
3. **`Publish` — duplicate version returns 409:**
   - In `PackageService.Publish`, the existing-version check returns `400`. Change to `409 Conflict` with body `{ "error": "version_exists", "message": "Version X.Y.Z already exists for package <name>." }`.
   - Update the corresponding controller/endpoint mapping if status is set there (audit if `PackageService` returns a `Result<T>` and the controller maps it to status — both layers may need updating).

## Acceptance Criteria

- [ ] Publishing `1.5.0` after `2.0.0` leaves `Latest = 2.0.0`.
- [ ] Publishing `2.1.0` after `2.0.0` updates `Latest = 2.1.0`.
- [ ] Unpublishing the latest version recomputes `Latest` to the next-highest remaining stable version.
- [ ] Publishing a prerelease (`1.0.0-rc.1`) when no stable exists sets `Latest` to the prerelease; publishing `1.0.0` after promotes `Latest` to the stable.
- [ ] Unpublishing all stable versions promotes the highest remaining prerelease to `Latest` (match npm).
- [ ] Re-publishing an existing version returns HTTP `409 Conflict` with a structured error body.
- [ ] xUnit tests in `Stash.Tests` cover each scenario above against `PackageService` directly.
- [ ] CLI integration tests confirm the 409 path produces a clear "already exists" message rather than a generic "bad request".

## Risk / Notes

- Existing rows in production registries may have a stale `Latest` field. A one-time migration that recomputes `Latest` for every package on startup (or via a `dotnet ef`-style data migration) is recommended but optional — it can also be left to natural recomputation on next publish/unpublish.
- Confirm semver comparator handles `+build` metadata correctly (per semver 2.0.0, build metadata is ignored for ordering).

## Out of Scope

- Custom dist-tags beyond `Latest` (npm-style `next`, `beta`, etc.) — not in this fix.
- Yanking semantics (different from unpublish) — not in this fix.
