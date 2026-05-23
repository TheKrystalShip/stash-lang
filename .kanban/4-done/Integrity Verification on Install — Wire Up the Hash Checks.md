# Integrity Verification on Install — Wire Up the Hash Checks

> **Status:** Backlog
> **Created:** 2026-05-05
> **Priority:** Critical
> **Discovery context:** Stash package registry audit — findings **I3** and **B11**.

## Background

The Stash package toolchain has all the moving parts for tarball integrity verification — but they are not connected to each other. The result is that **no integrity check actually runs** during `stash pkg install`:

- **I3 — `PackageCache.VerifyCache` has no callers.** The method exists, is implemented correctly, hashes the cached tarball, and compares against an expected digest. Grep for `VerifyCache` finds zero call sites in `Stash.Cli/`. Dead code.
- **B11 — `X-Integrity` response header is never read on download.** The registry sets an `X-Integrity: sha256-<base64>` header on the tarball download response. The CLI download path (`RegistryClient.DownloadTarballAsync` or equivalent) never reads it, so the freshly downloaded bytes are never compared to the expected digest.
- **Lock-file integrity is never checked before extraction.** The `stash.lock` file records an `integrity` field per dependency. On install, the CLI extracts the cached tarball without checking that its hash matches the lock-file integrity field. A corrupted or tampered cache file would extract silently.

Together: a tarball can be corrupted in transit, swapped on the cache disk, or substituted by a man-in-the-middle (when a self-hoster runs the registry over plain HTTP) and the CLI will install it.

## Scope

**Files (CLI side, in `Stash.Cli`):**
- `Stash.Cli/Pkg/RegistryClient.cs` — tarball download method. Read the `X-Integrity` response header; verify against expected.
- `Stash.Cli/Pkg/PackageCache.cs` — the existing `VerifyCache` method. Hook it into the install flow.
- `Stash.Cli/Pkg/Commands/InstallCommand.cs` (and dependency-resolver callsite) — call `VerifyCache` before extracting any cached tarball. Compare against the lock-file `integrity` field.

**Changes:**

1. **Download path — verify on receive:**
   - Read `X-Integrity` from the response. Format: `sha256-<base64>` (RFC-style subresource integrity).
   - Hash the downloaded body. Compare. On mismatch: delete the cached file, surface a clear error (`Integrity check failed for <pkg>@<ver>: expected <X>, got <Y>. Refusing to install.`) and exit non-zero.
   - If the header is missing on a download: depending on registry trust policy, either warn or fail. Default: **fail**, with a flag (`--allow-missing-integrity`) for older registries. Audit confirms our registry always emits the header — so missing should be a hard failure.
2. **Cache verification before extraction:**
   - In the install flow, after locating the cached tarball, call `PackageCache.VerifyCache(path, expectedSha)` where `expectedSha` comes from `stash.lock`'s `integrity` field for that dependency.
   - On mismatch: same error path as above (delete cache, refuse to install).
3. **Lock-file integrity propagation:**
   - On a fresh install (no lock entry yet), the lock entry's `integrity` field must be populated from the verified `X-Integrity` value.
   - On a subsequent install with a populated lock, the lock value is the source of truth and must match both the download and the cache.

## Acceptance Criteria

- [ ] `PackageCache.VerifyCache` has at least one production caller in the install flow.
- [ ] Downloading a tarball whose body does not match the `X-Integrity` header fails with a clear error and does not write the cache file.
- [ ] Installing from a cached tarball whose hash does not match the lock-file `integrity` field fails with a clear error and does not extract the tarball.
- [ ] Fresh install populates the lock-file `integrity` field from the verified download.
- [ ] xUnit tests cover: header mismatch, cache mismatch, lock mismatch, missing header, all three matching (happy path).

## Risk / Notes

- Be careful with hash format: pick one canonical encoding (`sha256-<base64>`) and stick to it across server emit, lock file storage, and CLI verification. Audit confirms the registry currently emits base64; the lock file format must agree.
- Streaming hash during download is preferred over re-reading the file from disk (perf).

## Out of Scope

- Signature verification (PGP, sigstore, etc.) — separate spec.
- Mirror/proxy trust policies — separate spec.
