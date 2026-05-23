# Auth & Token UX Polish — Field Name, Refresh Errors, Registration Default

> **Status:** Backlog
> **Created:** 2026-05-05
> **Priority:** Medium
> **Discovery context:** Stash package registry audit — findings **A5**, **A6**, **A10**.

## Background

Three small auth/token UX fixes that share a theme — the auth subsystem is functionally correct but rough around the edges:

- **A5 — Inconsistent token field name.** The login response uses `token` as the JSON field name. The refresh response uses `accessToken`. The CLI reads both, so it works, but server consumers (third-party tools, scripts using `curl`) get confused and the docs have to enumerate both. Pick one.
- **A6 — `EnsureTokenFresh` swallows refresh failures silently.** When the access token is near expiry, the CLI calls `EnsureTokenFresh` to refresh it transparently. If the refresh fails (refresh token expired, registry unreachable, 5xx), the method swallows the exception and lets the original request proceed with the stale token. The request then fails with 401 and the user sees a confusing "not authenticated" error instead of "your refresh token expired, please log in again". Should print a warning to stderr.
- **A10 — `RegistrationEnabled` should default to `false`.** Currently the shipped `appsettings.json` has `RegistrationEnabled: true` (or omits the key, defaulting to true). Self-hosters spinning up a private registry for their team don't want random internet visitors registering accounts. Default-deny is the correct posture for self-hosted services. The public Stash-managed registry can override to `true` in its own deployment config.

## Scope

**Files:**
- **A5:** `Stash.Registry/Auth/AuthEndpoints.cs` (or `AuthController.cs`) — login response shape. `Stash.Registry/Contracts/AuthDtos.cs` — DTOs.
- **A5 (CLI side):** `Stash.Cli/Pkg/RegistryClient.cs` — token deserialization.
- **A6:** `Stash.Cli/Pkg/RegistryClient.cs` — `EnsureTokenFresh` method.
- **A10:** `Stash.Registry/appsettings.json`, `Stash.Registry/Auth/RegistrationEndpoint.cs`, `Stash.Registry/README.md`.

**Changes:**

### A5 — Standardize on `accessToken`

Rationale: `accessToken` distinguishes from `refreshToken` in the same response (refresh endpoint already returns both). `token` is ambiguous when refresh tokens enter the picture. Match OAuth 2.0 / RFC 6749 wording.

1. Login response: rename `token` → `accessToken` in the DTO. Include `refreshToken` (if not already) and `expiresIn` (seconds).
2. Refresh response: already uses `accessToken`. Confirm consistency.
3. CLI: read `accessToken` only. Drop the dual-read fallback.
4. **Breaking change** for any third-party tooling that reads the login response by hand. Document in release notes. Consider keeping `token` as an alias in the response for one release with a deprecation comment, then removing.

### A6 — Surface refresh failures

1. In `EnsureTokenFresh`, when the refresh call fails:
   - Catch the specific exception types (HTTP 401 from refresh endpoint = refresh token expired; HTTP 5xx / network = transient).
   - For "refresh token expired": print to stderr `warning: token refresh failed (refresh token expired). Run 'stash pkg login' to re-authenticate.` and then proceed (the next API call will 401 with a clear context).
   - For transient (5xx, network): print `warning: could not refresh token (registry unreachable). Continuing with existing token.` and proceed.
   - Do not crash the calling command. Refresh is best-effort; the caller deals with the eventual 401 if it happens.
2. If refresh is the entire purpose of the command (rare), the warning is sufficient — no special-casing needed.

### A10 — Registration default false

1. `appsettings.json`: set `Auth:RegistrationEnabled: false`.
2. `appsettings.Development.json`: set to `true` to keep dev convenient.
3. README: document the default and explain the rationale (self-host private; flip to true if you want public).
4. The registration endpoint must continue to respect the config — confirm it returns `403 Forbidden` (not `404`) when disabled, with body `{ "error": "registration_disabled", "message": "User registration is disabled on this registry." }`.

## Acceptance Criteria

- [ ] Login response uses `accessToken` (and optionally `refreshToken`, `expiresIn`).
- [ ] CLI reads only `accessToken` from login responses.
- [ ] Failed refresh prints a warning naming the cause; calling command continues.
- [ ] `appsettings.json` ships with `RegistrationEnabled: false`.
- [ ] Hitting the registration endpoint with `RegistrationEnabled: false` returns 403 with structured body.
- [ ] README documents the default and how to enable.
- [ ] xUnit tests cover all three changes.

## Risk / Notes

- A5 is technically a breaking change to the public API. If the registry has any third-party CLI clients in the wild (audit confirms there are none today), keep `token` as a duplicate field for one release with a deprecation note in the release notes. Otherwise drop it cleanly.
- A10 changes default behavior on upgrade — existing self-hosters who rely on the `true` default and have not edited their `appsettings.json` will find registration broken after upgrade. Call this out clearly in the release notes.

## Out of Scope

- Token revocation / sign-out flow.
- Refresh-token rotation policy.
- OAuth provider integration.
