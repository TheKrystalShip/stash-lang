# Stash.Registry.Web

`Stash.Registry.Web` is the public browse-only web client for the Stash package registry. It is a server-rendered ASP.NET Core Razor Pages application that lets users discover, search, and read package documentation from any browser — without creating an account or installing anything.

The website is a **decoupled API consumer**: it calls the registry's public REST endpoints server-to-server via a typed `IRegistryClient`, and the browser only ever talks to the website's own origin. There is no CORS configuration on the registry side, and no registry API calls are made from browser JavaScript. This is intentional — anonymous browse, server-side rendering, and a clean trust boundary all follow from this choice.

## Architecture and Dependency Constraint

`Stash.Registry.Web` depends on **`Stash.Registry.Contracts` only**. It has no compile-time reference to `Stash.Registry` (the registry server project). This is enforced at two levels:

- **Structural** — the `Stash.Registry.Web.csproj` file declares only a `<ProjectReference>` to `Stash.Registry.Contracts`. A stray `<ProjectReference>` to `Stash.Registry` would break the build.
- **Test guard** — `WebProjectIsolationMetaTests` (in `Stash.Tests/Registry/Web/`) reflects over the loaded assembly and asserts (a) `Stash.Registry.Contracts` is referenced, (b) `Stash.Registry` is not. The test includes a fail-path fixture that trips the scan, proving the guard is not vacuous.

Downstream code in the website works with shared DTOs from `Stash.Registry.Contracts` (`PackageSummaryResponse`, `PackageDetailResponse`, `VersionDetailResponse`, `SearchQuery`, `PackageSortOrder`, etc.) and never touches the registry's EF context, storage layer, or DI container directly.

## Build

```bash
dotnet build Stash.sln
```

The project is part of `Stash.sln` and builds together with the rest of the Stash suite. There is no separate build step for the web project.

## Run

```bash
dotnet run --project Stash.Registry.Web/
```

By default the website listens on `http://localhost:5000` (or the first available port Kestrel selects). See the **Configuration** section below to point it at a running registry.

## Configuration

The website reads configuration from `appsettings.json` and `appsettings.Development.json` (the standard ASP.NET Core layering). Environment variables and `dotnet user-secrets` work too.

### Pointing at a registry

The only required setting for Phase 2 is the registry origin:

```json
{
  "Registry": {
    "BaseUrl": "http://localhost:5290"
  }
}
```

The default is `http://localhost:5290` (the default port for `dotnet run --project Stash.Registry/`). To point at a different instance, set `Registry__BaseUrl` as an environment variable or override it in `appsettings.Development.json`:

```bash
Registry__BaseUrl=https://registry.example.com dotnet run --project Stash.Registry.Web/
```

The website appends `/api/v1/...` to `BaseUrl` when constructing registry requests.

## Routes

The website exposes four user-visible pages:

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/` | Home — search hero + recently-updated packages rail |
| `GET` | `/search?q=&keyword=&license=&deprecated=&owner=&sort=&page=&pageSize=` | Search results with filters, sort, and pagination |
| `GET` | `/packages/@{scope}/{name}` | Package detail — sanitized README, metadata sidebar, versions table, dependencies, install command |
| `GET` | `/packages/@{scope}/{name}/v/{version}` | Version detail — version metadata, dependencies, integrity, deprecation status |

The literal `@` in the package and version routes is a UI affordance that matches the typical user spelling (`@scope/name`). The registry's own routes are `/packages/{scope}/{name}` (no `@`); the website strips the `@` before forwarding requests.

A `/health` page (not linked in the UI) calls `IRegistryClient.GetDiscoveryAsync` and returns the registry capability advertisement — useful as a smoke-test for the server-to-server connection.

## Security Notes

- **Anonymous browse is session-free.** The home, search, package detail, and version detail pages carry no authentication, no CSRF token, and set no cookies. The authenticated maintainer area (see below) is entirely separate; anonymous pages are byte-unchanged on disk from Phase 2.
- **No CORS.** The browser never calls the registry. The server-to-server boundary means a CORS misconfiguration on the registry side cannot expose data to a malicious page.
- **README sanitization.** All package-authored README content passes through a single `IReadmeRenderer` chokepoint (`Stash.Registry.Web/Rendering/`) that runs Markdig then HtmlSanitizer before the view touches it. This is the **sole** `@Html.Raw` call site in the project, and the `ReadmeChokepointMetaTests` scan enforces it.
- **Default HTML encoding everywhere else.** Every DTO field (description, keywords, license, version strings, publisher names, error messages) reaches the Razor view as a plain `@expression`, so Razor's default HTML encoding applies. The README is the one and only exception.

## Brand and CSS

The visual identity is shared with the rest of the Stash family:

- **Catppuccin palette.** The Catppuccin Mocha (dark) and Latte (light) CSS custom-property definitions are adapted from `Stash.Playground/wwwroot/css/app.css`. The website ships dark-by-default for Phase 2; a theme toggle is a later-phase addition.
- **stash-icon.svg.** The brand mark in the top-left of every page is `stash-icon.svg`, sourced from `.vscode/extensions/stash-lang/images/` and copied into `Stash.Registry.Web/wwwroot/`.
- The site is a visual sibling of the Playground and VS Code extension — not npm-red, not NuGet-blue.

## Authenticated Maintainer Area

In addition to the anonymous browse surface, the website provides an authenticated **maintainer area** for package owners. Authenticated pages are served from the `Areas/Maintainer/` Razor area and use a separate `_MaintainerLayout.cshtml`; the anonymous pages are completely unchanged.

### BFF authentication model

The website is a **backend-for-frontend (BFF)**: the browser never handles a JWT directly.

Login works in two steps:

1. The website posts the user's credentials to the registry's `POST /api/v1/auth/login`, which returns a short-lived read-ceiling JWT.
2. The website immediately uses that read JWT to eagerly mint a **publish-ceiling** session token via `POST /api/v1/auth/tokens { "ceiling": "publish", "expiresIn": "8h" }`. The read JWT is discarded after this call and never persisted.

The `BffSession` record (username, publish JWT, and token id) is stored **entirely server-side** in `ISessionStore`. The browser receives only an opaque session id in an HTTP-only, `SameSite=Strict`, `Secure` cookie (`stash_web_session`). The JWT string never reaches the browser. The registry required **zero changes** for this flow — a user-role caller holding a read token may mint a publish token by design.

### Session lifetime

The session lifetime defaults to **8 hours** and can be tuned via the `Bff:SessionLifetime` configuration key (a duration string, e.g. `"8h"`, `"24h"`). The publish token's `expiresIn` is set to the same value, so session expiry and token expiry are aligned. There is no refresh-token flow in v1; an expired session simply prompts for a fresh login.

```json
{
  "Bff": {
    "SessionLifetime": "8h"
  }
}
```

### Routes

| Method | Path | Description |
| --- | --- | --- |
| `GET`, `POST` | `/login` | Username/password login form. On success: mints the server-side session and sets the session cookie. On failure: re-renders the form with an error banner and sets no cookie. |
| `POST` | `/logout` | Revokes the publish token (best-effort), removes the server-side session, clears the cookie, and redirects to `/`. |
| `GET` | `/dashboard` | Authenticated user's owned packages, including private ones — the registry's visibility predicate includes private packages when a publish-ceiling token is threaded. |
| `GET`, `POST` | `/manage/@{scope}/{name}` | Maintainer view for a single package. Supports: deprecate package, un-deprecate package, deprecate a specific version, un-deprecate a specific version, and change package visibility. A 403 from the registry surfaces as an inline error; a 404 renders the site's 404 page. |
| `GET`, `POST` | `/settings/tokens` | API-token management: list active tokens, create a new token (read or publish ceiling; the token value is shown exactly once on the page immediately after creation, then never again), and revoke a token. The current-session token is displayed with a "current session" badge and has no revoke button — it can only be ended via `/logout`. |

### What is deliberately out of scope (v1)

The following features are not available in the authenticated maintainer area and have no planned route in this version:

- **Version yank / delete.** Deferred; `DELETE /packages/{scope}/{name}/{version}` is not wired.
- **Ownership and role management.** No UI for assigning or revoking collaborator roles (`/packages/.../roles`).
- **Admin tooling.** No user management, audit search, advisory controls, quarantine, or webhook configuration.
- **Self-service sign-up.** There is no `/register` route. Accounts are created via the CLI or by a registry operator.
- **Profile and account settings.** The "Settings" surface covers API tokens only; there is no password-change or profile-edit UI.
- **Download metrics, dependents, provenance, or verified labels.** These are Bucket-B features with no backing data in the registry at this time.

### Operational note — single-instance hosting

The default `InMemorySessionStore` is process-local: sessions are held in a `ConcurrentDictionary` in the web process. This means **multi-instance (load-balanced) hosting is not supported in v1** — a request routed to a different instance will not find the session and will redirect the user to login. To support multi-instance deployments, replace `InMemorySessionStore` with a distributed implementation (Redis, SQL Server, or Postgres) behind the `ISessionStore` interface. No other code changes are required.
