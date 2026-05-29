# Registry Feature Gaps - Self-Hosted Registry Roadmap

> **Status:** Backlog
> **Created:** 2026-05-28
> **Priority:** High
> **Discovery context:** Requested review of Stash's self-hosted package registry against mature package registry ecosystems. Focus areas included usage metrics, audit logs, supply-chain trust, governance, package visibility, and operational readiness.

> **⚠ Branching decisions locked 2026-05-29 — see [Locked Decisions](#locked-decisions-2026-05-29) at the end of this document.** That addendum is authoritative. Where it conflicts with an earlier section (notably the original *Recommended Roadmap*, *Suggested Card Split*, *Open Design Questions*, and Gaps §7/§8), the addendum wins; the earlier text is retained for context only. Spec from the addendum.

## Background

The Stash package registry already has a solid v1 package-feed foundation. The current reference document defines:

- Auth with short-lived JWT access tokens, rotating refresh tokens, and scoped API tokens.
- Public package metadata, version metadata, tarball downloads, publish, unpublish, and deprecation endpoints.
- Search over package names and descriptions.
- Owner management through admin endpoints.
- A basic admin stats endpoint.
- An audit log for state-changing operations.
- Rate limiting for auth, publish, download, search, and refresh categories.
- SQLite and PostgreSQL database configuration.
- Filesystem storage, with S3 configuration present but not implemented.

The registry is therefore not missing the basic mechanics of a package registry. The main gaps are the features that mature registries add around the package feed: trust, governance, visibility, analytics, operational observability, and security response.

This document captures the discovered gaps and a recommended roadmap so they can be split into implementation cards later.

## Existing Capability Snapshot

Current registry API surface:

- `POST /api/v1/auth/login`
- `POST /api/v1/auth/register`
- `POST /api/v1/auth/tokens/refresh`
- `GET /api/v1/auth/whoami`
- `POST /api/v1/auth/tokens`
- `GET /api/v1/auth/tokens`
- `DELETE /api/v1/auth/tokens/{id}`
- `GET /api/v1/packages/{name}`
- `GET /api/v1/packages/{name}/{version}`
- `GET /api/v1/packages/{name}/{version}/download`
- `PUT /api/v1/packages/{name}`
- `DELETE /api/v1/packages/{name}/{version}`
- Package and version deprecation / undeprecation endpoints.
- `GET /api/v1/search`
- `GET /api/v1/admin/stats`
- `POST /api/v1/admin/users`
- `DELETE /api/v1/admin/users/{username}`
- `PUT /api/v1/admin/packages/{name}/owners`
- `GET /api/v1/admin/audit-log`

Important current limitations:

- `GET /api/v1/admin/stats` currently returns only user count.
- The `read` token scope exists, but there are currently no authenticated read endpoints.
- Only local password auth is functional. LDAP and OIDC configuration exists but is stubbed.
- Only filesystem storage is functional. S3 storage is configured but not implemented.
- Only `sha256` integrity is supported.
- Audit logs cover state-changing operations, not read operations or all security-relevant events.
- Package ownership is package-level and admin-managed; there are no organizations, teams, invitations, or package-level role distinctions.

## External Registry Feature Discoveries

The following mature registry ecosystems informed this gap list:

- **npm:** scoped packages, organizations, granular access tokens, two-factor authentication, deprecation warnings, download statistics, provenance, package signatures, audit/signature checks.
- **PyPI:** project collaborators, organizations and teams, project-scoped API tokens, two-factor authentication, Trusted Publishing through OIDC, download statistics through public datasets.
- **NuGet:** package owners, package deprecation, vulnerability metadata, package signing, repository signing, package ID prefix reservation, download statistics.
- **GitHub Packages:** private/internal/public package visibility, inherited repository permissions, package activity/download visibility, organization audit logs.

The shared pattern: mature registries evolve from "store and fetch packages" into "govern, observe, and trust packages."

## High-Value Feature Gaps

### 1. Package Usage Metrics

**Gap:** The registry does not track or expose package usage beyond the existence of package/version rows. The admin stats endpoint currently returns only user count.

**Why it matters:** Package authors need adoption signals. Registry operators need traffic and storage insight. Users benefit from popularity and freshness signals during discovery.

**Recommended capabilities:**

- Track every tarball download as a durable event or aggregated counter.
- Store at least package name, version, timestamp bucket, requester identity when authenticated, IP hash or prefix, user agent, response status, and bytes served.
- Expose package-level metrics:
  - total downloads
  - downloads today
  - downloads in last 7 days
  - downloads in last 30 days
  - per-version totals
  - per-version recent windows
- Expose admin-wide metrics:
  - total packages
  - total versions
  - total users
  - total downloads
  - total storage bytes
  - recent publish count
  - recent unpublish/deprecation count
  - top packages by downloads
- Add metrics to search ranking and package metadata responses where appropriate.

**Potential endpoints:**

- `GET /api/v1/packages/{name}/metrics`
- `GET /api/v1/packages/{name}/{version}/metrics`
- `GET /api/v1/admin/metrics/downloads`
- Extend `GET /api/v1/admin/stats`

**Implementation notes:**

- Avoid writing one unbounded row per download forever unless retention and rollups are designed up front.
- Use daily/hourly rollup tables for normal queries.
- Keep raw download events optional or short-retention for operators that need detailed auditability.
- Downloads can be counted after a successful response starts or after stream completion. Define the semantics explicitly.

### 2. Richer Audit Logs

**Gap:** Stash has an audit log, but filtering is limited to package and action, and the action list omits several security-relevant events.

**Why it matters:** Self-hosted registries are often operated in corporate or infrastructure environments where "who changed what and when" is a first-class requirement.

**Recommended capabilities:**

- Add filters:
  - `user`
  - `target`
  - `package`
  - `version`
  - `action`
  - `ip`
  - `from`
  - `to`
  - `page`
  - `pageSize`
- Export audit logs as JSONL and CSV.
- Add retention configuration.
- Add optional external sink support:
  - webhook
  - file append
  - syslog
  - OpenTelemetry
  - SIEM-friendly JSON stream
- Add tamper-evident fields:
  - `previousHash`
  - `entryHash`
  - canonical serialized event payload
- Add more event types:
  - `auth.login.success`
  - `auth.login.failure`
  - `auth.refresh.failure`
  - `auth.register`
  - `user.role.update`
  - `user.password.change`
  - `user.disable`
  - `user.delete`
  - `token.create`
  - `token.revoke`
  - `token.expire`
  - `token.scope.denied`
  - `package.visibility.update`
  - `package.transfer.request`
  - `package.transfer.accept`
  - `package.quarantine`
  - `package.unquarantine`
  - `advisory.create`
  - `advisory.update`
  - `trusted_publisher.create`
  - `trusted_publisher.delete`

**Potential endpoints:**

- Extend `GET /api/v1/admin/audit-log`
- `GET /api/v1/admin/audit-log/export?format=jsonl`
- `GET /api/v1/admin/audit-log/export?format=csv`

**Implementation notes:**

- Keep audit schema append-only.
- Avoid logging full secrets, passwords, bearer tokens, refresh tokens, or raw package bodies.
- Prefer structured metadata over prose messages.

### 3. Organizations, Teams, and Package-Level Roles

**Gap:** Stash currently has users, global user/admin roles, and package owners. This is too coarse once several teams share one registry.

**Why it matters:** A self-hosted registry will likely be used by teams, CI systems, and internal platform groups. Global admin rights should not be required for normal package maintenance.

**Recommended capabilities:**

- Add organizations.
- Add organization membership.
- Add teams inside organizations.
- Add package ownership by user, team, or organization.
- Add package-level roles:
  - `owner`: full package administration, role changes, transfers
  - `maintainer`: publish, deprecate, manage versions
  - `publisher`: publish new versions only
  - `reader`: read private packages
- Add invitations:
  - invite user to package
  - invite user to organization
  - invite user to team
- Add package transfer workflows:
  - user to user
  - user to organization
  - organization to organization

**Potential endpoints:**

- `POST /api/v1/orgs`
- `GET /api/v1/orgs/{org}`
- `POST /api/v1/orgs/{org}/members`
- `DELETE /api/v1/orgs/{org}/members/{username}`
- `POST /api/v1/orgs/{org}/teams`
- `PUT /api/v1/packages/{name}/permissions`
- `POST /api/v1/packages/{name}/transfer`
- `POST /api/v1/packages/{name}/transfer/accept`

**Implementation notes:**

- This should replace the blunt admin-only owner mutation model over time, not sit beside it forever.
- Because Stash is pre-1.0, it is acceptable to break the ownership schema into a cleaner role assignment model.

### 4. Private Packages and Authenticated Reads

**Gap:** The `read` scope exists, but all package read endpoints are public today.

**Why it matters:** Private packages are one of the central reasons to run a self-hosted registry. Without authenticated reads, internal company packages need a separate mechanism or cannot be safely hosted.

**Recommended capabilities:**

- Add package visibility:
  - `public`
  - `private`
  - optionally `internal` for organization-visible packages
- Enforce visibility on:
  - package metadata
  - version metadata
  - tarball download
  - search results
  - metrics visibility
- Make `read` scope meaningful.
- Add reader permissions by user, team, organization, or package role.
- Decide whether unauthenticated search hides private packages completely or returns redacted stubs. Hiding is simpler and safer.

**Potential endpoints:**

- `PATCH /api/v1/packages/{name}/visibility`
- `GET /api/v1/packages/{name}/permissions`
- `PUT /api/v1/packages/{name}/permissions`

**Implementation notes:**

- This feature should be designed together with organizations/teams if possible.
- CLI auth refresh behavior already exists and can support authenticated installs.
- Search indexes must include visibility predicates.

### 5. Trusted Publishing Through OIDC

**Gap:** Publishing currently depends on user credentials or long-lived API tokens. CI users must store publish tokens.

**Why it matters:** Long-lived CI secrets are a common supply-chain risk. Mature registries increasingly support OIDC trusted publishing, where CI identity is exchanged for a short-lived publish credential.

**Recommended capabilities:**

- Add trusted publisher definitions per package:
  - provider, such as GitHub Actions, GitLab CI, Azure Pipelines
  - repository/project identifier
  - workflow or pipeline identifier
  - branch/tag/environment constraints
  - allowed package name
- Add OIDC token exchange endpoint.
- Issue short-lived, package-scoped publish tokens after verifying the provider token.
- Record provenance metadata on publish.

**Potential endpoints:**

- `POST /api/v1/packages/{name}/trusted-publishers`
- `GET /api/v1/packages/{name}/trusted-publishers`
- `DELETE /api/v1/packages/{name}/trusted-publishers/{id}`
- `POST /api/v1/auth/oidc/exchange`

**Implementation notes:**

- Start with GitHub Actions if only one provider is implemented first.
- Tokens issued through trusted publishing should be short-lived and should not be refreshable.
- Audit every exchange attempt.

### 6. Provenance and Package Signatures

**Gap:** Stash stores tarball integrity hashes, but there is no build provenance, package signing, or attestation model.

**Why it matters:** Hash integrity proves that bytes did not change after publish. It does not prove who built them, from what source, or in what CI environment.

**Recommended capabilities:**

- Store provenance fields per version:
  - source repository URL
  - commit SHA
  - CI provider
  - CI run ID / URL
  - workflow path
  - builder identity
  - trusted publisher ID
  - attestation payload
  - signature
- Add optional package signing.
- Add verification status to package/version metadata:
  - `provenance`: present / absent / verified / failed
  - `signature`: present / absent / verified / failed
- Add CLI verification:
  - `stash pkg verify <package>@<version>`
  - install-time warning for missing/failed provenance when policy requires it

**Potential endpoints:**

- `GET /api/v1/packages/{name}/{version}/provenance`
- `GET /api/v1/packages/{name}/{version}/signature`
- `POST /api/v1/packages/{name}/{version}/attest`

**Implementation notes:**

- Trusted publishing and provenance should share data models.
- Do not require signatures for every self-hosted registry by default; provide policy knobs.

### 7. Vulnerability Advisories and `stash pkg audit`

> **Superseded by a dedicated doc (decision D16).** The advisory/vulnerability model is owned in full by
> **`Registry Security Reports and Advisories - Vulnerability Handling Roadmap.md`** (report lifecycle, report/advisory/CVE statuses, coordinated disclosure, CLI/website surfaces). Spec advisories from that document, not from here, to avoid two conflicting advisory models.

**Gap:** The registry has no first-class security advisory or vulnerability model.

**Why it matters:** Registries are the natural place to publish and consume vulnerability metadata. Without this, Stash cannot warn users when dependency resolution selects known-vulnerable versions.

**Scope of this entry:** record the gap only. All recommended capabilities, the advisory schema, endpoints, resolver integration, and `stash pkg audit` are specified in the dedicated security doc above.

### 8. Reserved Namespaces and Verified Publishers

> **Largely dissolved into scope allocation (decisions D1/D4).** With mandatory `@scope/name` packages, there are no flat names to prefix-reserve — "reserving `stash-*`" becomes "owning the `@stash` scope." The open scopes-vs-prefix question is resolved in favour of npm-style scopes. The standalone reserved-prefix card is dropped; what remains here is verified-publisher metadata, which attaches to the scope owner (user or org).

**Gap:** Without namespace ownership, any first publisher can claim any name, enabling dependency confusion and impersonation. There is also no verified-publisher signal.

**Why it matters:** Scope ownership eliminates the flat-name land-grab. Verified-publisher signals improve trust in search and metadata pages.

**Recommended capabilities (post-decision):**

- Reservation is handled by scope allocation (see decisions **D1/D2/D4** in the [Locked Decisions](#locked-decisions-2026-05-29) addendum) — `@stash` and other protected scopes are owned, not pattern-matched. Note: §3 below ("Organizations, Teams, and Package-Level Roles") predates the scope decision and does not yet describe scopes; the scope model lives only in the addendum.
- Add verified-publisher metadata on the scope owner (user or org).
- Surface verified status in search and package metadata.

**Potential endpoints:**

- `PATCH /api/v1/orgs/{org}/verification`
- Scope allocation/claim endpoints are **not yet defined anywhere** — they are a TODO for the architect when speccing the org/scope foundation (P1).

## Strong Next-Layer Features

### 9. Dist-Tags and Release Channels

**Gap:** Stash has a single `latest` field. There is no equivalent of npm dist-tags or release channels.

**Recommended capabilities:**

- Tags such as:
  - `latest`
  - `beta`
  - `nightly`
  - `lts`
  - `preview`
- Install by tag:
  - `stash pkg install foo@beta`
- Move tags without republishing.

**Potential endpoints:**

- `GET /api/v1/packages/{name}/tags`
- `PUT /api/v1/packages/{name}/tags/{tag}`
- `DELETE /api/v1/packages/{name}/tags/{tag}`

### 10. Yank, Unlist, Quarantine, and Block

**Gap:** The lifecycle states are currently publish, unpublish within a window, and deprecate. There is no admin emergency state.

**Recommended capabilities:**

- Add package/version states:
  - `listed`
  - `unlisted`
  - `deprecated`
  - `yanked`
  - `quarantined`
  - `blocked`
- Define resolver behavior for each state.
- Let admins quarantine known-malicious or compromised versions.
- Let maintainers unlist packages from search without breaking existing lockfiles.

**Implementation notes:**

- `quarantined` should probably block install by default.
- `yanked` may allow install only when exact version is already locked.
- Keep deprecation as advisory, not blocking.

### 11. Ownership Workflows

**Gap:** Owner changes are admin-mediated and direct. There are no invitations, transfer requests, or last-owner protections documented.

**Recommended capabilities:**

- Owner invitation workflow.
- Maintainer invitation workflow.
- Package transfer workflow.
- Last-owner protection.
- Owner removal audit reasons.
- Optional maintainer self-service for adding publishers under policy.

### 12. Search and Discovery Improvements

**Gap:** Search currently covers names and descriptions with basic pagination.

**Recommended capabilities:**

- Search by:
  - keyword
  - owner
  - organization
  - license
  - Stash version compatibility
  - deprecated status
  - vulnerable status
  - visibility
- Sort by:
  - relevance
  - downloads
  - recently updated
  - recently published
  - name
- Include trust and health signals:
  - verified publisher
  - provenance verified
  - vulnerabilities
  - deprecated
  - last publish date

### 13. Webhooks and Event Stream

**Gap:** External systems cannot subscribe to registry events.

**Recommended capabilities:**

- Webhook subscriptions for:
  - publish
  - unpublish
  - deprecate
  - owner or role change
  - token create/revoke
  - advisory create/update
  - quarantine
  - package transfer
- Retry with exponential backoff.
- Signing secret for webhook payloads.
- Delivery log endpoint.

**Potential endpoints:**

- `POST /api/v1/admin/webhooks`
- `GET /api/v1/admin/webhooks`
- `DELETE /api/v1/admin/webhooks/{id}`
- `GET /api/v1/admin/webhooks/{id}/deliveries`

### 14. Operational Metrics

**Gap:** Package usage metrics and operator service metrics are different things. Stash currently has neither in meaningful form.

**Recommended capabilities:**

- Prometheus/OpenMetrics endpoint:
  - request count by route/status
  - request latency
  - auth failures
  - publish failures
  - download bytes
  - storage errors
  - database query latency
  - rate-limit hits
  - active requests
- Health/readiness endpoints:
  - process alive
  - database reachable
  - storage backend reachable
  - signing key configured

**Potential endpoints:**

- `GET /metrics`
- `GET /healthz`
- `GET /readyz`

### 15. Backup, Restore, and Storage Maturity

**Gap:** S3 is configured but not implemented. There is no documented backup/restore workflow.

**Recommended capabilities:**

- Implement S3/MinIO storage.
- Add storage migration tooling:
  - filesystem to S3
  - S3 to filesystem
- Add backup/export:
  - database dump
  - tarball export
  - metadata export
- Add restore/import.
- Add orphan cleanup:
  - tarball exists without DB row
  - DB row exists without tarball
- Add background integrity verification.

## Recommended Roadmap

> **Superseded by the post-decision roadmap in [Locked Decisions](#locked-decisions-2026-05-29).** The phase ordering below predates the namespace decisions (mandatory scopes, orgs-first, merged visibility), which invert "metrics first." Use the addendum's roadmap table; the phases below are kept for the original reasoning.

### Phase 1 - Observability and Metrics

Start with package usage metrics and richer admin stats.

Deliverables:

- Download counting.
- Per-package and per-version metric endpoints.
- Expanded `GET /api/v1/admin/stats`.
- Basic top packages report.
- Storage byte accounting.

Reasoning:

- This is high value, relatively low risk, and feeds discovery/search later.
- It does not require a full permission model redesign.

### Phase 2 - Private Packages and Real Read Scope

Make the existing `read` scope meaningful.

Deliverables:

- Package visibility field.
- Authenticated reads.
- Visibility-aware search.
- Read permissions for users and owners.

Reasoning:

- This is central to self-hosted registry adoption.
- It should happen before organizations if a minimal user/package model is acceptable, or together with organizations if the cleaner model is chosen.

### Phase 3 - Audit Log v2

Expand the existing audit system into an operator-grade compliance surface.

Deliverables:

- More filters.
- More auth and security event types.
- JSONL/CSV export.
- Retention policy.
- Optional tamper-evident hashes.

Reasoning:

- Stash already has the audit log seed.
- This makes the registry credible in managed environments.

### Phase 4 - Organizations, Teams, and Roles

Replace the coarse user/admin/owner model with package and organization permissions.

Deliverables:

- Organizations.
- Teams.
- Memberships.
- Package role assignments.
- Invitations.
- Transfer workflow.

Reasoning:

- This is a structural change and should be done cleanly while Stash is pre-1.0.

### Phase 5 - Trusted Publishing and Provenance

Reduce CI secret risk and add supply-chain trust metadata.

Deliverables:

- Trusted publisher definitions.
- OIDC token exchange.
- Short-lived package-scoped publish tokens.
- Publish provenance metadata.
- Provenance endpoints.
- CLI verification command or install-time provenance warnings.

Reasoning:

- This gives Stash a modern trust story early.
- It should build on the org/team/permission model.

### Phase 6 - Vulnerability Advisories and `stash pkg audit`

Let the registry warn users about known-bad dependency versions.

Deliverables:

- Advisory schema.
- Admin advisory endpoints.
- Package/version advisory endpoints.
- Resolver integration.
- `stash pkg audit`.
- Install-time warnings or policy-based failures.

Reasoning:

- This is one of the most user-visible safety features mature package managers provide.

### Phase 7 - Release Channels and Emergency Lifecycle States

Add dist-tags, yank/unlist/quarantine/block states, and resolver semantics.

Deliverables:

- Dist-tag endpoints.
- Install by tag.
- Lifecycle state schema.
- Admin quarantine.
- Unlist/yank behavior.

Reasoning:

- These improve release management and incident response without needing to delete immutable artifacts.

## Suggested Card Split

The following individual backlog cards can be created from this roadmap:

- Registry Metrics - Download Counting and Package Usage API
- Registry Admin Stats - Expand Beyond User Count
- Registry Audit Log v2 - Filters, Event Coverage, Export, Retention
- Registry Visibility - Private Packages and Authenticated Reads
- Registry Permissions - Organizations, Teams, and Package Roles
- Registry Trusted Publishing - OIDC Token Exchange for CI
- Registry Provenance - Publish Attestations and Verification Metadata
- ~~Registry Advisories - Vulnerability Database and Audit API~~ → owned by the dedicated security doc (D16); do not create a duplicate card here
- ~~CLI Package Audit - Consume Registry Advisories~~ → covered by the security doc + the CLI Evolution Plan
- ~~Registry Namespace Reservation - Prefixes and Verified Publishers~~ → folded into the org/scope card (D4); only verified-publisher metadata remains
- Registry Dist-Tags - Release Channels
- Registry Lifecycle States - Yank, Unlist, Quarantine, Block
- Registry Webhooks - Event Delivery and Delivery Logs
- Registry Operational Metrics - Prometheus/OpenMetrics
- Registry Storage - Implement S3/MinIO Backend
- Registry Backup and Restore - Export, Import, and Integrity Verification

## Open Design Questions

> **Resolved 2026-05-29 — see [Locked Decisions](#locked-decisions-2026-05-29).** All eight questions below have been answered (and two derived questions added). The list is retained for traceability; the addendum carries the resolutions.

- Should Stash package names support npm-style scopes like `@org/pkg`, or should the registry use NuGet-style prefix reservation while keeping the current name grammar?
- Should private package support be implemented before organizations, or should visibility wait for the full organization/team permission model?
- Should download metrics store raw events, rollups only, or both?
- Should IP addresses in metrics and audit logs be stored raw, truncated, hashed, or operator-configurable?
- Should audit log read events include package downloads, or should download metrics and audit logs remain separate surfaces?
- Should trusted publishing be limited to GitHub Actions first, or designed with a provider abstraction from the start?
- Should provenance be advisory by default, or can a registry operator require verified provenance for selected packages or prefixes?
- What install behavior should `quarantined`, `yanked`, and `unlisted` versions have when already present in a lockfile?

## Out of Scope for This Backlog Item

- Implementing any one of these features directly.
- ~~Changing the current `/api/v1` registry contract before a concrete feature spec exists.~~ → **Superseded by D20:** the scope migration breaks the v1 contract in place. This is now sanctioned and is the P1 deliverable.
- Editing generated standard library docs.
- Designing a public hosted Stash registry policy. This document is focused on the self-hosted registry.

## Locked Decisions (2026-05-29)

This addendum is **authoritative**. It records branching decisions taken in a design review and supersedes any conflicting earlier section. It is written to hand directly to the architect (`/spec`).

### Corrected current-state baselines

These were verified against the registry source and correct framings used earlier in this document:

- **Private packages are actively rejected, not merely absent.** `Stash.Registry/Services/PrivatePackageException.cs` maps a manifest with `"private": true` to HTTP 403 at publish (`PackagesController.cs`). Visibility work must *replace this rejection path*, not build on a blank slate.
- **Audit Log v2 is not a schema change.** `Database/Models/AuditEntry.cs` already carries `User`, `Target`, `Ip`, `Package`, `Version`, `Timestamp` and is append-only. Only `AuditService.GetAuditLogAsync` is narrow (filters package + action). v2 = widen the query + add new action *strings* (free-form column) + export/retention/tamper-evidence.
- **The `read` scope is inert.** `TokenRecord.Scope` accepts `"read"`, but `Startup.cs` defines only `RequirePublishScope`/`RequireAdminScope`/`RequireAdmin` — there is no `RequireReadScope` policy and no read endpoint enforces one.
- **Ownership is a flat user-only join.** `Database/Models/OwnerEntry.cs` is `(PackageName, Username)`. The org/team/role model replaces this table; it cannot extend it.
- **S3 and OIDC are explicit stubs** throwing `NotSupportedException` (`Storage/S3Storage.cs`, `Auth/OidcAuthProvider.cs`).
- **`GET /api/v1/admin/stats` returns only `{ Users }`** (`AdminController.cs`).

### Decision ledger

| ID | Decision | Notes / consequence |
| --- | --- | --- |
| **D1** | All packages use mandatory `@scope/name`. No flat names. | Breaking; acceptable pre-1.0. Touches grammar, resolver, lockfiles, every `/packages/{name}` route. |
| **D2** | Scope owner is **polymorphic: user or org**. `@username` auto-provisioned per user; `@orgname` per org. | Org/scope schema must model `scope_owner = user \| org` from the start. |
| **D3** | **No legacy migration — clean break.** Existing flat-named packages do not carry forward; there is no old-name→scoped-name alias and no backward compatibility. References to pre-scope names simply stop resolving. | Justified by pre-1.0 status. Dissolves both former migration residuals (no alias subsystem to build; no multi-owner tie-break, since nothing is re-homed). |
| **D4** | Reserved-prefix card dissolves into scope allocation. `stash-*` → own the `@stash` scope. Drop standalone flat-prefix reservation. | Only verified-publisher metadata survives from old §8. |
| **D5** | Visibility (`public`/`private`/`internal`) ships **with** the org/team/role schema — one migration. | Not a user-scoped pre-step. |
| **D6** | Add the missing `RequireReadScope` policy; enforce on metadata/version/download/search. | Makes the `read` scope meaningful. |
| **D7** | Replace the publish-time 403 on `"private": true` with the visibility model. | Behavior reversal on a live path. |
| **D8** | Download metrics = short-retention raw events + daily/hourly rollups. | Raw TTL ~30–90d; rollups permanent. Retention job required. |
| **D9** | Metrics read path carries a visibility gate from day one. | Avoids retrofitting auth onto already-public metrics endpoints. |
| **D10** | "Storage bytes" reads a DB column written at publish, not a filesystem stat. | Backend-agnostic for future S3. |
| **D11** | IP handling operator-configurable (`raw\|truncated\|hashed\|off`), default **hashed** (HMAC + server secret). | Applies to both metrics and audit. |
| **D12** | Audit Log v2 = query plumbing + new action strings + export/retention/tamper-evidence. **Not** a schema change. | See corrected baseline above. |
| **D13** | Downloads stay **out** of the audit log (separate surface). | Keeps audit low-volume and its tamper chain clean. |
| **D14** | Trusted publishing: provider abstraction, **GitHub Actions first**. | Mirror existing `IAuthProvider`/`IPackageStorage` stub pattern. |
| **D15** | Provenance **advisory by default** + per-prefix/per-package enforce knob. | Don't break existing publish flows on day one. |
| **D16** | Advisories owned by `Registry Security Reports and Advisories…` as source-of-truth. | §7 here is a pointer only. |
| **D17** | Lifecycle install semantics: `quarantine` blocks always (even locked) · `yank` installs only-if-locked · `unlist` hides-not-blocks · `deprecate` advisory. | Resolver behavior spec. |
| **D18** | This roadmap is the **master index**; deep-dives live in the sibling docs (see *Related Documents*). | Prevents duplicate/conflicting specs. |
| **D19** | **Unified scope namespace.** A scope name maps to exactly one owner (user *or* org, never both); usernames and org names share one pool and cannot collide. System scopes (`@stash`, `@admin`, …) reserved at bootstrap. | Schema: single `scopes` table with `owner_type`. Registration must reject an org name that collides with an existing username and vice versa. |
| **D20** | **Break `/api/v1` in place.** Flat `/packages/{name}` routes are replaced by scoped `/packages/{scope}/{name}` routes under v1; no parallel old contract, no `/api/v2`. | Consistent with D3 clean break. Existing registry data does not carry forward — operators republish under scopes. Supersedes the old *Out of Scope* guard on the v1 contract. |

### Migration — resolved (clean break, no migration)

Both former residuals are **closed** by the clean-break decision (D3):

- **Name-alias / backward resolution — not built.** Old flat names hard-break. There is no alias table and no resolver shim; references to pre-scope names (`foo`) simply stop resolving. Accepted breakage, justified by pre-1.0 status.
- **Multi-owner tie-break — moot.** Because nothing is re-homed, the "which owner's scope does `foo` migrate into" question disappears with the migration itself.

### Resolution of the original Open Design Questions

1. Scopes vs prefix reservation → **npm-style scopes** (D1).
2. Visibility before or with orgs → **with orgs** (D5).
3. Metrics raw / rollup / both → **both** (D8).
4. IP raw / truncated / hashed / configurable → **operator-configurable, default hashed** (D11).
5. Audit read events include downloads → **no, separate** (D13).
6. Trusted publishing single-provider vs abstraction → **abstraction, GitHub first** (D14).
7. Provenance advisory vs enforceable → **advisory + enforce knob** (D15).
8. Lockfile behavior for quarantined/yanked/unlisted → **D17**.

Derived questions added by the scope decision:

- Unscoped names coexist vs mandatory → **mandatory scopes** (D1).
- New Phase 1 → **org/scope foundation first** (revised roadmap below).
- Personal vs org-only scopes → **personal + org scopes** (D2).
- Scope namespace collision (user vs org) → **unified namespace + reserved system scopes** (D19).
- API contract break handling → **break `/api/v1` in place** (D20).

### Revised roadmap (post-decision)

| Phase | Content | Note |
| --- | --- | --- |
| **P1** | Orgs + teams + roles + **mandatory scopes** (polymorphic owner) + visibility + `RequireReadScope` + **legacy migration** | New foundation; the largest/riskiest block now goes first. Architect should decompose into checkpoint sub-phases (e.g. scope grammar → org/role schema → visibility enforcement → migration tooling). |
| **P2** | Download metrics (raw + rollup, visibility-gated, scoped routes) + expanded `GET /admin/stats` | Built on scoped routes from the start (no re-pathing). |
| **P3** | Audit Log v2 | Cheap — schema already present (D12). |
| **P4** | Trusted publishing + provenance | Builds on P1 orgs. |
| **P5** | Advisories + `stash pkg audit` | Per D16; spec from the security doc. |
| **P6** | Dist-tags + lifecycle states | Per D17. |
| *Cross-cutting* | S3/MinIO storage, backup/restore, webhooks, operational metrics (`/metrics`, `/healthz`, `/readyz`) | Independent of the namespace work; slot opportunistically. |

### Related Documents (D18)

This document is the index. Spec the following areas from their dedicated docs, not from the summaries here:

- **Advisories / vulnerability handling** → `Registry Security Reports and Advisories - Vulnerability Handling Roadmap.md` (source-of-truth for the advisory model; §7 here is a pointer).
- **CLI surface** (`stash pkg audit`, `stash pkg verify`, install-by-tag, daily-workflow reliability) → `Registry and Package CLI - Incremental Evolution Plan.md`.
- **Web client + the read-facing API shape** (metrics/visibility/audit as consumed by a website) → `Registry Website - Optional Web Client and API Readiness.md` (its "Registry API Changes Needed" section overlaps Gaps §1/§2/§4 — reconcile, don't duplicate).

