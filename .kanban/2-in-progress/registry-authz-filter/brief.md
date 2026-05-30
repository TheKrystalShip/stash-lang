# RFC: Registry Authorization Filter

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-05-30
> **Slug:** registry-authz-filter

## Summary

The Stash registry today scatters Policy Decision Point (PDP) plumbing across six controllers: an identical `BuildPrincipal(ClaimsPrincipal)` helper is copy-pasted in every controller, ~32 endpoints carry the same `_authorizer.AuthorizeAsync(...) → if !Allowed → audit + status mapping + ErrorResponse` block, and one controller (`PackagesController`) ships a better `DenyReasonToStatus` mapping that the others silently disagree with (it correctly maps `VisibilityHidden` / `PackageNotFound` to 404; the others return 403). There is also no static guarantee that a controller action actually consults the PDP — an action can carry `[Authorize]`, satisfy `AuthzCoverageMetaTests`, and still ship with no PDP dispatch at all.

This feature introduces a declarative `[RegistryAuthorize(RegistryAction)]` attribute backed by a single MVC authorization filter that builds the principal, resolves the route-derived resource, calls the PDP, and renders the unified deny response. ~33 "clean" endpoints lose their inline boilerplate; the 4 endpoints that genuinely need pre/post-PDP work (`PublishPackage`, `DeleteOrg`, `ClaimScope`, `DeleteScope`) keep their inline logic and gain an explicit `[ImperativeAuthz("reason")]` marker so the new PDP-dispatch coverage meta-test treats them as auditable exemptions rather than blind gaps. The refactor is strictly behavior-preserving; `RegistryAuthzMatrixTests` is the canonical safety net.

## Motivation

- **Code duplication.** `BuildPrincipal` is identical in 6 files. ~32 PDP call sites share an identical 10-line block. Any future change (a new typed deny reason, a new audit field, an emergent status-code mapping) requires editing 6 files in lockstep — and the existing `DenyReasonToStatus` divergence proves this lockstep already broke once.
- **Silent dispatch gaps.** `AuthzCoverageMetaTests` checks that every action has `[Authorize]` or `[PublicEndpoint]`, but does NOT check that any action other than the public ones actually reaches the PDP. A future developer can ship a `[Authorize]` endpoint that performs a privileged DB write with no PDP call and no test will fail. There is no compile-time or test-time enforcement of "every authenticated action consults the PDP."
- **Inconsistent deny responses.** `PackagesController` (4 sites) maps `VisibilityHidden`/`PackageNotFound` → 404; every other controller returns 403 for the same deny reason. This is a latent privacy leak waiting to bite, but fixing it inline means touching 32 sites. Centralising the mapping fixes it once and lets the matrix test prove the fix.
- **Readability.** Today a reader of `OrganizationsController.CreateOrg` must scan past 12 lines of authz boilerplate before reaching the actual controller logic. After this feature, the action declares its intent in one attribute and the body is just the work.

This refactor is a prerequisite for the follow-on `registry-authz-pdp-completion` feature, which will fold the four `[ImperativeAuthz]` cases into the PDP itself. That work cannot proceed cleanly while every endpoint owns its own copy of the dispatch boilerplate.

## Goals

- A single MVC `IAsyncAuthorizationFilter` performs principal-build → resource-resolve → PDP call → unified deny response on behalf of every endpoint that opts in via `[RegistryAuthorize(RegistryAction)]`.
- The duplicated `BuildPrincipal(ClaimsPrincipal)` helper exists in exactly **one** shared production location; the six per-controller copies are deleted.
- The deny response shape — status code mapping (incl. `VisibilityHidden`/`PackageNotFound` → 404, `NotAuthenticated` → 401, otherwise 403), the `ErrorResponse{Error=Reason.ToString(), Message=Detail}` body, and the authenticated-only `LogAuthzDenyAsync` call — has one production implementation reused by both the filter and the imperative remnants.
- ~33 controller actions carry only `[RegistryAuthorize(RegistryAction.X)]` (plus `[HttpVerb]` and `[Authorize]`/`[PublicEndpoint]` as today). Their bodies contain no PDP call and no deny block.
- A new dispatch-coverage meta-test fails the build if any non-`[PublicEndpoint]` action on a production controller carries neither `[RegistryAuthorize]` nor an explicit `[ImperativeAuthz("reason")]` exemption.
- The 4 imperative endpoints (`PublishPackage`, `DeleteOrg`, `ClaimScope`, `DeleteScope`) carry `[ImperativeAuthz("...")]` and continue to work bit-identically. Where convenient they may route their deny response through the shared helper for body-shape consistency, but this is not required.
- `RegistryAuthzMatrixTests`, `AuthzCoverageMetaTests`, `NoMagicAuthStringsMetaTests`, `RegistryAuthorizerTests`, and the new dispatch-coverage meta-test all pass.
- `Stash.Registry/CLAUDE.md` "Controller Pattern" and request-flow sections document the attribute as the canonical pattern.

## Non-Goals

- **No decision changes.** Every status code, body, audit row, and PDP outcome is identical before and after this feature. `RegistryAuthzMatrixTests` is the gate.
- **No PDP semantic fixes.** Folding `PublishPackage`'s dynamic Create-vs-Publish action choice, `DeleteOrg`'s reuse of `AddOrgMember` for authorization (a known wrong-action bug), `ClaimScope`'s post-PDP owner-type/ownership checks, and `DeleteScope`'s post-PDP work into the PDP itself is **out of scope**. Those changes alter decisions and require new `AuthzMatrixData` rows; they ship in `registry-authz-pdp-completion`.
- **No middleware reordering.** The JTI revocation gate runs between `UseAuthentication()` and `UseAuthorization()` in `Startup.cs`; this feature does not touch that. The new filter runs as part of the authorization filter pipeline, after the JTI gate.
- **No removal of `[Authorize]`.** `[RegistryAuthorize]` is composed with the existing `[Authorize]` (authentication) attribute and does not replace it. `AuthzCoverageMetaTests` continues to require `[Authorize]` or `[PublicEndpoint]` on every action.
- **No language/LSP/DAP work.** Registry-only change; `.claude/language-changes.md` does not apply.
- **No fix to `ClaimScope`'s non-standard 409 status mapping for `ScopeReserved`/`ScopeNotOwned`.** That mapping is part of the imperative remnant; it is preserved verbatim under `[ImperativeAuthz]`.

## Design

The end state is a single attribute on each clean endpoint, a single filter class that owns the dispatch, and a single deny-response helper consumed by both the filter and the four imperative endpoints.

### Surface

```csharp
// New attribute (production: Stash.Registry/Auth/Authorization/RegistryAuthorizeAttribute.cs)
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RegistryAuthorizeAttribute : Attribute, IFilterFactory
{
    public RegistryAction Action { get; }
    public RegistryAuthorizeAttribute(RegistryAction action) { Action = action; }
    public bool IsReusable => false;
    public IFilterMetadata CreateInstance(IServiceProvider sp) =>
        ActivatorUtilities.CreateInstance<RegistryAuthorizeFilter>(sp, Action);
}

// New exemption marker for the 4 imperative endpoints
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ImperativeAuthzAttribute : Attribute
{
    public string Reason { get; }
    public ImperativeAuthzAttribute(string reason) { Reason = reason; }
}
```

Clean-endpoint usage (before → after):

```csharp
// BEFORE
[Authorize]
[HttpDelete("{scope}/{name}/{version}")]
public async Task<IActionResult> UnpublishVersion(string scope, string name, string version)
{
    var resource = PackageRoute.From(...);
    var principal = BuildPrincipal();
    string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.UnpublishVersion, resource);
    if (!decision.Allowed) { /* 10 lines of audit + status map + body */ }
    /* actual work */
}

// AFTER
[Authorize]
[RegistryAuthorize(RegistryAction.UnpublishVersion)]
[HttpDelete("{scope}/{name}/{version}")]
public async Task<IActionResult> UnpublishVersion(string scope, string name, string version)
{
    /* actual work; principal/username read from User claims as needed */
}
```

Imperative remnant usage:

```csharp
[Authorize]
[ImperativeAuthz("dynamic action choice: CreatePackage vs PublishVersion depends on a DB existence check; folded into PDP in registry-authz-pdp-completion")]
[HttpPut("{scope}/{name}")]
public async Task<IActionResult> PublishPackage(string scope, string name) { /* existing inline logic, optionally routed through shared deny helper */ }
```

### Semantics

**`RegistryAuthorizeFilter` (`IAsyncAuthorizationFilter`) — implements `OnAuthorizationAsync`:**

1. **Resolve scoped services** from `context.HttpContext.RequestServices`: `IRegistryAuthorizer`, `AuditService`, `IRegistryAuthzPrincipalFactory` (the shared `BuildPrincipal` host). This is mandatory — these services are registered Scoped in `Startup.cs` and an MVC attribute is effectively singleton; constructor-injection on the attribute would either fail to resolve or capture a DbContext across requests.
2. **Build principal** via the shared factory from `context.HttpContext.User`. Anonymous → `AnonymousPrincipal`; authenticated → `UserPrincipal(username, role, ceiling, jti)`.
3. **Resolve resource** for the configured `RegistryAction` using a small static dispatch table from `RegistryAction` to a `(RouteValueDictionary, HttpContext) → ResourceRef` resolver. Route values are URL-decoded the same way the controllers do today (`Uri.UnescapeDataString`). For token actions the JTI source matches today's controllers: `RevokeOwnToken` reads `id` from the route; `IssueToken`/`ListOwnTokens`/`Whoami` use `PrincipalSelfResource`.
4. **Call PDP** `await _authorizer.AuthorizeAsync(principal, action, resource)`.
5. **On allow**, return (let the action run).
6. **On deny**, set `context.Result` to the shared deny `IActionResult`:
   - If `principal is UserPrincipal up`, await `_auditService.LogAuthzDenyAsync(action.ToString(), up.Username, ResourceIdForAudit(resource), decision.Reason, ip)`. Anonymous denials do NOT audit.
   - Status code: `VisibilityHidden | PackageNotFound → 404`, `NotAuthenticated → 401`, otherwise `403`.
   - Body: `new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail }`.
   - The 404 body for `VisibilityHidden`/`PackageNotFound` on package endpoints reads `$"Package '{packageName}' not found."` today — preserve this exactly: the shared helper takes a `notFoundMessageFactory` callback the filter supplies from the resolved `PackageResource` (other resource kinds pass through the standard `decision.Reason.ToString()` body).
7. **No magic strings.** Audit-action strings derive from `action.ToString()` exactly as today; principal role lookup goes through `UserRoles.Admin`; claim names via `RegistryClaims.*`; ceiling parsing via `TokenCeilingConverter.FromClaimValue`. The filter source must pass `NoMagicAuthStringsMetaTests` (any new auth sink usage must use named constants from `RegistryAuthConstants`).

**Shared principal factory (`IRegistryAuthzPrincipalFactory` + `RegistryAuthzPrincipalFactory`):**

- One production class in `Stash.Registry/Auth/Authorization/` exposing `Principal Build(ClaimsPrincipal user)`. Implementation is the byte-identical body that all six controllers share today. Registered Singleton (pure function over `ClaimsPrincipal`; no scoped state).
- Controllers that still need a `Principal` for non-authz work (e.g., reading `username` for downstream service calls) receive the factory via constructor injection and call `_principalFactory.Build(User)`. Each of the six per-controller `BuildPrincipal` helpers is deleted.

**Shared deny-response helper (`AuthzDenyResponse` static class):**

- Owns the status-code switch and `ErrorResponse` construction. The filter uses it via `context.Result = AuthzDenyResponse.For(decision, notFoundMessage)`.
- The 4 imperative endpoints may opt into the helper for response-shape consistency (recommended for `PublishPackage`/`DeleteOrg`/`DeleteScope`); `ClaimScope`'s bespoke 409 mapping for `ScopeReserved`/`ScopeNotOwned` stays inline — that mapping is exactly the kind of imperative deviation `[ImperativeAuthz]` exists to flag.

**Resource resolver table (`RegistryAction → resolver`):**

A switch keyed by the closed `RegistryAction` enum. Each action that opts into the filter must have an entry. The resource builder is pure and never touches the database — it only reads route values and (for self-resources) the authenticated `ClaimsPrincipal`. Examples:

| Action                         | Resolver                                                                            |
|--------------------------------|-------------------------------------------------------------------------------------|
| `ReadPackageMetadata` … `DeletePackage`, `AssignPackageRole`, `RevokePackageRole`, `AdminAssignPackageRole`, `AdminRevokePackageRole` | `PackageRoute.From(scope, name)` from route values                                  |
| `ResolveScope` / `VerifyScope` / `DeleteScope` / `ClaimScope`                                      | `new ScopeResource(scope)` from route or body (`ClaimScope` is `[ImperativeAuthz]`) |
| `CreateOrg` / `ReadOrg` / `AddOrgMember` / `RemoveOrgMember` / `CreateTeam` / `AddTeamMember` / `DeleteOrg` | `new OrgResource(org)`                                                              |
| `RevokeOwnToken`               | `new TokenResource(routeValues["id"]?.ToString() ?? "")`                            |
| `Whoami`, `ListOwnTokens`, `IssueToken`, `Login`, `Register` | `new PrincipalSelfResource()`                                                       |
| `Search`                       | `new SearchResource()`                                                              |
| `ReadAdminStats`, `ManageUser`, `ReadAuditLog` | `new AdminResource()`                                                               |

**`[ImperativeAuthz]` discovery:**

- The new dispatch-coverage meta-test enumerates the six production controllers and asserts: for every action that is NOT `[PublicEndpoint]`, the action carries either `[RegistryAuthorize]` OR `[ImperativeAuthz]`. Class-level `[Authorize]` does NOT count for dispatch coverage; the action itself must be classified.
- The four expected `[ImperativeAuthz]` endpoints (`PackagesController.PublishPackage`, `OrganizationsController.DeleteOrg`, `ScopesController.ClaimScope`, `ScopesController.DeleteScope`) are pinned by a second meta-test assertion: the exact set of imperative actions must equal this list. New `[ImperativeAuthz]` markers require this assertion to be updated, forcing review.

### Implementation Path

`RegistryAuthorizeAttribute` defined → `RegistryAuthorizeFilter` resolves scoped PDP/audit per-request via `IFilterFactory` → shared `RegistryAuthzPrincipalFactory` replaces six copy-pasted `BuildPrincipal` helpers → shared `AuthzDenyResponse` helper centralises status mapping (incl. 404 for `VisibilityHidden`/`PackageNotFound`) → ~33 clean endpoints carry only `[RegistryAuthorize(action)]` and their bodies lose the PDP block → 4 hard endpoints carry `[ImperativeAuthz("reason")]` (optionally routed through `AuthzDenyResponse` for body shape) → new `AuthzDispatchCoverageMetaTests` fails the build if any non-public action is unclassified → `RegistryAuthzMatrixTests` proves wire behavior is identical → docs in `Stash.Registry/CLAUDE.md` updated.

## Acceptance Criteria

- **Behavior preservation (the gate).** `dotnet test --filter "FullyQualifiedName~RegistryAuthzMatrixTests"` passes with zero changes to `AuthzMatrixData`. Every covered (action × principal) row produces the same status code AND the same `ErrorResponse` body shape as before the refactor.
- **Deduplication.** `grep -n "private.*BuildPrincipal" Stash.Registry/Controllers/*.cs` returns zero matches; the helper exists once under `Stash.Registry/Auth/Authorization/`.
- **Inline PDP block elimination.** No clean-endpoint action body contains `await _authorizer.AuthorizeAsync` or `LogAuthzDenyAsync`. The four `[ImperativeAuthz]` endpoints are the only remaining call sites of either method in the controllers directory.
- **Filter coverage.** Every endpoint marked `[RegistryAuthorize(action)]` produces the documented behavior end-to-end through the real HTTP pipeline (covered by the matrix test): allow runs the action body; deny returns the unified status + body; authenticated denies audit, anonymous denies do not.
- **Dispatch coverage enforcement.** `AuthzDispatchCoverageMetaTests` fails the build if a fixture controller action lacks both `[RegistryAuthorize]` and `[ImperativeAuthz]` (proven by a fail-path fixture mirroring `AuthzCoverageMetaTests`'s pattern). The production controllers pass.
- **Imperative exemption pinning.** The set of `[ImperativeAuthz]` actions equals exactly `{PublishPackage, DeleteOrg, ClaimScope, DeleteScope}`; adding or removing a marker requires updating the meta-test, forcing reviewer attention.
- **Magic-string compliance.** `dotnet test --filter "FullyQualifiedName~NoMagicAuthStringsMetaTests"` passes; the new filter source uses `RegistryClaims`/`UserRoles`/`TokenCeilingConverter`, not bare strings.
- **Coverage compatibility.** `dotnet test --filter "FullyQualifiedName~AuthzCoverageMetaTests"` still passes (every action remains `[Authorize]` or `[PublicEndpoint]` or class-level `[Authorize]`).
- **DI lifetime correctness.** The filter resolves `IRegistryAuthorizer` and `AuditService` per request via `HttpContext.RequestServices`; no DbContext is captured across requests. Verified by `RegistryAuthorizerTests` and matrix tests continuing to pass under `WebApplicationFactory`.
- **JTI revocation preserved.** `JtiRevocationPreservedTests` passes — the new authorization filter runs after the JTI revocation middleware, and revoked tokens are still rejected before the PDP runs.
- **Docs updated.** `Stash.Registry/CLAUDE.md` "Controller Pattern" mentions `[RegistryAuthorize]` and `[ImperativeAuthz]` as the canonical patterns; the request-flow diagram notes the filter step.

## Phases

The phase list lives in `plan.yaml`. The intended shape (architect's call):

1. **P1** — Introduce `RegistryAuthorizeAttribute` + `RegistryAuthorizeFilter` + shared `RegistryAuthzPrincipalFactory` + `AuthzDenyResponse` helper. Wire DI. Apply the attribute to a **pilot pair** (one clean endpoint in `AuthController` and one in `OrganizationsController`) to prove the matrix stays green and to lock the public surface. Old `BuildPrincipal` helpers remain (still used by the other 31 sites).
2. **P2** — Convert the remaining clean endpoints, one controller at a time, deleting their inline PDP blocks. Delete each controller's `BuildPrincipal` once its last consumer is gone; substitute `_principalFactory.Build(User)` where downstream code still needs a `Principal`.
3. **P3** — Introduce `[ImperativeAuthz]`, apply it to `PublishPackage`/`DeleteOrg`/`ClaimScope`/`DeleteScope`. Optionally route the three sane deny responses (everything except `ClaimScope`'s bespoke 409 mapping) through `AuthzDenyResponse`. Add `AuthzDispatchCoverageMetaTests` (production assertion + fail-path fixture + imperative-pin assertion).
4. **P4** — Documentation: update `Stash.Registry/CLAUDE.md` Controller Pattern and request flow; cross-link to `[ImperativeAuthz]` and the dispatch-coverage test; record the deferred work pointer to `registry-authz-pdp-completion`.

## Open Questions

- Should `[RegistryAuthorize]` implicitly imply `[Authorize]` (so we can drop the redundant attribute) or remain composed alongside it? **Recommendation:** keep them composed. `AuthzCoverageMetaTests` classifies on `[Authorize]`/`[PublicEndpoint]` and changing that contract is out of scope. Composition keeps the meta-test untouched and makes the authentication intent visually explicit.
- Should the shared deny helper return an `IActionResult` directly, or a `(status, ErrorResponse)` tuple the caller wraps? **Recommendation:** `IActionResult` for the filter (which only needs to assign `context.Result`); the imperative remnants can either consume the same `IActionResult` or destructure a tuple variant — decided in P3.
- For `Search` (public endpoint) the existing flow runs the PDP even when `[PublicEndpoint]`. Should the filter run on `[PublicEndpoint]` endpoints, or do they keep their inline PDP call? **Recommendation:** the filter runs whenever `[RegistryAuthorize]` is present, independent of `[PublicEndpoint]`. The existing `SearchController` already exhibits this pattern (public + PDP), and reproducing it via the filter keeps behavior bit-identical.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-05-30 | Implement as `IAsyncAuthorizationFilter` via `IFilterFactory`, not a plain attribute with constructor injection. | `IRegistryAuthorizer` and `AuditService` are Scoped (shared DbContext). Plain attributes are effectively singletons; capturing scoped services from a singleton corrupts the per-request DbContext. |
| 2026-05-30 | Centralise the `DenyReasonToStatus` mapping (including 404 for `VisibilityHidden`/`PackageNotFound`) in the shared helper. | Five of six controllers map these to 403 today, which is a privacy-leak bug `PackagesController` already mitigates. The centralised mapping aligns all controllers on the correct behavior without touching 32 call sites individually. |
| 2026-05-30 | Treat the dispatch-coverage meta-test as a NEW guarantee, not a refactor target. | No existing test verifies an endpoint reaches the PDP. Without this guard, the post-refactor surface is no safer than before — anyone can add a privileged endpoint with neither attribute and tests stay green. |
| 2026-05-30 | Keep the 4 hard endpoints inline under `[ImperativeAuthz("reason")]` and defer their PDP-fold to `registry-authz-pdp-completion`. | Folding them changes decisions (`DeleteOrg` currently uses `AddOrgMember`'s authz — a real bug; `PublishPackage`'s dynamic action choice needs new matrix rows). That work belongs in a decision-changing feature, not this strictly behavior-preserving one. |
| 2026-05-30 | Predecessor `registry-authz-pipeline` is COMPLETE and promoted to `.kanban/4-done/`; implementation is not blocked. | Verified: `ls .kanban/4-done/registry-authz-pipeline/` lists `brief.md`/`plan.yaml`/`checkpoint.yaml`/`review.md`. The PDP, attribute infrastructure, and matrix test this feature depends on are all landed. |
| 2026-05-30 | Run `python3 scripts/checkpoint/check-parallel-safety.py registry-authz-filter` before `/next-phase`; serialize with `pkg-cli-api-parity` if controller-file overlap is detected. | Both features touch `Stash.Registry/Controllers/`. `.claude/WORKFLOW.md` "Running Features in Parallel" mandates the check. The controller surface this feature edits (all six) intersects with the routes pkg-cli-api-parity is extending. |
