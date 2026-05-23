# BasePath Configuration — Apply as Middleware, Not Just a Banner String

> **Status:** Backlog
> **Created:** 2026-05-05
> **Priority:** High
> **Discovery context:** Stash package registry audit — finding **S4**.

## Background

`Stash.Registry` exposes a `BasePath` configuration setting (e.g., `"BasePath": "/registry"` in `appsettings.json`) intended to let self-hosters mount the registry behind a reverse proxy at a path prefix — e.g., `https://example.com/registry/...`.

Today the value is **read at startup, printed in the banner, and then ignored**. No middleware applies it. ASP.NET routes are registered at the root, so requests to `/registry/packages/foo` return 404. The user sees the banner say "BasePath: /registry" and reasonably concludes the server will respond on that prefix — silent breakage.

The fix is a one-liner in `Program.cs`/`Startup.cs`: call `app.UsePathBase(basePath)` before any routing middleware, when `BasePath` is set and non-empty.

## Scope

**Files:**
- `Stash.Registry/Program.cs` (and/or `Startup.cs` — confirm which the project uses; audit found both files exist).
- `Stash.Registry/Configuration/...` — wherever the `BasePath` config is bound.

**Changes:**

1. After `var app = builder.Build();` and before any route registration (`app.MapControllers()`, `app.MapEndpoints()`, etc.):
   ```csharp
   var basePath = builder.Configuration["BasePath"];
   if (!string.IsNullOrEmpty(basePath))
   {
       app.UsePathBase(basePath);
   }
   ```
2. Validate `BasePath` at startup: if set, must start with `/` and not end with `/`. Reject `""` (treat as unset), `"/"` (treat as unset), `"registry"` (no leading slash — fail with a clear error), and `"/registry/"` (trailing slash — fail with a clear error).
3. Update the startup banner to confirm the prefix is **applied**, not merely configured. Suggest: `Listening on http://0.0.0.0:5000{basePath}`.
4. Confirm any internally generated absolute URLs (e.g., `Location` headers on publish, links in JSON responses) honor the base path. Use `Request.PathBase` rather than hardcoded paths when constructing URLs server-side. Audit any controller that returns absolute URLs.

## Acceptance Criteria

- [ ] With `BasePath` unset, all routes work at root as today (zero regression).
- [ ] With `BasePath: "/registry"`, requests to `/registry/packages/foo` succeed and requests to `/packages/foo` return 404.
- [ ] Invalid `BasePath` values (`"registry"`, `"/registry/"`) cause a startup-time configuration error with a clear message.
- [ ] Banner output reflects the actual mount path.
- [ ] Server-emitted absolute URLs (Location headers, links) include the base path.
- [ ] xUnit integration test boots the registry with `BasePath: "/registry"` and confirms a `GET /registry/health` succeeds.

## Risk / Notes

- `UsePathBase` must be registered **before** routing/auth middleware. Order matters — verify by integration test.
- Reverse proxies typically strip the prefix before forwarding (so `app.UsePathBase` is what tells ASP.NET to expect it); document this in the README so self-hosters know to configure their proxy correctly.

## Out of Scope

- HTTPS termination, auth headers, or other reverse-proxy-specific concerns.
- Multi-tenant path-based routing.
