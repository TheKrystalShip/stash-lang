# RFC: Registry Declarative Request Validation

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-06-03
> **Slug:** registry-declarative-request-validation
> **Milestone:** —

## Summary

The registry controllers still do manual, inline request validation: ten action bodies open `Request.Body`, deserialize a DTO with `JsonSerializer.DeserializeAsync<T>`, `try/catch (JsonException)`, then run a sequence of `if (string.IsNullOrEmpty(...))` / regex / length guards before doing real work. This feature moves request validation **out of the controllers and into ASP.NET Core's declarative pipeline** — `[ApiController]`'s automatic `ModelStateInvalidFilter`, fed by `[FromBody]` parameters and `DataAnnotations` on the DTOs in `Stash.Registry.Contracts/`. The result: controller bodies carry only business logic, structural validation runs before the action, and the 400 it produces is the same `ErrorResponse` envelope the rest of the registry already promises (via the already-shipped `InvalidModelStateResponseFactory`).

The shape on the wire does not change: `ErrorResponse { error, message }` 400s today, `ErrorResponse { error, message }` 400s after. The `RegistryAuthzMatrixTests` matrix stays green every phase. What changes is *where* the 400 comes from and *what the controllers contain*.

## Motivation

Today's manual-validation pattern has four costs:

1. **The same ten-line ceremony appears across ten endpoints** (`try { DeserializeAsync } catch { 400 }` + null/empty/length/regex guards). It is copy-pasted, drifts subtly, and obscures the business logic the action actually performs.
2. **DataAnnotations are inert on manually-deserialized bodies.** `DeprecatePackageRequest.Message` already carries `[MinLength(1)]`, but the action does not flow through the validation filter, so the attribute does nothing. The platform feature the user pays for is sitting unused.
3. **`registry-openapi-hardening` shipped the destination** for this work and called it out explicitly: `InvalidModelStateResponseFactory` is wired in `Startup.cs:82-99` to emit `ErrorResponse` on every `[ApiController]`-driven 400 — including the enum-deserialization 400s for the new `BoundedDomains` enums. That destination is dormant for every action that bypasses model binding.
4. **No guard against a new endpoint regressing to the manual pattern.** A future controller addition that opens `Request.Body` directly looks identical at review (no diff in the validation filter, no diff in the OpenAPI doc) but ships an endpoint with zero attribute-level checks. The closest existing example is `OrganizationsController.AddMember` (line 159, `[FromBody] AddOrgMemberRequest? body`) — already half-migrated and silently nullable, so passing an empty body skips DTO validation entirely.

The user's framing nails the goal: "controllers don't have to do manual model validation… attributes like `[Required]` or range ones for exactly this purpose."

## Goals

- Migrate the ten remaining manual-`DeserializeAsync` actions to `[FromBody]` parameters so the dormant `ModelStateInvalidFilter` activates on them.
- Annotate the non-bounded fields on the request DTOs in `Stash.Registry.Contracts/` with `DataAnnotations` (length, range, regex, presence). Bounded fields (`role`, `visibility`, `principal_type`, …) are already covered by the seven shipped enums and are out of scope.
- Express cross-field / grammar rules that no single attribute captures (`PackageManifest.IsValidScopeName`, semver, password policy beyond length, "owner required when `owner_type=org`") as `ValidationAttribute` subclasses or `IValidatableObject.Validate()` so they run inside the same filter.
- Land a Roslyn meta-test in phase 1 that fails CI if any non-exempt controller action reads `Request.Body` directly or accepts a request DTO without `[FromBody]`. The exemption list shrinks to empty across the migration phases.
- Preserve every existing wire behaviour: `RegistryAuthzMatrixTests`, `OpenApiSnapshotTests`, `OpenApiCoverageMetaTests`, and `BoundedDomainBoundaryTests` stay green at every phase boundary.

## Non-Goals

- **Bounded-domain re-work.** `Stash.Registry.Contracts/BoundedDomains.cs` shipped P4 of `registry-openapi-hardening` as seven C# enums; illegal `role`/`visibility`/`principal_type`/`owner_type`/`token_scope`/`org_role`/user-`role` wire values already 400 at the JSON deserializer. Do not touch this file.
- **`ValidationProblemDetails` / RFC 9457.** The user's earlier choice was made before the OpenAPI hardening landed. We build on the shipped `InvalidModelStateResponseFactory` and `ErrorResponse` envelope. No second 400 shape will be introduced.
- **Folding `ScopesController.ClaimScope` into the shared `RegistryAuthorizeFilter`.** Migrating `ClaimScope` to `[FromBody] ClaimScopeRequest` is in scope (so the validation filter sees its DTO); folding the authz decision into the shared filter requires a body-resolver refactor, tracked in `.kanban/0-backlog/registry/Body-resolver authz filter.md`. The `[ImperativeAuthz]` marker, the inline PDP call, and the `AuthzDispatchCoverageMetaTests` imperative-pin all stay.
- **`ScopesController.VerifyScope`, owner verification flows, scope challenge issuance.** Untouched by this feature.
- **AOT trim warnings on new attributes as a per-phase test.** New `[RegularExpression]` / `[StringLength]` / `[Range]` attributes have `[RequiresUnreferencedCode]` paths on their reflection helpers; mirror the existing `DeprecatePackageRequest.Message` `[UnconditionalSuppressMessage]` precedent on each. AOT confirmation is a `dotnet publish` check in `build.stash`, not in `dotnet test` (per project memory: the AOT enum round-trip was deliberately removed from the unit suite).
- **Validation of `[FromRoute]` parameters** (e.g. `{scope}`, `{name}`, `{username}` segments). Route templates and `Uri.UnescapeDataString` cover the existing surface; that work belongs to a separate feature.

## Design

### Surface

The wire surface does not change. The internal C# surface gains three things:

1. **`[FromBody] T request` parameters** on the ten currently-manual actions.
2. **`DataAnnotations` attributes** on request DTOs in `Stash.Registry.Contracts/`:
   - `[Required]` for fields the action requires beyond JSON `required` (which only enforces *presence*; `[Required]` also rejects empty/whitespace strings).
   - `[StringLength(maximumLength: M, MinimumLength: N)]` for `password` (min 8), `Username` (≤ 64 on the admin path).
   - `[RegularExpression]` for the scope-grammar identifiers (`Username` in `RegisterRequest`, `Name` in `CreateOrgRequest` and `CreateTeamRequest`, `Scope` in `ClaimScopeRequest`).
   - `[Range]` for `[FromQuery]` `page` and `pageSize` (see Semantics below for the behaviour-change call-out).
   - `[MinLength(1)]` (already shipped on `DeprecatePackageRequest.Message` and `DeprecateVersionRequest.Message`) for non-empty free-text fields.
3. **Two custom `ValidationAttribute` types** for closed-form helpers that already exist in the registry:
   - `ScopeGrammarAttribute` — wraps `PackageManifest.IsValidScopeName`. Used on `RegisterRequest.Username`, `CreateOrgRequest.Name`, `ClaimScopeRequest.Scope`. Centralises the single rule that today is expressed in four slightly different error strings.
   - `TokenExpiryAttribute` — wraps `AuthHelper.ParseTokenExpiry` + the ≥ 1h floor. Used on `TokenCreateRequest.ExpiresIn`. The ceiling (`Security.MaxTokenLifetime`) is config-dependent and stays as an inline guard.
4. **`IValidatableObject` on cross-field DTOs** (only `ClaimScopeRequest`): `OwnerType=org` requires `Owner`; `OwnerType=user` requires `Owner` to be a non-empty username string. These attributes live alongside the trim/lowercase normalisation in the action (normalisation stays inline — see Semantics).

The `InvalidModelStateResponseFactory` already in `Startup.cs:82-99` is the 400 sink for all four. Its body remains:

```csharp
options.InvalidModelStateResponseFactory = ctx =>
{
    // ... aggregated message (see Decision Log 2026-06-03 — Multi-field aggregation) ...
    return new BadRequestObjectResult(new ErrorResponse { Error = "InvalidRequest", Message = message });
};
```

### Semantics

**The 400 path.** A request that fails any attribute on a `[FromBody]` / `[FromQuery]` parameter short-circuits in the `ModelStateInvalidFilter` *before* the controller action body runs. The factory turns the `ModelState` into a `BadRequestObjectResult(ErrorResponse)` with `Error = "InvalidRequest"`. Stable, machine-readable. The current `Error = "Invalid JSON body."` literal (used by the ten manual catches) goes away — the wire `Error` for "couldn't deserialize" becomes `InvalidRequest` everywhere, including the malformed-JSON case (the existing factory already covers it; we are extending its reach, not changing its body).

**Aggregation across fields.** The factory shipped today reads only `FirstOrDefault()` from `ModelState`. Once DataAnnotations is doing the work, multiple fields can fail at once. The factory is enriched (one-line change) to join all field-bound messages with `"; "` into `ErrorResponse.Message`. The `Error` code stays `"InvalidRequest"`. This is a coordinated change with `OpenApiSnapshotTests` (the OpenAPI snapshot doc references `ErrorResponse` by `$ref`, not by error-message text, so no snapshot bump is needed) and with `RegistryAuthzMatrixTests` (its 400 assertions key on `Error`, not on `Message` — see Open Questions 1 for the discriminating verification).

**The `[ImperativeAuthz]` action — `ScopesController.ClaimScope`.** Migrating to `[FromBody] ClaimScopeRequest? body` does **not** fold the authz into the shared filter. The marker stays. The action body still calls `_authorizer.AuthorizeAsync(...)` inline. The `AuthzDispatchCoverageMetaTests` imperative-pin (`{ScopesController.ClaimScope}` is the single allowed `[ImperativeAuthz]` endpoint) stays green untouched. The benefit is purely that `ClaimScopeRequest.Scope` and `ClaimScopeRequest.OwnerType` now flow through the validation filter, so the `if (string.IsNullOrEmpty(scopeName))` and `if (ownerTypeRaw == null || ...)` guards collapse into attributes and an `IValidatableObject`. The deferred body-resolver refactor in `.kanban/0-backlog/registry/Body-resolver authz filter.md` is *helped* by this migration (the `[FromBody]` parameter is the shape that refactor wants), not blocked. We verified that no `AuthzMatrixData` row sends a malformed/incomplete body to ClaimScope expecting a 403/409 status — every ClaimScope row uses a well-formed body, so moving structural validation in front of the inline PDP is wire-equivalent.

**Query-string pagination — declared, not clamped.** Two endpoints read pagination via manual `Request.Query`: `SearchController.Search` (page, pageSize; clamp to 100) and `AdminController.GetAuditLog` (page, pageSize; clamp to 200). We migrate both to `[FromQuery] SearchQuery` / `[FromQuery] AuditLogQuery` parameter DTOs with `[Range(1, int.MaxValue)]` on `page` and `[Range(1, 100)]` / `[Range(1, 200)]` on `pageSize`. **This is a behaviour change**: a request with `pageSize=500` today **silently clamps** to 100; after this feature it returns 400 `InvalidRequest`. We accept the change because the current silent-clamp behaviour is undiscoverable from the API and confusingly conceals the cap — but it is a behaviour change for any client passing out-of-range values, and is recorded in the Decision Log. (No test today asserts the clamp behaviour, verified by grep: no `pageSize` assertion in `Stash.Tests/Registry/` outside the OpenAPI snapshot's schema.)

**Guard deletion — the policy.** Once `[FromBody]` + DataAnnotations are in place, many inline guards become dead. We delete:

- Every `try { DeserializeAsync } catch { 400 }` block (replaced by the filter).
- Every `if (string.IsNullOrEmpty(...))` / `if (... == null)` on a `[Required]`-annotated field.
- Every `if (... .Length < 8)` / regex check that a `[StringLength]` / `[RegularExpression]` now covers.

We keep:

- DB-existence checks (`if (orgRecord == null) return NotFound(...)` — these are 404s, not 400s, and they touch the database; DataAnnotations cannot see them).
- Normalisation (`body.Name.Trim().ToLowerInvariant()`, `Uri.UnescapeDataString(scope)`). These mutate values, not validate them, and stay inline.
- Reserved-name checks (`ReservedScopes.IsReserved(name)` returns 409, not 400).
- Cross-resource business guards (`callerUsername == owner` for user-scope claim, "is org owner"). These need the principal and the database; DataAnnotations cannot see them.
- The "deferred shape" guard (`if (body?.Capabilities != null)` in `CreateToken`) — semantically a *forbid*, not a *validate*, and it returns a custom `capabilities_not_supported` error code. Stays inline.

This policy makes the migration diff intentional: every deleted line was strictly covered, every kept line was strictly out of reach.

### Implementation Path

```
A. The enforcement test goes RED with a pinned exemption list (P1)
    -> A Roslyn meta-test scans every controller .cs in Stash.Registry/Controllers/
       and flags either (a) a call to JsonSerializer.DeserializeAsync<T>(Request.Body, ...)
       or (b) a public action method missing [FromBody]/[FromQuery] on any request DTO
       parameter declared in Stash.Registry.Contracts/.
    -> The exemption list is a HashSet<string> of "{Controller}.{Action}" entries,
       seeded with the ten currently-manual actions PLUS the two query-string actions
       (Search.Search, Admin.GetAuditLog). Twelve entries total.
    -> Self-tests prove the scan has teeth (fail-path fixture, binding-floor probe,
       file-count floor).

B. The Contracts DTOs gain attributes (P2)
    -> DataAnnotations on the eight request DTOs (LoginRequest, RegisterRequest,
       TokenCreateRequest, RefreshTokenRequest, CreateUserRequest, CreateOrgRequest,
       CreateTeamRequest, AddTeamMemberRequest, ClaimScopeRequest, plus the two new
       [FromQuery] DTOs SearchQuery and AuditLogQuery). Two new ValidationAttribute
       subclasses (ScopeGrammarAttribute, TokenExpiryAttribute) live in
       Stash.Registry.Contracts/Validation/. ClaimScopeRequest implements
       IValidatableObject for the cross-field rules.
    -> All attributes with [RequiresUnreferencedCode] reflection paths carry
       [UnconditionalSuppressMessage] mirroring the existing [MinLength] precedent.

C. The factory aggregates all field messages (P2, same phase as B)
    -> One-line factory change in Startup.cs: SelectMany over all ModelState errors,
       join with "; ". Error stays "InvalidRequest".
    -> The OpenAPI snapshot does not change ($ref to ErrorResponse stays).

D. Controllers migrate batch-by-batch — each batch removes its endpoints from the
   meta-test exemption list (P3, P4, P5)
    -> P3: AuthController (4 actions: Login, Register, CreateToken, RefreshToken)
       + SearchController.Search (1 query-string migration).
    -> P4: OrganizationsController (3 actions: CreateOrg, CreateTeam, AddTeamMember;
       AddMember already [FromBody] but body is nullable — flip to non-nullable).
    -> P5: AdminController (2 actions: CreateUser, AdminAssignRole) +
       AdminController.GetAuditLog (1 query-string migration) + ScopesController.ClaimScope
       (1 body migration; [ImperativeAuthz] stays).

E. The exemption list is empty and the feature is done (P5 done_when)
    -> The meta-test's exemption set asserts equal to the empty set.
    -> Every action that takes a request body or query DTO is model-bound.
    -> Final verify runs the full dotnet test suite (no namespace filters).

F. Documentation (P6)
    -> Stash.Registry/CLAUDE.md — "Controller Pattern" section gains a "Request
       validation" subsection naming the four moving parts (filter + attributes
       + IValidatableObject + custom ValidationAttribute) and pointing at the
       meta-test.
    -> docs/Registry — Package Registry.md — the "Input Validation" line in
       Security Requirements references the wire-visible 400 shape and the
       pagination caps.
```

### Cross-Cutting Concerns

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| The 400 response shape for every model-binding / validation failure | `InvalidModelStateResponseFactory` in `Stash.Registry/Startup.cs` (registered in `AddControllers().ConfigureApiBehaviorOptions(...)`) | **Construct.** The factory is globally registered against `ApiBehaviorOptions`; every `[ApiController]` action's `ModelStateInvalidFilter` short-circuits through it. There is no per-controller hook a future endpoint can fail to wire — the only way to bypass it is to remove `[ApiController]` (which would simultaneously break authentication, routing, and authz). Fail-closed: a malformed request that the factory cannot summarise still emits `ErrorResponse { Error = "InvalidRequest", Message = "Request body is invalid." }`, never a raw `ValidationProblemDetails`. |
| Every request endpoint is model-bound (`[FromBody]` / `[FromQuery]`) so the validation filter actually runs on its DTO | `RequestModelBindingMetaTests` (new in P1, `Stash.Tests/Registry/Validation/`) | **Detect — with the failure mode the doctrine reserves for this level.** ASP.NET cannot make a missing `[FromBody]` a compile error; an action that reads `Request.Body` directly is well-typed C# the type system has no opinion on. This is the canonical "the property the type system can't express" case the Construct/Detect/Instruct ladder reserves for a meta-test. The test follows `NoMagicAuthStringsMetaTests` and `CliNoMagicWireStringsMetaTests` exactly: load-order-deterministic `MetadataReference`s from `TRUSTED_PLATFORM_ASSEMBLIES` + `Stash.Registry.Contracts` + `System.ComponentModel.DataAnnotations`; a binding-floor probe (a known `Stash.Registry.Contracts` type must resolve to a non-error symbol); a file-count floor (at least the six controllers); a self-test fixture for both the positive case (manual `DeserializeAsync` is flagged) and the negative case (`[FromBody]` action is not flagged); a *pinned* `KnownExemptions` set so adding a silent new exemption forces a test edit. The set starts at twelve entries in P1 and is asserted empty in P5's `done_when`. The test goes RED with the pin in P1, not green-as-a-final-phase. |

## Acceptance Criteria

- **End-to-end behaviour preserved.** `dotnet test --filter FullyQualifiedName~RegistryAuthzMatrixTests` is green at every phase boundary, including the malformed-body 400 rows.
- **Validation now runs declaratively.** A POST to `/api/v1/auth/login` with `{"username":""}` (no password, empty username) returns 400 `ErrorResponse { Error = "InvalidRequest", Message = "Username is required.; Password is required." }`. The action body of `AuthController.Login` is never entered. (One test in `Stash.Tests/Registry/Validation/` per migrated action verifies the action body is not entered — easiest assertion is that the side-effect of the action is absent: no user row, no audit entry, no token issuance.)
- **Pre-existing inert `[MinLength(1)]` on `DeprecatePackageRequest.Message` now bites.** A POST with `{"message":""}` returns 400 (today: returns 200 with empty message stored).
- **Pagination is declared, not silently clamped.** A `GET /api/v1/search?pageSize=500` returns 400 `InvalidRequest` (today: silently clamps to 100 and returns 200).
- **The meta-test is empty at `/done` time.** `KnownExemptions.Count == 0` and the regression-guard self-test still passes.
- **No new 400 shape introduced.** `OpenApiSnapshotTests` is green without snapshot regeneration; `OpenApiCoverageMetaTests` is green (every 400 still `$ref`s `ErrorResponse`).
- **AOT-clean.** `dotnet publish Stash.Cli -c Release -p:PublishAot=true` produces no new trim warnings (`build.stash` AOT step, not `dotnet test`).
- **Full registry suite green.** `dotnet test` reports zero failures, zero new skips. Final-verify is the full suite, no `--filter` exclusions.

## Phases

The phase list lives in `plan.yaml`. Each phase carries a concrete `done_when` list there. The phase ordering implements the Implementation Path above:

- **P1** — Roslyn meta-test (`RequestModelBindingMetaTests`) lands RED with twelve pinned exemptions and its self-tests. *Construct-where-possible, Detect-where-not*: ships before any controller migrates.
- **P2** — `Stash.Registry.Contracts` DTOs gain `DataAnnotations` + two custom `ValidationAttribute` subclasses + `IValidatableObject` on `ClaimScopeRequest` + two new `[FromQuery]` DTOs. `InvalidModelStateResponseFactory` enriched to aggregate. No controller changes yet.
- **P3** — AuthController batch: Login, Register, CreateToken, RefreshToken migrate to `[FromBody]`. SearchController.Search migrates to `[FromQuery] SearchQuery`. Exemption list shrinks by 5.
- **P4** — OrganizationsController batch: CreateOrg, CreateTeam, AddTeamMember migrate. AddMember's `[FromBody] AddOrgMemberRequest? body` flips to non-nullable. Exemption list shrinks by 3 (the AddMember change is invisible to the scanner — already `[FromBody]` — but covered by a test asserting the empty-body case now 400s).
- **P5** — AdminController + ScopesController batch: CreateUser, AdminAssignRole, GetAuditLog (query DTO), ClaimScope migrate. Exemption list reaches empty; meta-test asserts `KnownExemptions.Count == 0`.
- **P6** — Documentation: `Stash.Registry/CLAUDE.md` Controller Pattern section gains a "Request validation" subsection; `docs/Registry — Package Registry.md` Input Validation line updated. No code changes.

## Open Questions

1. **Multi-field error aggregation in `InvalidModelStateResponseFactory`.** Today's factory reads `FirstOrDefault()` — a single error string. Once DataAnnotations is doing the work, multiple fields can fail at once.
   - **Recommended (and adopted in P2): aggregate.** Join every `ModelState` error message with `"; "` into `ErrorResponse.Message`. Stable `Error = "InvalidRequest"` code. Client gets the full failure picture in one round-trip.
   - **Discriminating check before flipping.** The factory change hits *every* model-binding 400, including the enum-deserialization 400s that `BoundedDomainBoundaryTests` and `OpenApiSnapshotTests` exercise. We grepped: tests assert `ErrorResponse.Error` (or status code), not `ErrorResponse.Message` text. The OpenAPI snapshot references `ErrorResponse` by `$ref`, not by example. Aggregation is safe with no snapshot regen.
   - **Counter-option:** keep first-error-only and accept that a malformed request needs multiple round-trips to fix. Rejected because it would also keep `ErrorResponse.Message` brittle to internal ASP.NET ordering changes (today the "first" error is non-deterministic across binders).

2. **`ScopesController.ClaimScope` body migration — defer or include.**
   - **Recommended (and adopted in P5): include.** Migrating `ClaimScope` to `[FromBody] ClaimScopeRequest? body` is *orthogonal* to the deferred body-resolver authz fold tracked in `.kanban/0-backlog/registry/Body-resolver authz filter.md`. The `[ImperativeAuthz]` marker stays, the inline PDP call stays, the 409 mapping for `ScopeReserved` / `ScopeNotOwned` stays. The migration is actually a *prerequisite* the deferred refactor wants (the body-resolver filter needs the request body as a model-bound parameter, not a raw stream).
   - **Discriminating check.** We grepped every `AuthzMatrixData` row that exercises POST `/api/v1/scopes`. None send a malformed or incomplete body expecting 403/409 — every row uses a well-formed body. So moving structural validation in front of the inline PDP is wire-equivalent for the matrix gate. Verified.
   - **Counter-option:** defer ClaimScope to a sibling feature with the body-resolver fold. Rejected because (a) the migration is mechanical (one [FromBody] parameter, one IValidatableObject method), (b) leaving it manual means the meta-test must permanently exempt it, defeating the empty-set `done_when`, and (c) deferral chains the validation feature to an unrelated refactor.

3. **Inline guard deletion policy.**
   - **Recommended (and adopted in P3–P5):** *delete what validation fully covers, keep what it cannot see.* See Semantics → "Guard deletion — the policy." The split is explicit: deserialization guards, null/empty/length/regex guards on `[Required]`-annotated fields go away; DB lookups, normalisation, reserved-name checks, cross-resource business rules, and the `capabilities`-shape-forbid stay. This is a rule, not a per-action judgement.
   - **Counter-option:** belt-and-suspenders — keep every inline guard. Rejected: the inline guards then *contradict* the DataAnnotations error strings (different wording, different error codes), and the controllers stay just as fat as before. The point of the migration is to delete code, not duplicate it.

4. **Pagination `pageSize` clamp → reject.**
   - **Recommended (and adopted in P3/P5): reject.** `[Range(1, 100)]` on `SearchQuery.PageSize` and `[Range(1, 200)]` on `AuditLogQuery.PageSize` produce 400 `InvalidRequest` for out-of-range values, replacing today's `Math.Min(parsed, 100)` clamp.
   - **Discriminating check.** No test in `Stash.Tests/Registry/` asserts the clamp behaviour (verified by grep); the OpenAPI snapshot only describes `pageSize` as an integer property. Behaviour change is invisible to the test suite but visible to live clients passing out-of-range values.
   - **Counter-option:** keep manual `[FromQuery]` parsing + clamp for Search and GetAuditLog; carry both as permanent meta-test exemptions. Rejected: the permanent exemption is exactly the silent-bypass shape the meta-test exists to prevent, and the clamp is undiscoverable from the API contract.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-03 | Build on the shipped `InvalidModelStateResponseFactory` and `ErrorResponse` envelope. No `ValidationProblemDetails`. | The user's earlier `ValidationProblemDetails` lean predated the `registry-openapi-hardening` merge. That feature baked `ErrorResponse` into every typed `Results<BadRequest<ErrorResponse>, ...>` return and the OpenAPI doc's `400 → $ref ErrorResponse` contract. Adopting `ValidationProblemDetails` now would produce two 400 shapes on the same endpoint, drift the OpenAPI doc, and reverse a deliberate sibling-feature decision. |
| 2026-06-03 | Bounded-domain fields (`role`, `visibility`, `principal_type`, `owner_type`, `token_scope`, `org_role`, user-`role`) are out of scope. | `BoundedDomains.cs` shipped P4 of `registry-openapi-hardening` as seven C# enums with `[JsonStringEnumConverter]` + per-member `[JsonStringEnumMemberName("…")]`. Illegal wire values already 400 at the deserializer — strictly more rigorous than DataAnnotations. Touching them would re-litigate a finished decision. |
| 2026-06-03 | Meta-test (`RequestModelBindingMetaTests`) lands in P1 with a *pinned* exemption list, not as a P-final phase. | Architect doctrine ("Designing Out Cross-Cutting Omission"): never schedule the Detect mechanism so late that earlier phases merge with the invariant unenforced. With the pin, any *new* controller endpoint added during P3–P5 that bypasses model binding fails immediately. The pin shrinks each phase; reaching empty is the feature's `done_when`. |
| 2026-06-03 | Enrich `InvalidModelStateResponseFactory` to aggregate all `ModelState` errors into `ErrorResponse.Message` with `"; "` separators. Code stays `"InvalidRequest"`. | Verified discriminating check: no test asserts `ErrorResponse.Message` text on the validation-factory path; the OpenAPI snapshot references `ErrorResponse` by `$ref`. Aggregation is wire-stable and gives clients a complete failure picture in one round-trip. |
| 2026-06-03 | Migrate `ScopesController.ClaimScope` to `[FromBody]` *without* folding its authz into `RegistryAuthorizeFilter`. The `[ImperativeAuthz]` marker stays. | Verified discriminating check: no `AuthzMatrixData` ClaimScope row sends a malformed body expecting 403/409, so moving structural validation in front of the inline PDP is wire-equivalent. The fold is a separate refactor (`.kanban/0-backlog/registry/Body-resolver authz filter.md`) that this migration *enables*, not the other way around. |
| 2026-06-03 | Migrate `[FromQuery]` pagination on both `SearchController.Search` and `AdminController.GetAuditLog`. Accept the behaviour change from `Math.Min(parsed, 100)` clamp to 400 reject on out-of-range `pageSize`. | The current silent-clamp behaviour is undiscoverable from the API contract; verified discriminating check shows no test asserts the clamp. The alternative (permanent meta-test exemptions for two endpoints) is exactly the silent-bypass shape the test exists to prevent. |
| 2026-06-03 | Guard-deletion policy: *delete what validation fully covers, keep what validation cannot see.* | Spelled out in Semantics → "Guard deletion — the policy." Makes every migration diff intentional. |
| 2026-06-03 | AOT/trim suppression mirrors the existing `[MinLength]` precedent on `DeprecatePackageRequest.Message`; AOT verify is a `dotnet publish` step in `build.stash`, not a per-phase `verify:` and not in `dotnet test`. | Per project memory: the AOT enum round-trip was deliberately relocated out of the unit suite. A per-phase publish gate would bloat the inner loop; one `build.stash` confirmation at feature-complete time is sufficient. |
| 2026-06-03 | `OrganizationsController.AddMember`'s `[FromBody] AddOrgMemberRequest? body` parameter flips to non-nullable in P4. | Today's nullable shape lets an empty request body skip DTO validation entirely (no model error, action body sees `body == null`). Non-nullable `[FromBody]` triggers the validation filter on a missing body. This is invisible to the meta-test scanner (the action is already `[FromBody]`) but is covered by an action-level test asserting `POST /api/v1/orgs/{org}/members` with empty body returns 400 `InvalidRequest`. |
