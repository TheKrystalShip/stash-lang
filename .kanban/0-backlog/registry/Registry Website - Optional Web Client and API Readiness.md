# Registry Website - Optional Web Client and API Readiness

> **Status:** Backlog
> **Created:** 2026-05-28
> **Priority:** Medium
> **Discovery context:** Follow-up to the self-hosted registry feature gap analysis. Question: whether Stash should eventually provide a website where users can see, search, and interact with packages, similar to npmjs.com, NuGet.org, PyPI, or GitHub Packages, while keeping the registry as a standalone API.

## Executive Summary

Yes, Stash should eventually provide an optional package registry website.

A registry without a web surface eventually feels opaque. Users need a place to discover packages, inspect versions, read package documentation, evaluate trust signals, understand dependency health, and manage package ownership without memorizing every CLI command or raw API endpoint.

The website must remain optional. The registry should stay a standalone API service, and the website should be just another client of that API. Operators should be able to run:

- the registry alone,
- the website pointed at an existing registry,
- both behind the same reverse proxy,
- or neither public-facing, using only the CLI/API.

The main work before building the website is not visual design. It is making the registry API web-client-friendly without coupling the API to a particular frontend.

## Motivation

Mature package ecosystems all provide a human-facing package browsing experience:

- npm has package pages, README rendering, versions, provenance indicators, maintainers, download/activity signals, and search.
- NuGet.org has package detail pages, README rendering, dependencies, supported frameworks, download statistics, package owners, vulnerabilities, and report/contact flows.
- PyPI has project pages, metadata, project links, verified/unverified project details, release files, maintainers, and vulnerability-related integrations.
- GitHub Packages exposes package pages with versions, visibility, package metadata, and download/activity information.

The pattern is consistent: the CLI is excellent for workflows, but the website is where users evaluate packages.

For Stash, a website would help with:

- Package discovery.
- Ecosystem credibility.
- Trust and safety transparency.
- Package author visibility.
- Easier administration of self-hosted registries.
- Lower onboarding friction for users who are not yet comfortable with `stash pkg`.
- Presenting future features such as metrics, advisories, provenance, verified publishers, private packages, organizations, teams, and audit logs.

## Core Constraint

The registry remains the source of truth.

The website must not:

- own registry data,
- talk directly to the registry database,
- import EF Core database models from `Stash.Registry`,
- require the registry to run in a web-hosted UI mode,
- make package operations available only through website-specific endpoints,
- or become a mandatory deployment dependency.

The website may:

- use the registry's documented REST API,
- use generated API clients from OpenAPI,
- use its own session/auth layer if needed,
- cache read-only API responses,
- render README and package metadata,
- and provide UI workflows over the same operations exposed to CLI/API users.

## Recommended Architecture

### Projects

Suggested shape:

- `Stash.Registry`
  - Standalone API server.
  - Owns database, storage backends, auth, package operations, permissions, audit logs, metrics, and registry policy.
- `Stash.Registry.Web`
  - Optional web application.
  - Talks only to `Stash.Registry` through documented HTTP APIs.
  - Can be deployed separately, behind the same reverse proxy, or not deployed at all.
- Optional shared generated client package:
  - generated from OpenAPI,
  - DTO-only,
  - no EF Core entities,
  - no database access.

### Deployment Models

Supported deployment targets:

- API only:
  - `registry.example.com/api/v1`
- Website plus API behind same origin:
  - `registry.example.com/`
  - `registry.example.com/api/v1`
- Website and API on separate origins:
  - `packages.example.com`
  - `registry-api.example.com/api/v1`
- Local development:
  - website dev server points at local registry API.

### Coupling Rules

- Website features must map to registry API endpoints.
- Registry API endpoints must be documented independently of the website.
- Website-specific presentation choices must not leak into API response names.
- Registry responses may expose capabilities and structured metadata, but not UI layout.
- Any action available in the website must also be available to another API client with appropriate credentials.

## Website Capabilities

### Public Package Discovery

The website should provide:

- Search page.
- Package detail page.
- Version detail page.
- Owner/user package listings.
- Organization package listings, if organizations exist.
- Keyword/license/compatibility filtering.
- Sort by relevance, downloads, recently updated, recently published, and name.

Package cards and result rows should expose concise signals:

- name
- description
- latest version
- last updated
- downloads
- owners or organization
- license
- keywords
- deprecated status
- vulnerability status
- verified publisher status
- provenance status

### Package Detail Pages

A package page should include:

- README rendering.
- Description.
- Latest version.
- Installation command.
- Owner/maintainer list.
- License.
- Repository link.
- Documentation link.
- Homepage link.
- Keywords.
- Version list.
- Dependencies for selected/latest version.
- Stash runtime version requirement.
- Download statistics.
- Deprecation message and alternative, if present.
- Vulnerability/advisory summary, if present.
- Provenance and signature status, if present.
- Verified publisher signal, if present.
- Visibility, if the viewer has permission to see it.

### Version Pages

A version page should include:

- version string
- publication timestamp
- publisher
- dependencies
- Stash version requirement
- integrity hash
- tarball size
- download URL
- provenance metadata
- signature metadata
- advisories affecting that version
- deprecation status
- lifecycle state, such as yanked/quarantined/unlisted, if implemented

### Authenticated User Workflows

For regular users and maintainers:

- Login / logout.
- View current user.
- Manage API tokens.
- View owned packages.
- View package permissions.
- Invite owners/maintainers/publishers, once supported.
- Change package visibility, once supported.
- Manage trusted publishers, once supported.
- Deprecate package/version.
- Manage dist-tags, once supported.
- View package metrics.

### Admin Workflows

For registry admins:

- User management.
- Role management.
- Package owner/permission management.
- Audit log search/export.
- Advisory management.
- Reserved prefix management.
- Quarantine/block package versions.
- Webhook management.
- Registry stats and operational metrics.
- Storage health and integrity reports.

## Registry API Changes Needed

The registry already has a useful v1 API, but a web client will need a few API-first improvements.

### 1. Stable Public Read API

Package detail pages should not require fragile stitching of many undocumented responses.

Recommended endpoints:

- `GET /api/v1/packages/{name}`
- `GET /api/v1/packages/{name}/readme`
- `GET /api/v1/packages/{name}/versions`
- `GET /api/v1/packages/{name}/{version}`
- `GET /api/v1/packages/{name}/{version}/download`
- `GET /api/v1/packages/{name}/{version}/provenance`
- `GET /api/v1/packages/{name}/advisories`
- `GET /api/v1/packages/{name}/metrics`

The existing package metadata endpoint can remain, but subresources make the website easier to cache and evolve.

### 2. Search API Designed for UI

Current search covers names and descriptions. A website needs richer filtering and sorting.

Recommended query parameters:

- `q`
- `keyword`
- `owner`
- `org`
- `license`
- `deprecated`
- `vulnerable`
- `verified`
- `provenance`
- `visibility`
- `stashVersion`
- `sort`
- `page`
- `pageSize`

Recommended sort values:

- `relevance`
- `downloads`
- `updated`
- `published`
- `name`

Potential endpoint:

- `GET /api/v1/search`

### 3. Consistent Pagination

Every list endpoint should use one pagination model.

Applies to:

- search results
- versions
- packages by user
- packages by organization
- audit logs
- advisories
- tokens
- webhooks
- webhook delivery logs
- trusted publishers

Recommended response shape:

```json
{
  "items": [],
  "totalCount": 0,
  "page": 1,
  "pageSize": 20,
  "totalPages": 0
}
```

### 4. Browser-Friendly Auth Strategy

The registry currently uses bearer tokens, which is appropriate for CLI and API clients. A browser frontend needs a careful auth model.

Preferred approach:

- The website owns browser sessions.
- The website backend stores secure HTTP-only session cookies.
- The website backend calls the registry API using registry tokens or an OAuth-style exchange.
- The registry remains token/API-native.

Alternative approach:

- The registry directly supports browser auth:
  - secure cookies
  - CSRF protection
  - same-site cookie settings
  - CORS
  - logout/session revocation endpoints

Recommendation:

Use a website backend-for-frontend. This keeps the registry clean and avoids turning the API server into a browser session manager.

### 5. CORS and API Base Discovery

If the website and registry are hosted on separate origins, the registry needs configurable CORS.

Recommended configuration:

- `Cors.Enabled`
- `Cors.AllowedOrigins`
- `Cors.AllowedMethods`
- `Cors.AllowedHeaders`
- `Cors.AllowCredentials`

Recommended discovery endpoint:

- `GET /api/v1/.well-known/registry`

Example response:

```json
{
  "name": "Stash Registry",
  "apiVersion": "v1",
  "basePath": "/api/v1",
  "features": {
    "privatePackages": true,
    "organizations": false,
    "metrics": true,
    "advisories": false,
    "trustedPublishing": false,
    "provenance": false
  },
  "limits": {
    "maxPackageSize": 10485760,
    "maxPageSize": 100
  },
  "links": {
    "search": "/api/v1/search",
    "login": "/api/v1/auth/login",
    "packages": "/api/v1/packages"
  }
}
```

### 6. Link Fields and Canonical URLs

Responses should include structured links where useful.

Examples:

- package metadata URL
- README URL
- version metadata URL
- tarball download URL
- repository URL
- documentation URL
- homepage URL
- provenance URL
- advisory URL
- metrics URL

These should be API affordances, not website-specific page routes.

### 7. README and Markdown Safety

The registry should store and expose raw README content. The website should sanitize and render it.

Recommended endpoint:

- `GET /api/v1/packages/{name}/readme`

Recommended metadata:

- content type
- source filename
- byte size
- extracted from version
- ETag
- last modified timestamp

Security notes:

- Do not trust package-provided HTML.
- Sanitize rendered Markdown.
- Rewrite or block unsafe links.
- Avoid loading remote scripts, iframes, or inline event handlers.
- Consider limiting README size.

### 8. ETags and Cache Headers

A website will repeatedly hit package and search endpoints. Add HTTP caching support.

Recommended headers:

- `ETag`
- `Last-Modified`
- `Cache-Control`

Good candidates:

- package metadata
- README
- version metadata
- search results
- metrics rollups
- tarball downloads

### 9. Trust and Health Fields

Website pages need compact trust signals.

Recommended metadata fields:

- `deprecated`
- `deprecationMessage`
- `deprecationAlternative`
- `vulnerable`
- `advisories`
- `quarantined`
- `yanked`
- `verifiedPublisher`
- `provenanceStatus`
- `signatureStatus`
- `downloadsTotal`
- `downloads30d`
- `lastPublishedAt`
- `ownerCount`
- `license`

These are useful for CLI output too, so they should live in API responses rather than in a website-only data model.

### 10. Owner, User, and Organization APIs

The website will need profile and listing pages.

Potential endpoints:

- `GET /api/v1/users/{username}`
- `GET /api/v1/users/{username}/packages`
- `GET /api/v1/orgs/{org}`
- `GET /api/v1/orgs/{org}/packages`
- `GET /api/v1/orgs/{org}/members`

Privacy note:

For self-hosted registries, user profile visibility should be configurable or minimal by default.

### 11. Admin API Completeness

The website should not need private backdoors for admin actions.

Admin API should eventually cover:

- users
- roles
- package permissions
- audit logs
- advisories
- reserved prefixes
- quarantine/block state
- webhook management
- operational metrics
- storage health
- integrity checks

### 12. OpenAPI Schema

The registry should publish an OpenAPI document.

Potential endpoint:

- `GET /api/v1/openapi.json`

Benefits:

- Website can generate a typed client.
- CLI can share request/response contracts.
- Third-party operators can integrate without reverse-engineering.
- API drift becomes more visible in review.

## Website-Specific Non-Goals for the Registry

The registry API should not expose:

- homepage card layouts,
- CSS or theme settings,
- website route names,
- frontend-specific view models,
- HTML-rendered README as the only README format,
- website-only auth/session semantics,
- or hidden endpoints used only by the web UI.

The registry should expose capabilities and structured data. The website decides presentation.

## Security Considerations

### Browser Auth

If the website has a backend, prefer HTTP-only secure cookies for browser sessions. Avoid storing registry bearer tokens in local storage.

### CORS

CORS must be explicit, configurable, and off or restrictive by default.

### CSRF

If browser cookies are used for state-changing actions, CSRF protection is required.

### README Rendering

README content is attacker-controlled package content. Treat it as hostile input.

### Private Package Leakage

Search, user pages, organization pages, metrics, and package detail endpoints must respect visibility and permissions.

### Metrics Privacy

Download metrics can leak package existence or internal project activity. Private package metrics should require authorization.

### Audit Logs

Admin audit log pages must not expose raw secrets, bearer tokens, refresh tokens, passwords, or sensitive request bodies.

## Data and API Readiness Checklist

- [ ] Package metadata includes all fields needed for a package detail page.
- [ ] README has a dedicated endpoint with cache validators.
- [ ] Version lists are paginated or bounded.
- [ ] Search supports UI filters and sort modes.
- [ ] Package metrics exist and respect visibility.
- [ ] Trust fields are available in metadata responses.
- [ ] Advisories are queryable by package/version.
- [ ] Provenance/signature data is queryable by version.
- [ ] User/org package listing endpoints exist.
- [ ] Admin endpoints cover all planned website admin workflows.
- [ ] CORS is configurable.
- [ ] API discovery endpoint exists.
- [ ] OpenAPI schema is published.
- [ ] Browser auth strategy is chosen.

## Suggested Implementation Phases

### Phase 1 - API Readiness

Before building UI:

- Add OpenAPI generation.
- Add discovery endpoint.
- Add CORS configuration.
- Add ETags/Last-Modified to package metadata and README.
- Add dedicated README endpoint.
- Expand search API.

### Phase 2 - Public Browse-Only Website

Initial optional website:

- Search page.
- Package detail page.
- Version detail page.
- README rendering.
- Basic package metadata.
- No login required.

This phase can launch while private packages, orgs, and trusted publishing are still future work.

### Phase 3 - Authenticated Maintainer Website

Add login-backed workflows:

- API token management.
- Owned packages list.
- Deprecation actions.
- Package settings.
- Metrics pages.

### Phase 4 - Admin Website

Add admin-only workflows:

- User management.
- Audit log search/export.
- Advisory management.
- Package quarantine/block.
- Reserved prefixes.
- Webhooks.
- Operational metrics.

### Phase 5 - Trust and Security UX

Expose advanced signals:

- provenance verification
- signature verification
- trusted publisher configuration
- advisories
- verified publishers
- package lifecycle states

## Open Design Questions

- Should the website be a separate project in this repo or a separate repository?
- Should it be static frontend plus registry API, or a server-rendered/backend-for-frontend app?
- Should browser users authenticate directly with the registry or through a website-owned session layer?
- Should package pages be accessible at clean routes like `/packages/foo`, while API remains under `/api/v1/packages/foo`?
- Should README rendering happen server-side in the website or client-side?
- Should the website support multiple registry backends, or exactly one configured registry?
- Should a self-hosted operator be able to disable public browsing while keeping authenticated browsing?
- Should private package names be hidden from search entirely or shown as redacted results to authorized users only?

## References

- Stash registry reference: `docs/Registry - Package Registry.md`
- Registry feature gap roadmap: `.kanban/0-backlog/packages/Registry Feature Gaps - Self-Hosted Registry Roadmap.md`
- npm package pages and provenance indicators: https://docs.npmjs.com/viewing-package-provenance/
- npm package access and token model: https://docs.npmjs.com/about-access-tokens/
- NuGet package discovery and package details: https://learn.microsoft.com/nuget/consume-packages/finding-and-choosing-packages
- NuGet vulnerability information API: https://learn.microsoft.com/en-us/nuget/api/vulnerability-info
- PyPI project metadata behavior: https://docs.pypi.org/project_metadata/
- PyPI JSON API: https://docs.pypi.org/api/json/
- GitHub Packages viewing packages: https://docs.github.com/en/packages/learn-github-packages/viewing-packages
- GitHub Packages REST API: https://docs.github.com/rest/reference/packages

## Out of Scope

- Building the website now.
- Choosing a frontend framework now.
- Designing final visual branding.
- Changing registry auth semantics before a browser auth strategy is selected.
- Merging website-only view models into the registry API.

---

## API Readiness Evaluation (2026-06-04)

> Assessed against the actual code (`Stash.Registry/Controllers/*`, `Stash.Registry.Contracts/*`,
> `Stash.Registry/Startup.cs`, `Database/Models/*`), not against this brief's assumptions. Where the
> brief and the code disagree, the code wins.

### Verdict (read this first)

**The answer is phase-conditioned, not yes/no.**

- **For an anonymous, browse-only website (this brief's Phase 2 — search, package page, version page,
  README), the API is *close*.** The data already exists; it needs a small "reshape what's there"
  polish slice plus a deployment decision (same-origin or a backend-for-frontend). It is **not**
  buildable *as-is* with good UX, but the gap is days, not a quarter.
- **For the maintainer / admin / trust-and-safety website (Phases 3–5), the API is *not ready*, and
  the missing pieces are not API polish — they are product features that do not exist at all**
  (download metrics, advisories, provenance, signatures, verified/trusted publishers, quarantine/yank
  lifecycle, webhooks, user profiles, full admin surface). The website would *surface* those features;
  it cannot be the reason they get built.

So this brief's central thesis — *"the main work before the website is making the API
web-client-friendly"* — is right for Phase 2 and **understated for Phases 3–5**: a large fraction of
the checklist is "build a feature," not "expose existing data web-friendly." The single most useful
reframe this evaluation adds is to **split the checklist into two buckets** (below), because lumping
them on one list hides that one bucket is cheap and the other is a roadmap.

### What the brief under-credits (already done — and these are the load-bearing ones)

- **OpenAPI is published today.** `Startup.cs` calls `AddOpenApi()` + `MapOpenApi()`; the document is
  served at `GET /openapi/v1.json`, **public in every environment** (the `IsDevelopment()` gate was
  removed in "P1"), with an operation-id transformer, a metadata transformer, and an
  `OpenApiCoverageMetaTests` gate. (One binary tarball-download operation is intentionally exempt from
  the typed-schema coverage requirement — so the codegen claim is "complete except one stream
  endpoint," not 100%.) Checklist item *"OpenAPI schema is published"* = **done**. The brief lists it
  as needed (§12) — stale. Note the path is `/openapi/v1.json`, not the brief's `/api/v1/openapi.json`.
- **The expensive architectural constraint is already satisfied.** Wire DTOs live in a dependency-free
  `Stash.Registry.Contracts` assembly with no EF entities and no view models; the same types are
  consumed by the CLI. The brief's core rule — *"any action available in the website must be available
  to any API client; no website-only endpoints; no UI leakage"* — holds today. This is the costly
  thing to retrofit, and it's done.
- **Read-path visibility enforcement already exists.** Search passes `callerUsername` into a
  PDP-backed predicate (anonymous callers see only public packages); package/version detail map
  `VisibilityHidden → 404` to avoid leaking existence. The brief's biggest security worry (§Security →
  Private Package Leakage) is **already handled on the surfaces that exist today.** Forward constraint:
  every new Bucket-B listing/metrics endpoint MUST replicate this same predicate.

These three are why a raw "1 of 14 checklist items done" score is misleading — the structural
foundation a website needs is in place; what's missing is breadth, not architecture.

### Bucket A — true API-readiness polish (data exists; reshape/expose it). Unblocks Phase 2.

| # | Item | State today | Work |
| - | ---- | ----------- | ---- |
| 2 | Dedicated README endpoint + cache validators | README is embedded in `PackageDetailResponse.readme` only; no `/readme` route, no ETag/Last-Modified anywhere in the codebase | New thin GET endpoint over existing `PackageRecord.Readme` + ETag |
| 3 | Version lists paginated/bounded | `PackageDetailResponse.versions` is an **unbounded dict** inlined in package detail; no dedicated versions route | Add `GET …/versions` with the shared envelope |
| 4 (partial) | Search filters + sort — the **column-backed** half | Today only `q`/`page`/`pageSize`. Backed by existing columns: `keyword`, `license`, `deprecated`, `owner` (derivable from `PackageRoleEntry`); sorts `name`/`updated`/`published`/`relevance` | Extend `SearchQuery` + the SQL predicate |
| 6 (partial) | Summary-row trust fields — the **reshape** half | `PackageSummaryResponse` lacks `license` and `ownerCount`, both of which exist on the records | Add to the summary DTO |
| 3-wide | One consistent pagination envelope | Fields already match (`totalCount/page/pageSize/totalPages`) but the **collection key differs** (`packages` vs `entries`) and **caps differ** (search 100, audit 200); no shared base type | Extract a shared `PagedResponse<T>` |
| 5-cfg | CORS configurable | **Absent** (no `AddCors`/`UseCors`). Only blocks the *separate-origins* deployment; a same-origin or BFF site needs nothing here | Add config-driven CORS (off by default) |
| 5-disc | Discovery endpoint (`.well-known/registry`) | **Absent** | Small static-ish endpoint advertising features/limits |
| 6/8 | Link fields + ETag/Cache-Control on read endpoints | **Absent** | Add headers/affordances over existing data |

### Bucket B — new product features (no backing data exists). Blocks Phases 3–5; independent of "web-readiness."

Confirmed absent from the schema and the entire registry source tree (grep: zero hits beyond
incidental "signature"=JWT):

| Checklist item | Reality |
| -------------- | ------- |
| 5. Package metrics / downloads | **No download counter, no metrics column, no endpoint.** Nothing tracks downloads at all. |
| 7. Advisories queryable | No advisory model/table/endpoint. |
| 8. Provenance / signature per version | No provenance or package-signing infrastructure. |
| 9. User & org package-listing endpoints | No `GET /users/{username}`, no `…/packages`, no `GET /orgs/{org}/packages`. Only `GET /orgs/{org}` (detail) exists. |
| 10. Admin completeness | Have: stats, user create/delete, package-role override, audit-log. Missing: role mgmt, advisories, reserved prefixes, quarantine/block, webhooks, storage health, integrity checks. |
| 6 (rest) | Trust fields `vulnerable`/`verifiedPublisher`/`provenanceStatus`/`signatureStatus`/`quarantined`/`yanked`/`downloads*` | None exist. |
| 4 (rest) | Search filters `vulnerable`/`verified`/`provenance` + sort-by-`downloads` | Wait on the features above — **do not scope these into "expand search."** |

> **The straddle that matters for whoever specs Phase 1:** "Expand search" and "add trust fields" each
> span *both* buckets. The column-backed half is cheap (Bucket A); the `vulnerable`/`verified`/
> `provenance`/`downloads` half is gated on unbuilt features (Bucket B). Scope only the Bucket-A half
> into an API-readiness phase, or it will look small and land half-impossible.

### The one decision that isn't code: browser auth (checklist 14)

The registry is bearer/JWT-only — correct for CLI, and **correct for this brief's own recommended
architecture (a website backend-for-frontend).** Under a BFF (or same-origin) the registry needs *no*
auth change: the BFF holds the browser session and calls the registry with a token. Only the
*direct-to-registry-from-browser* alternative would force cookies/CSRF/CORS/session-revocation into the
API. **Recommendation: don't treat "browser auth" as a registry blocker** — pick the BFF the brief
already prefers, and this item costs the registry nothing.

### Recommended minimal "Phase 1 — API Readiness" slice (all Bucket A)

Smallest scope that makes Phase 2 a credible browse site, in dependency order:

1. **Shared `PagedResponse<T>` envelope** (collapse `packages`/`entries`, unify caps) — everything else
   that lists reuses it.
2. **`GET …/versions`** (paginated) + **`GET …/readme`**, both with **ETag/Last-Modified**.
3. **Search v2:** column-backed filters (`keyword`, `license`, `deprecated`, `owner`) + sorts
   (`name`/`updated`/`published`/`relevance`); add `license`+`ownerCount` to summary rows.
4. **CORS config** (off by default) — only if separate-origins is a target; skip for BFF/same-origin.
5. **`GET /api/v1/.well-known/registry`** discovery endpoint (advertise features so the client can
   feature-detect as Bucket-B lands incrementally).

Defer everything in Bucket B to the existing self-hosted-registry roadmap; the website surfaces those
as they ship, gated by the discovery endpoint's `features{}` map.

### Minor corrections to this brief

- §12 / checklist 13 (OpenAPI) is **already done** — see above.
- Endpoint shapes assume single-segment names (`/packages/{name}`); the real routes are **two-segment
  scoped** (`/packages/{scope}/{name}`). A web client/BFF must handle `@scope/name`.
- The "Registry feature gap roadmap" reference points at `0-backlog/packages/…`; the file actually
  lives at **`.kanban/0-backlog/registry/Registry Feature Gaps - Self-Hosted Registry Roadmap.md`**.
- "Consistent Pagination" (§3) reads as net-new; in reality the *fields* already agree — it's a
  shared-base-type extraction, not a redesign.

