# Registry OpenAPI Hardening — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header is parseable: `## Fxx — [SEVERITY] short title`.
> Severity scheme (per task prompt): CRITICAL / HIGH / MEDIUM / LOW.

**Scope reviewed:** commits `f194030e..2579be3c` on branch `feature/registry-openapi-hardening`
**Brief:** ../brief.md
**Generated:** 2026-06-03

Baseline at review entry: full `dotnet test` is **green** (failed=0 passed=13103 skipped=6 — the 6 skips are pre-existing source-level quarantines, not introduced by this feature). Brief parity is high: every Acceptance Criterion has a corresponding implementation + test. The six findings below are residual quality concerns, none CRITICAL.

---

## F01 — [HIGH] `[FromBody]` endpoints return `ValidationProblemDetails`, not `ErrorResponse`, on malformed bodies — contract lies about 400 shape

**Status:** fixed
**Fixed in:** 9055f12b
**Files:** `Stash.Registry/Controllers/OrganizationsController.cs:159`, `Stash.Registry/Controllers/PackagesController.cs:283,341,428,456,490`, `Stash.Registry/Controllers/AdminController.cs:190`, `Stash.Registry/Startup.cs:79`, `docs/Registry — Package Registry.md:1534`
**Phase:** P4 (regression cross-cuts P4 docs + P2/P3 controller refactor)
**Commit:** 4ec47470 (P4 enum conversion that introduced `[FromBody]` on `AddMember`); `[FromBody]` also present on six other endpoints from P2/P3.

### Observation

The published OpenAPI document — and §22.6 of `docs/Registry — Package Registry.md` — claim every 400 on every operation returns `ErrorResponse { Error, Message }`. Every `BadRequest<ErrorResponse>` variant in the controllers' `Results<...>` unions advertises the same schema (see e.g. `Stash.Tests/Registry/OpenApi/Snapshots/openapi-v1.json:78-87`, repeated ~10 times).

But seven endpoints take their request body via `[FromBody]` on `[ApiController]` (see `grep "FromBody" Stash.Registry/Controllers/*.cs`): `OrganizationsController.AddMember`, `AdminController.AdminRevokeRole`, `PackagesController.DeprecatePackage`, `PackagesController.DeprecateVersion`, `PackagesController.AssignRole`, `PackagesController.RevokeRole`, `PackagesController.SetVisibility`. `Startup.cs:79` calls `services.AddControllers()` with no `ConfigureApiBehaviorOptions(o => o.SuppressModelStateInvalidFilter = true)` or `InvalidModelStateResponseFactory` override (verified by `grep -r "SuppressModelStateInvalidFilter\|ConfigureApiBehaviorOptions\|InvalidModelStateResponseFactory" Stash.Registry/` → empty).

For an `[ApiController]` endpoint using `[FromBody]`, malformed JSON OR an illegal enum wire value (e.g. `{"role":"NOT_A_REAL_ROLE"}`) triggers the framework's default `ValidationProblemDetails` shape (RFC 7807): `{"type":"...","title":"...","status":400,"errors":{"$.role":["..."]}}` — NOT `ErrorResponse { Error, Message }`. The body shape an external client will actually receive on a deserialization-failure 400 does not match the schema the contract declares.

Note: business-validation 400s (those produced inside the action body via `TypedResults.BadRequest(new ErrorResponse {...})`) do return the documented shape. The mismatch is exclusively on the *deserialization failure* path.

§22.6 of `docs/Registry — Package Registry.md` makes the claim concretely: "Submitting any other value for a field typed to one of these schemas yields `400 Bad Request` with an `ErrorResponse` body." For `AssignRoleRequest.role` (PackageRoles), `SetVisibilityRequest.visibility` (Visibilities), `AddOrgMemberRequest.org_role` (OrgRoles), and the `DeprecatePackage/Version` bodies — every one of which is bound via `[FromBody]` — this claim is demonstrably false.

The P4 boundary test (`Stash.Tests/Registry/Authz/BoundedDomainBoundaryTests.cs:28`) does not exercise this path: `AdminController.CreateUser` uses inline `JsonSerializer.DeserializeAsync` + `try/catch JsonException → TypedResults.BadRequest(new ErrorResponse{...})` (see `AdminController.cs:80-88`), so the test passes against the inline path while the `[FromBody]` siblings remain uncovered.

### Why this matters

The feature's stated purpose is "a high-fidelity public contract suitable for third-party client generation" (brief Summary). A generated client modeled against the published OpenAPI document will be wired to deserialize 400 bodies as `ErrorResponse` — and will throw / mis-route on the framework's `ValidationProblemDetails` shape that actually arrives. This is the exact "client cannot reject illegal values at the call site" failure mode the bounded-domain enum work was meant to prevent.

It also makes the project's published documentation (§22.6) false. That section is the one external integrators read first.

### Suggested fix

Pick one of:

1. **Conform to the contract** (preferred, minimal change). In `Startup.ConfigureServices` (around `Startup.cs:79`):
   ```csharp
   services.AddControllers()
       .ConfigureApiBehaviorOptions(options =>
       {
           options.InvalidModelStateResponseFactory = ctx =>
           {
               var first = ctx.ModelState.Values
                   .SelectMany(v => v.Errors).FirstOrDefault();
               return new BadRequestObjectResult(new ErrorResponse
               {
                   Error = "InvalidRequest",
                   Message = first?.ErrorMessage ?? "Request body is invalid.",
               });
           };
       });
   ```
   This normalizes every `[FromBody]` 400 to `ErrorResponse` shape across all seven endpoints in one place.

2. **Replace `[FromBody]` with the inline `JsonSerializer.DeserializeAsync` + `try/catch` pattern already used by `AdminController.CreateUser` and `OrganizationsController.CreateTeam`** (consistent with the registry's existing style). More mechanical edits but no startup-pipeline change.

Either fix, then extend `BoundedDomainBoundaryTests` with a parameterized case that POSTs a bad enum value (e.g. `{"role":"NOT_A_REAL_ROLE"}` to `AssignRoleRequest` and `OrgRole` for `AddMember`) and asserts the body is `ErrorResponse`-shaped — closes the test gap.

Optionally also clarify §22.6 wording to be exact about the body shape — leaving the doc claim intact requires #1 or #2.

### Verify

```bash
dotnet test --filter "FullyQualifiedName~BoundedDomainBoundaryTests|FullyQualifiedName~Registry.OpenApi"
# new test should POST {"role":"NOT_A_REAL_ROLE"} to /api/v1/packages/{scope}/{name}/roles and assert ErrorResponse shape
```

---

## F02 — [MEDIUM] CLR-default enum member alignment with DB default literal is documented in prose only — no test guards it

**Status:** open
**Files:** `Stash.Registry/Database/RegistryDbContext.cs:97,136,223`, `Stash.Registry.Contracts/BoundedDomains.cs:46,99,196`, `Stash.Registry/CLAUDE.md`
**Phase:** P4
**Commit:** 4ec47470

### Observation

Three EF columns use `.HasConversion(...)` + `.HasDefaultValueSql("'<literal>'")` (`packages.visibility='public'`, `users.role='user'`, `org_members.org_role='member'`). EF's INSERT path omits properties whose CLR value equals the type's CLR default (zero-value for enums), letting the DB-level default fill in. The end-to-end correctness invariant is therefore:

> the wire string of `default(T)` (the enum member with numeric value 0) must equal the SQL literal in `HasDefaultValueSql("'X'")`.

The enums are arranged today so this holds: `Visibilities.Public = 0` (first declared) → `"public"`; `UserRoles.User = 0` (first declared) → `"user"`; `OrgRoles.Member = 0` (explicitly assigned). `Stash.Registry/CLAUDE.md`'s bounded-domain bullet documents the rule explicitly ("the SQL literal must match the CLR-default enum member's wire string — for OrgRoles this means Member = 0 (the CLR default) is ordered first").

But this is **Instruct** only (per the CLAUDE.md doctrine "prefer Construct, then Detect, then Instruct"). A future contributor reordering enum members for any reason — e.g. moving `Visibilities.Public` after `Visibilities.Private` for alphabetical neatness, or assigning explicit numeric values that shift the default — silently corrupts every default-visibility insert (DB stores `'public'` while the in-memory entity reads back `Visibilities.Private`). EF's own EF20601 model-validation warning fires at runtime model-finalization for exactly this fragility ("configured with a database-generated default, but has no configured sentinel value"); the warning is not surfaced by `dotnet build` (it is an `ILogger` runtime warning, not an MSBuild diagnostic, and the test contexts don't wire a logger).

`Stash.Tests/Registry/SqliteDatabaseTests.cs:885-947` (`Initialize_BoundedDomainColumnDefaults_AreLowercaseWireStrings`) guards the DDL literal — proving the SQL side stores `'public'`/`'user'`/`'member'`. Nothing asserts the matching invariant on the CLR side.

### Why this matters

This is a classic silent-corruption shape: the regression is INSERT-side (rows land with the wrong literal value), not visible until the table is queried later. The fix is a one-time three-line test; the cost of leaving it is a future P0 with a "we changed an enum order and now everything is private" postmortem.

### Suggested fix

Add a guard alongside the existing DDL-literal test in `Stash.Tests/Registry/SqliteDatabaseTests.cs`:

```csharp
[Fact]
public void BoundedDomain_CLRDefault_MatchesDDLDefaultLiteral()
{
    // The DB default fills in when EF omits a CLR-default value from INSERT.
    // If the CLR default member's wire string drifts from the SQL literal,
    // every default-valued row stores the wrong value silently.
    Assert.Equal("public", default(Visibilities).ToWire()); // packages.visibility DEFAULT 'public'
    Assert.Equal("user",   default(UserRoles).ToWire());    // users.role         DEFAULT 'user'
    Assert.Equal("member", default(OrgRoles).ToWire());     // org_members.org_role DEFAULT 'member'
}
```

Alternatively (Construct, stronger): replace `.HasDefaultValueSql("'public'")` with `.HasDefaultValue(Visibilities.Public)` + `.HasSentinel(Visibilities.Public)` and let EF's converter emit the right literal — but the explicit-SQL approach is intentional per CLAUDE.md (the SQL literal stays exact), so the Detect guard is the lighter fix.

### Verify

```bash
dotnet test --filter "FullyQualifiedName~SqliteDatabaseTests.BoundedDomain_CLRDefault"
```

---

## F03 — [MEDIUM] Three `Json{Status}<T>` IResult helpers duplicated across two controller files instead of a shared home

**Status:** open
**Files:** `Stash.Registry/Controllers/AuthController.cs:31-80`, `Stash.Registry/Controllers/ScopesController.cs:21-48`
**Phase:** P2 (`JsonUnauthorized<T>`, `JsonForbidden<T>`) + P3 (`JsonNotImplemented<T>`)
**Commit:** 8c573d63 (P2), aeb9a6f8 (P3)

### Observation

P2 introduced two custom typed-result helpers (`JsonUnauthorized<T>`, `JsonForbidden<T>` at `AuthController.cs:43,64`); P3 added a third (`JsonNotImplemented<T>` at `ScopesController.cs:32`). All three are `public sealed class … : IResult, IEndpointMetadataProvider`, declared at *namespace* scope inside a controller file. `ScopesController.cs:26-27` even acknowledges the duplication ("mirroring the `JsonUnauthorized<T>` / `JsonForbidden<T>` helpers in `AuthController.cs`").

A side-issue: the explanatory comment at `AuthController.cs:36-38` says the helpers "must be internal (not private) so they can appear in a public action's generic Results<…> return." The declared accessibility is `public`, not `internal` — `public` is in fact required (a private/internal type used in a public method signature triggers CS0050). The comment is imprecise but harmless.

### Why this matters

The placement is the canonical "helper buried in a feature file" anti-pattern: the next reviewer cannot find these via any module-level search, the duplication invitation is already visible (P3 cloned the pattern), and the next status code the codebase needs (e.g. `JsonTooManyRequests<T>` for rate-limited routes) will likely get a fourth copy in whichever controller introduces it first. This is exactly the kind of cross-cutting concern the project doctrine (CLAUDE.md "Single source of truth — A closed set duplicated across files is the same defect as an inline literal") frames as a quality issue worth flagging.

### Suggested fix

Move the three helpers to a single file `Stash.Registry/OpenApi/JsonStatusResults.cs` (or `Stash.Registry/Endpoints/JsonStatusResults.cs` — `OpenApi/` already exists for transformers). Keep them `public` (the public-action-signature requirement is real). Update `AuthController.cs:31-80` and `ScopesController.cs:21-48` to delete the local declarations. Optionally clean up the AuthController's "internal (not private)" comment to read "public (so they can appear in a public action's generic Results<…> return — CS0050)".

A small extra payoff: a single home is the natural place for a parameterized base (`JsonStatusResult<T>(int status)`) if the family grows.

### Verify

```bash
dotnet build Stash.Registry/Stash.Registry.csproj
dotnet test --filter "FullyQualifiedName~Registry.OpenApi|FullyQualifiedName~AuthController|FullyQualifiedName~ScopesController"
```

---

## F04 — [LOW] AOT subprocess self-test exercises serialize but not deserialize — partial coverage of P5 done_when

**Status:** open
**Files:** `Stash.Cli/Program.cs:442-502` (`RunEnumSelfTest`), `Stash.Tests/Cli/AotPublishedBinaryEnumRoundTripTests.cs`
**Phase:** P5
**Commit:** 3d559674

### Observation

`Program.RunEnumSelfTest` (the `--self-test enums` implementation that the AOT subprocess test invokes) only serializes each enum value and compares the result to `ToWire()` — see the `cases[]` table at `Program.cs:450-479` and the comparison loop at `483-491`. It never calls `JsonSerializer.Deserialize(...)`. The brief's Cross-Cutting Concerns row 4 frames the round-trip as the load-bearing property: "every enum value round-trips byte-identical through CLI source-gen + Native AOT," and the P5 done_when paraphrases as "`--self-test enums` invocation that prints PASS/FAIL after running the same round-trip." "Same round-trip" refers to the in-process test (`Stash.Tests/Cli/EnumRoundTripTests.cs:89-148`), which DOES exercise both serialize and deserialize (`AssertRoundTrip<T>` at line 184).

The in-process test runs under JIT, where the runtime can fall back to reflection if source-gen metadata for a type is missing — exactly the failure mode the AOT subprocess test is named for and built to guard. Deserialize is the more failure-prone direction under trim/AOT (parsing relies on `JsonTypeInfo` constructor metadata that trim can strip). Serialize-only coverage means a future regression that strips deserialize-side source-gen metadata would still print PASS.

### Why this matters

The brief explicitly frames closing the shared-contracts feature's "no AOT round-trip test shipped there" gap as a P5 goal (Acceptance Criteria: "Closes the shared-contracts residual gap"). Closing it with serialize-only coverage leaves the gap half-closed and the test name (`AotBinary_SelfTestEnums_PrintsPassAndExitsZero`) misleading.

### Suggested fix

Extend `Program.RunEnumSelfTest` (`Program.cs:442`) with a second loop that, for each enum value, deserializes the bare-string JSON literal back to the enum and asserts `Equals(value)`. The cleanest pattern reuses the existing `cases[]` table:

```csharp
foreach (var (label, json, expected) in cases)
{
    // … existing serialize check …

    // Round-trip back: parse the same JSON via the typed JsonTypeInfo and assert equality
    // (snippet — match the typed pattern already used for Serialize, per enum)
}
```

Adding seven typed `JsonSerializer.Deserialize(json, ctx.PackageRoles)` etc. calls keeps the code AOT-safe (no reflection). The fail-path coverage check in `EnumRoundTripTests` (`Stash.Tests/Cli/EnumRoundTripTests.cs:171`) already proves the in-process scanner has teeth on the deserialize direction — the AOT subprocess test should not have a weaker contract than the in-process one.

### Verify

```bash
dotnet publish Stash.Cli/Stash.Cli.csproj -c Release -o .bench-bin
dotnet test --filter "FullyQualifiedName~AotPublishedBinaryEnumRoundTripTests"
```
