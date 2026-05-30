# Body-resolver authz filter — unlock ClaimScope fold

**Status:** Backlog — Enhancement
**Created:** 2026-05-31
**Discovery context:** registry-authz-pdp-completion P4 (implementer). During the PDP-completion feature, three of the four imperative endpoints (PublishPackage, DeleteOrg, DeleteScope) were folded into `[RegistryAuthorize(RegistryAction.X)]`. ClaimScope remains as the sole `[ImperativeAuthz]` endpoint because its authorization-relevant fields (scope name, ownerType, owner) live in the JSON request body, not in route values. The shared `RegistryAuthorizeFilter` uses `RegistryActionResourceResolver.Resolve`, which is a pure-route resolver — it cannot read the request body. Folding ClaimScope requires the body-buffering refactor described here.

---

## Problem

`ScopesController.ClaimScope` (POST /api/v1/scopes) cannot be folded into the declarative `[RegistryAuthorize(RegistryAction.ClaimScope)]` pattern because the authorization-relevant fields — `scope` (the scope name to claim), `owner_type`, and `owner` — arrive in the JSON request body, not in route values.

The shared `RegistryAuthorizeFilter` calls `RegistryActionResourceResolver.Resolve` to build a `ResourceRef` before invoking the PDP. That resolver is deliberately pure: it reads only `RouteValueDictionary` and `HttpContext` (without reading the body). This purity is a load-bearing invariant — the resolver runs inside an authorization filter, before the controller's model binding, and reading the body at that point would consume it, making it unavailable to the controller action.

Additionally, ClaimScope has a bespoke 409 status mapping: when the PDP returns `ScopeReserved` or `ScopeNotOwned`, the controller maps these to HTTP 409 (Conflict) rather than the standard 403. The shared filter uses the uniform `AuthzDenyResponse.For(...)` mapping, which does not produce 409 for these reasons. Replicating the bespoke mapping in the filter would require per-action status overrides, adding complexity to the filter.

The net effect is that `RegistryAction.ClaimScope` is intentionally absent from `RegistryActionResourceResolver.Resolve`. Attempting to place `[RegistryAuthorize(RegistryAction.ClaimScope)]` on the endpoint now throws `InvalidOperationException` at request time (pinned by `RegistryActionResourceResolverTests.Resolve_ClaimScope_ThrowsInvalidOperationException`), which fails loud rather than silently producing a `ScopeResource("")`.

## Reproduction

No active bug. The constraint is structural: reading the request body in an authorization filter consumes it before the controller can read it. Verify by inspecting:

- `Stash.Registry/Auth/Authorization/RegistryActionResourceResolver.cs` — ClaimScope is absent from the switch
- `Stash.Registry/Controllers/ScopesController.cs` line ~81 — `[ImperativeAuthz(...)]` documents the reason
- `Stash.Tests/Registry/RegistryActionResourceResolverTests.cs` — pins the throw

## Blast radius

Only `ScopesController.ClaimScope` is affected. All other formerly-imperative endpoints (PublishPackage, DeleteOrg, DeleteScope) were folded in registry-authz-pdp-completion. The `[ImperativeAuthz]` pin in `AuthzDispatchCoverageMetaTests.PinnedImperativeActions` is now `{"ScopesController.ClaimScope"}`.

The body-buffering refactor, if implemented, must preserve the bespoke 409 status mapping and the body-derived field resolution while keeping the filter pure from the controller's perspective.

## Root cause

Two interacting constraints:

1. **Body consumed on first read**: ASP.NET Core request bodies are non-rewindable streams by default. An authorization filter that reads the body leaves the body position at end-of-stream; the controller's `JsonSerializer.DeserializeAsync` call then reads an empty stream.
2. **Bespoke 409 mapping**: ClaimScope maps `ScopeReserved`/`ScopeNotOwned` → 409, diverging from the filter's uniform deny mapping.

## Suggested fix

Two options, not mutually exclusive:

- **(A) Body buffering + request-body resolver extension** — Enable request body buffering (`app.UseBufferedRequestBody()` or `EnableBuffering()` in middleware before the filter pipeline, or use `HttpContext.Request.EnableBuffering()` selectively). Extend `RegistryActionResourceResolver.Resolve` to support an async overload that reads a buffered body and extracts `scope`, `ownerType`, `owner` for the ClaimScope arm. Then rewind the stream before returning. Add a `ClaimScope` arm to the resolver returning a `ScopeResource(scopeName)` with the body-derived scope name. This allows the filter to build the correct resource; the PDP call proceeds as normal. The bespoke 409 mapping can be expressed via an action-specific status-override hook in the filter or by promoting `ScopeReserved`/`ScopeNotOwned` to produce 409 in `AuthzDenyResponse.For`.
- **(B) Body-derived ScopeResource via filter middleware** — Introduce a per-endpoint `IActionFilter` (not `IAuthorizationFilter`) that runs after model binding and calls the PDP inline, replacing `[ImperativeAuthz]` with `[ClaimScopeAuthz]`. This keeps the body read in the MVC pipeline after model binding. It avoids the stream-consumption problem but requires per-endpoint filter machinery (not the shared-filter model).

Recommend (A): body buffering is already a common pattern in ASP.NET Core for logging/tracing middleware; the scope-resolution stream-rewind is safe for small JSON bodies; and keeping `[RegistryAuthorize]` as the single dispatch pattern is worth the added complexity.

## Verification

After the refactor:

```bash
dotnet test --filter "FullyQualifiedName~AuthzDispatchCoverageMetaTests"
# PinnedImperativeActions must become empty {} (or be deleted).

dotnet test --filter "FullyQualifiedName~RegistryActionResourceResolverTests"
# Resolve_ClaimScope_ThrowsInvalidOperationException must be updated or removed.

dotnet test --filter "FullyQualifiedName~ScopeOwnershipPolicyTests|FullyQualifiedName~RegistryAuthzMatrixTests"
# All existing scope/claim tests must remain green with the new fold.
```

## Related

- `Stash.Registry/Auth/Authorization/RegistryActionResourceResolver.cs` — ClaimScope intentionally absent
- `Stash.Registry/Controllers/ScopesController.cs` — `[ImperativeAuthz]` on ClaimScope with corrected reason
- `Stash.Tests/Registry/RegistryActionResourceResolverTests.cs` — pins the throw
- `.kanban/4-done/registry-authz-filter/` — the original authz filter feature
- `.kanban/4-done/registry-authz-pdp-completion/` (after /done) — the PDP completion feature that established the irreducible {ClaimScope} end-state
- `Stash.Tests/Registry/Authz/AuthzDispatchCoverageMetaTests.cs` — `PinnedImperativeActions = {"ScopesController.ClaimScope"}`
