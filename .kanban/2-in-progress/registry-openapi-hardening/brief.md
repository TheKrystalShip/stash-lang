# RFC: Registry OpenAPI Hardening

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-06-03
> **Slug:** registry-openapi-hardening
> **Milestone:** —

## Summary

The Stash package registry already wires .NET 10's built-in OpenAPI generator (`Microsoft.AspNetCore.OpenApi` 10.0.5, `OpenApiGenerateDocuments=true`, `services.AddOpenApi()` + `app.MapOpenApi()`), but the document it publishes today is a bare skeleton: every operation is `200: OK` with `schema=None`, no `operationId`s, no `servers`, no document metadata, bounded domains as bare `{"type":"string"}`, and the endpoint is gated to `IsDevelopment()` so an external consumer cannot fetch it at all. This feature hardens that document into a high-fidelity public contract suitable for third-party client generation, without writing or shipping any in-repo client (the monorepo references the shared `Stash.Registry.Contracts` assembly directly).

It also folds in the "bounded domains as real C# `enum`s" end-to-end conversion the user resolved on 2026-06-03 — reversing the just-shipped "DTO properties stay `string`" decision in favour of the project doctrine's preferred 100% solution: illegal values fail to compile, and the generated OpenAPI document surfaces `enum` schemas automatically.

## Motivation

The shared-contracts half of [`Registry Consumer Client - Shared Contracts and OpenAPI Strategy.md`](../../0-backlog/registry/Registry%20Consumer%20Client%20-%20Shared%20Contracts%20and%20OpenAPI%20Strategy.md) shipped on 2026-06-03 (commit `953d799f`); the OpenAPI half is unstarted. The originally-cited owner of the OpenAPI work — `registry-web-api-readiness` — was never specced. There is no upstream blocker; this **is** the OpenAPI-quality work.

Pain today:

- `MapOpenApi()` is `IsDevelopment()`-gated (`Startup.cs:264-267`) — external consumers literally cannot fetch the contract in prod.
- Response bodies are unmapped. The generated `obj/Stash.Registry.json` (~23 KB, 1136 lines) carries five component schemas (the `[FromBody]` request DTOs) and zero response schemas, despite 39 controller actions returning typed DTOs from `Stash.Registry.Contracts`.
- No `operationId` on any operation, so a generated client gets ugly auto-derived method names that churn whenever the framework heuristic changes.
- Bounded domains (`role`, `principal_type`, `visibility`, `owner_type`, `token_scope`, `org_role`, user `role`) surface as bare `string` — a generated client cannot reject illegal values at the call site.
- No drift guard, no coverage guard, no AOT round-trip guard for enum wire strings.

Cost of doing nothing: the published contract is unfit for client generation, the shared-contracts feature's residual "no AOT round-trip test" gap persists, and the project's bounded-domain doctrine (CLAUDE.md: *"types are the 100%; centralized string constants are the cheap 80%"*) is unsatisfied.

## Goals

- Serve `GET /openapi/v1.json` publicly in every environment (drop the `IsDevelopment()` gate and bypass the JTI revocation gate for the OpenAPI path so a caller with a stale token can still fetch the contract).
- Every controller action returns a typed `Results<...>` / `TypedResults` union over wire DTOs (compiler-derived response schemas — no `[ProducesResponseType]` attribute drift class).
- Every operation carries a stable, predictable `operationId` (convention: `{Controller}_{Action}` — applied by a document/operation transformer so omission is structurally impossible).
- Document metadata populated: title, description, version, license, contact, `servers`.
- Bounded-domain `const string` sets in `Stash.Registry.Contracts/BoundedDomains.cs` (7 sets: `PackageRoles`, `TokenScopes`, `Visibilities`, `PrincipalTypes`, `ScopeOwnerTypes`, `OrgRoles`, `UserRoles`) become real C# `enum`s end-to-end — DTOs + registry serialization + EF value converters + AOT CLI source-gen JsonContext — with byte-identical lowercase wire strings preserved.
- A snapshot test of `openapi.json` (drift visibility) **and** a coverage meta-test (read against the generated document) asserting every operation has an `operationId` and every declared response code resolves to a real schema (`$ref` to a component, not empty `200: OK`).
- An AOT CLI runtime round-trip test asserting every bounded-domain enum value serializes byte-identical under the source-gen `CliJsonContext` and the published-AOT binary path. This is the residual gap the shared-contracts feature shipped without; do not repeat it.
- Documentation in `docs/Registry — Package Registry.md` describing the published contract and the public endpoint.

## Non-Goals

- Generating any in-repo client from the OpenAPI document. The monorepo references `Stash.Registry.Contracts` directly; the published contract serves third parties only. (Restated from the backlog doc's Non-Goals.)
- Data migration for the enum conversion. Registry is pre-release (see [[project-registry-pre-release]] memory); no production instance, no existing data, design for the clean forward case.
- Refactoring server-internal bounded sets (`RegistryClaims`, `ReservedScopes`, `TokenCeiling`, `AuthzDenyReason`, `ScopeStates`). They stay as-is — the enum conversion touches **only** the seven wire-visible sets in `BoundedDomains.cs`.
- Unifying the new `TokenScopes` enum with the existing internal `Authorization.TokenCeiling` enum. `TokenCeilingConverter` continues to mediate between them; the two enums coexist (one wire-visible, one server-internal). Unification is a separable cleanup.
- Refactoring `RegistryClient.cs` transport, streaming, integrity, token, or auth logic.

## Design

### Verified foundation (one-action spike, 2026-06-03)

Before committing to "controllers adopt typed `Results<>`," a spike converted `ScopesController.GetScope` from `Task<IActionResult>` to `Task<Results<Ok<ScopeDetailResponse>, NotFound<ErrorResponse>>>` and rebuilt. The regenerated `obj/Stash.Registry.json` for `GET /api/v1/scopes/{scope}` now contains:

```json
"responses": {
  "200": { "description": "OK",
    "content": { "application/json": { "schema": { "$ref": "#/components/schemas/ScopeDetailResponse" } } } },
  "404": { "description": "Not Found",
    "content": { "application/json": { "schema": { "$ref": "#/components/schemas/ErrorResponse" } } } }
}
```

and `ScopeDetailResponse` + `ErrorResponse` are now in `components.schemas`. .NET 10's MVC ApiExplorer **does** read `IEndpointMetadataProvider` off typed `Results<>` union members for `[ApiController]` controllers — the foundation works. (Spike reverted; no source change shipped.)

### Surface

**Public endpoint.** `GET /openapi/v1.json` returns the published document, unauthenticated, in every environment. The path matches the existing `MapOpenApi()` default. The JTI revocation middleware (`Startup.cs:276-311`) gains a path-prefix bypass for `/openapi/` so a caller presenting a stale token still receives the contract (otherwise external tooling that accidentally sets a `Bearer` header breaks).

**Document metadata.**

```yaml
info:
  title: "Stash Package Registry"
  description: "REST API for the Stash language's package registry — packages, scopes, organizations, search, auth."
  version: "1.0.0"
  license:    { name: "MIT" }
  contact:    { name: "Stash project", url: "https://github.com/cmoraru/stash-lang" }
servers:
  - url: "{registryBase}", variables: { registryBase: { default: "https://registry.example.com" } }
```

**Operation IDs.** `{ControllerName}_{ActionName}`, e.g. `Scopes_GetScope`, `Packages_PublishPackage`. Applied by an `IOpenApiOperationTransformer` (or document transformer) that walks every operation and sets `OperationId` from the `ApiDescription`'s controller/action descriptor when it is null. This is the **Construct** prevention for "every operation has an operationId" — a new endpoint cannot ship without one because the transformer fills it in unconditionally.

**Typed Results returns.** Every controller action's return type changes from `Task<IActionResult>` (or `IActionResult`) to `Task<Results<Ok<TSuccess>, ...errorVariants>>` (or `Results<...>` for sync). The error variant set is the union of HTTP status codes the action can actually emit, each typed with the response DTO it carries (`NotFound<ErrorResponse>`, `Conflict<ErrorResponse>`, `BadRequest<ErrorResponse>`, etc.). `TypedResults.X(...)` replaces `Ok(...)`/`NotFound(...)`/etc. inside the body.

Permanent exemption (pinned): `PackagesController.DownloadVersion` returns a binary tarball stream (`application/octet-stream` + `X-Integrity` header), not a JSON DTO. It keeps `IActionResult` (or a file-stream-typed result) and lands on the coverage meta-test's permanent exemption list, pinned the same way `AuthzDispatchCoverageMetaTests` pins `{ScopesController.ClaimScope}`.

**Bounded domains become enums.** Seven `public static class X { public const string Y = "..."; }` declarations in `Stash.Registry.Contracts/BoundedDomains.cs` become `public enum X { Y, ... }` with a `[JsonConverter(typeof(JsonStringEnumConverter<X>))]` and `[JsonStringEnumMemberName("y")]` on each member (or equivalent — verified empirically during P4) so the wire string is byte-identical (`"owner"`, `"publish"`, `"public"`, `"user"`, `"system"`, `"member"`). DTO property types change from `string` to the new enum types. EF properties pick up `.HasConversion<string>()` value converters and `.HasDefaultValue(MyEnum.Public)`. The CLI's source-gen `CliJsonContext` registers each enum type so AOT serialization round-trips. The `Rank`/`RankOrder`/`IsValid`/`IsReserved` helpers stay (translated to enum-typed signatures).

### Semantics

**Public OpenAPI endpoint, JTI bypass.** No auth required; no JTI check; no rate-limit bucket beyond the default. The bypass is **path-based** in the JTI middleware (an `if (context.Request.Path.StartsWithSegments("/openapi"))` early return placed before the `TokenRevoked` check). No `[PublicEndpoint]` attribute is involved because `MapOpenApi()` registers a minimal-API endpoint, not a controller action — `AuthzCoverageMetaTests` therefore does not require classification (it scans controllers only).

**Typed Results behaviour preservation.** Each typed `Results<>` variant maps 1:1 to the HTTP status + body the old `IActionResult` returned. `RegistryAuthzMatrixTests` (the primary behaviour-preservation gate; every (action × principal) row asserts HTTP status and `ErrorResponse` body) is the canonical regression net for this refactor — it must stay green for every phase.

**Enum wire strings.** Round-trip is byte-identical: `JsonSerializer.Serialize(PackageRoles.Owner) == "\"owner\""` under both the registry's reflection-based serializer and the CLI's source-gen `CliJsonContext` (and the published Native-AOT binary). Unknown wire values deserialize to a parse failure (HTTP 400 with `ErrorResponse`) at the controller boundary — replacing today's bare string that would propagate an illegal value into the database.

**Coverage meta-test.** Runs after every build. Uses `WebApplicationFactory<Stash.Registry.Startup>` to start the registry in-process, fetches `/openapi/v1.json`, parses it, and asserts: every operation has a non-null `operationId`; every declared response code under each operation has a `content` map whose schema is a `$ref` to `components.schemas` (not empty, not bare `string`); every enum domain (the seven) appears as an OpenAPI `enum` schema. Ships with a self-test fixture (a controller action that violates the rule) proving the scan has teeth, a binding-floor that fails loudly if the doc has zero operations (preventing vacuous pass), and an exemption list pinned to two categories — (a) "not yet migrated" (initially every action, shrinks to zero), (b) **permanent** (`Packages_DownloadVersion`).

**AOT round-trip test.** Runs inside `Stash.Tests/Cli/` against the CLI's source-gen `CliJsonContext`. For each enum domain: serialize each enum value, assert the JSON literal matches the documented wire string byte-for-byte; deserialize each wire string, assert the enum value round-trips. A second variant runs the published Native-AOT binary as a subprocess (`dotnet publish -c Release` then exec) and asserts the same round-trip through a `--self-test enums` CLI flag — closing the residual gap the shared-contracts feature shipped without.

### Implementation Path

OpenAPI plumbing in Startup → operation/document transformers (operationId, metadata, servers) → JTI bypass for `/openapi/` → coverage meta-test lands RED with full exemption list → controllers refactor to typed `Results<>` in two batches → coverage meta-test exemption list shrinks to permanent-only → bounded-domain enum conversion in contracts + registry + EF + boundary parsers → CLI source-gen registration + AOT round-trip test → snapshot baseline of `openapi.json` + drift gate → docs.

### Cross-Cutting Concerns

> All concerns shared across phases. Prefer Construct over Detect; never rely on Instruct alone.

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| Every operation has an `operationId` | `OpenApiOperationIdTransformer` (registered via `services.AddOpenApi(o => o.AddOperationTransformer<...>())`) | **Construct** — the transformer fills in `{Controller}_{Action}` unconditionally for any operation whose `OperationId` is null. A new endpoint cannot ship without an `operationId`; the coverage meta-test (Detect) reads the generated doc and confirms the property held — fast feedback if the transformer regresses. |
| Every operation has non-opaque response schemas | The typed `Results<...>` / `TypedResults` return shape on every controller action | **Construct (preferred) + Detect.** Construct: a `Results<>` return type forces every variant to carry a body type that .NET 10's ApiExplorer reads (verified by spike). Detect: a meta-test reads the **generated `openapi.json`** (not source-level return types — the source-level proxy is exactly the failure shape the AuthController cautionary tale warned about) and asserts every operation's declared responses have a schema `$ref`. Two-category exemption list: "not yet migrated" (shrinks to zero across phases) + **permanent** (`Packages_DownloadVersion` — binary tarball stream, pinned). |
| Every wire-visible bounded value is the single canonical set | The seven `enum`s in `Stash.Registry.Contracts/BoundedDomains.cs` (`PackageRoles`, `TokenScopes`, `Visibilities`, `PrincipalTypes`, `ScopeOwnerTypes`, `OrgRoles`, `UserRoles`) | **Construct** — illegal values will not compile. `NoMagicAuthStringsMetaTests` strengthens automatically: a bare string at an auth sink cannot satisfy an enum-typed parameter. The boundary parser (`RegistryAuthzPrincipalFactory` / `TokenCeilingConverter`) becomes parse-or-reject. |
| Every enum value round-trips byte-identical through CLI source-gen + Native AOT | The AOT round-trip test (`Stash.Tests/Cli/...`) + the published-binary `--self-test enums` subprocess test | **Detect (load-bearing).** The type system cannot express "this enum value's wire string matches its documented spelling under source-gen + AOT" — the runtime test is the only sound check. Ships with a fail-path fixture (a deliberately-misnamed `[JsonStringEnumMemberName]`) proving the test would catch the bug it's named for, and a binding floor (asserts every enum domain is registered in `CliJsonContext`). |
| OpenAPI document doesn't drift unannounced | The snapshot test of `openapi.json` (re-baseline with `STASH_SNAPSHOT_REGEN=1`, mirroring `CompletionSurfaceSnapshotTests`) | **Detect.** Snapshot lands LATE (P5) after the doc shape stabilizes across the typed-Results + enum work, re-baselined once at that point. Pairs with the coverage meta-test (which prevents the *new-endpoint-ships-schema-less* failure mode the snapshot cannot catch). |

## Acceptance Criteria

- `GET /openapi/v1.json` returns 200 in `Production` and `Development` environments (`Startup.cs:264-267` gate removed; integration test asserts both).
- A request to `/openapi/v1.json` with a revoked JWT in the `Authorization` header still returns 200 (JTI bypass for the OpenAPI path; integration test asserts this).
- Every controller action — except the pinned permanent exemption `Packages_DownloadVersion` — declares a typed `Results<...>` return; the coverage meta-test reads the in-process-fetched openapi.json and confirms every operation has (a) a non-null `operationId`, (b) every declared response code resolves to a `$ref` schema. The exemption list is empty except for the pin.
- The minimal-API health endpoint `GET /` (today: `app.MapGet("/", () => Results.Json(new HealthCheckResponse{...}))` at `Startup.cs:315`) is converted to `TypedResults.Ok<HealthCheckResponse>(...)` + `.WithName("Health_Check")` so it is a first-class documented operation (operationId `Health_Check`, schema `$ref` to `HealthCheckResponse`) — not a minimal-API operation that escapes the coverage machinery.
- The seven bounded-domain `const string` sets are gone from `BoundedDomains.cs`; the file now declares seven `enum` types. `dotnet build` is green for all consuming projects (registry, CLI, contracts, tests).
- A request to `POST /api/v1/admin/users` with `{"role": "owner"}` (an illegal `UserRoles` wire string) returns 400 with `ErrorResponse` — the deserializer rejects at the boundary instead of writing an illegal value to the database.
- The AOT round-trip test runs the published Native-AOT CLI binary via `dotnet publish -c Release` and asserts every enum value's wire string is byte-identical to the documented spelling. (Closes the shared-contracts residual gap.)
- `RegistryAuthzMatrixTests` (every (action × principal) row asserts HTTP status + `ErrorResponse` body) is green throughout — behaviour preserved.
- `NoMagicAuthStringsMetaTests` is green; `AuthzCoverageMetaTests` is green; `AuthzDispatchCoverageMetaTests` is green.
- `docs/Registry — Package Registry.md` documents the public OpenAPI endpoint, its stability guarantees, and the bounded-domain enum schemas.
- Full `dotnet test` is green; `final_verify` is green.

## Phases

The phase list lives in `plan.yaml`. Summary (six phases):

1. **P1 — Foundation: transformers, metadata, public endpoint, coverage meta-test (RED).** OpenAPI document/operation transformers (operationId convention + metadata + servers), drop `IsDevelopment()` gate, path-prefix JTI bypass for `/openapi/`. Ship the coverage meta-test against the in-process-fetched generated doc, with every controller action exempted and `Packages_DownloadVersion` flagged permanent. RED-with-exemption-list pattern.
2. **P2 — Typed Results refactor: Packages + Auth controllers (~21 actions).** Convert every `Task<IActionResult>` to `Task<Results<...>>`; replace `Ok(...)` / `NotFound(...)` / etc. with `TypedResults.X(...)`. Shrink the coverage meta-test exemption list correspondingly.
3. **P3 — Typed Results refactor: Orgs + Scopes + Search + Admin controllers (~18 actions).** Same shape as P2; coverage exemption list reaches its permanent floor (`Packages_DownloadVersion` only).
4. **P4 — Bounded-domain enum conversion: contracts + registry + EF.** Seven `const string` sets → `enum` types in `BoundedDomains.cs`. Add `JsonStringEnumConverter<T>` + member-name attributes to preserve byte-identical lowercase wire strings. Update DTO property types, registry usages, EF `.HasConversion<string>()` + `.HasDefaultValue(MyEnum.X)`, boundary parsers (`RegistryAuthzPrincipalFactory`, `TokenCeilingConverter`). `RegistryAuthzMatrixTests` is the regression net.
5. **P5 — CLI AOT verification + snapshot baseline.** Register every enum in `CliJsonContext`; replace CLI-side string equality with enum comparison; ship the AOT round-trip test (in-process + published-binary subprocess). Re-baseline the `openapi.json` snapshot once at this stabilization point.
6. **P6 — Docs.** Update `docs/Registry — Package Registry.md` (public OpenAPI endpoint section, enum schemas table). Confirm all tooling-compat surfaces (LSP/playground/etc. are unaffected; this is registry/contracts only, not a language change, but the language-changes checklist's discipline still applies to the docs check).

## Open Questions

- **`/openapi/` path-prefix bypass — exact placement.** The JTI middleware in `Startup.cs:276` is a single inline `app.Use(async (context, next) => {...})`. The cleanest insertion is a guard at the top of that lambda (`if (context.Request.Path.StartsWithSegments("/openapi")) { await next(); return; }`). Confirm during P1 that this placement leaves all other auth/audit behaviour for non-OpenAPI paths unchanged.
- **Document metadata `servers` default.** The brief proposes `https://registry.example.com` with a `{registryBase}` variable; the user can override at codegen time. Confirm during P1 whether a more specific default (e.g. omitted, forcing the consumer to supply it) is preferred.
- **OperationId convention vs explicit overrides.** Convention is `{Controller}_{Action}`. Should we allow per-action override via a `[ProducesOperationId("...")]`-style attribute, or hold the line on convention? Default: convention only; revisit if a downstream consumer needs stability across rename.
- **Enum wire-string mechanism.** .NET 10 supports both generic `JsonStringEnumConverter<T>` (registered globally) and per-member `[JsonStringEnumMemberName("...")]` attributes. The lowercase-with-PascalCase-members combination needs explicit member-name attributes (camelCase naming policy would not handle multi-word values, though all seven domains are single-word today). Confirm empirically in P4 which mechanism works under BOTH the registry's reflection serializer AND the CLI's source-gen context under Native AOT; if they diverge, document the workaround.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-03 | Convert bounded-domain `const string` sets to real C# `enum`s end-to-end | Project doctrine (CLAUDE.md): *"types are the 100%; centralized string constants are the cheap 80%."* Illegal values fail to compile, the OpenAPI document surfaces `enum` schemas automatically, the AOT CLI's `CliJsonContext` registers the enum types directly. Reverses the just-shipped "DTO properties stay `string`" decision; user-resolved (Make-It-Right doctrine). |
| 2026-06-03 | Serve `GET /openapi/v1.json` publicly in prod | An external client generator cannot fetch a `Development`-gated document. The contract is *the* public surface of the registry. |
| 2026-06-03 | Controllers adopt typed `Results<...>` / `TypedResults` (not `IActionResult` + `[ProducesResponseType]`) | Response schemas become compiler-derived (zero attribute-vs-reality drift). Verified empirically by a one-action spike on `ScopesController.GetScope` in .NET 10 — schemas + component `$ref`s populated correctly in `obj/Stash.Registry.json`. |
| 2026-06-03 | Coverage meta-test asserts against the **generated `openapi.json`**, not source-level return types | Source-level "every action declares a result union" is the proxy the AuthController cautionary tale warned about; the load-bearing property is "every operation in the published doc has schemas," and only a doc-level check captures it (a misfiring transformer or a `Results<>` variant the explorer fails to read passes the source check and fails the user). |
| 2026-06-03 | Land the coverage meta-test RED in P1 with full exemption list; shrink to permanent-only across migration phases | Architect doctrine: a Detect meta-test scheduled as the final phase merges all prior phases with the invariant unenforced. Pin the permanent exemption (`Packages_DownloadVersion`) so a silent new exemption forces a test edit. |
| 2026-06-03 | Permanent exemption: `PackagesController.DownloadVersion` | Binary tarball stream (`application/octet-stream` + `X-Integrity` header), not a JSON DTO; typed `Results<>` over a body schema does not apply. Pin like `AuthzDispatchCoverageMetaTests` pins `{ScopesController.ClaimScope}`. |
| 2026-06-03 | Path-based JTI bypass for `/openapi/` (not a `[PublicEndpoint]` attribute) | `MapOpenApi()` registers a minimal-API endpoint, not a controller action. The cleanest gate is a path-prefix early return at the top of the existing JTI middleware lambda; controllers are unaffected. |
| 2026-06-03 | Do not unify the new wire `TokenScopes` enum with the existing internal `Authorization.TokenCeiling` enum | One is wire-visible, one is server-internal; `TokenCeilingConverter` already mediates between them and becomes parse-or-reject. Unification is a separable cleanup, not required for this feature's goals. |
| 2026-06-03 | AOT round-trip test runs the published Native-AOT CLI binary as a subprocess (`dotnet publish -c Release` + exec `--self-test enums`) | Closes the shared-contracts feature's residual gap (no AOT round-trip test shipped there). The in-process source-gen check is necessary but insufficient; only the published binary exercises the full trim/AOT path. |
| 2026-06-03 | Snapshot test of `openapi.json` lands in P5 (after enum work), re-baselined once | Earlier baselines would churn through P2/P3/P4 and add noise. The snapshot is a drift guard against future change; the coverage test guards omission across this feature's phases. |
| 2026-06-03 | Convert the minimal-API health endpoint `GET /` to typed `TypedResults.Ok<HealthCheckResponse>(...) + .WithName("Health_Check")` rather than pin it as a permanent coverage exemption | The endpoint appears in the generated doc today (`"/": {"get": {... "200": {"description":"OK"}}}`) but is neither a controller action (operationId transformer ignores it) nor schema-typed — it would escape both halves of the coverage machinery as an unowned operation, the exact omission-failure shape the AuthController cautionary tale warned about. Converting is cleaner than pinning: the endpoint returns a real DTO and is first-class to both the transformer (named operationId) and the meta-test (typed schema). |
| 2026-06-03 | Coverage meta-test's enum-schema assertion is added in P4, not P1 | The seven enums do not exist until P4; asserting "every enum domain appears as an OpenAPI enum schema" in P1 would be logically unachievable. P1 asserts (operationId present) AND (response $ref schemas); P4 extends the test with the third assertion. |
| 2026-06-03 | P2 typed-Results refactor is enforced by source-level Construct (the `Results<>` return type is structurally distinct from `IActionResult`) + the doc-level Detect coverage test, not by a separate Roslyn meta-test | A separate Roslyn meta-test banning `IActionResult` returns is *possible* but the typed `Results<>` shape already makes a forgotten conversion structurally visible in the diff (the file's return-type signature changes), and the doc-level coverage meta-test catches any case where the framework's ApiExplorer fails to read a `Results<>` variant — which is the actual user-facing failure. Adding a third source-level check is redundant. (Decision noted to forestall a reviewer suggesting "add a Roslyn meta-test" without seeing the rationale.) |
