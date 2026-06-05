# RFC: Registry Download Metrics — Counting, Usage API, and Expanded Admin Stats

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-06-05
> **Slug:** registry-download-metrics
> **Milestone:** self-hosted-registry

## Summary

Add download-usage observability to the Stash package registry: count every successful tarball download non-blockingly, persist a short-retention raw event stream plus permanent daily/hourly rollups, expose package- and version-level metrics through new visibility-gated endpoints on the scoped routes, and broaden the admin surface (`GET /admin/stats` — currently `{ users }` only — and a new `GET /admin/metrics/downloads`) with storage-bytes, publish/unpublish activity, and top-packages. Introduce the operator-configurable IP-handling pipeline (`raw|truncated|hashed|off`, default `hashed`) that audit-log-v2 will later reuse. Flip the `Metrics` discovery feature flag from `false` to `true`.

This is the **P2 unit of the `self-hosted-registry` milestone** (P1 — orgs/scopes/visibility — already shipped). It is explicitly scoped to backend metrics: search ranking, audit-log-v2, trust signals, advisories, and provenance are separate, later units and are *non-goals* here.

## Motivation

Package authors need adoption signals; registry operators need traffic and storage insight; users want popularity/freshness cues during discovery. Today the registry stores `PackageRecord`/`VersionRecord` rows and nothing else — `GET /api/v1/admin/stats` returns only `{ users }`; the download endpoint counts nothing. The roadmap's Gap §1 names the full capability set; the **Locked Decisions (2026-05-29) addendum** picks the design (D8 raw+rollup, D9 visibility from day one, D10 storage-bytes from a DB column, D11 operator-configurable IP, default hashed). This feature lands those decisions on the live scoped routes.

## Goals

- Count every **successfully-served** download (`200`, full stream) — exactly once per download, off the request's critical path.
- Persist raw download events (short retention, e.g. 30–90d) **and** permanent daily/hourly rollups, with a background retention/rollup service (D8).
- Expose **package-level** and **version-level** metrics endpoints on the scoped routes (`GET /api/v1/packages/{scope}/{name}/metrics`, `GET /api/v1/packages/{scope}/{name}/{version}/metrics`), each carrying the existing PDP-backed visibility predicate (D9).
- Expand `GET /api/v1/admin/stats` (today returns `{ users }`) to include totals (packages, versions, downloads, storage bytes, recent publish / unpublish / deprecation counts).
- Add `GET /api/v1/admin/metrics/downloads` returning top-packages-by-downloads in the shared `PagedResponse<T>` envelope.
- Persist `storage_bytes` as a column on `VersionRecord`, written by the publish path, summed for admin stats (D10).
- Introduce `Configuration/MetricsConfig.cs` with the operator-configurable IP-handling mode (`raw|truncated|hashed|off`, default `hashed`); persist the HMAC secret across restarts; reusable by audit-log-v2 (D11).
- Flip `DiscoveryFeatures.Metrics` from `false` to `true` and re-baseline the discovery snapshot tests.
- Add OpenAPI coverage for every new endpoint; every new wire DTO lives in `Stash.Registry.Contracts`.

## Non-Goals

- **Search ranking / sort-by-downloads.** Gap §1's "add metrics to search ranking" line is a Bucket-B *search* straddle that the registry website readiness doc warns lands "half-impossible." Defer; do **not** add `downloads` as a search sort/filter, and do **not** add a `downloads` column to `SearchResult`.
- **Trust signals** (`Vulnerable`, `Verified`, `Provenance`, `Signature` flags) — separate milestone units.
- **Audit Log v2.** This feature only *introduces* the IP-handling config that v2 will reuse; it does not touch `AuditService.GetAuditLogAsync`, action coverage, export, retention, or tamper-evidence.
- **Trusted publishing, advisories, dist-tags, lifecycle states** — later P-phases of this milestone.
- **Downloads in the audit log.** Per D13, downloads stay out of audit; metrics are a separate surface.
- **Legacy / backfill / data migration.** The registry is pre-release (no deployed instance, no existing data); design for the clean forward case.
- **Metadata denormalization.** `PackageSummaryResponse` / `PackageDetailResponse` do **not** receive a `downloads` field in this feature — metrics live on the dedicated endpoints. Revisit only if the front-end discovery list demonstrates the round-trip is genuinely expensive in practice.
- **External sinks** (webhooks, OTLP, syslog) for download events — out of scope; audit-log-v2 owns that conversation.

## Design

### Surface

**New endpoints:**

| Method | Path | Auth class | Auth action | Response |
| --- | --- | --- | --- | --- |
| GET | `/api/v1/packages/{scope}/{name}/metrics` | `[PublicEndpoint]` + `[RegistryAuthorize(RegistryAction.ReadPackageMetadata)]` | reused | `PackageMetricsResponse` |
| GET | `/api/v1/packages/{scope}/{name}/{version}/metrics` | `[PublicEndpoint]` + `[RegistryAuthorize(RegistryAction.ReadPackageVersion)]` | reused | `VersionMetricsResponse` |
| GET | `/api/v1/admin/metrics/downloads` | `[Authorize]` + `[RegistryAuthorize(RegistryAction.ReadAdminStats)]` | reused | `PagedResponse<TopPackageDownloadsEntry>` |

**Modified endpoints:**

- `GET /api/v1/admin/stats` — `StatsResponse` widens from `{ users }` to include `packages`, `versions`, `downloadsTotal`, `downloadsLast24h`, `storageBytes`, `publishesLast24h`, `unpublishesLast24h`, `deprecationsLast24h`.
- `GET /api/v1/packages/{scope}/{name}/{version}/download` — unchanged response shape; gains a single best-effort event-enqueue at successful response completion.
- `GET /api/v1/.well-known/registry` — `features.metrics` flips `false → true`.

**Response shape — one object, all windows, no `?window=` discriminator.** Bounded-domain classification: the *set* of windows (`total`, `last24h`, `last7d`, `last30d`) is closed, but they appear as fixed response *fields*, not as a parameter — there is **no parameter-side bounded domain to enum** for this feature.

```jsonc
// PackageMetricsResponse
{
  "package": "@scope/name",
  "downloads": { "total": 1234567, "last24h": 4321, "last7d": 30210, "last30d": 102003 },
  "perVersion": [
    { "version": "1.2.3", "total": 100200, "last24h": 800, "last7d": 5600, "last30d": 24100 }
  ],
  "generatedAt": "2026-06-05T12:00:00Z"
}

// VersionMetricsResponse
{
  "package": "@scope/name",
  "version": "1.2.3",
  "downloads": { "total": 100200, "last24h": 800, "last7d": 5600, "last30d": 24100 },
  "generatedAt": "2026-06-05T12:00:00Z"
}

// StatsResponse (expanded)
{
  "users": 17,
  "packages": 412,
  "versions": 2189,
  "downloads": { "total": 9123456, "last24h": 4321 },
  "storageBytes": 8123456789,
  "activity": { "publishesLast24h": 12, "unpublishesLast24h": 1, "deprecationsLast24h": 0 }
}

// PagedResponse<TopPackageDownloadsEntry>.items[i]
{ "package": "@scope/name", "downloads": 100200, "windowDays": 7 }
```

**New configuration (`appsettings.json` → `Configuration/MetricsConfig.cs`):**

```jsonc
"Metrics": {
  "Enabled": true,
  "IpMode": "hashed",              // raw | truncated | hashed | off  (D11)
  "IpHashSecret": "<base64>",      // operator-supplied; required when IpMode = hashed (warn + persist auto-gen if missing)
  "Raw": { "RetentionDays": 30 },  // 0 disables raw capture
  "Rollup": { "IntervalMinutes": 60 }
}
```

The IP-mode value is a **server-side, non-wire** closed set. It lives as a real C# `enum IpHandlingMode` in `Stash.Registry/Configuration/` (NOT in `Stash.Registry.Contracts`, which is wire-only), with a `JsonStringEnumConverter` for the config binder. The name is `IpHandlingMode` (not `MetricsIpMode`) so audit-log-v2 can reuse it without rename.

### Semantics

**Count exactly the successfully-served downloads.** A download event is enqueued **only when** the response writes to completion with status `200`. Cases excluded:
- `404 Not Found` (package/version missing, or visibility-hidden — these never reach the streaming path).
- `304 Not Modified` (download endpoint does not emit 304 today, but the rule is forward-stated).
- Client disconnect / write failure mid-stream (no enqueue).

This single rule reconciles the constraint pair: it guarantees `bytes_served` is a real count, and it is what makes "downloads today/7d/30d" comparable to "storage bytes." We avoid `OnStarting` because it fires before bytes flow.

**Off the request critical path.** The download handler calls a synchronous `IDownloadEventQueue.Enqueue(...)` (in-process bounded `Channel<DownloadEvent>`) at response completion. A single `BackgroundService` (`MetricsBackgroundService`) drains the channel into the `download_events` table in batches. The background service is the **canonical answer** to the "reuse retention job" constraint — the registry has no existing `IHostedService` (verified by grep); this feature introduces the pattern, and the same service hosts the rollup + retention sweeps. **Trap pinned**: off-request writers cannot use the request's scoped `DbContext` (disposed). The background service opens its own DI scope per drain cycle; the architect's reviewer must reject any `Task.Run(() => _db.Insert(...))` shortcut.

**Raw vs rollup read model.** The metrics endpoints read **only rollups**:
- **Rollup buckets are authoritative** for all closed time windows.
- The **current open bucket** (e.g. the in-progress hour) is computed from raw on-demand and added to the closed-bucket sum.
- Raw retention defaults to `30d`; rollups are permanent.
- "Downloads today" tolerates ≤ `Rollup.IntervalMinutes` staleness for the current bucket and is exact for all closed buckets.

This rule is stated **once, here**; later phases (rollup job, metrics endpoint, admin stats) all implement it identically.

**Storage bytes (D10) — write at publish, read on demand, NOT a filesystem stat.** A new column `version_records.storage_bytes BIGINT NOT NULL` is populated in `PackageService.PublishAsync` from `IPackageStorage.GetSize(packageName, version)` (which returns the tarball size after `Store`). `GET /admin/stats` `storageBytes` field is `SELECT SUM(storage_bytes) FROM version_records`. **Trap pinned**: a phase that only adds the column + the SUM read passes its `done_when` with `storageBytes = 0` because the publish-path write was forgotten; the storage-bytes phase MUST cover both write and read in one phase, with an end-to-end `done_when` that publishes a real package and asserts `storageBytes ≥ that.size`.

**IP handling (D11) — applied at capture time.**
- `raw` — `HttpContext.Connection.RemoteIpAddress.ToString()` verbatim.
- `truncated` — `/24` for IPv4, `/64` for IPv6.
- `hashed` (default) — `Convert.ToHexString(HMACSHA256(secret, raw_ip)).Substring(0, 32)`. The HMAC secret **MUST persist across restarts** (do NOT mimic the JWT auto-regenerate-on-missing fallback — that breaks hash correlation). If missing on startup, generate once, write back to disk, and log a warning naming the file written. Reused by audit-log-v2.
- `off` — store `NULL`.

**Visibility predicate (D9) — reused, never re-invented.** Both new metrics read endpoints carry `[PublicEndpoint(...)] + [RegistryAuthorize(RegistryAction.ReadPackageMetadata)]` (package metrics) or `RegistryAction.ReadPackageVersion` (version metrics). The shared `RegistryAuthorizeFilter` resolves the `PackageResource` from route values and runs the existing PDP — anonymous on private/internal returns `404 VisibilityHidden`, never `403`, never an existence leak. **The invariant is byte-indistinguishability** between a hidden package and a genuinely-missing package: both return the same `{"error": "Package '@scope/name' not found."}` body. This means the 404 body for a hidden package DOES contain the package name — that is the correct behavior; a name-free 404 on `/metrics` but a name-bearing 404 on `/packages/{scope}/{name}` would leak existence by the differing body shapes. **Action reuse is the design**, not a coincidence: it requires no new `RegistryAction` enum members, no new `RegistryActionResourceResolver` cases, no new PDP code. The same logic (and the same indistinguishability invariant) underlies `GetReadme` and `GetVersions` today, pinned by `VersionDenyBodyShape_Matrix`.

**Important — dispatch-coverage is not behavior-coverage.** `AuthzDispatchCoverageMetaTests` asserts that every endpoint is *classified* (carries the right decorator pair); it does NOT assert that a particular endpoint returns 404 on a hidden package. That is the same shape of gap that bit the AuthController (architect doc reference). For each new metrics route, we add behavior tests against `RegistryAuthzMatrixTests` data — anonymous-against-public → 200, anonymous-against-private → 404, unauthorized-against-private → 404, member-against-private → 200 — pinned per route, not per action.

**Admin metric action.** `GET /api/v1/admin/metrics/downloads` reuses `RegistryAction.ReadAdminStats` (it is a stats read; `ManageUser` / `AdminAssignPackageRole` would be wrong). No new admin action.

**Public for `[PublicEndpoint]` vs `[Authorize]`.** Package/version metrics endpoints follow the same pattern as `GetPackage`/`GetVersions` — `[PublicEndpoint]` (anonymous allowed → PDP gates on visibility). The admin endpoint follows `AdminController` — `[Authorize]` class-level + `[RegistryAuthorize]` per-action.

### Implementation Path

```
1. MetricsConfig + IpHandlingMode enum + IpHasher (Configuration/) — secret persists across restarts
2. EF migration: download_events + download_rollup_daily + download_rollup_hourly tables
   + version_records.storage_bytes column. PackageService.PublishAsync writes the size.
3. IDownloadEventQueue (Channel<DownloadEvent>) + IDownloadMetricsStore + MetricsBackgroundService (drains + opens own DI scope)
4. PackagesController.DownloadVersion — best-effort enqueue at successful 200 completion (off the hot path)
5. Hourly rollup pass + nightly retention sweep (same MetricsBackgroundService)
6. Wire DTOs in Stash.Registry.Contracts (PackageMetricsResponse, VersionMetricsResponse, expanded StatsResponse, TopPackageDownloadsEntry)
7. PackagesController.GetPackageMetrics + GetVersionMetrics (visibility-gated via [RegistryAuthorize])
   + AdminController.GetStats expanded + AdminController.GetDownloadsMetrics
8. DiscoveryEndpoint: features.Metrics false → true; re-baseline DiscoveryEndpointTests assertions; OpenAPI coverage; docs
```

Each layer participates: capture (download endpoint) → transport (queue) → durability (background service) → aggregation (rollup) → exposition (metrics + admin endpoints) → discovery (flag) → contract (OpenAPI).

### Cross-Cutting Concerns

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| **IP recording mode** — every code path that persists a remote IP (download events; later, audit entries) MUST go through one transform | `IpHasher` service in `Stash.Registry/Configuration/` (consumes `IpHandlingMode` enum), injected via DI | **Construct** — every IP-writing call site receives `IIpHasher` by constructor injection and writes `_ipHasher.Apply(remoteIp)`; a forgotten transform is a missing constructor parameter at compile time. **Detect** (defense-in-depth) — Roslyn meta-test `NoMagicRemoteIpAccessMetaTests` flags any direct read of `HttpContext.Connection.RemoteIpAddress` outside `IpHasher` itself (sink-targeted scan + self-test + binding-floor per CLAUDE.md Roslyn-determinism rule). Seeded (NOT empty) exemption list at land: `IpHasher.cs` plus 6 PERMANENT non-metrics raw-IP readers (auth/admin audit, rate-limit keying, authz audit) that legitimately keep the raw IP; any new direct read must be added explicitly. File-level granularity means a read inside an already-exempt file is not caught — the `DownloadVersion` metrics path's compliance is proven by `DownloadCaptureSemanticsTests`, not this meta-test (see backlog stub). |
| **Closed set of IP-handling modes** | `IpHandlingMode` enum (`raw`/`truncated`/`hashed`/`off`) in `Stash.Registry/Configuration/` | **Construct** — real C# `enum`; an illegal config value fails `JsonStringEnumConverter` at startup with a clear error. |
| **Time-window labels in metrics responses** (`total`, `last24h`, `last7d`, `last30d`) | Response DTO field names in `PackageMetricsResponse` / `StatsResponse.Downloads` | **Construct** — fixed C# property names; no parameter-side enum exists (deliberately — no `?window=` discriminator). |
| **Count-on-success semantics** — one decision: enqueue at successful response completion, status 200, full bytes flushed | One `EnqueueOnCompletion` helper attached to the response in `DownloadVersion` | **Construct + Instruct** — single helper called from exactly one place; acceptance tests assert counts after a 404, after a hidden-package 404, and after a simulated mid-stream disconnect are all 0. |
| **Storage-bytes write↔read** — admin SUM reads `version_records.storage_bytes`; the publish path MUST populate it | `PackageService.PublishAsync` (write) ↔ `StatsResponse.storageBytes` (read) | **Detect** — end-to-end acceptance test publishes a tarball of known size and asserts `storageBytes ≥ size`. Same phase covers write and read, so the omission cannot ship green. |
| **Visibility on metrics endpoints** (anonymous → public only; hidden → 404, never existence leak) | `RegistryAuthorizeFilter` + `ReadPackageMetadata` / `ReadPackageVersion` PDP handlers | **Construct** — reuse the existing decorators; `AuthzCoverageMetaTests` + `AuthzDispatchCoverageMetaTests` fail-close if a new endpoint forgets the decorator pair. **Detect (behavior)** — per-route 404-on-hidden behavior tests added to the matrix, since dispatch coverage classifies but does not run the behavior. |
| **Discovery flag truthfulness** — `features.metrics` flipped only when the feature is *whole* | `DiscoveryEndpoint.cs` + `DiscoveryEndpointTests` | **Construct + Detect** — the flag flip is the final phase; the snapshot test is re-baselined in the *same* phase, so an in-progress / partial feature cannot ship the flag green. |

## Acceptance Criteria

**Behavioral, end-to-end (proving the feature works as a whole):**

1. **Counted-once-per-success.** Publish `@a/foo@1.0.0` (size `N` bytes). Issue 3 successful `GET /api/v1/packages/@a/foo/1.0.0/download` requests (each returns `200` and the full tarball). After the rollup interval, `GET /api/v1/packages/@a/foo/metrics` returns `downloads.total = 3` and `downloads.last24h = 3`; `GET /api/v1/packages/@a/foo/1.0.0/metrics` returns the same.
2. **Excluded paths do not count.** A `404` on a non-existent version, a `404 VisibilityHidden` on a private package by an anonymous caller, and a simulated mid-stream disconnect each leave the package's `downloads.total` unchanged.
3. **Visibility predicate on metrics reads.** Create `@a/private@1.0.0` with `visibility = private`. Anonymous `GET /api/v1/packages/@a/private/metrics` returns **404 with a body byte-indistinguishable from a genuinely-missing package** (not `403`, not `200`). The shared `RegistryAuthorizeFilter` enforces this via the same `VisibilityHidden → 404` rendering used by `GetPackage` / `GetVersions` / `GetReadme` — see `VersionDenyBodyShape_Matrix`. A token with read access to the package returns `200`.
4. **Storage bytes end-to-end.** Publish a tarball whose `IPackageStorage.GetSize` returns `M` bytes. `GET /api/v1/admin/stats` `storageBytes` is `≥ M`.
5. **Top-packages.** Drive downloads such that `@a/foo` outranks `@b/bar` over the requested window. `GET /api/v1/admin/metrics/downloads?page=1&pageSize=10&windowDays=7` returns `@a/foo` before `@b/bar` in the `items` list, both with their integer counts.
6. **IP hashing (default).** With `IpMode = hashed`, persisted `download_events.ip` for two requests from the same source IP are **equal**; from two different source IPs are **different**; neither equals the raw IP string. Restart the registry; the same source IP still produces the same hash (secret persisted).
7. **Discovery flag.** `GET /api/v1/.well-known/registry` returns `features.metrics = true`. The `DiscoveryEndpointTests.GetDiscovery_BucketBFlags_ArePinnedFalse` test is re-baselined (metrics no longer asserted false; advisories / provenance / signatures / trustedPublishing / verifiedPublishers remain false).
8. **OpenAPI coverage.** `OpenApiCoverageMetaTests` passes with the three new operations (`Packages_GetMetrics`, `Packages_GetVersionMetrics`, `Admin_GetDownloadsMetrics`) and zero new exemptions.
9. **Hot-path latency.** A unit test wraps `DownloadVersion` and asserts that across N=100 iterations the per-call wall-clock variance does not regress more than 10% when metrics are enabled vs disabled (no synchronous DB call on the hot path).

**Structural / meta-test:**

10. **No magic remote-IP reads.** `NoMagicRemoteIpAccessMetaTests` is green with a seeded exemption list: `IpHasher.cs` (permanent transform home) plus 6 PERMANENT non-metrics raw-IP readers (auth/admin audit, rate-limit keying, authz audit). No new unexempted file reads `RemoteIpAddress`. (File-level limitation documented in the backlog stub; the `DownloadVersion` metrics-path compliance is proven by `DownloadCaptureSemanticsTests`.)
11. **Authz coverage.** `AuthzCoverageMetaTests` + `AuthzDispatchCoverageMetaTests` pass; both new package endpoints classify with `[PublicEndpoint] + [RegistryAuthorize(ReadPackageMetadata|ReadPackageVersion)]`, the admin endpoint with `[Authorize] + [RegistryAuthorize(ReadAdminStats)]`.
12. **Bounded domains.** `IpHandlingMode` is a real `enum` (not a string); `NoMagicAuthStringsMetaTests` remains green.
13. **Declarative model binding.** `RequestModelBindingMetaTests` is green; the admin `GET` accepts `?page=&pageSize=&windowDays=` via `[FromQuery]`.

## Phases

The phase list lives in `plan.yaml`. Six phases:

1. **M1 — Config + IP transform.** `MetricsConfig`, `IpHandlingMode` enum, `IpHasher` service, persisted HMAC secret, `NoMagicRemoteIpAccessMetaTests` lands green-enforcing with an exemption list seeded with the current direct readers (`IpHasher.cs` + 6 permanent non-metrics readers — audit/rate-limit/authz).
2. **M2 — Schema + storage-bytes write/read.** EF migration: `download_events`, `download_rollup_daily`, `download_rollup_hourly`, `version_records.storage_bytes`. `PackageService.PublishAsync` writes the size; `AdminController.GetStats` returns `storageBytes` from `SUM(storage_bytes)`. End-to-end `done_when`: publish a real tarball, then `GET /admin/stats` reports `storageBytes ≥ tarball size`.
3. **M3 — Non-blocking capture.** `IDownloadEventQueue` (bounded `Channel`) + `MetricsBackgroundService` (drain loop, own DI scope). `PackagesController.DownloadVersion` enqueues at successful 200 completion, reading IP only via `IIpHasher`. `NoMagicRemoteIpAccessMetaTests` stays green; the exemption list keeps `IpHasher.cs` + the 6 permanent non-metrics readers (it does NOT shrink to a single entry — see backlog stub).
4. **M4 — Rollup + retention.** Hourly rollup pass aggregates `download_events` into the rollup tables. Nightly retention sweep deletes raw events older than `Raw.RetentionDays`. Acceptance: after the rollup interval elapses, sum of rollups equals the raw event count just retired.
5. **M5 — Read endpoints (package + version + admin top-packages).** `Packages_GetMetrics`, `Packages_GetVersionMetrics`, `Admin_GetDownloadsMetrics` (PagedResponse<TopPackageDownloadsEntry>) — visibility predicate behavior tests for each new package route; admin behavior tests for top-packages.
6. **M6 — Discovery flag + admin stats activity counts + OpenAPI + docs.** Expand `StatsResponse` with `downloads`/`activity` (publishesLast24h / unpublishesLast24h / deprecationsLast24h). Flip `DiscoveryFeatures.Metrics` to `true`; re-baseline `DiscoveryEndpointTests`. OpenAPI coverage gate green. Update `docs/Registry — Package Registry.md` and `Stash.Registry/CLAUDE.md` (endpoints table + config table + meta-test table).

## Open Questions

- **HMAC secret file location.** Propose `{config.dir}/metrics-ip-secret.bin` (a sibling of the SQLite DB or `appsettings.json`). Final path decided in M1.
- **`windowDays` parameter on admin top-packages.** Default `7`; allowed values `{1, 7, 30}`. Decide in M5 whether to back this with a `[Range]` + a docs note, or an enum; the architect leans toward `[Range(1, 30)]` since only three points are documented and an enum is overkill for a single-endpoint parameter.
- **Counter granularity for `last24h` vs `today`.** Brief uses `last24h` (rolling 24h) consistently to avoid TZ ambiguity; admin stats also report `last24h` (not "today"). Confirm operators are happy with rolling-window semantics; if they want calendar-day buckets, swap server timezone strategy in M4 (no API surface change).

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-05 | Count on **successful 200 completion**, not on response start. | Reconciles the count-semantics constraint with the bytes-served admin metric (D10) — both want completion. Excludes 404s (incl. VisibilityHidden) and mid-stream failures from counts. |
| 2026-06-05 | Reuse `ReadPackageMetadata` / `ReadPackageVersion` PDP actions for the new metrics endpoints (no new enum members). | Same convention as `GetReadme` and `GetVersions`. The shared filter resolves a `PackageResource` and runs the visibility check — the predicate is identical to existing read surfaces (D9). |
| 2026-06-05 | Admin metric endpoint reuses `RegistryAction.ReadAdminStats`. | It is a stats read; adding `ReadAdminMetrics` would split a closed domain for no PDP benefit. |
| 2026-06-05 | Return all windows in **one response object** (`total/last24h/last7d/last30d`); no `?window=` discriminator. | Collapses the would-be bounded-domain parameter; no parameter enum to add. |
| 2026-06-05 | `IpHandlingMode` lives in `Stash.Registry/Configuration/`, NOT `Stash.Registry.Contracts`. | Contracts is wire-only. The IP mode is a server config value, not a wire value. Named `IpHandlingMode` (not `MetricsIpMode`) so audit-log-v2 can reuse without rename. |
| 2026-06-05 | HMAC secret persists across restarts; auto-generated once and written back if missing. | Hashed IPs must correlate across restarts. JWT's auto-regenerate-each-start fallback would silently break the invariant. |
| 2026-06-05 | Introduce the registry's first `BackgroundService` (`MetricsBackgroundService`) as the canonical pattern for the rollup/retention job AND the download-event drain. | The registry has no existing `IHostedService` (verified by grep). The "reuse" constraint resolves to "introduce one; use it for both responsibilities." |
| 2026-06-05 | Background service opens its **own DI scope** per drain cycle (does NOT use the request's scoped DbContext). | Off-request writers cannot use a request-scoped, disposed DbContext. Pinned in the brief so reviewers reject `Task.Run(() => _db.Insert(...))` patterns. |
| 2026-06-05 | IP-mode + transform land in **M1 (first phase)**, not the last phase. | M3 capture writes the IP; if the mode/enum/HMAC don't exist when capture is built, M3 either ships raw IPs (D11 default violation) or builds a transform it immediately rewrites. |
| 2026-06-05 | Storage-bytes write AND read in the **same phase** (M2). | A phase that adds the column + the admin SUM but forgets the publish-path write passes `done_when` with `storageBytes = 0`. Same-phase write/read with an end-to-end `done_when` makes the omission impossible to ship green. |
| 2026-06-05 | Rollup tables are authoritative for closed buckets; current open bucket is computed from raw + added. | Resolves the raw-vs-rollup read-model ambiguity once, here, so M4 and M5 cannot disagree. |
| 2026-06-05 | NOT denormalizing `downloads` onto `PackageSummaryResponse` / `PackageDetailResponse`. | Lean minimal-first; revisit only if the front-end shows the per-row round-trip is genuinely expensive. |
| 2026-06-05 | NOT adding `downloads` to search ranking/sort. | Gap §1 mentions it; the website-readiness doc warns it lands "half-impossible." Defer to a separate search feature. |
| 2026-06-05 | `Detect`-level meta-test `NoMagicRemoteIpAccessMetaTests` paired with **Construct** (DI of `IIpHasher`). | Construct alone (DI) catches forgotten parameters at compile time; the meta-test catches the subtle case of a controller that takes the dep but uses `HttpContext.Connection.RemoteIpAddress` anyway. Follows CLAUDE.md "Construct + Detect" pattern with sink-targeted scan + self-test + binding-floor. |
| 2026-06-05 | Add **per-route** visibility behavior tests for each new metrics endpoint (not just dispatch coverage). | `AuthzDispatchCoverageMetaTests` classifies but does not exercise. The AuthController gap the architect doc warns about is exactly this shape — classification passed, behavior wasn't run. |
