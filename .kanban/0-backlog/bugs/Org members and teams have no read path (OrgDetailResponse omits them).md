# Org members and teams have no read path (`OrgDetailResponse` omits them)

**Status:** Backlog — Bug
**Created:** 2026-06-02
**Discovery context:** Surfaced while re-speccing `pkg-cli-api-parity` (2026-06-02). The original spec's acceptance criterion — "`org member add` … reflected in a subsequent `stash pkg org info`" — turned out to be unsatisfiable against the current server: there is no read path that returns an org's members or teams.

---

## Problem

An organization's members and teams can be *written* —
`POST /api/v1/orgs/{org}/members`, `POST /api/v1/orgs/{org}/teams`, and
`POST /api/v1/orgs/{org}/teams/{team}/members` all exist — but they cannot be
*read back*. `GET /api/v1/orgs/{org}` returns `OrgDetailResponse`, which carries
only `id`, `name`, `display_name`, `created_at`, and `created_by`. There is no
`members` or `teams` collection on it, and no `GET /orgs/{org}/members` or
`GET /orgs/{org}/teams` route. The only way to confirm a member or team add is by
its side effects (e.g. publishing under the org scope), not by inspection.

This caps what `stash pkg org info` can display: it can show the org's flat
metadata but not who belongs to it. The `pkg-cli-api-parity` feature ships
`org info` against the fields that exist and defers membership display to this fix.

## Reproduction

```bash
# With a running registry and a publish-scoped token:
stash pkg org create acme
stash pkg org member add acme bob            # 200/201 — succeeds
curl -s http://localhost:5000/api/v1/orgs/acme | jq 'has("members"), has("teams")'
# Expected: true, true
# Actual:   false, false   — neither key is present
```

## Blast radius

- Any caller (CLI or HTTP) needing to enumerate an org's members or teams. No
  read path exists.
- Latent today, but it is an immediate UX hole once the org commands land: a user
  can add members and teams and never list them back through the API. It also
  makes the org write commands hard to test end-to-end (no read-side assertion).

## Root cause

`Stash.Registry/Contracts/OrganizationContracts.cs` `OrgDetailResponse` (lines
51–72) defines no membership collections, and
`Stash.Registry/Controllers/OrganizationsController.cs` `GetOrg` builds only the
flat fields. The DB layer almost certainly has the listing methods already (org
members and team members are stored relationally), so this is a projection/route
gap, not a data-model gap.

## Suggested fix

- (A) **Extend `OrgDetailResponse`** with a `members` collection
  (`username` + `org_role`) and a `teams` collection (`name`, optionally each
  team's members), populated in `GetOrg`. One round-trip, mirrors the
  `PackageRolesListResponse` shape. Trade-off: `GET /orgs/{org}` is currently a
  **public** endpoint, so this would publish org membership to the world — likely
  undesirable.
- (B) **Add dedicated publish-scoped routes** `GET /orgs/{org}/members` and
  `GET /orgs/{org}/teams` (and/or `GET /orgs/{org}/teams/{team}/members`), each
  `RegistryAction`-classified at the `Publish` ceiling like the corresponding
  write routes. Keeps membership behind a publish token and lets the CLI add an
  `org member list` / `org team list` surface. Trade-off: more routes + PDP
  entries + tests.

Recommend **(B)** — membership should not be world-readable, and matching the
write routes' publish ceiling keeps the authz model coherent.

## Verification

```bash
dotnet test --filter "FullyQualifiedName~Organization"
# After the fix: a read route returns the members/teams just added; a test
# asserts the round-trip (add member → list members shows it).
```

End-to-end: `stash pkg org member add acme bob` followed by the new read surface
(`stash pkg org member list acme` or an enriched `org info`) shows `bob`.

## Related

- `pkg-cli-api-parity` — this re-spec narrowed `org info` to the flat fields and
  deferred membership/team display to this fix.
- `registry-scope-foundation` — introduced orgs, teams, and the member/team write
  routes.
- `Stash.Registry/Contracts/OrganizationContracts.cs:51` — `OrgDetailResponse`.
- `Stash.Registry/Controllers/OrganizationsController.cs` — `GetOrg`.
