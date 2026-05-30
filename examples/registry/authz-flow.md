# Authorization Flow Example

This walkthrough shows the canonical happy path for the claim-based scope ownership
policy (`Security.ScopeOwnershipPolicy = claim`, the default), then demonstrates
the DENY that occurs when a token with an insufficient ceiling is used.

## Scenario

- Registry is configured with `ScopeOwnershipPolicy = claim` (default)
- `alice` registers, claims the `@acme` scope, and publishes `@acme/widgets`
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

The login token has `token_scope = "read"` — it cannot publish.

---

## Step 2 — Claim the `@acme` scope

Under `ScopeOwnershipPolicy = claim`, a scope must be claimed before publishing into it.

```
POST /api/v1/scopes
Authorization: Bearer <login-token>
Content-Type: application/json

{
  "scope": "acme",
  "owner_type": "user",
  "owner": "alice"
}
```

Response `201 Created`:

```json
{ "ok": true, "scope": "acme", "owner_type": "user", "owner": "alice" }
```

`@acme` is now owned by `alice`. Any attempt by another user to publish into `@acme` will
be denied with `403 ScopeNotOwned`.

---

## Step 3 — Issue a publish-ceiling API token

The login token is `read`-ceiling. Publishing requires a `publish`-ceiling token. Issue one
via `POST /auth/tokens`:

```
POST /api/v1/auth/tokens
Authorization: Bearer <login-token>
Content-Type: application/json

{
  "name": "ci-publish-acme",
  "ceiling": "publish",
  "expires_in": "30d"
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

---

## Step 4 — Publish `@acme/widgets`

```
PUT /api/v1/packages/acme/widgets
Authorization: Bearer <publish-token>
Content-Type: application/gzip
X-Version: 1.0.0

<tarball bytes>
```

**PDP evaluation (two steps):**

1. **Ceiling check:** `publish >= publish` → PASS
2. **Resource-side check (CreatePackage):** Is `@acme` owned by `alice`? Yes (`ScopeOwnershipPolicy = claim`, scope row exists with `owner_type = user`, `owner_username = alice`) → PASS

Response `201 Created`:

```json
{
  "ok": true,
  "package": "@acme/widgets",
  "version": "1.0.0",
  "integrity": "sha256-abc123..."
}
```

`alice` is automatically assigned the `owner` role on `@acme/widgets`.

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
| Login | `read` (default) | `IssueToken` | ALLOW (ceiling sufficient, self-resource) |
| Claim scope | `read` | `ClaimScope` | ALLOW (ceiling sufficient for scope claim, `alice` is owner) |
| Issue publish token | `read` | `IssueToken` | ALLOW |
| Publish 1.0.0 | `publish` | `CreatePackage` | ALLOW (ceiling + scope owned) |
| Publish 1.1.0 | `read` | `PublishVersion` | DENY `TokenScopeInsufficient` |

**Key takeaways:**

- Login always issues a `read`-ceiling token (least-privilege default, Decision D6).
- Publishing requires an explicit `publish`-ceiling API token from `POST /auth/tokens`.
- The PDP checks the ceiling first; admin role does NOT bypass the ceiling check.
- Under `ScopeOwnershipPolicy = claim` (default), the scope must exist before publishing.
