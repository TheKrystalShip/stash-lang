# Authorization Flow Example

This walkthrough shows the canonical happy path for the claim-based scope ownership
policy (`Security.ScopeOwnershipPolicy = claim`, the default), then demonstrates
the DENY that occurs when a token with an insufficient ceiling is used.

## Scenario

- Registry is configured with `ScopeOwnershipPolicy = claim` (default)
- `alice` registers, creates the `acme` org (which claims the `@acme` scope), and publishes `@acme/widgets`
- `alice` creates a `publish`-ceiling API token for CI use
- A separate `read`-ceiling token is denied when it tries to publish

---

## Step 1 — Register and log in

```
POST /api/v1/auth/register
Content-Type: application/json

{
  "username": "alice",
  "password": "s3cr3tpassword"
}
```

Response `201 Created`:

```json
{ "ok": true, "username": "alice" }
```

Login to get a `read`-ceiling access token (the default since the authz-pipeline refactor):

```
POST /api/v1/auth/login
Content-Type: application/json

{
  "username": "alice",
  "password": "s3cr3tpassword"
}
```

Response `200 OK`:

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresAt": "2026-07-01T14:00:00Z",
  "refreshToken": null,
  "refreshTokenExpiresAt": null
}
```

The login token has `token_scope = "read"` — it is suitable for read operations but
cannot publish or manage scopes.

---

## Step 2 — Issue a publish-ceiling API token

Creating an org and claiming a scope both require a `publish`-ceiling token. The login token
is `read`-ceiling, so we issue a publish token first.

```
POST /api/v1/auth/tokens
Authorization: Bearer <login-token>
Content-Type: application/json

{
  "name": "ci-publish-acme",
  "ceiling": "publish",
  "expiresIn": "30d"
}
```

Response `201 Created`:

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.ci-publish...",
  "tokenId": "550e8400-e29b-41d4-a716-446655440000",
  "ceiling": "publish",
  "expiresAt": "2026-08-01T12:00:00Z",
  "name": "ci-publish-acme"
}
```

This token carries `token_scope = "publish"`. The PDP ceiling check will pass for
`CreatePackage` and `PublishVersion` actions.

**Token lifetime cap.** If `expiresIn` exceeds `Security.MaxTokenLifetime` (default `90d`),
the registry rejects the request `400 TokenLifetimeExceeded`.

---

## Step 3 — Create the `acme` org (claims the `@acme` scope automatically)

Under `ScopeOwnershipPolicy = claim`, a scope must be owned before publishing into it.
Creating an org provisions its scope atomically in the same transaction.

```
POST /api/v1/orgs
Authorization: Bearer <publish-token>
Content-Type: application/json

{
  "name": "acme",
  "display_name": "Acme Corp"
}
```

**PDP evaluation for `CreateOrg`:**

1. **Ceiling check:** `publish >= publish` → PASS
2. **Resource-side check:** caller is authenticated, org name is valid and not taken → PASS

Response `201 Created`:

```json
{ "ok": true, "name": "acme" }
```

The `@acme` scope is now owned by the `acme` org, and `alice` (as the org creator) is the
org `owner`. Any attempt by another user to publish into `@acme` will be denied `403 ScopeNotOwned`.

---

## Step 4 — Publish `@acme/widgets`

```
PUT /api/v1/packages/acme/widgets
Authorization: Bearer <publish-token>
Content-Type: application/gzip
X-Version: 1.0.0

<tarball bytes>
```

**PDP evaluation (two steps) — `CreatePackage` (first publish):**

1. **Ceiling check:** `publish >= publish` → PASS
2. **Resource-side check (CreatePackage):** Is `@acme` owned by a principal `alice` controls?
   Yes — `acme` org is owned by `alice` as org `owner`, and `@acme` scope is org-owned by `acme` → PASS

Response `201 Created`:

```json
{
  "ok": true,
  "package": "@acme/widgets",
  "version": "1.0.0",
  "integrity": "sha256-abc123..."
}
```

`alice` (as org owner) is automatically assigned the `owner` role on `@acme/widgets`.
Subsequent publishes to `@acme/widgets` use the `PublishVersion` PDP action, gated on
the `publisher` (or higher) package role.

---

## Step 5 — DENY: read-ceiling token tries to publish

Using the original login token (`token_scope = "read"`) to publish a second version:

```
PUT /api/v1/packages/acme/widgets
Authorization: Bearer <login-token>
Content-Type: application/gzip
X-Version: 1.1.0

<tarball bytes>
```

**PDP evaluation:**

1. **Ceiling check:** `read >= publish`? No → DENY `TokenScopeInsufficient`

Response `403 Forbidden`:

```json
{
  "error": "TokenScopeInsufficient",
  "message": "The token's coarse ceiling (read) is insufficient for action PublishVersion. A publish or admin ceiling is required."
}
```

The ceiling check runs **first** and short-circuits before the resource-side role check.
Even if `alice` is the package owner, the `read`-ceiling token cannot publish.

---

## Summary

| Step | Token ceiling | Action | PDP result |
|------|--------------|--------|------------|
| Login | `read` (default) | `Login` | Access token issued (`read` ceiling) |
| Issue publish token | `read` | `IssueToken` | ALLOW (ceiling sufficient, self-resource) |
| Create org | `publish` | `CreateOrg` | ALLOW (ceiling sufficient) |
| Publish 1.0.0 | `publish` | `CreatePackage` | ALLOW (ceiling + scope owned via org) |
| Publish 1.1.0 | `read` | `PublishVersion` | DENY `TokenScopeInsufficient` |

**Key takeaways:**

- Login always issues a `read`-ceiling token (least-privilege default, Decision D6).
- Publishing requires an explicit `publish`-ceiling API token from `POST /auth/tokens`.
- Scope/org management also requires `publish` ceiling — obtain the publish token first.
- The PDP checks the ceiling first; admin role does NOT bypass the ceiling check.
- Under `ScopeOwnershipPolicy = claim` (default), the scope must be owned before publishing.
- Atomic guarantee: two concurrent `POST /api/v1/orgs` (or `POST /api/v1/scopes`) requests
  for the same name result in exactly one `201` and one `409`.
