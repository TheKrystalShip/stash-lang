# RFC: Registry Website Phase 2 — Public Browse-Only Website

> **Status:** Draft
> **Owner:** cristian.moraru@live.com
> **Created:** 2026-06-04
> **Slug:** registry-website-phase2
> **Milestone:** website

## Summary

Stand up `Stash.Registry.Web`, a new server-rendered ASP.NET Razor Pages project in
this repo, as the first human-facing client of the Stash package registry. It is
**browse-only** (no login), surfaces only the Bucket-A data the Phase-1 API
readiness slice already exposes, and treats the registry strictly as a
documented REST dependency reached over HTTP. The site lives as a separately
hostable project that consumes `Stash.Registry.Contracts` for wire DTOs and
**must not** carry a compile-time dependency on `Stash.Registry` itself.

Pages: Home (`/`) with global search and a "recently updated" rail; Search
(`/search?q=…&…`) with the Phase-1 column-backed filters and sorts; Package
detail (`/packages/@scope/name`) with sanitized README, metadata sidebar,
versions table, dependencies list, and one-line install command; Version detail
(`/packages/@scope/name/v/{version}`). Routes are two-segment scoped throughout
because the registry routes are.

## Motivation

A registry without a human-facing web surface feels opaque. Phase 1 shipped the
Bucket-A API readiness slice (`PagedResponse<T>`, `GET …/versions`,
`GET …/readme`, search v2 column-backed filters/sorts, the discovery endpoint)
specifically so a public browse client could be built **without** new registry
endpoints. The cost of doing nothing is that nobody can discover, read, or
evaluate packages from a browser, and the ecosystem credibility benefit of that
surface is forfeited indefinitely.

This is also a forcing function for the coupling rules the milestone exists to
prove: a real client built against `Stash.Registry.Contracts` and `/openapi/v1.json`
demonstrates the API is genuinely web-client-friendly, and surfaces any
documented-but-impractical edges before Phase 3 (authenticated maintainer site).

## Goals

- Ship a runnable `Stash.Registry.Web` project added to `Stash.sln` that builds
  via `dotnet build Stash.sln` and is testable via the existing `dotnet test`
  gate.
- Render four pages — Home, Search, Package detail, Version detail — driven
  entirely by today's Bucket-A endpoints, with sanitized README rendering.
- Be **separately hostable**: a configurable `Registry:BaseUrl` (typed
  `RegistryClientConfig` following the existing `RegistryConfig` pattern) points
  at any registry instance; default targets localhost for dev.
- Reuse Stash's existing identity: Catppuccin Mocha (dark) / Latte (light)
  palette from `Stash.Playground/wwwroot/css/app.css` and `stash-icon.svg` from
  `.vscode/extensions/stash-lang/images/`. The site visually belongs to the
  Stash family — not npm-red or NuGet-blue.
- Enforce, at compile time and test time, that the web client has **zero**
  dependency on `Stash.Registry`.
- Treat all package-authored content (README, description, keywords) as hostile
  input. README rendering goes through a single Markdig → HtmlSanitizer
  chokepoint; everything else rides Razor's default HTML encoding.

## Non-Goals

- **No new registry API endpoints.** This phase only consumes the surface that
  shipped in Phase 1; the registry's controllers, OpenAPI document, and authz
  filters gain no new surface.
- **No login, accounts, tokens, sessions, or CSRF.** Anonymous browse only —
  Phase 3 owns the maintainer surface.
- **No Bucket-B UI scaffolding.** No download counts, "used by"/dependents,
  vulnerabilities/advisories, provenance, verified/trusted-publisher,
  signatures, dependency graph, or source-code browser. These light up in later
  phases via the discovery `features{}` map; this phase must not render empty
  placeholders for them.
- **No CORS, no browser-to-registry calls.** The browser only ever talks to the
  website's own origin; the website backend calls the registry server-to-server.
  Future search-as-you-type proxies server-side too.
- **No client-side visibility logic.** The registry's existing PDP enforces
  visibility (anonymous → public only, hidden → 404); the website inherits that
  behavior unchanged.
- **No deployment artifacts.** Dockerfile, reverse-proxy config, and prod
  hosting recipes are out of scope. Building and running locally is in scope.
- **No language/stdlib changes.** The `.claude/language-changes.md` checklist
  does not apply.

## Design

### Surface

**New project** `Stash.Registry.Web` at the repo root, sibling to
`Stash.Registry` and `Stash.Registry.Contracts`. ASP.NET Core 10 **Razor Pages**
(page-per-route with a co-located `PageModel`; user-locked over classic MVC
controllers+views — simpler for a read-only browse site). Added to `Stash.sln`.

**Project references (csproj):**

- `Stash.Registry.Contracts` — sole project reference. Brings the typed DTOs
  (`PagedResponse<T>`, `SearchQuery`, `PackageSortOrder`, `PackageSummaryResponse`,
  `PackageDetailResponse`, `VersionDetailResponse`, `VersionsQuery`,
  `ReadmeResponse`, `DiscoveryResponse`, `ErrorResponse`, and the
  `BoundedDomains` enums).
- **MUST NOT** project-reference `Stash.Registry`. A stray reference breaks the
  build via the architecture-test guard (see Cross-Cutting Concerns).
- NuGet packages: `Markdig` (Markdown parser), `HtmlSanitizer` (HTML sanitizer),
  standard ASP.NET Core packages.

**Typed configuration** `RegistryClientConfig` in
`Stash.Registry.Web/Configuration/`, following the established pattern in
`Stash.Registry/Configuration/RegistryConfig.cs` (the `CorsConfig` /
`SecurityConfig` style — a plain config class bound from `appsettings.json`).
Single setting in scope for Phase 2:

- `BaseUrl` — registry origin (e.g. `http://localhost:5290`). Default
  `http://localhost:5290` for dev. The website appends `/api/v1/...` itself.

(`HttpTimeout`, `UserAgent`, etc. may be added by the implementer if needed,
but the only one the brief mandates is `BaseUrl`.)

**Typed registry client** `IRegistryClient` in `Stash.Registry.Web/Services/`,
backed by `HttpClient`. Methods cover only the endpoints Phase 2 needs:

- `Task<PagedResponse<PackageSummaryResponse>> SearchAsync(SearchQuery, CancellationToken)`
- `Task<PackageDetailResponse?> GetPackageAsync(string scope, string name, CancellationToken)` — `null` on 404
- `Task<PagedResponse<VersionDetailResponse>?> GetVersionsAsync(string scope, string name, VersionsQuery, CancellationToken)`
- `Task<ReadmeResponse?> GetReadmeAsync(string scope, string name, CancellationToken)`
- `Task<VersionDetailResponse?> GetVersionAsync(string scope, string name, string version, CancellationToken)` — uses `GET /api/v1/packages/{scope}/{name}/{version}`
- `Task<DiscoveryResponse> GetDiscoveryAsync(CancellationToken)` — for `/health`-style display and future feature-detection

The interface is **injectable** so `WebApplicationFactory` integration tests
swap a stubbed `HttpMessageHandler` (or a fake `IRegistryClient`) without
needing a live registry. Default implementation `HttpRegistryClient` uses
`IHttpClientFactory` so timeouts and base address come from
`RegistryClientConfig`.

**Routes (web — two-segment scoped throughout):**

| Method | Path | Backed by |
| --- | --- | --- |
| GET | `/` | `IRegistryClient.SearchAsync(sort=Updated, pageSize=N)` — "recently updated" rail |
| GET | `/search?q=&keyword=&license=&deprecated=&owner=&sort=&page=&pageSize=` | `IRegistryClient.SearchAsync(SearchQuery)` |
| GET | `/packages/@{scope}/{name}` | `GetPackageAsync` + `GetReadmeAsync` (parallel) |
| GET | `/packages/@{scope}/{name}/v/{version}` | `GetVersionAsync` |
| GET | `/health` (optional) | `GetDiscoveryAsync` |

**Scope handling.** The registry's routes are `{scope}/{name}` — two URL
segments — and the typical user spelling is `@scope/name`. The website's route
template `/packages/@{scope}/{name}` makes the literal `@` part of the URL path
(so `/packages/@my-org/my-lib` is the canonical browse URL), then passes the
raw segments straight through to `IRegistryClient.GetPackageAsync(scope, name)`
which constructs `…/packages/{scope}/{name}` (after `Uri.EscapeDataString` on
each segment to match the registry's `Uri.UnescapeDataString` on the controller
side). The `@` is **not** part of the registry path; it's a UI affordance only.

**Pages and components (Razor):**

- `_Layout.cshtml` — global shell: top bar with stash-icon + search box (POSTs
  to `/search`), Catppuccin theme, no theme toggle (Phase 2 ships dark by
  default; toggle is later-phase polish).
- `Pages/Index.cshtml` (or `HomeController`) — landing: search hero + recently
  updated cards.
- `Pages/Search.cshtml` — filter bar (keyword, license, deprecated, owner) +
  sort dropdown (relevance/name/updated/published) + paged result cards. The
  filter/sort dropdown options are populated from the `PackageSortOrder` enum
  and from named view constants — never inlined strings.
- `Pages/Package.cshtml` — README main column + metadata sidebar (description,
  latest version, last updated, license, ownerCount, deprecation banner) +
  Versions section (table with publish date, publisher) + Dependencies section
  (list). One-line install widget showing `stash pkg add @scope/name` with
  copy-to-clipboard.
- `Pages/Version.cshtml` — version metadata: version string, publishedAt,
  publishedBy, dependencies, stashVersion, integrity, deprecation status.

**README rendering chokepoint.** A single internal service
`IReadmeRenderer.RenderToSafeHtml(string markdown) → HtmlString` in
`Stash.Registry.Web/Rendering/`. Implementation pipeline:

1. Markdig: parse Markdown to HTML with a conservative pipeline (advanced
   extensions disabled where they enable raw HTML pass-through).
2. HtmlSanitizer: strip remote `<script>`, `<iframe>`, inline event handlers
   (`onerror=`, `onclick=`, etc.), `javascript:`/`data:` URIs; allow only safe
   inline elements/attributes.
3. Return as `HtmlString` (the **only** type the README view binds with
   `@Html.Raw`).

The view template treats README markup as `Html.Raw(renderer.RenderToSafeHtml(content))`,
**period**. Every other field (description, keywords, license, version strings,
publisher, error messages) goes through Razor's default HTML encoding via
plain `@expression`.

### Semantics

- **Anonymous-only**, no `Authorization` header sent. The registry's own
  visibility enforcement is the sole gate; an attempt to load
  `/packages/@private/foo` for a hidden package surfaces as the registry's 404
  and the website renders a generic "not found" page (no existence leak).
- **No README**: when `GetReadmeAsync` returns the response but `Content` is
  empty, or when the package has no `latest` (no published version), the page
  shows an empty-state ("This package has no README.") rather than rendering
  an empty `<div>`.
- **Deprecation banner**: when `PackageDetailResponse.Deprecated == true`, the
  package page shows a prominent banner with `DeprecationMessage` and a link to
  `DeprecationAlternative` if present.
- **Error mapping** from the typed client:
  - Registry 404 → website 404 page (with the search box still rendered).
  - Registry 5xx / network error → website 502 "registry unreachable" page
    with the configured `BaseUrl` displayed (operator-visible diagnostic, not
    end-user error text).
  - Registry 400 (e.g. bad `sort=`) → website 400 with the validation message
    bubbled up.
- **Pagination**: search and versions reuse `PagedResponse<T>` directly; page
  links emit `?page=N` preserving all other query parameters. The page-size
  cap (`PagingLimits.MaxPageSize == 100`) is honored implicitly because the
  registry rejects out-of-range values; the website passes whatever the user
  selects from a small named dropdown (e.g. 20/50/100).
- **HTTP caching**: the registry serves ETag/Last-Modified on `/readme`,
  `/versions`, and the search endpoint (Phase-1 deliverable). The website's
  `HttpRegistryClient` does **not** implement conditional GETs in Phase 2 — it
  is a thin pass-through. (Server-side caching is a later-phase optimization.)

### Implementation Path

The end-to-end path the design must keep intact across phases:

```
Browser  →  Stash.Registry.Web (Razor view)
              →  IRegistryClient (typed, DI)
                   →  HttpClient (server-side)
                        →  /api/v1/...  (Stash.Registry)
                             →  IRegistryDatabase (visibility-aware PDP)
              →  IReadmeRenderer (Markdig → HtmlSanitizer)  ← README path ONLY
              ←  HtmlString
       ←  Razor view (default-encoded everywhere except README)
```

The website never touches `Stash.Registry`'s DI container, EF context, storage,
or PDP directly. The architecture test enforces this at the **compile boundary**.

### Design Language

User-locked: information-architecture borrows from npmjs.com (primary skeleton —
cleaner/airier, suits the thinner Bucket-A field set) with NuGet patterns where
they fit (versions table, dependencies list). One coherent direction, not a
mashup. **Reimplement patterns; never copy npmjs.com / nuget.org CSS, logos,
or trademarked marks.**

Adopt:

- Prominent global search in the header on every page.
- Result cards with concise signals: name, description, latest version, last
  updated, license, ownerCount, deprecated badge.
- Package detail = README main column + metadata sidebar.
- Versions section: list/table with publish date and publisher.
- Dependencies section: list (name → version constraint).
- Copy-to-clipboard single install command: `stash pkg add @scope/name`.
  Explicitly **not** NuGet's multi-package-manager tab strip — Stash is the
  only package manager that consumes this registry.
- Deprecation banner when applicable.
- If a tabbed package page is preferred, tabs may be Readme / Versions /
  Dependencies. **No "Code" / source-browser tab** (needs tarball extraction —
  out of scope).

Adapt:

- Cards and sidebar show only the Bucket-A fields above. No "Downloads",
  "Dependents", "Used by", "Vulnerabilities", "Provenance", "Verified", or
  "Signed" labels appear anywhere — neither rendered with values nor as
  placeholder rows.

Defer (Bucket-B / polish — explicitly out of scope):

- Download counts and graphs.
- "Used by" / dependents.
- Provenance / verified-publisher badges.
- Vulnerability warnings.
- Search autocomplete (would require AJAX to a website-side proxy endpoint).
- Source-code browser tab.
- Dependency graph visualization.
- Light/dark theme toggle (ship dark by default for Phase 2).

Brand:

- Reuse the Catppuccin Mocha (dark) and Latte (light) palette already defined
  in `Stash.Playground/wwwroot/css/app.css`. Copy/adapt the CSS custom
  properties block — do not invent a parallel palette.
- Reuse `stash-icon.svg` from `.vscode/extensions/stash-lang/images/` as the
  brand mark in the header.
- The site is visually a sibling of the Playground and the VS Code extension.
  Not npm-red. Not NuGet-blue.

### Cross-Cutting Concerns

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| No compile-time dependency on `Stash.Registry` from `Stash.Registry.Web` | `Stash.Registry.Web.csproj` (declares only `Stash.Registry.Contracts`) | **Detect** — `WebProjectIsolationMetaTests` reflects over the loaded `Stash.Registry.Web` assembly and asserts (a) it loaded, (b) it *does* reference `Stash.Registry.Contracts` (positive **binding floor** — defeats the "0 violations because nothing bound" vacuous pass; see `CliNoMagicWireStringsMetaTests` precedent), (c) it does **not** reference `Stash.Registry`. Ships a fail-path fixture (a fixture-only `<ProjectReference>` to `Stash.Registry` trips the scan and is detected). The csproj `<ItemGroup>` itself is the design-time prevention — the test is the standing review-time guard. Lands in phase P1 with the scaffold. |
| Package-authored README is hostile input | One `IReadmeRenderer` service (`Stash.Registry.Web/Rendering/`) | **Construct** — Razor default-encodes every DTO string field; the README view template is the *sole* `@Html.Raw` call site in the entire project, and it accepts only `HtmlString` returned by `IReadmeRenderer.RenderToSafeHtml`. **Detect** — `ReadmeChokepointMetaTests`: a Roslyn / regex scan over all `.cshtml` and `.cshtml.cs` files asserts that any `@Html.Raw(...)` argument is the renderer's output (i.e. either an invocation of `IReadmeRenderer.RenderToSafeHtml(...)` or a model field typed `HtmlString` populated from it); any other `Html.Raw` call fails the test with a precise location. Ships a fail-path fixture proving the scan has teeth. Plus per-pattern hostile-input xUnit tests (`<script>`, `<iframe>`, `onerror=`, `javascript:`, `data:` URIs) on `IReadmeRenderer`. Lands in the phase that introduces README rendering (phase P5) — **before** any page actually renders README (phase P4 explicitly stubs the README column). |
| Field discipline — render only Bucket-A fields, never empty Bucket-B placeholders | `Stash.Registry.Contracts` DTOs (`PackageSummaryResponse`, `PackageDetailResponse`, `VersionDetailResponse`) | **Construct** — the Bucket-A DTOs simply do not carry `downloads`, `dependents`, `vulnerable`, `verified`, `provenance`, `signatures`. A view that tries to bind those fields fails to compile. **Detect** — `FieldDisciplineMetaTests`: rendered-HTML scan over a `WebApplicationFactory` walk of the home, search, package, and version pages asserts the forbidden label vocabulary (`Downloads`, `Dependents`, `Used by`, `Vulnerabilities`, `Provenance`, `Verified publisher`, `Signed`, etc.) is absent. This catches hardcoded placeholder chrome ("Downloads: —") that the DTO-shape guard wouldn't see. Lands in phase P3 with the first card rendering — not phase P6/P7, otherwise phases P3–P5 merge with the invariant unenforced. |
| Sort/filter wire vocabulary (sent to registry, displayed in UI) | `PackageSortOrder` enum + named view-side `SortLabels` const class for dropdown labels | **Construct** — reuse the existing `PackageSortOrder` enum from `Stash.Registry.Contracts`. An illegal `sort=` literal won't compile against the typed `SearchQuery.sort`. Filter parameter names are bound through strongly-typed Razor model binding to `SearchQuery` — no string keys. UI display labels live in a single `SortLabels` const class (e.g. `public const string Relevance = "Relevance"`) — never inlined in `.cshtml`. |
| Browser-to-registry boundary (no direct browser-to-registry calls; CORS stays off) | Architecture — server-rendered Razor, no client-side fetch helpers that target the registry | **Construct** — there is no JavaScript SDK on the page and no CORS configured on the registry side; the browser physically cannot reach the registry without proxying through the website. **Instruct** — locked in the brief: future search-as-you-type and any AJAX must proxy server-side via a website-owned endpoint that calls `IRegistryClient`. |

## Acceptance Criteria

End-to-end behavior:

- `dotnet build Stash.sln` succeeds; `Stash.Registry.Web` builds and is part of
  the solution.
- `dotnet test` (full unfiltered suite) is green, including the new
  `Stash.Tests/Registry/Web/` test classes.
- A developer can run the website locally pointed at a running registry by
  setting `Registry:BaseUrl=http://localhost:5290` in `appsettings.Development.json`
  (or via env var), and:
  - `GET /` renders the home page with the search hero and a recently-updated
    rail populated from `IRegistryClient.SearchAsync(sort=Updated)`.
  - `GET /search?q=foo` renders search results, including pagination links that
    round-trip all query parameters.
  - `GET /packages/@my-org/my-lib` renders the package detail page with
    sanitized README, metadata sidebar, versions table, dependencies list, and
    the install command `stash pkg add @my-org/my-lib`.
  - `GET /packages/@my-org/my-lib/v/1.2.3` renders the version detail page.

Error behavior:

- A request for a nonexistent package returns the website's 404 page (no
  existence leak about hidden/private packages — the registry returns 404 and
  the website renders 404).
- With the registry stopped, `GET /` returns a website 502 page indicating the
  registry is unreachable; the website itself does not crash.
- A request with `sort=downloads` (Bucket-B value the registry rejects) bubbles
  up the registry's 400 InvalidRequest message.

Cross-entrypoint / structural behavior:

- The architecture test `WebProjectIsolationMetaTests` is green and would fail
  if `Stash.Registry.Web` added a `<ProjectReference>` to `Stash.Registry`
  (proven via the included fail-path fixture).
- The README chokepoint meta-test `ReadmeChokepointMetaTests` is green and
  would fail if any `.cshtml` introduced an `@Html.Raw(...)` not sourced from
  `IReadmeRenderer` (proven via the included fail-path fixture).
- README hostile-input tests cover: `<script>` stripped, `<iframe>` stripped,
  `onerror=`/`onclick=` neutralized, `javascript:` and `data:` URIs blocked,
  and a benign Markdown sample (headings, lists, code fences, links to
  `https://`) round-trips intact.
- The field-discipline meta-test `FieldDisciplineMetaTests` is green and
  asserts the forbidden Bucket-B label vocabulary is absent from all rendered
  pages.
- Final verify (`dotnet test`) runs the **full** suite — no namespace-exclude
  filter — and no Phase-1 registry meta-test (`OpenApiCoverageMetaTests`,
  `AuthzCoverageMetaTests`, `NoMagicAuthStringsMetaTests`) regresses, because
  this phase adds no registry endpoints.

## Phases

The phase list lives in `plan.yaml`. The high-level shape, with the
cross-cutting guards explicitly placed:

- **P1 — Scaffold + Contracts-only project + the no-dependency
  architecture test.** Creates `Stash.Registry.Web.csproj` (referencing only
  `Stash.Registry.Contracts`), wires it into `Stash.sln`, adds an empty Razor
  page (`/health`-style) that calls `IRegistryClient.GetDiscoveryAsync` so the
  pipeline is wired end-to-end. Adds `WebProjectIsolationMetaTests` with the
  binding floor and fail-path fixture. **This guard ships before any feature
  code.**
- **P2 — Brand layout + typed registry client.** `_Layout.cshtml` with
  Catppuccin theme variables copied/adapted from
  `Stash.Playground/wwwroot/css/app.css`, the stash-icon brand mark in the
  header, and the search box (POST `/search`). `IRegistryClient` /
  `HttpRegistryClient` with `RegistryClientConfig` and DI registration. Search
  and detail methods land here; README is **not** rendered yet.
- **P3 — Home + Search pages + the field-discipline guard.** Home
  recently-updated cards and the Search page with filters/sort/pagination.
  Cards bind only Bucket-A fields. `FieldDisciplineMetaTests` lands here, with
  the first rendered cards in scope.
- **P4 — Package detail shell (no README yet).** The package detail page
  renders the metadata sidebar, versions section, dependencies section, install
  command, and deprecation banner. **README column is explicitly stubbed** with
  an empty-state placeholder ("Loading README…" or "README rendering arrives
  in the next phase") that contains no `@Html.Raw`. This avoids shipping an
  unsanitized-README window between phase P4 and phase P5.
- **P5 — README chokepoint + sanitized rendering + hostile-input tests.**
  Introduces `IReadmeRenderer`, the Markdig → HtmlSanitizer pipeline, the
  `ReadmeChokepointMetaTests` Roslyn scan with its fail-path fixture, the
  hostile-input xUnit tests, and wires the package detail page's README column
  to it. **This is the only phase that opens an `@Html.Raw` call site.**
- **P6 — Version detail page.** Version detail with dependencies,
  stashVersion, integrity, publishedAt/publishedBy, deprecation. No new
  cross-cutting surface.
- **P7 — Docs.** `Stash.Registry.Web/README.md` with build/run/configure
  instructions; cross-link from the registry doc's "Clients" section.

Phase-ordering rationale: cross-cutting guards land **with or before** the
first phase that could violate them. The architecture test ships in phase P1.
The field-discipline test ships in phase P3 with the first card rendering. The
README chokepoint and its meta-test ship in phase P5, and phase P4 deliberately
does **not** render README — that delay is enforced in the brief and in the
phase's `done_when`.

## Open Questions

- **Tabs vs. always-visible sections on package detail.** npm uses tabs
  (Readme/Code/Dependencies/Dependents/Versions); NuGet uses a single scroll
  with sidebar tabs. Phase 2 leans NuGet-side (single scroll, all sections
  visible) because there are only 3 sections (README, Versions, Dependencies)
  and tabs hurt deep-linkability. Implementer may revisit; not a blocker.
- **Page-size selector.** Whether to expose a 20/50/100 dropdown on search or
  fix at 20. Defaulting to a fixed 20 in Phase 2 is fine; opening it up is a
  one-line follow-up.
- **`/health` endpoint visibility.** Whether to expose `/health` on the
  website itself (a thin pass-through to `GetDiscoveryAsync`) or omit it.
  Including it in phase P1 is the cheapest end-to-end smoke for the client.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-04 | New project `Stash.Registry.Web` in this repo, sibling to `Stash.Registry` and `Stash.Registry.Contracts`. | User-locked. Keeps the build single-tree, makes the no-dependency guard a single-solution architecture test, and reuses the existing solution's CI/test infra. |
| 2026-06-04 | ASP.NET Razor **Pages**, server-rendered (user chose Razor Pages over classic MVC controllers+views — page-per-route, simpler for a read-only browse site). Project-references `Stash.Registry.Contracts` only, NO codegen. | User-locked. Server rendering means the browser never talks to the registry directly (no CORS attack surface), no client JS bundle to ship, and shared DTOs are byte-identical with the CLI. |
| 2026-06-04 | Separately hostable via configurable `Registry:BaseUrl`; no CORS. | User-locked. Browser only talks to the website origin; website backend calls the registry server-to-server. Phase-1's CORS toggle stays off for the same-origin/BFF deployment this phase ships. |
| 2026-06-04 | The no-dependency architecture test is the flagship omission guard. Detect (not Construct), but with a binding-floor and a fail-path fixture to defeat the "0 violations vacuous pass" failure mode seen in `CliNoMagicWireStringsMetaTests`. | Absence-of-a-ProjectReference cannot be a compile error, so it has to be enumeration-based — but the bookkeeping is structured to make a stray reference fail loudly, not silently pass. |
| 2026-06-04 | Field discipline is primarily **Construct**: the Bucket-A DTOs lack the Bucket-B fields, so a data-bound Bucket-B field won't compile. `FieldDisciplineMetaTests` is the **Detect** backstop catching hardcoded placeholder chrome. | The user explicitly named "no empty Bucket-B placeholders" as load-bearing; this captures both the structural reason it can't happen and the fallback for the one way it could (hardcoded labels). |
| 2026-06-04 | README rendering chokepoint (`IReadmeRenderer`) lands in phase P5, and phase P4 (Package detail) deliberately does **not** render README. | Avoids a window between "package detail shell exists" and "sanitizer wired" during which an implementer might reach for a raw `Markdig.ToHtml(content)`. The chokepoint test (`ReadmeChokepointMetaTests`) is what makes the single `@Html.Raw` call site enforceable. |
| 2026-06-04 | Reuse Catppuccin Mocha/Latte palette from `Stash.Playground/wwwroot/css/app.css` and `stash-icon.svg` from the VS Code extension. NOT npm-red / NuGet-blue. | User-locked brand direction. Visual consistency across Playground + VS Code extension + Website is a deliberate property of the Stash family. |
| 2026-06-04 | Information-architecture borrows from npm (cleaner/airier — suits the thinner Bucket-A field set) with NuGet patterns where they fit (versions table, dependencies list). One coherent direction, not a mashup. Reimplement; never copy npmjs.com/nuget.org CSS or trademarked assets. | User-locked. |
| 2026-06-04 | Two-segment scoped routes throughout (`/packages/@{scope}/{name}` and `/packages/@{scope}/{name}/v/{version}`). The literal `@` is a UI affordance, not part of the registry path. | The registry routes are two-segment scoped (per `Stash.Registry/CLAUDE.md` route table); the website mirrors that, and the `@` makes the URL match the typical user spelling without polluting the registry path. |
