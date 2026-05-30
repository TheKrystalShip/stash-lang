# registry-authz-pipeline — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header is parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve registry-authz-pipeline Fxx` reads the selected section verbatim.

**Scope reviewed:** `1072728..HEAD` restricted to plan globs (`Stash.Registry/**`, `Stash.Tests/Registry/**`, `docs/Registry — Package Registry.md`, `examples/registry/**`) — 73 files, ~9 965 / ~967 LOC.
**Brief:** ../brief.md
**Generated:** 2026-05-30

## Overall

The feature substantially delivers the brief: PDP `IRegistryAuthorizer` is in place; ceiling-first holds; the `RegistryAction` enum is closed; the role matrix is wired; Bug A (scope-squat) and Bug B (manifest/route mismatch) are closed structurally with regression tests; the admin-narrow intersection holds; `AuthzCoverageMetaTests` and `NoMagicAuthStringsMetaTests` both ship with positive **and** negative self-tests plus a floor guard; P8 cleanup grep returns zero matches for every banned symbol (`HasPackagePermissionAsync`, `CanReadPackageAsync`, `AuthPolicies`, the four legacy string policies); P7 wired the five formerly-stranded AdminController endpoints (`GetStats`, `CreateUser`, `DeleteUser`, `AdminAssignRole`, `GetAuditLog`) to the PDP with non-admin → 403 regression guards in `OrgAndAdminAuthzTests`; docs and the canonical happy-path example landed.

The findings below are real gaps against the brief, not stylistic complaints. F01 is the largest: AuthController's entire token-management surface bypasses the PDP, which both contradicts the D1 "single PDP" goal and means the `IssueToken`/`Whoami`/`ListOwnTokens`/`RevokeOwnToken` ceiling logic in `RegistryAuthorizer.RequiredCeiling` is dead code. F02 (the `scope`→`ceiling` alias) directly contradicts D11. F03–F04 are smaller PDP-bypass shapes on `DeleteScope`/`DeleteOrg` with a missing audit-deny signal. F05 covers production magic-string sites the sink-scoped meta-test cannot catch (write-call sites + visibility literals + DB defaults).

---

## F01 — [HIGH] AuthController never calls the PDP — entire token surface bypasses `IRegistryAuthorizer`

**Status:** fixed
**Fixed in:** dcf590a
**Files:** `Stash.Registry/Controllers/AuthController.cs:251` (Whoami), `Stash.Registry/Controllers/AuthController.cs:268` (ListTokens), `Stash.Registry/Controllers/AuthController.cs:312` (CreateToken), `Stash.Registry/Controllers/AuthController.cs:438` (RevokeToken), `Stash.Registry/Controllers/AuthController.cs:482` (DeleteToken); cross-reference dead ceiling rows at `Stash.Registry/Auth/Authorization/RegistryAuthorizer.cs:105-107,127`
**Phase:** P5 (cross-cutting against D1)
**Commit:** 5e7ec13

### Observation

Five `AuthController` actions reach the controller body without ever calling `IRegistryAuthorizer.AuthorizeAsync`. They rely on raw `User.Identity!.Name!` plus inline `User.FindFirstValue(ClaimTypes.Role)` checks for self-service authorization:

- `CreateToken` (line 314) — only enforces "admin role required for admin ceiling" inline at line 350-354; never asks the PDP. The PDP's `RegistryAction.IssueToken` ceiling mapping at `RegistryAuthorizer.cs:127` (`TokenCeiling.Publish`) is unreached.
- `RevokeToken` / `DeleteToken` — re-implement token-ownership inline (lines 452-458, 487-499). The PDP has `AuthorizeRevokeOwnTokenAsync` (`RegistryAuthorizer.cs:477-494`) that does exactly this; it is dead.
- `Whoami` / `ListTokens` — inline; PDP rows `Whoami`/`ListOwnTokens` at `RegistryAuthorizer.cs:105-106` are dead.

Operationally observable: the published example `examples/registry/authz-flow.md` Step 2 uses a `read`-ceiling login token to `POST /api/v1/auth/tokens` and mint a `publish`-ceiling token. The example works only **because** the PDP is not called — if the PDP's `IssueToken => TokenCeiling.Publish` ceiling were enforced, a read-ceiling token would receive `403 TokenScopeInsufficient` on `POST /auth/tokens`, breaking the documented elevation flow end-to-end.

So there are two coupled problems: (a) controller-side PDP bypass, and (b) the PDP's recorded required ceiling for `IssueToken` would be wrong (too strict) if it were ever consulted. Both have to be resolved together.

### Why this matters

- **Brief D1 parity gap.** Brief Goals (line 91–93): "One Policy Decision Point (PDP) for every authorization decision in the registry, keyed by `(Action, Resource, Principal)`. Reads and writes go through the SAME engine." `AuthController` is exempt today. This is the structural property the redesign was built around — Bug A and Bug B existed *because* checks lived in controllers AND services on different inputs, and the auth surface was the very example.
- **Dead PDP rows breed silent drift.** Five rows in `RegistryAction` + `RegistryAuthorizer.RequiredCeiling` (`Whoami`, `IssueToken`, `ListOwnTokens`, `RevokeOwnToken`, the resource branch `AuthorizeRevokeOwnTokenAsync`) are unreachable from production. A future change to those rows will pass tests but not change real behavior, and a future change to the controller will not be cross-validated by the PDP. This is precisely the divergence shape D1 was meant to retire.
- **Audit-trail gap.** `AuditService.LogAuthzDenyAsync` is wired through the PDP-call path everywhere else (the `if (!decision.Allowed) { LogAuthzDenyAsync(...); ... }` template). The auth-surface's bespoke 401/403 returns at `AuthController.cs:353,457,498` emit no `LogAuthzDenyAsync` entry, contradicting D19 ("every authenticated authorization denial emits a deny audit entry").
- **No active CVE today** — the inline checks are equivalent-or-stricter than what the PDP would compute for `ManageUser`-style admin actions, and `Login`/`Register` are public. But the same was true of the AdminController endpoints P7 was inserted to fix; the durable risk is exactly the "next change opens the gap silently" shape that motivated this entire feature.

### Suggested fix

1. Decide the canonical ceiling for `RegistryAction.IssueToken`: the brief intent (D6) is that the `read`-ceiling login token must be able to mint a publish-ceiling token via `auth/tokens`, so `IssueToken` should map to `TokenCeiling.Read`, not `Publish`. Update `RegistryAuthorizer.RequiredCeiling` (`Stash.Registry/Auth/Authorization/RegistryAuthorizer.cs:127`) accordingly and add a matrix cell in `RegistryAuthzMatrixTests` asserting a `read` token can call `IssueToken`.
2. Wire all five `AuthController` actions to `_authorizer.AuthorizeAsync` using the existing template (`BuildPrincipal` → `AuthorizeAsync(action, resource)` → on deny `LogAuthzDenyAsync` + 401/403). Resources: `PrincipalSelfResource()` for `Whoami`/`IssueToken`/`ListOwnTokens`, `TokenResource(id)` for `RevokeOwnToken`. The PDP already implements the "owner-of-token" check in `AuthorizeRevokeOwnTokenAsync`; delete the inline ownership check in `RevokeToken`/`DeleteToken`.
3. Preserve the admin-narrow ceiling-first cell at the controller level only as a `[Authorize]` baseline; let the PDP carry the typed deny reasons (the current "Only admin users can create admin-ceiling tokens" 403 should map to `AuthzDenyReason.TokenScopeInsufficient` or `PolicyDenied`, set in `RegistryAuthorizer`, not the controller).
4. Add `AuthControllerPdpDispatchTests` (or extend `LoginReadDefaultTests`) asserting that `POST /auth/tokens` with a `read`-ceiling token still mints a `publish` token (regression for the example), and that `DELETE /auth/tokens/{id}` for a token owned by a different user returns `403` with reason surfaced through the PDP (the existing assertion stays green but is now PDP-mediated).

### Verify

```
dotnet test --filter "FullyQualifiedName~LoginReadDefaultTests|FullyQualifiedName~TokenLifetimeCapTests|FullyQualifiedName~AdminNarrowCeilingTests|FullyQualifiedName~JtiRevocationPreservedTests|FullyQualifiedName~RegistryAuthzMatrixTests|FullyQualifiedName~AuthzCoverageMetaTests"
```

---

## F02 — [MEDIUM] `POST /auth/tokens` accepts `scope` as a backwards-compat alias for `ceiling` — direct violation of D11

**Status:** fixed
**Fixed in:** dcf590a
**Files:** `Stash.Registry/Controllers/AuthController.cs:340`, `Stash.Registry/Contracts/AuthContracts.cs` (the `Scope` field on `TokenCreateRequest`), `docs/Registry — Package Registry.md:332`
**Phase:** P5
**Commit:** 5e7ec13

### Observation

`CreateToken` resolves the ceiling at line 340 with `string? rawCeiling = body?.Ceiling ?? body?.Scope;` — `scope` is a live, code-level backwards-compatible alias for `ceiling`. The docs at line 332 even advertise it: "The `scope` field is accepted as a backwards-compatible alias for `ceiling` when `ceiling` is absent."

### Why this matters

The brief's D11 explicitly forbids this shape: "Hard clean break: old `TokenRecord.Scope` shape and four `RequireXxxScope` policies replaced outright … No alias layer; no parallel `/api/v2`. … pre-1.0; shim would be load-bearing dead code." The registry has no deployed consumers (per the project-memory note) — there is no client to be backwards-compatible *with*. The alias is a load-bearing shim against a client that does not exist, contradicting the explicit non-goal.

A second-order issue: `body?.Ceiling ?? body?.Scope` also means that when both fields are present and disagree, the client's intent is ambiguous — `Ceiling` wins. Better to fail fast than to silently pick one.

### Suggested fix

- Remove the `Scope` field from `TokenCreateRequest` in `Stash.Registry/Contracts/AuthContracts.cs`.
- In `CreateToken`, drop the `?? body?.Scope` fallback at `AuthController.cs:340`.
- Delete the "backwards-compatible alias" sentence from `docs/Registry — Package Registry.md:332` (the surrounding paragraph documenting `ceiling` is fine as-is).
- Add a regression cell in `TokenLifetimeCapTests` (or a new `TokenCreateContractTests`) asserting that a request body with `{"scope":"publish","expires_in":"30d"}` (no `ceiling`) is rejected `400` with a typed error explaining that `ceiling` is mandatory.

### Verify

```
dotnet test --filter "FullyQualifiedName~TokenLifetimeCapTests|FullyQualifiedName~LoginReadDefaultTests"
```

---

## F03 — [MEDIUM] `DELETE /scopes/{scope}` and `DELETE /orgs/{org}` route the PDP call through the wrong action and then re-decide locally; missing audit-deny

**Status:** open
**Files:** `Stash.Registry/Controllers/ScopesController.cs:228-269` (`DeleteScope`), `Stash.Registry/Controllers/OrganizationsController.cs:163-195` (`DeleteOrg`)
**Phase:** P4
**Commit:** 635c7bc

### Observation

Both delete endpoints exhibit the same shape:

- `DeleteScope` calls `_authorizer.AuthorizeAsync(principal, RegistryAction.ClaimScope, ScopeResource(scope))` — `ClaimScope` is a semantic proxy ("can you claim this scope?") for "can you delete this scope?" There is no `DeleteScope` value in the closed `RegistryAction` enum. It then **suppresses** the PDP deny in the specific case `decision.Reason == ScopeNotOwned` (line 237) and re-implements ownership locally with `User.IsInRole(UserRoles.Admin)` and a direct record comparison (lines 250-255).
- `DeleteOrg` does the same, using `AddOrgMember` as the proxy action and suppressing `OrgMembershipRequired`/`PolicyDenied` (lines 168-169), then re-implements `IsOrgOwnerAsync || isAdmin` locally (lines 182-184).

When the local-only path returns 403 (line 255, 259 in ScopesController; 184 in OrganizationsController), no `LogAuthzDenyAsync` call is made — these 403 paths emit zero audit entries.

The de-escalation that keeps this MEDIUM (not the P7 admin-escalation shape): the bespoke local checks are equal-or-stricter than the suppressed PDP deny in every branch — there is no input where the local code ALLOWs what the PDP would DENY. The risk is structural, not exploitable today.

### Why this matters

- **D1 single-PDP parity.** The brief is unambiguous: every authz decision goes through the PDP. A controller that calls the PDP, conditionally ignores its decision, and re-decides locally is exactly the shape the redesign retired everywhere else.
- **D19 audit-coverage gap.** Brief §"Audit logging of authorization decisions": "Every authenticated authorization denial emits a deny audit entry carrying the `AuthzDenyReason` enum value verbatim. This exists to feed intrusion-detection on abnormal deny rates per principal." The 403 paths at `ScopesController.cs:255,259` and `OrganizationsController.cs:184` produce no `AuditService` entry — an authenticated `bob` repeatedly probing `DELETE /api/v1/scopes/{victim}` will accumulate zero deny entries, defeating the intrusion-detection signal.
- **Missing regression guard for `DeleteOrg_NonOwner`.** `OrgAndAdminAuthzTests` has `DeleteScope_NonOwner_Returns403` (line 170) but no `DeleteOrg_NonOwner_Returns403`. The DeleteOrg-bespoke check is the same shape that P7 added to surface — without an explicit regression test, a future cleanup that drops the local check would not redden the suite.

### Suggested fix

1. Add `DeleteScope` and `DeleteOrg` to the `RegistryAction` enum and to `RegistryAuthorizer.CheckResourceAsync` (the resource-side handler can delegate to the existing owner/admin checks). Map their ceilings to `Publish` and map `ScopeReserved`/`ScopeNotOwned`/`OrgMembershipRequired` deny paths to controller 403 the standard way.
2. Update both controllers to use the standard template (no suppressed deny reasons, no local re-decision, deny path goes through `LogAuthzDenyAsync`).
3. Add `DeleteOrg_NonOwner_Returns403` to `OrgAndAdminAuthzTests.cs` mirroring `DeleteScope_NonOwner_Returns403`, plus assert a single `deny` audit entry is appended for both.

### Verify

```
dotnet test --filter "FullyQualifiedName~OrgAndAdminAuthzTests|FullyQualifiedName~CascadeRefusalTests|FullyQualifiedName~RegistryAuthzAuditMutationTests|FullyQualifiedName~RegistryAuthzMatrixTests"
```

---

## F04 — [LOW] Production bounded-domain literals outside `RegistryAuthConstants` — sink-scoped meta-test does not catch them

**Status:** open
**Files:** `Stash.Registry/Services/PackageService.cs:185` (`"user"`, `"owner"`), `Stash.Registry/Auth/Authorization/RegistryAuthorizer.cs:256` (`visibility == "public"`), `Stash.Registry/Auth/Authorization/RegistryAuthorizer.cs:273` (`visibility == "internal"`), `Stash.Registry/Database/StashRegistryDatabase.cs:152` (`p.Visibility == "public"`), `Stash.Registry/Database/Models/PackageRecord.cs:63` (`Visibility = "public"` default), `Stash.Registry/Database/RegistryDbContext.cs:92` (`.HasDefaultValue("public")`), `Stash.Registry/Database/RegistryDbContext.cs:125` (`.HasDefaultValue("user")`), `Stash.Registry/Auth/LocalAuthProvider.cs:89` (`CreateUserAsync(..., "user")`), `Stash.Registry/Controllers/PackagesController.cs:658` (duplicated `validVisibilities` literal array)
**Phase:** P1 (visibility literals); P3 (PackageService write); cross-cutting
**Commit:** e8e69ad / 154671b

### Observation

The brief's "Bounded-domain auth strings" standing constraint (and the project-wide CLAUDE.md rule) requires bounded values to come from a named source of truth. The brief is explicit that the **NoMagicAuthStrings sink scanner is auth-sink-scoped by design** and that comparison-domain centralization is the accepted bar for non-sink call sites. The following production sites violate the centralization rule for two bounded domains the codebase otherwise handles correctly:

- **Package visibility** — the closed set `{public, private, internal}` has no named home. The literal appears in PDP comparisons (`RegistryAuthorizer.cs:256, 273`), DB query (`StashRegistryDatabase.cs:152`), EF default values (`RegistryDbContext.cs:92`, `PackageRecord.cs:63`), and as a duplicated validation array (`PackagesController.cs:658`).
- **PrincipalType + PackageRole at the DB write site** — `PackageService.cs:185` writes `AssignPackageRoleAsync(packageName, "user", username, "owner")` instead of `PrincipalTypes.User` / `PackageRoles.Owner`, which are already defined and used everywhere else.
- **UserRole at the auth-provider** — `LocalAuthProvider.cs:89` calls `CreateUserAsync(username, hash, "user")` instead of `UserRoles.User`. `RegistryDbContext.cs:125` mirrors with `HasDefaultValue("user")`.

`NoMagicAuthStringsMetaTests` does not flag any of these — its sink set is `{IsInRole, FindFirstValue, FindFirst, HasClaim, RequireClaim, RequireRole}`. These sites are correctly outside the sink scan, but they violate the broader centralization rule the brief calls out as the future-100% hardening direction.

### Why this matters

- Each centralization gap is a place where renaming or extending the bounded set (adding e.g. a `restricted` visibility) requires hand-finding every literal — exactly the rake the bounded-domain rule exists to prevent.
- The visibility case is the most worth fixing because it spans **four layers** (PDP, query, EF default, model default) plus a duplicated validation array. A `Visibilities` static class on the model of `PackageRoles` would collapse all five into one named home.
- Severity is LOW (not MEDIUM) because the meta-test's sink scope is deliberately narrow per the brief, and the brief explicitly accepts comparison-domain centralization as the bar rather than a real `enum` + EF value converter. This finding is the convention nudge, not a meta-test failure.

### Suggested fix

1. Add a `Visibilities` static class to `Stash.Registry/Auth/RegistryAuthConstants.cs` with `Public`, `Private`, `Internal` constants plus a `IsValid(string)` predicate and an `All` array. Replace the literal at every site cited above. (Promotion to a real `enum` + EF value converter — the 100% direction the brief calls out — can be tracked separately if scoped out.)
2. Replace `"user"`/`"owner"` at `PackageService.cs:185` with `PrincipalTypes.User` / `PackageRoles.Owner`.
3. Replace `"user"` at `LocalAuthProvider.cs:89` with `UserRoles.User`; replace the EF defaults at `RegistryDbContext.cs:125` with `UserRoles.User` (and `RegistryDbContext.cs:92` / `PackageRecord.cs:63` with the new `Visibilities.Public`).
4. Delete the duplicated `validVisibilities` literal array in `PackagesController.cs:658`; reference the new `Visibilities.All`.

### Verify

```
dotnet test --filter "FullyQualifiedName~Registry&FullyQualifiedName!~BasePathIntegrationTests&Category!=SqliteConcurrencyStress"
```
