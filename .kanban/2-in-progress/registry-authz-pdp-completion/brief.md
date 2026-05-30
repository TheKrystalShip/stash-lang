# RFC: Registry Authorization PDP Completion

> **Status:** Draft (specced, not started)
> **Owner:** Cristian Moraru
> **Created:** 2026-05-31
> **Slug:** registry-authz-pdp-completion

## Summary

`registry-authz-filter` reduced ~30 controller endpoints to `[RegistryAuthorize(RegistryAction)]` and pinned the four remaining endpoints that still hold inline PDP-coordination logic under `[ImperativeAuthz("reason")]`: `PackagesController.PublishPackage`, `OrganizationsController.DeleteOrg`, `ScopesController.ClaimScope`, `ScopesController.DeleteScope`. This feature folds three of those four endpoints' authorization decisions into the PDP so they become plain `[RegistryAuthorize(action)]` endpoints, and **explicitly keeps `ClaimScope` as the documented irreducible imperative remnant** (its scope name and `ownerType`/`owner` fields live in the JSON body, not the route — folding it would force the shared filter to buffer request bodies, breaking the pure-resolver invariant that protects every other endpoint).

The end-state `[ImperativeAuthz]` pin shrinks from `{PublishPackage, DeleteOrg, ClaimScope, DeleteScope}` to `{ClaimScope}` (with a corrected, accurate reason string and a backlog entry pointing at the body-resolver path as the only way to reach `{}`). Unlike `registry-authz-filter`, this is **decision-changing / decision-formalizing** work: every fold lands its own new/updated `AuthzMatrixData` and `RegistryAuthzAuditMutationTests` rows in the same phase so the matrix moves with the decision, not behind it.

## Motivation

- **The catalogue of "imperative" endpoints is now partly false.** Two of the four pinned `[ImperativeAuthz]` reasons describe behavior that no longer matches the code:
  - `OrganizationsController.DeleteOrg`'s reason says it "uses `AddOrgMember` authz action (known wrong-action bug)". The bug was already fixed by `registry-authz-pipeline` F03 — the controller now correctly calls `AuthorizeAsync(..., RegistryAction.DeleteOrg, ...)`. The remaining inline logic is just the boilerplate `Build principal → call PDP → audit → AuthzDenyResponse.For(decision)` block — every line of which is what the shared filter already does. Folding is mechanical with zero behavior delta.
  - `ScopesController.DeleteScope`'s reason says "post-PDP audit targets `@{scope}` prefix requiring inline coordination". This is a one-character audit-string difference (the filter writes bare `scope` for `ScopeResource`, the controller writes `@{scope}`), not a real inline-coordination requirement.
- **`PublishPackage`'s dynamic action choice is the only nontrivial fold.** `PublishPackage` chooses `CreatePackage` vs `PublishVersion` from a DB existence check executed **before** the PDP call. The shared filter's resource resolver is pure (route-only, no DB, no body) by design — that invariant is what keeps the filter free of DI-lifetime hazards and request-body buffering across 30+ endpoints. The right place for "branch the action by a DB lookup" is inside the PDP itself, where DB access is already a first-class capability.
- **`ClaimScope` cannot fold without breaking the shared filter's invariants.** Its scope name is in the JSON body, and its post-PDP checks (caller-owns owner? caller is org owner of `owner`?) need body fields (`ownerType`, `owner`) that the PDP never receives. The honest answer is to leave it pinned, correct its reason string, and remove the dead `ClaimScope` arm from the route-only resolver. The previous brief's "design how to fold ClaimScope" expectation does not survive contact with the code.
- **The pin must stay tight.** The `AuthzDispatchCoverageMetaTests` imperative-pin is the auditability hinge — every endpoint marked `[ImperativeAuthz]` is a deviation from the canonical `[RegistryAuthorize]` pattern. Letting it sit at four when only one is genuinely irreducible erodes the meta-test's signal.

## Goals

- `OrganizationsController.DeleteOrg` carries `[Authorize] + [RegistryAuthorize(RegistryAction.DeleteOrg)]`; its body contains only the actual delete work. No inline PDP call, no inline deny block, no inline `LogAuthzDenyAsync`.
- `ScopesController.DeleteScope` carries `[Authorize] + [RegistryAuthorize(RegistryAction.DeleteScope)]`; its body contains only the actual delete work. The deny-audit `resource_id` becomes `@{scope}` uniformly via the shared filter (decision change — see Semantics).
- `PackagesController.PublishPackage` carries `[Authorize] + [RegistryAuthorize(RegistryAction.PublishPackage)]`. A new `RegistryAction.PublishPackage` enum member exists; its PDP handler performs the `PackageExistsAsync` check and delegates to the existing create / publish authorization paths. The deny-audit action label becomes `PublishPackage` (decision change — see Semantics). The allow-path mutation audit labels (`package.create` / `package.publish`) are unchanged because they are driven by `PackageService.PublishAsync`'s `isNewPackage` return, not by the authz action.
- `ScopesController.ClaimScope` keeps `[ImperativeAuthz("…")]` with an accurate reason string naming the body-resolver constraint as the real blocker, and links to a backlog stub describing the body-buffering refactor that would unlock it.
- The dead `ClaimScope` arm in `RegistryActionResourceResolver.Resolve` (currently produces `ScopeResource("")` from a nonexistent `{scope}` route param) is removed. `ClaimScope` falls into the `throw new InvalidOperationException(...)` default — if anyone in the future tries to mark `ClaimScope` `[RegistryAuthorize]` without first refactoring the body path, the filter throws loudly.
- `AuthzDispatchCoverageMetaTests.PinnedImperativeActions` equals exactly `{"ScopesController.ClaimScope"}`. The fail-path fixtures and self-tests continue to demonstrate the scan has teeth.
- `AuthzMatrixData` carries new rows for each decision change: a `publish.deny.label` row asserting the new `PublishPackage` deny-action label, a `delete_scope.deny.audit_id` row asserting the `@{scope}` deny audit-id, and a `delete_org.deny.audit_id` row asserting the bare `{org}` deny audit-id (locks in that the fold did NOT change DeleteOrg's audit shape).
- `RegistryAuthzAuditMutationTests` gains rows asserting one deny entry on each of the three folded endpoints with the new label / id shape, plus a row asserting `VerifyScope` deny audit-id is now `@{scope}` (locks in the uniform `ScopeResource` prefix change).
- `Stash.Registry/CLAUDE.md` "Controller Pattern" table is updated: three rows removed, one row remains (`ClaimScope`) with the corrected reason. The "Request Flow" / `[ImperativeAuthz]` documentation explicitly states that `ClaimScope` is the documented permanent remnant and links to the backlog stub that would unlock the final fold.
- All meta-tests stay green: `NoMagicAuthStringsMetaTests`, `AuthzCoverageMetaTests`, `AuthzDispatchCoverageMetaTests`, `Wave1ThrowsCoverageTests`, `CompletionSurfaceSnapshotTests`, `StandardLibraryReferenceTests`. The full registry suite (`RegistryAuthzMatrixTests`, `RegistryAuthzAuditMutationTests`, `RegistryAuthorizerTests`, `OrgAndAdminAuthzTests`, `ScopeOwnershipPolicyTests`, etc.) stays green.

## Non-Goals

- **`ClaimScope` is not folded.** The shared filter's pure-route resolver invariant, the body-derived `ownerType`/`owner` checks, and the bespoke 409 status mapping for `ScopeReserved`/`ScopeNotOwned` together make ClaimScope structurally different. We document it as the irreducible remnant, correct the reason string, and stop. A future feature could fold it if the team accepts a body-buffering authorization filter; that decision belongs in a separate spec, not this one.
- **No re-architecture of the shared filter.** `RegistryAuthorizeFilter`, `AuthzDenyResponse`, `RegistryActionResourceResolver` keep their shape. The only resolver edit is adding `PublishPackage` (delegating to the existing `PackageResource` path) and deleting the dead `ClaimScope` arm. The only audit-id helper change is `ScopeResource → "@" + scope.Scope` (decision change).
- **`CreatePackage` and `PublishVersion` enum members are not removed.** They remain referenced by the PDP internally (the `PublishPackage` handler dispatches to the existing `AuthorizeCreatePackageAsync` / package-publish branch). Their resolver arms remain so any downstream code that still emits these actions directly (e.g., test fixtures, `RegistryAuthorizerTests` matrix rows) continues to work. They are simply no longer the authz action attached to the controller endpoint.
- **No removal or renaming of allow-path mutation audit labels.** `package.create` / `package.publish` are still emitted by `AuditService.LogMutationAllowAsync` from inside `PublishPackage`'s body based on `isNewPackage`. This brief touches only the deny-side action label that comes from `_action.ToString()`.
- **No JTI/middleware/`[Authorize]` changes.** Same as the predecessor — authentication and JTI revocation are out of scope.
- **No language/LSP/DAP work.** Registry-only change; `.claude/language-changes.md` does not apply.
- **No automated rewrite of historical audit rows.** The registry is pre-release (see project memory `project_registry_pre_release.md`); there is no deployed history to migrate. The audit-label / audit-id changes apply forward-only.

## Design

The end state is three controllers with three more `[RegistryAuthorize(action)]` endpoints (their imperative blocks deleted), one new `RegistryAction.PublishPackage` enum member whose PDP handler internally branches by `PackageExistsAsync`, a one-line change in `RegistryAuthorizeFilter.ResourceIdForAudit` that prefixes scope resource ids with `@`, a tightened `[ImperativeAuthz]` pin set, and a corrected `ClaimScope` reason string.

### Surface

```csharp
// New PDP action — replaces CreatePackage/PublishVersion as the action attached to the PUT endpoint.
public enum RegistryAction
{
    // … existing members …
    CreatePackage,    // still exists — internal PDP delegation target + test fixtures
    PublishVersion,   // still exists — internal PDP delegation target + test fixtures
    PublishPackage,   // NEW — the action attached to PUT /api/v1/packages/{scope}/{name}
    // … existing members …
}
```

Controller surfaces (before → after):

```csharp
// PublishPackage  — BEFORE: [ImperativeAuthz]("dynamic action choice…")
// AFTER:
[Authorize]
[RegistryAuthorize(RegistryAction.PublishPackage)]
[HttpPut("{scope}/{name}")]
public async Task<IActionResult> PublishPackage(string scope, string name) {
    // body is now pure work: parse headers, call _packageService.PublishAsync,
    // emit allow-side mutation audit by isNewPackage, return 201 / 400 / 409 as today.
}

// DeleteOrg — BEFORE: [ImperativeAuthz]("uses AddOrgMember…" — false; bug fixed by F03)
// AFTER:
[Authorize]
[RegistryAuthorize(RegistryAction.DeleteOrg)]
[HttpDelete("{org}")]
public async Task<IActionResult> DeleteOrg(string org) {
    // body is the GetOrgAsync 404 + DeleteOrgAsync + 409 OrgNotEmpty mapping.
}

// DeleteScope — BEFORE: [ImperativeAuthz]("post-PDP audit targets @{scope}…")
// AFTER:
[Authorize]
[RegistryAuthorize(RegistryAction.DeleteScope)]
[HttpDelete("{scope}")]
public async Task<IActionResult> DeleteScope(string scope) {
    // body is the GetScopeAsync 404 + DeleteScopeAsync + 409 ScopeNotEmpty mapping.
}

// ClaimScope — stays imperative with a corrected reason string
[Authorize]
[ImperativeAuthz(
    "scope name + ownerType + owner fields live in the JSON body, not the route; " +
    "the shared filter's route-only resolver cannot produce a ScopeResource for this " +
    "endpoint, and post-PDP body-driven ownership checks (user|org owner) plus the " +
    "bespoke 409 status mapping for ScopeReserved/ScopeNotOwned make this the documented " +
    "irreducible remnant. See .kanban/0-backlog/registry/Body-resolver authz filter.md " +
    "for the refactor that would unlock the final fold.")]
[HttpPost]
public async Task<IActionResult> ClaimScope() { /* unchanged */ }
```

### Semantics

**`RegistryAction.PublishPackage` (new) — PDP handler.**

A new switch arm in `RegistryAuthorizer.AuthorizeAsync` for `RegistryAction.PublishPackage`:

1. If `resource is not PackageResource pkg` → `Deny(PolicyDenied, "Expected PackageResource.")` (matches the existing per-action shape).
2. If `username == null` → `Deny(NotAuthenticated)`.
3. Look up `bool exists = await _ctx.Packages.AsNoTracking().AnyAsync(p => p.Name == pkg.FullName)` (the same predicate the controller runs today via `_db.PackageExistsAsync`).
4. If `exists`, delegate to the existing `PublishVersion` resource-side handler. If not, delegate to `AuthorizeCreatePackageAsync(username, isAdmin, pkg)`.
5. The token-ceiling table gets one new entry: `RegistryAction.PublishPackage => TokenCeiling.Publish`. Both delegation targets already require `Publish`; the ceiling check happens once for `PublishPackage` and is not double-checked inside delegation.

**Deny-audit action label change (PublishPackage).**

Today, deny-side audit on a publish attempt logs `CreatePackage` or `PublishVersion` depending on existence. After fold, it logs `PublishPackage` uniformly. This is the **single decision-changing wire shift for PublishPackage** and is asserted by a new `AuthzMatrixData` row (`publish.deny.label = "PublishPackage"`) and a new `RegistryAuthzAuditMutationTests` row.

**Deny-audit resource-id change (ScopeResource).**

`RegistryAuthorizeFilter.ResourceIdForAudit(ScopeResource)` changes from `scope.Scope` to `"@" + scope.Scope`. This makes the filter's audit `resource_id` for every `ScopeResource` action — `ResolveScope`, `VerifyScope`, `DeleteScope` — write `@{scope}` instead of bare `scope`. The change is uniform on purpose: `@{scope}` is already the canonical scope identifier elsewhere in the codebase (e.g., `PackageResource.FullName` → `@{Scope}/{LocalName}`), and aligning all `ScopeResource` audit rows on the prefixed form is more consistent than letting only the controller-folded one keep it. The change is asserted by:

- a new `RegistryAuthzAuditMutationTests` row for `DeleteScope` (denied → one entry, `resource_id == "@" + scope`),
- a new row for `VerifyScope` (denied → one entry, `resource_id == "@" + scope`) — locks in the *uniform* application of the change, not just the folded endpoint,
- a new `AuthzMatrixData` row `delete_scope.deny.audit_id` pinning the `@`-prefix.

If review rejects the uniform change (e.g., only the folded `DeleteScope` should keep `@`), the alternative is a per-action audit-id formatter on the filter; deferring that complexity is the recommendation and the decision belongs in the Decision Log.

**Deny-audit resource-id (OrgResource).**

No change. `RegistryAuthorizeFilter.ResourceIdForAudit(OrgResource) → org.OrgName` already matches `DeleteOrg`'s controller-written `org`. A new `AuthzMatrixData` row `delete_org.deny.audit_id` pins the bare `{org}` shape so a future filter change can't silently regress it.

**Stale `ClaimScope` resolver arm.**

`RegistryActionResourceResolver.Resolve`'s arm
```csharp
RegistryAction.ResolveScope or RegistryAction.VerifyScope or RegistryAction.DeleteScope or RegistryAction.ClaimScope
    => new ScopeResource(Route(routeValues, "scope")),
```
is split: `ClaimScope` is removed from the arm. Since `ClaimScope` is `[ImperativeAuthz]` it never reaches the resolver in production. If anyone in the future mistakenly attaches `[RegistryAuthorize(RegistryAction.ClaimScope)]` to an endpoint, the resolver's default `throw new InvalidOperationException(...)` fires loudly — this turns "silent misroute that produces an empty `ScopeResource`" into "fail-fast at request time."

**Imperative-pin shrink.**

`AuthzDispatchCoverageMetaTests.PinnedImperativeActions` shrinks from
```csharp
{ "PackagesController.PublishPackage", "OrganizationsController.DeleteOrg",
  "ScopesController.ClaimScope", "ScopesController.DeleteScope" }
```
to
```csharp
{ "ScopesController.ClaimScope" }
```

**Critical phasing rule:** the pin update lands **in the same phase as each fold**, not in a trailing docs phase. After each fold phase, the pin assertion's `actual` set decreases by one. A fold phase that does not also shrink the pin set fails the meta-test (`extra` set non-empty); a phase that shrinks the pin without folding the endpoint also fails (`missing` set non-empty). This is the structural guarantee that the pin and the source of truth (the actual `[ImperativeAuthz]` attribute presence) never drift apart across phases.

### Implementation Path

Fold `OrganizationsController.DeleteOrg` (mechanical attribute swap) + add `delete_org.deny.audit_id` matrix row + shrink pin → change `RegistryAuthorizeFilter.ResourceIdForAudit(ScopeResource)` to emit `@{scope}` + fold `ScopesController.DeleteScope` + add `delete_scope.deny.audit_id` matrix row + audit-mutation rows for `DeleteScope` and `VerifyScope` + shrink pin → add `RegistryAction.PublishPackage` enum member + token-ceiling entry + PDP handler (DB-existence branch → delegate to existing create/publish handlers) + resolver arm + fold `PackagesController.PublishPackage` + add `publish.deny.label` matrix row + `PublishPackage` audit-mutation deny rows for both "package exists" and "package does not exist" cases + shrink pin → correct `ScopesController.ClaimScope`'s `[ImperativeAuthz]` reason string + delete the dead `ClaimScope` resolver arm + file the body-resolver backlog stub → update `Stash.Registry/CLAUDE.md` Controller Pattern table and `[ImperativeAuthz]` end-state row + `.claude/repo.md` history line.

### Cross-Cutting Concerns

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| Every controller endpoint reaches the PDP exactly once (declarative or imperative) | `AuthzDispatchCoverageMetaTests` (production assertion + fail-path fixture) | **Construct** (filter is the only path for `[RegistryAuthorize]` endpoints; resolver default-throws on missing arms) + **Detect** (existing meta-test from `registry-authz-filter`; this feature does not touch its shape, only the pinned set it asserts against). |
| The pinned `[ImperativeAuthz]` set equals exactly the documented remnant after each fold | `AuthzDispatchCoverageMetaTests.PinnedImperativeActions` constant | **Detect** with **enforced shrink-with-fold phasing** — every fold phase's `done_when` requires both (a) the endpoint's attribute change AND (b) the pin set decrement. The meta-test fails closed on either side of a drift: a fold that forgets to shrink the pin trips the `extra` set assertion; a shrink that forgets to fold the endpoint trips the `missing` set assertion. Ships a self-test (existing fail-path fixture from `registry-authz-filter`) proving teeth. |
| `RegistryAction → resource resolver` arm completeness (no silent empty-resource bug) | `RegistryActionResourceResolver.Resolve` default `throw new InvalidOperationException` | **Construct** — the resolver fails-closed at request time on any action the filter is asked to dispatch without an arm. Removing the dead `ClaimScope` arm replaces a *silent empty `ScopeResource`* with a *loud throw*. No meta-test needed; the architecture itself denies the omission. |
| Bounded auth domains (action names, deny reasons, roles, scopes) never reach auth sinks as bare strings | `NoMagicAuthStringsMetaTests` + `RegistryAuthConstants` / `RegistryAction` enum | **Construct** for the enum (illegal value won't compile) + **Detect** for sink-targeted string scan (existing meta-test from `registry-authz-pipeline`; this feature inherits it). |
| Wire-level behavior changes (deny-audit labels, deny-audit resource-ids) are visible to a test, not just asserted in prose | `AuthzMatrixData` + `RegistryAuthzAuditMutationTests` | **Detect** with **phase-coupled gating** — each fold phase that ships a decision change must, in the same `done_when`, ship the `AuthzMatrixData` row OR `RegistryAuthzAuditMutationTests` row that pins it. The reviewer's job (and the implementer's verify) is to refuse the phase if the matrix did not move with the decision. |

## Acceptance Criteria

- **End-to-end PublishPackage fold proof.** A direct HTTP call to `PUT /api/v1/packages/{scope}/{name}` by a `read`-ceiling token returns `403 TokenScopeInsufficient` and writes exactly one deny audit entry whose `action` column equals `PublishPackage` (NOT `CreatePackage`/`PublishVersion`). A successful publish by a `publish`-ceiling token returns `201` and writes exactly one allow audit entry whose `action` column equals `package.create` (first publish) or `package.publish` (subsequent publish) — the allow-side mutation label is unchanged.
- **DeleteOrg end-to-end.** `DELETE /api/v1/orgs/{org}` by a non-owner non-admin authenticated caller returns `403 OrgMembershipRequired` and writes exactly one deny audit entry whose `action == "DeleteOrg"` and `resource_id == org` (bare). Allow path is bit-identical to today.
- **DeleteScope end-to-end.** `DELETE /api/v1/scopes/{scope}` by a non-owner non-admin authenticated caller returns `403 ScopeNotOwned` and writes exactly one deny audit entry whose `action == "DeleteScope"` and `resource_id == "@" + scope` (prefixed). Allow path is bit-identical to today.
- **Uniform `@`-prefix.** `VerifyScope` deny (any unauthorized authenticated caller) writes a deny audit entry whose `resource_id == "@" + scope`. This proves the filter-level `ResourceIdForAudit(ScopeResource)` change is applied uniformly to every `ScopeResource` action, not only to `DeleteScope`.
- **No inline PDP in folded controllers.** `grep -nE '_authorizer\.AuthorizeAsync|LogAuthzDenyAsync' Stash.Registry/Controllers/{Packages,Organizations,Scopes}Controller.cs` returns matches ONLY inside `ScopesController.ClaimScope`.
- **Pin is `{ClaimScope}`.** `AuthzDispatchCoverageMetaTests.PinnedImperativeActions.SetEquals({"ScopesController.ClaimScope"})` is true; the meta-test passes. The fail-path fixture still trips the scan (proving the test has teeth).
- **Reason-string correctness.** `ScopesController.ClaimScope`'s `[ImperativeAuthz]` reason string mentions both (a) body-derived resource and (b) body-derived owner/ownerType, and links to a backlog stub at `.kanban/0-backlog/registry/Body-resolver authz filter.md` describing the refactor that would unlock the final fold.
- **Resolver hardening.** `RegistryActionResourceResolver.Resolve` has no `ClaimScope` arm; an attempt to call `Resolve(RegistryAction.ClaimScope, …)` throws `InvalidOperationException` with the documented "Add an entry to RegistryActionResourceResolver.Resolve" message. A direct unit test asserts this throw.
- **Matrix coverage.** `AuthzMatrixData` carries three new pinning rows: `publish.deny.label = "PublishPackage"`, `delete_org.deny.audit_id = "{org}"`, `delete_scope.deny.audit_id = "@{scope}"`. `RegistryAuthzMatrixTests` exercises them and passes.
- **Magic-string compliance.** `NoMagicAuthStringsMetaTests` passes (no new auth-string literals reach sinks; the new enum member is a typed `RegistryAction.PublishPackage`).
- **Coverage compatibility.** `AuthzCoverageMetaTests` and `AuthzDispatchCoverageMetaTests` both pass; class-level `[Authorize]` continues to satisfy authentication classification, and every non-`[PublicEndpoint]` action carries either `[RegistryAuthorize]` or `[ImperativeAuthz]` (now exactly one).
- **Sibling suites green.** `OrgAndAdminAuthzTests`, `ScopeOwnershipPolicyTests`, `RegistryAuthzRoleMatrixWriteTests`, `RegistryAuthzRoleRevokeTests`, `RegistryAuthorizerTests`, `JtiRevocationPreservedTests`, `RegistryAuthzBugATests`, `RegistryAuthzBugBTests` all pass with no test edits beyond the new rows / label assertions.
- **Docs updated.** `Stash.Registry/CLAUDE.md` Controller Pattern table lists exactly one `[ImperativeAuthz]` row (`ClaimScope`, corrected reason). The `Tests` section's `AuthzDispatchCoverageMetaTests` row reflects the `{ClaimScope}` pin. `.claude/repo.md` gains a completed-features history line referencing this feature's slug.
- **`final_verify` passes.** Build green + filtered `dotnet test` (excluding documented env-flakies per `.claude/repo.md` Known Issues, matching the predecessor's filter shape) green.

## Phases

The phase list lives in `plan.yaml`. Intended shape (architect's call, advisor-endorsed sequencing):

1. **P1 — Fold `DeleteOrg`** (trivial). Validates the attribute-swap + pin-shrink-in-same-phase mechanics on the lowest-risk fold (no audit-id or label change). Pin shrinks `{PublishPackage, DeleteOrg, ClaimScope, DeleteScope} → {PublishPackage, ClaimScope, DeleteScope}`. New `delete_org.deny.audit_id` row added.
2. **P2 — Audit-id uniformity + fold `DeleteScope`**. Changes `RegistryAuthorizeFilter.ResourceIdForAudit(ScopeResource)` to prefix `@`, folds `DeleteScope`, adds `delete_scope.deny.audit_id` matrix row + `DeleteScope` audit-mutation row + `VerifyScope` audit-mutation row (proving uniform application). Pin shrinks to `{PublishPackage, ClaimScope}`.
3. **P3 — `RegistryAction.PublishPackage` + fold `PackagesController.PublishPackage`** (the only non-mechanical fold). Adds the enum member + token-ceiling entry + PDP handler (DB-existence branch → delegate to existing create/publish handlers) + resolver arm. Folds the controller. Adds `publish.deny.label` matrix row + `PublishPackage` audit-mutation deny rows for both "package exists" and "package does not exist" cases (proves the deny label is uniformly `PublishPackage` regardless of existence). Pin shrinks to `{ClaimScope}`.
4. **P4 — `ClaimScope` reason correction + resolver hardening + backlog stub + docs**. Rewrites the `ClaimScope` `[ImperativeAuthz]` reason string. Removes the dead `ClaimScope` arm from `RegistryActionResourceResolver` and adds a unit test for the throw. Files `.kanban/0-backlog/registry/Body-resolver authz filter.md`. Updates `Stash.Registry/CLAUDE.md` (Controller Pattern table → one row, Tests section pin reference). Adds `.claude/repo.md` history line.

## Open Questions

- **Uniform vs targeted `@`-prefix on `ScopeResource` audit ids.** Recommendation: uniform (applies to `ResolveScope` / `VerifyScope` / `DeleteScope` via the filter helper). Rationale: `@{scope}` is the canonical scope identifier everywhere else in the codebase; lining up all `ScopeResource` audit rows on the same form removes a latent inconsistency rather than introducing one. If review prefers targeted, a per-action `Func<ResourceRef, string>` formatter on the filter is the alternative — declared a follow-on, not built here.
- **Should `CreatePackage` / `PublishVersion` enum members be deprecated after the fold?** Recommendation: keep both, with a doc comment noting that controllers should attach `[RegistryAuthorize(RegistryAction.PublishPackage)]` and that the two existence-specific members are internal PDP delegation targets + test fixture inputs. Removing them would touch `RegistryAuthorizer`'s internal switch and `RegistryAuthorizerTests`' matrix rows — out of scope for this brief.
- **Should the `PublishPackage` PDP handler reuse `_db.PackageExistsAsync` or query `_ctx.Packages` directly?** Recommendation: query `_ctx.Packages.AsNoTracking().AnyAsync(...)` directly to keep the PDP self-contained (consistent with how `AuthorizeClaimScopeAsync`/`AuthorizeDeleteScopeAsync` already query `_ctx.Scopes`). Cost: one duplicated predicate. Benefit: PDP has no `IRegistryDatabase` dependency, preserving its current minimal surface.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-05-31 | Reduce in-scope folds from four to three; explicitly keep `ClaimScope` `[ImperativeAuthz]` as the documented irreducible remnant. | `ClaimScope`'s scope name and owner fields live in the JSON body, not the route. The shared filter's resource resolver is route-only by invariant (no DB, no body buffering) — that invariant protects the DI lifetime of 30+ endpoints and is the load-bearing simplification of `registry-authz-filter`. Folding ClaimScope requires either a body-buffering filter (touches every other endpoint) or a per-endpoint resolver escape hatch (defeats the purpose of the shared filter). The honest end-state is `{ClaimScope}`, with a corrected reason string. |
| 2026-05-31 | The `[ImperativeAuthz]` reason strings for `DeleteOrg` and `DeleteScope` are PARTIALLY STALE — design from the code, not the comments. | Verified against the current controller bodies and `RegistryAuthorizer.cs`: `DeleteOrg` already calls `AuthorizeAsync(..., RegistryAction.DeleteOrg, ...)` (the "wrong-action bug" was closed by `registry-authz-pipeline` F03); `DeleteScope`'s "inline coordination" is a one-character audit-string difference (`@{scope}` vs `scope`), not a real coordination requirement. The previous brief's "fold all four" framing did not survive contact with the code. |
| 2026-05-31 | Single new `RegistryAction.PublishPackage` enum member; PDP handler internally branches on `PackageExistsAsync` and delegates to existing create/publish authorization paths. | The route-only resolver invariant forbids a DB lookup in the resolver. A unified action keeps the controller declarative; delegation inside the PDP preserves existing matrix-test coverage for `CreatePackage`/`PublishVersion` cells (their handlers stay intact). The only wire-level delta is the deny-audit `action` label (`CreatePackage`/`PublishVersion` → `PublishPackage`), pinned by a new matrix row. |
| 2026-05-31 | Change `RegistryAuthorizeFilter.ResourceIdForAudit(ScopeResource)` to prefix `@` uniformly across `ResolveScope` / `VerifyScope` / `DeleteScope`. | `@{scope}` is the canonical scope identifier elsewhere (`PackageResource.FullName`, controller code). The previous per-controller inconsistency was the result of controllers individually formatting the id; centralising it in the filter is consistent with how every other resource kind is already handled. Pinned by new audit-mutation rows that cover BOTH `DeleteScope` and `VerifyScope` so the uniform application is enforced, not just the folded endpoint. |
| 2026-05-31 | Delete the dead `ClaimScope` arm from `RegistryActionResourceResolver.Resolve` rather than annotate-and-leave. | The arm currently produces `ScopeResource("")` from a route value that does not exist on `POST /api/v1/scopes`. With `ClaimScope` permanently `[ImperativeAuthz]`, the arm is unreachable; leaving it lets a future contributor mistakenly attach `[RegistryAuthorize(RegistryAction.ClaimScope)]` and silently produce an empty resource. Deletion converts that silent failure into a loud `InvalidOperationException` from the resolver's default arm — a **Construct**-level prevention. |
| 2026-05-31 | The pin set in `AuthzDispatchCoverageMetaTests.PinnedImperativeActions` shrinks **in the same phase** as each fold, never in a trailing phase. | The pin is the cross-cutting omission guard for "the documented imperative set equals reality." If folds land first and pin-shrinks land later, every intervening commit ships with the meta-test asserting an `extra` set non-empty (red build) — or, if the developer pre-shrinks, the meta-test asserts a `missing` set non-empty. Phase-coupling makes both directions of drift fail closed within the same phase. |
| 2026-05-31 | Sequencing: DeleteOrg → DeleteScope → PublishPackage → ClaimScope-cleanup/docs. | Advisor-endorsed risk ordering: DeleteOrg is mechanical (validates attribute-swap + pin-shrink-in-same-phase on the lowest-risk fold), DeleteScope adds the filter audit-id change (one shared edit + one fold), PublishPackage is the hardest (new enum + new PDP handler + new matrix rows). The final phase is non-code-altering cleanup. |
| 2026-05-31 | **Feature is SPECCED NOW, TACKLED LATER.** `.claude/repo.md` "Active Multi-Phase Work" pointer is annotated `specced, not started`. | Two existing in-progress features (`pkg-cli-api-parity`, `readonly-modifier`) are active. `pkg-cli-api-parity` touches `Stash.Registry/Controllers/` — the exact surface this feature also edits. Per `.claude/WORKFLOW.md` "Running Features in Parallel" guidance and the project memory `project_registry_pre_release.md`, this feature must serialise behind any concurrent registry-controller work. Run `python3 scripts/checkpoint/check-parallel-safety.py registry-authz-pdp-completion` before the first `/next-phase`; if the script reports controller-file overlap with `pkg-cli-api-parity`, do not start this feature until that one promotes to done. |
