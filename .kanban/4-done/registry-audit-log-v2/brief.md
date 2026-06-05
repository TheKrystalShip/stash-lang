# RFC: Registry Audit Log v2 — Filters, Event Coverage, Export, Retention, Tamper-Evidence

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-06-05
> **Slug:** registry-audit-log-v2
> **Milestone:** self-hosted-registry

## Summary

Evolve the registry's existing audit log from a narrow package/action-filtered list into an
operator-grade compliance surface. Five capabilities, all on the **existing append-only
`AuditEntry`** (no schema redesign — D12), with the **two tamper-evidence columns** as the only
additive columns:

1. **Richer filters** on `GET /api/v1/admin/audit-log` — widen the query from `package`/`action`
   to also accept `user`, `target`, `version`, `ip`, `from`, `to` (over the existing
   `PagedResponse<AuditEntryResponse>` envelope, declarative `[FromQuery]`).
2. **Event-type coverage** — (a) migrate **every** inline audit-action literal into the
   `AuditActions` named-constant home (bounded domain), preserving each existing wire value
   byte-for-byte; (b) instrument the live-but-currently-unaudited security events whose code paths
   already exist (`auth.login.success`, `auth.login.failure`, `auth.refresh.failure`,
   `auth.register`). Bounded-domain omission is closed by a **new sink-targeted meta-test built
   first**, not by the one-time sweep.
3. **Export** — `GET /api/v1/admin/audit-log/export?format=jsonl` and `?format=csv` (admin-only,
   honors the same filters).
4. **Retention** — a config-driven background sweep of old audit entries, a **separate** knob from
   download-metrics retention.
5. **Optional tamper-evidence** — a knob-gated hash chain (`previousHash`/`entryHash` over a single
   canonical serialized payload), computed in insertion order on a **serialized** append path, plus
   a `GET /api/v1/admin/audit-log/verify` endpoint that reports the first broken link.

Reuses the operator-configurable IP-handling pipeline shipped by P2 (`IIpHasher`, D11) for the new
`ip` filter; flips the new `Audit` discovery feature flag from `false` to `true` when the unit is
whole.

This is the **P3 unit of the `self-hosted-registry` milestone** (P1 orgs/scopes/visibility and P2
download-metrics already shipped). External sinks (webhook/syslog/OTLP/SIEM) and action strings for
not-yet-built features (`advisory.*`, `trusted_publisher.*`, `package.quarantine`) are explicit
non-goals.

## Motivation

Self-hosted registries run in corporate / infrastructure / security-conscious environments where
"who changed what, when, and from where" is a first-class, frequently audited requirement. Today
the registry has the *seed* of this — an append-only `AuditEntry` table written for mutations and
authenticated denials — but the surface is too narrow to be operator-grade:

- **Filtering** is limited to `package` + `action` (`AuditService.GetAuditLogAsync`), so an operator
  cannot answer "everything user X did," "everything from IP Y," or "everything between two
  timestamps."
- **Event coverage** has two defects: (a) the action vocabulary is **scattered as inline string
  literals** across `AuditService`, `AuthController`, `AdminController`, `PackagesController`, and
  `Startup` — only 7 of the ~13 live values live in `AuditActions`, so the bounded-domain single
  source of truth is already violated; (b) **login success/failure and refresh-failure are not
  audited at all**, despite being the most security-relevant events an operator wants.
- There is **no export** (compliance pipelines want JSONL/CSV), **no retention** (the table grows
  unbounded), and **no tamper-evidence** (an operator with DB access — or an attacker who gains it —
  can silently rewrite history).

The roadmap's Gap §2 names the full capability set; the **Locked Decisions (2026-05-29) addendum**
picks the design (D11 reuse the IP pipeline, D12 not-a-schema-change + tamper-evidence is in scope,
D13 downloads stay out). This feature lands those decisions on the live admin audit surface.

## Goals

- **Filters.** Widen `AuditLogQuery` + `AuditService.GetAuditLogAsync` + the EF query to filter on
  `user`, `target`, `version`, `ip`, `from`, `to` (in addition to the existing `package`, `action`,
  `page`, `pageSize`). All over the existing `PagedResponse<AuditEntryResponse>` envelope via
  `[FromQuery]`.
- **Bounded-domain consolidation.** Every audit-action string written anywhere comes from
  `AuditActions`. Migrate the inline literals (`role.assign`, `role.revoke`,
  `package.visibility_change`, `user.create`, `user.disable`, `token.create`, `token.revoke`,
  `token.refresh`, `token_theft_detected`, `token.revoked`) to named constants **preserving each
  existing value byte-for-byte**. The deny path's `RegistryAction.ToString()` vocabulary is
  enum-derived (already bounded) and stays.
- **Prevent re-introduction.** A new `NoMagicAuditActionStringsMetaTests` (Roslyn sink-scan, modeled
  on `NoMagicAuthStringsMetaTests`) fails the build if any string literal reaches an audit-action
  sink. Built **first** (the chokepoint phase), with a fail-path self-test, binding-floor, and
  pinned exemption list.
- **Event instrumentation.** Add audit writes for the live-but-unaudited security events:
  `auth.login.success`, `auth.login.failure`, `auth.refresh.failure`, `auth.register`.
- **Export.** `GET /api/v1/admin/audit-log/export?format=jsonl|csv` streams the filtered entry set;
  `format` is a bounded `AuditExportFormat` enum.
- **Retention.** A config-driven background sweep deleting audit entries older than
  `Audit.RetentionDays`; a **separate** knob from `Metrics.Raw.RetentionDays`. `0` / unset disables
  the sweep (never deletes).
- **Tamper-evidence (optional, knob-gated).** When `Audit.TamperEvidence.Enabled = true`, the append
  path computes `entryHash = H(canonical(entry) || previousHash)` where `previousHash` is the
  prior hashed entry's `entryHash`, under a **serialized append critical section**. A
  `GET /api/v1/admin/audit-log/verify` endpoint walks the chain and reports the first broken index.
- **`ip` filter through `IIpHasher`.** The stored IP is already transformed at write time (D11); the
  `ip` filter applies the **same** `IIpHasher.Apply(...)` to the operator-supplied IP before
  matching, so the operator types a real IP regardless of mode.
- **Discovery + contract.** Add a `DiscoveryFeatures.Audit` flag (pinned `false` on add, flipped
  `true` in the final phase). Every new endpoint gets OpenAPI coverage; every new wire DTO lives in
  `Stash.Registry.Contracts` (no `AuditEntry` EF entity leak — it already maps to
  `AuditEntryResponse`).

## Non-Goals

- **External sinks** — webhook, file-append, syslog, OpenTelemetry, SIEM JSON stream. The Phase-3
  roadmap row omits them; heavier infra owned by a later feature.
- **Action strings for not-yet-built features.** `advisory.*` (P5), `trusted_publisher.*` (P4),
  `package.quarantine` / `package.unquarantine` (P6), `package.transfer.*` (P6/ownership) have **no
  live call site** (their discovery flags are still `false`). Adding their constants now is dead
  code — each lands with its own feature. The gap doc's event list is a *menu of eventual events*,
  not a today's-work list.
- **`user.role.update` as a distinct event.** A user's global role changes today only inside the
  admin `CreateUser` promotion branch (`UpdateUserRoleAsync` for a new admin) — there is no
  standalone "change an existing user's role" endpoint. With no independent live call site, this is
  deferred (it lands when an explicit role-change endpoint does). The admin user-create already
  emits `user.create`; we do not synthesize a second event for the same call.
- **`user.password.change`, `user.delete`, `token.expire`, `token.scope.denied`** — no live call
  site (no password-change endpoint; "delete user" emits `user.disable` today; token expiry is
  passive; scope-denied is covered by the existing PDP deny path's `RegistryAction.ToString()`
  vocabulary). Deferred as dead code.
- **Schema redesign of `AuditEntry`.** D12 — v2 is query plumbing + action strings + export +
  retention + tamper-evidence. The **only** new columns are the two tamper-evidence fields
  (`previous_hash`, `entry_hash`), which are the additive realization of D12's own tamper-evidence
  clause — not a redesign of the existing columns.
- **Downloads in the audit log.** Per D13, downloads stay a separate surface (the metrics
  subsystem); folding them in would flood the table and bloat the tamper chain.
- **A second IP-handling pipeline.** Per D11, reuse `IIpHasher` exactly; do not build a parallel
  transform.
- **Any non-admin audit read path.** No per-package maintainer activity feed, no per-user "my
  actions" endpoint. Every audit read (`list`, `export`, `verify`) is admin-only; a non-admin read
  path would be an existence-leak surface.
- **Legacy / backfill / data migration.** The registry is pre-release (no deployed instance, no
  existing data); design for the clean forward case. Pre-tamper-evidence rows are simply pre-genesis
  (null hash).

## Design

### Surface

**New endpoints:**

| Method | Path | Auth class | Auth action | Response |
| --- | --- | --- | --- | --- |
| GET | `/api/v1/admin/audit-log/export?format=jsonl\|csv&<filters>` | `[Authorize]` + `[RegistryAuthorize(RegistryAction.ReadAuditLog)]` | reused | streamed `application/x-ndjson` or `text/csv` |
| GET | `/api/v1/admin/audit-log/verify` | `[Authorize]` + `[RegistryAuthorize(RegistryAction.ReadAuditLog)]` | reused | `AuditVerifyResponse` |

**Modified endpoints:**

- `GET /api/v1/admin/audit-log` — `AuditLogQuery` widens from `{ page, pageSize, package, action }`
  to also accept `user`, `target`, `version`, `ip`, `from`, `to`. Response shape
  (`PagedResponse<AuditEntryResponse>`) is **unchanged**; `AuditEntryResponse` gains optional
  `entryHash` / `previousHash` fields (null when tamper-evidence disabled).
- `GET /api/v1/.well-known/registry` — `features.audit` flips `false → true` (the flag is **added**
  to `DiscoveryFeatures` in this feature, pinned false, flipped true in the final phase).

**No new `RegistryAction` member.** `ReadAuditLog` already exists and gates `GET /admin/audit-log`;
export and verify are the same read (admin reading the audit surface), so they reuse it. Adding
`ExportAuditLog` / `VerifyAuditLog` would split a closed PDP domain for no benefit (same convention
as download-metrics reusing `ReadAdminStats`).

**New configuration (`appsettings.json` → `Configuration/AuditConfig.cs`):**

```jsonc
"Audit": {
  "RetentionDays": 0,                 // 0 / unset = never delete (default). > 0 enables the nightly sweep.
  "TamperEvidence": {
    "Enabled": false,                 // off by default; opt-in hash chain
    "HashSecret": "<base64>"          // optional keyed hash (HMAC); if absent, plain SHA-256 over canonical payload
  }
}
```

`AuditExportFormat` (the `?format=` bounded domain) is a **wire-visible** closed set → a C# `enum`
in `Stash.Registry.Contracts/BoundedDomains.cs` with `[JsonStringEnumMemberName("jsonl")]` /
`("csv")`, parsed at the boundary via `[FromQuery]` model binding (an illegal `?format=xml`
returns `400 InvalidRequest`, not a silent default).

**Export body shapes:**

```text
# format=jsonl  (Content-Type: application/x-ndjson)  — one AuditEntryResponse JSON object per line
{"action":"package.publish","package":"@a/foo","version":"1.0.0","user":"alice","ip":"<transformed>","timestamp":"2026-06-05T12:00:00Z","decision":"allow"}
{"action":"auth.login.failure","user":"bob","ip":"<transformed>","timestamp":"2026-06-05T12:01:00Z","decision":"deny"}

# format=csv  (Content-Type: text/csv)  — header row + RFC-4180-quoted fields
action,package,version,user,target,ip,timestamp,decision,denyReason,previousHash,entryHash
package.publish,@a/foo,1.0.0,alice,,<transformed>,2026-06-05T12:00:00Z,allow,,,
```

```jsonc
// AuditVerifyResponse
{
  "enabled": true,             // tamper-evidence configured on
  "checkedCount": 1402,        // number of hashed entries walked
  "valid": false,              // whole chain intact?
  "firstBrokenId": 938,        // id of the first entry whose recomputed hash != stored hash (null if valid)
  "genesisId": 17              // id of the first hashed (post-enable) entry
}
```

### Semantics

**Filters — additive AND, all optional.** Each supplied filter narrows the result (logical AND);
omitted filters are inert. The EF query (`StashRegistryDatabase.GetAuditLogAsync`) gains a `WHERE`
clause per supplied filter; ordering stays `ORDER BY timestamp DESC`; pagination is unchanged.
`from`/`to` are inclusive UTC `DateTime` bounds on `Timestamp`. `user`/`target`/`version`/`package`
are exact-match (consistent with the existing `package`/`action` exact match — no `LIKE`, no leak of
a substring-search oracle).

**Audit IPs are transformed at WRITE time (D11 "applies to both metrics and audit") — this feature
brings audit under the IP pipeline.** Today every audit call site reads
`HttpContext.Connection.RemoteIpAddress?.ToString()` and stores the **raw** IP verbatim; P2
(download-metrics) listed these auth/admin audit readers as "PERMANENT raw-IP keepers" — but that
was a *within-P2-scope* deferral (audit-v2 did not exist yet). D11 in the roadmap ledger is locked:
operator-configurable IP handling, default hashed, **"applies to both metrics and audit."** P3 is
the feature D11 always intended to fold audit into the pipeline; it **retires** P2's raw-IP stopgap.

Design (Construct chokepoint): the transform lives in **exactly one place** — `AuditService`. The
`Log*` helpers take the raw request IP (controllers/middleware keep *obtaining* it from
`HttpContext`, the layering-correct place to read the request) and `AuditService` applies
`IIpHasher.Apply(...)` **once** before constructing the `AuditEntry`. Every audit write — existing
and future — is transformed for free; a new audit event cannot forget the transform because it never
touches the IP representation itself. The stored `Ip` column therefore holds the transformed value
(raw / `/24` / HMAC-hash / null) chosen by `Metrics.IpMode`.

**`ip` filter routes the query value through the SAME `IIpHasher`.** Because the column stores the
transformed value, the filter applies `IIpHasher.Apply(parsedIp)` to the operator-supplied IP and
matches the **transformed** value, so the operator always types a real IP. The write side and the
filter side share one `IIpHasher` (single source of truth). Edge cases, stated once here:
- `IpMode = hashed` / `truncated` — both the stored value and the query value pass through the same
  transform, then exact-match.
- `IpMode = raw` — `Apply` returns the raw string on both sides; exact-match works directly.
- `IpMode = off` — the `Ip` column is `null` for all entries written under `off`; an `ip` filter
  then matches nothing (documented, not an error). Entries written under a *previous* mode retain
  their old stored representation and are not retroactively rewritten.

**Event coverage — migrate values byte-for-byte; add the missing live events.**

*Migration (no behavior change, only a literal → constant move).* The following inline literals move
into `AuditActions` with the **exact same value**. Changing any value is a wire-breaking change and
is forbidden:

| New constant (name may be tidy) | Value (MUST stay byte-for-byte) | Current inline site(s) |
| --- | --- | --- |
| `RoleAssign` | `role.assign` | Admin + Packages controllers, `AuditService.LogRoleAssignAsync` |
| `RoleRevoke` | `role.revoke` | Admin + Packages controllers, `AuditService.LogRoleRevokeAsync` |
| `PackageVisibilityChange` | `package.visibility_change` | `PackagesController:817` (note the **underscore** — do NOT "fix" to `visibility.update`) |
| `UserCreate` | `user.create` | `AuditService.LogUserCreateAsync` |
| `UserDisable` | `user.disable` | `AuditService.LogUserDisableAsync` |
| `TokenCreate` | `token.create` | `AuditService.LogTokenCreateAsync` |
| `TokenRevoke` | `token.revoke` | `AuditService.LogTokenRevokeAsync` |
| `TokenRefresh` | `token.refresh` | `AuditService.LogTokenRefreshAsync` |
| `TokenTheftDetected` | `token_theft_detected` | `AuditService.LogTokenTheftDetectedAsync` (note the **underscores**) |
| `TokenRevoked` | `token.revoked` | `Startup.cs:426` (JTI revocation deny) |

The existing `AuditActions` values (`Publish`, `Unpublish`, `PackageCreate`, `PackagePublish`,
`PackageUnpublish`, `PackageDeprecate`, `PackageUndeprecate`, `VersionDeprecate`,
`VersionUndeprecate`) are untouched — **never mutate an existing value** (that is the exact
download-metrics regression: a shared-constant value change passed a filtered verify and broke the
`RegistryAuthz*` wire contract). `AuditServiceTests` pins `role.assign`/`token.create` and
`RegistryAuthzAuditMutationTests` pins `role.assign`/`role.revoke`; both must stay green.

*New live events (instrument existing code paths).* These paths exist and execute today but write no
audit entry; we add the write (and the constant):

| New constant | Value | Call site to instrument | Decision field |
| --- | --- | --- | --- |
| `AuthLoginSuccess` | `auth.login.success` | `AuthController.Login` success path | `allow` |
| `AuthLoginFailure` | `auth.login.failure` | `AuthController.Login` bad-credential return | `deny` |
| `AuthRefreshFailure` | `auth.refresh.failure` | `AuthController.RefreshToken` failure returns | `deny` |
| `AuthRegister` | `auth.register` | `AuthController.Register` success path | `allow` |

**Note on `auth.login.failure` volume.** Failed logins are higher-volume than mutations and (like
downloads, which D13 kept out) could grow the table and lengthen the tamper chain. It is **kept in**
— it is the single most security-relevant event an operator audits — but the volume interacts with
retention and the chain length; the mitigation is the **existing per-category auth rate limiting**
(`RateLimitingMiddleware`, `Auth` category), which already caps the failed-login rate. Recorded so a
future operator-load concern has a documented home.

**Note on test churn from `auth.login.success`.** Many existing tests log in during setup; once
`auth.login.success` is wired, each login writes an audit entry. Tests that assert a *total*
audit-log count (e.g. `RegistryAuthzAuditMutationTests`' "exactly one entry" patterns) may shift and
need their assertions scoped to a specific `action` or `decision` rather than a global count. The A2
implementer should expect this and adjust count-asserting tests to filter by action.

**Note on `auth.register` vs `user.create`.** `Register` already calls `LogUserCreateAsync`
(`user.create`). We **keep that** (it is the user-creation record) and **add** a distinct
`auth.register` entry recording the self-service registration event (vs. an admin creating a user).
This is the only place two entries are written for one call; documented so it is not mistaken for a
double-write bug. (Open Question OQ1 lets the user collapse these to one if preferred.)

**Export — same filters, streamed, admin-only.** `GET /admin/audit-log/export` binds the **same**
filter fields (`user/target/version/ip/from/to/package/action`) plus the bounded `format`, but is
**not paginated** — export streams the full filtered set. Use a dedicated `AuditExportQuery` DTO (the
filter fields without `page`/`pageSize`) rather than reusing `AuditLogQuery` and ignoring its
`[Range]`'d pagination fields — a streaming endpoint must not advertise a `pageSize`. It streams rows
so a large log does not buffer in memory. JSONL is one
`AuditEntryResponse` JSON object per line; CSV is a header row plus RFC-4180-quoted fields in a fixed
column order. The IP column carries the already-transformed stored value (trivially true now that
audit IPs are transformed at write time — the raw IP never reaches the column). Export honors
visibility trivially — it is admin-only and the audit log is
a flat global table.

**Retention — separate knob, background sweep, never best-effort-drops a write.**
`Audit.RetentionDays > 0` enables a nightly sweep that deletes audit entries with
`Timestamp < now - RetentionDays`. `0` / unset = never delete (the safe default for a compliance
log). This is **independent** of `Metrics.Raw.RetentionDays` (download-events retention) — two
distinct knobs, two distinct sweeps. The sweep runs in a `BackgroundService` (`AuditBackgroundService`,
the audit analogue of the metrics `MetricsBackgroundService`; it opens its own DI scope per cycle —
an off-request writer cannot use the request-scoped `DbContext`).

**Retention × tamper-evidence interaction (designed, not incidental).** Deleting old entries
truncates the chain's *anchor* (the genesis) but each retained row still stores its own
`previousHash`, so contiguity among **retained** rows still verifies. The verify procedure's
window is therefore "retained ∧ hashed": `verify` walks from the earliest retained hashed entry,
treating its stored `previousHash` as the trusted anchor for the retained window, and reports a
break only within that window. The retention sweep and the verify endpoint **agree** on this
definition (single source of truth: `AuditChainVerifier`). The truncation is intended and
documented, not a chain corruption.

**Tamper-evidence — serialized append, one canonical serializer, knob-gated.**

- **One canonical serializer.** `AuditChainHasher.CanonicalPayload(AuditEntry)` is the **single**
  function producing the bytes hashed, used by **both** the write path and the verify path. Two
  divergent serializers is the classic omission bug; there is exactly one home. The canonical form
  is a fixed-field-order concatenation (or canonical JSON) of the entry's content fields
  (`action`, `package`, `version`, `user`, `target`, `ip`, `timestamp` (ISO-8601 UTC, fixed
  precision), `decision`, `denyReason`) — **never** the `id` (a surrogate) and **never** the hash
  fields themselves.
- **Chain rule.** `entryHash = H(CanonicalPayload(entry) || previousHash)`, where `previousHash` is
  the `entryHash` of the immediately prior **hashed** entry (or the empty/genesis value for the
  first). `H` is HMAC-SHA256 when `Audit.TamperEvidence.HashSecret` is set, else plain SHA-256.
- **Serialized append.** The "read last hash → compute → insert" critical section MUST be atomic, or
  two concurrent appends fork the chain. SQLite serializes writes; PostgreSQL does **not** — so the
  append is guarded by a process-global async lock (single-writer path) around
  `AddAuditEntryAsync` when tamper-evidence is enabled. Audit writes are **durable before the
  response** (not fire-and-forget like download counts) — the metrics `Channel` pattern does **not**
  transfer to audit; the lock cost is acceptable for the low-volume mutation/deny stream.
- **disabled → enabled mid-stream.** Genesis = the first entry written after enabling
  (its `previousHash` = genesis sentinel). Prior entries keep `null` hashes and are **pre-genesis**;
  `verify` starts at the first non-null `entryHash`. enabled → disabled → re-enabled: on re-enable the
  new entry links to the most recent hashed entry (`PreviousHash` = its `EntryHash`); null-hash entries
  written while disabled are invisible to the walker, which verifies **one continuous chain anchored at
  the original genesis**. This maximises the verified set — the entire history of hashed entries is
  covered by a single unbroken chain, even across disabled gaps. Documented so the implementer does not
  invent a different rule.
- **verify endpoint.** `GET /admin/audit-log/verify` recomputes each hashed entry's hash from its
  stored fields + the prior stored hash, comparing to the stored `entryHash`; returns
  `AuditVerifyResponse` naming the first broken id. This gives a clean end-to-end acceptance test:
  tamper one row's `User` directly in the DB → `verify` returns `valid=false, firstBrokenId=<that
  row>`.

**Admin-only, no existence leak.** All three audit reads (`list`, `export`, `verify`) carry
`[Authorize]` (class-level on `AdminController`) + `[RegistryAuthorize(RegistryAction.ReadAuditLog)]`.
There is no anonymous or maintainer audit path. The audit log is a flat global table with no
per-package visibility predicate, so the only gate is admin authorization — but we explicitly do
**not** add any non-admin read surface that could leak private-package existence through audit
entries.

### Implementation Path

```
1. Action-string consolidation + bounded-domain CHOKEPOINT (first):
   - migrate every inline audit-action literal into AuditActions (values byte-for-byte).
   - ship NoMagicAuditActionStringsMetaTests (Roslyn sink-scan on `AuditEntry { Action = ... }`
     initializer + Log* action params) GREEN with a pinned exemption list + fail-path self-test
     + binding-floor. Later phases route through the now-enforced home.
2. Event instrumentation: AuthController writes auth.login.success/failure, auth.refresh.failure,
   auth.register through AuditService (constants only — meta-test already enforcing).
3. Audit IP under the pipeline + filter widening:
   - AuditService applies IIpHasher.Apply ONCE at the single write site (Log* helpers take the raw
     request IP; controllers/middleware keep obtaining it) — audit IPs now stored transformed (D11).
   - AuditLogQuery + AuditService.GetAuditLogAsync + StashRegistryDatabase.GetAuditLogAsync gain
     user/target/version/ip/from/to; the `ip` filter routes the query value through the SAME IIpHasher.
   - P2's NoMagicRemoteIpAccessMetaTests exemption rationale for the audit-write files flips from
     "keeps raw IP" to "obtains raw IP, transformed downstream in AuditService" (files stay exempt).
4. Export: AdminController.ExportAuditLog (jsonl/csv), AuditExportFormat enum in Contracts,
   streamed writer, honors the same filters.
5. Retention: AuditConfig + AuditBackgroundService nightly sweep (own DI scope), separate knob.
6. Tamper-evidence: previous_hash/entry_hash columns (EF migration), AuditChainHasher (one canonical
   serializer), serialized append in AddAuditEntryAsync, AdminController.VerifyAuditLog + verify
   endpoint, AuditEntryResponse gains the two optional hash fields.
7. Discovery flag (false→true) + OpenAPI re-baseline + docs.
```

Each layer participates: vocabulary home (chokepoint) → instrumentation (auth events) → query
(filters) → exposition (export) → durability hygiene (retention) → integrity (tamper chain) →
discovery + contract.

### Cross-Cutting Concerns

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| **Audit-action vocabulary** — every audit `Action` value written anywhere is a member of the closed set | `AuditActions` (static constants) for literal events; `RegistryAction` enum (via `.ToString()`) for the deny vocabulary | **Detect** — `NoMagicAuditActionStringsMetaTests` (Roslyn sink-scan: any string literal reaching an `AuditEntry { Action = … }` initializer or a `Log*` action parameter) ships **first** with a fail-path self-test, a binding-floor + file-count floor (CLAUDE.md Roslyn-determinism rule), and a **pinned** exemption list (the deny path's `RegistryAction.ToString()` is the legitimate exemption — that vocabulary is enum-derived, not a literal, and cannot be folded into `AuditActions`). Detect (not Construct) because the deny path's vocabulary is `RegistryAction`-sourced; a pure `enum AuditAction` cannot cover it. The literal-event portion *is* Construct-eligible — see Decision Log: an `enum AuditAction` upgrade for that portion is presented first per Make-It-Right, but the meta-test remains the umbrella. |
| **Literal action VALUES are wire contract** — a constant's value must equal the existing live string byte-for-byte | The value column of the migration table in Semantics (above) | **Detect + Instruct** — `AuditServiceTests` / `RegistryAuthzAuditMutationTests` pin specific values (`role.assign`, `role.revoke`, `token.create`); the brief forbids mutating any existing value; `final_verify` runs the **full unfiltered** `dotnet test` (a filtered verify is what let the download-metrics value-change regression through). |
| **IP recording/matching mode** — audit IPs are transformed at write time AND the `ip` filter transforms the query value through the SAME pipeline | `IIpHasher` (from P2, `Stash.Registry/Configuration/`, consumes `IpHandlingMode` enum), applied in exactly ONE write site inside `AuditService` and the same instance in the filter | **Construct** — `AuditService` applies `IIpHasher.Apply(...)` once before constructing every `AuditEntry`, so a new audit event cannot forget the transform (it never touches the IP representation); the filter receives the same `IIpHasher` by constructor injection and matches `_ipHasher.Apply(queryIp)`. Reuses P2's single source of truth — no second IP path (D11). The P2 `NoMagicRemoteIpAccessMetaTests` keeps the audit-write files exempt with the rationale flipped from "keeps raw IP" to "obtains raw IP, transformed downstream in `AuditService`" — rate-limit keying stays raw and exempt (genuinely needs raw, not an audit write). |
| **Canonical hash payload** — the bytes hashed on write MUST equal the bytes hashed on verify | `AuditChainHasher.CanonicalPayload(AuditEntry)` (one function) | **Construct** — exactly one serializer, called by both the append path and the verify endpoint; there is no second function to drift. A round-trip acceptance test (write N entries, verify → valid; tamper one → verify names it) proves both call the same home. |
| **Chain verification window under retention** — retention truncates the anchor; verify must agree on "verifiable = retained ∧ hashed" | `AuditChainVerifier` (the verify endpoint's walker) | **Instruct + Detect** — the retention sweep and verify share the brief's single definition; an acceptance test deletes pre-genesis rows and asserts `verify` still returns `valid=true` over the retained window. |
| **Export format** — closed `{jsonl, csv}` set | `AuditExportFormat` enum in `Stash.Registry.Contracts/BoundedDomains.cs` | **Construct** — real C# `enum` with `[JsonStringEnumMemberName]`; `?format=xml` fails model binding with `400 InvalidRequest`. |
| **Audit endpoint authorization** — every audit read is admin-only, no existence leak | `RegistryAuthorizeFilter` + `RegistryAction.ReadAuditLog` | **Construct** — reuse the existing `[Authorize] + [RegistryAuthorize(ReadAuditLog)]` decorator pair; `AuthzCoverageMetaTests` + `AuthzDispatchCoverageMetaTests` fail-close if a new endpoint forgets it. **Instruct** — the brief forbids any non-admin audit read path. |
| **Discovery flag truthfulness** — `features.audit` true only when the feature is whole | `DiscoveryEndpoint.cs` + `DiscoveryFeatures` DTO + `DiscoveryEndpointTests` | **Construct + Detect** — the flag is **added** pinned `false` and **flipped** `true` only in the final phase; the snapshot test is re-baselined in the same phase, so a partial feature cannot ship the flag green. |

## Acceptance Criteria

**Behavioral, end-to-end (proving the feature works as a whole):**

1. **Filter by user.** Seed audit entries for users `alice` and `bob`.
   `GET /api/v1/admin/audit-log?user=alice` returns only `alice`'s entries; total count matches.
2. **Filter by time window + action.** `GET /api/v1/admin/audit-log?action=package.publish&from=<t0>&to=<t1>`
   returns only `package.publish` entries with `t0 <= timestamp <= t1`.
3. **Filter by ip through the hasher (write-time + filter-time symmetric).** With `IpMode = hashed`,
   two audited actions occur from source IP `203.0.113.7`; `AuditService` stored the **hashed** value
   (not raw). `GET /api/v1/admin/audit-log?ip=203.0.113.7` (operator types the **raw** IP) returns
   exactly those two entries — the filter hashed the query value through the same `IIpHasher` and
   matched the stored hash. Asserting the stored `Ip` column is the hash, not `203.0.113.7`, proves
   write-time transform. With `IpMode = off`, the same query returns nothing (column is null).
4. **Login events audited.** A successful login writes one `auth.login.success` (`decision=allow`)
   entry; a bad-credential login writes one `auth.login.failure` (`decision=deny`) entry; both are
   returned by `GET /api/v1/admin/audit-log?action=auth.login.failure` etc.
5. **Refresh-failure + register audited.** A failed token refresh writes `auth.refresh.failure`; a
   self-service `POST /auth/register` writes `auth.register` (alongside the existing `user.create`).
6. **Export JSONL/CSV/bad-format.** `GET /api/v1/admin/audit-log/export?format=jsonl&user=alice`
   returns `Content-Type: application/x-ndjson`; each line parses as an `AuditEntryResponse` JSON
   object; the set equals the filtered list. `?format=csv` returns `text/csv` with a header row and
   the same rows. `?format=xml` returns `400 InvalidRequest`.
7. **Retention sweep.** With `Audit.RetentionDays = 30`, seed an entry timestamped 40 days ago and
   one timestamped now; run the sweep; the 40-day-old entry is gone, the recent one remains. With
   `RetentionDays = 0`, the sweep deletes nothing.
8. **Tamper-evidence chain valid.** With `TamperEvidence.Enabled = true`, perform 5 audited actions.
   `GET /api/v1/admin/audit-log/verify` returns `valid=true, checkedCount=5`. Each entry's
   `entryHash` is non-null and `previousHash` links to the prior entry.
9. **Tamper-evidence detects mutation.** With the chain from (8), directly update one entry's `User`
   column in the DB (simulating tampering). `verify` returns `valid=false` with `firstBrokenId` =
   that entry's id.
10. **disabled → enabled mid-stream.** Write 2 entries with tamper-evidence **off** (null hashes),
    enable it, write 3 more. `verify` returns `valid=true, checkedCount=3, genesisId=<3rd entry>`;
    the first 2 are pre-genesis and excluded, not reported as broken.
11. **Retention × tamper.** With a verified chain and `RetentionDays` deleting the genesis +
    pre-genesis rows, `verify` still returns `valid=true` over the retained window (the earliest
    retained hashed row's stored `previousHash` is the trusted anchor).
12. **Discovery flag.** `GET /api/v1/.well-known/registry` returns `features.audit = true`.
    `DiscoveryEndpointTests` is re-baselined (`audit` added to the true set; the remaining Bucket-B
    flags — advisories / provenance / signatures / trustedPublishing / verifiedPublishers — stay
    false).

**Structural / meta-test:**

13. **No magic audit-action strings.** `NoMagicAuditActionStringsMetaTests` is green: no string
    literal reaches an audit-action sink outside the pinned exemption (the deny path's
    `RegistryAction.ToString()`). Ships a positive fail-path self-test (a fixture that trips it) and
    a binding-floor + file-count floor.
14. **No magic remote-IP reads.** `NoMagicRemoteIpAccessMetaTests` (from P2) stays green: the new
    `ip`-filter path reads the operator IP via `IIpHasher.Apply`, not via
    `HttpContext.Connection.RemoteIpAddress`; `AuditService` applies `IIpHasher` to the write-path IP
    once. The audit-write files (`AuthController`, `AdminController`, `Startup`,
    `RegistryAuthorizeFilter`) stay on the pinned exemption list, with their rationale flipped from
    "keeps raw IP" to "obtains raw IP, transformed downstream in `AuditService`" (the P2 brief/test's
    "their reads keep the raw IP" line is corrected). Rate-limit keying stays raw and exempt
    (genuinely needs raw, not an audit write).
15. **Authz coverage.** `AuthzCoverageMetaTests` + `AuthzDispatchCoverageMetaTests` pass; the export
    and verify endpoints classify with `[Authorize] + [RegistryAuthorize(ReadAuditLog)]`.
16. **OpenAPI coverage + snapshot re-baseline.** `OpenApiCoverageMetaTests` passes with the two new
    operations (`Admin_ExportAuditLog`, `Admin_VerifyAuditLog`) and zero new exemptions. (If the
    streamed export operation cannot carry a `$ref` response schema like `Packages_DownloadVersion`,
    it joins the permanent stream exemption with a recorded reason.) Additionally
    `OpenApiSnapshotTests` (which pins the **full** generated doc against the committed
    `Stash.Tests/Registry/OpenApi/Snapshots/openapi-v1.json`) is **re-baselined** in A7 via
    `STASH_SNAPSHOT_REGEN=1 dotnet test --filter FullyQualifiedName~OpenApiSnapshotTests` — the two
    new operations and the new `audit` discovery field change the doc, so the snapshot regen is
    expected, not a regression.
17. **Declarative model binding.** `RequestModelBindingMetaTests` is green; the widened audit query,
    export query, and verify endpoint all use `[FromQuery]` (no `Request.Query`/`Request.Body`
    reads).
18. **Existing wire contracts preserved.** `AuditServiceTests` and `RegistryAuthzAuditMutationTests`
    stay green — no existing audit-action value changed.

## Phases

The phase list lives in `plan.yaml`. Seven phases:

1. **A1 — Action-string consolidation + bounded-domain chokepoint.** Migrate every inline
   audit-action literal into `AuditActions` (values byte-for-byte). Ship
   `NoMagicAuditActionStringsMetaTests` green-enforcing with a pinned exemption list + fail-path
   self-test + binding-floor. **First**, so later phases route through an enforced home.
2. **A2 — Auth event instrumentation.** `AuthController` writes `auth.login.success`,
   `auth.login.failure`, `auth.refresh.failure`, `auth.register` through `AuditService` (new
   constants; meta-test from A1 already enforcing).
3. **A3 — Audit IP under the pipeline + filter widening.** Move audit IP transformation to write time
   (`AuditService` applies `IIpHasher.Apply` once; controllers keep obtaining the raw IP) — retiring
   P2's "audit keeps raw" stopgap (D11). Widen `AuditLogQuery` + `AuditService.GetAuditLogAsync` +
   `StashRegistryDatabase.GetAuditLogAsync` to filter on `user/target/version/ip/from/to`; the `ip`
   filter routes the query value through the same `IIpHasher`. Flip the P2
   `NoMagicRemoteIpAccessMetaTests` exemption rationale for the audit-write files (stay exempt).
4. **A4 — Export (jsonl/csv).** `AdminController.ExportAuditLog` + `AuditExportFormat` enum +
   streamed writer, honoring the same filters.
5. **A5 — Retention sweep.** `AuditConfig` + `AuditBackgroundService` nightly sweep (own DI scope),
   separate knob from metrics retention.
6. **A6 — Tamper-evidence (hash chain + verify endpoint).** `previous_hash`/`entry_hash` EF columns,
   `AuditChainHasher` (one canonical serializer) + serialized append, `AdminController.VerifyAuditLog`
   + `GET /admin/audit-log/verify`, `AuditEntryResponse` gains the two optional hash fields.
7. **A7 — Discovery flag flip + OpenAPI + docs.** Add `DiscoveryFeatures.Audit` then flip it
   `true`; re-baseline `DiscoveryEndpointTests`; OpenAPI coverage gate green; update
   `Stash.Registry/CLAUDE.md` + `docs/Registry — Package Registry.md`.

## Open Questions

> **RESOLVED 2026-06-05 (user decision): all four open questions take their stated default.**
> OQ1 → two entries (creation record + `auth.register` event). OQ2 → canonical JSON. OQ3 → its own
> `Audit.TamperEvidence.HashSecret` knob (HMAC-SHA256 when set, plain SHA-256 otherwise). OQ4 →
> stream export unbounded. The implementer must build to these defaults and **not** re-surface these
> as undecided. Each item below is retained for rationale only.

- **OQ1 — `auth.register` vs `user.create` double-write.** `Register` already emits `user.create`
  via `LogUserCreateAsync`. The brief adds a distinct `auth.register` event for the self-service
  registration semantic. If the operator prefers a single entry, collapse `auth.register` into the
  existing `user.create` (drop the new event) — decide in A2. The brief's default is two entries
  (creation record + registration event).
- **OQ2 — Canonical payload format: fixed-order concatenation vs canonical JSON.** Both are
  deterministic. JSON is more self-describing and aligns with the JSONL export; concatenation is
  cheaper. Decide in A6; the only hard requirement is one serializer used by both write and verify.
  Lean: canonical JSON (reuses the export serialization mental model).
- **OQ3 — HMAC vs plain SHA-256 for the chain.** The brief makes the secret optional (HMAC when
  set, plain SHA-256 otherwise). Confirm whether the secret should reuse the P2 IP-hash secret
  persist-across-restarts infrastructure or be its own `Audit.TamperEvidence.HashSecret`. Lean:
  its own knob (the IP secret is a different concern), reusing P2's persist-if-missing helper if one
  was extracted. Decide in A6.
- **OQ4 — Export pagination cap.** Export streams the full filtered set (no `pageSize`). Confirm an
  operator dumping a multi-million-row log over HTTP is acceptable, or whether export should cap /
  require a `from`/`to` window. Lean: stream unbounded (admin-only, operator's own server); revisit
  if load testing shows a problem.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-05 | **Event coverage = migrate all inline literals + instrument the live-but-unaudited auth events** (`auth.login.success/failure`, `auth.refresh.failure`, `auth.register`). | The user's instruction — "find their call sites and **wiring** only those" — and the dead-code discriminator (live path exists ⇒ in; no path ⇒ out) settle it. `advisory.*`/`trusted_publisher.*`/`quarantine` are out (no path); login/refresh/register are in (live path, instrumenting is not dead code). Over-instrumenting is a one-line review fix; shipping "event coverage" that adds zero events is not. |
| 2026-06-05 | **Preserve every existing audit-action value byte-for-byte when migrating to constants** (`package.visibility_change` with the underscore, `token_theft_detected` with underscores, `token.revoked`). | Changing a value is a wire-breaking change pinned by `AuditServiceTests` / `RegistryAuthzAuditMutationTests`. This is the exact download-metrics regression (a shared-constant change passed a filtered verify, broke the controller wire contract). The gap doc's tidy names (`package.visibility.update`) are an aspirational menu — do NOT "fix" the live value to match. |
| 2026-06-05 | **Bounded-domain prevention = a new `NoMagicAuditActionStringsMetaTests` (Detect), built FIRST, not the centralization alone (Instruct).** | Migrating literals into `AuditActions` is a one-time sweep; nothing stops the next inline literal. `NoMagicAuthStringsMetaTests` is sink-targeted on *auth* sinks and does not catch audit-action literals. Per Construct > Detect > Instruct, the meta-test (modeled on the trusted `NoMagicAuthStrings` precedent: sink-scan + fail-path self-test + binding-floor + pinned exemptions) is the chokepoint and ships in the first phase so later phases route through an enforced home. |
| 2026-06-05 | **Detect (meta-test), not Construct (a single `enum AuditAction`), is the umbrella** — because the deny path writes `RegistryAction.ToString()`, an enum-derived vocabulary a pure `AuditAction` enum cannot cover. | The literal-event portion *is* enum-eligible (Make-It-Right: an `enum AuditAction` with `.ToWire()` is presented as the recommended upgrade for that portion); but the meta-test must still cover the dynamic deny vocabulary, so it is the load-bearing guard. The A1 implementer may upgrade the literal-event portion to an enum if it does not change any wire value and keeps the meta-test as the umbrella. |
| 2026-06-05 | **Tamper-evidence columns (`previous_hash`/`entry_hash`) do NOT violate D12's "not a schema change."** | D12 scopes "not a schema change" to the filter/event/export/retention work (the existing columns suffice). Tamper-evidence is named in scope **by D12 itself** and cannot exist without persisted hash fields (on-demand hashing of possibly-tampered data detects nothing). The two columns are the additive, append-only realization of D12's own tamper-evidence clause. Pre-release (D3) ⇒ zero backfill; pre-existing rows are pre-genesis (null hash). |
| 2026-06-05 | **Audit IPs are transformed at WRITE time — P3 brings audit under D11's IP pipeline, retiring P2's "audit keeps raw IP" stopgap. (The one design decision beyond the locked set.)** | Today every audit call site stores the raw IP; P2 listed these as "PERMANENT raw-IP keepers." But that was a *within-P2-scope* deferral — D11 ("operator-configurable, default hashed, **applies to both metrics and audit**") is the locked decision, and a feature brief cannot supersede a roadmap decision (milestone-charter rule). Store-raw/hash-on-read cannot support the `ip` filter (HMAC-in-SQL is impossible; the operator never sees a value to filter on), so the only coherent design that satisfies "reuse it for the `ip` filter / displayed IP" is write-time transform. The forensic "audit wants raw" need is met by `IpMode = raw`. Transform lives in ONE place (`AuditService`) so a new audit event cannot forget it (Construct). This **revises a prior shipped feature's documented invariant** and is surfaced to the user. |
| 2026-06-05 | **`ip` filter routes the operator-supplied IP through the SAME `IIpHasher.Apply`, matching the write-time-transformed stored value.** | Symmetric with the write side (one `IIpHasher`). Transforming the query value identically lets the operator type a real IP regardless of mode. `off` ⇒ column null ⇒ filter inert (documented). No second IP path. |
| 2026-06-05 | **P2 `NoMagicRemoteIpAccessMetaTests`: audit-write files STAY exempt, rationale flipped from "keeps raw IP" to "obtains raw IP, transformed downstream in `AuditService`" (shape (a)).** | Controllers/middleware keep *obtaining* the request IP from `HttpContext` (the layering-correct read site; a domain service reaching into `HttpContext` via `IHttpContextAccessor` is the anti-pattern). The single transform is downstream in `AuditService`. No exemption-list churn, no teeth/floor edit — just the rationale text. Rate-limit keying stays raw and exempt (needs raw, not an audit write). |
| 2026-06-05 | **Audit append is SERIALIZED (process-global async lock) when tamper-evidence is enabled; audit writes are durable-before-response, NOT the metrics `Channel` fire-and-forget pattern.** | The read-last-hash → compute → insert critical section must be atomic or concurrent appends fork the chain. SQLite serializes writes; PostgreSQL does not — design for the general case. Audit must be durable before the response (unlike download counts), so the queue pattern does not transfer. The lock cost is acceptable for the low-volume mutation/deny stream. |
| 2026-06-05 | **One canonical serializer (`AuditChainHasher.CanonicalPayload`) used by both write and verify; `id` and the hash fields are excluded from the hashed payload.** | Two divergent serializers is the classic omission bug. Excluding `id` (a surrogate that may differ across DBs) and the hash fields (which depend on the payload) keeps the hash a pure function of content. |
| 2026-06-05 | **Retention and tamper-evidence are independent knobs; the verifiable window is "retained ∧ hashed"; the retention sweep and verify agree on this.** | Audit retention ≠ metrics retention (user-locked separate knob). Deletion truncates the anchor but retained rows stay internally verifiable; verify treats the earliest retained hashed row's stored `previousHash` as the trusted anchor. Documented as intended truncation, not corruption. |
| 2026-06-05 | **Export and verify reuse `RegistryAction.ReadAuditLog`; no new PDP action.** | Both are "admin reads the audit surface." Same convention as download-metrics reusing `ReadAdminStats`. Splitting the closed PDP domain adds no benefit. |
| 2026-06-05 | **`Audit` discovery flag is ADDED to `DiscoveryFeatures` (pinned false) and FLIPPED true only in the final phase.** | User-locked addition (ninth flag) so website P4 feature-detects audit search. The DTO addition is itself a contract change (OpenAPI + discovery-snapshot re-baseline). Flipping only when whole keeps the flag truthful. |
| 2026-06-05 | **No non-admin audit read path; all audit reads are `[Authorize] + [RegistryAuthorize(ReadAuditLog)]`.** | A per-package/per-user audit feed would be an existence-leak surface. The user did not ask for one; we deliberately do not add one. |
