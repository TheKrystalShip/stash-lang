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

- **No login, sessions, or CSRF.** Anonymous browse only. Phase 3 owns the authenticated maintainer surface.
- **No CORS.** The browser never calls the registry. The server-to-server boundary means a CORS misconfiguration on the registry side cannot expose data to a malicious page.
- **README sanitization.** All package-authored README content passes through a single `IReadmeRenderer` chokepoint (`Stash.Registry.Web/Rendering/`) that runs Markdig then HtmlSanitizer before the view touches it. This is the **sole** `@Html.Raw` call site in the project, and the `ReadmeChokepointMetaTests` scan enforces it.
- **Default HTML encoding everywhere else.** Every DTO field (description, keywords, license, version strings, publisher names, error messages) reaches the Razor view as a plain `@expression`, so Razor's default HTML encoding applies. The README is the one and only exception.

## Brand and CSS

The visual identity is shared with the rest of the Stash family:

- **Catppuccin palette.** The Catppuccin Mocha (dark) and Latte (light) CSS custom-property definitions are adapted from `Stash.Playground/wwwroot/css/app.css`. The website ships dark-by-default for Phase 2; a theme toggle is a later-phase addition.
- **stash-icon.svg.** The brand mark in the top-left of every page is `stash-icon.svg`, sourced from `.vscode/extensions/stash-lang/images/` and copied into `Stash.Registry.Web/wwwroot/`.
- The site is a visual sibling of the Playground and VS Code extension — not npm-red, not NuGet-blue.
