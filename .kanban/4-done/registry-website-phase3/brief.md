# RFC: Registry Website Phase 3 — Authenticated Maintainer Site

> **Status:** Draft
> **Owner:** cristian.moraru@live.com
> **Created:** 2026-06-04
> **Slug:** registry-website-phase3
> **Milestone:** website

## Summary

Extend `Stash.Registry.Web` with an authenticated **maintainer area** so package
owners can log in, see all of their packages (including private ones), manage
the API tokens issued to them, and perform the safe owner-side write actions
that the registry already exposes (deprecate / un-deprecate, change package
visibility). The site continues to be a **backend-for-frontend (BFF)**: the
browser only ever talks to the website origin, the website talks to the
registry server-to-server, and a session token lives entirely server-side keyed
by an HTTP-only cookie. The registry gains **zero** new endpoints — the BFF
composes the auth, search, token-CRUD, deprecate, and visibility endpoints that
shipped earlier.

The Phase-2 anonymous pages (`/`, `/search`, `/packages/@scope/name`,
`/packages/@scope/name/v/{version}`, `/health`) and their shared shell
(`Pages/Shared/_Layout.cshtml`) are **byte-unchanged on disk**: no Phase-2
file is edited, the anonymous `HttpRegistryClient` they inject is untouched.
All new behavior lives in a parallel **authenticated area** with its own
pages, its **own `_MaintainerLayout.cshtml`** (sharing `wwwroot/css/site.css`
for visual consistency), its own `IAuthenticatedRegistryClient`, and its own
DI surface. The anonymous Phase-2 surface gains zero new chrome (no header
"Log in" link) — the maintainer entry point is the bare `/login` route that
authenticated users bookmark or are redirected to.

New surface (the "safe maintainer loop"):

| Route | Page |
| --- | --- |
| `GET /login`, `POST /login` | Username/password form. POST exchanges credentials for the server-side session. |
| `POST /logout` | Revokes the minted publish token, clears the session, redirects to `/`. |
| `GET /dashboard` | "My packages" — `IRegistryClient.SearchAsync(owner=<me>)` threaded with the session token (returns both public and private packages owned by the signed-in user). |
| `GET /manage/@{scope}/{name}` | Dedicated maintainer view for a single owned package: deprecate / un-deprecate (package + version), change visibility. |
| `GET /settings/tokens`, `POST …`, `POST …/{id}/revoke` | API-token management: list, create, revoke. |

## Motivation

Phase 2 made the registry browsable but kept the website strictly anonymous:
package owners cannot discover their own private packages, cannot rotate the
tokens the CLI relies on, and cannot deprecate a broken version without
shelling out to the CLI. Today the only way to perform these actions is by hand
through HTTP, which makes the registry feel like an API endpoint behind a
read-only window.

This phase exists because the **API readiness work is already done**: login
mints a `read`-ceiling token, but a `user`-role caller holding *that* read token
is already allowed to issue a `publish`-ceiling token via
`POST /api/v1/auth/tokens` (verified — `RegistryAuthorizer.cs:135` + the
ceiling gate on admin in `AuthController.cs:279-312`), and search threads the
session principal into the visibility predicate (verified —
`SearchController.cs:81` + `StashRegistryDatabase.cs:142-254`). So a BFF can
compose the existing endpoints into an end-to-end maintainer loop **without**
touching the registry. Doing nothing forfeits that composability and forces
owners to either embed an unprivileged tool or wait for a Phase-3b that has
identical backend requirements.

The cost of mis-design is asymmetric. This is an auth feature; the canonical
omission example in `.claude/agents/architect.md` (and in this repo's history)
is a forgotten controller that skipped the decision point and passed a green
proxy test. The brief therefore puts the **chokepoint architecture first**
(separate authed client; global anti-forgery; session token never leaves the
server) and treats every shared concern with the **Construct over Detect over
Instruct** ladder.

## Goals

- Ship a logged-in user experience for the safe maintainer loop above, served
  out of the same `Stash.Registry.Web` project Phase 2 stood up.
- Keep the Phase-2 anonymous output **byte-unchanged**: the existing pages, the
  existing anonymous `HttpRegistryClient`, and the read-path visibility
  enforcement are not touched. Architecture-tested.
- Make token-threading a **Construct chokepoint**: an authenticated read or
  write that forgets the session token must be unrepresentable at the type /
  DI level. A separate `IAuthenticatedRegistryClient` type is constructed only
  with a session-bound `HttpClient` whose `Authorization` header is always set;
  the public `IRegistryClient` is untouched and continues to send no
  `Authorization` header.
- Make CSRF a **Construct chokepoint**: `AutoValidateAntiforgeryTokenAttribute`
  is registered globally so any unsafe-method request without a valid token
  fails closed. Lands in the same phase that introduces the first POST
  (`/login`).
- Keep the session token off the wire to the browser: it lives in a
  `DataProtection`-protected server-side session store, keyed by an HTTP-only,
  SameSite=Strict, Secure cookie. The cookie carries an opaque session id only.
- Treat the BFF's bounded-domain decisions (token ceiling, visibility values,
  user roles) as enum reuse from `Stash.Registry.Contracts/BoundedDomains.cs` —
  no inlined wire strings.
- Surface no Bucket-B fields and no out-of-scope endpoints. No yank/unpublish
  UI, no ownership/role-management UI, no metrics, no profile/password
  settings, no self-service sign-up.

## Non-Goals

- **No new registry API endpoints.** The BFF composes existing endpoints
  (`POST /auth/login`, `POST /auth/tokens`, `DELETE /auth/tokens/{id}`,
  `GET /auth/whoami`, `GET /auth/tokens`, `GET /search?owner=`,
  `GET …/packages/{scope}/{name}`, `PATCH/DELETE …/deprecate`,
  `PATCH/DELETE …/{version}/deprecate`, `PATCH …/visibility`). If a maintainer
  workflow seems to need a new registry endpoint, **stop and re-spec** — the
  task statement is explicit that the registry-side change list is empty.
- **No unpublish / yank** (`DELETE …/{version}` minus deprecate). Deferred to a
  future Phase-3b. The maintainer page does not surface this control.
- **No ownership / role management** (`GET/PUT/DELETE …/roles`). Deferred to
  Phase-3b.
- **No admin features.** No user-create, role-change, audit search, advisory
  management, quarantine, webhooks, reserved-prefix UI. These are Phase 4 and
  Bucket B.
- **No metrics / download counts / dependents / provenance / verified /
  signed labels.** Bucket B; the Phase-2 `FieldDisciplineMetaTests` extends to
  cover the new pages so a placeholder cannot land silently.
- **No profile / password / account settings.** The registry has no endpoint
  for this; the "settings" surface is exactly token management.
- **No self-service sign-up.** `POST /auth/register` exists but is explicitly
  out — accounts are created via CLI or admin only. There is no `/register`
  route.
- **No browser → registry direct calls.** The browser still only talks to the
  website origin. CORS stays off on the registry.
- **No client-side visibility logic.** The BFF threads the token and trusts
  the registry's 404-for-hidden / visibility predicate; the BFF never decides
  what an authenticated user can see.
- **No edits to ANY Phase-2 file** — pages, shell layout, anonymous
  `HttpRegistryClient` are all byte-identical on disk. The Maintainer area
  uses its own `_MaintainerLayout.cshtml`. The anonymous home/search/package/
  version/health pages render zero new chrome.
- **No language / stdlib changes.** The `.claude/language-changes.md` checklist
  does not apply.

## Design

The destination is one project (`Stash.Registry.Web`) with **two parallel
client surfaces** — a public one (Phase 2, unchanged) and an authenticated one
(new). The authenticated surface is a Razor area, has its own DI registrations,
its own session-bound client type, and its own page set. Login binds the
server-side session; the authenticated client refuses to construct without a
session token; the per-request session resolver is the **single chokepoint**
through which every authenticated registry call is routed.

### Surface

**New ASP.NET Core area.** `Stash.Registry.Web/Areas/Maintainer/Pages/` —
co-located `.cshtml` + `PageModel`s, area-scoped routing
(`@page "/{path}"` with `[Area("Maintainer")]` via convention) so the public
routes are not disturbed. The area's pages are the only ones that bind the
authenticated client.

**New typed authenticated client.**

```csharp
namespace Stash.Registry.Web.Auth;

public interface IAuthenticatedRegistryClient
{
    Task<PagedResponse<PackageSummaryResponse>> SearchOwnedAsync(SearchQuery, CancellationToken);
    Task<TokenListResponse> ListTokensAsync(CancellationToken);
    Task<TokenCreateResponse> CreateTokenAsync(TokenCreateRequest, CancellationToken);
    Task RevokeTokenAsync(string tokenId, CancellationToken);
    Task<DeprecationResponse> DeprecatePackageAsync(string scope, string name, DeprecatePackageRequest, CancellationToken);
    Task<DeprecationResponse> UndeprecatePackageAsync(string scope, string name, CancellationToken);
    Task<DeprecationResponse> DeprecateVersionAsync(string scope, string name, string version, DeprecateVersionRequest, CancellationToken);
    Task<DeprecationResponse> UndeprecateVersionAsync(string scope, string name, string version, CancellationToken);
    Task<SetVisibilityResponse> SetVisibilityAsync(string scope, string name, SetVisibilityRequest, CancellationToken);
    Task<WhoamiResponse> WhoamiAsync(CancellationToken);
    Task<PackageDetailResponse?> GetPackageAsync(string scope, string name, CancellationToken); // authed view (for /manage)
}
```

Implementation: `HttpAuthenticatedRegistryClient`. Constructor takes
`IHttpClientFactory` and a per-scope `ISessionTokenAccessor`. The constructor
**throws** if no session token is currently in scope (fail-closed Construct).
Every method creates the HTTP client and unconditionally sets
`client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token)`
before issuing the request. There is no public method that lets a caller skip
the header.

**New session-management primitives** (`Stash.Registry.Web/Auth/`):

- `BffSession` — record carrying `Username`, `PublishTokenJwt`,
  `PublishTokenId` (returned by `POST /auth/tokens`), `ExpiresAt`. **The JWT
  string never leaves the server.** (No `Role` field — `LoginResponse` does
  not carry a role and the only UI that would need it, the token-create
  dropdown, is intentionally hardcoded to `[Read, Publish]` in v1 because
  admin tooling is out of scope. Wiring `whoami` into login just to populate
  an unused field would be ceremony; if a future phase introduces a
  role-gated UI it adds the `whoami` call then.)
- `ISessionStore` — `Task<BffSession?> GetAsync(string sessionId)`,
  `Task SetAsync(string sessionId, BffSession session, DateTimeOffset expiresAt)`,
  `Task RemoveAsync(string sessionId)`. Default implementation is
  `InMemorySessionStore` (a `ConcurrentDictionary` with TTL eviction; v1
  swappable with a Redis/Postgres-backed store later).
- `ISessionTokenAccessor` — per-request scoped accessor that resolves the
  current `BffSession` from the request's session cookie and surfaces the
  publish JWT to `HttpAuthenticatedRegistryClient`. Throws a typed
  `NoActiveSessionException` if invoked outside an authed scope. Concrete:
  `CookieSessionTokenAccessor` (reads `IHttpContextAccessor`). The throw is
  a **fail-closed backstop** for the DI chokepoint, not the user-facing
  auth gate — the gate is the cookie-auth handler + `[Authorize]` convention
  below, which 302s anonymous users before resolution.
- `SessionCookieAuthenticationHandler` — ASP.NET Core `AuthenticationHandler`
  (the user-facing auth gate). Registered via
  `services.AddAuthentication("BffCookie").AddScheme<SessionCookieAuthenticationOptions, SessionCookieAuthenticationHandler>("BffCookie", null)`.
  Reads the session cookie, looks up the `BffSession` in `ISessionStore`,
  populates `HttpContext.User` with a `ClaimsPrincipal` carrying `Name =
  session.Username` and the `BffCookie` authentication type. A missing /
  expired / unknown session leaves `User.Identity.IsAuthenticated == false`
  and triggers the standard `[Authorize]` 302 to `/login?returnUrl=…` via
  `CookieAuthenticationDefaults`-style redirect-config (no `Challenge` body).
  This is what makes the `[Authorize]` convention in A2 work — without it,
  anonymous → `IAuthenticatedRegistryClient` → throw → 500.
- `SessionCookie` — central definition of the cookie name (e.g.
  `"stash_web_session"`), options (`HttpOnly = true`, `SameSite = Strict`,
  `Secure = true` in non-Development), and a `DataProtection`-purpose constant.
- `LoginService` — owns the login workflow: validate credentials against
  `POST /api/v1/auth/login` (using a *separate* `HttpClient` named
  `"AuthLogin"` that sends no `Authorization` header), then immediately mint
  the session's publish token via `POST /api/v1/auth/tokens` (using a
  `HttpClient` named `"AuthMint"` that threads the just-returned read JWT —
  this is the *only* place that uses the read login token), build a
  `BffSession`, persist to `ISessionStore`, set the cookie. The read login
  token is discarded after the mint completes; it is not persisted, never
  becomes the session token.
- `LogoutService` — calls `DELETE /api/v1/auth/tokens/{publishTokenId}` (with
  the publish JWT as bearer), removes the session from `ISessionStore`, clears
  the cookie. Fire-and-forget the revoke if it fails; always clear the cookie.

**DI registration** (`Program.cs`, additive only):

- The anonymous `IRegistryClient` registration (Phase 2) is **untouched**.
- `IAuthenticatedRegistryClient` is registered **Scoped** (per HTTP request).
  Its factory throws if `ISessionTokenAccessor.TryGetSession(out _)` is false.
  This is the chokepoint: injecting `IAuthenticatedRegistryClient` outside an
  authed scope fails-closed at resolution time.
- Three named `HttpClient`s: `"RegistryClient"` (Phase 2, anonymous,
  unchanged), `"AuthLogin"` (anonymous, base address from
  `RegistryClientConfig.BaseUrl`), `"AuthMint"` (anonymous; the read JWT is
  attached per-call by `LoginService`).
- ASP.NET Core `DataProtection` configured (purpose string defined in
  `SessionCookie`).
- `services.AddAuthentication("BffCookie").AddScheme<…, SessionCookieAuthenticationHandler>("BffCookie", null)`
  + `services.AddAuthorization()` so `[Authorize]` works.
  `app.UseAuthentication(); app.UseAuthorization();` is added to the pipeline
  before `app.MapRazorPages()`. This is the user-facing auth gate.
- A **page convention** in `MaintainerAreaConventions.cs` adds
  `[Authorize(AuthenticationSchemes = "BffCookie")]` to every page under
  `Areas/Maintainer/Pages/`, so any anonymous request to a Maintainer page
  is 302'd to `/login?returnUrl=<original>` by the auth handler's
  redirect-config — **never** reaches the `IAuthenticatedRegistryClient`
  DI factory. The DI factory's throw is the fail-closed backstop, not the
  primary gate.
- `AutoValidateAntiforgeryTokenAttribute` registered globally via
  `services.AddRazorPages(o => o.Conventions.ConfigureFilter(new AutoValidateAntiforgeryTokenAttribute()));`
  (or equivalent global registration). Every POST that does not carry a valid
  anti-forgery token fails closed.

**Layout strategy — own-layout for the Maintainer area.**
Phase-2's `Pages/Shared/_Layout.cshtml` is **not edited**. The Maintainer
area ships its own `Areas/Maintainer/Pages/Shared/_MaintainerLayout.cshtml`,
which `@{ Layout = "_MaintainerLayout"; }` via the area's
`_ViewStart.cshtml`. The Maintainer layout reuses
`wwwroot/css/site.css` (the Catppuccin palette) and the `stash-icon.svg`
brand mark for visual consistency, and adds an authed-user header element
(`<username> · Log out`). The Phase-2 anonymous pages, anonymous shell, and
anonymous client all keep their Phase-2 bytes — the byte-unchanged
constraint is enforced by a literal `git diff` on the Phase-2 file set
(architecture test below), not by an HTML-snapshot comparator (which would
be fragile to anti-forgery tokens and Razor whitespace).

### Semantics

**Login (`POST /login`).** Required form: `username`, `password`, plus the
anti-forgery token. Pipeline:

1. Call `POST /api/v1/auth/login` with the credentials. On 401 → render the
   login page with an error banner and **no session created** (no cookie set).
   On 400 → bubble the validation message. On 5xx → render the 502 page.
2. On 200 → the read JWT is returned. **Eager-mint** a publish token via
   `POST /api/v1/auth/tokens { "ceiling": "publish", "name": "stash-web session",
   "expiresIn": "8h" }` (the session lifetime — user-decided 2026-06-04;
   surfaced as a config key, e.g. `Bff:SessionLifetime`, defaulting to `"8h"`,
   so a self-hosted operator can tune it). The mint uses the read JWT as bearer.
   On any non-201, log out the read token by best-effort and render the login
   page with an error; do **not** establish a session.
3. Build `BffSession` from the `TokenCreateResponse`. Persist to
   `ISessionStore` with `ExpiresAt = TokenCreateResponse.ExpiresAt`. Emit the
   session cookie. Redirect to `?returnUrl` if it's a same-origin relative path,
   else `/dashboard`.

**Logout (`POST /logout`).** Reads the session, calls
`DELETE /api/v1/auth/tokens/{publishTokenId}` (best-effort; success or 4xx
both proceed), removes the session, clears the cookie, redirects to `/`.

**Dashboard (`GET /dashboard`).** Calls `SearchOwnedAsync(SearchQuery {
owner = <session.Username>, sort = Updated, pageSize = N })`. The
authenticated client threads the publish JWT; the registry's
`SearchPackagesAsync` therefore intersects `name/desc LIKE`, the
visibility predicate (`callerUsername != null`), and the owner filter, so the
result includes the user's **private** packages alongside the public ones.
Cards reuse `Views/PackageCard.cshtml` from Phase 2 (so visibility-aware
behavior comes from the *client* injected, not from a card-side branch).

**Owned-package management (`GET/POST /manage/@{scope}/{name}`).** The page
loads `GetPackageAsync(scope, name)` via the authenticated client (to see
private packages). If the response is 404 → 404 page. If 200 → render
read-only metadata plus the four maintainer controls:

- **Deprecate package** — form with `message` and optional `alternative`; POST
  calls `DeprecatePackageAsync`. Authz failures (registry 403) → display an
  inline error and re-render the page; **the BFF does not pre-decide
  authorization**.
- **Un-deprecate package** — single-button POST; calls
  `UndeprecatePackageAsync`.
- **Deprecate / un-deprecate version** — per-version control in the versions
  table; POST calls the version variant.
- **Change visibility** — dropdown bound to the `Visibilities` enum; POST
  calls `SetVisibilityAsync`. Wire values are pinned by the enum (no inlined
  `"public"` / `"private"` strings).

Successful actions → re-render the page with a success banner; the page is the
authoritative read of the post-mutation state.

**Token management (`GET /settings/tokens`).** Lists active tokens
(scope, description, createdAt, expiresAt; **never the token value**) via
`ListTokensAsync`. The page provides:

- A "Create token" form (`POST /settings/tokens`) bound to a
  `TokenCreateRequest`. Wire ceiling values are constrained to the
  `TokenScopes` enum members surfaced on the page (i.e. `read`, `publish`;
  `admin` is selectable only when the session's role is `admin` — but admin
  tooling is out of scope so the dropdown is built from
  `[TokenScopes.Read, TokenScopes.Publish]` only in v1). On success, the
  newly minted token value is shown **once** with a "copy and close" affordance
  and an explicit warning that it cannot be retrieved again. The token value is
  passed via `TempData` (which uses DataProtection-encrypted cookies) **once**,
  not persisted.
- A "Revoke" button on every row (`POST /settings/tokens/{id}/revoke`) that
  calls `RevokeTokenAsync`. The session's own publish token is **not**
  revocable from this page (the row is rendered with a "current session"
  badge and no revoke button; revoking it would terminate the session you're
  using to call the endpoint). The session is terminated only via
  `POST /logout`.

**Authn failure (401 from registry on an authed call).** The BFF
interprets a 401 as "session is no longer valid" — clears the cookie, removes
the server-side session, and redirects to `/login?expired=1`. This handles the
case where an admin revoked the user's session token out-of-band.

**Authz failure (403 / 404 from registry on a write).** Surface the
registry's `ErrorResponse.Message` inline as a banner; remain on the page.
Do not interpret 403 as expired (that's 401's job).

**Network failure / 5xx from registry.** Same as Phase 2: the website's 502
page indicates the registry is unreachable; the website itself does not crash
and the session is preserved (the user can retry).

### Implementation Path

The single end-to-end path the design must keep intact across phases:

```
Browser
  ├── HTTP-only session cookie ───────► Stash.Registry.Web (BFF)
  │                                       │
  │   (anonymous pages, Phase 2)          ├── _Layout.cshtml renders auth-aware header
  │                                       │
  │   (authenticated area, Phase 3)       ├── Razor Page (Areas/Maintainer/Pages/*)
  │                                       │     │
  │                                       │     └── (Construct) IAuthenticatedRegistryClient
  │                                       │           │   resolved via ISessionTokenAccessor
  │                                       │           │   — throws if no session in scope
  │                                       │           │
  │                                       │           └── HttpClient w/ Bearer <publish JWT>
  │                                       │                  always attached, no opt-out
  │                                       │
  │                                       └── Anti-forgery middleware
  │                                             (global; unsafe methods fail closed)
  │
  └──────────────────────────────────────────────────────────────────────────► Stash.Registry
                                                                                │
                                                            (zero changes;     ├── /auth/login, /auth/tokens, /auth/tokens/{id}
                                                             composed only)    ├── /search?owner=<me> (PDP threads principal)
                                                                                ├── /packages/{scope}/{name} (visibility-aware read)
                                                                                ├── /packages/.../deprecate, .../visibility
                                                                                └── /auth/whoami (debugging / status)
```

### Cross-Cutting Concerns

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| **C1. Zero `Stash.Registry` dependency** | `Stash.Registry.Web.csproj` (one ProjectReference: `Stash.Registry.Contracts`) | **Construct** — a stray `<ProjectReference>` breaks the build via the architecture-test guard. **Detect (existing, Phase 2)** — `WebProjectIsolationMetaTests` reflects + parses the csproj; no Phase-3 file may introduce a forbidden reference. The Phase-3 phases all run this test in their per-phase verify so a regression is caught at the phase boundary, not at `/done`. |
| **C2. Token-threading on authenticated registry calls** | `IAuthenticatedRegistryClient` (single new type, one implementation, Scoped DI registration that resolves through `ISessionTokenAccessor`) | **Construct** — the type physically wraps an `HttpClient` whose `Authorization` header is set inside every method body; there is no public method that sends a request without the header. The DI factory throws `NoActiveSessionException` if no session is in scope, so an authed page that forgets to require authentication fails closed at resolution time, not silently with an anonymous response. **Detect (sink-targeted Roslyn scan)** — `AuthClientChokepointMetaTests` scans `Stash.Registry.Web/` for `HttpClient.SendAsync` / `GetAsync` / `PostAsync` / `DeleteAsync` call sites; the only allowed locations are inside `HttpAuthenticatedRegistryClient` (always threaded), `HttpRegistryClient` (anonymous; Phase-2 file unchanged), and `LoginService` (the bootstrapping anonymous calls — a small explicit allowlist with a comment naming each). Ships a fail-path fixture that reintroduces a rogue `HttpClient` call and asserts the scan trips. The binding-floor is: at least one allowed call site must be found, so a vacuous pass (scan bound nothing) fails loudly. |
| **C3. CSRF on every state-changing request** | `AutoValidateAntiforgeryTokenAttribute` registered globally in `Program.cs` (Phase A1) | **Construct** — every POST/PUT/PATCH/DELETE without a valid anti-forgery token fails 400 at the filter, before any page handler runs. The registration is a single line in `Program.cs`; removing it would break dozens of tests. **Detect (behavioral)** — `AntiForgeryConstructMetaTests` drives a `WebApplicationFactory` POST to every state-changing route (`/login`, `/logout`, `/manage/.../deprecate`, `/manage/.../visibility`, `/settings/tokens`, `/settings/tokens/{id}/revoke`) **without** an anti-forgery token and asserts each returns 400. The behavioral check is the load-bearing assertion (it's what the filter actually does); a best-effort structural reflection check (look for the attribute in the page-convention filter list) is included as a secondary signal but not relied on (page conventions register via `Conventions.ConfigureFilter`, not `MvcOptions.Filters`, so a structural-only check would be brittle). Ships a fail-path fixture (a test that *would* succeed if the filter were ungated) to prove the guard has teeth. |
| **C4. Session token never reaches the browser** | `BffSession` lives **only** in `ISessionStore`; the cookie carries only an opaque session id; `LoginResponse.AccessToken` / `TokenCreateResponse.Token` flow only into the store | **Construct** — the cookie value is a freshly-generated session id (e.g. 256-bit base64), never the JWT. The Razor page models never read `BffSession.PublishTokenJwt` (it has no getter exposed to views — accessor methods on `ISessionTokenAccessor` return what the authed client needs, not the JWT itself). **Detect (Roslyn + integration)** — `SessionTokenLeakMetaTests` (1) scans `.cshtml` and view-side `.cs` files asserting no reference to `PublishTokenJwt`; (2) runs a `WebApplicationFactory` walk of `/dashboard`, `/settings/tokens`, `/manage/@scope/name` and asserts the JWT string from the fixture never appears in the rendered HTML body **or** in any `Set-Cookie` header. Fail-path fixture: a page model that deliberately leaks the JWT into ViewData; the scan flags it. |
| **C5. No client-side visibility logic** | The registry's PDP (`SearchPackagesAsync` + `GetPackage`'s `[RegistryAuthorize]`). The BFF threads the token and trusts the registry's 404 / filtered result. | **Construct** — the Maintainer pages have no `if (user.Owns(package))` or `if (package.Visibility == Private)` branches; they render whatever the authenticated client returns. **Instruct (load-bearing)** — the brief states the rule explicitly and the page-level tests pin it: an `IAuthenticatedRegistryClient` returning a `PackageDetailResponse` whose visibility is `private` renders the page; a `null` return renders 404. The page does not introspect `Visibility` to decide what to show. |
| **C6. Bounded-domain wire values are typed** | `Stash.Registry.Contracts/BoundedDomains.cs` (`TokenScopes`, `Visibilities`, `UserRoles`) | **Construct** — the BFF binds form values via Razor model binding to enum-typed properties (`Visibilities`, `TokenScopes`); an illegal value won't compile and an unrecognised wire value is a 400 from model binding. Razor view dropdowns iterate `Enum.GetValues<T>()` for option values and use `[Display]` / a small `ViewLabels` const class for human labels. No Web-side `NoMagicAuthStringsMetaTests` — enum-reuse + the existing registry-side guard cover the wire boundary; recorded as a Decision-Log entry. |
| **C7. Phase-2 files byte-unchanged on disk** | The Phase-2 file set: `Pages/Index.cshtml(.cs)`, `Pages/Search.cshtml(.cs)`, `Pages/Package.cshtml(.cs)`, `Pages/Version.cshtml(.cs)`, `Pages/Health.cshtml(.cs)`, `Pages/Shared/_Layout.cshtml`, `Pages/Shared/_ViewImports.cshtml`, `Pages/Shared/_ViewStart.cshtml`, `Services/HttpRegistryClient.cs`, `Services/IRegistryClient.cs`, `Services/RegistryClientException.cs`, `Configuration/RegistryClientConfig.cs`, `Rendering/IReadmeRenderer.cs`, `Rendering/ReadmeRenderer.cs`, `Rendering/SafeUrl.cs` | **Construct** — the new area lives under `Areas/Maintainer/`; the new client is a new type; the Maintainer area has its own `_MaintainerLayout.cshtml` so no Phase-2 shell file is edited. The new DI registrations in `Program.cs` are additive (lines added, no lines removed). **Detect** — `PhaseTwoFilesByteUnchangedMetaTests` shells out to `git diff --quiet <phase-2-merge-base> -- <Phase-2 file set>` (the file set is a pinned const inside the test) and fails if the diff is non-empty. The Phase-2 merge-base is computed via `git merge-base HEAD main` then `git log --oneline -- .kanban/4-done/registry-website-phase2/` to locate the merge commit deterministically (or pinned via an env var the test reads). Ships a fail-path fixture (a deliberately-modified Phase-2 file in a sibling test fixture branch / a synthetic diff string) proving the diff comparator trips. The pinned file set is the binding floor: removing a path from it must be a deliberate edit caught at review. **No HTML-snapshot byte-equality test** — that would be fragile to anti-forgery token nonces and Razor whitespace; the file-diff is the load-bearing invariant. |

## Acceptance Criteria

End-to-end behavior:

- `dotnet build Stash.sln` succeeds; the new authenticated area builds inside
  the existing `Stash.Registry.Web` project.
- `dotnet test` (full, unfiltered) is green, including the four new meta-tests
  (`AuthClientChokepointMetaTests`, `AntiForgeryConstructMetaTests`,
  `SessionTokenLeakMetaTests`, `PhaseTwoFilesByteUnchangedMetaTests`) and the
  existing `WebProjectIsolationMetaTests` / `FieldDisciplineMetaTests` /
  `ReadmeChokepointMetaTests`.
- An anonymous (no cookie) `GET /dashboard`, `GET /manage/@scope/name`, or
  `GET /settings/tokens` returns **302 Found** to `/login?returnUrl=…` (NOT
  500 — the auth handler + `[Authorize]` convention 302s before the page
  model is constructed and the `IAuthenticatedRegistryClient` factory ever
  runs). The DI factory's `NoActiveSessionException` is a fail-closed
  backstop tested by direct unit-test, never reached on a normal anonymous
  request.
- A developer can log in (`GET /login` → submit form → server-side session
  established → cookie set), and:
  - `GET /dashboard` lists their owned packages, **including at least one
    private package** that an anonymous `GET /search?owner=<them>` does not
    return (proven by an integration test with two fixture packages — one
    public, one private — and an authed-vs-anonymous comparison).
  - `GET /manage/@my-org/my-lib` renders maintainer controls for a package
    they own; `POST` to the deprecate / visibility forms succeeds and the page
    re-renders with the new state.
  - `GET /settings/tokens` lists active tokens; creating a token shows the
    value **once** and the row appears on refresh; revoking a token removes the
    row on refresh.
  - `POST /logout` revokes the minted publish token (verified via a stub that
    records the `DELETE …/{id}` call) and clears the cookie; subsequent
    `GET /dashboard` redirects to `/login`.

Error / authz behavior:

- `POST /login` with a wrong password renders the login page with an error and
  sets no cookie (verified — no `Set-Cookie` header, no session created in
  the store).
- An authenticated user who does **not** own a package navigating
  `GET /manage/@other-org/their-lib`:
  - If the package is public → renders maintainer controls; `POST` to deprecate
    is rejected by the registry as 403, the BFF surfaces the registry's error
    banner inline. *(The BFF does not pre-decide authorization.)*
  - If the package is hidden → the registry returns 404, the BFF renders the
    website's 404 page.
- A POST to any state-changing route **without** a valid anti-forgery token
  returns 400 (verified by an integration test that strips the token).
- An authed call that receives 401 from the registry (e.g. session token
  revoked out-of-band) clears the cookie + session and redirects to
  `/login?expired=1`.

Cross-entrypoint / structural behavior:

- `WebProjectIsolationMetaTests` (Phase 2) still passes — Phase 3 adds no
  forbidden reference.
- `PhaseTwoFilesByteUnchangedMetaTests` passes — `git diff` against the
  Phase-2 merge-base over the pinned Phase-2 file set is empty (no
  Phase-2 page, no Phase-2 shell layout, and no anonymous client file
  is touched).
- `AuthClientChokepointMetaTests` passes — no `HttpClient` call site outside
  the three pinned locations; fail-path fixture trips.
- `AntiForgeryConstructMetaTests` passes — global filter present; fail-path
  fixture trips.
- `SessionTokenLeakMetaTests` passes — the publish JWT never appears in any
  rendered HTML or `Set-Cookie` header for any walked authed page.
- The publish token is minted via `POST /auth/tokens { "ceiling": "publish" }`
  using exactly the `Stash.Registry.Contracts` DTOs (no Web-side duplicate),
  and the form-binding for visibility goes through the `Visibilities` enum
  (no inlined `"public"` / `"private"` strings — `grep -nF '"public"'
  Stash.Registry.Web/Areas/` finds zero hits).
- Final verify (`dotnet test`, unfiltered) runs the **full** suite, no
  namespace-exclude filter, and no existing registry meta-test
  (`AuthzCoverageMetaTests`, `AuthzDispatchCoverageMetaTests`,
  `NoMagicAuthStringsMetaTests`, `OpenApiCoverageMetaTests`) regresses because
  no registry endpoint was added or modified.

## Phases

The full phase list lives in `plan.yaml`. The high-level shape with the
omission-guards placed at or before the first phase that could violate them:

- **A1 — BFF auth infrastructure + login/logout + global anti-forgery + the
  byte-unchanged guard.** Stand up `ISessionStore` (in-memory),
  `ISessionTokenAccessor`, `LoginService`, `LogoutService`, `SessionCookie`,
  `BffSession`, **`SessionCookieAuthenticationHandler`** (the user-facing
  auth gate — rehydrates `HttpContext.User` from the session cookie and
  configures the `/login` redirect on challenge); wire DataProtection +
  authentication / authorization middleware (`UseAuthentication` +
  `UseAuthorization`) + the global `AutoValidateAntiforgeryTokenAttribute`;
  add `Pages/Login.cshtml` and `Pages/Logout.cshtml` at the project root
  (NOT in the Maintainer area — login is the on-ramp; these pages do NOT
  carry `[Authorize]`). Phase-2 files are untouched (no header link). Land
  `PhaseTwoFilesByteUnchangedMetaTests` and `AntiForgeryConstructMetaTests`
  here, with their fail-path fixtures. CSRF lands with the first POST —
  login itself. The login flow uses two named anonymous `HttpClient`s
  (`AuthLogin`, `AuthMint`); the `IAuthenticatedRegistryClient` does not
  exist yet.
- **A2 — IAuthenticatedRegistryClient + Maintainer-area scaffold (own layout,
  Authorize convention) + dashboard + the chokepoint guards.** Introduce
  `IAuthenticatedRegistryClient` / `HttpAuthenticatedRegistryClient` with
  the methods listed in Surface; wire the Scoped DI registration that throws
  if no session is in scope (fail-closed backstop). Create the Maintainer
  area with its own `_MaintainerLayout.cshtml` and a
  `MaintainerAreaConventions` page-convention that applies `[Authorize(AuthenticationSchemes = "BffCookie")]`
  to every Maintainer page — anonymous requests 302 to `/login?returnUrl=…`
  via the auth handler from A1, NEVER reach the DI factory. Ship
  `GET /dashboard` calling `SearchOwnedAsync`. Land
  `AuthClientChokepointMetaTests` and `SessionTokenLeakMetaTests` here, with
  their fail-path fixtures. **These guards land *with* the first authed call
  site, not after.**
- **A3 — Owned-package management page (deprecate + visibility).** Add
  `Areas/Maintainer/Pages/Manage.cshtml` rendering the metadata,
  versions table with per-version deprecate controls, the package-level
  deprecate / un-deprecate forms, and the visibility dropdown. POST handlers
  call the authed client. All forms route through global anti-forgery
  (already in place). Tests cover happy path, authz-fail (registry 403),
  hidden-package 404, and an authed-vs-anonymous visibility-aware read
  (the authed `GetPackageAsync` returns the private package; the anonymous
  client returns 404).
- **A4 — Token management page (list / create / revoke).** Add
  `Areas/Maintainer/Pages/Settings/Tokens.cshtml`. POST handlers call
  `CreateTokenAsync` / `RevokeTokenAsync`. The newly minted token value is
  surfaced once via `TempData` and never persisted. The session's own token
  is rendered with a "current session" badge and no revoke button. Tests:
  list/create/revoke happy paths, token value shown once, current-session
  token unrevocable from the UI.
- **A5 — Docs.** `Stash.Registry.Web/README.md` gains an "Authenticated
  maintainer area" section describing the BFF model, what's behind login,
  what's deliberately out of scope (yank, role mgmt, admin, sign-up,
  profile). Cross-link from `docs/Registry — Package Registry.md` "Clients"
  section. This is a pure docs phase; no language/stdlib generator applies.

Phase-ordering rationale: the **chokepoint architecture** (separate authed
client + global anti-forgery + server-only session) is built **before** the
pages that consume it. Global anti-forgery lands with the first POST (login,
A1) — not deferred to write actions (A3). The chokepoint Roslyn scans
(`AuthClientChokepointMetaTests`, `SessionTokenLeakMetaTests`) land with the
first authed call site (A2), not at the end, so A3–A4 merge with the
invariants enforced. The byte-unchanged guard for the anonymous Phase-2 pages
lands in A1 — the moment a single shared file (`_Layout.cshtml`) might
regress.

## Open Questions

- **Visibility-aware caching is deliberately out of scope.** Phase 2 explicitly
  shipped `HttpRegistryClient` as a thin pass-through ("Server-side caching is
  a later-phase optimization"). Phase 3 keeps that posture: there is no
  Phase-2 cache to retrofit, and the separate authed client means
  "anonymous-cached response served to an authed user" is structurally
  impossible (different clients, different DI scopes, no shared cache layer).
  If a future phase introduces a server-side cache, the cross-cutting concern
  **C2** must be extended to require principal-keyed caching (anonymous and
  authed views never share a key). Flagging here so the constraint is on the
  record.
- **Session store lifetime / eviction.** `InMemorySessionStore` is fine for
  v1 (single-instance hosting). Multi-instance hosting needs a distributed
  store (Redis / SQL Server / Postgres) — left as a TODO in A1's docstring;
  the `ISessionStore` seam exists so this is a configuration swap, not a
  redesign.
- **"current session" badge on the token page.** The dashboard knows the
  current session's `PublishTokenId` (from `BffSession`), so the row whose
  `tokenId == session.PublishTokenId` is rendered with the badge and no
  revoke button. If a future feature lets users revoke individual sessions,
  this becomes a "revoke session" affordance instead — out of scope here.
- **Login `returnUrl` handling.** Only same-origin relative paths are honored
  to prevent open-redirect. Confirmed as the v1 rule; the implementer
  enforces with `Url.IsLocalUrl(returnUrl)`.
- **Refresh-token use.** Phase 1 supports refresh tokens when the caller
  supplies `X-Machine-Id`. For v1 the BFF uses the **publish token's
  natural expiry** (== session lifetime), so a session that outlives the
  publish token simply re-prompts for login. Refresh-token plumbing is
  deferred; recorded so a later phase can opt in without redesigning the
  store.
- **Should the anonymous header surface a "Log in" link?** v1 says no
  (Decision Log) to keep Phase-2 files byte-equal. If the user wants the
  link, the cleanest fix is **not** to edit `_Layout.cshtml` but to ship a
  tiny client-side JS overlay that injects the link only when no session
  cookie is present (the cookie is HTTP-only, but its *absence* is JS-visible
  via `document.cookie === ""` from the same origin) — preserving the
  byte-unchanged file invariant. Flagged in case the v1 choice should be
  revisited.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-04 | **Extend the existing `Stash.Registry.Web` project** (user-locked). Auth code lives under a new `Areas/Maintainer/` area + new `Auth/` namespace; Phase-2 files stay byte-unchanged on disk (no exceptions). | Single project keeps the no-`Stash.Registry`-dependency guard centralized; an Areas-split makes the public-vs-authed boundary visible at the directory level; byte-unchanged is then trivially enforced via a literal `git diff` on the pinned Phase-2 file set. |
| 2026-06-04 | **Maintainer area uses its own `_MaintainerLayout.cshtml`** instead of editing the Phase-2 `Pages/Shared/_Layout.cshtml` with an auth-aware header. | Editing the Phase-2 shell with an `@if (IsAuthenticated)` branch — even an additive one — bumps a Phase-2 file off byte-equal. The own-layout collapses two problems at once: (1) preserves the user-locked "anonymous pages byte-unchanged" rule literally, and (2) the C7 guard reduces to a `git diff --quiet` on the pinned file set, which is cheap and reliable (an HTML-snapshot byte-equality test would be defeated by Razor's per-request anti-forgery token in any form tag-helper and by Razor `@if` whitespace, both common after a global anti-forgery filter is installed). Both layouts share `wwwroot/css/site.css` and the brand mark so the visual family stays consistent. |
| 2026-06-04 | **No "Log in" link in the anonymous header.** Authenticated users reach the maintainer area via the bare `/login` route (bookmark or external nav); the anonymous Phase-2 chrome is unchanged. | The user-locked Phase-2 pages list does not request a header chrome change; adding one would either bump a Phase-2 file off byte-equal (rejected, see above) or require a second mechanism (a client-side overlay) that is more complexity than the v1 affordance is worth. A future phase may revisit. |
| 2026-06-04 | **BFF with server-side session, HTTP-only cookie, separate authed client** (user-locked). The browser never sees the JWT. | Recommended by the doctrine over the "browser-direct-to-registry" alternative because (a) it lets the registry stay token-native (zero registry change), (b) the session token is invisible to JavaScript and to network logs, (c) the chokepoint is *structural* — pages literally cannot make an authed call without going through the typed authed client. The alternative (browser holds the JWT and posts CORS-laden writes to the registry) would force CORS, CSRF on the registry, and session-revocation plumbing on `Stash.Registry` — explicitly **rejected** by the milestone charter. |
| 2026-06-04 | **Eager-mint the publish token at login** — recommended. Lazy-mint listed as the alternative. | *Recommended (Make It Right):* one chokepoint, one revoke on logout, one always-present token threaded by the authed client; the authenticated dashboard works on the same token as the write path, so the same `IAuthenticatedRegistryClient` serves authed reads (private-package visibility) and writes uniformly. The `expires_in` is set to the session lifetime so an abandoned session self-cleans. *Alternative considered (lazy-mint):* mint on first write only; reads use the read login token. Pros: minimum standing privilege (browse-only sessions never hold a publish token). Cons: two tokens to manage, two revokes at logout, the read login token would have to be threaded by a *third* client variant (authed read with read-ceiling) doubling the chokepoint surface. Rejected: the privilege gain is small (the session token is server-side and short-lived) and the doctrine cost (two chokepoints) is large. Both designs are fail-closed; the discriminator is chokepoint simplicity, and Eager wins on that axis. |
| 2026-06-04 | **Owned-package management on a dedicated `/manage/@{scope}/{name}` view**, not inline controls on the Phase-2 package detail page — recommended. | Two structural reasons: (1) inline controls would force an edit to `Pages/Package.cshtml`, breaking the "Phase-2 anonymous pages byte-unchanged" rule (or forcing a per-page "is-owner?" branch that injects the authed client into a page that today injects only the anonymous one — muddying the chokepoint). (2) The maintainer view's data needs (private-visibility read of the package, plus the four write controls) are different from the anonymous detail view's needs (sanitized README, public metadata sidebar). Cleanly separated views keep both surfaces single-purpose. *Alternative (inline controls):* surface the same controls on `/packages/@scope/name` when `User.Identity.IsAuthenticated && ownerCheck`. Rejected for the chokepoint reason above; record so a future change has the trade-off documented. |
| 2026-06-04 | **Global `AutoValidateAntiforgeryTokenAttribute` lands in A1 (with login), not A3 (with writes).** | Login is itself a POST; deferring CSRF to A3 ships a window where `POST /login` runs without anti-forgery. The "Construct lands with or before the first phase that could violate it" rule is satisfied only if the global filter is in place before any POST handler exists. |
| 2026-06-04 | **No Web-side `NoMagicAuthStringsMetaTests`; enum reuse + Razor model binding suffice.** Recorded explicitly. | The BFF's bounded-domain surface is small (`TokenScopes`, `Visibilities`, `UserRoles`) and entirely typed by reuse of `Stash.Registry.Contracts/BoundedDomains.cs`. The registry's own `NoMagicAuthStringsMetaTests` guards the registry; an additional Web-side scan would be pure ceremony. The `done_when` includes an explicit `grep` check that wire literals do not appear in the Areas tree. |
| 2026-06-04 | **No new registry endpoints.** Source-verified that the composition is sufficient (`AuthController.cs:104-118, 135, 279-312`, `SearchController.cs:81`, `StashRegistryDatabase.cs:142-254`, `PackagesController.cs:407, 436, 465, 494, 615`). If implementation discovers a hole, **stop and re-spec**. | The task statement is explicit, and the verification above shows the registry is genuinely sufficient. Adding a registry endpoint here would silently expand the milestone's scope. |
| 2026-06-04 | **In-memory `ISessionStore` for v1; distributed store deferred.** | A pre-release registry has no multi-instance deployment story yet; the seam (`ISessionStore`) exists so the swap is a future configuration change, not a redesign. Recorded in Open Questions so the limitation is visible. |
| 2026-06-04 | **Logout best-efforts the token revoke; the cookie is cleared unconditionally.** | A failed revoke must not leave the user "logged in" client-side. The token's natural expiry caps the worst-case window; a logged audit-side warning (server-log only) makes the failure visible to operators. |
| 2026-06-04 | **The user-facing auth gate is a `SessionCookieAuthenticationHandler` + `[Authorize]` page convention; the `IAuthenticatedRegistryClient` DI factory's throw is a fail-closed *backstop*, not the primary gate.** | An anonymous → throw → 500 path would be a poor user experience and would silently mask a forgotten `[Authorize]` as a server error rather than a redirect to login. The auth handler 302s anonymous requests to `/login?returnUrl=…` before the page model is constructed; the DI throw remains as defense-in-depth (any future page that bypasses the convention still fails closed at resolution). |
| 2026-06-04 | **`BffSession` does NOT carry a `Role` field.** | `LoginResponse` does not return a role; populating `Role` would require an extra `GET /auth/whoami` call per login. The only UI that would consume the field — the token-create dropdown — is intentionally hardcoded to `[Read, Publish]` in v1 because admin tooling is out of scope. If a future phase introduces role-gated UI, it adds the `whoami` call then; doing it speculatively is ceremony. |
| 2026-06-04 | **The C7 byte-unchanged guard is `git diff --quiet` over a pinned Phase-2 file set, NOT an HTML-snapshot comparator.** | A snapshot byte-equality test is fragile to anti-forgery token nonces (a per-request random hidden field auto-injected into any non-GET `<form>` tag-helper) and to Razor `@if` whitespace. The file-diff is what the constraint *actually* means (no Phase-2 file edited) and is robust by construction. |
| 2026-06-04 | **Session lifetime = 8h** (user-decided). The publish token's `expiresIn` and the server-side session TTL both use it, surfaced as a config key (`Bff:SessionLifetime`, default `"8h"`). The "no Log-in link on anonymous pages" v1 row above was **user-confirmed** the same day. | The user weighed 1h / 8h / 24h and chose a working-day session; a config key lets a self-hosted operator tune it without a code change. Refresh-token plumbing stays deferred, so a session simply re-prompts for login at expiry. |
