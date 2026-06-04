# RFC: Registry API Readiness — Phase 1 (web-client prerequisites)

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-06-04
> **Slug:** registry-api-readiness-phase1
> **Milestone:** —

## Summary

Reshape and lightly extend the existing public registry read surface so a future
browser web client (its own out-of-scope project) can build credible search,
package, and version pages **without** the registry growing any UI-shaped
endpoints. Scope is strictly **Bucket A** from the evaluation in
`.kanban/0-backlog/registry/Registry Website - Optional Web Client and API
Readiness.md` — "expose data that already exists in a web-friendly shape." No
new product features (downloads, advisories, provenance, trusted publishers,
metrics, …); those are Bucket B and belong on the self-hosted-registry
roadmap.

Concretely, this feature:

1. Introduces a shared `PagedResponse<T>` pagination envelope in
   `Stash.Registry.Contracts`, migrating `SearchResponse` and `AuditLogResponse`
   onto it. This is a **breaking wire change**; the in-repo `Stash.Cli` (the
   only API consumer) is updated in lockstep.
2. Adds `GET /api/v1/packages/{scope}/{name}/versions` — a paginated, visibility-
   enforced version listing — and `GET /api/v1/packages/{scope}/{name}/readme` —
   a typed JSON readme endpoint over the existing `PackageRecord.Readme`. Both
   carry `ETag` + `Last-Modified` + `Cache-Control` and honor conditional
   requests (304).
3. Extends `GET /api/v1/search` (search v2) with the column-backed filters and
   sorts the existing schema can serve: `keyword`, `license`, `deprecated`,
   `owner` (filter); `name`, `updated`, `published`, `relevance` (sort). Adds
   `license` and `ownerCount` to `PackageSummaryResponse`.
4. Adds an opt-in CORS layer (`Cors.Enabled=false` by default) following the
   existing `Configuration/*.cs` typed-config pattern.
5. Adds `GET /api/v1/.well-known/registry`, a public discovery endpoint
   advertising name, apiVersion, basePath, limits, links, and a `features{}`
   map whose **known-future Bucket-B flags are pinned to `false`** so the
   eventual web client can feature-detect as those land later.

Out-of-scope, on purpose: a website project, browser auth (the recommended
backend-for-frontend deployment makes the registry's bearer-only model
sufficient), and every Bucket-B feature.

## Motivation

A future browser web client is the goal, but the **current API gap to a
credible browse-only site is small** — the architecture already satisfies the
expensive constraints:

- OpenAPI is published in every environment (`GET /openapi/v1.json`) with a
  coverage gate (`OpenApiCoverageMetaTests`).
- Wire DTOs live in the dependency-free `Stash.Registry.Contracts` assembly;
  the CLI consumes the same types — no EF entity / view-model leakage.
- Read-path visibility is enforced through a single PDP gate
  (`RegistryAction.ReadPackageMetadata` → `AuthorizePackageReadAsync` →
  `VisibilityHidden → 404`); the predicate is already
  `callerUsername`-parameterized in `SearchPackagesAsync`.

What's missing is *breadth* in a few specific shapes:

- The package-detail response embeds **all** versions inline as an unbounded
  dict — fine for a CLI install resolver, hostile to a paginated UI. A
  dedicated `/versions` listing is the natural fix.
- The README is only retrievable as part of the package-detail blob; a web
  client polling for readme content re-fetches the full versions dict
  every time, with no cache validator.
- Two paginated endpoints (`/search`, `/admin/audit-log`) have **identical
  field shapes** but different collection keys (`packages` vs `entries`); a
  third (`/versions`) would compound the inconsistency. A shared envelope
  resolves it once.
- Search has only `q`/`page`/`pageSize`. The column-backed filters and sorts
  that already have backing data (keyword, license, deprecated, owner; sort by
  name/updated/published/relevance) are trivial to expose and immediately
  useful to any UI.
- No CORS, no discovery — fine while the CLI is the only consumer; required
  before a separate-origin web client can begin.

Because the registry is **pre-release** (no deployed instance, no existing
package or user data, CLI is the only consumer), breaking wire changes are
free; we are designing the long-term shape, not migrating it.

## Goals

- Ship a single `PagedResponse<T>` envelope in `Stash.Registry.Contracts`; all
  three paginated read endpoints (`/search`, `/admin/audit-log`, the new
  `/versions`) return it.
- Add `GET …/versions` and `GET …/readme`, both visibility-gated by the same
  PDP chokepoint that protects `GetPackage`, and both serving `ETag` +
  `Last-Modified` + `Cache-Control` with 304 conditional support.
- Extend `/search` with column-backed filters (`keyword`, `license`,
  `deprecated`, `owner`) and a `sort` parameter
  (`relevance|name|updated|published`); add `license` and `ownerCount` to
  `PackageSummaryResponse`.
- Add `Cors.{Enabled,AllowedOrigins,AllowedMethods,AllowedHeaders,
  AllowCredentials}` configuration with off-by-default behavior.
- Add `GET /api/v1/.well-known/registry` (public), pinned-`false` Bucket-B
  feature flags included, `maxPageSize` source-shared with the search/audit/
  versions `[Range]` caps.
- Keep every existing meta-test green: `AuthzCoverageMetaTests`,
  `AuthzDispatchCoverageMetaTests`, `NoMagicAuthStringsMetaTests`,
  `RequestModelBindingMetaTests`, `OpenApiCoverageMetaTests`,
  `RegistryAuthzMatrixTests`.
- Update the CLI's deserialization in lockstep so the same commit set ships
  both server and client of the breaking wire shape.
- Add one new Detect meta-test that prevents the cross-cutting omission this
  feature introduces (see Cross-Cutting Concerns).

## Non-Goals

- Building the website itself (separate project, not in this repo).
- Any Bucket-B feature: package downloads/metrics, advisories, provenance,
  signatures, verified or trusted publishers, quarantine/yank lifecycle,
  webhooks, user-profile or organization-package listing endpoints, admin
  completeness (role mgmt, reserved prefixes, storage health, integrity
  checks).
- Bucket-B search filters/sorts (`vulnerable`, `verified`, `provenance`,
  `sort=downloads`) — these gate on Bucket-B data and must NOT be added now.
- Browser auth changes (cookies, CSRF, session revocation). The registry
  remains bearer-only; the eventual web client is expected to be a
  backend-for-frontend.
- A migration / backfill path for existing registries — there is no deployed
  instance and no existing user data.
- An OpenAPI version bump or `/api/v2` namespace. All new endpoints live under
  the existing `/api/v1`.
- Renaming `/openapi/v1.json` to match the brief's old `/api/v1/openapi.json`
  hint — the current path is already correct and documented.
- README sanitization / rendering. The registry stores and returns raw
  markdown; the website sanitizes (as documented in the original brief and
  reaffirmed here).
- Re-implementing the visibility predicate. The single PDP gate is reused —
  see Cross-Cutting Concerns.

## Design

### Surface

#### Pagination envelope (Contracts)

```csharp
// Stash.Registry.Contracts/PagedResponse.cs
public sealed class PagedResponse<T>
{
    [JsonPropertyName("items")]     public required List<T> Items { get; set; }
    [JsonPropertyName("totalCount")] public int TotalCount { get; set; }
    [JsonPropertyName("page")]       public int Page { get; set; }
    [JsonPropertyName("pageSize")]   public int PageSize { get; set; }
    [JsonPropertyName("totalPages")] public int TotalPages { get; set; }
}

// SearchResponse and AuditLogResponse are deleted; controllers return
// PagedResponse<PackageSummaryResponse> and PagedResponse<AuditEntryResponse>.
// The collection key is unified to "items" across all three endpoints.
```

Per-endpoint pageSize caps stay as documented bounds on the **query** DTOs
(`SearchQuery.[Range(1,100)]`, `AuditLogQuery.[Range(1,200)]`, new
`VersionsQuery.[Range(1,100)]`). The envelope unifies the **response** shape
only — it does not fold the caps.

#### Versions listing

```text
GET /api/v1/packages/{scope}/{name}/versions?page=1&pageSize=20
  Auth: [PublicEndpoint] + [RegistryAuthorize(RegistryAction.ReadPackageMetadata)]
  Response: PagedResponse<VersionDetailResponse>
  Headers: ETag, Last-Modified, Cache-Control
  Conditional: If-None-Match / If-Modified-Since → 304
  Visibility: same as GET /packages/{scope}/{name}
```

The `[RegistryAuthorize]` attribute **and** action are reused from
`GetPackage`: the existing PDP path (`AuthorizePackageReadAsync` →
`VisibilityHidden → 404`) runs unchanged. A new DB method
`GetPackageVersionsAsync(name, page, pageSize)` returns
`(items, totalCount)`. The package's `UpdatedAt` (already loaded by the PDP)
seeds Last-Modified; a weak ETag combines the `UpdatedAt` ticks with
`TotalCount`.

#### README endpoint

```text
GET /api/v1/packages/{scope}/{name}/readme
  Auth: [PublicEndpoint] + [RegistryAuthorize(RegistryAction.ReadPackageMetadata)]
  Response: ReadmeResponse { content (string, raw markdown), contentType ("text/markdown"),
                             byteSize (int), extractedFromVersion (string?) }
  Headers: ETag, Last-Modified, Cache-Control
  Conditional: If-None-Match / If-Modified-Since → 304
```

A **typed JSON DTO** (not a raw markdown body) — preserves
`OpenApiCoverageMetaTests` with **no** new schema exemption (binary download
remains the only permitted exempt op). The `content` field carries the raw,
unsanitized markdown; the registry never renders or sanitizes — that
responsibility stays with the website.

`extractedFromVersion` is the package's `latest` (the version whose README
content was ingested into `PackageRecord.Readme` at publish time). It is not
a separate stored field — it's a read-time projection of the existing
`PackageRecord.Latest` column, surfaced so a future web client can show
"README from version X.Y.Z."

#### Search v2

```text
GET /api/v1/search?q=&keyword=&license=&deprecated=&owner=
                  &sort=relevance|name|updated|published
                  &page=1&pageSize=20
  Filters (all optional, all column-backed):
    q          — existing free-text on name/description
    keyword    — exact-match against the package keywords JSON
    license    — exact-match against PackageRecord.License (SPDX identifier)
    deprecated — boolean
    owner      — exact-match against username with a PackageRoleEntry on the package
  Sort (default: relevance):
    relevance|name|updated|published
  Response: PagedResponse<PackageSummaryResponse>
```

`PackageSummaryResponse` gains two fields (both derivable from existing
records, no new column):

- `license` — copied from `PackageRecord.License`
- `ownerCount` — count of `PackageRoleEntry` rows on the package with
  `Role = PackageRoles.Owner`

The sort vocabulary is a **named C# enum** `PackageSortOrder` (Construct
prevention — see Cross-Cutting Concerns). Bucket-B filter/sort values
(`vulnerable`, `verified`, `provenance`, `sort=downloads`) are deliberately
**not** added; they require unbuilt data.

#### CORS

```jsonc
// appsettings.json — new section, off by default
"Cors": {
  "Enabled": false,
  "AllowedOrigins":     [],
  "AllowedMethods":     ["GET", "HEAD"],
  "AllowedHeaders":     ["Content-Type", "Authorization", "If-None-Match", "If-Modified-Since"],
  "AllowCredentials":   false
}
```

`CorsConfig` is added under `Stash.Registry/Configuration/` and wired into
`RegistryConfig`. `Startup.ConfigureServices` calls `AddCors` and
`Startup.Configure` calls `UseCors` only when `Enabled` is true. Default
behavior is byte-identical to today (no headers, no preflight handling).

#### Discovery

```text
GET /api/v1/.well-known/registry
  Auth: [PublicEndpoint]    (no PDP — capability advertisement is static)
  Response: DiscoveryResponse {
    name:        string,
    apiVersion:  "v1",
    basePath:    "/api/v1",
    limits:      { maxPackageSize: int (bytes), maxPageSize: int },
    links:       { search: "/api/v1/search",
                   packages: "/api/v1/packages",
                   openapi: "/openapi/v1.json",
                   wellKnown: "/api/v1/.well-known/registry" },
    features:    {
      metrics:           false,
      advisories:        false,
      provenance:        false,
      signatures:        false,
      trustedPublishing: false,
      verifiedPublishers:false,
      organizations:     true,
      privatePackages:   true,
      cors:              <Cors.Enabled>
    }
  }
```

Hosted as a minimal-API `MapGet` (health-endpoint style) so it bypasses the
PDP attribute surface entirely; `TypedResults.Ok<DiscoveryResponse>().WithName`
gives it an operation ID for the OpenAPI gate. The advertised `maxPageSize`
**must** come from the same `const int MaxPageSize = 100` used by the search /
versions `[Range]` caps (a `const int` is attribute-legal) so the advertised
limit cannot drift from the enforced one. The feature flags reflect
**actual implemented features**: `metrics`/`advisories`/`provenance`/
`signatures`/`trustedPublishing`/`verifiedPublishers` are pinned `false`
because none of them exist yet; `organizations` and `privatePackages` are
`true` because they do; `cors` reflects the current `CorsConfig.Enabled`.

### Semantics

**Visibility on the new read endpoints.** `/versions` and `/readme` carry
`[RegistryAuthorize(RegistryAction.ReadPackageMetadata)]` — the **same** action
`GetPackage` uses. The shared filter (`RegistryAuthorizeFilter` →
`AuthorizePackageReadAsync`) runs **before** the controller body. Outcomes:

| Caller            | Package           | PDP outcome              | HTTP |
| ----------------- | ----------------- | ------------------------ | ---- |
| anonymous         | public            | Allow                    | 200  |
| anonymous         | private/internal  | Deny(VisibilityHidden)   | 404  |
| anonymous         | does not exist    | Deny(PackageNotFound)    | 404  |
| auth'd, no role   | private/internal  | Deny(VisibilityHidden)   | 404  |
| auth'd, reader+   | private/internal  | Allow                    | 200  |
| admin             | any               | Allow                    | 200  |

Identical to `GetPackage` today. The new endpoints do **not** thread
`callerUsername` into a custom DB query — the single-package gate already
covered this; conflating it with the multi-row `SearchPackagesAsync`
predicate would re-implement the chokepoint we're reusing.

**Conditional requests.** Both `/versions` and `/readme` set:

- `ETag: W/"<UpdatedAt-ticks>-<TotalCount-or-bytelen>"` (weak)
- `Last-Modified: <UpdatedAt RFC 7232 date>`
- `Cache-Control: public, max-age=60`

A request with a matching `If-None-Match` **or** an `If-Modified-Since`
greater-or-equal to `Last-Modified` returns `304 Not Modified` with no body
(matching RFC 7232 §4.1). 304 fires **after** the PDP allows — a private
package still returns 404 for an unauthorized caller, never 304.

**Search v2 semantics.** Filters compose with AND. Empty/absent filters are
no-ops. An unknown enum literal in `sort=` returns
`400 InvalidRequest` (the existing `InvalidModelStateResponseFactory`
aggregates the binder error). `relevance` defaults to the existing
name/description-LIKE ordering by `Name`; `name` orders by `Name` ascending;
`updated` by `UpdatedAt` descending; `published` by `CreatedAt` descending.
`owner` filters against `PackageRoleEntry` rows with `PrincipalType=User` AND
`Role=Owner` (only user owners, not teams/orgs).

**Envelope migration.** `SearchResponse` and `AuditLogResponse` are deleted.
Their callers (`SearchController.Search`, `AdminController.GetAuditLog`)
return `PagedResponse<T>`. The on-the-wire collection key becomes `items`
uniformly. CLI updates `RegistryClient.Search` and `SearchCommand` (the only
CLI consumers of either type — `AuditLogResponse` has no CLI consumer) in
lockstep.

**Discovery is read-only and side-effect-free.** No database call, no
audit-log entry, no rate-limit category. Pure capability advertisement.

### Implementation Path

The big-picture path the phases must keep intact:

```
PagedResponse<T> in Contracts (new shape)
   ↓
Migrate Search/Audit onto it + CLI lockstep (breaking wire change, contained to envelope use)
   ↓
RegistryAction.ReadPackageMetadata PDP gate already enforces visibility
   ↓
Reuse RegistryAction.ReadPackageMetadata on /versions and /readme
   ↓
   ├── /versions → PagedResponse<VersionDetailResponse> + ETag/Last-Modified/Cache-Control
   └── /readme   → ReadmeResponse (typed JSON) + ETag/Last-Modified/Cache-Control
   ↓
Search v2: extend SearchQuery + SearchPackagesAsync predicate (filters/sort), extend PackageSummaryResponse
   ↓
CorsConfig (off-by-default) wired into Startup
   ↓
.well-known/registry discovery (MapGet, [PublicEndpoint] semantics, TypedResults)
   ↓
Documentation: docs/Registry — Package Registry.md updated for every surface above
```

Every layer that participates in the PDP / wire / OpenAPI surfaces stays the
same set as today: Contracts → Database → Authorization → Controllers →
Startup → CLI. New phases never introduce a parallel surface; they extend
this one.

### Cross-Cutting Concerns

Four shared concerns span multiple phases. Each is recorded with its single
source of truth and the **strongest available** prevention level. The
visibility entry is **the** load-bearing one — it is exactly the kind of
omission this section exists to prevent (a quiet `[PublicEndpoint]`-only
attribution on `/versions` would silently ship a private-package existence
leak with a green build; both existing coverage meta-tests accept it).

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| Read-path package visibility (anonymous → 404 on private packages; reader+ → 200) | `RegistryAuthorizer.AuthorizePackageReadAsync` via `RegistryAction.ReadPackageMetadata` (PDP chokepoint, reused, not re-implemented) | **Detect** — a new `PackagesControllerRegistryAuthorizeRequiredTests` (Roslyn or reflection over `PackagesController`'s actions) asserts that every action carries `[RegistryAuthorize]`. `AuthzCoverageMetaTests` and `AuthzDispatchCoverageMetaTests` both **accept `[PublicEndpoint]` alone** and would pass an open door; this assertion is the structural floor. Paired with per-endpoint anonymous-vs-authorized 404 behavior tests. Recorded explicitly as Detect (not Construct) because `[PublicEndpoint]` is the fail-open opt-out and the type system cannot forbid a future author from picking it. Ships with a fail-path fixture and a pin assertion. |
| Pagination response shape (`items/totalCount/page/pageSize/totalPages`) across `/search`, `/admin/audit-log`, `/packages/.../versions` | `PagedResponse<T>` in `Stash.Registry.Contracts` | **Construct** — the type *is* the shape. A controller that returns anything else fails to compile (or returns an off-spec DTO that meta-tests already enumerate via response-type discovery). Per-endpoint pageSize caps live on the **query** DTOs (`[Range]`), not on the envelope. |
| Bounded `sort=` vocabulary on `/search` (`relevance|name|updated|published`) | `PackageSortOrder` enum in `Stash.Registry.Contracts` | **Construct** — illegal values won't bind (`[FromQuery]` rejects them as a binder error → 400 via `InvalidModelStateResponseFactory`). Note: query-string enum binding uses **case-insensitive member-name** parsing, not the `JsonStringEnumMemberName` attribute that drives body deserialization. Enum members are named to match the wire spelling (`Relevance/Name/Updated/Published`); a test covers an invalid `sort=` returning 400. |
| ETag / Last-Modified generation on `/versions` and `/readme` | A shared `ConditionalResponse` helper used by both endpoints | **Detect** — per-endpoint 304-on-If-None-Match and 304-on-If-Modified-Since tests. (No structural compile-time guarantee is possible; only two endpoints share it, so a meta-scan over all controllers would be overkill. Behavior tests are the proportionate guard.) |

## Acceptance Criteria

End-to-end:

- `GET /api/v1/search` returns `PagedResponse<PackageSummaryResponse>` with
  the `items` key and accepts every new filter/sort; an invalid `sort=`
  returns `400 InvalidRequest`. `PackageSummaryResponse` includes `license`
  and `ownerCount`.
- `GET /api/v1/admin/audit-log` returns `PagedResponse<AuditEntryResponse>`
  with the `items` key (collection key unified across all three paginated
  endpoints).
- `GET /api/v1/packages/{scope}/{name}/versions` returns
  `PagedResponse<VersionDetailResponse>` for a public package and
  honors `page`/`pageSize`.
- `GET /api/v1/packages/{scope}/{name}/readme` returns
  `ReadmeResponse { content, contentType, byteSize, extractedFromVersion }`
  for a public package.
- Both new endpoints emit `ETag`, `Last-Modified`, and `Cache-Control` on
  200; honor `If-None-Match` and `If-Modified-Since` with a 304.

Visibility (the load-bearing security criterion):

- `GET …/versions` for an **anonymous** caller on a **private** package
  returns `404 Not Found` — never `200` with a list, never `403`. Same for
  `GET …/readme`.
- `GET …/versions` for an **authenticated** caller **without** a role on
  a **private** package returns `404 Not Found`.
- `GET …/versions` for an **authenticated** caller **with** a reader-or-above
  role on a **private** package returns `200 OK` with the versions list.
- `PackagesControllerRegistryAuthorizeRequiredTests` is **green** today and
  **fails red** if a new `PackagesController` action is added with only
  `[PublicEndpoint]` and no `[RegistryAuthorize]`. Includes a fail-path
  fixture proving it has teeth.

CORS:

- With `Cors.Enabled=false` (default), no `Access-Control-Allow-Origin`
  header is emitted on any response, and no preflight OPTIONS handling is
  attached. Behavior is byte-identical to today's release.
- With `Cors.Enabled=true` and a configured origin, a preflight OPTIONS
  succeeds and an actual GET emits the expected `Access-Control-Allow-Origin`
  header.

Discovery:

- `GET /api/v1/.well-known/registry` returns 200 with name, apiVersion,
  basePath, limits, links, and a features map; all Bucket-B feature flags
  are `false`; `organizations` and `privatePackages` are `true`;
  `limits.maxPageSize` equals the constant used in the search `[Range]`
  cap.
- Endpoint is public (no token required) and appears in the published
  OpenAPI document with an operation ID.

CLI lockstep:

- `stash pkg search <q>` reads the new envelope shape (`items` not
  `packages`) and prints the same human output as today.
- AOT publish of `Stash.Cli` succeeds with the new closed generic
  `PagedResponse<PackageSummaryResponse>` registered in `CliJsonContext`.

Meta-tests (all green at `/done` time, never excluded):

- `AuthzCoverageMetaTests`, `AuthzDispatchCoverageMetaTests`,
  `NoMagicAuthStringsMetaTests`, `RequestModelBindingMetaTests`,
  `OpenApiCoverageMetaTests`, `RegistryAuthzMatrixTests` — all green.
- New `PackagesControllerRegistryAuthorizeRequiredTests` — green with the
  current set of `PackagesController` actions, fail-path fixture proves it
  has teeth.

Documentation:

- `docs/Registry — Package Registry.md` describes the new envelope, the new
  endpoints, the CORS config, and the discovery endpoint.

## Phases

The phase list lives in `plan.yaml`. Each phase has a concrete `done_when`.

## Open Questions

- **None blocking.** Resolved during architect exploration:
  - *Should `/versions` re-implement the visibility predicate?* No — reuse the
    PDP gate via `RegistryAction.ReadPackageMetadata` (see Cross-Cutting).
  - *Should `/readme` return raw markdown or a typed JSON DTO?* Typed JSON
    (`ReadmeResponse`) — preserves `OpenApiCoverageMetaTests` with no new
    exemption.
  - *Discovery endpoint as `MapGet` or as a controller action?* `MapGet` —
    keeps it out of the PDP attribute surface; pattern matches the existing
    health endpoint.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-04 | Migrate **both** `SearchResponse` and `AuditLogResponse` onto a shared `PagedResponse<T>` and update the CLI in lockstep (breaking wire change). | Registry is pre-release, CLI is the only consumer, three paginated endpoints are about to exist; converging now is cheaper than deferring. Make-it-right doctrine. |
| 2026-06-04 | Reuse `RegistryAction.ReadPackageMetadata` for `/versions` and `/readme` instead of introducing new actions. | Single PDP chokepoint; new endpoints inherit `VisibilityHidden→404` without re-implementation. Construct over Detect for the predicate itself. |
| 2026-06-04 | Detect (not Construct) for "every `PackagesController` action carries `[RegistryAuthorize]`." | `[PublicEndpoint]` is the fail-open opt-out and satisfies both existing coverage gates; a future author could ship a leak with a green build. A new structural meta-test asserts the invariant; honest about what the architecture can and cannot prevent. |
| 2026-06-04 | `/readme` returns `ReadmeResponse { content, contentType, byteSize, extractedFromVersion }` (typed JSON), not a raw `text/markdown` body. | Avoids a second `OpenApiCoverageMetaTests` schema exemption (binary download stays the only exempt op); preserves "registry stores raw markdown; website renders" without coupling to a content-type that breaks the schema gate. |
| 2026-06-04 | CORS off by default; configured under `Configuration/CorsConfig.cs`. | The recommended deployment is BFF / same-origin (zero CORS needed); a separate-origin operator must explicitly opt in. Matches the brief's "off or restrictive by default" stance. |
| 2026-06-04 | Discovery as a minimal-API `MapGet`, styled on the existing health endpoint. | Capability advertisement is static; keeping it off the controller surface keeps the PDP/authz-attribute surface untouched. The `WithName` operationId still lands it in the OpenAPI document. |
| 2026-06-04 | `maxPageSize` advertised by discovery sources from the **same** `const int` used by the search/versions `[Range]` caps. | Removes the drift class — an `appsettings.json` operator cannot accidentally make the advertised cap diverge from the enforced cap. `const int` is attribute-legal. |
| 2026-06-04 | `PackageSortOrder` C# enum members named with wire-matching spellings (`Relevance/Name/Updated/Published`). | `[FromQuery]` enum binding ignores `JsonStringEnumMemberName` and uses case-insensitive **member-name** matching; naming members to match wire strings is the simplest legal binding without a custom binder. |
| 2026-06-04 | Defer all Bucket-B features (downloads, advisories, provenance, signatures, trusted publishers, metrics, user/org listings, admin completeness) to the existing roadmap. | Per the evaluation: those have no backing data; including any of them would balloon the scope and starve the genuine API-readiness work. The discovery `features{}` map is the seam they light up later. |
