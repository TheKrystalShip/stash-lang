# Server-Side Correctness — Enforce `private: true` and Use 409 for Version Conflicts

> **Status:** Backlog
> **Created:** 2026-05-05
> **Priority:** High
> **Discovery context:** Stash package registry audit — findings **B5** and **B7**.

## Background

Two server-side correctness fixes that belong together because they're both in `PackageService.Publish` and both are about rejecting bad publish requests with the right status:

- **B5 — `private: true` not enforced server-side.** A package's `stash.json` manifest can declare `"private": true` to mark it un-publishable. The CLI checks this and refuses to publish. But the server does not — it accepts the tarball, extracts the manifest, ignores the field, and publishes. Anyone using `curl` (or a buggy/old CLI) can bypass the intent. The server is the only authoritative enforcement point.
- **B7 — Duplicate version returns 400 instead of 409.** Already covered in spec **02-latest-version-correctness.md** but listed here because it lives in the same method. If spec 02 is done first this is no-op; if this spec is done first, spec 02 should reference the change. Coordination only.

> **Coordination note:** The 409 fix overlaps with spec **02-latest-version-correctness.md**. Whichever lands first should mark the change in this section; the other spec should reference the existing change and not duplicate it.

## Scope

**Files:**
- `Stash.Registry/Services/PackageService.cs` — `Publish` method.
- `Stash.Registry/Controllers/...` (or `Endpoints/...`) — confirm status code mapping if results bubble up via a `Result<T>`-style pattern.

**Changes:**

1. **Reject `private: true` on publish:**
   - In `Publish`, after parsing the tarball's manifest (`stash.json`), check for `private == true`.
   - If true, return `403 Forbidden` with body `{ "error": "private_package", "message": "This package is marked as private and cannot be published." }`.
   - Use 403 (not 400) — the request is well-formed; the server is refusing to fulfill it for policy reasons. (npm uses 402/403 for similar refusals.)
   - Log the rejection at info level with the calling user and package name (helps self-hosters audit attempted bypasses).
2. **Duplicate version returns 409:**
   - Replace the existing 400 status when a (name, version) pair already exists with 409 Conflict, body `{ "error": "version_exists", "message": "Version <X.Y.Z> already exists for package <name>." }`.
   - See spec 02 for the same change — execute once, reference from both.

## Acceptance Criteria

- [ ] Publishing a tarball whose manifest has `"private": true` returns HTTP 403 with the documented body, regardless of which client sent it.
- [ ] The package row is **not** created in the database for a private-rejected publish (no partial state).
- [ ] Server log records the rejection at info level with user and package name.
- [ ] Re-publishing an existing version returns HTTP 409 Conflict with structured error body.
- [ ] xUnit tests cover: publish private → 403, publish non-private → 200, publish duplicate → 409, all three with no DB pollution on rejection paths.

## Risk / Notes

- The CLI already checks `private: true` before sending — that check stays as a fast-fail UX improvement. Server enforcement is the authoritative gate.
- Confirm tarball parsing happens before the DB transaction — if not, the rejection might leave a partial record. Audit before fixing.

## Out of Scope

- Differentiating between accidental and malicious bypass attempts (rate limiting, IP blocking) — separate concern.
- A `private: true` toggle UI in the registry web frontend.
