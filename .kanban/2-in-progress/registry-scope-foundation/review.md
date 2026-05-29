# registry-scope-foundation — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header is parseable: `## Fxx — [SEVERITY] short title`.

**Scope reviewed:** commits `2e7b0e1..1c9c025` on branch `main`
**Brief:** ../brief.md
**Generated:** 2026-05-29

---

## F01 — [HIGH] CLI package-manager test fixtures still use flat names — 17+ test failures

**Status:** fixed
**Fixed in:** 79af0d8
**Files:** `Stash.Tests/Interpreting/PackageInstallerTests.cs:325,1022,1286` (and the `PackageCacheTests`, `LockFileFreshnessTests`, `DeprecationWarningTests`, `InstallAtomicityTests` classes living in the same file), `Stash.Tests/Interpreting/CliPackageCommandsTests.cs`
**Phase:** P1 / P6 (fallout from the grammar tightening; P6 owns the CLI-side migration)
**Commit:** 22525c3 (P1, root cause), 6c9b005 (P6, missed scope)

### Observation

`Stash.Tests/Interpreting/PackageInstallerTests.cs` (the file housing
`PackageInstallerTests`, `InstallAtomicityTests`, `LockFileFreshnessTests`,
`DeprecationWarningTests`, and the `PackageCacheTests` exercised via
`CreateAndCachePackage` helpers) constructs package names with patterns like
`test-pkg-<guid>`, `atomic-pkg-<guid>`, `fastpath-pkg-<guid>`, `alpha`, etc.
These call into `PackageCache.GetCachePath`, which in turn calls
`PackageManifest.IsValidPackageName`. After P1 tightened the grammar, every
such name is rejected:

```
System.ArgumentException : Invalid package name: 'test-pkg-3af9ff6077644f689782139f5495894f'
   at Stash.Cli.PackageManager.PackageCache.GetCachePath(...)
   at Stash.Cli.PackageManager.PackageCache.Store(...)
   at Stash.Tests.Interpreting.PackageInstallerTests.CreateAndCachePackage(...)
```

The `Stash.Cli/PackageManager/**` files are inside the feature scope glob
(`plan.yaml:24`). The Registry-side test fixtures in
`Stash.Tests/Registry/**` were updated to `@scope/name`; the CLI-side ones
were not. A targeted run reproduces 17 failures across these classes:

```
$ dotnet test --filter "FullyQualifiedName~PackageInstallerTests|FullyQualifiedName~PackageCacheTests|FullyQualifiedName~LockFileFreshnessTests|FullyQualifiedName~DeprecationWarningTests|FullyQualifiedName~InstallAtomicityTests"
Failed!  - Failed: 17, Passed: 4, Skipped: 0
```

A full baseline run further surfaces 6 `IntegrityVerificationTests` failures
with the same root cause; those are *currently masked* in `final_verify`'s
exclude filter (`plan.yaml:188`) because their name contains
`IntegrityVerification`. That mask predates this feature and should not be
relied on to hide a regression caused by it.

The production cache path is unaffected: `PackageCache.GetCachePath` already
sanitizes `@scope/name` correctly (`.TrimStart('@').Replace('/', '-')`), so
this is a fixture-only gap, not a production defect.

### Why this matters

- 17 in-tree tests fail on `dotnet test`. Several more (the 6
  `IntegrityVerificationTests`) are silently passing through `final_verify`
  only because of a pre-existing exclusion filter that was not authored to
  cover this regression.
- The CLI install/cache/uninstall paths now have **no live test coverage** at
  all for the new scoped grammar — every previously-green test in these
  classes is short-circuiting in `Setup`-equivalent helpers before exercising
  the path under test.
- Without a fix, `/done` will green-light on `final_verify` despite real
  regressions in the feature's own CLI surface, defeating the purpose of the
  gate.

### Suggested fix

Update the fixture name patterns in
`Stash.Tests/Interpreting/PackageInstallerTests.cs` (and any sibling test in
`Stash.Tests/Interpreting/` invoking `PackageCache` /
`PackageInstaller` /  `RegistryClient` with the affected helpers) so every
package name becomes `@<scope>/<localName>`, e.g.:

- `test-pkg-<guid>` → `@testscope/test-pkg-<guid-trimmed>` (note: local
  names also have the 39-char and `[a-z0-9-]` rules; trim the guid or use
  a deterministic short suffix).
- `atomic-pkg-<guid>` → `@atomic/pkg-<short-guid>`.
- `fastpath-pkg-<guid>` → `@fastpath/pkg-<short-guid>`.
- `alpha`, `beta`, etc. → `@suite/alpha`, `@suite/beta`.

Do **not** weaken `PackageManifest.IsValidPackageName` or
`PackageCache.GetCachePath`. The production path is correct; the fixtures
are wrong.

Also broaden the targeted exclude on `IntegrityVerification` to *re-include*
the integrity tests once their fixtures are renamed — this feature does
exercise that surface.

### Verify

```
dotnet test --filter "FullyQualifiedName~PackageInstallerTests|FullyQualifiedName~PackageCacheTests|FullyQualifiedName~LockFileFreshnessTests|FullyQualifiedName~DeprecationWarningTests|FullyQualifiedName~InstallAtomicityTests|FullyQualifiedName~IntegrityVerificationTests"
```

Expected: 0 failures.

---

## F02 — [HIGH] Dead `AddOwner` / `RemoveOwner` in `RegistryClient` — wrong URL and wrong body

**Status:** fixed
**Fixed in:** 219bde6
AddOwner wired to admin roles route; RemoveOwner fails honestly — revoke-over-HTTP deferred, tracked in .kanban/0-backlog/bugs/Package role revocation not exposed over HTTP.md
**Files:** `Stash.Cli/PackageManager/RegistryClient.cs:816-823`, `Stash.Cli/PackageManager/RegistryClient.cs:835-842`
**Phase:** P5 / P6
**Commit:** 74ea0f7 (P5 renamed endpoint), 6c9b005 (P6 migrated the path token but missed the rest)

### Observation

P5 renamed the admin-side endpoint from
`PUT /admin/packages/{name}/owners` (with `OwnerUpdateRequest` body
`{ Add: [...], Remove: [...] }`) to
`PUT /admin/packages/{scope}/{name}/roles` (with `AssignRoleRequest` body
`{ username, role }`) — both controller side
(`AdminController.cs`, see plan `done_when` "PUT /api/v1/packages/{scope}/{name}/roles
assigns a role …") and contract side (`PackageContracts.cs`,
`AssignRoleRequest`).

The CLI's `RegistryClient.AddOwner` and `RegistryClient.RemoveOwner`:

```csharp
// line 821 & 840
var response = _http.PutAsync(
    $"{_baseUrl}/admin/packages/{ScopedPackagePath(packageName)}/owners",  // <— wrong suffix
    content                                                                 // <— wrong body shape
).GetAwaiter().GetResult();
```

P6 migrated the path *prefix* (`packageName` → `ScopedPackagePath(...)`)
but did **not** change the trailing `/owners` to `/roles`, and did **not**
swap `OwnerUpdateRequest { Add, Remove }` for the new `AssignRoleRequest
{ Username, Role }`. Result: both methods POST to a route that no longer
exists (will 404) with a body the new route would not understand.

The CLI surface `stash pkg owner add/remove` (documented in
`docs/PKG — Package Manager CLI.md:422-423,613-614`) calls these methods,
so the user-visible owner-management subcommand is broken end-to-end.

### Why this matters

- `stash pkg owner add` / `stash pkg owner remove` silently fail (return
  `false` on 404) against any registry built from this feature. The CLI's
  primary owner-management surface is dead.
- Plan `done_when` for P6 line 159 ("`RegistryClient.cs` contains no
  remaining single-segment `/packages/{...}` call site; every URL is built
  as `/packages/{scope}/{name}`") was interpreted narrowly: it caught the
  segment swap but missed the route-shape rename that P5 introduced
  simultaneously.
- `OwnerUpdateRequest` is now dead client-only model code (no server
  consumer).

### Suggested fix

Replace both methods to call the new role endpoint and adapt the body:

```csharp
public bool AddOwner(string packageName, string username) {
    EnsureTokenFresh();
    var body = JsonSerializer.Serialize(
        new AssignRoleRequest { Username = username, Role = "owner" },
        CliJsonContext.Default.AssignRoleRequest);
    var content = new StringContent(body, Encoding.UTF8, "application/json");
    var response = _http.PutAsync(
        $"{_baseUrl}/admin/packages/{ScopedPackagePath(packageName)}/roles",
        content).GetAwaiter().GetResult();
    return response.IsSuccessStatusCode;
}

public bool RemoveOwner(string packageName, string username) {
    // The new role surface does not support “remove role” via PUT.
    // Either use DELETE /api/v1/packages/{scope}/{name}/roles/{username}
    // (if/when implemented) or assign a no-op role (reader) — confirm with
    // brief.md before choosing. Surface the absence of a clean Revoke path
    // as a follow-up if neither maps cleanly.
}
```

Add `AssignRoleRequest` to `CliJsonContext` (and to the AOT
`[JsonSerializable]` list) if it is not already present. Delete the now-dead
`OwnerUpdateRequest` from the CLI if nothing else references it.

While here, confirm whether the corresponding `stash pkg owner ...` CLI
command still maps semantically (`add` ≈ assign `owner` role; `remove` may
need a true role-revoke endpoint that does not yet exist — escalate if
so).

### Verify

```
grep -n "/owners" Stash.Cli/PackageManager/RegistryClient.cs   # expect: no matches
grep -n "OwnerUpdateRequest" Stash.Cli/                         # expect: no matches
dotnet build
# Manual e2e: `stash pkg owner add @alice/widget bob` against a registry
# should land a role assignment row in package_roles (verify via SQL or
# GET /api/v1/admin/packages/{scope}/{name}/roles).
```

---

## F03 — [HIGH] Search visibility filter ignores team- and org-mediated permissions

**Status:** fixed
**Fixed in:** 2218ffa
**Note:** `internal` and `private` are treated identically in search (matching today's controller at PackagesController.cs:623); the `internal`-specific shortcut remains tracked in F04.
**Files:** `Stash.Registry/Database/StashRegistryDatabase.cs:119-152`
**Phase:** P4
**Commit:** 34c3c96

### Observation

`SearchPackagesAsync` filters non-public packages by *only* the direct
`PackageRoles` user-principal join:

```csharp
queryable = queryable.Where(p =>
    p.Visibility == "public" ||
    _context.PackageRoles.Any(r =>
        r.PackageName == p.Name &&
        r.PrincipalType == "user" &&
        r.PrincipalId == callerUsername));
```

The brief's "Semantics — Search" (line 292-294) reads:

> When a JWT is present, public union (private the caller can read) union
> (internal the caller can read).

"Can read" is defined by `HasPackagePermissionAsync`, which unions
direct-user + team-mediated + org-mediated (scope-owner) roles plus the
`org_member → reader` inheritance floor on org-owned scopes
(`StashRegistryDatabase.cs:602-668`). The search query implements only the
first of those three branches.

Concrete consequence:

- An org owner of `@acme` who has no direct `package_roles` row for
  `@acme/widget` cannot find `@acme/widget` in search even when it is
  `private` or `internal`, despite the explicit brief language that org
  owners inherit `owner` on every package in any scope owned by the org
  (brief, line 295-296).
- Likewise team-mediated readers and explicit org-principal role rows
  (`principal_type='org'`) are invisible to search.
- The `internal` visibility's org-member shortcut (brief line 198-200) is
  not honored at all — `internal` is treated identically to `private`
  for search purposes.

### Why this matters

- Direct contradiction of an acceptance-criteria-adjacent semantic in the
  brief; this is the exact "search filtered by visibility" requirement the
  brief calls out under D6/D7.
- Users will lose discoverability of their team's / org's own packages,
  which is the primary justification for the org/team model. The defect
  is silent — no error, just missing rows.
- Permission-resolution logic now lives in two places with different
  rules. The DB-helper version and the search version will continue to
  drift.

### Suggested fix

Either:

1. Materialise the visibility filter through `HasPackagePermissionAsync`
   semantics — e.g. for each candidate row apply the helper (acceptable for
   small result sets; document the perf cost matching the brief's "Open
   Questions" note on search predicate cost), **or**
2. Expand the LINQ predicate to union all three branches the helper
   implements: direct user role, team-mediated role
   (`PrincipalType='team'` with the user's TeamMembers), and org-mediated
   inheritance (`OrgMembers` rows pointing at the scope owner of the
   package), plus the org-principal explicit branch.

Make sure `internal` also returns rows where (a) scope is org-owned AND
caller is in `OrgMembers` for that org, OR (b) scope is user-owned AND
caller IS the scope owner — matching `CanReadPackageAsync` once F04 is
fixed.

Add a test in `RegistryVisibilityTests` (or `RegistryScopeAndOrgTests`)
that publishes `@acme/widget` as `private`, makes `alice` an `org owner`
of `@acme` *without* a direct package_roles row, and asserts the package
appears in `GET /api/v1/search?q=widget` for `alice`.

### Verify

```
dotnet test --filter "FullyQualifiedName~RegistryVisibilityTests|FullyQualifiedName~RegistryScopeAndOrgTests"
```

---

## F04 — [MEDIUM] `internal` visibility is treated as `private` — org-member shortcut never implemented

**Status:** fixed
**Fixed in:** d8cec0b
**Note:** `internal` now implements all three brief branches (a)/(b)/(c) in both `CanReadPackageAsync` (read gate) and `SearchPackagesAsync` (search predicate). Branch (b) — user-owned scope, caller is scope owner, no package_roles row — was the missing case; all branches are exercised by new tests.
**Files:** `Stash.Registry/Controllers/PackagesController.cs:616-647`
**Phase:** P4 (deferred to P5 in source comment; never landed)
**Commit:** 34c3c96 (deferred), no follow-up commit

### Observation

`CanReadPackageAsync` collapses `private` and `internal` into the same
"caller has at least `reader` permission" path:

```csharp
///   <item><description><c>internal</c> — same requirements as <c>private</c>
///       in P4; org-member shortcut is added in P5.</description></item>
private async Task<bool> CanReadPackageAsync(PackageRecord package)
{
    if (package.Visibility == "public") return true;
    ...
    return await _db.HasPackagePermissionAsync(package.Name, username, "reader");
}
```

The comment defers the `internal` org-member shortcut to P5, but P5 added
no such shortcut anywhere I can find — there is no `internal`-specific
branch in `PackagesController` or `StashRegistryDatabase`.

The brief is explicit (line 197-201):

> `internal` -> caller has a `read`-scoped token AND
>   (a) the scope is org-owned AND the caller is a member of that org, OR
>   (b) the scope is user-owned AND the caller is the scope owner, OR
>   (c) the caller has at least `reader` on the package directly.

What actually happens today: `internal` falls through to the
`HasPackagePermissionAsync` check, which *does* grant org members
`reader` (via the inheritance floor) when the scope is org-owned — so
branch (a) is reachable for org-owned scopes by accident, through the
permission table rather than the visibility table. But branch (b) (user
scope; caller is the scope owner) is not honored: a personal scope's
owner who hasn't been auto-assigned an `owner` `package_roles` row for
that specific package will be denied.

Per the brief (line 166): "The scope owner (user or the org backing the
scope) is auto-assigned `owner` on package creation during publish." If
that auto-assignment is reliable, branch (b) is effectively covered by
the `package_roles` table for any *published* package. But the code does
not document or rely on that invariant explicitly, and `internal` is the
same as `private` for any case the auto-assignment missed.

### Why this matters

- `internal` was sold in the brief as semantically distinct from
  `private`; today it isn't. Operators reading `Visibility=internal` on
  their package and expecting org-wide visibility for the org-owned case
  get behavior that *happens* to work via permission-table inheritance,
  not by a visibility-layer guarantee.
- The behavior is brittle: any future change that drops the
  `org_member → reader` permission floor would silently break `internal`.
- An end-to-end test asserting the brief's three-branch `internal`
  semantics does not appear to exist in `RegistryVisibilityTests`.

### Suggested fix

Implement the explicit branches in `CanReadPackageAsync`:

```csharp
if (package.Visibility == "internal") {
    // Brief: scope owner / org member / direct reader
    if (scope.OwnerType == "user" && scope.OwnerUsername == username) return true;
    if (scope.OwnerType == "org"  && await _db.IsOrgMemberAsync(scope.OwnerOrgId, username)) return true;
}
return await _db.HasPackagePermissionAsync(package.Name, username, "reader");
```

Add `RegistryVisibilityTests` cases covering:
- `internal` package in org-owned scope, caller is org member with no
  `package_roles` row → 200.
- `internal` package in user-owned scope, caller is the scope owner
  with no `package_roles` row → 200.
- `internal` package, caller is unrelated → 404.

Once landed, the SearchPackagesAsync `internal` predicate (F03) should
also gain the same shortcuts.

### Verify

```
dotnet test --filter "FullyQualifiedName~RegistryVisibilityTests"
```

---

## F05 — [MEDIUM] `POST /api/v1/scopes` does not reject collisions against usernames or org names

**Status:** open
**Files:** `Stash.Registry/Controllers/ScopesController.cs:95-145`
**Phase:** P5
**Commit:** 74ea0f7

### Observation

Plan `done_when` for P5 (plan.yaml:138) requires:

> POST /api/v1/scopes rejects collisions with usernames, existing scopes,
> and reserved system scopes.

Brief (line 122-126) further mandates:

> Usernames and org names share one pool: registration of a username or
> org name fails if a scope of the same name already exists. The
> registration controller rejects collisions with HTTP 409.

The reverse direction is asserted by the brief's
`POST /api/v1/scopes` endpoint (line 254-258):

> Rejects collisions with usernames, org names, existing scopes, and
> reserved system scopes.

`ScopesController.cs:95-145` checks only:
1. Reserved scope names (`stash`, `admin`) — line 102-105.
2. Existing scopes (`ScopeExistsAsync`) — line 108-109.

There is no `UserExists`-style check, and no org-name lookup independent
of the scope table.

In practice, because P3 auto-provisions `@<username>` at registration and
`POST /api/v1/orgs` creates a scope of the org's name, the *scope* table
transitively covers most collisions. But:

- A user pre-existing P3 (registered without auto-provision) has no scope
  row, so their name is grabbable.
- If an org is ever created without a backing scope (none today, but the
  invariant is not enforced by FK), the org name is grabbable.
- The defense-in-depth the brief explicitly asks for is absent.

### Why this matters

- Misses an explicit `done_when` of P5.
- Removes a defense layer the brief calls for as part of the unified
  namespace D19.
- Future code changes that decouple scopes from users/orgs (e.g., a
  user/org rename feature) would silently open the impersonation hole.

### Suggested fix

In `POST /api/v1/scopes`, before the existing-scope check, add explicit
lookups:

```csharp
if (await _db.GetUserAsync(scopeName) is not null)
    return Conflict(new ErrorResponse { Error = $"Scope '@{scopeName}' collides with an existing username." });
if (await _db.GetOrgAsync(scopeName) is not null)
    return Conflict(new ErrorResponse { Error = $"Scope '@{scopeName}' collides with an existing organization name." });
```

Add a `RegistryScopeAndOrgTests` case for each collision direction.

### Verify

```
dotnet test --filter "FullyQualifiedName~RegistryScopeAndOrgTests"
```

---

## F06 — [MEDIUM] No grammar validation on `scopes.name` at the DB layer — invalid scopes can be inserted

**Status:** open
**Files:** `Stash.Registry/Database/RegistryDbContext.cs:245-266`, `Stash.Registry/Database/StashRegistryDatabase.cs:507-544`, `Stash.Registry/Controllers/AuthController.cs:205-208`
**Phase:** P2 / P3
**Commit:** f228537 (schema), 3a5b0fd (auto-provision)

### Observation

`AuthController.Register` accepts usernames matching
`^[a-zA-Z0-9_-]+$` up to 64 chars. `CreateUserWithScopeAsync` then
inserts the same string into `scopes.Name` as the user's auto-provisioned
scope (`StashRegistryDatabase.cs:534-540`). The `scopes` table has no
`CHECK` constraint enforcing the scope grammar
(`[a-z][a-z0-9-]{0,38}`) — only `owner_type` and `single_owner`
constraints exist.

Result: a user `Alice_42` registers successfully, the row
`scopes(Name='Alice_42', OwnerType='user', ...)` is committed, but any
attempt to publish `@Alice_42/foo` fails `PackageManifest.IsValidPackageName`
because uppercase letters and underscores are illegal in scope segments
per the manifest grammar. The user has an unusable personal scope and no
diagnostic tells them why.

Same applies to org names if `POST /api/v1/orgs` does not validate
the name against the scope grammar before insertion.

### Why this matters

- Violates the unified-namespace D19 intent: usernames and scope names
  must be drawn from the same pool, with a single grammar.
- Silent failure mode: registration succeeds, publish fails opaquely.
- Brief line 134-135: "`organizations` — `id`, `name` (unique,
  lower-case, same grammar as scope)" — neither the model attributes,
  the EF model builder, nor the registration validator enforce this.

### Suggested fix

Either:

1. Tighten `AuthController.Register`'s username regex to match the scope
   grammar (`^[a-z][a-z0-9-]{0,38}$`) — most invasive but eliminates the
   class of bug entirely; or
2. Add a separate scope-grammar validator inside
   `CreateUserWithScopeAsync` and `OrganizationsController.Create` that
   rejects the request with 400 when the resulting scope name would be
   illegal; or
3. Add a CHECK constraint on `scopes.name` and let the DB reject the
   insert (less friendly error, but defense-in-depth).

(1) is the brief-aligned fix per D19. Document the breaking change for
existing username characters (`A-Z`, `_`).

Add a test asserting `POST /api/v1/auth/register` with username
`Alice_42` returns 400 with a grammar-error message.

### Verify

```
dotnet test --filter "FullyQualifiedName~RegistryScopeAndOrgTests|FullyQualifiedName~AuthControllerTests"
```

---

## F07 — [LOW] P6 done_when #3 satisfied at DB layer, not over HTTP

**Status:** open
**Files:** `Stash.Tests/Registry/RegistryRoutesTests.cs`, `Stash.Tests/Interpreting/PackageInstallerTests.cs` (blocked — F01)
**Phase:** P6
**Commit:** 6c9b005

### Observation

Plan `done_when` for P6 line 160 reads:

> An end-to-end CLI publish + install of @alice/widget against a fresh
> registry round-trips through the scoped routes.

`RegistryRoutesTests` exercises route shape via the DB / controller
seam, not via the CLI binary calling a live `WebApplicationFactory`.
The phase report acknowledged this because `PackageInstallerTests`
(the natural home for the e2e check) was unusable owing to F01.

Once F01 is resolved (CLI fixtures become scoped), reinstate or add a
genuine CLI-driven round-trip test that drives `stash pkg publish` and
`stash pkg install` against a `WebApplicationFactory`-hosted registry
for `@alice/widget`. The current `RegistryRoutesTests` coverage is
necessary but not sufficient for the `done_when` as written.

### Why this matters

- The `done_when` is the literal acceptance gate; partial satisfaction
  pushes risk into a future feature.
- Brief acceptance criterion (line 351-353) re-states the same
  end-to-end intent; passing P6 with only the DB-layer test leaves the
  CLI→HTTP→DB seam unverified for this feature.

### Suggested fix

After F01, add (or restore) a test that:

1. Boots a `WebApplicationFactory<Startup>` with a fresh SQLite DB.
2. Calls the registered-user happy path: register `alice`, claim no
   extra scopes (her `@alice` is auto-provisioned).
3. Drives `Stash.Cli.PackageManager.RegistryClient.Publish` for
   `@alice/widget`.
4. Drives `RegistryClient.GetPackageMetadata` / `DownloadPackage` for
   the same name.
5. Asserts the round-trip via response status and body.

`PackageInstallerTests` is the right home once its fixtures are scoped.

### Verify

```
dotnet test --filter "FullyQualifiedName~PackageInstallerTests&FullyQualifiedName~RoundTrip"
```

---

## F08 — [LOW] Audit action strings `owner.add` / `owner.remove` retained after role model rename

**Status:** open
**Files:** `Stash.Registry/Services/AuditService.cs:47,60`, `docs/Registry — Package Registry.md:1115-1116`
**Phase:** P5 (model rename), P7 (docs)
**Commit:** 74ea0f7, effe750

### Observation

The role model moved off "owners" → "package roles"
(`PackageRoleEntry`, `AssignRoleRequest`, `/roles` routes). The audit log
still emits action strings `owner.add` and `owner.remove`, and the
public documentation now footnotes them with "Package role assigned
(principal added or role changed) / Package role revoked" — i.e., the
wire string no longer matches its meaning.

This is plausibly intentional (preserving the wire contract for
downstream audit-log consumers), but nothing in the brief or `plan.yaml`
calls it out, and the new documentation reads awkwardly.

### Why this matters

- Audit-log consumers parsing on the action string see stale names.
- Future developers will reasonably assume "owner" still means the
  removed `OwnerEntry` model.
- If the rename is intentional, a one-line note in
  `docs/Registry — Package Registry.md` would close the gap with minimal
  cost. If it is a missed rename, the right action strings are
  `role.assign` / `role.revoke`.

### Suggested fix

Decide and document:

- If wire-contract preservation: add a sentence under §13 stating "Action
  names `owner.add` and `owner.remove` are retained for log-consumer
  compatibility; they now denote any package role assignment / revocation
  respectively." No code change.
- If accidental retention: rename the constants in `AuditService.cs` to
  `role.assign` / `role.revoke`, update the docs table, and surface a
  brief note in `repo.md` Known Issues / changelog for log consumers.

This is intentionally a LOW finding because the behavior is not broken,
only confusing.

### Verify

```
grep -n "owner.add\|owner.remove" Stash.Registry/ docs/
# review the doc note; no test assertion required unless renaming.
```

---

## Summary

| Severity | Count |
| -------- | ----- |
| CRITICAL | 0     |
| HIGH     | 3     |
| MEDIUM   | 3     |
| LOW      | 2     |
| **Total**| **8** |

Pre-existing baseline noise verified not feature-caused (do not become
findings; documented for posterity):

- `DiffPackageTests` ×35 — flaky per `.claude/repo.md`.
- `AuthEndpointTests` ×2, `FirstRegistrationAdminTests` ×2 — environment
  `DirectoryNotFoundException`, no source touched by this feature.
- `BasePathIntegrationTests` ×2 — environment `DirectoryNotFoundException`
  and a stale-DB `no such table: scopes` failure tied to D3's no-migration
  posture (`EnsureCreated()` does not retro-add tables to existing DBs).
  This is operational guidance, not a schema defect — add a Known-Issues
  note to `.claude/repo.md` if not already present.
- `NamespaceMembersDapTests` ×1 — DAP/os-namespace area, unrelated.

The 6 `IntegrityVerificationTests` failures share the F01 root cause and
are currently masked by `final_verify`'s `IntegrityVerification` exclude.
Treat them as part of F01's verify step (broaden the filter after fix).
