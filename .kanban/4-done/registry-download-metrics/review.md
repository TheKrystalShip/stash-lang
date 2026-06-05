# registry-download-metrics — Review (pass 2)

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
>
> **Finding `**Status:**` lifecycle** (the promotion gate enforces this — see `promote-gate.stash`):
> - `open` — not yet addressed. **Blocks `/done`.**
> - `fixed` — resolved in code; carries a `**Fixed in:** <sha>` line. Set by `/resolve`.
> - `accepted` — a deliberate, human-recorded decision to ship without fixing. Set ONLY by a human
>   via `/accept <feature> <Fxx> <reason>`. Requires an `**Accepted because:** <reason>` line, and a
>   backlog stub for any deferred work. **CRITICAL findings can NEVER be `accepted`** — they must be
>   fixed or the run stops. The autopilot never self-accepts.
> - Any other value (typos, `wontfix`, …) is rejected by the gate — it fails closed.

**Scope reviewed:** commits `aaadba6612f75e65c68c0a877190b6c0082af913..7ca0230c6062a129f83dcdeb57798a04314fe35e` on branch `feature/registry-download-metrics`
**Brief:** ../brief.md (registry-download-metrics, P2 of self-hosted-registry milestone)
**Generated:** 2026-06-05 (pass 2 — re-review of the seven pass-1 findings' fixes + one pass-2 follow-up)

**Baseline:** `dotnet test` — **PASS** (failed=0, passed=13773, skipped=6, total=13779). All three original reds (F01, F02, F03) and the pass-2 audit-contract regression introduced by F01's single-constant revert are resolved.

**Summary:** **0 findings.** Clean re-review.

---

## Verdict

All seven pass-1 findings and the one pass-2 audit-contract regression are correctly resolved with no
new regression introduced by the fixes. Detailed pass-2 verification follows.

### F01 — split is sound (CRITICAL → fixed)

- `AuditActions.cs` (post-`04402e48`) carries four constants with a clear taxonomy:
  - `PackagePublish = "package.publish"` and `PackageUnpublish = "package.unpublish"` — real
    controller mutation audit actions, written by `PackagesController.PublishPackage` /
    `UnpublishVersion` and counted by `AdminController.GetStats`.
  - `Publish = "publish"` and `Unpublish = "unpublish"` — vestigial test-only helper values
    emitted by `AuditService.LogPublishAsync` / `LogUnpublishAsync`; verified to have **no
    production caller** (`grep` shows only `AuditServiceTests` invokes them).
- **Write↔count agreement (verified):** `PackagesController` writes via
  `AuditActions.PackagePublish` / `PackageUnpublish` (PackagesController.cs:536, 580);
  `AdminController.GetStats` (AdminController.cs:96, 99) counts those exact same constants
  for `publishesLast24h` / `unpublishesLast24h`. Cannot drift.
- **Wire-contract pinning still asserted:** `RegistryAuthzAuditMutationTests.cs:38, 60` asserts
  `e.Action == "package.create" || e.Action == "package.publish"` against real controller calls
  — still satisfied.
- **Docstring on the constants class (`AuditActions.cs:5-26`) explicitly names the wire-exposure
  surface** (`AuditEntryResponse.Action`, `AuditLogQuery.action`) and the rule "constant NAME may
  be renamed, constant VALUE is wire-breaking." This neutralises the original misleading "never
  wire-exposed" claim that enabled the F01 defect to ship.

### F02 — snapshot consistent with final code (IMPORTANT → fixed)

- `Stash.Tests/Registry/OpenApi/Snapshots/openapi-v1.json` carries all three new operations
  (`Admin_GetDownloadsMetrics:407`, `Packages_GetPackageMetrics:1610`,
  `Packages_GetVersionMetrics:1658`).
- **No audit-action literal is baked into the snapshot.** The only `"publish"` occurrence is the
  pre-existing `TokenScopes` enum value (line 4108) — a wire **scope**, not an audit action — so
  F01's value-split could not have re-drifted the snapshot. Confirmed by direct text scan:
  `grep "package.publish\|package.unpublish\|\"publish\"\|\"unpublish\""` returns only the
  TokenScopes occurrence.

### F03 — docs corrected, design deferred (IMPORTANT → fixed)

- `Stash.Registry/Configuration/MetricsConfig.cs:51-66` now states explicitly: "A value of 0 (or
  negative) disables ONLY the nightly retention sweep — raw events are still captured… It does
  NOT disable capture." No remaining false "0 disables capture" claim anywhere in the source.
- `Enabled` property (line 11-19) is now documented as `RESERVED — intended master on/off switch …
  NOT YET WIRED: no code reads this property` with a pointer to the deferred backlog
  `registry-metrics-capture-killswitch.md`.
- Brief `appsettings.json` JSON-with-comments snippet (`brief.md:102`) aligned via `bbf0c916`.
- `RetentionSweepTests.SweepRetentionAsync_RetentionDaysZero_IsNoOp` (line 93-106) asserts the
  documented behavior (no rows deleted, all 3 seeded events still present).

### F04 — `packages`/`versions` totals shipped (IMPORTANT → fixed)

- `StatsResponse` (AdminContracts.cs:181-217) carries `Packages` (int) and `Versions` (int)
  fields with `[JsonPropertyName]` matching the brief's JSON example.
- `AdminController.GetStats` populates them via two cheap `CountAsync` calls
  (`CountAllPackagesAsync` / `CountAllVersionsAsync`, StashRegistryDatabase.cs:807, 813).
- **No EF entity leak to the wire DTO** — `StatsResponse` exposes only primitives
  (`int`, `long`) and two nested DTO types (`AdminDownloadsSummary`, `AdminActivitySummary`)
  also declared in the wire-only Contracts assembly.

### F05 / F06 — docstring + brief reconciled (MINOR → fixed)

- `DownloadRollupDailyRecord.cs:9-15` accurately describes the implementation: "aggregates raw
  `download_events` (closed-day buckets) directly… daily rows are NOT recomputed from hourly
  rows — both daily and hourly come from the same raw closed-bucket projection."
- Brief AC3 (line 179) and the "Visibility predicate (D9)" paragraph (line 136) explicitly state
  the byte-indistinguishability invariant, citing the shared `RegistryAuthorizeFilter` and the
  pinning test `VersionDenyBodyShape_Matrix`.

### F07 — AC9 honest about structural guarantee (MINOR → fixed)

- Brief AC9 (line 185) rewritten to accurately describe what is structurally verified:
  `DownloadVersion` registers `Response.OnCompleted` + `Channel.TryWrite` only; the actual
  `download_events` write is off-request in `MetricsBackgroundService` (own DI scope via
  `IServiceScopeFactory`). Cites the two existing test classes that together verify the contract:
  `DownloadCaptureSemanticsTests` (request-path enqueue with `FakeDownloadEventQueue`) +
  `MetricsBackgroundServiceTests` (`DrainBatchAsync` writes happen in the background scope).

### Independent re-review of pass-1 priorities — no new issues

- **Bounded domains across new code:** `IpHandlingMode` is a real C# `enum`;
  `DownloadEventStatus` is unused in this slice (counts use `bytes_served`); `AuditActions` now
  models the bounded set with clear write/count agreement; time-window labels are response field
  names (no parameter-side closed set to enum). `NoMagicAuthStringsMetaTests` and
  `NoMagicRemoteIpAccessMetaTests` both reported green in the baseline.
- **Hot-path: no synchronous DB call.** `PackagesController.DownloadVersion` (lines 472-491)
  registers `Response.OnCompleted` + non-blocking `Channel.TryWrite` only.
- **IP transform routed through `IIpHasher` on the download path** (PackagesController.cs:459).
  Other `RemoteIpAddress` reads in `PackagesController.cs` are pre-existing audit-path reads;
  `PackagesController.cs` is in the documented permanent-exempt list (file-level granularity
  limitation accepted, see `.kanban/0-backlog/bugs/registry-no-magic-ip-file-level-granularity.md`).
- **404 indistinguishability** is enforced by the shared `RegistryAuthorizeFilter`, asserted by
  `MetricsVisibilityBehaviorTests` and `VersionDenyBodyShape_Matrix`.
- **Background service own DI scope** — `MetricsBackgroundService` opens its own
  `IServiceScopeFactory` scope per drain pass.
- **Count-on-completion** — single `OnCompleted` callback gated on `StatusCode == 200OK &&
  !RequestAborted.IsCancellationRequested`.
- **Rollup closed-bucket-only + idempotency** — `DownloadMetricsStore.RollupAsync` skips the
  current open bucket and uses a `(package, version, bucket)` skip-set.
- **Retention sweep** behaves as documented (positive `RetentionDays`: deletes; `≤ 0`: no-op).
- **Wire DTOs in Contracts only** — `StatsResponse`, `AdminDownloadsSummary`,
  `AdminActivitySummary`, `PackageMetricsResponse`, `VersionMetricsResponse`,
  `TopPackageDownloadsEntry` all live in `Stash.Registry.Contracts`.
- **Discovery flag** — `DiscoveryEndpoint.cs:62` carries `Metrics = true`.
- **NoMagicRemoteIpAccessMetaTests file-level granularity** is the already-accepted residual —
  not re-filed (per prompt §7).

### Out-of-scope observation filed as backlog (not in review.md)

- `Stash.Registry.Contracts/AdminContracts.cs:226` — `AuditEntryResponse.Action`'s XML docstring
  lists stale example values `"publish"`, `"unpublish"` (the helper-only values), instead of the
  real wire-contract `"package.publish"`, `"package.unpublish"`. The docstring **predates this
  feature** (`git log -L` shows it last touched by `b036d653` shared-contracts extraction; not
  modified by any commit in the feature range). Per the reviewer hard rule, filed as
  `.kanban/0-backlog/bugs/registry-audit-entry-response-action-docstring-drift.md` rather than
  as a finding in this review.
