# registry-download-metrics — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.

**Scope reviewed:** commits `aaadba66..f0696765` on branch `feature/registry-download-metrics`
**Brief:** ../brief.md (registry-download-metrics, P2 of self-hosted-registry milestone)
**Generated:** 2026-06-05

**Baseline:** `dotnet test` — FAIL (failed=3, passed=13769, skipped=6). The three failures are F01/F02/F03 below.

**Summary:** 7 findings — 1 CRITICAL, 3 IMPORTANT, 3 MINOR.

---

## F01 — [CRITICAL] AuditActions value drift silently changed an audit-log wire contract; breaks existing AuditServiceTests (×2)

**Status:** fixed
**Fixed in:** 69fd790d
**Files:** `Stash.Registry/Services/AuditActions.cs:29-31`, `Stash.Registry/Services/AuditService.cs:110,124`, `Stash.Tests/Registry/AuditServiceTests.cs:39,51,54`
**Phase:** M6
**Commit:** c2515b87

### Observation

M6 introduced `AuditActions` (`Stash.Registry/Services/AuditActions.cs`) to centralize audit-log action strings, then re-pointed `AuditService.LogPublishAsync` / `LogUnpublishAsync` from the prior literals to the new constants:

| Audit action | Pre-feature stored value | Post-feature stored value |
| --- | --- | --- |
| `LogPublishAsync` | `"publish"`  | `"package.publish"` |
| `LogUnpublishAsync` | `"unpublish"` | `"package.unpublish"` |
| (others — `package.deprecate`, `version.deprecate`, etc.) | already dotted | unchanged |

This is **not** a behavior-preserving refactor. The two existing canonical tests in `Stash.Tests/Registry/AuditServiceTests.cs` go red:

- `LogPublish_CreatesEntry` — `Assert.Equal("publish", log.Items[0].Action)` → Expected `"publish"`, Actual `"package.publish"`.
- `LogUnpublish_CreatesEntry` — queries with `action="unpublish"` filter, gets 0 rows; expected 1.

`AuditActions.cs:11-19` justifies "we can rename freely" with this docstring claim:

> *"The constants live in Stash.Registry (server-internal), not in Stash.Registry.Contracts (wire-only), because action strings are never exposed as wire values."*

**That claim is false.** The audit action string is exposed on the wire in **two** places:
1. `AuditEntryResponse.Action` in `Stash.Registry.Contracts/AdminContracts.cs:217` — returned by `GET /api/v1/admin/audit-log`.
2. `AuditLogQuery.action` filter in `Stash.Registry.Contracts/AdminContracts.cs:90` — accepted as a query-string parameter on the same endpoint.

So the M6 refactor is a silent wire-contract change — exactly the failure mode the project's bounded-domains rule guards against ("centralize literals, never silently change a contract"). Registry pre-release status (no deployed data) does NOT excuse a contract drift that breaks already-pinned tests.

### Why this matters

- Hard regression: full `dotnet test` baseline goes red. Blocks `/done`.
- The misleading "never wire-exposed" docstring is what made this look safe — fix the docstring or future centralizations will repeat the mistake.
- A CLI / future Web client filtering audit by `action=publish` or `action=unpublish` would silently get zero rows — a contract regression hidden behind a "cleanup" refactor.

### Suggested fix

**Align the constant VALUES to the prior wire strings**, not the audit-log tests to the new values. The intent of bounded-domains is single-source-of-truth, NOT renaming.

```csharp
// Stash.Registry/Services/AuditActions.cs
public const string PackagePublish   = "publish";    // was "package.publish"
public const string PackageUnpublish = "unpublish";  // was "package.unpublish"
// (PackageCreate stays "package.create" — it is a new value introduced by this feature
//  for the first-publish split in PublishPackage; the brief documents it and
//  AdminController.GetStats counts {PackageCreate, PackagePublish} together for
//  publishesLast24h, so the math holds after this revert.)
```

Also correct the misleading docstring in `AuditActions.cs:11-19`: action strings ARE wire-exposed via `AuditEntryResponse.Action` and `AuditLogQuery.action`. Mention this and add: "changing a constant VALUE is a wire-breaking change; only the name may be refactored freely."

`AdminStatsExpandedTests` keeps passing because it writes and counts symmetrically via the same constants — value choice is irrelevant to that test, so a revert breaks nothing it covers.

### Verify

```
dotnet test --filter "FullyQualifiedName~AuditServiceTests|FullyQualifiedName~AdminStatsExpandedTests"
```

---

## F02 — [IMPORTANT] OpenAPI snapshot is stale (3 new metrics operations not baselined); M6 verify filter omitted OpenApiSnapshotTests

**Status:** open
**Files:** `Stash.Tests/Registry/OpenApi/Snapshots/openapi-v1.json`, `.kanban/2-in-progress/registry-download-metrics/plan.yaml:181`
**Phase:** M6
**Commit:** c2515b87

### Observation

The generated OpenAPI document grew from 91 084 to 101 611 bytes when M6 added the three new operations (`Packages_GetPackageMetrics`, `Packages_GetVersionMetrics`, `Admin_GetDownloadsMetrics`). The committed baseline snapshot at `Stash.Tests/Registry/OpenApi/Snapshots/openapi-v1.json` was NOT regenerated, so `OpenApiSnapshotTests.OpenApiDoc_MatchesBaselineSnapshot` is RED.

Process root cause: `plan.yaml:181` (M6 verify) filter is

```
FullyQualifiedName~DiscoveryEndpointTests|FullyQualifiedName~AdminStatsExpandedTests|FullyQualifiedName~OpenApiCoverageMetaTests|FullyQualifiedName~HttpRegistryClientTests
```

`OpenApiCoverageMetaTests` (which checks every operation has an `operationId` and `$ref` schema) was included, but `OpenApiSnapshotTests` (byte-for-byte fixture diff) was not. Any change to the OpenAPI surface must also re-baseline that snapshot, and the per-phase verify didn't surface the staleness.

### Why this matters

- Full `dotnet test` baseline goes red. Blocks `/done`.
- A stale OpenAPI snapshot is a contract-regression early-warning that this feature has now disarmed.

### Suggested fix

Re-baseline the snapshot (purely mechanical — the new content is the three correctly-shaped operations):

```
STASH_SNAPSHOT_REGEN=1 dotnet test --filter FullyQualifiedName~OpenApiSnapshotTests
```

Then commit the regenerated `Stash.Tests/Registry/OpenApi/Snapshots/openapi-v1.json`. Important: do this AFTER F01 lands — if the audit-log action constant values change (per F01), the OpenAPI enum hints / examples may differ too, and a second regen would be wasted.

(Process note for future briefs: when adding endpoints, M6-style "discovery flag + OpenAPI" phases should include `OpenApiSnapshotTests` in `verify` alongside `OpenApiCoverageMetaTests`. Not a finding to file — flagged here so the next architect catches it.)

### Verify

```
dotnet test --filter "FullyQualifiedName~OpenApiSnapshotTests|FullyQualifiedName~OpenApiCoverageMetaTests"
```

---

## F03 — [IMPORTANT] `RetentionDays=0` does NOT disable raw capture as the config docstring and M4 done_when promise

**Status:** fixed
**Fixed in:** f42eb4b6
**Note:** Resolved as documentation-truth fix. Both docstrings corrected to reflect actual behavior; plan.yaml M4 done_when #4 corrected. The design question (which knob should disable capture) is deferred to `.kanban/0-backlog/registry/registry-metrics-capture-killswitch.md`. No runtime behavior changed.
**Files:** `Stash.Registry/Configuration/MetricsConfig.cs:51-56`, `Stash.Registry/Controllers/PackagesController.cs:472-491`, `Stash.Tests/Registry/Metrics/RetentionSweepTests.cs:93-106`
**Phase:** M3 + M4 (capture path + retention semantics gap)
**Commit:** 98439efc / 6ee722c4

### Observation

Three documents promise that `RetentionDays = 0` disables raw download capture:

- `MetricsConfig.cs:53` (docstring): *"0 disables raw capture entirely."*
- `plan.yaml:140` (M4 done_when #4): *"Retention sweep with RetentionDays=0 is a no-op (raw capture disabled)."*

Actual behavior is the opposite:

- `PackagesController.DownloadVersion` (lines 472-491) registers `Response.OnCompleted(...)` and enqueues a `DownloadEvent` **unconditionally** — there is no check on `Metrics.Enabled` or `Raw.RetentionDays`.
- `MetricsBackgroundService.RunRetentionPassAsync` (line 240) short-circuits to no-op when `RetentionDays <= 0`.
- `RetentionSweepTests.SweepRetentionAsync_RetentionDaysZero_IsNoOp` (line 93-106) seeds 3 events, runs sweep with `RetentionDays=0`, asserts 0 deleted AND `Assert.Equal(3, await _db.DownloadEvents.AsNoTracking().CountAsync())` — i.e. the rows ARE still there, the test only proves the SWEEP doesn't fire. The test does NOT assert that capture was disabled.

So an operator who sets `RetentionDays=0` expecting "don't keep raw download events" actually gets the **worst** outcome: raw events captured AND kept forever (sweep won't delete them, capture wasn't gated). This is a configurable footgun, not a feature.

### Why this matters

- The cross-cutting "Count-on-success semantics" row in the brief's Cross-Cutting Concerns table promises the count rule is enforced "single helper called from exactly one place." That single place (`DownloadVersion`) ignores the operator's disable knob — the enforcement is incomplete.
- For the test suite this is a hidden-correctness gap: M4 done_when #4 was checked off based on the sweep being a no-op, but the COMPOUND invariant (no capture + no sweep) isn't actually upheld.
- For ops, a future privacy-conscious deployment that wants downloads counted but raw rows never persisted has no working configuration — `RetentionDays=0` doesn't do it, and `Metrics.Enabled=false` is read by nothing either (also see suggested fix).

### Suggested fix

One of two corrections — both are small. Prefer **(A)** because it makes the docstring true and the operator footgun goes away:

**(A) Gate capture on the documented disable knob.** In `DownloadVersion`, skip the `Response.OnCompleted` registration when `_metricsConfig.Raw.RetentionDays == 0` OR `_metricsConfig.Enabled == false`. Add a `DownloadCaptureSemanticsTests` row that publishes a package, sets `RetentionDays=0`, performs a successful download, and asserts `download_events` is empty. Also wire `Metrics.Enabled=false` to bypass capture (currently unused — Startup still registers the queue / hosted service / IpHasher).

**(B) Correct the docstring + plan.** If the design choice is genuinely "no kill switch — `RetentionDays=0` means keep raw forever," update `MetricsConfig.cs:53` and `plan.yaml:140` to say "0 disables ONLY the retention sweep; raw events accumulate forever." Then remove the dead `Metrics.Enabled` config knob (or fail-fast on `Enabled=false`) so the surface doesn't lie. Document the upgrade story for an operator who actually wants no raw events.

The architect should pick (A) or (B); the resolver shouldn't choose silently.

### Verify

After (A):

```
dotnet test --filter "FullyQualifiedName~Registry.Metrics.DownloadCaptureSemantics|FullyQualifiedName~Registry.Metrics.RetentionSweep"
```

(With a new test asserting `Captured_RetentionDaysZero_NoEnqueue`.)

---

## F04 — [IMPORTANT] `StatsResponse` omits `packages` and `versions` totals enumerated in the brief Goals

**Status:** open
**Files:** `Stash.Registry.Contracts/AdminContracts.cs:180-208`, `Stash.Registry/Controllers/AdminController.cs:82-114`
**Phase:** M6
**Commit:** c2515b87

### Observation

Brief Goals (line 24) explicitly enumerates the expanded admin-stats set:

> *"Expand GET /api/v1/admin/stats (today returns { users }) to include totals (packages, versions, downloads, storage bytes, recent publish / unpublish / deprecation counts)."*

Brief's JSON example for `StatsResponse` (lines 84-85) shows:

```jsonc
{
  "users": 17,
  "packages": 412,       // ← missing
  "versions": 2189,      // ← missing
  "downloads": { ... },
  "storageBytes": ...,
  "activity": { ... }
}
```

The implementation (`AdminContracts.cs:180-208`) lands `users`, `storageBytes`, `downloads`, `activity` — but NOT `packages` or `versions`. `AdminController.GetStats` (lines 82-114) reads only the present fields.

M6 done_when listed only `downloads`/`activity` and `storageBytes` (in M2), so the per-phase verify passed despite the gap. The brief Goal was, however, not amended.

### Why this matters

- Surface gap from an explicit Goal. The brief's example body — used as the contract test for parity reviews — does not match the actual response.
- Adding two integers later is trivial, but landing it as a follow-up grows `StatsResponse` again post-merge (another wire-DTO change) instead of doing it once now.

### Suggested fix

Add `Packages` (int) and `Versions` (int) fields to `StatsResponse`, populate in `GetStats` via two cheap counts (`COUNT(*) FROM package_records` and `COUNT(*) FROM version_records`). Extend `AdminStatsExpandedTests` with a sub-test that seeds N packages with M versions and asserts both counters round-trip.

### Verify

```
dotnet test --filter "FullyQualifiedName~AdminStatsExpandedTests"
```

---

## F05 — [MINOR] `DownloadRollupDailyRecord` docstring claims it "aggregates hourly rollup rows" but daily rollup is computed from raw events

**Status:** open
**Files:** `Stash.Registry/Database/Models/DownloadRollupDailyRecord.cs:9-13`, `Stash.Registry/Services/Metrics/DownloadMetricsStore.cs:144-196`
**Phase:** M4
**Commit:** 6ee722c4

### Observation

`DownloadRollupDailyRecord.cs:9-13` documents:

> *"Daily rollup rows are permanent (never subject to retention sweeps). The background rollup service (MetricsBackgroundService, landed in M4) aggregates hourly rollup rows into this table as part of the nightly pass."*

But `DownloadMetricsStore.RollupAsync` (lines 144-196) computes daily buckets directly from `closedEvents` (raw `download_events`), NOT by aggregating already-rolled hourly rows. The actual implementation is fine (both produce the same numbers because both feed off the same closed-bucket raw events), and avoiding hourly→daily compaction simplifies idempotency. The docstring just describes a design that was not implemented.

### Why this matters

A reader debugging a daily-rollup discrepancy will look first at the hourly rollup table per the docstring and waste time. Once docs drift, every subsequent reader pays.

There is also a latent interaction with F03: if `Raw.RetentionDays` is set very small (say 1 day) AND the daily rollup is run after the retention sweep on a given day, raw events for *yesterday* could be swept before the daily rollup walks them. Today the default ordering (rollup hourly @ 60min, retention nightly + `RetentionDays=30`) makes this safe; a future operator tightening `RetentionDays` could hit it. Worth a comment near the rollup loop too.

### Suggested fix

Two-line docstring fix in `DownloadRollupDailyRecord.cs`:

> *"Daily rollup rows are permanent (never subject to retention sweeps). The background rollup service aggregates raw `download_events` (closed-day buckets, `ts < currentDayStart`) directly into this table; daily rows are NOT recomputed from hourly rows — both daily and hourly come from the same raw closed-bucket projection."*

Optionally add a 2-line comment near `DownloadMetricsStore.cs:148` noting the daily-vs-retention ordering invariant and pinning it in plan/test if F03 is fixed via (A).

### Verify

```
dotnet build Stash.Registry
```

(No runtime behavior change; pure doc fix.)

---

## F06 — [MINOR] Brief wording "404 body must NOT contain the package name" conflicts with the registry's indistinguishability invariant — reconcile the brief, not the code

**Status:** open
**Files:** `.kanban/2-in-progress/registry-download-metrics/brief.md:179`, `Stash.Tests/Registry/Metrics/MetricsVisibilityBehaviorTests.cs:170-178`
**Phase:** M5 (and brief)
**Commit:** 9e496091

### Observation

Brief AC §3 (line 179) says:

> *"Anonymous GET /api/v1/packages/@a/private/metrics returns 404 (not 403, not 200, body does not contain the package name in any field). A token with read access returns 200."*

The implementation routes through the existing shared `RegistryAuthorizeFilter` (`Stash.Registry/Auth/Authorization/RegistryAuthorizeFilter.cs:95-106`), which renders `VisibilityHidden → 404` with body `{"error": "Package '@a/private' not found."}` — i.e. the body DOES contain the package name. The implementer correctly reasoned that the registry-wide invariant is **byte-indistinguishability** between hidden and genuinely-missing packages (see the pre-existing `VersionDenyBodyShape_Matrix` test at `RegistryAuthzMatrixTests.cs:1015`, which pins the version-scoped name-bearing error body). Diverging just for metrics would CREATE a leak (`/metrics` for `@x/secret` returning a name-less 404 distinguishes "exists but hidden" from a nonexistent-package response that echoes the name).

`MetricsVisibilityBehaviorTests.cs:170-178` correctly documents this reconciliation:

> *"the brief's literal phrase 'body must NOT contain the package name' is superseded by this stronger indistinguishability property. Literally removing the name from the hidden-package body is out of M5 scope (it would break the pinned VersionDenyBodyShape_Matrix test) ..."*

This is a brief-wording defect, not a code defect. Filing it MINOR so the next reader of the brief doesn't re-open the question.

### Why this matters

The brief is the long-lived spec. Leaving the over-specified clause in place is a future re-open trap (a later reviewer will read the brief and think the registry leaks existence on `/metrics`). The implementer's reconciliation is buried in code comments and won't be re-discovered by a brief-only reader.

### Suggested fix

Amend `brief.md:179` to:

> *"Anonymous GET /api/v1/packages/@a/private/metrics returns **404 with a body byte-indistinguishable from a genuinely-missing package** (not 403, not 200). The shared `RegistryAuthorizeFilter` enforces this via the same `VisibilityHidden → 404` rendering used by `GetPackage` / `GetVersions` / `GetReadme` — see `VersionDenyBodyShape_Matrix`."*

Also amend the brief's "Visibility predicate (D9)" paragraph (~line 136) to make explicit that the invariant is indistinguishability, not literal name-omission.

### Verify

```
dotnet test --filter "FullyQualifiedName~MetricsVisibilityBehaviorTests|FullyQualifiedName~VersionDenyBodyShape_Matrix"
```

(Behavior unchanged; brief edit only.)

---

## F07 — [MINOR] AC9 (hot-path latency variance test) has no corresponding test artifact

**Status:** open
**Files:** `.kanban/2-in-progress/registry-download-metrics/brief.md:185` (AC9), `Stash.Tests/Registry/Metrics/DownloadCaptureSemanticsTests.cs`
**Phase:** M3 / cross-phase
**Commit:** 98439efc

### Observation

Brief AC §9 (line 185) says:

> *"Hot-path latency. A unit test wraps DownloadVersion and asserts that across N=100 iterations the per-call wall-clock variance does not regress more than 10% when metrics are enabled vs disabled (no synchronous DB call on the hot path)."*

A grep across `Stash.Tests/Registry/Metrics/` returns no test asserting wall-clock variance or before/after timing comparison. The architectural intent (no synchronous DB write on the hot path) IS proven structurally:

- `DownloadVersion` (lines 472-491) only registers `Response.OnCompleted` + enqueues to a `Channel`; no `await _db.SaveChangesAsync(...)` on the request thread.
- `DownloadCaptureSemanticsTests` asserts the count semantics indirectly verify the enqueue path is non-blocking (events arrive AFTER drain ticks).
- `MetricsBackgroundService` opens its own DI scope, draining off-request.

So the *invariant* AC9 cares about is met. The brief asks for a wall-clock variance test on top — which the project has historically avoided because such tests are flaky under CI load. Filing this MINOR so the architect can either (a) explicitly accept the structural proof and amend AC9, or (b) add a deterministic micro-benchmark.

### Why this matters

- AC9 is technically unverified by an explicit assertion; a future regression that quietly synchronizes the hot path would be caught only by perf regression, not CI.
- Wall-clock variance tests are flaky; a deterministic "no `await _db.*` inside `DownloadVersion`" Roslyn meta-test (analogous to `NoMagicRemoteIpAccessMetaTests`) would be a more reliable Construct-or-Detect guard.

### Suggested fix

Pick one:

- **(A) Amend AC9** to: *"Hot-path latency is structurally guaranteed: `DownloadVersion` performs no `await` on `IRegistryDatabase` / `DbContext` after the visibility check; enqueue is a non-blocking `Channel.TryWrite`. Asserted by `DownloadCaptureSemanticsTests` (count semantics) + a one-shot Roslyn meta-test `NoSyncDbWriteInDownloadVersionMetaTests` that flags any `await _db.*` / `await ctx.SaveChanges*` reaching the `DownloadVersion` method body."* Then write the meta-test.
- **(B) Keep AC9 as written** and add a `[Trait("Category","Perf")]` test that runs N=100 iterations and compares medians (not means, to ride out CI noise) with a 25% slack, not 10%, and gate it behind an env flag so CI skips it.

The architect should pick.

### Verify

After (A):

```
dotnet test --filter "FullyQualifiedName~NoSyncDbWriteInDownloadVersionMetaTests|FullyQualifiedName~DownloadCaptureSemantics"
```

---

## Notes / non-findings (acknowledged, NOT to be filed)

The following were flagged in the review prompt as items to verify; each was confirmed acceptable and is documented here so a re-reviewer doesn't re-open them:

- **M2 `VersionRecord.StorageBytes = tarballBytes.LongLength` (NOT `IPackageStorage.GetSize`).** Acceptable. `_storage.StoreAsync` runs AFTER the `VersionRecord` is constructed (`PackageService.cs:204-232`), so calling `GetSize` at that point is impossible without re-reading the file the publish path just wrote. Both values are identical for `FileSystemStorage` (no compression). This is a better implementation than the brief literally specifies; D10's invariant ("real persisted column written at publish, not a filesystem stat at read") is met.
- **Spec correction `fabbe92d` (M3 done_when #6 file-level granularity).** Verified: `NoMagicRemoteIpAccessMetaTests` is GREEN with the documented 7-entry permanent exemption list (`IpHasher.cs` + 6 non-metrics readers); no `Services/Metrics/*.cs` file reads `RemoteIpAddress` directly; download capture compliance is proven by `DownloadCaptureSemanticsTests`. The file-level limitation is documented in `.kanban/0-backlog/bugs/registry-no-magic-ip-file-level-granularity.md` and is a user-accepted residual.
- **M6 operationId naming: `Packages_GetPackageMetrics` vs brief's `Packages_GetMetrics`.** Acceptable. The operationId is auto-derived `{Controller}_{Action}` and the controller method is `GetPackageMetrics`; the brief's `Packages_GetMetrics` was an early-draft name. `OpenApiCoverageMetaTests` is structurally green; the rendered name appears in `docs/Registry — Package Registry.md:1866-1867` and is the public-facing one going forward.
- **Visibility predicate, non-blocking hot path, count-on-completion, rollup closed-bucket idempotency, IP-hasher persistence, discovery flag flip truthfulness.** All inspected, all consistent with the brief. The shared `RegistryAuthorizeFilter` resolves visibility before `DownloadVersion` runs; `MetricsBackgroundService` opens its own DI scope per pass; rollup uses a `(package, version, bucket)` skip-set for idempotency; secret is persisted to `<db-dir>/metrics-ip-secret.bin`; `DiscoveryFeatures.Metrics = true`.
