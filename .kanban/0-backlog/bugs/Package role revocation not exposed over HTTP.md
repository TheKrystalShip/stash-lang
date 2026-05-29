# Package role revocation not exposed over HTTP

**Status:** Backlog — Bug
**Created:** 2026-05-30
**Discovery context:** Surfaced during the `registry-scope-foundation` feature review (F02) while wiring the CLI `stash pkg owner remove` command to the renamed roles endpoint. The resolver agent found that `RevokePackageRoleAsync` exists at the DB layer but no controller route exposes it, making `stash pkg owner remove` permanently non-functional.

---

## Problem

`stash pkg owner remove <package> <username>` cannot work because the registry has no HTTP endpoint to revoke a package role. The database layer provides `RevokePackageRoleAsync` (see `StashRegistryDatabase.cs:582`) but no controller wires it up. The CLI `RegistryClient.RemoveOwner` therefore throws `InvalidOperationException` with a clear message rather than silently hitting a 404. The `stash pkg owner remove` surface is entirely broken and will remain so until this gap is closed.

## Reproduction

```bash
# With a running registry and a valid admin token:
stash pkg owner remove mypkg someuser
# Expected: user's owner role is revoked
# Actual: Error: Role revocation is not yet supported by this registry version. ...
```

Alternatively, confirm at the HTTP level — there is no `DELETE /api/v1/admin/packages/{scope}/{name}/roles` or equivalent route defined anywhere in the registry.

## Blast radius

- All users of `stash pkg owner remove` — the command is completely non-functional today.
- Currently latent (no known real-world deployments with multiple owners), but becomes load-bearing as soon as any org or team uses the registry for shared package ownership.
- Does not affect `stash pkg owner add` (assign role) or `stash pkg owner list` — those work correctly.

## Root cause

`AdminController` (and `PackagesController`) were extended with assign-role (`PUT …/roles`) and list-roles (`GET …/roles`) endpoints during `registry-scope-foundation`, but the revoke-role operation was intentionally deferred as out of scope for that feature. The DB method `RevokePackageRoleAsync` at `Stash.Registry/Database/StashRegistryDatabase.cs:582` exists but is orphaned — no controller action calls it.

## Suggested fix

Add a revoke-role HTTP endpoint and wire it to the existing DB method:

- (A) `DELETE /api/v1/admin/packages/{scope}/{name}/roles/{principalType}/{principalId}` on `AdminController` — matches the admin-scoped assign route shape; requires admin token. Recommended: admin-only revocation keeps privilege management centralised.
- (B) `DELETE /api/v1/packages/{scope}/{name}/roles/{principalType}/{principalId}` on `PackagesController` — would allow a publish-scoped token to revoke roles, which may be too permissive.

Recommend (A): mirrors the `PUT /admin/packages/{scope}/{name}/roles` assign route and keeps role management under the admin policy, consistent with how roles were added.

Implementation sketch for (A):

1. Add `[HttpDelete("{scope}/{name}/roles/{principalType}/{principalId}")]` action to `AdminController`, calling `await _db.RevokePackageRoleAsync(scope, name, principalType, principalId)`.
2. Return `204 No Content` on success, `404` if the package or role entry does not exist.
3. Update `RegistryClient.RemoveOwner` in `Stash.Cli` to call `DELETE {_baseUrl}/admin/packages/{ScopedPackagePath(packageName)}/roles/user/{username}` instead of throwing.
4. Regenerate docs if any stdlib reference is affected (unlikely for a registry-only change).

## Verification

```bash
# After the fix, this should succeed with a 204:
curl -X DELETE -H "Authorization: Bearer $ADMIN_TOKEN" \
  http://localhost:5000/api/v1/admin/packages/myscope/mypkg/roles/user/someuser

# CLI smoke test:
stash pkg owner remove mypkg someuser
# Expected: "Removed someuser from owners of mypkg."

# Regression: existing assign and list still work:
stash pkg owner add mypkg someuser
stash pkg owner list mypkg

# Test gate:
dotnet test --filter "FullyQualifiedName~RegistryRoutesTests"
# Plus a new test: AdminController_RevokeRole_Returns204
```

## Related

- `registry-scope-foundation` feature — introduced the roles system; revoke was explicitly deferred (see `review.md` F02 fixed note and `plan.yaml` `done_when`).
- `Stash.Registry/Database/StashRegistryDatabase.cs:582` — `RevokePackageRoleAsync` implementation, orphaned.
- `Stash.Cli/PackageManager/RegistryClient.cs` — `RemoveOwner` currently throws `InvalidOperationException` as an honest stub; it should call the new endpoint once this bug is resolved.
- `Stash.Cli/PackageManager/Commands/OwnerCommand.cs` — `remove` branch calls `RemoveOwner` and prints "Failed to remove owner." on `false`; will work correctly once `RemoveOwner` stops throwing and returns a real result.
