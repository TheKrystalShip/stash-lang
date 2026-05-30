# RFC: Stash pkg CLI parity with scoped registry API

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-05-30
> **Slug:** pkg-cli-api-parity

## Summary

The `stash pkg` CLI is missing commands for most of the capabilities the scoped
registry already exposes after `registry-scope-foundation`. The wire layer
(`RegistryClient`) was migrated to scoped routes, but the CLI command surface
still only covers a narrow user-owner flow plus the original install / publish /
deprecate / token set. This feature closes the gap: every registry API
capability gets a first-class `stash pkg <verb>` command. Where the old surface
does not match the new model, it is removed outright — no aliases, no shims, no
backwards compatibility (consistent with the registry's D3 posture).

It also closes the one true server-side gap that blocks role parity end-to-end:
the registry has no HTTP endpoint to revoke a package role, even though the
database method exists (orphaned). This feature adds that endpoint and wires it
through the CLI. Doing so retires the backlog stub
`.kanban/0-backlog/bugs/Package role revocation not exposed over HTTP.md`.

## Motivation

After `registry-scope-foundation` the registry speaks:

- Scoped packages with three visibility tiers (`public` / `internal` / `private`).
- Scope ownership (a user or org claims a scope).
- Organizations with members and teams; teams with members.
- Per-package role grants for **users, teams, and orgs**, with four roles
  (`owner`, `maintainer`, `publisher`, `reader`).

The CLI today only speaks "package owner = user." `stash pkg owner add foo bob`
hard-codes `principal_type=user`, `role=owner`, and `stash pkg owner remove`
throws because the server has no revoke route. There is no way from the CLI to:

- Set or read a package's visibility.
- Claim or inspect a scope.
- Create or inspect an org, manage members, manage teams.
- Grant a non-owner role, or grant a role to a team or org.
- Revoke any role at all (server gap).

End users who want the new model must hit the HTTP API directly. That defeats
the point of shipping the scoped registry.

## Goals

- Full command-surface parity: every registry capability listed in
  `Stash.Registry/CLAUDE.md`'s endpoint table is reachable through a `stash pkg`
  subcommand.
- One coherent CLI grammar for all role management — principal type
  (`user|team|org`) and role (`owner|maintainer|publisher|reader`) are first
  class, not implicit.
- Close the server-side revoke gap with a single new endpoint, wired to the
  existing `RevokePackageRoleAsync` DB method and audited via the existing
  `AuditService.LogRoleRevokeAsync`.
- Retire `.kanban/0-backlog/bugs/Package role revocation not exposed over HTTP.md`.
- Keep `Stash.Cli` Native-AOT-clean: every new request/response DTO is
  registered in `CliJsonContext` with source-gen, no reflection or `Activator`.
- Honor the full language-changes checklist: CLI docs updated, tab completion
  updated, example added, xUnit tests cover every new command at CLI→HTTP
  integration level.

## Non-Goals

- No changes to the authentication/authorization model itself. We reuse the
  existing four policies (`RequireReadScope`, `RequirePublishScope`,
  `RequireAdminScope`, `RequireAdmin`) and put the new revoke route under the
  same policy as the existing assign route (admin).
- No changes to `stash pkg install` / dependency resolution / lockfile shape.
- No regeneration of `docs/Stash — Standard Library Reference.md`; this feature
  does not touch stdlib namespaces. `Stash.Docs` is not run.
- No SDK / library surface — the CLI is the deliverable. `RegistryClient` is
  only extended because the CLI needs it.
- No backwards-compat aliases for the removed `owner` command. Users see
  "unknown subcommand" through normal dispatch.

## Design

Describe the intended end state. Keep this focused on decisions that future
agents must preserve.

### Surface

New `stash pkg` grammar. Verbs marked **new** are added; the `owner` verb is
**removed** outright.

```
# Package visibility (new)
stash pkg visibility get <pkg>
stash pkg visibility set <pkg> <public|internal|private>

# Package roles (new — replaces `stash pkg owner`)
stash pkg role list   <pkg>
stash pkg role assign <pkg> <user|team|org> <principal> <owner|maintainer|publisher|reader>
stash pkg role revoke <pkg> <user|team|org> <principal>

# Scopes (new)
stash pkg scope claim <scope>                  # POST /scopes
stash pkg scope info  <scope>                  # GET  /scopes/{scope}

# Organizations + teams (new)
stash pkg org create        <org>
stash pkg org info          <org>
stash pkg org member add    <org> <username>
stash pkg org member remove <org> <username>
stash pkg org team add      <org> <team>
stash pkg org team member add <org> <team> <username>
```

Verbs preserved unchanged: `init`, `install`/`i`, `uninstall`/`remove`,
`list`/`ls`, `pack`, `update`, `outdated`, `publish`, `search`, `info`,
`login`, `logout`, `whoami`, `unpublish`, `deprecate`, `undeprecate`,
`token`, `help`.

Verb-level dispatch lives in `Stash.Cli/PackageManager/Commands/PackageCommands.cs`.
Each new top-level verb (`visibility`, `role`, `scope`, `org`) gets its own
command class under `Stash.Cli/PackageManager/Commands/`. The existing
`OwnerCommand.cs` is deleted, the dispatch entry for `owner` is removed, and
the `Stash.Cli/Completion/` subcommand list (plus the `PackageCommands` help
text) drops `owner` and gains the four new top-level verbs.

### Semantics

- All new commands require a logged-in registry session except `scope info` and
  `org info` (anonymous GETs).
- `role assign` requires an explicit principal-type argument — no
  default-to-user. Clean grammar, no silent user-vs-team ambiguity.
- `role revoke` does not take a role argument — a principal holds at most one
  role per package, and revoke removes whatever role they hold. This matches
  `RevokePackageRoleAsync`'s DB signature.
- `visibility set` is idempotent: setting `public` on an already-public package
  returns success.
- Error mapping: `401 → "Not logged in. Run 'stash pkg login'."`,
  `403 → "Forbidden ({reason})."`, `404 → "Not found: <resource>."`,
  `409 → "Conflict: <reason>."`.

#### Server: the one new endpoint

Add `DELETE /api/v1/admin/packages/{scope}/{name}/roles` on `AdminController`,
mirroring the existing `PUT /admin/packages/{scope}/{name}/roles` assign route.
Body shape mirrors the assign request — `RevokeRoleRequest { principal_type,
principal_id }` (no `role` field; revocation removes whatever role the
principal holds on that package, which matches `RevokePackageRoleAsync`).

- Auth policy: `RequireAdmin` (same as assign).
- Returns `204 No Content` on success; `404` if package or role entry missing.
- Emits `role.revoke` audit action via `AuditService.LogRoleRevokeAsync`
  (already exists, currently uncalled).

Rationale for body-shape over path-segment shape (option A in the backlog bug):
keeping the principal pair in the body matches the assign route's shape,
sidesteps URL-encoding of arbitrary principal IDs, and gives one canonical
request DTO across assign / revoke. The bug stub recommended path segments — we
deviate consciously, documented here and in the Decision Log.

#### `RegistryClient` additions

New methods (all AOT-clean, all serializers registered in `CliJsonContext`):

- `SetVisibility(packageName, visibility) → bool` —
  `PATCH /packages/{scope}/{name}/visibility`.
- `GetVisibility(packageName) → string?` — derived from the existing package
  detail endpoint (no new server route required).
- `GetRoles(packageName) → PackageRolesListResponse?` —
  `GET /packages/{scope}/{name}/roles`.
- `AssignRole(packageName, principalType, principalId, role) → bool` —
  `PUT /packages/{scope}/{name}/roles` (publish-scoped variant). The CLI uses
  the publish-scoped route by default; the admin route stays available for
  callers that already hold admin scope.
- `RevokeRole(packageName, principalType, principalId) → bool` —
  `DELETE /admin/packages/{scope}/{name}/roles` (new endpoint, admin-scoped).
- `ClaimScope(scope) → ScopeResponse?` — `POST /scopes`.
- `GetScope(scope) → ScopeResponse?` — `GET /scopes/{scope}`.
- `CreateOrg(org) → OrganizationResponse?` — `POST /orgs`.
- `GetOrg(org) → OrganizationResponse?` — `GET /orgs/{org}`.
- `AddOrgMember(org, username) → bool` — `POST /orgs/{org}/members`.
- `RemoveOrgMember(org, username) → bool` —
  `DELETE /orgs/{org}/members/{username}`.
- `CreateTeam(org, team) → TeamResponse?` — `POST /orgs/{org}/teams`.
- `AddTeamMember(org, team, username) → bool` —
  `POST /orgs/{org}/teams/{team}/members`.

The legacy helpers `AddOwner` / `RemoveOwner` / `GetOwners` are **deleted**
(clean break). The CLI no longer reads owners as a flat string array — `role
list` reads `PackageRolesListResponse` from the new GET endpoint.

#### Clean-break: drop `owners` from `PackageDetailResponse`

Today `PackageDetailResponse.Owners` (`PackageContracts.cs:21`) carries a
server-derived `List<string>` of usernames materialized from `package_roles`
where role = owner and principal_type = user. With `role list` available, this
duplicates state and quietly hides team/org owners. We drop the `owners` field
entirely from `PackageDetailResponse` and from any CLI mirror DTO as part of
this feature.

**`stash pkg info` consequence.** `GET /packages/{scope}/{name}/roles` requires
**admin scope** (see `Stash.Registry/CLAUDE.md` line 146), while `stash pkg
info` is a public/anonymous read. We do **not** rewire `info` to call
`GetRoles` — that would either 403 for normal callers or silently hide owner
display behind admin scope. Instead, `stash pkg info` simply **stops showing
the owner list**. The new canonical reader is `stash pkg role list <pkg>`,
which surfaces the full principal-typed role table and requires the correct
scope to view. This is documented in the help text and in `docs/PKG`.

### Implementation Path

Server endpoint → wire layer → CLI command surface → docs/tab-completion/example
→ tests → cleanup. Each phase below keeps that arrow intact and ends with an
observable user-facing or test-observable behavior.

1. **Server revoke endpoint.** Add `DELETE /admin/packages/{scope}/{name}/roles`
   on `AdminController`, wired to `IRegistryDatabase.RevokePackageRoleAsync`,
   with a `RevokeRoleRequest` DTO and `AuditService.LogRoleRevokeAsync`
   emission. Server-side tests prove the end-to-end revoke works at the HTTP
   layer before the CLI is touched.
2. **Drop `owners` from `PackageDetailResponse`.** Remove the property
   server-side, remove its serializer registration, update `stash pkg info` to
   no longer print an owners section, and update server tests and any CLI
   mirror DTOs that read it. After this phase the registry's
   `GET /packages/{scope}/{name}` JSON has no `owners` field and `stash pkg
   info` no longer references it.
3. **`RegistryClient` additions.** Add every new method listed above; register
   every new request/response DTO in `CliJsonContext` so AOT publish stays
   green. Delete `AddOwner` / `RemoveOwner` / `GetOwners`. AOT publish + unit
   tests against a fake `HttpMessageHandler` prove every new method shapes the
   right HTTP request.
4. **`stash pkg role` command (replacing `owner`).** Add `RoleCommand` with
   `list` / `assign` / `revoke` subverbs; delete `OwnerCommand.cs`; remove
   `owner` from dispatch; update `info` to read roles via `GetRoles`. CLI →
   HTTP integration test confirms the full assign + list + revoke cycle.
5. **`stash pkg visibility` command.** Add `VisibilityCommand` with `get` /
   `set` subverbs. CLI → HTTP integration test flips visibility and confirms
   read behavior matches the visibility-tier policy.
6. **`stash pkg scope` command.** Add `ScopeCommand` with `claim` / `info`
   subverbs. Integration test claims a scope, then reads it back.
7. **`stash pkg org` command (incl. teams).** Add `OrgCommand` with the full
   nested grammar (`org create`, `org info`, `org member add|remove`,
   `org team add`, `org team member add`). Integration tests cover each path.
8. **Docs + example + cleanup.** Update `docs/PKG — Package Manager CLI.md`
   (replace `owner` section with `role`; add `visibility`, `scope`, `org`
   sections); update `docs/Registry — Package Registry.md` to document the new
   DELETE route; add `examples/package_roles.stash` demonstrating the new
   grammar (CLI walkthrough via `os.exec` of `stash pkg`); update
   `Stash.Cli/Completion/` to drop `owner` and add the new verbs; delete the
   backlog stub at `.kanban/0-backlog/bugs/Package role revocation not exposed
   over HTTP.md`.

## Acceptance Criteria

- `stash pkg role assign @alice/widget team designers maintainer` succeeds
  against a running registry; the team appears in `stash pkg role list
  @alice/widget`.
- `stash pkg role revoke @alice/widget team designers` removes the row; a
  follow-up `role list` no longer shows it; the registry's audit log records
  one `role.revoke` event.
- `stash pkg visibility set @alice/widget private` succeeds; a subsequent
  unauthenticated `GET /packages/alice/widget` is rejected by the visibility
  policy; an authenticated read by the owner succeeds.
- `stash pkg scope claim alice` followed by `stash pkg scope info alice` shows
  the claim; a second claim by a different user fails with a clear error.
- `stash pkg org create acme`, `stash pkg org member add acme bob`,
  `stash pkg org team add acme designers`, and
  `stash pkg org team member add acme designers bob` each succeed against the
  registry and are reflected in `stash pkg org info acme`.
- `stash pkg owner …` is no longer a recognized subcommand. Help text and tab
  completion show no `owner` verb.
- The `PackageDetailResponse` JSON returned by `GET /packages/{scope}/{name}`
  no longer contains an `owners` field.
- `dotnet test` (with the documented flaky filter) is green, and the
  `Stash.Cli` Native-AOT publish succeeds (no new trim warnings).

## Phases

The phase list lives in `plan.yaml` so scripts can read it. Each phase must
have a concrete `done_when` list there.

## Open Questions

- Should `stash pkg role assign` accept an `--admin` flag that routes to the
  admin endpoint instead of the publish-scoped endpoint? Lean **no** for v1 —
  the policy difference is transparent to most callers. Revisit if test
  coverage shows a real need.
- Should the new revoke endpoint return the deleted role row in the response
  body (e.g. `204 No Content` vs `200` with the row)? Lean `204` for
  consistency with the existing DELETE routes (`/deprecate`, `/{version}`).

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-05-30 | Replace `stash pkg owner` outright; no alias | Clean-break posture; principal/role grammar should be explicit, not implicit-user-owner. |
| 2026-05-30 | New revoke endpoint takes a JSON body, not path segments | Mirrors the assign route's shape; avoids URL-encoding of arbitrary principal IDs; one DTO covers the assign/revoke pair. |
| 2026-05-30 | Drop `Owners` from `PackageDetailResponse` | Duplicates `package_roles`; quietly hides team/org owners; `role list` is the canonical reader. |
| 2026-05-30 | `role assign` uses the publish-scoped route by default | Matches what package maintainers can actually do without admin scope. |
| 2026-05-30 | `role revoke` does not take a role argument | Matches `RevokePackageRoleAsync` (principal holds at most one role per package). |
| 2026-05-30 | Required principal-type argument, no default | Avoids silent user-vs-team ambiguity in the new grammar. |
| 2026-05-30 | `stash pkg info` drops owner display entirely (does NOT proxy to `role list`) | `GET /packages/.../roles` is admin-only; proxying would either 403 anonymous callers or silently hide owners behind admin scope. `role list` is the canonical reader. |
| 2026-05-30 | No `stash pkg` tab-completion subsystem to update | Verified: `Stash.Cli/Completion/` is generic infrastructure with no `pkg` subcommand list. The subcommand surface is reflected only in `PackageCommands.cs` help text. |
