# Backlog: Registry Declarative Request Validation

**Status:** Parked — blocked on `registry-openapi-hardening` merging to `main`
**Created:** 2026-06-03
**Discovery context:** Investigation session (pre-spec). User asked to move request validation out of the controllers into ASP.NET Core's declarative, pre-controller validation pipeline. Investigation surfaced a hard dependency on the in-flight `registry-openapi-hardening` feature, so this was parked rather than specced. Re-spec it (`/spec registry-declarative-request-validation`) once the blocker clears.

## Goal

Stop the registry controllers doing manual, inline model validation. Let ASP.NET Core validate the request **before** the action body runs, declaratively, so controllers carry only business logic.

## Mechanism (settled during investigation — do not re-discover)

- It is **not middleware**. Middleware runs before model binding, so there is no typed object to validate yet. The correct layer is the **`[ApiController]` automatic model-validation action filter** (`ModelStateInvalidFilter`), which runs *after* binding and *before* the action, short-circuiting with a 400 when `ModelState` is invalid.
- All six controllers already carry `[ApiController]`, so the filter is wired up but **dormant** — nothing feeds it.
- Automatic validation only fires on **model-bound** parameters (`[FromBody]`/`[FromQuery]`/`[FromRoute]`). Endpoints that read `Request.Body` manually bypass it entirely. Proof the dormant path does nothing today: the inert `[MinLength(1)]` on `DeprecatePackageRequest.Message`.
- Stock `DataAnnotations` (`[Required]`, `[StringLength]`, `[Range]`, `[RegularExpression]`) cover nullness/length/range/format. They do **not** cover closed sets (handled by enums — see below) or cross-field/grammar rules (handled by `IValidatableObject` or a custom `ValidationAttribute` wrapping existing helpers like `PackageManifest.IsValidScopeName`).
- Sources: [Model validation in ASP.NET Core MVC](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation?view=aspnetcore-10.0), [Create web APIs](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-10.0).

## Why this is blocked

`registry-openapi-hardening` (worktree `../stash-registry-openapi-hardening`, branch `feature/registry-openapi-hardening`) owns the same hot files and is 3/6 phases done:

- **P2/P3 (done):** rewrote all six controllers to typed `Results<...>` returns, and **baked `ErrorResponse` into every error variant's type signature** (e.g. `Results<Ok<LoginResponse>, BadRequest<ErrorResponse>, JsonUnauthorized<ErrorResponse>>`), including custom `JsonUnauthorized<T>`/`JsonForbidden<T>` result types built specifically to preserve the `ErrorResponse` wire body. This is now also encoded in the published OpenAPI document (coverage meta-test asserts `400 → ErrorResponse` schema).
- **P4 (pending):** converts the 7 `BoundedDomains.cs` const sets to real C# enums end-to-end — illegal `role`/`visibility`/`principal_type`/`owner_type`/`token_scope`/`org_role`/user-`role` values become a **400 at the deserializer boundary**. This is the bounded-domain half of validation; this backlog feature must **not** touch `BoundedDomains.cs`.

No disjoint slice exists to parallelize (both features touch all six controllers + `Startup.cs` + the `Contracts` DTOs), and there is a genuine **design conflict** on the 400 error shape (see below). Per the project's parallel-work doctrine, serialize: spec this only after `registry-openapi-hardening` is `/done` and merged to `main`.

## Adjusted scope (once unblocked — assumes openapi-hardening has merged)

Post-merge the feature is **smaller** because it builds on enums + typed `Results<>` instead of fighting them:

1. **`[FromBody]` migration** — still required; 11 manual `DeserializeAsync` calls survive on the openapi branch (P2/P3 only changed return types, not request binding): `Auth` ×4, `Organizations` ×4, `Admin` ×2, `Scopes` ×1.
2. **`DataAnnotations` on non-bounded fields only** — password min-length, the `AdminController` username regex, string lengths, `Search` pagination bounds. (Bounded domains are already enums — leave them.)
3. **Cross-field / grammar** — `IValidatableObject` or custom `ValidationAttribute` wrapping existing helpers: scope-name grammar, semver, "owner required when `owner_type=org`", password policy.
4. **Auto-validation 400 shape** — wire `ConfigureApiBehaviorOptions`. See the re-opened decision below.
5. **Enforcement meta-test** (Construct > Detect > Instruct) — fail CI when a new endpoint regresses to manual `Request.Body` deserialization or leaves a request DTO un-bound/un-validated. Follow the `NoMagicAuthStringsMetaTests` Roslyn pattern: load-order-deterministic references from `TRUSTED_PLATFORM_ASSEMBLIES` (+ assemblies under test) — **never** `AppDomain.CurrentDomain.GetAssemblies()` — plus a binding-floor assertion so a vacuous "0 findings because nothing bound" pass fails loudly.
6. **AOT/trim verify** (not a blocker) — `DataAnnotations` attributes added to `Stash.Registry.Contracts` are pure metadata for the AOT CLI (it calls no `Validator.*`; precedent: the existing `[MinLength]` ships with `[UnconditionalSuppressMessage]`). Confirm the CLI trim/AOT config tolerates them.

## RE-OPENED DECISION: 400 error shape

Earlier in the investigation the user chose **`ValidationProblemDetails` (RFC 9457)** — made *before* the `registry-openapi-hardening` conflict was known. That choice now fights a committed design: the sibling feature standardized **all** errors (including 400s) on `ErrorResponse`, baked into both the action type signatures and the published OpenAPI contract. The automatic validation filter short-circuits *before* the action with `ValidationProblemDetails`, so adopting it would (a) produce two different 400 shapes on the same endpoint, (b) drift the just-hardened OpenAPI doc, and (c) pass the coverage meta-test green while runtime differs (the meta-test reads the *declared* doc, not the filter output).

**Two coherent resolutions — decide at spec time:**
- **(A) `ErrorResponse` via `InvalidModelStateResponseFactory`** — make automatic validation emit the same `ErrorResponse` envelope the typed `Results<>` signatures and the merged OpenAPI doc already promise. One error dialect; no doc drift; smallest blast radius. *Leaning recommendation given the merged state.* Caveat: `ErrorResponse` has a single `error` string, so multi-field validation results must be flattened.
- **(B) `ValidationProblemDetails`** — only viable as a *coordinated* change that also revises the (by-then-merged) typed `BadRequest<ErrorResponse>` variants, the OpenAPI coverage meta-test, the snapshot, and the CLI's error parsing. Much larger; reverses a deliberate sibling-feature decision.

## Revisit trigger

When `registry-openapi-hardening` is promoted to `.kanban/4-done/` and merged to `main`. At that point: `/spec registry-declarative-request-validation`, feeding it this stub.
