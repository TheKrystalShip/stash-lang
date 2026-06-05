# NoMagicRemoteIpAccessMetaTests — file-level granularity can't guard the metrics-path read

**Status:** Open — accepted limitation (not scheduled)
**Created:** 2026-06-05
**Discovery context:** Surfaced during `registry-download-metrics` (self-hosted-registry P2) autopilot, between phases M2 and M3. The M1 implementer flagged that M3's original `done_when` ("exemption list shrinks to a single entry `IpHasher.cs`") was unsatisfiable; code verification confirmed it and exposed the deeper granularity limitation below. User chose to accept file-level granularity + document (over re-implementing at callsite granularity now).

## Problem

`Stash.Tests/Registry/Configuration/NoMagicRemoteIpAccessMetaTests.cs` is a Roslyn meta-test that
flags any direct read of `HttpContext.Connection.RemoteIpAddress` in `Stash.Registry/` outside an
**exemption list**. The exemption list is **file-level**: the scanner does
`if (AllowedFiles.Contains(rel)) continue;` — it skips the *entire file*.

The registry reads the raw remote IP for **two kinds** of reason:

1. **Download metrics** (the concern this feature governs) — must go through `IIpHasher.Apply(...)`
   so the operator-configured mode (raw|truncated|hashed|off, decision D11) is honored.
2. **Legitimate non-metrics reads** that must keep the *raw* IP — audit logging
   (`AuthController`, `AdminController`, `PackagesController` publish/yank audit, `Startup.cs`
   revoked-token audit), rate-limit keying (`RateLimitingMiddleware`), and authz-deny audit
   (`RegistryAuthorizeFilter`). These deliberately do **not** hash.

`PackagesController.cs` contains **both**: one metrics read (`DownloadVersion`, migrated to
`IIpHasher` in M3) and 8 audit reads (which stay raw). Because the exemption is file-level,
`PackagesController.cs` must remain on the exemption list forever (to allow its 8 audit reads).

**Consequence:** the meta-test can never see a *future* regression that adds a direct raw-IP read
back into the `DownloadVersion` metrics path (or anywhere else in `PackagesController.cs`) — the one
file it most needs to guard is permanently skipped. The guard is real for *new* files, but blind
inside already-exempt files.

## Current mitigation (why this is acceptable today)

The `DownloadVersion` metrics-path IP compliance is proven by a **behavioral** test, not the
meta-test: `Stash.Tests/Registry/Metrics/DownloadCaptureSemanticsTests.cs` asserts that persisted
`download_events.ip` is populated through `IIpHasher` (e.g. with `IpMode = hashed`, the stored value
is a 32-char HMAC hash, never the raw IP string). So the *current* behavior is locked; only *future
regression detection inside exempt files* is the gap.

## Blast radius

Detection-only gap, no runtime defect. Affects future-proofing of the IP-handling invariant inside
`PackagesController.cs` and the other 5 permanently-exempt files. Does not affect any shipped
behavior.

## Suggested fix (if/when closed)

Move the guard from **file-level** to **callsite/marker-level** (this was option (b) at the decision
point): each legitimate raw-IP read carries an explicit inline justification marker
(e.g. `// ip-raw-ok: <reason>`), and the scanner flags any *unmarked* `.RemoteIpAddress` read
anywhere — including inside files that also contain marked reads. That makes the `DownloadVersion`
metrics read genuinely guarded (it carries no marker, so a direct raw read there would fail), while
audit/rate-limit/authz reads are individually justified. Touches the 6 exempt files
(`AuthController`, `AdminController`, `PackagesController`, `RateLimitingMiddleware`,
`RegistryAuthorizeFilter`, `Startup.cs`) to add markers — out of `registry-download-metrics`'s
scope, hence deferred to its own unit.

## Verification (when fixed)

- Self-test: an *unmarked* `.RemoteIpAddress` read inside a file that also has marked reads must trip
  the scanner (proves callsite granularity, not file granularity).
- A marked read does not trip it.
- Binding-floor + file-count floor retained per CLAUDE.md Roslyn-determinism rule.

## Related

- Feature: `registry-download-metrics` (self-hosted-registry milestone P2), phases M1/M3.
- Decision D11 (IP handling operator-configurable, default hashed) — roadmap doc
  `0-backlog/registry/Registry Feature Gaps - Self-Hosted Registry Roadmap.md`.
- Doctrine: CLAUDE.md "Construct > Detect > Instruct" + vacuous-meta-test guard rules.
