# AuthzCoverageMetaTests treats a class-level [Authorize] as authorization coverage without verifying the endpoint dispatches to the PDP

**Status:** Backlog — Bug
**Created:** 2026-05-30
**Discovery context:** Surfaced during `registry-authz-pipeline` P7 (originally the docs+cleanup phase). The implementer refused to delete the legacy `RequireAdmin` policy because five `AdminController` endpoints (`GetStats`, `CreateUser`, `DeleteUser`, `AdminAssignRole`, `GetAuditLog`) were gated *only* by the class-level `[Authorize(Policy=RequireAdmin)]` and never called `IRegistryAuthorizer`. The default-deny meta-test had reported full compliance the whole time — it counts a class-level `[Authorize]` as "classified" and never checks that an endpoint actually makes a PDP decision.

---

## Problem

`AuthzCoverageMetaTests.AllProductionEndpoints_AreExplicitlyClassified` enforces *default-deny*: every controller action must carry `[Authorize(...)]` (on the method or its declaring class) or `[PublicEndpoint(...)]`. That is a real and useful invariant — but it is **weaker than the property the registry actually relies on**, which is that every non-public endpoint routes its authorization decision through the PDP (`IRegistryAuthorizer.AuthorizeAsync`).

Because the check is satisfied by the *presence of an attribute*, an endpoint can be "classified" (green) while doing no PDP call at all — its only gate being a legacy policy attribute, or, after that policy is removed, a bare `[Authorize]` that merely requires authentication. The meta-test cannot tell the difference between "this endpoint is authorized by the PDP" and "this endpoint is merely authenticated."

## Reproduction

Deterministic, today, against `HEAD` of the `registry-authz-pipeline` branch *before* P7 landed (commit `a757975` and earlier):

```bash
# 1. AdminController.GetStats/CreateUser/DeleteUser/AdminAssignRole/GetAuditLog had
#    NO _authorizer.AuthorizeAsync call — only the class-level
#    [Authorize(Policy=RequireAdmin)] gated them.
# 2. The coverage meta-test still passed green:
dotnet test --filter "FullyQualifiedName~AuthzCoverageMetaTests"
#    => AllProductionEndpoints_AreExplicitlyClassified: PASS (the class attribute counts)
```

The escalation it fails to catch: had P7's docs+cleanup deleted `RequireAdmin` and stripped the class attribute to bare `[Authorize]` (which the meta-test *still* accepts as classified), `/admin/users`, `/admin/packages/{scope}/{name}/roles`, and `/admin/audit-log` would have become reachable by any authenticated non-admin user, and **this meta-test would have stayed green**. Only `/admin/stats` had an independent non-admin→403 regression test (`OrgAndAdminAuthzTests.GetAdminStats_WithPublishToken_Returns403`).

## Blast radius

- **Latent, defensive.** This is not a production auth hole today — P7 (commit `c703a40`) wired all five endpoints to the PDP and added the missing non-admin→403 regression guards, and `RegistryAuthzMatrixTests` covers the cross-product. The hole is in the *gate that is supposed to prevent regressions*, not in the product.
- **Becomes load-bearing on the next controller added or migrated.** A new controller, or a future endpoint added to an existing one, can ship gated-but-not-PDP-routed and pass every meta-test. The class of bug that took a careful human + advisor exchange to catch during P7 would recur silently.
- Compounds with the no-magic-strings ethos: the project deliberately moves enforcement into the test/CI gate ("enforce in the test/CI gate, not just convention"). A gate that over-reports coverage undercuts that.

## Root cause

`Stash.Tests/Registry/AuthzCoverageMetaTests.cs` — `IsClassified(MethodInfo)` (around line 85) returns true if the action OR its declaring controller carries `[Authorize]`, or the action carries `[PublicEndpoint]`. It is a purely *attribute-presence* check. It performs no analysis of the method body, so it cannot assert that a non-public endpoint calls `IRegistryAuthorizer.AuthorizeAsync`. The underlying mechanism that let the `AdminController` gap persist was P4's `done_when`, which stated "every AdminController endpoint is authorized through IRegistryAuthorizer" but specified only a `grep "HasPackagePermissionAsync"` proxy check — neither gate verified actual PDP dispatch.

## Suggested fix

- (A) **Roslyn body scan** — extend the meta-test (or add a sibling) that loads the `Stash.Registry` syntax trees and asserts every non-`[PublicEndpoint]` action method contains an invocation of `IRegistryAuthorizer.AuthorizeAsync` (directly or via a small allow-list of delegating helpers). Sketch: mirror the syntax-scan pattern already used by `NoMagicAuthStringsMetaTests`. Trade-off: must maintain an allow-list for endpoints that legitimately don't dispatch (pure auth-plane actions like `Whoami`/`IssueToken` whose PDP branch is `Allow()` and may be inlined), and helper-indirection makes "calls AuthorizeAsync" non-trivial to prove statically — this is its own small design problem, which is why it was deliberately deferred out of P7.
- (B) **Runtime coverage assertion** — drive every endpoint through `WebApplicationFactory` with an authenticated-but-unauthorized principal and assert a PDP-sourced deny (e.g. a typed `AuthzDenyReason` in the body / audit entry), not merely a 401/403. Sketch: a data-driven test enumerating routes. Trade-off: heavier, needs a route inventory, and a 403 alone doesn't prove the *PDP* produced it.
- (C) **Action-enum dispatch ledger** — assert every `RegistryAction` enum member is referenced by at least one controller `AuthorizeAsync` call site (catches "declared but never dispatched" actions — the exact fingerprint here: `ReadAdminStats`/`ManageUser`/`AdminAssignPackageRole`/`ReadAuditLog` were defined and handled by the resolver but never invoked). Sketch: Roslyn scan for `RegistryAction.<Member>` references across controllers. Trade-off: proves the action is *used somewhere*, not that the *right* endpoint uses it; weaker than (A) but cheap and would have caught this specific gap.

Recommend **(A)** as the durable fix, optionally backed by **(C)** as a cheap complementary tripwire. Keep it out of `registry-authz-pipeline` (that feature is closing); this is a test-infrastructure hardening item.

## Verification

```bash
# A new meta-test that asserts PDP dispatch. Construct it so it FAILS against a
# controller action that carries [Authorize] but makes no AuthorizeAsync call
# (use a test fixture controller, mirroring UnclassifiedEndpointFixtureController),
# and PASSES against the current production controllers (all now PDP-routed after P7).
dotnet test --filter "FullyQualifiedName~AuthzDispatchMetaTests"
```

Cross-cutting checks that must continue to pass: `AuthzCoverageMetaTests` (the existing default-deny test stays — this is additive), `RegistryAuthzMatrixTests`, `NoMagicAuthStringsMetaTests`, and the full `FullyQualifiedName~Registry` filter.

## Related

- Feature that surfaced it: `registry-authz-pipeline` — P7 (`c703a40`, the migration that closed the product-side gap) and the inserted-phase rationale in that feature's `brief.md` (`## Phases`, P7) and `plan.yaml`.
- Mechanism that let it persist: P4 (`635c7bc`) — `done_when` asserted full AdminController PDP migration but verified it only with a `grep "HasPackagePermissionAsync"` proxy.
- Same surface: `Stash.Tests/Registry/AuthzCoverageMetaTests.cs`, `Stash.Registry/Auth/Authorization/RegistryAction.cs` (the closed action enum), `Stash.Tests/Registry/Authz/NoMagicAuthStringsMetaTests.cs` (the existing Roslyn-scan meta-test to mirror for approach A).
