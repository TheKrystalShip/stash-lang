# RFC: Registry P1 — Organizations, Scopes, and Visibility Foundation

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-05-29
> **Slug:** registry-scope-foundation

## Summary

This RFC introduces the foundational identity, ownership, and visibility primitives
for the Stash self-hosted registry: **organizations**, **teams**, **package-level
roles**, **mandatory `@scope/name` package names** with a polymorphic
`user|org` scope owner, **package visibility** (`public`/`private`/`internal`),
and the **`RequireReadScope`** authorization policy. It is the post-decision
**Phase 1 (P1)** of the revised registry roadmap.

Three behavior reversals are inseparable from this change and ship together as
one logical break (per decisions D3, D7, D20):

1. Flat, unscoped package names (`foo`) stop being a legal manifest name.
   `IsValidPackageName` rejects them at both the CLI and the registry.
2. The publish-time 403 on `"private": true` (today in
   `Stash.Registry/Services/PrivatePackageException.cs` /
   `PackageService.cs:61-68`) is **removed**; the manifest `private` flag is
   replaced by a server-side `visibility` field on the package record, gated on
   read.
3. Every flat `/api/v1/packages/{name}` route is replaced in place by
   `/api/v1/packages/{scope}/{name}`. There is no parallel old contract, no
   `/api/v2`, and no resolver shim — pre-scope name references simply stop
   resolving.

There is **no data migration**: existing flat-named registry data does not carry
forward (D3 clean break). Operators republish under scopes.

## Motivation

Today the registry has:

- A flat `(PackageName, Username)` ownership join (`Database/Models/OwnerEntity.cs`).
- An inert `read` JWT scope: `TokenRecord.Scope` accepts `"read"`, but
  `Startup.cs` only defines `RequirePublishScope`/`RequireAdminScope`/
  `RequireAdmin` — there is no `RequireReadScope` and every package read endpoint
  is `[AllowAnonymous]`.
- A publish-time rejection of any manifest with `"private": true` (HTTP 403).
- A package-name grammar that accepts both `foo` and `@scope/foo`.
- Routes that take a single `{name}` path segment.

This is too coarse for a self-hosted registry shared across teams, and the
"private" knob is a publishing trap rather than a working access-control feature.
P1 is the largest and riskiest block in the new roadmap; per D5, the org/role
schema, scoped names, visibility, and the `read` policy all migrate together
in one schema cut and one route cut so we never re-path or re-auth later.

## Goals

- Replace the flat `OwnerEntry` join with an org/team/role model and a
  polymorphic `scope_owner = user | org` identity, in one EF schema cut.
- Make `@scope/name` the **only** legal Stash package name (D1).
- Auto-provision `@username` personal scopes at user registration; reserve
  `@stash` and `@admin` system scopes at server bootstrap (D2, D19).
- Add the `package.visibility` column (`public`/`private`/`internal`) and
  enforce it on every read surface: metadata, version metadata, download,
  search (D5, D6).
- Add the missing `RequireReadScope` authorization policy and apply it to all
  read endpoints (D6).
- Remove the publish-time `"private": true` 403 and the
  `PrivatePackageException` path; visibility is a server-side state, not a
  manifest assertion (D7).
- Replace every `/packages/{name}` route in `PackagesController` and
  `AdminController` with `/packages/{scope}/{name}`; update every CLI call site
  in `Stash.Cli/PackageManager/RegistryClient.cs` to match (D20).
- Define and implement the role -> permission matrix
  (`owner`/`maintainer`/`publisher`/`reader`) and the scope
  allocation/claim endpoint shapes.
- Carry through the language-changes checklist items that apply: spec text for
  the package-name grammar, `docs/Registry — Package Registry.md`, an example
  manifest, registry test classes, and the `final_verify` flaky filter.

## Non-Goals

- **No name aliases, no resolver shim, no data migration.** Pre-scope flat names
  hard-break (D3). Operators republish; lockfiles and any cache pointing at
  flat names must be regenerated. No phase in this plan builds a migration or
  alias subsystem.
- **No metrics, no audit v2, no trusted publishing/provenance, no advisories,
  no lifecycle states.** Those are P2-P6 of the revised roadmap and live in
  separate specs.
- **No `/api/v2`.** The v1 contract breaks in place (D20).
- **No multi-owner-tie-break logic.** Moot under the clean break (D3).
- **No transfer workflows.** Section 3's transfer endpoints (`POST
  /packages/{name}/transfer`) are not in P1 scope; deferred to a later card.
- **No stdlib namespace changes.** This feature does not touch
  `Stash.Stdlib`; stdlib enforcement meta-tests
  (`Wave1ThrowsCoverageTests`, `CompletionSurfaceSnapshotTests`,
  `StandardLibraryReferenceTests`) do not apply and are not added to
  `final_verify`.

## Design

### Surface

#### Manifest grammar (`Stash.Core/Common/PackageManifest.cs`)

```
@[a-z][a-z0-9-]{0,38}/[a-z][a-z0-9-]{0,38}
```

- The scope segment and the name segment are each 1-39 chars, start with a
  lowercase letter, and contain only `[a-z0-9-]`. Combined length stays
  under the existing 64-char manifest cap.
- The `LocalPackageName()` regex is **removed** from `PackagingRegexes`.
- `IsValidPackageName` returns `false` for any input without a leading `@` and
  the `/` separator. The validator's error string changes to name only the
  scoped form.

#### Scope namespace (D2, D19)

- Single `scopes` table; one row per scope name, primary key the scope name
  (without the leading `@`).
- Each row has `owner_type ∈ {user, org, system}` and exactly one of
  `owner_username` or `owner_org_id` set (or neither, for system scopes).
- Usernames and org names share one pool: registration of a username or
  org name fails if a scope of the same name already exists. The
  registration controller rejects collisions with HTTP 409.
- Bootstrap reserves `@stash` and `@admin` as `system` scopes; no user or org
  may claim them.

#### Org / team / role schema (D5)

New tables (snake_case columns, EF Core conventions match
`RegistryDbContext.OnModelCreating`):

- `organizations` — `id`, `name` (unique, lower-case, same grammar as scope),
  `display_name`, `created_at`, `created_by`.
- `org_members` — `(org_id, username)` composite key, `org_role ∈ {owner,
  member}`, `joined_at`. Cascade on org delete.
- `teams` — `id`, `org_id` (FK), `name` (unique within org), `created_at`.
- `team_members` — `(team_id, username)` composite key, `joined_at`.
- `scopes` — see above; FK to `organizations.id` when `owner_type='org'`,
  FK to `users.username` when `owner_type='user'`.
- `package_roles` — replaces `owners`: `(package_name, principal_type,
  principal_id, role)` where `principal_type ∈ {user, team, org}` and
  `role ∈ {owner, maintainer, publisher, reader}`.

The `owners` table and `OwnerEntry` C# model are **deleted** (D3 — replaces,
not extends). `IRegistryDatabase.AddOwnerAsync` / `GetOwnersAsync` /
`IsOwnerAsync` / `RemoveOwnerAsync` are replaced by role-aware equivalents:
`AssignPackageRoleAsync`, `GetPackageRolesAsync`,
`HasPackagePermissionAsync(name, username, permission)`,
`RevokePackageRoleAsync`.

#### Role -> permission matrix

| Permission                                            | reader | publisher | maintainer | owner |
| ----------------------------------------------------- | :----: | :-------: | :--------: | :---: |
| Read metadata / versions / download (private package) | yes    | yes       | yes        | yes   |
| Publish new version of existing package               | no     | yes       | yes        | yes   |
| Unpublish version                                     | no     | no        | yes        | yes   |
| Deprecate / undeprecate package or version            | no     | no        | yes        | yes   |
| Change visibility                                     | no     | no        | no         | yes   |
| Assign / revoke package roles                         | no     | no        | no         | yes   |
| Delete package                                        | no     | no        | no         | yes   |

`admin`-role users carry every permission implicitly. The scope owner (user or
the org backing the scope) is auto-assigned `owner` on package creation
during publish.

Permission resolution for a `(package, username)` pair: union of direct user
role + roles via every team the user belongs to + roles via the org owning
the package's scope. Highest-permission rule wins.

#### Package visibility (D5, D7)

`PackageRecord` gains:

- `visibility text NOT NULL DEFAULT 'public'` with `CHECK (visibility IN
  ('public','private','internal'))`.
- New row default for newly-published packages is `public`. Subsequent
  versions inherit the package's current visibility — visibility is a
  package-scope state, not per-version.
- Manifest `private: true` is **removed** from
  `PackageManifest` interpretation. The publish path no longer reads or
  reacts to it. (The field is still tolerated in JSON deserialization for
  forward-compat with older clients but is ignored; an analyzer warning is
  out of scope here.)
- Visibility change endpoint (owner-only):
  `PATCH /api/v1/packages/{scope}/{name}/visibility` with body
  `{ "visibility": "private" }`.

`internal` semantics: visible to authenticated members of the owning org
(when the scope is org-owned) or to the owning user only (when the scope is
user-owned). The visibility check at every read surface resolves to:

- `public`  -> anyone (including unauthenticated callers; no `read` token
  required).
- `private` -> caller has a `read`-scoped token AND has at least the `reader`
  permission on the package.
- `internal` -> caller has a `read`-scoped token AND
  (a) the scope is org-owned AND the caller is a member of that org, OR
  (b) the scope is user-owned AND the caller is the scope owner, OR
  (c) the caller has at least `reader` on the package directly.

#### Authorization policies (`Startup.cs`)

- Add `RequireReadScope` requiring the JWT to carry `token_scope ∈ {read,
  publish, admin}`.
- Read endpoints stay reachable without auth (so public packages still work
  unauthenticated), and do the visibility resolve in the handler. The handler
  returns `404 Not Found` (not `403`) for unauthorized callers on
  private/internal packages so the existence of the package is not leaked.
  When a caller does supply a JWT, the `RequireReadScope` policy is applied
  by the handler before the visibility check.
- Search results filter private/internal packages the caller cannot see.
  Unauthenticated callers get only `public` results.

#### Route migration (D20)

Replace, in place under `/api/v1/`:

| Old                                               | New                                                       |
| ------------------------------------------------- | --------------------------------------------------------- |
| `GET /packages/{name}`                            | `GET /packages/{scope}/{name}`                            |
| `GET /packages/{name}/{version}`                  | `GET /packages/{scope}/{name}/{version}`                  |
| `GET /packages/{name}/{version}/download`         | `GET /packages/{scope}/{name}/{version}/download`         |
| `PUT /packages/{name}`                            | `PUT /packages/{scope}/{name}`                            |
| `DELETE /packages/{name}/{version}`               | `DELETE /packages/{scope}/{name}/{version}`               |
| `PATCH /packages/{name}/deprecate`                | `PATCH /packages/{scope}/{name}/deprecate`                |
| `DELETE /packages/{name}/deprecate`               | `DELETE /packages/{scope}/{name}/deprecate`               |
| `PATCH /packages/{name}/{version}/deprecate`      | `PATCH /packages/{scope}/{name}/{version}/deprecate`      |
| `DELETE /packages/{name}/{version}/deprecate`     | `DELETE /packages/{scope}/{name}/{version}/deprecate`     |
| `PUT /admin/packages/{name}/owners`               | `PUT /admin/packages/{scope}/{name}/roles`                |

URL form: the `@` of `@scope/name` is **not** part of the URL path. The
`{scope}` route token is the bare scope name (e.g. `@stash/foo` ->
`/packages/stash/foo`). CLI helper `EncodePackageName` is replaced by
`SplitScopedName(name) -> (scope, localName)` plus per-segment URL encoding.
The server canonicalizes back to `@{scope}/{name}` for response bodies and
DB lookups.

#### New endpoints

- `POST   /api/v1/orgs` — create org (any authenticated user with a
  `publish`+ scoped token; creator becomes `owner` of the org and the org's
  scope). Body: `{ "name": "...", "display_name": "..." }`. 201/409.
- `GET    /api/v1/orgs/{org}` — public org metadata.
- `POST   /api/v1/orgs/{org}/members` — owner-only; body `{ "username":
  "...", "org_role": "member" }`.
- `DELETE /api/v1/orgs/{org}/members/{username}` — owner-only.
- `POST   /api/v1/orgs/{org}/teams` — owner-only.
- `POST   /api/v1/orgs/{org}/teams/{team}/members` — owner-only.
- `GET    /api/v1/scopes/{scope}` — resolve a scope to its owner shape
  `{ "scope": "stash", "owner_type": "system" | "user" | "org",
    "owner": "<username|org-id|null>" }`.
- `POST   /api/v1/scopes` — claim a new scope (owner = caller's user, or
  caller's org for which they are `owner`). Body `{ "scope": "...",
  "owner_type": "user" | "org", "owner": "<id-or-username>" }`. Rejects
  collisions with usernames, org names, existing scopes, and reserved system
  scopes.
- `PATCH  /api/v1/packages/{scope}/{name}/visibility` — owner-only; body
  `{ "visibility": "public" | "private" | "internal" }`.
- `PUT    /api/v1/packages/{scope}/{name}/roles` — owner-only role
  assignment.
- `GET    /api/v1/packages/{scope}/{name}/roles` — list package roles
  (owner-only).

#### Bootstrap behavior

`Stash.Registry/Bootstrap/AdminBootstrapper.cs` is extended to:

1. Seed the `@stash` and `@admin` system scopes if they do not exist.
2. Auto-provision a `@<username>` scope for the bootstrap admin user.

`AuthController.Register` is extended to auto-provision `@<username>` for
every newly registered user inside the same transaction, and to fail
registration if the username collides with an existing scope (user, org, or
system).

### Semantics

- **Manifest validation:** a manifest with a flat (unscoped) name fails
  `IsValidPackageName` and `ValidateForPublishing`. The CLI rejects the
  manifest before any HTTP call; the registry rejects it again on PUT as a
  defense in depth (400 with an `Invalid package manifest` error).
- **Publish authorization:** the caller must have at least `publisher`
  permission on the package (or be the scope owner, which gets `owner`
  auto-assigned on package creation). The publish-time `private: true`
  check is gone.
- **Visibility enforcement:** for every read surface, the visibility check
  precedes the data lookup return. Private/internal packages return `404 Not
  Found` for unauthorized callers. The `download` endpoint additionally
  rejects unauthorized callers with `404` rather than streaming.
- **Search:** the search SQL is filtered by visibility: when no JWT is
  present, only `public`. When a JWT is present, public union (private the
  caller can read) union (internal the caller can read).
- **Role inheritance:** the org's `owner` members inherit `owner` on every
  package in any scope owned by that org. Org `member` members inherit
  `reader` on private/internal packages in org-owned scopes; explicit higher
  package roles override.
- **Edge case — scope deleted while package exists:** scopes are *not*
  deletable in P1. Org delete cascades only when no packages depend on
  scopes owned by that org; otherwise the call fails 409.

### Implementation Path

```
PackageManifest grammar (mandatory @scope/name, drop flat regex)
    ↓
EF schema cut: orgs + teams + org_members + team_members + scopes
    + package_roles (replaces owners) + packages.visibility
    + IRegistryDatabase methods + RegistryDbContext.OnModelCreating
    ↓
Scope provisioning: AdminBootstrapper seeds @stash/@admin/admin's scope;
    AuthController.Register provisions @<username> + rejects collisions
    ↓
Visibility + RequireReadScope policy:
    Startup.cs adds the policy
    PackagesController applies the visibility check at every read surface,
        deletes PrivatePackageException + PackageService private-flag branch
    SearchController filters by visibility
    PATCH .../visibility endpoint
    ↓
Org / scope / role REST surface:
    OrganizationsController, ScopesController, role endpoints,
    AdminController PUT roles
    ↓
Route migration: PackagesController + AdminController re-pathed to
    /{scope}/{name}; RegistryClient.cs CLI call sites all switched;
    SplitScopedName helper in Stash.Core/Common; example packages updated
    ↓
Docs + example manifest + flaky filter:
    docs/Registry — Package Registry.md updated with the new routes,
    role matrix, scope endpoints
    docs/Stash — Language Specification.md grammar note for @scope/name
    examples/packages/* manifests verified scoped
```

The path must stay end-to-end: every layer that named a package or hit a
route changes in the same logical break. A passing build with a CLI that
still emits flat routes is an incomplete phase.

## Acceptance Criteria

- `PackageManifest.IsValidPackageName("foo")` returns `false`. Publishing a
  manifest with a flat name fails at the CLI before any HTTP call.
- The registry rejects a flat-name PUT with `400 Bad Request` referencing the
  scoped grammar.
- A user registered as `alice` automatically has the `@alice` scope, owned
  by user `alice`, visible via `GET /api/v1/scopes/alice`.
- Registering a username `stash` fails with `409 Conflict` (reserved scope).
- An end-to-end publish + install of `@alice/widget` to a fresh registry
  works: `stash pkg publish` -> `PUT /api/v1/packages/alice/widget` -> the
  package round-trips through `GET /api/v1/packages/alice/widget` and the
  CLI installer reaches it via the scoped route.
- `GET /api/v1/packages/alice/widget` on a `private` package returns 404 to
  an unauthenticated caller, 404 to a caller without `reader`, and 200 to a
  caller with `reader` plus a `read`-scoped JWT.
- The publish flow no longer raises `PrivatePackageException` for a
  manifest with `"private": true`; that source file is deleted from the tree.
- An org `acme` whose owner is user `alice` can own scope `@acme`. A
  publish of `@acme/widget` succeeds when `alice` has `publisher` on
  `@acme/widget` (auto-granted as scope owner) and fails 403 when a
  non-member `bob` tries.
- All flat `/api/v1/packages/{name}` routes 404 on a registry that has
  scoped data. `Stash.Cli/PackageManager/RegistryClient.cs` issues only
  scoped routes — `grep '/packages/{EncodePackageName' RegistryClient.cs`
  returns no matches.
- `docs/Registry — Package Registry.md` documents the scoped grammar, the
  role -> permission matrix, visibility semantics, and the new endpoints.
- `dotnet build` is clean. The new registry test classes
  (`RegistryScopeAndOrgTests`, `RegistryVisibilityTests`,
  `RegistryRoutesTests`) all pass under the `final_verify` filter.

## Phases

The phase list lives in `plan.yaml`. Each phase has a concrete `done_when`
list there. Summary:

- **P1** Mandatory scoped package-name grammar (drop flat names).
- **P2** EF schema cut: orgs/teams/scopes/roles/visibility in one migration;
  delete `OwnerEntry`.
- **P3** Scope provisioning: bootstrap reserves `@stash`/`@admin`;
  registration auto-provisions `@<username>` and rejects collisions.
- **P4** Visibility enforcement + `RequireReadScope`; delete the publish-time
  private 403; visibility PATCH endpoint; search filtering.
- **P5** Org / scope / role REST surface (`OrganizationsController`,
  `ScopesController`, role endpoints).
- **P6** Route migration: all `/packages/{name}` controllers and every CLI
  call site cut over to `/packages/{scope}/{name}`; example packages
  verified.
- **P7** Docs + example + spec text. Updates `docs/Registry — Package
  Registry.md`, language spec grammar note, and one canonical example
  manifest.

## Open Questions

- **Org `owner` succession.** If the last user with `org_role=owner` on an
  org leaves, do we promote a `member` or freeze the org? Proposal: freeze
  (block writes, allow reads) and require an admin RPC to repair. To be
  resolved during P5 implementation.
- **Search visibility predicate cost.** Current `SearchController` does
  string `LIKE` matching against `packages`. Adding a visibility join may
  push us toward a search index column; deferred unless benchmarks hurt.
- **`internal` for user-owned scopes.** Spec says it collapses to "the
  scope-owning user only." Useful enough to ship, or should `internal` be
  allowed only on org-owned scopes? Default to "allow on both" for symmetry;
  revisit if confusing in practice.

## Decision Log

| Date       | Decision (mirrored from authoritative addendum)                                                                       | Rationale                                                                              |
| ---------- | --------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| 2026-05-29 | D1 — Mandatory `@scope/name`; drop flat names.                                                                        | Breaking is acceptable pre-1.0; one grammar everywhere.                                |
| 2026-05-29 | D2 — Polymorphic scope owner (user or org); auto-provision `@<username>`.                                             | One scope namespace, no second-class personal packages.                                |
| 2026-05-29 | D3 — Clean break: no aliases, no resolver shim, no data migration.                                                    | Pre-1.0; avoids building a migration subsystem that has no long-term value.            |
| 2026-05-29 | D5 — Visibility ships **with** orgs in one schema cut.                                                                | Avoid a second migration; visibility-on-user-scope alone would be a throwaway.         |
| 2026-05-29 | D6 — Add `RequireReadScope` policy; enforce on metadata/version/download/search.                                      | Makes the inert `read` JWT scope mean something.                                       |
| 2026-05-29 | D7 — Replace publish-time `"private": true` 403 with the visibility model.                                            | Current behavior is a trap, not access control.                                        |
| 2026-05-29 | D19 — Unified scope namespace; reserve `@stash` / `@admin`; usernames and org names cannot collide with a scope.      | Single resolution table; prevents impersonation-style scope grabs.                     |
| 2026-05-29 | D20 — Break `/api/v1` in place; no `/api/v2`.                                                                         | Operators republish under scopes anyway; parallel contracts add cost without payoff.   |
| 2026-05-29 | P1 plan-time — `internal` is package-level (not per-version); versions inherit the package's visibility.              | Avoids a second visibility column on `versions`.                                       |
| 2026-05-29 | P1 plan-time — Read endpoints return `404` (not `403`) for unauthorized callers on private/internal packages.         | Prevents existence-leak via response code differentiation.                             |
| 2026-05-29 | P1 plan-time — `@` is **not** part of the URL path; `{scope}` segment is the bare scope name.                         | Avoids URL-encoding ambiguity across HTTP clients.                                     |
| 2026-05-29 | P1 plan-time — Visibility is package-scope state; manifest `private` field is dropped from publish semantics.         | The manifest cannot meaningfully assert server-side access control; only the server can. |
| 2026-05-29 | P1 plan-time — No legacy data migration tooling phase. The roadmap-table mention of "legacy migration" is **route** migration only, per D3. | Resolves the conflict between the roadmap table and D3 / the Migration-resolved section. |
| 2026-05-29 | P1 plan-time — Stdlib enforcement meta-tests (`Wave1ThrowsCoverageTests`, `CompletionSurfaceSnapshotTests`, `StandardLibraryReferenceTests`) are **not** added to `final_verify`. | This feature does not touch stdlib namespaces; including them would be wrong-shaped.   |
