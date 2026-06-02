# RFC: Stash pkg CLI parity with scoped registry API

> **Status:** Draft (re-specced 2026-06-02 against the post-`registry-authz-*` API)
> **Owner:** Cristian Moraru
> **Created:** 2026-05-30
> **Re-specced:** 2026-06-02
> **Slug:** pkg-cli-api-parity

## Summary

The `stash pkg` CLI is missing commands for most capabilities the scoped registry
exposes after `registry-scope-foundation` and the `registry-authz-*` series. The
wire layer (`RegistryClient`) speaks scoped routes, but the CLI command surface
still only covers a narrow user-owner flow plus install / publish / deprecate /
token. This feature closes the gap: every registry capability the API actually
exposes gets a first-class `stash pkg <verb>` command. Where the old surface does
not match the new model it is removed outright — no aliases, no shims (consistent
with the registry's D3 clean-break posture).

**This is a CLI-surface feature plus one server contract cleanup** (dropping the
derived `owners` field from the package-detail response). It adds **no new server
endpoints** — the one server gap the original draft planned to close (a revoke-role
route) was shipped independently while this spec sat unstarted (see *What changed*).

### What changed since the 2026-05-30 draft (why this was re-specced)

The original draft was written against an incomplete API and an open server gap.
Both moved underneath it:

1. **The revoke-role endpoint already exists.** `registry-authz-pipeline` P3
   (commit `154671b4`) shipped **two** revoke routes —
   `DELETE /api/v1/packages/{scope}/{name}/roles` (owner self-service, publish
   ceiling) and `DELETE /api/v1/admin/packages/{scope}/{name}/roles` (admin
   override) — both taking a `{principal_type, principal_id}` body that mirrors
   the `PUT .../roles` assign route. A new `PackageRoleService.RevokeRoleAsync`
   owns a **last-owner orphan-protection invariant (D18)**: a revoke that would
   drop the package's owner count to zero is refused with `409`
   (`LastOwnerException`); a principal holding no role yields `404`
   (`RoleNotFoundException`); success is `204`. The original **P1 is therefore
   deleted** — it is done, with richer semantics than the draft anticipated.
2. **The backlog stub is already retired.** `Package role revocation not exposed
   over HTTP.md` is in `.kanban/4-done/` (commit `e7a03a04`); the CLI's
   `RegistryClient.RemoveOwner` is already wired to the admin revoke route
   (commit `8628eaeb`) and surfaces the `409`/`404` server messages. The draft's
   goal to "close the revoke gap and retire the stub" is satisfied.
3. **The authz ceilings are uniform — and the draft's source was stale.** The
   draft (citing `Stash.Registry/CLAUDE.md`) assumed `GET .../roles` needed
   **admin** while assign needed **publish**. The actual Policy Decision Point
   (`Stash.Registry/Auth/Authorization/RegistryAuthorizer.cs:115-119`) maps
   **all** of `ListPackageRoles`, `AssignPackageRole`, `RevokePackageRole`, and
   `ChangePackageVisibility` to `TokenCeiling.Publish`; `ReadPackageMetadata` is
   anonymous. So `role list / assign / revoke` and `visibility set` all run on
   the **self-service publish routes** at a uniform publish ceiling — the
   `/admin/...` routes are admin-override only. The draft's central
   admin-vs-publish asymmetry dissolves.
4. **Two read-path gaps were found and deferred.** Verifying the reads the CLI
   would call showed the server has **no way to read a package's visibility**
   (`PackageDetailResponse` has no `visibility` field; no `GET .../visibility`
   route) and **no way to read an org's members or teams** (`OrgDetailResponse`
   carries only flat metadata; no `GET .../members` route). Per the scope
   decision (2026-06-02), this feature stays CLI-only: it ships against the read
   paths that exist and files the two gaps as backlog bugs
   (`.kanban/0-backlog/bugs/Package visibility has no read path …`,
   `… Org members and teams have no read path …`). Consequently `visibility get`
   is **not** shipped, and `org info` shows flat org metadata only.

## Motivation

After `registry-scope-foundation` + `registry-authz-*` the registry speaks:

- Scoped packages with three visibility tiers (`public` / `internal` / `private`).
- Scope ownership (a user or org claims a scope), with an optional Verified mode
  (DNS-TXT challenge).
- Organizations with members and teams; teams with members.
- Per-package role grants for **users, teams, and orgs**, with four roles
  (`owner`, `maintainer`, `publisher`, `reader`), assignable/revokable/listable at
  the **publish** ceiling via self-service routes.

The CLI today speaks only "package owner = user." `stash pkg owner add foo bob`
hard-codes `principal_type=user`, `role=owner`, routes through the **admin**
endpoint, and reads owners from a flat `owners[]` array on the package detail.
There is no way from the CLI to: assign a non-owner role; grant a role to a team
or org; revoke any role using the principal-typed grammar; set visibility; claim
or inspect a scope; or create/inspect an org and manage its members and teams.

End users who want the new model must hit the HTTP API directly. That defeats the
point of shipping the scoped registry.

## Goals

- **Full command-surface parity with the API as it exists today**: every registry
  capability with a working read/write path is reachable through a `stash pkg`
  subcommand.
- **One coherent CLI grammar for role management** — principal type
  (`user|team|org`) and role (`owner|maintainer|publisher|reader`) are first-class
  arguments, not implicit. All role + visibility operations run at the uniform
  **publish** ceiling on the self-service routes.
- **Surface the D18 last-owner invariant**: `role revoke` maps the server's `409`
  (last owner) and `404` (no such role) into clear, actionable CLI errors.
- **Drop the derived `owners` field** from `PackageDetailResponse` (and stop
  printing it in `stash pkg info`); `stash pkg role list` becomes the canonical,
  principal-typed reader.
- **Keep `Stash.Cli` Native-AOT-clean**: every new request/response DTO is
  registered in `CliJsonContext` with source-gen — no reflection, no `Activator`.
- **Honor the language-changes checklist**: CLI docs updated, example added, xUnit
  tests cover every new command at CLI→HTTP integration level.
- **Record the two deferred read-path gaps** as backlog bugs (done as part of this
  re-spec) so `visibility get` and membership display have a home.

## Non-Goals

- **No new server endpoints, and no server read-path additions.** The revoke route
  already exists; the visibility-read and org-membership-read gaps are deferred to
  the two filed backlog bugs, not built here.
- **No changes to the authorization model.** We consume the existing PDP ceilings
  as-is (publish for role/visibility writes + role list; anonymous for package and
  org/scope reads). No new `RegistryAction`s, no policy changes.
- **No changes to `stash pkg install` / dependency resolution / lockfile shape.**
- **No stdlib changes.** `docs/Stash — Standard Library Reference.md` is not
  regenerated; `Stash.Docs` is not run.
- **No SDK / library surface.** The CLI is the deliverable; `RegistryClient` is
  extended only because the CLI needs it.
- **No backwards-compat alias for the removed `owner` command.** Users see
  "unknown command" through normal dispatch.
- **No `visibility get` and no org membership display** — both lack a server read
  path (deferred to backlog).

## Design

### Surface

New `stash pkg` grammar. Verbs marked **new** are added; the `owner` verb is
**removed** outright.

```
# Package roles (new — replaces `stash pkg owner`)
stash pkg role list   <pkg>                                                  # GET    /packages/{scope}/{name}/roles   (publish)
stash pkg role assign <pkg> <user|team|org> <principal> <owner|maintainer|publisher|reader>
                                                                            # PUT    /packages/{scope}/{name}/roles   (publish)
stash pkg role revoke <pkg> <user|team|org> <principal>                      # DELETE /packages/{scope}/{name}/roles   (publish)

# Package visibility (new) — set only; `get` deferred (no server read path)
stash pkg visibility set <pkg> <public|internal|private>                     # PATCH  /packages/{scope}/{name}/visibility (publish)

# Scopes (new)
stash pkg scope claim <scope> [--org <org>]                                  # POST   /scopes                          (publish)
stash pkg scope info  <scope>                                               # GET    /scopes/{scope}                  (public)

# Organizations + teams (new)
stash pkg org create        <org> [--display-name <name>]                    # POST   /orgs                            (publish)
stash pkg org info          <org>                                           # GET    /orgs/{org}                      (public)  — flat metadata only
stash pkg org member add    <org> <username> [--role owner|member]           # POST   /orgs/{org}/members              (publish)
stash pkg org member remove <org> <username>                                 # DELETE /orgs/{org}/members/{username}    (publish)
stash pkg org team add      <org> <team>                                     # POST   /orgs/{org}/teams                (publish)
stash pkg org team member add <org> <team> <username>                        # POST   /orgs/{org}/teams/{team}/members  (publish)
```

Verbs preserved unchanged: `init`, `install`/`i`, `uninstall`/`remove`,
`list`/`ls`, `pack`, `update`, `outdated`, `publish`, `search`, `info`, `login`,
`logout`, `whoami`, `unpublish`, `deprecate`, `undeprecate`, `token`, `help`.

Verb-level dispatch lives in
`Stash.Cli/PackageManager/Commands/PackageCommands.cs`. Each new top-level verb
(`role`, `visibility`, `scope`, `org`) gets its own command class under
`Stash.Cli/PackageManager/Commands/`. `OwnerCommand.cs` is deleted, the `owner`
dispatch entry and help line are removed, and the help text gains the four new
verbs. (There is no `stash pkg` tab-completion subsystem — verified: the
subcommand surface is reflected only in `PackageCommands.cs` help text.)

### Semantics

- **Uniform publish ceiling for role + visibility.** `role list/assign/revoke` and
  `visibility set` all require a token whose ceiling is `publish` (or higher) AND
  the appropriate resource-side role (the PDP's two-step check). A read-only token
  gets a clean `403`. The CLI uses the **self-service** routes
  (`/packages/{scope}/{name}/roles`, `…/visibility`), **not** the `/admin/...`
  routes — a package owner manages their own package's roles with a publish token,
  no admin required. This is the symmetric self-service the revoke bug-fix flagged
  as a follow-up; it is folded in here.
- **`role assign` requires an explicit principal-type argument** — no
  default-to-user. Clean grammar, no silent user-vs-team ambiguity.
- **`role revoke` takes no role argument** — a principal holds at most one role per
  package, and revoke removes whatever role they hold (matches the
  `RevokeRoleRequest` body shape).
- **`role revoke` error mapping (D18-aware):** `204 → success`;
  `404 → "Not found: <principal> holds no role on <pkg>."` (RoleNotFoundException);
  `409 → "Cannot revoke: that would leave <pkg> with no owner."` (LastOwnerException,
  server message surfaced verbatim); `403 → "Forbidden (<reason>)."`;
  `401 → "Not logged in. Run 'stash pkg login'."`.
- **`scope claim`** sends a `ClaimScopeRequest { scope, owner_type, owner,
  verification_method? }`. Default `owner_type=user`, `owner=<authenticated user>`
  (resolved via `whoami`); `--org <org>` sets `owner_type=org`, `owner=<org>`. If
  the registry runs in **Verified** mode the response carries `state=pending` and
  a `challenge` (DNS-TXT `record_name`/`record_value`/`expires_at`) — the CLI
  prints those instructions instead of reporting a completed claim. (Implementer:
  confirm whether the server infers `owner` from the token when omitted; if so,
  prefer sending it explicitly for determinism.)
- **`scope info` / `org info` are anonymous reads.** `org info` prints the flat
  `OrgDetailResponse` fields (`id`, `name`, `display_name`, `created_at`,
  `created_by`) — it does **not** list members or teams (no server read path; see
  backlog bug).
- **`visibility set` is idempotent** (setting `public` on a public package
  succeeds). There is no `visibility get`.
- **General error mapping:** `401 → "Not logged in. Run 'stash pkg login'."`,
  `403 → "Forbidden (<reason>)."`, `404 → "Not found: <resource>."`,
  `409 → "Conflict: <reason>."` — always surfacing the server's `ErrorResponse`
  message where present.

#### Server: nothing new (the draft's P1 is retired)

Both revoke routes already exist with full D18 semantics (see *What changed* #1).
The CLI consumes the existing self-service route. No controller, no `RegistryAction`,
no DTO is added server-side **except** the P1 cleanup below.

#### `RegistryClient` additions

New methods (all AOT-clean; every new request/response DTO registered in
`CliJsonContext`). Routes are the **self-service publish** variants:

- `SetVisibility(packageName, visibility) → bool` — `PATCH /packages/{scope}/{name}/visibility`.
- `GetRoles(packageName) → PackageRolesListResponse?` — `GET /packages/{scope}/{name}/roles` (publish).
- `AssignRole(packageName, principalType, principalId, role) → bool` — `PUT /packages/{scope}/{name}/roles` (publish).
- `RevokeRole(packageName, principalType, principalId) → bool` — `DELETE /packages/{scope}/{name}/roles` (publish); throws with the server message on `404`/`409`.
- `ClaimScope(scope, ownerType, owner) → ScopeDetailResponse?` — `POST /scopes`.
- `GetScope(scope) → ScopeDetailResponse?` — `GET /scopes/{scope}`.
- `CreateOrg(name, displayName?) → CreateOrgResponse?` — `POST /orgs`.
- `GetOrg(org) → OrgDetailResponse?` — `GET /orgs/{org}`.
- `AddOrgMember(org, username, orgRole?) → bool` — `POST /orgs/{org}/members`.
- `RemoveOrgMember(org, username) → bool` — `DELETE /orgs/{org}/members/{username}`.
- `CreateTeam(org, team) → CreateTeamResponse?` — `POST /orgs/{org}/teams`.
- `AddTeamMember(org, team, username) → bool` — `POST /orgs/{org}/teams/{team}/members`.

**Already present (reuse / generalize, do not duplicate):** the CLI `AssignRoleRequest`
and `RevokeRoleRequest` DTOs already exist in `CliJsonContext` (used by the legacy
owner helpers). `RevokeRole`/`AssignRole` generalize `RemoveOwner`/`AddOwner` from
hard-coded `user`/`owner` on the **admin** route to principal-typed arguments on the
**self-service** route.

**Deleted (clean break):** `AddOwner`, `RemoveOwner`, `GetOwners`. The CLI no longer
reads owners as a flat string array — `role list` reads `PackageRolesListResponse`.
New CLI mirror response DTOs to add and register: `PackageRoleResponse`,
`PackageRolesListResponse`, `ScopeDetailResponse` (+ `ScopeChallengeBody`),
`CreateOrgResponse`, `OrgDetailResponse`, `CreateTeamResponse`, and any request DTOs
(`ClaimScopeRequest`, `CreateOrgRequest`, `AddOrgMemberRequest`, `CreateTeamRequest`,
`AddTeamMemberRequest`) — mirroring the server's snake_case JSON keys.

#### Clean-break: drop `owners` from `PackageDetailResponse`

`PackageDetailResponse.Owners` (`PackageContracts.cs:20-22`, `required`) carries a
server-derived `List<string>` of usernames materialized from `package_roles` where
`role = owner` and `principal_type = user` (built in `PackagesController.GetPackage`,
lines 100-105). With `role list` available this duplicates state and silently hides
team/org owners. Drop the `owners` field from `PackageDetailResponse`, stop deriving
it in `GetPackage`, and remove the `Owners:` block from `InfoCommand.Render`
(`InfoCommand.cs:107-122`).

**`stash pkg info` consequence.** `info` is anonymous; `role list` requires a
publish token. So after this change, *anonymous* callers no longer see any owner
information through `info` — owner/role display moves behind a publish token via
`role list`. This is the intended D3 posture (principal-typed roles are the real
model; the flat `owners[]` was a lossy projection). Documented in help text and
`docs/PKG`.

### Implementation Path

Server contract cleanup → wire layer → CLI command surface → docs/example. Each
phase ends with an observable, test-observable behavior. (Phase IDs below match
`plan.yaml`.)

1. **P1 — Drop `owners` from `PackageDetailResponse`.** Remove the property,
   stop deriving it in `GetPackage`, strip the `Owners:` block from
   `InfoCommand.Render`, update server + CLI tests. After P1 the package-detail
   JSON has no `owners` key and `stash pkg info` prints no owners section.
2. **P2 — `RegistryClient` parity additions + AOT registration.** Add every method
   above (self-service routes); add + register every new mirror DTO in
   `CliJsonContext`; delete `AddOwner`/`RemoveOwner`/`GetOwners`. Fake-handler unit
   tests assert each method's URL, verb, and body shape; AOT publish stays clean.
3. **P3 — `stash pkg role` (replaces `owner`).** Add `RoleCommand`
   (`list`/`assign`/`revoke`); delete `OwnerCommand.cs`; remove `owner` from
   dispatch + help. Integration test runs a full assign → list → revoke round-trip
   against a `WebApplicationFactory<Program>` registry **with a publish token**,
   and asserts the D18 `409`/`404` mappings.
4. **P4 — `stash pkg visibility set`.** Add `VisibilityCommand` (`set` only).
   Integration test flips visibility and confirms the tier change via the
   visibility policy (an anonymous read of a now-private package is rejected).
5. **P5 — `stash pkg scope`.** Add `ScopeCommand` (`claim`/`info`), including the
   `--org` flag and Verified-mode challenge handling. Integration test claims a
   scope then reads it back; a second claim by a different user fails clearly.
6. **P6 — `stash pkg org` (orgs + teams).** Add `OrgCommand` with the nested
   grammar. Integration tests cover each write path's success + a clear failure on
   missing args; `org info` shows the flat fields.
7. **P7 — Docs, example, cleanup.** Update `docs/PKG` (replace `owner` with
   `role`; add `visibility`/`scope`/`org`; note `visibility get` + org membership
   deferral) and `docs/Registry` (note the self-service role routes the CLI uses);
   add `examples/package_roles.stash`; ensure the help text is correct.

### Acceptance Criteria

- `stash pkg role assign @alice/widget team designers maintainer` succeeds against a
  running registry with a **publish** token; the team appears in
  `stash pkg role list @alice/widget`.
- `stash pkg role revoke @alice/widget team designers` removes the row; a follow-up
  `role list` no longer shows it. Revoking the **last owner** of a package surfaces
  the server's `409` last-owner message; revoking a principal with no role surfaces
  the `404` message.
- `stash pkg visibility set @alice/widget private` succeeds; a subsequent
  unauthenticated `GET /packages/alice/widget` is rejected by the visibility policy;
  an authenticated read by the owner succeeds.
- `stash pkg scope claim alice` followed by `stash pkg scope info alice` shows the
  claim; a second claim by a different user fails with a clear error. (Under
  Verified mode, `scope claim` prints the DNS-TXT challenge.)
- `stash pkg org create acme`, `org member add acme bob`, `org team add acme
  designers`, and `org team member add acme designers bob` each succeed against the
  registry (return success / non-zero on missing args). `stash pkg org info acme`
  shows the org's flat metadata. *(Membership is not displayed — deferred to
  backlog; no acceptance criterion asserts member/team readback.)*
- `stash pkg owner …` is no longer a recognized subcommand; help text shows no
  `owner` verb and lists `role`, `visibility`, `scope`, `org`.
- The `PackageDetailResponse` JSON returned by `GET /packages/{scope}/{name}` no
  longer contains an `owners` field, and `stash pkg info` prints no owners section.
- The full `dotnet test` suite is green, and the `Stash.Cli` Native-AOT publish
  succeeds with no new trim warnings.

## Phases

The phase list lives in `plan.yaml` so scripts can read it. Each phase has a
concrete `done_when` list there.

## Open Questions

- **Should `role assign`/`role revoke` offer an `--admin` flag** routing to the
  `/admin/...` override routes? Lean **no** for v1 — the self-service publish routes
  cover the package-owner use case, and an org/registry admin can already use raw
  HTTP. Revisit if test coverage shows a real need.
- **Does `POST /scopes` infer `owner` from the token when omitted?** The CLI sends
  it explicitly (from `whoami`); the implementer should confirm the server's
  behavior and keep the explicit send for determinism either way.
- **Three working write endpoints are intentionally omitted from v1 — confirm.**
  Source grep (2026-06-02) shows the server also exposes `DELETE /orgs/{org}`
  (`OrganizationsController.DeleteOrg`) and `DELETE /scopes/{scope}`
  (`ScopesController.DeleteScope`) — both real, both absent from this CLI grammar
  (and from the 2026-05-30 draft). They are **destructive** org/scope teardown
  operations; lean **defer to a follow-up** rather than ship `org delete` /
  `scope delete` in this parity pass. `POST /scopes/{scope}/verify` exists but is a
  **501 stub** (DNS-TXT verification not implemented), so it is correctly omitted
  until the server implements it. If true command-surface parity is required now,
  add `org delete` and `scope delete` as a small addition to P5/P6.

### Resolved since the draft

- *Revoke endpoint shape / status code* — settled by the shipped server: `204` on
  success, body `{principal_type, principal_id}`, two routes (self-service +
  admin). The draft's "one new endpoint" question is moot.
- *`role list` vs `revoke` auth asymmetry* — there is none: both are publish
  (`RegistryAuthorizer.cs:115-119`). The draft's admin-vs-publish tension is gone.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-05-30 | Replace `stash pkg owner` outright; no alias | Clean-break posture; principal/role grammar should be explicit, not implicit-user-owner. |
| 2026-05-30 | Drop `Owners` from `PackageDetailResponse` | Duplicates `package_roles`; quietly hides team/org owners; `role list` is the canonical reader. |
| 2026-05-30 | Required principal-type argument, no default | Avoids silent user-vs-team ambiguity in the new grammar. |
| 2026-05-30 | `role revoke` takes no role argument | A principal holds at most one role per package; revoke removes whatever they hold. |
| 2026-06-02 | **Re-spec: delete P1 (server revoke endpoint)** | Shipped by `registry-authz-pipeline` P3 (`154671b4`) with richer D18 semantics; the backlog stub is already retired (`e7a03a04`). |
| 2026-06-02 | **`role list/assign/revoke` + `visibility set` use the self-service publish routes at a uniform publish ceiling** | The PDP (`RegistryAuthorizer.cs:115-119`) maps all four actions to `Publish`; the draft's admin-vs-publish asymmetry was based on a stale `CLAUDE.md` table. Self-service is what a package owner can actually do without admin. |
| 2026-06-02 | **`role revoke` surfaces the D18 `409` last-owner and `404` no-such-role messages** | The shipped server enforces last-owner orphan-protection; the CLI must not swallow these reasons. |
| 2026-06-02 | **Feature stays CLI-only; defer the two read-path gaps to backlog** | Per the scope decision: no server read-path work in this feature. `visibility get` and org-membership display lack a server read path — filed as `0-backlog/bugs/Package visibility has no read path …` and `… Org members and teams have no read path …`. |
| 2026-06-02 | **Ship `visibility set` only (no `visibility get`)** | `PackageDetailResponse` has no `visibility` field and there is no `GET .../visibility` route — nothing to read. Deferred to the backlog bug. |
| 2026-06-02 | **`org info` shows flat metadata only (no members/teams)** | `OrgDetailResponse` carries no membership; no read route exists. Deferred to the backlog bug. |
| 2026-06-02 | **`scope claim` sends `owner_type`/`owner` and handles the Verified-mode DNS-TXT challenge** | `ClaimScopeRequest` requires the owner pair, and Verified mode returns a `pending` state + challenge the user must act on. |
| 2026-05-30 | No `stash pkg` tab-completion subsystem to update | Verified: `Stash.Cli/Completion/` is generic infrastructure with no `pkg` subcommand list; the surface lives only in `PackageCommands.cs` help text. |
