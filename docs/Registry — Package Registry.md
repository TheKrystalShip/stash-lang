# Registry - Package Registry

> **Status:** Stable v1 registry reference
> **Audience:** Registry operators, registry implementers, authors of alternate `stash pkg` clients, and contributors changing registry behavior.
> **Purpose:** Defines the REST surface, authentication model, identity and ownership model, visibility rules, configuration, storage layout, and operational contract of the Stash package registry server.
>
> **Companion documents:**
>
> - [PKG - Package Manager CLI](PKG%20—%20Package%20Manager%20CLI.md) - the `stash pkg` client that consumes this API.
> - [Language Specification](Stash%20—%20Language%20Specification.md) - import semantics and manifest grammar.
> - [Standard Library Reference](Stash%20—%20Standard%20Library%20Reference.md) - the `pkg` namespace and runtime APIs.

The Stash Package Registry is the server side of `stash pkg`. It is a self-hosted HTTP service that stores published package tarballs, exposes a versioned REST API, and authenticates clients with short-lived JWT access tokens and rotating refresh tokens. The default configuration (SQLite + filesystem storage + local password auth) requires no external dependencies; production deployments can swap in PostgreSQL, S3, and externally provisioned signing keys without code changes.

This document defines the public contract of the registry. It intentionally omits implementation history, roadmap material, and engineering rationale except where a current limitation affects client behavior.

## 1. Roles

A registry deployment has three participants:

| Participant     | Responsibility                                                                                      |
| --------------- | --------------------------------------------------------------------------------------------------- |
| Registry server | The ASP.NET Core process that serves the REST API, manages packages and accounts, and signs tokens. |
| Registry client | Any tool that calls the REST API. The canonical client is `stash pkg`.                              |
| Storage backend | The filesystem directory or S3 bucket that stores package tarballs. Always paired with a database.  |

## 2. Transport

| Property     | Value                                                                  |
| ------------ | ---------------------------------------------------------------------- |
| Protocol     | HTTP/1.1 or HTTP/2                                                     |
| Encoding     | JSON for requests and responses; `application/gzip` for tarball bodies |
| Auth scheme  | `Authorization: Bearer <jwt>`                                          |
| Path prefix  | `/api/v1` (configurable via `Server.BasePath`)                         |
| TLS          | Optional; configured by `Server.Tls` (PEM cert + key)                  |
| Health probe | `GET /` (no auth)                                                      |

All timestamps are ISO 8601 UTC. All sizes returned by the API are in bytes unless explicitly suffixed. Error responses are JSON objects with at least an `error` (machine code) or `message` (human description) field; both may be present.

## 3. Endpoint Summary

### Package Endpoints

All package routes use the two-segment form `/packages/{scope}/{name}` where `{scope}` is the bare scope name without the leading `@`. The `@` is never part of the URL path; the server canonicalizes back to `@{scope}/{name}` in response bodies and DB lookups.

| Method | Path                                                   | Auth                  | Description                              |
| ------ | ------------------------------------------------------ | --------------------- | ---------------------------------------- |
| GET    | `/api/v1/packages/{scope}/{name}`                      | None (public packages) | Package metadata and version list        |
| GET    | `/api/v1/packages/{scope}/{name}/{version}`            | None (public packages) | Version details                          |
| GET    | `/api/v1/packages/{scope}/{name}/{version}/download`   | None (public packages) | Download tarball                         |
| PUT    | `/api/v1/packages/{scope}/{name}`                      | publish scope         | Publish a version                        |
| DELETE | `/api/v1/packages/{scope}/{name}/{version}`            | publish scope         | Unpublish (within window)                |
| PATCH  | `/api/v1/packages/{scope}/{name}/deprecate`            | publish scope         | Deprecate package                        |
| DELETE | `/api/v1/packages/{scope}/{name}/deprecate`            | publish scope         | Undeprecate package                      |
| PATCH  | `/api/v1/packages/{scope}/{name}/{version}/deprecate`  | publish scope         | Deprecate version                        |
| DELETE | `/api/v1/packages/{scope}/{name}/{version}/deprecate`  | publish scope         | Undeprecate version                      |
| GET    | `/api/v1/packages/{scope}/{name}/roles`                | admin                 | List package roles                       |
| PUT    | `/api/v1/packages/{scope}/{name}/roles`                | publish scope (owner) | Assign a role to a principal             |
| PATCH  | `/api/v1/packages/{scope}/{name}/visibility`           | publish scope (owner) | Change package visibility                |

Read endpoints (`GET`) for private or internal packages require a `read`-scoped (or higher) token AND the caller must have at least `reader` permission. Unauthorized callers receive `404 Not Found`, not `403`, to avoid leaking package existence.

### Auth Endpoints

| Method | Path                                          | Auth          | Description                          |
| ------ | --------------------------------------------- | ------------- | ------------------------------------ |
| GET    | `/`                                           | None          | Health check                         |
| POST   | `/api/v1/auth/login`                          | None          | Authenticate, receive token pair     |
| POST   | `/api/v1/auth/register`                       | None          | Create account (when enabled)        |
| POST   | `/api/v1/auth/tokens/refresh`                 | None          | Exchange refresh + access token pair |
| GET    | `/api/v1/auth/whoami`                         | Bearer        | Current user info                    |
| POST   | `/api/v1/auth/tokens`                         | Bearer        | Create scoped API token              |
| GET    | `/api/v1/auth/tokens`                         | Bearer        | List API tokens                      |
| DELETE | `/api/v1/auth/tokens/{id}`                    | Bearer        | Revoke token                         |

### Search Endpoint

| Method | Path               | Auth | Description       |
| ------ | ------------------ | ---- | ----------------- |
| GET    | `/api/v1/search`   | None | Search packages   |

Search results are filtered by visibility. Unauthenticated callers see only `public` packages. Authenticated callers additionally see `private` and `internal` packages they have permission to read.

### Organization and Scope Endpoints

| Method | Path                                             | Auth                  | Description                              |
| ------ | ------------------------------------------------ | --------------------- | ---------------------------------------- |
| POST   | `/api/v1/orgs`                                   | publish scope         | Create an organization                   |
| GET    | `/api/v1/orgs/{org}`                             | None                  | Organization metadata                    |
| POST   | `/api/v1/orgs/{org}/members`                     | publish scope (owner) | Add a member to the org                  |
| DELETE | `/api/v1/orgs/{org}/members/{username}`          | publish scope (owner) | Remove a member from the org             |
| POST   | `/api/v1/orgs/{org}/teams`                       | publish scope (owner) | Create a team within the org             |
| POST   | `/api/v1/orgs/{org}/teams/{team}/members`        | publish scope (owner) | Add a member to a team                   |
| GET    | `/api/v1/scopes/{scope}`                         | None                  | Resolve a scope to its owner             |
| POST   | `/api/v1/scopes`                                 | publish scope         | Claim a new scope                        |

### Admin Endpoints

| Method | Path                                           | Auth  | Description                    |
| ------ | ---------------------------------------------- | ----- | ------------------------------ |
| GET    | `/api/v1/admin/stats`                          | admin | Registry statistics            |
| POST   | `/api/v1/admin/users`                          | admin | Create user                    |
| DELETE | `/api/v1/admin/users/{username}`               | admin | Delete user                    |
| PUT    | `/api/v1/admin/packages/{scope}/{name}/roles`  | admin | Assign package role (admin override) |
| GET    | `/api/v1/admin/audit-log`                      | admin | Query audit log                |

## 4. Authentication

### 4.1 Token Model

The registry issues JWTs signed with HMAC-SHA256. Every protected endpoint validates the `Authorization: Bearer <token>` header, including a database lookup of the `jti` (JWT ID) claim on every request to support immediate revocation.

| Claim        | Description                                           |
| ------------ | ----------------------------------------------------- |
| `sub`        | Username.                                             |
| `jti`        | Unique token ID. Removed from the database to revoke. |
| `token_scope` | `read`, `publish`, or `admin`.                       |
| `role`       | `user` or `admin`.                                    |
| `exp`        | Expiry timestamp.                                     |
| `machine_id` | Machine fingerprint (only present for login tokens).  |

The registry uses three token kinds:

| Kind          | Issued by                                | Default lifetime | Purpose                                               |
| ------------- | ---------------------------------------- | ---------------- | ----------------------------------------------------- |
| Access token  | `POST /auth/login`, refresh              | `1h`             | Authenticated API calls                               |
| Refresh token | `POST /auth/login` (with `X-Machine-Id`) | `90d`            | Rotating renewal of access tokens                     |
| API token     | `POST /auth/tokens`                      | `90d`            | Long-lived tokens for CI/automation, named and scoped |

### 4.2 Scopes and Policies

| Scope     | Grants                                                                           |
| --------- | -------------------------------------------------------------------------------- |
| `read`    | Read-gated endpoints on private/internal packages (in addition to publish, admin). |
| `publish` | Publish, unpublish, deprecate / undeprecate, org management, scope claims.       |
| `admin`   | All endpoints, including `/api/v1/admin/*`.                                      |

| Policy                | Required claims                                |
| --------------------- | ---------------------------------------------- |
| `RequireReadScope`    | `token_scope ∈ { read, publish, admin }`       |
| `RequirePublishScope` | `token_scope ∈ { publish, admin }`             |
| `RequireAdminScope`   | `token_scope == admin`                         |
| `RequireAdmin`        | `token_scope == admin` AND `role == admin`     |

### 4.3 Refresh Token Rotation

Refresh tokens implement OAuth2-style rotation. Clients that supply `X-Machine-Id` on login receive a refresh-token pair; subsequent calls to `POST /auth/tokens/refresh` exchange a (refresh token, expired access token, machine ID) tuple for a new pair.

Each refresh consumes the prior refresh token. All refresh tokens issued from the same login share a `FamilyId`. If a consumed refresh token is presented again, the registry treats it as theft, revokes every token in the family, and writes a `token_theft_detected` audit entry.

Refresh tokens are bound to the machine fingerprint, which is a SHA-256 hash of `hostname:username:platform` supplied by the client. Requests from a different fingerprint are rejected.

### 4.4 Revocation

Token revocation is database-driven. Every JWT carries a `jti` that matches the primary key of the `tokens` (or `refresh_tokens`) row. Deleting the row revokes the token at the next request, because the JWT bearer middleware performs a `jti` lookup during `OnTokenValidated`. There is no separate revocation list.

### 4.5 Auth Provider Backends

| Provider | Status        | Description                                                                          |
| -------- | ------------- | ------------------------------------------------------------------------------------ |
| `local`  | Supported     | Built-in password authentication. Passwords hashed with Argon2id (OWASP parameters). |
| `ldap`   | Not supported | LDAP / Active Directory integration. Stub - configuration is accepted but unused.    |
| `oidc`   | Not supported | OpenID Connect delegation. Stub - configuration is accepted but unused.              |

All providers implement `IAuthProvider`:

```csharp
public interface IAuthProvider
{
    bool Authenticate(string username, string password);
    void CreateUser(string username, string password);
    bool UserExists(string username);
}
```

User self-registration via `POST /auth/register` is controlled by `Auth.RegistrationEnabled`. When disabled, only admins can create accounts via `POST /admin/users`.

When `Auth.RegistrationEnabled` is true, every new user automatically gets a personal scope `@<username>` provisioned in the same transaction. Registration is rejected with `409` if the username collides with an existing scope, org name, or reserved system scope (`@stash`, `@admin`).

## 5. Auth Endpoints

### 5.1 POST /api/v1/auth/login

Authenticate with username and password. Returns an access token, and a refresh token when `X-Machine-Id` is supplied.

Request:

```json
{
  "username": "alice",
  "password": "s3cr3tpassword"
}
```

| Header         | Required | Description                                                                    |
| -------------- | -------- | ------------------------------------------------------------------------------ |
| `X-Machine-Id` | No       | SHA-256 machine fingerprint. When supplied, a refresh token is issued as well. |

Response `200 OK`:

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresAt": "2026-06-20T13:00:00Z",
  "refreshToken": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4...",
  "refreshTokenExpiresAt": "2026-09-18T12:00:00Z"
}
```

When `X-Machine-Id` is omitted, `refreshToken` and `refreshTokenExpiresAt` are `null`. Access-token lifetime is `Auth.AccessTokenExpiry` (default `1h`); refresh-token lifetime is `Auth.RefreshTokenExpiry` (default `90d`).

| Status | Meaning                       |
| ------ | ----------------------------- |
| 400    | Missing username or password. |
| 401    | Invalid credentials.          |
| 429    | Rate limit exceeded.          |

### 5.2 POST /api/v1/auth/register

Create a new user account. Available only when `Auth.RegistrationEnabled` is `true`. The personal scope `@<username>` is auto-provisioned in the same transaction.

Request:

```json
{ "username": "alice", "password": "s3cr3tpassword" }
```

Response `201 Created`:

```json
{ "ok": true, "username": "alice" }
```

| Status | Meaning                                                                            |
| ------ | ---------------------------------------------------------------------------------- |
| 400    | Validation failed (username format, password too short).                           |
| 403    | Registration is disabled.                                                          |
| 409    | Username already taken, or username collides with an existing scope or org name.   |
| 429    | Rate limit exceeded.                                                               |

### 5.3 POST /api/v1/auth/tokens/refresh

Exchange a refresh token and its paired (possibly expired) access token for a new pair. Implements OAuth2 token rotation.

Request:

```json
{
  "refreshToken": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4...",
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "machineId": "a1b2c3d4e5f6..."
}
```

Response `200 OK`:

```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "bmV3IHJlZnJlc2ggdG9rZW4...",
  "expiresAt": "2026-06-20T14:00:00Z",
  "refreshTokenExpiresAt": "2026-09-18T13:00:00Z"
}
```

| Status | Meaning                                                                                                                                                      |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 400    | Missing required fields.                                                                                                                                     |
| 401    | Invalid signature, token pair mismatch, machine fingerprint mismatch, expired refresh token, or consumed refresh token (the entire token family is revoked). |
| 429    | Rate limit exceeded.                                                                                                                                         |

### 5.4 GET /api/v1/auth/whoami

Returns the authenticated user's username and role.

```json
{ "username": "alice", "role": "user" }
```

### 5.5 POST /api/v1/auth/tokens

Create a named, scoped API token. Scope must be `"read"`, `"publish"`, or `"admin"`; only an `admin` role may create `admin` tokens.

Request:

```json
{
  "scope": "publish",
  "description": "CI deploy token",
  "expiresIn": "30d"
}
```

`expiresIn` accepts duration strings (`Xd`, `Xh`, `Xm`). Minimum `1h`, maximum `365d`. When omitted, `Auth.ApiTokenExpiry` is used (default `90d`).

Response `201 Created`:

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "tokenId": "550e8400-e29b-41d4-a716-446655440000",
  "scope": "publish",
  "expiresAt": "2026-04-25T12:00:00Z",
  "description": "CI deploy token"
}
```

The `token` value is returned only at creation time and cannot be retrieved again. The `tokenId` can be used to revoke the token later.

### 5.6 GET /api/v1/auth/tokens

List the authenticated user's API tokens. Token values are never returned.

```json
{
  "tokens": [
    {
      "tokenId": "550e8400-e29b-41d4-a716-446655440000",
      "scope": "publish",
      "description": "CI deploy token",
      "createdAt": "2026-03-26T12:00:00Z",
      "expiresAt": "2026-04-25T12:00:00Z"
    }
  ]
}
```

### 5.7 DELETE /api/v1/auth/tokens/{id}

Revoke a token. The token row is deleted; revocation takes effect on the next request.

```json
{ "ok": true }
```

| Status | Meaning                                    |
| ------ | ------------------------------------------ |
| 403    | Token belongs to another user (non-admin). |
| 404    | Token not found.                           |

## 6. Package Endpoints

All package routes use the two-segment form `/packages/{scope}/{name}`. The `@` is not part of the URL; the server reconstructs `@{scope}/{name}` for response bodies. For example, package `@stash/http` is addressed as `/packages/stash/http`.

### 6.1 GET /api/v1/packages/{scope}/{name}

Returns package metadata and the full version dictionary. `versions` is keyed by version string, not an array.

For `public` packages no authentication is required. For `private` or `internal` packages the caller must supply a `read`-scoped (or higher) JWT and must have at least `reader` permission on the package. Unauthorized callers receive `404 Not Found` to avoid leaking package existence.

```json
{
  "name": "@stash/http",
  "description": "HTTP client utilities for Stash",
  "visibility": "public",
  "license": "MIT",
  "repository": "https://github.com/example/stash-http",
  "keywords": ["http", "client"],
  "readme": "# stash-http\n\nHTTP client utilities...",
  "versions": {
    "1.2.0": {
      "version": "1.2.0",
      "stashVersion": ">=1.0.0",
      "dependencies": {},
      "integrity": "sha256-abc123...",
      "publishedAt": "2026-03-10T14:00:00.000Z",
      "publishedBy": "alice",
      "deprecated": false,
      "deprecationMessage": null
    }
  },
  "deprecated": false,
  "deprecationMessage": null,
  "deprecationAlternative": null,
  "latest": "1.2.0",
  "createdAt": "2026-02-01T10:00:00.000Z",
  "updatedAt": "2026-03-10T14:00:00.000Z"
}
```

| Status | Meaning                                                           |
| ------ | ----------------------------------------------------------------- |
| 404    | Package not found, or caller is unauthorized for a non-public package. |

### 6.2 GET /api/v1/packages/{scope}/{name}/{version}

Returns a single version entry, identical in shape to the elements of the `versions` dictionary above. Visibility rules identical to [Section 6.1](#61-get-apiv1packagesscopename).

| Status | Meaning                       |
| ------ | ----------------------------- |
| 404    | Package or version not found, or unauthorized. |

### 6.3 GET /api/v1/packages/{scope}/{name}/{version}/download

Returns the raw `.tar.gz` tarball. Visibility rules identical to [Section 6.1](#61-get-apiv1packagesscopename).

| Property                | Value                                          |
| ----------------------- | ---------------------------------------------- |
| Response `Content-Type` | `application/gzip`                             |
| Response `X-Integrity`  | `sha256-<base64>` integrity hash of the bytes. |

| Status | Meaning                       |
| ------ | ----------------------------- |
| 404    | Package or version not found, or unauthorized. |

### 6.4 PUT /api/v1/packages/{scope}/{name}

Publish a new version. The request body is the raw tarball. Requires a token with `publish` or `admin` scope. The caller must be an owner of an existing package, or the first publisher of a new package name (in which case they become the `owner`).

The scope owner (the user or org backing the scope) is automatically assigned the `owner` role on package creation.

Headers:

```
Content-Type: application/gzip
X-Version: 1.2.0
X-Integrity: sha256-abc123...    (optional - verified if present)
Authorization: Bearer <token>
```

The tarball must contain a `stash.json` manifest and at least one `.stash` file. The version is read from the manifest. Versions are immutable; republishing an existing version returns `409`.

Response `201 Created`:

```json
{
  "ok": true,
  "package": "@stash/http",
  "version": "1.2.0",
  "integrity": "sha256-abc123..."
}
```

| Status | Meaning                                                                          |
| ------ | -------------------------------------------------------------------------------- |
| 400    | Missing manifest, no `.stash` files, invalid package name, or `X-Integrity` mismatch. |
| 403    | Not a package owner.                                                             |
| 409    | Version already exists. Body: `{ "error": "version_exists", "message": "..." }`. |
| 413    | Tarball exceeds `Security.MaxPackageSize`.                                       |
| 429    | Rate limit exceeded.                                                             |

### 6.5 DELETE /api/v1/packages/{scope}/{name}/{version}

Unpublish a version. The caller must have at least `maintainer` permission, and the version must be within the unpublish window (`Security.UnpublishWindow`, default `72h`). After the window expires, the only path is deprecation.

Removing the last version of a package does **not** delete the package row — the name remains reserved and the package shows up in metadata responses with an empty `versions` map.

| Status | Meaning                                         |
| ------ | ----------------------------------------------- |
| 400    | Unpublish window expired, or version not found. |
| 403    | Insufficient permission.                        |

### 6.6 Deprecation

Deprecation marks a package or version as discouraged without preventing installation. Existing dependents continue to resolve normally; clients are expected to surface the deprecation message to the user.

Requires `publish` scope and at least `maintainer` permission (or `admin` role).

| Method | Path                                                   | Body                        | Effect                     |
| ------ | ------------------------------------------------------ | --------------------------- | -------------------------- |
| PATCH  | `/api/v1/packages/{scope}/{name}/deprecate`            | `{ message, alternative? }` | Deprecate the package.     |
| DELETE | `/api/v1/packages/{scope}/{name}/deprecate`            | -                           | Clear package deprecation. |
| PATCH  | `/api/v1/packages/{scope}/{name}/{version}/deprecate`  | `{ message }`               | Deprecate the version.     |
| DELETE | `/api/v1/packages/{scope}/{name}/{version}/deprecate`  | -                           | Clear version deprecation. |

| Body field    | Type   | Required | Description                         |
| ------------- | ------ | -------- | ----------------------------------- |
| `message`     | string | yes      | Non-empty deprecation reason.       |
| `alternative` | string | no       | Suggested replacement package name. |

Response (all four):

```json
{
  "ok": true,
  "package": "@stash/http",
  "version": "1.1.0",
  "deprecated": true
}
```

`version` is `null` when the package itself is the deprecation target.

### 6.7 GET /api/v1/packages/{scope}/{name}/roles

List role assignments for a package. Requires `admin` role.

```json
{
  "package": "@stash/http",
  "roles": [
    { "principalType": "user", "principalId": "alice", "role": "owner" },
    { "principalType": "team", "principalId": "550e8400-...", "role": "maintainer" }
  ]
}
```

### 6.8 PUT /api/v1/packages/{scope}/{name}/roles

Assign a role to a principal on a package. Requires `publish` scope and `owner` permission on the package (or `admin` role via the admin endpoint).

Request:

```json
{
  "principal_type": "user",
  "principal_id": "bob",
  "role": "maintainer"
}
```

`principal_type` must be one of `user`, `team`, `org`. `role` must be one of `owner`, `maintainer`, `publisher`, `reader`.

| Status | Meaning                                       |
| ------ | --------------------------------------------- |
| 400    | Invalid `principal_type` or `role` value.     |
| 403    | Caller is not an owner of the package.        |
| 404    | Package not found.                            |

### 6.9 PATCH /api/v1/packages/{scope}/{name}/visibility

Change the visibility of a package. Requires `publish` scope and `owner` permission.

Request:

```json
{ "visibility": "private" }
```

`visibility` must be one of `public`, `private`, or `internal`. See [Section 11.1](#111-packages) for semantics of each value.

| Status | Meaning                                        |
| ------ | ---------------------------------------------- |
| 400    | Invalid visibility value.                      |
| 403    | Caller is not an owner of the package.         |
| 404    | Package not found.                             |

## 7. Search

### 7.1 GET /api/v1/search

Search package names and descriptions.

| Query parameter | Type    | Default | Description                 |
| --------------- | ------- | ------- | --------------------------- |
| `q`             | string  | -       | Search query (required).    |
| `page`          | integer | `1`     | Page number (1-based).      |
| `pageSize`      | integer | `20`    | Results per page (max 100). |

Results are filtered by visibility:
- Unauthenticated callers: only `public` packages.
- Authenticated callers: `public` packages, plus `private` and `internal` packages the caller has at least `reader` permission on.

```json
{
  "packages": [
    {
      "name": "@stash/http",
      "description": "HTTP client utilities for Stash",
      "latest": "1.2.0",
      "keywords": ["http", "client"],
      "updatedAt": "2026-03-10T14:00:00.000Z",
      "deprecated": false
    }
  ],
  "totalCount": 3,
  "page": 1,
  "pageSize": 20,
  "totalPages": 1
}
```

## 8. Organization and Scope Endpoints

### 8.1 POST /api/v1/orgs

Create an organization. The caller must have a `publish`-scoped (or higher) token. The creator becomes `owner` of the org. A scope with the same name as the org is provisioned automatically; registration fails with `409` if the org name collides with an existing scope, username, or org name.

Request:

```json
{ "name": "my-org", "display_name": "My Organization" }
```

Response `201 Created`:

```json
{ "ok": true, "name": "my-org" }
```

| Status | Meaning                                                         |
| ------ | --------------------------------------------------------------- |
| 400    | Invalid org name format.                                        |
| 409    | Name already taken (clashes with a username, scope, or org).    |

### 8.2 GET /api/v1/orgs/{org}

Returns public organization metadata. No authentication required.

```json
{
  "name": "my-org",
  "displayName": "My Organization",
  "createdAt": "2026-03-01T10:00:00Z"
}
```

### 8.3 POST /api/v1/orgs/{org}/members

Add a member to the organization. Requires `publish` scope and `owner` org role.

Request:

```json
{ "username": "bob", "org_role": "member" }
```

`org_role` must be `owner` or `member`.

### 8.4 DELETE /api/v1/orgs/{org}/members/{username}

Remove a member from the organization. Requires `publish` scope and `owner` org role.

### 8.5 POST /api/v1/orgs/{org}/teams

Create a team within the organization. Requires `publish` scope and `owner` org role.

Request:

```json
{ "name": "backend" }
```

### 8.6 POST /api/v1/orgs/{org}/teams/{team}/members

Add a member to a team. Requires `publish` scope and `owner` org role.

Request:

```json
{ "username": "carol" }
```

### 8.7 GET /api/v1/scopes/{scope}

Resolve a scope to its owner. No authentication required.

Response:

```json
{
  "scope": "stash",
  "owner_type": "system",
  "owner": null
}
```

`owner_type` is one of `system`, `user`, or `org`. `owner` is the username for `user` scopes, the org ID for `org` scopes, or `null` for `system` scopes.

### 8.8 POST /api/v1/scopes

Claim a new scope. Requires a `publish`-scoped (or higher) token. The `owner_type` must match a resource the caller controls: `user` (the caller's own username) or `org` (an org the caller owns).

Request:

```json
{ "scope": "my-tools", "owner_type": "user", "owner": "alice" }
```

| Status | Meaning                                                                             |
| ------ | ----------------------------------------------------------------------------------- |
| 400    | Invalid scope name format, or `owner_type`/`owner` combination is invalid.         |
| 403    | Caller does not control the requested owner.                                        |
| 409    | Scope name collides with a username, org name, existing scope, or reserved system scope. |

## 9. Admin Endpoints

All admin endpoints require `RequireAdmin` (scope `admin` AND role `admin`).

### 9.1 GET /api/v1/admin/stats

```json
{ "users": 15 }
```

### 9.2 POST /api/v1/admin/users

Create a user, bypassing `Auth.RegistrationEnabled`.

Request:

```json
{ "username": "carol", "password": "initialpassword", "role": "user" }
```

Response `201 Created`:

```json
{ "ok": true, "username": "carol", "role": "user" }
```

### 9.3 DELETE /api/v1/admin/users/{username}

Delete a user. Their tokens are cascade-deleted from the `tokens` and `refresh_tokens` tables.

```json
{ "ok": true }
```

### 9.4 PUT /api/v1/admin/packages/{scope}/{name}/roles

Assign a role to a principal on a package (admin override). Body is identical to [Section 6.8](#68-put-apiv1packagesscopenameroles).

### 9.5 GET /api/v1/admin/audit-log

Query the audit log. Entries are returned in descending chronological order.

| Query parameter | Type    | Description                               |
| --------------- | ------- | ----------------------------------------- |
| `package`       | string  | Filter by package name.                   |
| `action`        | string  | Filter by action type (see [Section 13]). |
| `page`          | integer | Page number (1-based).                    |
| `pageSize`      | integer | Results per page (default 50, max 200).   |

```json
{
  "entries": [
    {
      "id": 42,
      "action": "publish",
      "package": "@stash/http",
      "version": "1.2.0",
      "user": "alice",
      "target": null,
      "ip": "203.0.113.5",
      "timestamp": "2026-03-10T14:00:00Z"
    }
  ],
  "totalCount": 5,
  "page": 1,
  "pageSize": 50,
  "totalPages": 1
}
```

[Section 13]: #13-audit-log

## 10. Configuration

The registry reads configuration from `appsettings.json` in the working directory at startup. Every field has a default; the server starts with zero configuration for local development. Environment-variable overrides follow standard ASP.NET Core configuration binding rules.

### 10.1 Defaults

```json
{
  "Server": {
    "Host": "0.0.0.0",
    "Port": 8080,
    "BasePath": "/api/v1",
    "Tls": { "Enabled": false, "Cert": "", "Key": "" }
  },
  "Storage": {
    "Type": "filesystem",
    "Path": "data/packages",
    "Bucket": "",
    "Region": "",
    "Endpoint": "",
    "AccessKey": "",
    "SecretKey": ""
  },
  "Database": {
    "Type": "sqlite",
    "Path": "data/registry.db",
    "ConnectionString": ""
  },
  "Auth": {
    "Type": "local",
    "RegistrationEnabled": true,
    "ApiTokenExpiry": "90d",
    "AccessTokenExpiry": "1h",
    "RefreshTokenExpiry": "90d"
  },
  "Security": {
    "MaxPackageSize": "10MB",
    "RequiredIntegrity": "sha256",
    "UnpublishWindow": "72h",
    "JwtSigningKey": null
  },
  "RateLimiting": {
    "Enabled": true,
    "Auth": {
      "MaxAttempts": 10,
      "WindowSeconds": 300,
      "MaxPerHour": 60,
      "MaxPerMinute": 10
    },
    "Publish": {
      "MaxAttempts": 5,
      "WindowSeconds": 300,
      "MaxPerHour": 30,
      "MaxPerMinute": 5
    },
    "Download": {
      "MaxAttempts": 100,
      "WindowSeconds": 300,
      "MaxPerHour": 1000,
      "MaxPerMinute": 120
    },
    "Search": {
      "MaxAttempts": 50,
      "WindowSeconds": 300,
      "MaxPerHour": 500,
      "MaxPerMinute": 60
    }
  }
}
```

### 10.2 Server

| Property      | Type    | Default     | Description                                               |
| ------------- | ------- | ----------- | --------------------------------------------------------- |
| `Host`        | string  | `"0.0.0.0"` | Bind address. Use `"127.0.0.1"` to restrict to localhost. |
| `Port`        | integer | `8080`      | TCP port.                                                 |
| `BasePath`    | string  | `"/api/v1"` | URL prefix for all API routes.                            |
| `Tls.Enabled` | bool    | `false`     | Enable TLS termination.                                   |
| `Tls.Cert`    | string  | `""`        | Path to PEM certificate.                                  |
| `Tls.Key`     | string  | `""`        | Path to PEM private key.                                  |

### 10.3 Storage

| Property    | Type   | Default           | Description                                                                 |
| ----------- | ------ | ----------------- | --------------------------------------------------------------------------- |
| `Type`      | string | `"filesystem"`    | Storage backend: `"filesystem"` or `"s3"`. S3 is currently not implemented. |
| `Path`      | string | `"data/packages"` | Root directory for filesystem storage.                                      |
| `Bucket`    | string | `""`              | S3 bucket name.                                                             |
| `Region`    | string | `""`              | AWS region.                                                                 |
| `Endpoint`  | string | `""`              | Custom S3 endpoint URL (e.g., MinIO).                                       |
| `AccessKey` | string | `""`              | AWS access key ID.                                                          |
| `SecretKey` | string | `""`              | AWS secret access key.                                                      |

### 10.4 Database

| Property           | Type   | Default              | Description                                                           |
| ------------------ | ------ | -------------------- | --------------------------------------------------------------------- |
| `Type`             | string | `"sqlite"`           | `"sqlite"` or `"postgresql"`.                                         |
| `Path`             | string | `"data/registry.db"` | SQLite file path. The `data/` directory is created automatically.     |
| `ConnectionString` | string | `""`                 | PostgreSQL connection string. Required when `Type` is `"postgresql"`. |

Example PostgreSQL connection string:

```
Host=localhost;Port=5432;Database=stash_registry;Username=stash;Password=secret
```

### 10.5 Auth

| Property              | Type   | Default   | Description                                                     |
| --------------------- | ------ | --------- | --------------------------------------------------------------- |
| `Type`                | string | `"local"` | `"local"`, `"ldap"`, or `"oidc"`. Only `"local"` is functional. |
| `RegistrationEnabled` | bool   | `true`    | Allow unauthenticated users to call `POST /auth/register`.      |
| `ApiTokenExpiry`      | string | `"90d"`   | Default lifetime of API tokens created via `POST /auth/tokens`. |
| `AccessTokenExpiry`   | string | `"1h"`    | Lifetime of access tokens issued at login or refresh.           |
| `RefreshTokenExpiry`  | string | `"90d"`   | Lifetime of refresh tokens issued at login.                     |
| `LdapServer`          | string | `""`      | LDAP URI. Used when `Type == "ldap"` (currently a stub).        |
| `LdapBaseDn`          | string | `""`      | LDAP base DN.                                                   |
| `LdapPort`            | int    | `389`     | LDAP server port.                                               |
| `LdapUserFilter`      | string | `""`      | LDAP filter template, e.g., `"(uid={0})"`.                      |
| `OidcAuthority`       | string | `""`      | OIDC issuer URL. Used when `Type == "oidc"` (currently a stub). |
| `OidcClientId`        | string | `""`      | OIDC client ID.                                                 |
| `OidcClientSecret`    | string | `""`      | OIDC client secret.                                             |

Duration strings accept the suffixes `m`, `h`, `d`.

### 10.6 Security

| Property            | Type   | Default    | Description                                                                                                                                          |
| ------------------- | ------ | ---------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `MaxPackageSize`    | string | `"10MB"`   | Maximum tarball upload size. Suffixes `MB`, `GB`.                                                                                                    |
| `RequiredIntegrity` | string | `"sha256"` | Integrity algorithm. Only `"sha256"` is currently supported.                                                                                         |
| `UnpublishWindow`   | string | `"72h"`    | Window during which a freshly published version may be unpublished.                                                                                  |
| `JwtSigningKey`     | string | `null`     | HMAC-SHA256 signing key. Must be at least 32 characters. If `null`, the server generates a random key at startup; tokens will not survive a restart. |

### 10.7 RateLimiting

`RateLimiting.{Category}.*` controls a sliding-window bucket per category. Categories are `Auth`, `Publish`, `Download`, `Search`. A separate `Refresh` bucket exists with built-in defaults that are not currently configurable.

| Property                   | Type    | Description                                    |
| -------------------------- | ------- | ---------------------------------------------- |
| `Enabled`                  | bool    | Disables the middleware entirely when `false`. |
| `{Category}.MaxAttempts`   | integer | Maximum requests per `WindowSeconds`.          |
| `{Category}.WindowSeconds` | integer | Sliding window length in seconds.              |
| `{Category}.MaxPerHour`    | integer | Hard hourly cap.                               |
| `{Category}.MaxPerMinute`  | integer | Hard per-minute cap.                           |

Rate-limited requests receive `429 Too Many Requests` with a `Retry-After` header indicating the earliest retry time.

## 11. Database Schema

Column names below are the on-disk snake_case names produced by `RegistryDbContext`. EF Core manages migrations.

### 11.1 packages

One row per package name. The name is the primary key and must match `@{scope}/{name}` format.

| Column                    | Type     | Constraints                                             | Description                     |
| ------------------------- | -------- | ------------------------------------------------------- | ------------------------------- |
| `name`                    | string   | PK                                                      | Package name (e.g. `@stash/http`). |
| `description`             | string   |                                                         | Short description.              |
| `license`                 | string   |                                                         | SPDX license identifier.        |
| `repository`              | string   |                                                         | Source repository URL.          |
| `readme`                  | string   |                                                         | Extracted README content.       |
| `keywords`                | string   |                                                         | JSON array stored as string.    |
| `latest`                  | string   | not null                                                | Latest published version.       |
| `created_at`              | datetime |                                                         | First publication timestamp.    |
| `updated_at`              | datetime |                                                         | Last modification timestamp.    |
| `deprecated`              | bool     | default false                                           | Package-level deprecation flag. |
| `deprecation_message`     | string   |                                                         | Deprecation reason.             |
| `deprecation_alternative` | string   |                                                         | Suggested replacement package.  |
| `deprecated_by`           | string   |                                                         | User who set the deprecation.   |
| `visibility`              | string   | not null, default `'public'`, CHECK `IN ('public','private','internal')` | Package visibility state. |

**Visibility semantics:**

- `public` — readable by anyone, including unauthenticated callers. No `read` token required.
- `private` — readable only by callers who supply a `read`-scoped (or higher) JWT AND have at least `reader` permission on the package.
- `internal` — readable by callers who supply a `read`-scoped (or higher) JWT AND meet one of: (a) the scope is org-owned and the caller is a member of that org, (b) the scope is user-owned and the caller is the scope owner, or (c) the caller has at least `reader` package role directly.

In all non-public cases, unauthorized callers receive `404 Not Found`, not `403`, to avoid leaking package existence.

### 11.2 versions

Composite primary key on `(package_name, version)`. Foreign-key cascade on delete from `packages`.

| Column                | Type     | Constraints             | Description                         |
| --------------------- | -------- | ----------------------- | ----------------------------------- |
| `package_name`        | string   | PK, FK → packages(name) | Owning package.                     |
| `version`             | string   | PK                      | Semver version string.              |
| `stash_version`       | string   |                         | Required Stash interpreter version. |
| `dependencies`        | string   |                         | JSON object of dependencies.        |
| `integrity`           | string   | not null                | `sha256-<base64>` hash of tarball.  |
| `published_at`        | datetime |                         | Publication timestamp.              |
| `published_by`        | string   | not null                | Publishing username.                |
| `deprecated`          | bool     | default false           | Version-level deprecation flag.     |
| `deprecation_message` | string   |                         | Deprecation reason.                 |
| `deprecated_by`       | string   |                         | User who set the deprecation.       |

### 11.3 users

| Column          | Type     | Constraints                | Description                         |
| --------------- | -------- | -------------------------- | ----------------------------------- |
| `username`      | string   | PK                         | Login name.                         |
| `password_hash` | string   | not null                   | Argon2id hash in PHC string format. |
| `role`          | string   | not null, default `"user"` | `"user"` or `"admin"`.              |
| `created_at`    | datetime |                            | Account creation timestamp.         |

### 11.4 tokens

Scoped API tokens. The `id` column is the same value as the JWT `jti` claim.

| Column        | Type     | Constraints                    | Description                          |
| ------------- | -------- | ------------------------------ | ------------------------------------ |
| `id`          | string   | PK                             | Token ID (= JWT `jti`).              |
| `username`    | string   | FK → users(username), not null | Token owner.                         |
| `token_hash`  | string   | not null                       | SHA-256 hash of the token.           |
| `scope`       | string   | not null, default `"publish"`  | `"read"`, `"publish"`, or `"admin"`. |
| `created_at`  | datetime |                                | Creation timestamp.                  |
| `expires_at`  | datetime |                                | Expiry timestamp.                    |
| `description` | string   |                                | Human-readable label.                |

Foreign-key cascade on delete from `users`.

### 11.5 refresh_tokens

OAuth2 refresh tokens. All tokens issued from the same login share a `family_id`. Both `token_hash` and `family_id` are indexed.

| Column            | Type     | Constraints                    | Description                                        |
| ----------------- | -------- | ------------------------------ | -------------------------------------------------- |
| `id`              | string   | PK                             | Unique refresh token ID.                           |
| `username`        | string   | FK → users(username), not null | Token owner.                                       |
| `token_hash`      | string   | not null, indexed              | SHA-256 hash of the refresh token value.           |
| `access_token_id` | string   | not null                       | ID of the paired access token.                     |
| `family_id`       | string   | not null, indexed              | Token family ID shared across all rotations.       |
| `machine_id`      | string   | not null                       | SHA-256 machine fingerprint the token is bound to. |
| `scope`           | string   | not null, default `"publish"`  | Inherited permission scope.                        |
| `created_at`      | datetime |                                | Creation timestamp.                                |
| `expires_at`      | datetime |                                | Expiry timestamp.                                  |
| `consumed`        | bool     | default false                  | Set when the token has been rotated.               |

Foreign-key cascade on delete from `users`.

### 11.6 organizations

One row per organization.

| Column         | Type     | Constraints    | Description                                               |
| -------------- | -------- | -------------- | --------------------------------------------------------- |
| `id`           | string   | PK (UUID)      | Unique organization identifier.                           |
| `name`         | string   | unique, not null | Lower-case organization name (same grammar as scope).   |
| `display_name` | string   |                | Human-readable display name.                              |
| `created_at`   | datetime |                | Creation timestamp.                                       |
| `created_by`   | string   | not null       | Username of the creator.                                  |

### 11.7 org_members

Composite primary key on `(org_id, username)`. Foreign-key cascade on delete from `organizations`.

| Column     | Type     | Constraints                                    | Description                                |
| ---------- | -------- | ---------------------------------------------- | ------------------------------------------ |
| `org_id`   | string   | PK, FK → organizations(id), cascade            | Owning organization.                       |
| `username` | string   | PK                                             | Member username.                           |
| `org_role` | string   | not null, default `'member'`, CHECK `IN ('owner','member')` | Role in the organization. |
| `joined_at`| datetime |                                                | Membership timestamp.                      |

### 11.8 teams

| Column      | Type     | Constraints                          | Description                               |
| ----------- | -------- | ------------------------------------ | ----------------------------------------- |
| `id`        | string   | PK (UUID)                            | Unique team identifier.                   |
| `org_id`    | string   | FK → organizations(id), cascade      | Owning organization.                      |
| `name`      | string   | not null, unique within org          | Team name.                                |
| `created_at`| datetime |                                      | Creation timestamp.                       |

### 11.9 team_members

Composite primary key on `(team_id, username)`. Foreign-key cascade on delete from `teams`.

| Column      | Type     | Constraints                  | Description              |
| ----------- | -------- | ---------------------------- | ------------------------ |
| `team_id`   | string   | PK, FK → teams(id), cascade  | Owning team.             |
| `username`  | string   | PK                           | Member username.         |
| `joined_at` | datetime |                              | Membership timestamp.    |

### 11.10 scopes

One row per registered scope. The scope name is the primary key (without the leading `@`).

| Column           | Type   | Constraints                                                    | Description                                                                        |
| ---------------- | ------ | -------------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| `name`           | string | PK                                                             | Scope name without `@` (e.g. `stash`).                                             |
| `owner_type`     | string | not null, CHECK `IN ('user','org','system')`                   | Type of owner.                                                                     |
| `owner_username` | string |                                                                | Username for `user` scopes; null otherwise.                                        |
| `owner_org_id`   | string |                                                                | Org ID for `org` scopes; null otherwise.                                           |

A CHECK constraint enforces the single-owner invariant: `system` rows have both owner columns null; `user` rows have only `owner_username` set; `org` rows have only `owner_org_id` set.

The system scopes `stash` and `admin` are seeded at bootstrap and cannot be claimed or deleted.

### 11.11 package_roles

Role assignments for packages. Replaces the old `owners` table. Composite primary key on `(package_name, principal_type, principal_id)`. Foreign-key cascade on delete from `packages`.

| Column          | Type   | Constraints                                                         | Description                                                              |
| --------------- | ------ | ------------------------------------------------------------------- | ------------------------------------------------------------------------ |
| `package_name`  | string | PK, FK → packages(name), cascade                                    | Package.                                                                 |
| `principal_type`| string | PK, CHECK `IN ('user','team','org')`                                | Type of principal.                                                       |
| `principal_id`  | string | PK                                                                  | Username for `user`; team ID for `team`; org ID for `org`.               |
| `role`          | string | not null, CHECK `IN ('owner','maintainer','publisher','reader')`    | Assigned role.                                                           |

### 11.12 audit_log

Immutable record of all state-changing operations. Auto-incrementing primary key.

| Column      | Type     | Constraints        | Description                                |
| ----------- | -------- | ------------------ | ------------------------------------------ |
| `id`        | int      | PK, auto-increment | Internal identifier.                       |
| `action`    | string   | not null           | Action type (see [Section 13]).            |
| `package`   | string   |                    | Affected package name.                     |
| `version`   | string   |                    | Affected version.                          |
| `user`      | string   |                    | Actor username.                            |
| `target`    | string   |                    | Secondary subject (e.g., target username). |
| `ip`        | string   |                    | Client IP at time of request.              |
| `timestamp` | datetime |                    | Event time (UTC).                          |

## 12. Package Role Model

### 12.1 Role → Permission Matrix

| Permission                                             | reader | publisher | maintainer | owner |
| ------------------------------------------------------ | :----: | :-------: | :--------: | :---: |
| Read metadata / versions / download (private package)  | yes    | yes       | yes        | yes   |
| Publish new version of existing package                | no     | yes       | yes        | yes   |
| Unpublish version                                      | no     | no        | yes        | yes   |
| Deprecate / undeprecate package or version             | no     | no        | yes        | yes   |
| Change visibility                                      | no     | no        | no         | yes   |
| Assign / revoke package roles                          | no     | no        | no         | yes   |
| Delete package                                         | no     | no        | no         | yes   |

`admin`-role users carry every permission implicitly.

### 12.2 Permission Resolution

The effective permission for a `(package, username)` pair is the union of:

1. Direct user role assigned via `package_roles` for the caller's username.
2. Roles inherited via every team the caller belongs to (`team_members` join `package_roles` on `principal_type = 'team'`).
3. Roles inherited via the org that owns the package's scope (`org_members` join `package_roles` on `principal_type = 'org'`, or org `owner` members inherit `owner` on all packages in org-owned scopes).

The highest-permission rule wins across all sources. The scope owner (user or org) is automatically assigned `owner` on package creation.

**Additional org-member inheritance rules:**
- Org `owner` members inherit `owner` on every package in any scope owned by that org.
- Org `member` members inherit `reader` on private/internal packages in org-owned scopes. Explicit higher package roles override this default.

## 13. Audit Log

All state-changing operations write a single immutable row. Read-only requests are not logged.

| Action                 | Trigger                                                           |
| ---------------------- | ----------------------------------------------------------------- |
| `publish`              | Package version published.                                        |
| `unpublish`            | Package version unpublished.                                      |
| `user.create`          | User account created.                                             |
| `user.disable`         | User account disabled.                                            |
| `owner.add`            | Package role assigned (principal added or role changed).          |
| `owner.remove`         | Package role revoked.                                             |
| `token.create`         | API token created.                                                |
| `token.revoke`         | API token revoked.                                                |
| `token.refresh`        | Access token refreshed using a refresh token.                     |
| `token_theft_detected` | Consumed refresh token replayed; entire token family was revoked. |
| `package.deprecate`    | Package deprecated.                                               |
| `package.undeprecate`  | Package deprecation cleared.                                      |
| `version.deprecate`    | Version deprecated.                                               |
| `version.undeprecate`  | Version deprecation cleared.                                      |

## 14. Publishing Workflow

A publish request is processed as follows. Failures at any step abort the operation and write no audit entry.

1. Verify the request body size against `Security.MaxPackageSize`. Return `413` if exceeded.
2. Parse the `stash.json` manifest from the tarball; reject malformed manifests with `400`.
3. Validate the package name matches `^@[a-z][a-z0-9-]{0,38}/[a-z][a-z0-9-]{0,38}$`; reject with `400` if not.
4. Verify at least one `.stash` file is present in the tarball; reject with `400` if not.
5. Compute the SHA-256 integrity hash of the tarball bytes.
6. If `X-Integrity` was supplied, verify it matches the computed hash; reject mismatch with `400`.
7. Verify the version is not already published (versions are immutable); reject with `409` if it is.
8. Verify the authenticated user has at least `publisher` permission (or is the scope owner for new packages); reject with `403` if not.
9. On a new package, create the package row with `visibility = 'public'` and assign the scope owner as `owner`. The initial visibility is always `public`; change it via `PATCH /packages/{scope}/{name}/visibility` after publish.
10. Insert the version row, including the integrity hash and the manifest dependencies.
11. Extract `README.md` from the tarball if present and store its content on the package row.
12. Write the tarball to the storage backend.
13. Write a `publish` audit entry.

An unpublish request is processed symmetrically:

1. Verify the caller has at least `maintainer` permission; reject with `403` if not.
2. Verify the version's publication timestamp is within `Security.UnpublishWindow`; reject otherwise.
3. Delete the tarball from the storage backend.
4. Delete the version row.
5. Leave the package row in place; the name remains reserved even when all versions are gone.
6. Write an `unpublish` audit entry.

## 15. Rate Limiting

`RateLimitingMiddleware` applies per-category limits before requests reach any controller.

| Category   | Bucket key | Applies to                     |
| ---------- | ---------- | ------------------------------ |
| `Auth`     | IP address | Login, register.               |
| `Publish`  | Username   | Publish, unpublish, deprecate. |
| `Download` | IP address | Tarball download.              |
| `Search`   | IP address | Search.                        |
| `Refresh`  | IP address | Token refresh.                 |

Buckets are stored in memory and use a sliding window. Stale buckets (no activity for longer than the window) are cleaned up every 1000 requests to bound memory use. Setting `RateLimiting.Enabled` to `false` bypasses the middleware entirely.

Rate-limited responses:

```
HTTP/1.1 429 Too Many Requests
Retry-After: 47
Content-Type: application/json

{ "error": "Rate limit exceeded. Try again in 47 seconds." }
```

## 16. Storage Layout

### 16.1 Filesystem

The default backend stores tarballs under `{Storage.Path}/{safeName}/{version}.tar.gz`. `safeName` is the package name with all characters outside `[a-zA-Z0-9_-]` replaced, which combined with canonical-path verification prevents directory traversal. Every read or write resolves the canonical path via `Path.GetFullPath` and verifies that it lies within `Storage.Path`; otherwise the request is rejected.

### 16.2 S3

Not yet implemented. The `IPackageStorage` interface is satisfied and the class compiles, but all methods throw `NotImplementedException`. The S3 configuration keys are accepted and validated, but no requests are made to AWS.

### 16.3 Storage Interface

```csharp
public interface IPackageStorage
{
    void Store(string packageName, string version, Stream tarball);
    Stream? Retrieve(string packageName, string version);
    bool Exists(string packageName, string version);
    bool Delete(string packageName, string version);
    long GetSize(string packageName, string version);
}
```

Storage backends are selected by `Storage.Type` and bound at startup; switching backends requires only a configuration change.

## 17. Security Contract

| Surface                | Contract                                                                                                                                |
| ---------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| JWT signing key        | HMAC-SHA256, at least 32 characters. A `null` value generates a random key at startup; tokens do not survive restarts.                  |
| Password hashing       | Argon2id with OWASP parameters, stored in PHC string format.                                                                            |
| Integrity verification | Every published tarball stores a `sha256-<base64>` hash; downloads return it as `X-Integrity`. Optional client verification on publish. |
| Path traversal         | `FileSystemStorage` canonicalizes every path before reading or writing and rejects paths that escape `Storage.Path`.                    |
| Input validation       | Username `1-64` chars from `[a-zA-Z0-9_-]`. Password minimum 8 chars. Package name validated against `^@[a-z][a-z0-9-]{0,38}/[a-z][a-z0-9-]{0,38}$`. |
| Machine binding        | Refresh tokens are bound to a SHA-256 machine fingerprint of `hostname:username:platform`; mismatched fingerprints are rejected.        |
| Token revocation       | Database-driven via `jti` lookup on every authenticated request.                                                                        |
| Visibility enforcement | Private/internal packages return `404 Not Found` (not `403`) for unauthorized callers to avoid leaking package existence.               |

## 18. CLI Integration

The `stash pkg` CLI maps directly to the endpoints in [Section 3](#3-endpoint-summary). Credentials are stored at `~/.stash/config.json` with mode `0600` on Unix.

| CLI Command           | HTTP Request                                              | Description                        |
| --------------------- | --------------------------------------------------------- | ---------------------------------- |
| `stash pkg login`     | `POST /api/v1/auth/login`                                 | Authenticate and store token pair. |
| `stash pkg logout`    | -                                                         | Remove stored credentials.         |
| `stash pkg publish`   | `PUT /api/v1/packages/{scope}/{name}`                     | Upload tarball and publish.        |
| `stash pkg unpublish` | `DELETE /api/v1/packages/{scope}/{name}/{version}`        | Unpublish.                         |
| `stash pkg install`   | `GET /api/v1/packages/{scope}/{name}/{version}/download`  | Download and install.              |
| `stash pkg update`    | `GET /api/v1/packages/{scope}/{name}/{version}/download`  | Re-resolve and update.             |
| `stash pkg search`    | `GET /api/v1/search`                                      | Search.                            |
| `stash pkg info`      | `GET /api/v1/packages/{scope}/{name}`                     | Display metadata.                  |

The CLI targets exactly one registry per invocation. With no `--registry` flag it uses `defaultRegistry` from `~/.stash/config.json`; if no default is set, the command errors. `login` and `logout` always require an explicit `--registry` flag, since they configure the registry entry itself.

The config file is a URL-keyed dictionary:

```json
{
  "defaultRegistry": "https://registry.example.com/api/v1",
  "registries": {
    "https://registry.example.com/api/v1": {
      "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
      "expiresAt": "2026-06-20T13:00:00Z",
      "refreshToken": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4...",
      "refreshTokenExpiresAt": "2026-09-18T12:00:00Z",
      "machineId": "a1b2c3d4e5f6..."
    }
  }
}
```

The CLI refreshes the access token automatically when it is within five minutes of expiry, persists the new pair to the config file, and falls back silently to the existing access token if refresh fails. The first successful `login` sets `defaultRegistry` if none was configured. Logging out of the current default clears `defaultRegistry`.

Full CLI semantics are documented in [PKG - Package Manager CLI](PKG%20—%20Package%20Manager%20CLI.md).

## 19. Limitations

| Limitation          | Contract                                                                                        |
| ------------------- | ----------------------------------------------------------------------------------------------- |
| Auth providers      | Only `local` is functional. `ldap` and `oidc` configuration is accepted but unused.             |
| Storage backends    | Only `filesystem` is functional. `s3` returns `NotImplementedException` on every operation.     |
| Integrity algorithm | Only `sha256` is supported.                                                                     |
| Stats endpoint      | `GET /admin/stats` currently returns only the user count.                                       |
| Unpublish window    | After `Security.UnpublishWindow` expires, the only path to discourage a version is deprecation. |
| Scope deletion      | Scopes are not deletable in this release. Org deletion cascades only when no packages depend on the scope. |

## 20. Change Rules

Changes to the registry must preserve these rules:

- **Endpoint additions** must be documented in [Section 3](#3-endpoint-summary) before merging, and must use the existing `/api/v1` prefix; an incompatible change requires a new path prefix.
- **Breaking changes to request or response shapes** are not allowed under `/api/v1`. Add a new field rather than rename or repurpose an existing one.
- **New scopes, policies, or claims** must be documented in [Section 4](#4-authentication) and must not weaken any existing policy.
- **New configuration keys** must be documented in [Section 10](#10-configuration) with a default that preserves existing behavior.
- **New audit actions** must be added to [Section 13](#13-audit-log) and the schema must remain append-only.
- **New database tables or columns** require an EF Core migration; existing columns must not be renamed or repurposed.
- Implementation details (controller plumbing, DI registration, service internals, deployment topology) belong in source comments or engineering notes, not in this reference.
