# Registry OpenAPI Hardening — Review (Pass 2)

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header is parseable: `## Fxx — [SEVERITY] short title`.
> Severity scheme: CRITICAL / IMPORTANT / MINOR.

**Scope reviewed:** commits `f194030e..5e3ea036` on branch `feature/registry-openapi-hardening` (full feature range; this pass concentrates on the four fix commits since pass 1: `9055f12b`, `ad21b6f9`, `c1a77dd0`).
**Brief:** ./brief.md
**Generated:** 2026-06-03

Pass-1 findings (F01 HIGH, F02 MEDIUM, F03 MEDIUM, F04 LOW) have all been verified resolved by inspecting their fix commits against the original observations. Summary of verification:

- **F01 (9055f12b)** — `InvalidModelStateResponseFactory` is correctly installed globally in `Startup.ConfigureServices` and emits `BadRequestObjectResult(new ErrorResponse{Error="InvalidRequest", Message=...})`. The `Error = "InvalidRequest"` value is a free-form error message field (the existing convention is mixed prose / loose tokens — `"Invalid JSON body."`, `"registration_disabled"`, `"TokenLifetimeExceeded"`); the CLAUDE.md no-magic-strings doctrine explicitly exempts free-form error/exception prose, so this is not a bounded-domain violation. A new `BoundedDomainBoundaryTests.AssignRole_WithInvalidPackageRole_Returns400WithErrorResponse` exercises the `[FromBody]` deserialization-failure path (the original `CreateUser` case used the inline `JsonSerializer.DeserializeAsync` path) and asserts the body has `error` and NOT `type` (rejecting the RFC-7807 shape). No tests asserted RFC-7807 shape, so the global behavior change broke nothing.
- **F02 (c1a77dd0)** — `SqliteDatabaseTests.BoundedDomain_CLRDefault_MatchesDDLDefaultLiteral` asserts `default(Visibilities).ToWire() == "public"`, `default(UserRoles).ToWire() == "user"`, `default(OrgRoles).ToWire() == "member"`. Uses the actual `ToWire()` extension method. Pairs with the pre-existing `Initialize_BoundedDomainColumnDefaults_AreLowercaseWireStrings` DDL-side guard.
- **F03 (ad21b6f9)** — `JsonUnauthorized<T>`, `JsonForbidden<T>`, `JsonNotImplemented<T>` are relocated to `Stash.Registry/OpenApi/JsonStatusResults.cs` under namespace `Stash.Registry.OpenApi`. The three classes' bodies (`ExecuteAsync` + `IEndpointMetadataProvider.PopulateMetadata` with `ProducesResponseTypeMetadata`) are byte-identical to the originals. `using Stash.Registry.OpenApi;` is added to both `AuthController.cs` and `ScopesController.cs`; the originals are fully deleted (no stale duplicate). The misleading "must be internal" comment in `AuthController.cs` has been corrected to explain the `public`-required reason (CS0050). The OpenAPI snapshot's 401/403/501 entries still point to `ErrorResponse`/`ScopeVerifyResponse` schemas unchanged.
- **F04 (c1a77dd0)** — `Program.RunEnumSelfTest` now has a second deserialize loop using `Func<string, bool> RoundTrips` delegates that call `JsonSerializer.Deserialize(j, ctx.<EnumT>)` — the typed `JsonTypeInfo` overload, AOT-safe (no reflection-based `Deserialize<T>(string)`). All seven enum domains and all 19 enum values are exercised. PASS/FAIL/exit-code contract preserved; failures now distinguish `[serialize]` from `[deserialize]` direction in the FAIL detail.

Baseline at review entry: full `dotnet test` is **green** — failed=0 passed=13105 skipped=6 (the +2 over pass 1 is exactly the F01 boundary test and the F02 CLR-default test). The 6 skips are pre-existing source quarantines.

Fresh adversarial pass surfaces ONE new finding (a residual contract gap exposed by the F01 fix, distinct from the original F01 defect and not catchable by the existing coverage meta-test).

---

## F01 — [MINOR] Two `[FromBody]` endpoints can emit an undocumented `400 ErrorResponse` after the F01 fix — `Results<...>` unions omit `BadRequest<ErrorResponse>`

**Status:** fixed
**Fixed in:** b0061a8a
**Files:** `Stash.Registry/Controllers/PackagesController.cs:456` (`RevokeRole`), `Stash.Registry/Controllers/AdminController.cs:190` (`AdminRevokeRole`), `Stash.Tests/Registry/OpenApi/Snapshots/openapi-v1.json:241-265` (`Admin_AdminRevokeRole`), `Stash.Tests/Registry/OpenApi/Snapshots/openapi-v1.json:1666-1690` (`Packages_RevokeRole`)
**Phase:** Cross-phase — exposed by the F01 fix (`9055f12b`) on top of P2/P3 (typed `Results<...>` refactor) and P5 (snapshot baseline).
**Commit:** `9055f12b` (the fix that surfaces the gap)

### Observation

The F01 fix installs an `InvalidModelStateResponseFactory` globally in `Startup.ConfigureServices`. Every `[ApiController]` model-binding failure — including any `[FromBody]` deserialization failure on `JsonStringEnumConverter` — now returns `400 BadRequest` with an `ErrorResponse` body. Seven controller actions use `[FromBody]` (`grep "FromBody" Stash.Registry/Controllers/*.cs`):

| Action | `Results<...>` declares `BadRequest<ErrorResponse>`? |
| --- | --- |
| `PackagesController.DeprecatePackage` | yes |
| `PackagesController.DeprecateVersion` | yes |
| `PackagesController.AssignRole` | yes |
| `PackagesController.SetVisibility` | yes |
| `OrganizationsController.AddMember` | yes |
| **`PackagesController.RevokeRole`** | **no** — `Results<NoContent, NotFound<ErrorResponse>, Conflict<ErrorResponse>>` |
| **`AdminController.AdminRevokeRole`** | **no** — `Results<NoContent, NotFound<ErrorResponse>, Conflict<ErrorResponse>>` |

The published OpenAPI document (snapshot at `Stash.Tests/Registry/OpenApi/Snapshots/openapi-v1.json:241-265` for `Admin_AdminRevokeRole` and `:1666-1690` for `Packages_RevokeRole`) confirms the contract declares only `204`, `404`, `409` for these two endpoints — no `400`. With the global filter installed, a `DELETE /api/v1/packages/{scope}/{name}/roles` with `{"role":"NOT_A_REAL_ROLE"}` (or just malformed JSON) now returns a `400 ErrorResponse` body that is **not declared in the contract**.

**Other binding surfaces were checked and ruled out:** every controller action parameter is `string` (route/query) or `[FromBody] DTO?`. `SearchController.Search` reads `page`/`pageSize` manually via `int.TryParse(Request.Query["page"])`, not typed binding — so no model-binding failure path. No action exposes a typed `int`/`bool`/`Guid` parameter that could fail binding. The blast radius is exactly the two `RevokeRole` actions above.

**This is NOT a regression introduced by the F01 fix.** Pre-fix, these same two endpoints already returned a 400 on a malformed body — as `ValidationProblemDetails`. The fix changed only the *shape* (now correctly `ErrorResponse`); the response code itself was already undeclared. F01 was "wrong body shape"; this is "undeclared status code" — a distinct defect class that the `OpenApiCoverageMetaTests` cannot catch, because it only verifies that *declared* response codes resolve to `$ref` schemas (`FindViolations` at `OpenApiCoverageMetaTests.cs:189-201`) — it does not enumerate *emittable* codes back to declarations.

### Why this matters

The brief's stated purpose (Summary) is "a high-fidelity public contract suitable for third-party client generation." A strict OpenAPI client (e.g. `openapi-generator-cli` with `useResponseAsIs=false`, or any generator that enums response statuses) modeled against the document will reject the `400` body it does not expect on these two endpoints, even though the body is well-formed `ErrorResponse`. The fix made the body usable; this finding closes the residual gap by also documenting it.

`docs/Registry — Package Registry.md:1534` now claims "Submitting any other value for a field typed to one of these schemas yields `400 Bad Request` with an `ErrorResponse` body." For the two `RevokeRole` endpoints whose `Results<...>` unions don't declare 400, this is true at runtime (good, post-fix) but the published contract omits the 400 declaration — a generator can't see the doc-stated guarantee.

Severity rationale (MINOR, not IMPORTANT): the body is well-formed and matches every other 400 in the contract; clients implementing generic error handling will parse it correctly; only generators that strictly enum the declared response set are affected; and it is strictly better than the pre-fix state. No correctness or security impact on any well-formed call.

### Suggested fix

Two mechanical edits, then re-baseline the snapshot:

1. In `Stash.Registry/Controllers/PackagesController.cs:456` change
   ```csharp
   public async Task<Results<NoContent, NotFound<ErrorResponse>, Conflict<ErrorResponse>>> RevokeRole(...)
   ```
   to add `BadRequest<ErrorResponse>` to the union (mirroring the sibling `AssignRole` action at `:428` which already includes it).
2. In `Stash.Registry/Controllers/AdminController.cs:190` apply the same change to `AdminRevokeRole`.
3. Re-baseline `Stash.Tests/Registry/OpenApi/Snapshots/openapi-v1.json` with `STASH_SNAPSHOT_REGEN=1 dotnet test --filter FullyQualifiedName~OpenApiSnapshotTests` and inspect the diff: it should add `"400"` blocks pointing to `#/components/schemas/ErrorResponse` for the two operations.

Optionally extend `BoundedDomainBoundaryTests` with a parameterized case that POSTs an illegal `principalType` (e.g. `"NOT_A_REAL_TYPE"`) to `RevokeRole` and asserts both the 400 status and that the operation now declares 400 in the live doc — closes the test gap for these two endpoints. Optional because the existing `AssignRole_WithInvalidPackageRole_Returns400WithErrorResponse` case proves the global filter shape is correct; the missing piece is the doc declaration, which the snapshot diff itself attests.

### Verify

```bash
dotnet test --filter "FullyQualifiedName~Registry.OpenApi|FullyQualifiedName~BoundedDomainBoundaryTests|FullyQualifiedName~RegistryAuthzMatrixTests"
# OpenApiSnapshotTests must pass against the re-baselined snapshot
# RegistryAuthzMatrixTests must remain green (behavior preservation)
```
