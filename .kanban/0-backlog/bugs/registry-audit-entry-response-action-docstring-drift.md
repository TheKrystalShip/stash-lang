# AuditEntryResponse.Action docstring lists stale example values (`"publish"`, `"unpublish"`)

**Status:** Backlog — Bug
**Created:** 2026-06-05
**Discovery context:** Surfaced during pass-2 review of feature `registry-download-metrics`. The F01 fix split `AuditActions.Publish`/`Unpublish` (helper, vestigial test-only) from `AuditActions.PackagePublish`/`PackageUnpublish` (controller, real wire contract). The split made the existing wire DTO docstring on `AuditEntryResponse.Action` actively misleading. Pre-existing — NOT introduced or modified by this feature (last touched by `b036d653` shared-contracts extraction; the original `"publish"`/`"unpublish"` examples predate the controller's adoption of `"package.publish"`/`"package.unpublish"`).

---

## Problem

`Stash.Registry.Contracts/AdminContracts.cs:226` documents the `AuditEntryResponse.Action` wire field as:

```csharp
/// <summary>The action type string, e.g. <c>"publish"</c>, <c>"unpublish"</c>, <c>"user.create"</c>.</summary>
[JsonPropertyName("action")]
public string Action { get; set; } = "";
```

The example values `"publish"` and `"unpublish"` are **not** what the registry controllers actually write to the audit log. The real publish-path / unpublish-path wire values are `"package.publish"` and `"package.unpublish"` (emitted by `PackagesController.PublishPackage` and `PackagesController.UnpublishVersion` via `AuditActions.PackagePublish` / `AuditActions.PackageUnpublish`, pinned by `RegistryAuthzAuditMutationTests` / `RegistryAuthzMatrixTests`).

A future CLI / UI engineer reading this docstring and writing `?action=publish` or `?action=unpublish` against `GET /api/v1/admin/audit-log` will get zero rows for every real controller-driven publish/unpublish — because those are stored as `"package.publish"` / `"package.unpublish"`. The only entries that would match `"publish"` / `"unpublish"` are the ones written by the vestigial test-only helpers `AuditService.LogPublishAsync` / `LogUnpublishAsync`, which have **no production caller** (verified by `grep` — only `AuditServiceTests` calls them).

## Reproduction

```bash
# 1. Inspect the docstring
grep -n "action type string" Stash.Registry.Contracts/AdminContracts.cs
# Stash.Registry.Contracts/AdminContracts.cs:226: ... "publish", "unpublish", "user.create" ...

# 2. Inspect the actual controller wire values
grep -n "AuditActions.Package" Stash.Registry/Controllers/PackagesController.cs
# ... AuditActions.PackagePublish        ("package.publish")
# ... AuditActions.PackageUnpublish      ("package.unpublish")

# 3. Inspect the wire-contract pinning tests
grep -n "package.publish\|package.unpublish" Stash.Tests/Registry/Authz/RegistryAuthzAuditMutationTests.cs
# Both lines assert the real wire values.
```

## Blast radius

- **Latent today, high cost on first encounter.** No deployed registry instance, no existing audit-log consumer. The CLI does not surface audit-log filtering yet; the website P5 (admin/audit surface) is in the parked Bucket-B backlog.
- **Becomes load-bearing as soon as the website admin/audit panel ships.** That panel will hand the docstring's example values to a maintainer-facing UI; a UI engineer building filter chips from the example values would silently filter out every real publish/unpublish row.
- **Compounds with audit-log-v2.** That feature plans to expand the action-string surface; a misleading example set in the existing wire DTO will mislead its design.

## Root cause

`Stash.Registry.Contracts/AdminContracts.cs:226` — the example values `"publish"` and `"unpublish"` were accurate when `AuditService` was the sole audit writer and emitted those literals directly. The controller-level audit migration (which standardised on dotted `"package.publish"` / `"package.unpublish"` values) updated the writers and the wire-contract pinning tests but did not update the wire DTO's example docstring.

## Suggested fix

Single-line docstring revision:

```csharp
/// <summary>
/// The action type string written by the writer that produced this entry, e.g.
/// <c>"package.publish"</c>, <c>"package.unpublish"</c>, <c>"package.create"</c>,
/// <c>"package.deprecate"</c>, <c>"version.deprecate"</c>, <c>"user.create"</c>,
/// <c>"token.create"</c>, <c>"role.assign"</c>. The complete, single source of truth
/// is <see cref="Services.AuditActions"/> in <c>Stash.Registry</c>.
/// </summary>
```

Optionally extend `AuditLogQuery.action` (line 90, same file) with the same cross-reference so the filter-side docstring also points consumers at the canonical list.

No code change; pure documentation fix. Zero test impact.

## Verification

```bash
# Docstring grep should no longer surface the bare "publish" example
grep -n '"publish"' Stash.Registry.Contracts/AdminContracts.cs
# Expected: zero results (or only inside an explicit context that names it as helper-only).

# Existing pin still green
dotnet test --filter "FullyQualifiedName~RegistryAuthzAuditMutationTests"
```

## Related

- Surfaced during pass-2 review of `registry-download-metrics` (P2 of `self-hosted-registry` milestone, `.kanban/2-in-progress/registry-download-metrics/`).
- F01 of that feature (commits `69fd790d`, `04402e48`) split the constants and exposed the docstring drift.
- `Stash.Registry/Services/AuditActions.cs` — the corrected single source of truth (post-F01).
- Future blocker for the deferred website P5 (admin/audit panel).
- Audit-log-v2 milestone (later P-phase of `self-hosted-registry`) will refer to the canonical action set.
