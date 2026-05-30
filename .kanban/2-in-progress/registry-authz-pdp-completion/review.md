## Registry Authorization PDP Completion — Review

> Produced by `/feature-review`. Zero findings — clean review.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `a891e86..6b9f1fd` on branch `main`, intersected with `plan.yaml` scope globs (16 files, +570/-77).
**Brief:** ../brief.md
**Generated:** 2026-05-31 (UTC)

---

## Summary

Zero findings. The four-phase fold (`DeleteOrg`, `DeleteScope`, `PublishPackage` into the shared `[RegistryAuthorize]` filter; `ClaimScope` documented as the irreducible remnant) lands cleanly and conforms to every brief acceptance criterion and every `done_when` line in `plan.yaml`.

### Brief parity — every acceptance criterion verified against the diff

| Acceptance criterion | Verified by |
| --- | --- |
| `DeleteOrg` declarative + body has no inline PDP | `OrganizationsController.cs` diff: `[RegistryAuthorize(RegistryAction.DeleteOrg)]`; constructor lost `_authorizer`/`_auditService`/`_principalFactory`; body is pure work. |
| `DeleteScope` declarative + `@`-prefixed deny audit-id | `ScopesController.cs` diff: `[RegistryAuthorize(RegistryAction.DeleteScope)]`; inline PDP block deleted. `RegistryAuthorizeFilter.ResourceIdForAudit(ScopeResource) => "@" + scope.Scope`. |
| `PublishPackage` declarative + new enum member + PDP delegation | `RegistryAction.cs:26` (`PublishPackage` member), `RegistryAuthorizer.cs:112` (`PublishPackage => TokenCeiling.Publish`), `RegistryAuthorizer.cs:181-183` (dispatch arm) + `AuthorizePublishPackageAsync` (lines 414-429) with `_ctx.Packages.AsNoTracking().AnyAsync(p => p.Name == pkg.FullName)` and delegation to `AuthorizePackageRoleAsync(..., PackageRoles.Publisher)` / `AuthorizeCreatePackageAsync`. Predicate is semantically equivalent to the prior `_db.PackageExistsAsync(name)` (`StashRegistryDatabase.cs:84`: `_context.Packages.AnyAsync(p => p.Name == name)`). |
| Token ceiling parity | Both `PublishVersion` and `CreatePackage` were already `TokenCeiling.Publish`; the unified `PublishPackage => Publish` cannot over- or under-restrict either delegated path. |
| `ClaimScope` reason rewritten + backlog link | `ScopesController.cs` diff: reason names body-derived scope/owner/ownerType + bespoke 409 mapping + links `.kanban/0-backlog/registry/Body-resolver authz filter.md`. |
| Resolver hardening (no `ClaimScope` arm) | `RegistryActionResourceResolver.cs:71-74` — `ClaimScope` removed from the `ScopeResource` arm; default arm throws. Pinned by `RegistryActionResourceResolverTests.Resolve_ClaimScope_ThrowsInvalidOperationException`. No production caller currently invokes `Resolve(ClaimScope, …)` — the filter only invokes the resolver for endpoints carrying `[RegistryAuthorize]`, and `ClaimScope` carries `[ImperativeAuthz]`. Throw is reachable only via misuse, exactly as intended. |
| Pin = `{ClaimScope}` | `AuthzDispatchCoverageMetaTests.cs:65-67`: set shrunk to a single entry. |
| `delete_org.deny.audit_id` / `delete_scope.deny.audit_id` / `publish.deny.label` matrix rows | `AuthzMatrixData.cs` diff adds all three; `RegistryAuthzMatrixTests.cs` adds three new `[Theory]` methods (sections 15/16/17) each asserting status + body + `Single(denyEntries)` + (where applicable) `resource_id` shape. |
| Uniform `@` prefix proof at filter level | `RegistryAuthzAuditMutationTests.Audit_VerifyScope_AuthenticatedDeny_EmitsExactlyOneDenyEntry` now asserts `Assert.Equal("@alice-am9", denyEntries[0].Package)` — the previously-unasserted resource_id is now load-bearing for the uniform-helper claim. |
| Deny label uniform regardless of DB state | Two new audit-mutation tests cover both the "package exists" and "package does not exist" branches; both assert `action == "PublishPackage"`. |
| Allow-path mutation labels unchanged | `PackagesController.PublishPackage` body still calls `_packageService.PublishAsync` whose `isNewPackage` return drives the existing `package.create`/`package.publish` allow-path audit emission — not touched by P3. |
| `RegistryAuthorizerTests` covers PublishPackage delegation | Two new `[Fact]`s pin the exists→`PublishVersion`-path and not-exists→`CreatePackage`-path delegation outcomes. |
| Docs updated | `Stash.Registry/CLAUDE.md` Controller Pattern table now has exactly one `[ImperativeAuthz]` row (`ClaimScope`, corrected reason) and a three-row "now folded" list. `.claude/repo.md` carries the completed-features history line. |
| `final_verify` filter shape matches predecessor | `plan.yaml` final_verify mirrors `registry-authz-filter`'s precedent. |
| Tests green | Baseline: authz suite 135/135 pass; broad filtered registry suite 9536 pass / 0 fail / 101 skip. |

### Risk-area-by-risk-area confirmation (reviewer prompt's focus list)

1. **Deny-label shift on `PublishPackage` is uniform across both DB branches.** The two new `Audit_PublishPackage_*` tests deliberately exercise the not-existing and existing cases; both assert `action == "PublishPackage"`. Allow-path is unchanged because `PackageService.PublishAsync`'s `isNewPackage` return — not the authz action — drives the mutation label.
2. **PDP delegation correctness.** `AuthorizePublishPackageAsync` (Authorizer.cs:414-429) delegates to `AuthorizePackageRoleAsync(..., PackageRoles.Publisher)` when the package exists — which is exactly what `RegistryAction.PublishVersion`'s arm does (Authorizer.cs:161-162) — and to `AuthorizeCreatePackageAsync` when it does not, matching `RegistryAction.CreatePackage`'s arm (Authorizer.cs:164-165). No duplication of policy logic; both existing handlers remain the single source of truth.
3. **Existence-check predicate equivalence.** PDP uses `_ctx.Packages.AsNoTracking().AnyAsync(p => p.Name == pkg.FullName)`; `_db.PackageExistsAsync(name)` is `_context.Packages.AnyAsync(p => p.Name == name)`. `AsNoTracking()` is a perf-only difference (no entity-tracking overhead for an existence check). Same DbContext, same table, same predicate.
4. **TOCTOU window timing.** Pre-refactor the controller did the existence check inline, then did work. Post-refactor the PDP does it, then the controller does work. The window between check and work is structurally identical; this refactor neither widened nor narrowed it. `RegistryAuthzAtomicCreateTests` (still green per baseline) continues to cover the pre-existing concurrent-create case.
5. **`ClaimScope` resolver-arm deletion.** The default arm throws `InvalidOperationException("Add an entry to RegistryActionResourceResolver.Resolve for ...")`. Reachable only if `[RegistryAuthorize(RegistryAction.ClaimScope)]` is ever attached to a controller method — which would simultaneously trip `AuthzDispatchCoverageMetaTests` (pin would carry an `extra` `ClaimScope`). Both guards fail closed in opposite directions. Unit test `RegistryActionResourceResolverTests.Resolve_ClaimScope_ThrowsInvalidOperationException` pins the throw.
6. **Audit-id `@` prefix uniformity (P2).** The change lives in a single helper (`RegistryAuthorizeFilter.ResourceIdForAudit(ScopeResource)`) with no per-action branching, so it applies to `ResolveScope` / `VerifyScope` / `DeleteScope` alike. `Audit_VerifyScope_AuthenticatedDeny_EmitsExactlyOneDenyEntry` now asserts `Assert.Equal("@alice-am9", denyEntries[0].Package)` — load-bearing structural proof that the uniformity is filter-level, not controller-level.

### Hard rules

- No source files were edited during review.
- The feature directory was not moved.
- No backlog bug stubs were filed (no out-of-scope bugs surfaced).

Checkpoint review status will be set to `resolved`.
