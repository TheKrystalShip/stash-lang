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

