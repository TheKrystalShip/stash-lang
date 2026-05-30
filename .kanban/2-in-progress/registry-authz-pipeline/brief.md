# RFC: Registry Authorization Pipeline Redesign

> **Status:** Draft (revised)
> **Owner:** Cristian Moraru
> **Created:** 2026-05-30
> **Slug:** registry-authz-pipeline

## Summary

The Stash Package Registry's authorization is **advisory and opt-in**. Two
unrelated axes — JWT `token_scope` (read/publish/admin) and per-package role
(`owner`/`maintainer`/`publisher`/`reader`) — are evaluated by ad-hoc checks
scattered across controllers and `PackageService`, each over a *different*
notion of the resource. The result is two live, exploitable
authorization-bypass bugs in the shipped `registry-scope-foundation` feature
(Bug A: any account can squat any `@scope`; Bug B: PUT to `@me/lib` carrying
a manifest naming `@victim/lib` writes into `@victim/lib`), plus the entire
role matrix `publisher`/`maintainer`/`reader` is **inert** because every
mutating endpoint hard-codes a `"owner"` permission check.

This RFC replaces the scattered checks with a **single Policy Decision Point
(PDP)** keyed by a closed `(Action, Resource)` model, a **default-deny**
endpoint contract enforced by a build-failing meta-test, a
**route-authoritative single identity** for package writes, a **split between
`CreatePackage` and `PublishVersion`** as distinct actions gated on
different facts, a **coarse token ceiling** (`read`/`publish`/`admin`) that
the PDP intersects with resource-side permissions, a server-enforced
**`MaxTokenLifetime`** cap, and a **`Security.ScopeOwnershipPolicy`**
deploy-time enum (`open` | `claim` | `verified`, default `claim`) that
governs only the unclaimed-scope branch of `CreatePackage`. This is a hard
clean break — old token and permission shapes are replaced outright; no
shim layer survives.

Fine-grained per-package / per-action token capability rules (npm-style
granular tokens) are **deferred** to a follow-up feature — see
`.kanban/0-backlog/registry/Fine-grained token capabilities (deferred from authz-pipeline).md`.
That work is purely additive; the coarse ceiling shipped here covers the
security-critical least-privilege guarantee (D6) on its own.

## Motivation

A permission-chain review identified two live bugs and one structural
property in `4-done/registry-scope-foundation`:

**Bug A — no scope-ownership check on new-package publish.**
`PackageService.Publish` (`Stash.Registry/Services/PackageService.cs:81-102`)
creates any not-yet-existing package and assigns the *caller* as `owner`,
never checking the caller owns or controls the target `@scope`. Any
authenticated account can publish `@bigcorp/utils` (including reserved
`@stash`/`@admin`, which are guarded only on the claim/org paths, not on
publish). Pure squatting.

**Bug B — controller authorizes the route name, the service writes the
manifest name.** `PublishPackage`
(`Stash.Registry/Controllers/PackagesController.cs:247-278`) checks
`HasPackagePermissionAsync` against the route `@{scope}/{name}`, but
`PackageService.Publish(Stream, username, clientIntegrity)` is never passed
the route name — it acts on `manifest.Name` extracted from the tarball
(`PackageService.cs:73`). PUT a tarball to a route you own, but ship a
manifest naming `@victim/lib@9.9.9`: `packageExists` is `true` for
`@victim/lib`, the controller's owner check ran against `@me/lib` (which you
own), so the controller lets the call through; `PackageService` then calls
`AddVersionAsync(packageName=@victim/lib, ...)`, then recomputes
`SelectLatestVersion` and points `@victim/lib`'s `latest` at the attacker's
9.9.9. Default-install target is now attacker-controlled.

**Blast radius is "every account," not "every publisher,"** because login at
`AuthController.cs:119` auto-mints a `publish`-scoped token unconditionally.

**Structural property: authorization is opt-in, scattered, and decoupled
from identity.** Bug A and Bug B are symptoms of one underlying shape:
checks live in controllers AND services, on *different inputs*, with two
axes (token scope and package role) composed nowhere. There is no single
place to ask "is this principal allowed to perform action X on resource Y?"
Patching A and B in place would not fix what *let* them exist — the next
endpoint added will re-create the same gap.

**The role matrix is inert.** `registry-scope-foundation` brief.md:154-162
documents `publisher` → publish, `maintainer` → unpublish/deprecate,
`reader` → read, but every mutating endpoint in `PackagesController`
hard-codes `HasPackagePermissionAsync(..., "owner")` (lines 255, 346, 388,
431, 474, 517, 557, 599). A user assigned `publisher` cannot publish a new
version. A `maintainer` cannot deprecate. The role assignment endpoint
exists; its output does nothing.

These are the user-visible symptoms; the structural redesign is the only
fix shape that holds.

## Goals

- One **Policy Decision Point (PDP)** for every authorization decision in
  the registry, keyed by `(Action, Resource, Principal)`. Reads and writes
  go through the SAME engine.
- **Default-deny endpoint contract**: every controller action declares an
  explicit policy or carries `[PublicEndpoint]`. A meta-test enumerates
  every controller action and **fails the build** if any endpoint declares
  neither.
- **Route-authoritative single identity** for package writes: the URL path
  is canonical. Manifest `name` and `version` that disagree with the route
  are rejected. The manifest name is NEVER used as a write key.
- **Split `CreatePackage` from `PublishVersion`** as distinct PDP actions.
  Creating a new package id under a scope is gated on scope ownership;
  publishing a new version of an existing package is gated on package role.
- **Token ceiling as a capability ceiling**, intersected with resource
  permissions inside the PDP — not a separate axis evaluated elsewhere.
  Ceiling is the **only** token dimension this phase: `read` | `publish` |
  `admin`. The ceiling check runs **first**, before role / scope / org /
  visibility resolution.
- **Stop auto-granting `publish` on login.** Tokens are issued at a
  deliberately chosen ceiling, with mandatory expiry; the default login
  token is `read`-ceiling.
- **Server-enforced token lifetime cap** via `Security.MaxTokenLifetime`
  (default `90d`). `POST /auth/tokens` rejects `expires_in` over the cap.
- **Wire the role matrix end-to-end.** `publisher` can publish new
  versions of an existing package without being an owner. `maintainer` can
  deprecate and unpublish. `reader` can read private packages. `owner`
  retains all rights.
- **Admin short-circuit, narrowed.** `role == admin` resolves the
  **resource-side** dimension to effective `owner` on any package. The
  **ceiling check still applies first** — an admin holding a `read`-ceiling
  token is denied any write with `TokenScopeInsufficient`. This deliberately
  narrows the shipped blanket `IsInRole("admin")` bypass.
- **`Security.ScopeOwnershipPolicy`** deploy-time enum (`open` | `claim` |
  `verified`, default `claim`), governing only the unclaimed-scope branch
  of `CreatePackage`. Reserved scopes (`@stash`, `@admin`) and
  someone-else-owned scopes are denied under every setting.
- **OIDC-ready principal abstraction (design-only).** The PDP `principal`
  model accepts an ephemeral CI identity in a future feature without rework.
- Fold `CanReadPackageAsync` (the one currently-correct authz path in the
  shipped code) INTO the unified resolver. No second bespoke read system
  survives.

## Non-Goals

- **Fine-grained per-package / per-action token capability rules**
  (selectors, capability allow-lists; npm granular-token style). Deferred
  to a follow-up feature — see backlog stub. The deferral is purely
  additive: it reopens no security bug, because the coarse ceiling (D6)
  already enforces least-privilege at login.
- **No backwards compatibility.** Old `TokenRecord.Scope` shape, the four
  string-based `RequireXxxScope` policies, and ad-hoc service-level checks
  are replaced outright. No alias layer; no parallel `/api/v2`. The coarse
  ceiling is **stored in the existing `TokenRecord.Scope` column** (values
  `read`/`publish`/`admin`); no new column is introduced.
- **No OIDC / Trusted Publishing implementation.** Only the principal
  abstraction is shaped to admit it later.
- **No 2FA, no signed manifests, no provenance / SLSA, no advisories.**
- **No new identity types beyond `User`/`Org`/`Team`/`Admin`/(future)
  `Oidc`.** Service-account principals are deferred.
- **No data migration of existing tokens.** All in-flight tokens are
  invalidated by the schema cut; users re-login.
- **No changes to the EF org/team/scope/package-role tables shipped in
  `registry-scope-foundation`.** Those tables are reused; only the
  permission-resolver code that consumes them moves.
- **No stdlib namespace changes.** The meta-test enforcement family
  (`Wave1ThrowsCoverageTests`, `CompletionSurfaceSnapshotTests`,
  `StandardLibraryReferenceTests`) is kept in `final_verify`.

### Deferred follow-ups

- Fine-grained capability tokens (per-package / per-action allow-lists,
  npm-granular-token shape). Stub:
  `.kanban/0-backlog/registry/Fine-grained token capabilities (deferred from authz-pipeline).md`.

## Design

### Surface

#### PDP entry point

A single registry-owned authorization service, placed under
`Stash.Registry/Auth/Authorization/`:

```csharp
public interface IRegistryAuthorizer
{
    Task<AuthzDecision> AuthorizeAsync(
        Principal principal, RegistryAction action, ResourceRef resource);
}

public readonly record struct AuthzDecision(bool Allowed, AuthzDenyReason Reason, string? Detail)
{
    public static AuthzDecision Allow() => new(true, AuthzDenyReason.None, null);
    public static AuthzDecision Deny(AuthzDenyReason r, string? d = null) => new(false, r, d);
}

public enum AuthzDenyReason
{
    None, NotAuthenticated, TokenScopeInsufficient,
    TokenExpired, TokenRevoked, ScopeNotOwned, ScopeReserved,
    PackageRoleInsufficient, PackageNotFound, OrgMembershipRequired,
    VisibilityHidden, PolicyDenied
}
```

(`TokenCapabilityScopeMiss` is **not** present this phase — it ships with
the deferred fine-grained capability feature.)

#### Closed `RegistryAction` enum (the action axis)

```csharp
public enum RegistryAction
{
    // package
    ReadPackageMetadata,         // GET /packages/{scope}/{name}
    ReadPackageVersion,          // GET /packages/{scope}/{name}/{version}
    DownloadPackageVersion,      // GET .../{version}/download
    CreatePackage,               // PUT /packages/{scope}/{name} — when package does NOT yet exist
    PublishVersion,              // PUT /packages/{scope}/{name} — when package DOES exist
    UnpublishVersion,            // DELETE /packages/{scope}/{name}/{version}
    DeprecatePackage,            // PATCH .../deprecate (and undeprecate)
    DeprecateVersion,            // PATCH .../{version}/deprecate (and undeprecate)
    ChangePackageVisibility,     // PATCH .../visibility
    ListPackageRoles, AssignPackageRole, RevokePackageRole, DeletePackage,
    // scope
    ResolveScope, ClaimScope, VerifyScope,
    // org
    CreateOrg, ReadOrg, AddOrgMember, RemoveOrgMember, CreateTeam, AddTeamMember,
    // auth / tokens
    Login, Register, Whoami, IssueToken, ListOwnTokens, RevokeOwnToken,
    // admin
    ReadAdminStats, ManageUser, AdminAssignPackageRole, AdminRevokePackageRole, ReadAuditLog,
    // search
    Search
}
```

#### `ResourceRef` (the resource axis)

```csharp
public abstract record ResourceRef;
public sealed record PackageResource(string Scope, string LocalName) : ResourceRef;
public sealed record ScopeResource(string Scope) : ResourceRef;
public sealed record OrgResource(string OrgName) : ResourceRef;
public sealed record TokenResource(string TokenId) : ResourceRef;
public sealed record AdminResource() : ResourceRef;
public sealed record SearchResource() : ResourceRef;
public sealed record PrincipalSelfResource() : ResourceRef;
```

`PackageResource` is the only resource that participates in Bug B. Its only
constructor is fed by the route binding helper (below). The manifest is
never permitted to mint a `PackageResource`.

#### `Principal` shape

```csharp
public abstract record Principal;
public sealed record AnonymousPrincipal() : Principal;
public sealed record UserPrincipal(
    string Username,
    UserRole Role,                    // user | admin
    TokenCeiling Ceiling,             // read | publish | admin
    string TokenId) : Principal;
// Reserved for future OIDC trusted publishing — design-only this phase.
// public sealed record OidcPrincipal(string Issuer, string Subject, ...) : Principal;
```

`TokenCeiling` is the **only** token-side dimension this phase. There is no
`TokenCapabilities`, no `TokenCapabilityRule`, no `TokenResourceSelector`,
no `IsUnrestricted`. (All deferred — see backlog stub.)

#### PDP intersection (the only two steps this phase)

For each request:

1. **Ceiling check.** `principal.Ceiling >= action_required_ceiling` else
   `TokenScopeInsufficient`. Runs **first** — admin role does NOT
   short-circuit this step.
2. **Resource-side check.** Per the action, evaluate the appropriate
   facts: package role (with admin short-circuit to effective `owner`),
   scope ownership (CreatePackage only), org membership, visibility.

If both pass: ALLOW. Otherwise DENY with the typed reason from step 2.

#### `[PublicEndpoint]` attribute and default-deny meta-test

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PublicEndpointAttribute : Attribute
{
    public string Justification { get; }
    public PublicEndpointAttribute(string justification) { Justification = justification; }
}
```

Meta-test (`Stash.Tests/Registry/AuthzCoverageMetaTests.cs`) enumerates
every controller action and asserts each carries either an `[Authorize]`
(direct or inherited) or `[PublicEndpoint("reason")]`. `[AllowAnonymous]`
is **forbidden** going forward.

#### Route binding helper for package writes

```csharp
internal static class PackageRoute
{
    public static PackageResource From(string scope, string name)
        => new(Sanitize(scope), Sanitize(name));
}
```

`PackageService.PublishAsync` accepts an authoritative `PackageResource`
parameter and **rejects** the tarball with `400 ManifestRouteMismatch` if:

- `manifest.Name` disagrees with the route, OR
- `manifest.Version` disagrees with the intended version (extends the
  mismatch check to the version field — see Q5 resolution).

This closes Bug B structurally.

```csharp
public Task<VersionRecord> PublishAsync(
    PackageResource resource,           // canonical, from route
    Stream tarball,
    Principal principal,
    string? clientIntegrity);
```

#### `Security.ScopeOwnershipPolicy`

Added to `Stash.Registry/Configuration/SecurityConfig.cs`:

```csharp
public ScopeOwnershipPolicyKind ScopeOwnershipPolicy { get; set; }
    = ScopeOwnershipPolicyKind.Claim;

public enum ScopeOwnershipPolicyKind { Open, Claim, Verified }
```

`appsettings.json`:

```json
"Security": {
    ...
    "ScopeOwnershipPolicy": "claim",
    "MaxTokenLifetime": "90.00:00:00"
}
```

PDP-enforced semantics for `CreatePackage` on `PackageResource(scope, name)`:

| Scope ownership state                       | `open`                                              | `claim` (default)                                                                                          | `verified`                                                                                                            |
| ------------------------------------------- | --------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| Scope is reserved (`stash`, `admin`)        | **DENY** `ScopeReserved`                            | **DENY** `ScopeReserved`                                                                                   | **DENY** `ScopeReserved`                                                                                              |
| Scope owned by some other user/org          | **DENY** `ScopeNotOwned`                            | **DENY** `ScopeNotOwned`                                                                                   | **DENY** `ScopeNotOwned`                                                                                              |
| Scope owned by caller (or org caller owns)  | **ALLOW**                                           | **ALLOW**                                                                                                  | **ALLOW** (only after verification has succeeded)                                                                     |
| Scope does not exist (unowned)              | **AUTO-CLAIM** as user-scope owned by caller, ALLOW | **DENY** `ScopeNotOwned`, message "Scope '@x' is not claimed — run `stash pkg scope claim @x` first."     | **DENY** `ScopeNotOwned`, message "Scope '@x' requires verification — run `stash pkg scope claim @x` then `verify`." |

**Invariant** (PDP-enforced, independent of toggle): `ScopeReserved` and
`ScopeNotOwned` for someone-else-owned scopes are **never** suppressed by
any policy. Under `verified`, a **pending** scope is treated as unowned
(so the per-policy table denies with the verification message).

#### Scope-claim flow

`POST /api/v1/scopes` runs the validation gauntlet
(grammar → reserved → namespace-pool collision → ownership → per-policy
branch). Under `verified`, the response carries a normative challenge body:

```json
{ "scope": "acme", "owner_type": "org", "owner": "acme",
  "state": "pending",
  "challenge": {
    "method": "dns-txt",
    "record_name": "_stash-challenge.acme",
    "record_value": "stash-verify=01HXY...",
    "expires_at": "2026-06-06T12:00:00Z"
  } }
```

`POST /api/v1/scopes/{scope}/verify` returns the documented success/failure
shape. **The actual DNS-TXT / HTTP-well-known resolver is stubbed
(`501 NotImplemented`) this phase**; the wire contract is normative, the
verification depth is deferred (per Q4 resolution).

#### Token issuance (D6, Q8)

`POST /api/v1/auth/login` no longer mints a `publish` token. The access
token is `read`-ceiling. Callers who want to publish must explicitly issue
a publish-capable token:

```jsonc
POST /api/v1/auth/tokens
{
  "name": "ci-publish-acme-widgets",
  "ceiling": "publish",                  // read | publish | admin
  "expires_in": "30d"                    // mandatory; capped at Security.MaxTokenLifetime
}
```

There is **no `capabilities` array** in the request body this phase.

- `expires_in` is mandatory; absent or invalid values are rejected `400`.
- `expires_in` greater than `Security.MaxTokenLifetime` (default 90 days)
  is rejected `400 TokenLifetimeExceeded` with the configured cap echoed
  back.

`TokenRecord` carries no new column: the coarse ceiling **reuses the
existing `TokenRecord.Scope` column** with values `read`/`publish`/`admin`.
No data migration is needed beyond invalidating in-flight tokens.

PDP intersection at request time (the only two steps):

1. `principal.Ceiling >= action_required_ceiling` else
   `TokenScopeInsufficient`.
2. Resource-side check (role / scope ownership / visibility) per the
   action.

The login response in step 1 is `read`-only → default workflow
(install/search) is unaffected; publish flows must call `auth/tokens`
explicitly first.

#### Role-matrix wiring (closing the inert-roles bug)

The PDP resolves a per-action minimum role from this matrix (lifted from
`registry-scope-foundation/brief.md:154-162`):

| Action                                                              | Minimum direct/inherited role            |
| ------------------------------------------------------------------- | ---------------------------------------- |
| `ReadPackageMetadata` (private/internal)                            | `reader`                                 |
| `ReadPackageVersion`, `DownloadPackageVersion` (private/internal)   | `reader`                                 |
| `PublishVersion` (existing package)                                 | `publisher`                              |
| `UnpublishVersion`                                                  | `maintainer`                             |
| `DeprecatePackage`, `DeprecateVersion`                              | `maintainer`                             |
| `ChangePackageVisibility`                                           | `owner`                                  |
| `ListPackageRoles`, `AssignPackageRole`, `RevokePackageRole`        | `owner`                                  |
| `AdminAssignPackageRole`, `AdminRevokePackageRole`                  | n/a — admin-role short-circuit only      |
| `DeletePackage`                                                     | `owner`                                  |
| `CreatePackage` (new package)                                       | n/a — gated on **scope ownership**       |

Resolution: highest of (direct user role on package) + (any team the user
is on with role on package) + (org-owner-of-the-scope auto-grants `owner`
on every package in the scope).

**Admin short-circuit (Q2 resolution):** when `principal.Role == admin`,
the resource-side step resolves to effective `owner` on any package /
scope / org. This **does NOT bypass the ceiling check** — an admin holding
a `read`-ceiling token attempting any write receives
`403 TokenScopeInsufficient` before the role dimension is even consulted.
This deliberately narrows the shipped blanket `IsInRole("admin")` bypass.

#### Package role revocation + last-owner protection

`AssignPackageRole` has a peer `RevokePackageRole` (owner-gated, same dimension)
and an admin peer `AdminRevokePackageRole` (admin-role short-circuit, mirrors
`AdminAssignPackageRole`). Endpoints:

- `DELETE /api/v1/packages/{scope}/{name}/roles` — owner self-service revoke,
  body `{ principal_type, principal_id }`, `204` on success.
- `DELETE /api/v1/admin/packages/{scope}/{name}/roles` — admin override, same
  body, `204` on success.

Both return `404` for missing package or missing role assignment. Both enforce
the **last-owner / orphan-protection invariant**:

> A package MUST retain at least one principal directly holding `owner`. A
> revoke (or reassign-away) that would drop the package's owner count to zero
> is refused `409 Conflict` with detail "cannot remove the last owner of a
> package."

This follows industry standard: GitHub refuses to remove the last admin from
a repository; npm requires `>=1` owner. It is a **hard invariant** enforced
inside the role-mutation service, not the PDP, because the PDP only decides
whether the caller MAY mutate — the orphan check is a downstream integrity
gate that runs regardless of caller. It applies to any role mutation: direct
revoke, admin revoke, and a future "reassign owner" path.

Every successful role mutation (assign and revoke, owner-path and admin-path)
emits a `role.assign` or `role.revoke` audit entry (see "Audit logging" below).

> **Sourcing note:** server-side role revocation was previously claimed by
> `pkg-cli-api-parity` (its P1). That feature is being de-prioritized and the
> CLI `pkg role rm` work is now downstream of this feature; ownership of the
> revoke endpoint, the orphan invariant, and the admin-revoke peer moved
> here. See Decision Log D18.

#### Audit logging of authorization decisions

Two categories of events are appended to `AuditService` (the existing append
log table; no schema change beyond the discriminator):

1. **Every state-mutating authorized action emits a success audit entry.**
   That includes: `role.assign`, `role.revoke`, `scope.claim`, `scope.verify`,
   `token.issue`, `token.revoke`, `package.create`, `package.publish`,
   `package.unpublish`, `package.deprecate`, `package.visibility_change`. The
   entry carries the principal id, action, resource, decision (`allow`), and
   timestamp.
2. **Every authenticated authorization denial emits a deny audit entry**
   carrying the `AuthzDenyReason` enum value verbatim. This exists to feed
   intrusion-detection on abnormal deny rates per principal.

**Deliberately excluded from the audit log:** anonymous public-read denials
(typical 404 traffic on private packages to unauthenticated callers). This is
the dominant traffic shape and would flood the table without a security
signal — public-read 404s already render in the HTTP access log. The scope is
explicit: **mutations and authenticated denials only**. Stating this as a
conscious decision rather than an omission means a future "audit the
anonymous-deny stream" follow-up reopens it deliberately.

`AuditService.AppendAsync` is called from the PDP wrapper at the controller
seam (a single helper threaded through `IRegistryAuthorizer` consumers), so
no production code that wants to record an authz event can bypass it.

#### Concurrency / atomicity of namespace allocation

Two endpoints race under concurrent traffic and MUST be atomic at the
database layer, not the application layer:

- **`POST /api/v1/scopes`** (scope claim, including `open`-mode auto-claim
  inside `CreatePackage`).
- **`PUT /api/v1/packages/{scope}/{name}`** with `CreatePackage` semantics
  (first publish creates the package row).

The implementation pattern is **insert-then-handle-unique-violation**, never
check-then-act. Each table carries a DB-level `UNIQUE` constraint on its
namespace key (`scopes.name`, `packages.(scope, name)`), the service issues
the `INSERT`, and on `UniqueConstraintViolation` (EF `DbUpdateException`
inspecting the inner exception's sqlite error code 19 / PG 23505) it
translates to either `409` (claim) or the existing-package code path
(`CreatePackage` collapses into `PublishVersion`).

This closes the TOCTOU race where two concurrent claim requests both observe
"scope unowned" and both proceed; under check-then-act the loser would
overwrite the winner's row or produce a duplicate-row error surfaced as
`500`. Most operationally relevant under `ScopeOwnershipPolicy=open` where
auto-claim happens implicitly on first publish, and at the
`CreatePackage`/`PublishVersion` split where the first publisher's atomic
insert is the package-creation event.

#### Fail-closed permission resolver + lifecycle cascades

The permission resolver MUST **fail closed** on dangling references. If a
package's owning scope no longer resolves (referential integrity broken by
a since-deleted scope row), or a `PackageRoleEntry` references an
org/team/user that has been deleted, the grant path **yields no access** —
treated as absent rather than throwing or granting. The user-visible result
is the same as if the role had never been assigned. This is the only safe
default: a grant edge that points to a missing vertex must not be readable
as a permissive null.

Carrying forward the `registry-scope-foundation` invariant: **refusing to
delete a scope or org while it still owns packages or sub-resources** is a
hard `409`. Industry parallel: GitHub blocks org deletion while the org
still holds repositories.

- `DELETE /api/v1/scopes/{scope}` while the scope owns ≥1 package → `409
  ScopeNotEmpty`.
- `DELETE /api/v1/orgs/{org}` while the org owns ≥1 scope or package → `409
  OrgNotEmpty`.

Together these two rules mean dangling references are rare in practice
(deletion is refused upstream) AND uniformly handled if they do appear
(grant treated as absent).

#### JTI / token revocation preserved at the auth layer

The currently-shipped JTI revocation check (the DB lookup that drives the
`context.Fail("Token has been revoked")` branch in `Startup.cs`'s
`JwtBearer.OnTokenValidated`) is **preserved verbatim** in the new auth
pipeline. A revoked token MUST be rejected at the **authentication layer**,
before the PDP runs, mapping to `AuthzDenyReason.TokenRevoked` (already
present in the enum) when surfaced. The check still consults the
`tokens.revoked_at` column on every authenticated request.

Rationale: with `MaxTokenLifetime=90d` (D17), the JTI revocation list is
the **only** mechanism that can kill a leaked or rotated token before its
natural expiry. Removing or weakening the check would re-open a 90-day
window where compromised credentials cannot be invalidated. It MUST stay
at the auth-layer, not the PDP, so it gates every endpoint including those
classified `[PublicEndpoint]` (an attacker should not be able to reach
public reads with a known-revoked token either, to keep the audit signal
clean).

#### Visibility folded into the PDP

`CanReadPackageAsync` is **deleted**. Visibility resolves inside the PDP:

- `public`: ALLOW for any principal (including anonymous).
- `private`: principal must be authenticated AND PDP role ≥ `reader`. PDP
  returns `VisibilityHidden`; controller translates to **404** (not 403) to
  avoid existence leak.
- `internal`: ALLOW for any authenticated member of the owning org
  (org-owned scope) OR the owning user (user-owned scope) OR any principal
  with PDP role ≥ `reader`.

### Semantics

- **Bug A closure**: PDP `CreatePackage` denies (`ScopeNotOwned` or
  `ScopeReserved`) under every `ScopeOwnershipPolicy` setting for any
  principal that does not own the target scope.
- **Bug B closure**: `PackageService.PublishAsync` rejects `400
  ManifestRouteMismatch` when `manifest.Name` OR `manifest.Version`
  disagrees with the authoritative `PackageResource`/intended version
  derived from the route. The manifest fields never determine a write key.
- **Role matrix live**: `publisher` (direct/team/org-inherited) can
  `PublishVersion`; `maintainer` can `UnpublishVersion`/`DeprecatePackage`;
  `reader` can `ReadPackageMetadata` on private content.
- **Admin narrow** (Q2): `role == admin` short-circuits the **role**
  dimension to effective `owner`; the **ceiling check still runs first**,
  so an admin + `read`-ceiling token is denied any write.
- **Default-deny meta-test**: build-failing on any unclassified controller
  action.
- **Token semantics at login**: `read`-ceiling. Publish requires an
  explicit `auth/tokens` call with `ceiling=publish`.
- **Token lifetime cap** (Q8): `auth/tokens` rejects `expires_in` over
  `Security.MaxTokenLifetime` (default 90d).
- **Scope claim under `verified`**: challenge endpoint returns documented
  shape; verify endpoint returns documented shape. DNS/HTTP resolver is
  stubbed `501 NotImplemented` this phase (Q4).
- **Manifest version mismatch** (Q5): publish whose `manifest.Version`
  disagrees with the route-intended version is rejected
  `400 ManifestRouteMismatch`. Route stays unversioned this phase.
- **Role revocation**: `DELETE /api/v1/packages/{scope}/{name}/roles`
  (owner-gated) and `DELETE /api/v1/admin/packages/{scope}/{name}/roles`
  (admin-gated) return `204` on success; `404` for missing package/role;
  `409` if the revoke would drop the package's owner count to zero
  ("cannot remove the last owner of a package").
- **Atomic namespace allocation**: scope-claim and first-publish
  package-create are insert-then-handle-unique-violation. A duplicate
  concurrent claim resolves to exactly one winner; the loser receives
  `409`. A duplicate concurrent first-publish resolves to exactly one
  `CreatePackage` and the second collapses to `PublishVersion` semantics
  against the now-existing package row.
- **Audit logging**: every state-mutating allow and every authenticated
  deny emits exactly one audit entry; anonymous public-read denials are
  deliberately excluded.
- **Fail-closed cascades**: dangling role/scope/org references yield no
  access. Deleting a scope or org that still owns packages/sub-resources
  is refused `409 ScopeNotEmpty` / `409 OrgNotEmpty`.
- **JTI revocation preserved**: a token whose row carries `revoked_at`
  is rejected at the authentication layer (`401`) before the PDP runs,
  on every endpoint including `[PublicEndpoint]`.

### Implementation Path

```
PDP core + closed (Action, Resource, Principal) model + permission resolver
    (Stash.Registry/Auth/Authorization/* — IRegistryAuthorizer,
     RegistryAction, ResourceRef, Principal, AuthzDecision,
     IPermissionResolver, ScopeOwnershipPolicyKind)
    Folds CanReadPackageAsync logic into the resolver.
        ↓
Default-deny meta-test + [PublicEndpoint] attribute + annotate-all
    (AuthzCoverageMetaTests, PublicEndpointAttribute, AND all six
     controllers updated so the meta-test passes at end of this phase —
     no [AllowAnonymous] survives)
        ↓
Package-write endpoint migration — route-authoritative, split actions
    (PackagesController writes go through IRegistryAuthorizer with
     PackageRoute.From(scope, name); PackageService.PublishAsync takes a
     PackageResource and rejects manifest/route mismatch for BOTH name and
     version; role-matrix conformance wired for write endpoints;
     DELETE .../roles owner-revoke + DELETE /admin/.../roles admin-revoke
     land with last-owner 409 invariant; CreatePackage uses
     insert-then-handle-unique-violation atop the packages UNIQUE
     constraint; AuditService entries on every mutation and authenticated
     deny)
        ↓
Org / Scope / Team / Admin migration + ScopeOwnershipPolicy + verified-stub
    (Organizations/Scopes/Admin controllers swap policies for PDP calls;
     ScopeOwnershipPolicyKind added to SecurityConfig + appsettings.json;
     claim gauntlet under PDP ClaimScope; verified-mode challenge/verify
     endpoints return documented shapes with 501 resolver stub;
     scope-claim uses insert-then-handle-unique-violation; non-empty
     scope/org deletion refused 409; permission resolver fails closed on
     dangling org/team/scope references)
        ↓
Token issuance — least-privilege default + MaxTokenLifetime cap
    (AuthController.Login mints a read-only token by default;
     POST /api/v1/auth/tokens accepts {name, ceiling, expires_in} —
     NO capabilities array; Security.MaxTokenLifetime caps expires_in;
     JwtTokenService emits the new claim shape;
     coarse ceiling stored in existing TokenRecord.Scope column;
     JTI revocation check preserved verbatim in JwtBearer.OnTokenValidated
     — revoked tokens rejected 401 at the auth layer before the PDP runs)
        ↓
Dedicated authorization-conformance test phase (Q7)
    (RegistryAuthzMatrixTests — table-driven cross-product of
     ceiling × role × visibility × policy, every cell ALLOW + DENY,
     including admin-narrow intersections and Bug A/B regression cells)
        ↓
Docs + example + cleanup
    (docs/Registry — Package Registry.md updated for the PDP model,
     ScopeOwnershipPolicy, MaxTokenLifetime, coarse ceiling;
     one canonical example flow added under examples/registry/;
     dead-code sweep: HasPackagePermissionAsync, CanReadPackageAsync,
     RequireReadScope/RequirePublishScope/RequireAdminScope/RequireAdmin)
```

A passing build with controllers still calling `HasPackagePermissionAsync`
directly is an incomplete phase.

### Bounded-domain auth strings — standing constraint (added 2026-05-30)

Every remaining phase that touches `Stash.Registry/` production code must keep
the registry free of **unbounded magic strings** for auth domains. The
single source of truth is `Stash.Registry/Auth/RegistryAuthConstants.cs`:
`RegistryClaims` (claim names), `TokenScopes` / `UserRoles` / `PackageRoles` /
`OrgRoles` (wire role values), `PrincipalTypes`, `ScopeOwnerTypes`,
`ReservedScopes`, and `AuthPolicies` (policy names) — plus `TokenCeilingConverter`,
the **one** place wire strings are parsed into `TokenCeiling` (at the
`BuildPrincipal` boundary). Reference these named members; never inline the
literal at a use site.

Enforcement is `NoMagicAuthStringsMetaTests` (a Roslyn syntax-tree gate): it
fails the build if a bare string literal reaches an auth sink
(`IsInRole` / `FindFirstValue` / `FindFirst` / `HasClaim` / `RequireClaim` /
`RequireRole`). It is wired into the per-phase `verify` of P4, P5, and P7 so a
reintroduced literal fails *that* phase's commit, not `/done`.

**Coverage boundary (be honest, do not over-apply).** The gate scans
**production source only**. Test data is exempt by design — per the project-wide
rule in root `CLAUDE.md`, free-form and fixture/expected-output values are not
bounded domains. P6's `AuthzMatrixData` legitimately uses literal
`"owner"`/`"publish"`/`"private"` values: keeping the matrix's expectations
independent of the very constants under test is a feature, not a violation. Do
**not** force `RegistryAuthConstants` members into the matrix data. Comparison
domains a sink-scan cannot catch (e.g. `role == "owner"`) are guarded by
*centralization* (one `PackageRoles.RankOrder`), with promotion to real enums +
EF value converters as the future 100% hardening.

## Acceptance Criteria

End-to-end behaviors that prove the feature works:

- **Bug A — squat denied (all policies)**: With
  `ScopeOwnershipPolicy=claim` (default), `mallory` PUTting
  `/api/v1/packages/bigcorp/utils` (no prior `@bigcorp/utils`) receives
  `403 ScopeNotOwned`; package NOT created. With `open`, the same call on
  an *unowned* `@bigcorp` succeeds AND `@bigcorp` is auto-claimed; if
  `@bigcorp` is already owned by `alice`, the call is denied
  `403 ScopeNotOwned`. With `verified`, denied with the verification
  message.
- **Bug A — reserved scopes**: `PUT /api/v1/packages/stash/anything` and
  `/api/v1/packages/admin/anything` denied `403 ScopeReserved` under every
  policy, even for `admin`-role users.
- **Bug B — manifest/route NAME mismatch**: PUT to
  `/api/v1/packages/mallory/lib` with manifest naming `@alice/lib@9.9.9` →
  `400 ManifestRouteMismatch`; `@alice/lib` unchanged.
- **Bug B — manifest/route VERSION mismatch (Q5)**: PUT whose
  `manifest.Version` disagrees with the intended version → `400
  ManifestRouteMismatch`; no version row created.
- **Role matrix live — publisher / maintainer / reader**: each role can
  perform its matrix-permitted actions; each is denied (with the
  documented typed reason) for actions above its matrix level.
- **Admin-narrow intersection (Q2)**: `alice` with `role=admin` holding a
  `read`-ceiling token PUTting `/api/v1/packages/foo/bar` → `403
  TokenScopeInsufficient` (ceiling first). Same `alice` with a
  `publish`-ceiling token on a package she has NO direct role on →
  ALLOW (admin short-circuits the resource step). `alice` with `read`
  ceiling reading a private package she has no direct role on → ALLOW
  (read ceiling sufficient, admin resolves visibility).
- **Default-deny meta-test passes**: `AuthzCoverageMetaTests` green;
  removing the `[PublicEndpoint]`/`[Authorize]` from any real controller
  action fails the test; the fixture-controller test enumerates the
  exact unclassified endpoint name.
- **Default login token is `read`-ceiling**: login response's access token
  carries `"token_scope":"read"`; using it to PUT a package returns
  `403 TokenScopeInsufficient`. Issuing a `publish`-ceiling token via
  `auth/tokens` then permits publish.
- **Token lifetime cap (Q8)**: `POST /api/v1/auth/tokens` with
  `expires_in: "365d"` and `MaxTokenLifetime=90d` returns `400
  TokenLifetimeExceeded`; with `expires_in: "30d"` succeeds.
- **Role revoke — owner happy/fail**: `alice` (owner) DELETEs `bob`'s
  `publisher` role on `@alice/widgets` → `204`; the role row is gone and a
  subsequent `PublishVersion` by `bob` denies `403
  PackageRoleInsufficient`. `mallory` (no role) DELETEing any role on
  `@alice/widgets` → `403 PackageRoleInsufficient`.
- **Role revoke — last-owner protection**: `alice` (sole owner) DELETEs
  her OWN `owner` role on `@alice/widgets` → `409` "cannot remove the
  last owner of a package"; the role row is unchanged. After granting
  `bob` `owner`, the same revoke succeeds `204`.
- **Role revoke — admin override**: admin `root` calls `DELETE
  /api/v1/admin/packages/alice/widgets/roles` removing `bob` → `204`
  even when `root` holds no direct role on the package; the same
  last-owner invariant applies (admin cannot delete the last owner).
- **Audit — mutation success**: a successful role assign emits exactly
  one audit entry with action `role.assign`, principal id, and resource;
  a successful role revoke emits exactly one `role.revoke`; a successful
  publish emits exactly one `package.publish`.
- **Audit — authenticated deny**: an authenticated `bob` (no role)
  PUTting `/api/v1/packages/alice/widgets` produces exactly one audit
  entry with decision `deny` and reason `PackageRoleInsufficient`.
- **Audit — anonymous deny excluded**: an anonymous GET on a private
  package returning `404 VisibilityHidden` produces **zero** audit
  entries.
- **Atomic claim race**: two concurrent `POST /api/v1/scopes
  {name: "acme"}` requests from different principals — exactly one
  receives `201` and owns `@acme`; the loser receives `409`. Verified by
  a parallel-execution test issuing N concurrent requests and asserting
  exactly one DB row + exactly N-1 `409` responses.
- **Atomic first-publish race**: two concurrent first-publish requests
  to `/api/v1/packages/acme/widgets` (with caller owning `@acme`)
  resolve to exactly one `packages` row; one publish is the
  `CreatePackage` winner, the other becomes a `PublishVersion` on the
  already-created row (or rejects on version collision) — never a
  second `packages` row, never a `500`.
- **Fail-closed cascade — dangling role**: after manually deleting the
  team referenced by an inherited grant, the user's effective role on
  the package resolves to "no access"; PDP returns the appropriate
  `PackageRoleInsufficient` / `VisibilityHidden`, never throws.
- **Cascade refusal — non-empty scope/org**: `DELETE
  /api/v1/scopes/acme` while `@acme/widgets` exists → `409
  ScopeNotEmpty`; `DELETE /api/v1/orgs/acme` while `@acme` exists →
  `409 OrgNotEmpty`. After deleting the children, both deletes succeed.
- **JTI revocation preserved (P5)**: after `RevokeOwnToken` on token
  `T`, a subsequent request bearing `T` is rejected `401` even though
  `T`'s `exp` is still in the future; the response reason maps to
  `TokenRevoked`. A request bearing `T` to a `[PublicEndpoint]` is also
  rejected `401` (auth-layer gate is uniform).
- **`verified` claim flow scaffolds (Q4)**: `POST /api/v1/scopes` with
  `verification_method: "dns-txt"` returns 201 with the documented
  `challenge` body; `POST /api/v1/scopes/{scope}/verify` exists and
  returns `501 NotImplemented` (resolver is stubbed this phase).
- **PDP treats pending verified scope as unowned**: under `verified`,
  publishing into a `state=pending` scope is denied with the verification
  message (the same branch as "scope does not exist").
- **Visibility folded — no second read path**:
  `PackagesController.CanReadPackageAsync` deleted; `grep` returns no
  matches; private packages still 404 to unauthorized callers.
- **Inert role artifacts gone**: `grep -rn
  "HasPackagePermissionAsync(.*\"owner\"" Stash.Registry/Controllers/`
  returns no matches.
- **Authorization conformance suite (Q7)**: `RegistryAuthzMatrixTests`
  exercises the FULL cross-product `ceiling × role × visibility × policy`
  as a single table-driven suite. Every cell asserts BOTH the allow path
  and the corresponding deny path with the typed `AuthzDenyReason`. The
  suite also covers: role-revoke happy path, non-owner revoke denied,
  last-owner revoke `409`, admin-revoke override, duplicate-concurrent
  claim resolves to one winner with `409` for the loser, duplicate
  concurrent first-publish collapses to one create, dangling-role grant
  yields no access (fail closed), non-empty scope/org deletion refused
  `409`, audit entry emitted on every authorized mutation, audit deny
  entry emitted on every authenticated deny (and NOT on anonymous
  public-read 404), and a revoked-JTI token is rejected `401` before the
  PDP runs even though the token has not expired.
- **Build green + meta-test family green**: `dotnet build` clean;
  `final_verify` (env-flakies excluded only) green, including
  `AuthzCoverageMetaTests`, `RegistryAuthzMatrixTests`,
  `Wave1ThrowsCoverageTests`, `CompletionSurfaceSnapshotTests`,
  `StandardLibraryReferenceTests`, and the per-phase `RegistryAuthz*Tests`.
- **Docs reflect reality**: `docs/Registry — Package Registry.md`
  documents the PDP model, the closed action enum, the role-matrix
  table, the `ScopeOwnershipPolicy` enum + per-policy table, the coarse
  ceiling, and the `MaxTokenLifetime` cap. Fine-grained tokens are noted
  as deferred with a backlog link.

## Phases

The phase list lives in `plan.yaml` so scripts can read it. Each phase
has a concrete `done_when` list there. Summary:

- **P1** PDP core: `(Action, Resource, Principal)` model with coarse
  `TokenCeiling` only, `IRegistryAuthorizer`, `IPermissionResolver`,
  folding `CanReadPackageAsync` logic. No controller wiring yet.
- **P2** Default-deny meta-test + `[PublicEndpoint]` attribute + annotate
  every existing controller action.
- **P3** Package-write endpoint migration: route-authoritative
  `PackageService.PublishAsync` (rejects name AND version mismatch);
  `CreatePackage`/`PublishVersion` split; role matrix wired for write
  endpoints; closes Bug A and Bug B. **Also**: role-revoke endpoints
  (owner + admin) with last-owner `409` invariant;
  insert-then-handle-unique-violation for `CreatePackage`;
  `AuditService` entries on every mutation and authenticated deny.
- **P4** Org / Scope / Admin endpoint migration + `ScopeOwnershipPolicy`
  enum + claim gauntlet under PDP + `verified` challenge/verify endpoints
  scaffolded (resolver stubbed `501`). **Also**:
  insert-then-handle-unique-violation for scope-claim; non-empty
  scope/org deletion refused `409`; permission resolver fails closed on
  dangling references.
- **P5** Token issuance: login emits `read`-only by default; coarse
  ceiling stored in existing `TokenRecord.Scope` column; `MaxTokenLifetime`
  cap enforced; admin-narrow ceiling-first intersection. **Also**: JTI
  revocation check preserved verbatim — revoked tokens rejected `401` at
  the auth layer before the PDP runs.
- **P6** **Dedicated authorization conformance phase (Q7)**:
  `RegistryAuthzMatrixTests` — table-driven cross-product of `ceiling ×
  role × visibility × policy`, every cell ALLOW + DENY, including
  Bug A/B regression cells, admin-narrow intersection cells,
  role-revoke (happy, non-owner deny, last-owner `409`, admin override),
  duplicate-concurrent claim and first-publish races, fail-closed
  cascade cells (dangling role grants nothing; non-empty scope/org
  deletion refused), audit-emission assertions (mutation success +
  authenticated deny emit one entry; anonymous-deny emits zero), and
  the JTI-revocation `401` regression.
- **P7** **Complete the AdminController PDP migration** (inserted
  2026-05-30). The P3/P4/P5 controller swaps finished Packages/Scopes/Orgs
  — every endpoint there calls `AuthorizeAsync`, so their
  `[Authorize(Policy=…)]` attributes became redundant — but left **five
  AdminController endpoints** (`GetStats`, `CreateUser`, `DeleteUser`,
  `AdminAssignRole`, `GetAuditLog`) gated *only* by the class-level
  `[Authorize(Policy=RequireAdmin)]`; they never reach the PDP. P4's
  `done_when` claimed full admin migration but its check only grepped for
  `HasPackagePermissionAsync`, so the gap passed green. This phase wires
  those five endpoints to `IRegistryAuthorizer` (actions `ReadAdminStats`,
  `ManageUser`, `AdminAssignPackageRole`, `ReadAuditLog` — already fully
  handled by the resolver; `AdminResource()` for the global ones), **adds
  the missing non-admin → `403` regression guards** (only `/admin/stats`
  had one — so a naive attribute strip would have *silently* opened
  `/admin/users`, `/admin/.../roles`, `/admin/audit-log` to any
  authenticated user), and strips the 10 residual redundant policy
  attributes (1 Admin class + 3 Scopes + 6 Orgs) to bare `[Authorize]`.
  This unblocks the cleanup phase: only after it does `AuthPolicies` have
  no remaining referent.
- **P8** Docs + example + cleanup (delete `HasPackagePermissionAsync` /
  `CanReadPackageAsync`, the four string policies, and the now-dead
  `AuthPolicies` class). Was P7; renumbered when P7 was inserted.

## Open Questions

- **Q5 — Route-versioned PUT (`PUT /packages/{scope}/{name}/{version}`).**
  Deferred. P3 extends `ManifestRouteMismatch` to cover the version field
  (per resolution above), which closes the security-relevant cases.
  Switching to a versioned route is an ergonomics/contract decision and
  requires CLI churn.
- **`SecurityConfig.MaxTokenLifetime` minimum-expiry floor.** Cap is
  resolved (Q8 → 90d default). A minimum-expiry floor is not specified
  this phase; deferred until it bites.

(Resolved questions, lifted into the design above for reference: Q2 admin
narrowed to resource-side only; Q4 verified-resolver stubbed `501`; Q7
dedicated conformance phase; Q8 `MaxTokenLifetime=90d`.)

## Decision Log

| Date       | Decision                                                                                                       | Rationale                                                                                          |
| ---------- | -------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| 2026-05-30 | D1 — Single PDP for every authz decision; reads and writes share the engine.                                   | One place to ask the question; ad-hoc scatter is what let Bugs A/B exist.                          |
| 2026-05-30 | D2 — Default-deny enforced by a build-failing meta-test (`AuthzCoverageMetaTests`).                            | Makes the gap un-reintroducible; same enforcement idiom as `Wave1ThrowsCoverageTests`.             |
| 2026-05-30 | D3 — Route-authoritative single identity for package writes; manifest name AND version are redundancy checks.  | Structurally closes Bug B; manifest can never drive a write key or version row.                    |
| 2026-05-30 | D4 — One resolver for reads and writes; fold `CanReadPackageAsync` into the PDP.                               | The one currently-correct path joins the engine, not parallel to it.                               |
| 2026-05-30 | D5 — Token ceiling is intersected with resource permissions inside the PDP, ceiling-first.                     | Composing the two axes anywhere else is what produced the inert role matrix.                       |
| 2026-05-30 | D6 — Stop auto-granting `publish` at login; default access token is `read`-ceiling.                            | Blast radius of any new bug shrinks from "every account" to "every account that intentionally elevated." |
| 2026-05-30 | D7 — Split `CreatePackage` (scope-ownership-gated) from `PublishVersion` (package-role-gated).                 | Precise structural fix for Bug A; mirrors NuGet "Push" vs "Push only new package versions."        |
| 2026-05-30 | D8 — **Token axis is the coarse ceiling only this phase** (`read`/`publish`/`admin`, no auto-grant at login); fine-grained per-package rules deferred. | User found three-concept token model (ceiling + capability rules + IsUnrestricted) confusing. Coarse ceiling already satisfies the least-privilege guarantee (D6); fine-grained rules are purely additive and reopen no security bug. Deferred to backlog stub. |
| 2026-05-30 | D9 — `Principal` abstraction designed to admit a future `OidcPrincipal` (Trusted Publishing) without rework.   | All major registries are moving to ephemeral OIDC identities; don't preclude it.                   |
| 2026-05-30 | D10 — `Security.ScopeOwnershipPolicy` enum (`open` \| `claim` \| `verified`, default `claim`) gating only the unclaimed-scope branch of `CreatePackage`; reserved/someone-else-owned scopes denied under every setting; `open` auto-claims unowned (not "skip the check"). | Matches npm (claim) / crates (open auto-claim) / Maven Central (verified) and avoids re-creating Bug A. |
| 2026-05-30 | D11 — Hard clean break: old `TokenRecord.Scope` shape and four `RequireXxxScope` policies replaced outright; coarse ceiling **reuses** the existing `TokenRecord.Scope` column (no new column). | Explicit user directive; pre-1.0; shim would be load-bearing dead code. Reusing the column avoids a needless schema delta for the coarse ceiling. |
| 2026-05-30 | D12 — `[AllowAnonymous]` is forbidden going forward; replaced everywhere by `[PublicEndpoint("reason")]`.      | The meta-test needs a single, intentional public marker.                                           |
| 2026-05-30 | D13 — `final_verify` filter keeps stdlib enforcement meta-tests AND `AuthzCoverageMetaTests` AND `RegistryAuthzMatrixTests`; only documented env-flakies excluded. | Authz feature must not weaken the build gate.                                                      |
| 2026-05-30 | D14 — Admin role short-circuits ONLY the resource-side dimension to effective `owner`; the ceiling check still runs first. | Narrows the shipped `IsInRole("admin")` blanket bypass. An admin holding a `read` token cannot write. Q2 resolution. |
| 2026-05-30 | D15 — `verified` mode ships the normative challenge/verify wire shapes; the DNS-TXT / HTTP-well-known **resolver is stubbed `501 NotImplemented`** this phase. | Q4 resolution. Wire contract is what unblocks the CLI and clients; the resolver is additive and can land without re-opening any earlier decision. |
| 2026-05-30 | D16 — Dedicated authorization-conformance test phase owns the full cross-product `ceiling × role × visibility × policy` in a single table-driven suite. | Q7 resolution. Folding into P3 hid the matrix's intersections (especially the admin-narrow cells); a dedicated phase makes coverage observable. |
| 2026-05-30 | D17 — `Security.MaxTokenLifetime` (default `90d`) enforced server-side at `auth/tokens`. | Q8 resolution. Prevents long-lived tokens from undoing the least-privilege guarantee in D6.       |
| 2026-05-30 | D18 — Package role REVOCATION (owner-path `DELETE /api/v1/packages/{scope}/{name}/roles` and admin-path `DELETE /api/v1/admin/packages/{scope}/{name}/roles`) is owned by **this feature**, not `pkg-cli-api-parity`. Both endpoints enforce a hard last-owner invariant: a revoke that would drop the package's owner count to zero is refused `409`. | Gap #1. `pkg-cli-api-parity` is being de-prioritized and its P1 (server-side role revoke) is absorbed here so the registry server ships role mutation symmetrically. Last-owner protection follows GitHub (last-admin block) and npm (≥1 owner required). |
| 2026-05-30 | D19 — Audit logging covers two categories: every state-mutating authorized action AND every authenticated authorization deny (`AuthzDenyReason` recorded verbatim). Anonymous public-read denials are **explicitly excluded** so the audit table doesn't drown in 404 traffic. | Gap #2. Stated as a conscious scoping decision rather than an omission — reopening "audit anonymous denies" is a future follow-up, not a bug. Mutation entries enable forensic replay; authenticated-deny entries feed intrusion detection on abnormal deny rates per principal. |
| 2026-05-30 | D20 — Namespace uniqueness (scopes, packages) is arbitrated by DB `UNIQUE` constraints with **insert-then-handle-unique-violation**, never check-then-act. A duplicate concurrent claim or first-publish resolves to exactly one winner; the loser receives `409` (claim) or collapses to `PublishVersion` (publish). | Gap #3. Closes the TOCTOU race that existed under `ScopeOwnershipPolicy=open` auto-claim and at the `CreatePackage`/`PublishVersion` split. Application-level "is this unowned?" checks cannot be racy-safe; only the DB unique constraint can. |
| 2026-05-30 | D21 — Permission resolver **fails closed** on dangling references (role/team/org/scope pointing at a deleted vertex grants no access). Concurrently, deleting a scope or org that still owns packages/sub-resources is refused `409 ScopeNotEmpty` / `409 OrgNotEmpty`. | Gap #4. Two-sided invariant: cascading deletes are refused so dangling references should not arise; if they do (manual DB intervention, race), the resolver treats them as absent grants rather than throwing or accidentally granting. Carries forward the `registry-scope-foundation` non-empty deletion invariant. |
| 2026-05-30 | D22 — The currently-shipped JTI / `tokens.revoked_at` revocation check is **preserved verbatim** in the new auth pipeline, at the authentication layer (`JwtBearer.OnTokenValidated`), running **before** the PDP on every endpoint including `[PublicEndpoint]`. Surfaces as `AuthzDenyReason.TokenRevoked`. | Gap #5. With `MaxTokenLifetime=90d` (D17) the JTI revocation list is the only mechanism that can kill a leaked or rotated token before its natural expiry; removing it would re-open a 90-day uninvalidatable window. Gating uniformly at the auth layer keeps revoked tokens from reaching even public reads, preserving a clean intrusion-detection signal. |
