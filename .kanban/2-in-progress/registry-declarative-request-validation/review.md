# Registry Declarative Request Validation — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header is parseable: `## Fxx — [SEVERITY] short title`.
>
> **Status lifecycle** — `open` blocks `/done`; `fixed` carries `**Fixed in:** <sha>`;
> `accepted` is human-only and requires `**Accepted because:** <reason>`. CRITICAL
> findings can NEVER be `accepted`.

**Scope reviewed:** commits `5ff66c16..878a90c7` on branch `feature/registry-declarative-request-validation`
**Brief:** ./brief.md
**Generated:** 2026-06-03 (post-P6)

---

## Summary

The feature delivers its central goal: ten manual `Request.Body` actions and two
query-string actions are now `[FromBody]`/`[FromQuery]`-bound, the
`RequestModelBindingMetaTests.KnownExemptions` set is empty and asserted-empty,
`InvalidModelStateResponseFactory` aggregates field errors into one
`ErrorResponse { Error = "InvalidRequest" }` envelope, and the matrix /
OpenAPI / authz dispatch gates are green. The Construct-vs-Detect doctrine is
honoured (the meta-test landed P1 RED, not as a finishing flourish).

However, the migration introduces several user-visible behaviour changes that
the brief did not authorise and that the test suite cannot see — every one of
them is a *strictness increase* invisible to a green matrix. They are flagged
below for an explicit human accept-vs-preserve decision at merge. No CRITICAL
finding; all behaviour changes are HIGH because they remain candidates for a
deliberate human accept.

| Severity | Count |
| -------- | ----- |
| CRITICAL | 0 |
| HIGH     | 3 |
| MEDIUM   | 4 |
| LOW      | 2 |

---

## F01 — [HIGH] Validate-then-normalize ordering: stricter than pre-feature behaviour on multiple fields (P5-D1)

**Status:** fixed
**Fixed in:** 15252ae476fc3f6ad033fcc7389ead8c6e115f89
**Files:** `Stash.Registry.Contracts/AuthContracts.cs:62-64`, `Stash.Registry.Contracts/OrganizationContracts.cs:14-17`, `Stash.Registry.Contracts/ScopeContracts.cs:22-25`, `Stash.Registry/Controllers/AuthController.cs:91`, `Stash.Registry/Controllers/OrganizationsController.cs:55`, `Stash.Registry/Controllers/ScopesController.cs:91`
**Phase:** P3 / P4 / P5 (cross-phase, headline of `deviations.md` P5-D1)
**Commit:** c9344b47, 8db697ca, d628db5b

### Observation

Pre-feature controllers normalized inputs (`Trim()` and, for scope-name fields,
`ToLowerInvariant()`) BEFORE running the grammar / non-empty checks. After the
migration, DataAnnotations (`[ScopeGrammar]`, `[Required]`) run on the RAW
bound value, and the action body only normalizes afterwards. Concretely:

- `RegisterRequest.Username` — pre: `Trim()` then `IsValidScopeName`; now: `[ScopeGrammar]` on raw.
- `CreateOrgRequest.Name` — pre: `Trim().ToLowerInvariant()` then `IsValidScopeName`; now: `[ScopeGrammar]` on raw.
- `ClaimScopeRequest.Scope` — pre: `Trim().ToLowerInvariant()` then `IsValidScopeName`; now: `[ScopeGrammar]` on raw.

Concrete inputs that flip from accepted-and-normalized to 400:

| Endpoint | Pre-feature input | Pre-feature outcome | Post-feature outcome |
| --- | --- | --- | --- |
| `POST /api/v1/auth/register` | `{"username":"alice "}` (trailing space) | 201, stored as `alice` | 400 InvalidRequest |
| `POST /api/v1/orgs` | `{"name":"MyOrg"}` | 201, stored as `myorg` | 400 InvalidRequest |
| `POST /api/v1/orgs` | `{"name":"acme "}` | 201, stored as `acme` | 400 InvalidRequest |
| `POST /api/v1/scopes` | `{"scope":"MyOrg",...}` | claimed as `myorg` | 400 InvalidRequest |

### Why this matters

Invisible to the test suite — `RegistryAuthzMatrixTests` uses well-formed
lowercase bodies; the new validation tests assert empty / missing fields. No
test sends a normalizable-but-raw-invalid body. The brief explicitly authorised
**only** the `pageSize` reject behaviour change (Open Questions #4). This
normalization-strictness change was never surfaced and is exactly the kind of
"merge a stricter API quietly" defect the project tries to avoid. The defect
shape (silent strictness creep across multiple shipped fields) reaches the
public REST contract and breaks live clients that today send unconventional
casing or whitespace.

The brief's "Guard deletion — the policy" lists *normalisation stays inline*
as the rule; the implementation honoured the letter (Trim/ToLower remain in
the actions) but not the spirit (they no longer run before validation).

### Suggested fix

**Escalate to the human merge handoff.** Two viable resolutions:

- **(A) Accept** the stricter behaviour. Document it alongside the `pageSize`
  break — add a bullet to `docs/Registry — Package Registry.md` § 17
  Input-Validation row noting "request bodies are not Trim/lowercase
  normalized before validation; send canonical lowercase values without
  surrounding whitespace." Update `Stash.Registry/CLAUDE.md` Controller
  Pattern → Request validation to call this out next to the pagination break.

- **(B) Preserve** the pre-feature behaviour. Restore normalize-before-validate
  by trimming/lowercasing **in the DTO property setter** so the bound value is
  already canonical when DataAnnotations run. Example for `CreateOrgRequest`:

  ```csharp
  private string? _name;
  public string? Name
  {
      get => _name;
      set => _name = value?.Trim().ToLowerInvariant();
  }
  ```

  Apply to `RegisterRequest.Username` (Trim only), `CreateOrgRequest.Name`
  (Trim + lower), `CreateTeamRequest.Name` (Trim — see F02 about adding lower),
  and `ClaimScopeRequest.Scope` (Trim + lower). The action body's
  Trim/lowercase becomes a no-op (safe to remove) and the inline
  `IsValidScopeName` check on `ScopesController.ClaimScope:96-102` becomes
  truly redundant (see F08 for that dead-code finding under option B → fixed).

Per the scope-authorisation principle in memory, the safe default is **(B)
preserve** unless the user explicitly opts into (A). Do not auto-resolve —
this needs a human call.

### Verify

```
# Under (B) preserve: regression coverage proving the legacy normalise-first contract holds.
dotnet test --filter "FullyQualifiedName~RegisterControllerValidationTests|FullyQualifiedName~OrganizationsControllerValidationTests|FullyQualifiedName~ScopesControllerValidationTests"

# Add a new test per affected field that asserts whitespace/uppercase variant → 201,
# stored as the canonical lowercase form. Mirror the pre-feature semantics.
dotnet test --filter "FullyQualifiedName~RegistryAuthzMatrixTests"
```

---

## F02 — [HIGH] CreateTeam.Name: new ScopeGrammar validation, no equivalent pre-feature check

**Status:** fixed
**Fixed in:** 15252ae476fc3f6ad033fcc7389ead8c6e115f89
**Files:** `Stash.Registry.Contracts/OrganizationContracts.cs:96-100`, `Stash.Registry/Controllers/OrganizationsController.cs:197`
**Phase:** P4
**Commit:** 8db697ca

### Observation

Pre-feature `OrganizationsController.CreateTeam` validated only that the team
name was non-empty after `Trim()` — there was NO grammar check on team names.
The migration added `[ScopeGrammar]` to `CreateTeamRequest.Name`, so any team
name that does not conform to `^[a-z][a-z0-9-]{0,38}$` now returns 400. This
is a **new validation entirely**, not just a reordering of existing
normalize-then-validate logic (the case covered in F01).

Concrete impact:

| Endpoint | Pre-feature input | Pre-feature outcome | Post-feature outcome |
| --- | --- | --- | --- |
| `POST /api/v1/orgs/{org}/teams` | `{"name":"backend_team"}` (underscore) | 201, team created | 400 InvalidRequest |
| `POST /api/v1/orgs/{org}/teams` | `{"name":"Backend Team"}` | 201, team created (space + uppercase preserved) | 400 InvalidRequest |
| `POST /api/v1/orgs/{org}/teams` | `{"name":"team_42"}` | 201 | 400 |

### Why this matters

The brief lists `CreateTeamRequest.Name` under fields that gain `ScopeGrammar`
("Centralises the single rule that today is expressed in four slightly
different error strings" — `RegisterRequest.Username`, `CreateOrgRequest.Name`,
`ClaimScopeRequest.Scope`). But the discriminating-check claim is incorrect
for `CreateTeamRequest.Name`: it had no grammar check at all pre-feature. The
brief implicitly assumes parity with org names; the code did not.

Whether teams *should* share the scope grammar is a sensible product decision,
but it is the **product decision** that needs surfacing to the user, not a
stealth migration artifact. Existing organisations with teams named with
underscores / mixed case / spaces (legacy, present in any production DB) will
also be unable to create *new* teams matching their established convention.

### Suggested fix

**Surface to the user for a deliberate yes/no.** Then:

- **If "yes, teams should follow scope grammar":** keep `[ScopeGrammar]` as-is
  but document the new constraint in `docs/Registry — Package Registry.md`
  Section 17 (Input Validation), and consider a one-line audit query the
  operator runs at upgrade time to surface incompatible legacy team names.
  Note: pre-existing teams with underscores/uppercase are stored as-is and
  remain readable; only the *create* path tightens.

- **If "no, keep teams permissive":** remove `[ScopeGrammar]` from
  `CreateTeamRequest.Name`. The `[Required]` + `Trim()` behaviour matches the
  pre-feature contract.

This is a separate decision from F01 (which is about ordering, not novelty),
but the two are entangled: under F01-(B)-preserve, the team-name setter would
only `Trim()` (not lower), and the question becomes "should the grammar
attribute apply at all on team names?"

### Verify

```
# Add an explicit test capturing the chosen behaviour:
dotnet test --filter "FullyQualifiedName~OrganizationsControllerValidationTests"
```

---

## F03 — [HIGH] AuditLogQuery.PageSize default silently changed from 50 to 20 (docs still say 50)

**Status:** fixed
**Fixed in:** e9f7bdd4
**Files:** `Stash.Registry.Contracts/AdminContracts.cs:75`, `docs/Registry — Package Registry.md:827`
**Phase:** P5
**Commit:** d628db5b

### Observation

Pre-feature `AdminController.GetAuditLog` declared `int pageSize = 50;` and
clamped to `Math.Min(parsed, 200)`. The new `AuditLogQuery` DTO sets the
default to **20** (`public int PageSize { get; set; } = 20;`). The
P6 documentation update (`docs/Registry — Package Registry.md:827`) preserves
the pre-feature wording — "Results per page (default 50, 1–200);
out-of-range returns `400`" — so docs and code now disagree.

### Why this matters

Calling `GET /api/v1/admin/audit-log` with no `pageSize` parameter:

| Pre-feature | Post-feature |
| --- | --- |
| Returns page 1, 50 entries, `pageSize: 50` in body | Returns page 1, 20 entries, `pageSize: 20` in body |

Admin tooling that pages off the response's `totalPages` field will get
different pagination counts after upgrade. Live clients see a silent
behaviour shift. Combined with the docs saying "default 50" while code emits
20, this is a clean MEDIUM regression that becomes HIGH because it also
falsifies the freshly-written P6 docs (the source of truth for operators).

`SearchQuery.PageSize` default 20 was preserved (pre = 20, post = 20),
confirming the change in audit was unintentional drift, not a deliberate
parity push.

### Suggested fix

Pick one:

- **Preserve:** change `AuditLogQuery.PageSize` default to `50` (one-line fix
  in `AdminContracts.cs`). Add a test asserting `GET /audit-log` with no
  query parameter returns 50 entries (when 50+ exist). Docs match.
- **Accept:** update `docs/Registry — Package Registry.md:827` to "default 20"
  AND add a CHANGELOG/migration note for operators. This is a wire-visible
  default change.

Preserve is safer and matches the brief's "wire surface does not change"
discipline.

### Verify

```
dotnet test --filter "FullyQualifiedName~AdminControllerValidationTests"
# Add: GetAuditLog_NoPageSize_DefaultsTo<N>EntriesPerPage()
```

---

## F04 — [MEDIUM] OpenAPI query parameter names are PascalCase ("Q", "Page", "PageSize") but docs / SDK contract are lowercase

**Status:** fixed
**Fixed in:** f7c9ea9b
**Files:** `Stash.Tests/Registry/OpenApi/Snapshots/openapi-v1.json` (added `Q`, `Page`, `PageSize`, `Package`, `Action` parameter names), `Stash.Registry.Contracts/SearchContracts.cs:14-37`, `Stash.Registry.Contracts/AdminContracts.cs:57-83`
**Phase:** P3 / P5
**Commit:** c9344b47, d628db5b

### Observation

ASP.NET Core MVC binds `[FromQuery]` parameters by **C# property name**
(case-insensitive), not by `[JsonPropertyName]`. The DTOs carry
`[JsonPropertyName("q")]` etc., but Swashbuckle / minimal-API OpenAPI emits
the *C# property name* as the parameter `name`. The regenerated OpenAPI
snapshot now contains:

```json
{ "name": "Q",       "in": "query", ... }
{ "name": "Page",    "in": "query", ... }
{ "name": "PageSize","in": "query", ... }
{ "name": "Package", "in": "query", ... }
{ "name": "Action",  "in": "query", ... }
```

The published REST tables (`docs/Registry — Package Registry.md:623-628` and
`:822-827`) and the CLI / `stash pkg search` consumer use lowercase
`?q=...&page=1&pageSize=20`. ASP.NET binds case-insensitively so functionally
nothing breaks today, but the OpenAPI document is now the wrong contract.

This is the same root cause as `deviations.md` P3-D1 ("SearchQuery.Query →
Q rename"), but the deviation only flagged the property-name ugliness; the
wider issue is that ALL five query parameter names are wrong on the wire
contract document.

### Why this matters

- Generated SDKs (e.g. OpenAPI-Generator targets, TypeScript clients, k6/load
  testing) will emit `?Q=foo&Page=1&PageSize=20` — non-canonical, contradicts
  documented examples, will confuse log/metrics aggregators that key on URL
  shape.
- The OpenAPI doc is the published contract; it should match the documented
  request shape, not "happen to work because ASP.NET tolerates the casing."

### Suggested fix

Move the wire-name pinning OUT of the DTOs and INTO the controller signature
via a thin binding shim — `Stash.Registry` can reference MVC, but
`Stash.Registry.Contracts` deliberately cannot. Two options:

- **(A) Per-property `[FromQuery(Name = "...")]`** in the action signature:

  ```csharp
  public async Task<...> Search(
      [FromQuery(Name = "q")] string? q = null,
      [FromQuery(Name = "page")] int page = 1,
      [FromQuery(Name = "pageSize")] int pageSize = 20)
  ```

  Drops the `SearchQuery` DTO. Range validation still works via per-parameter
  `[Range]` (works on action parameters too).

- **(B) Lower-case the DTO property names** (`q`, `page`, `pageSize`,
  `package`, `action`). Conflicts with C# style conventions (and C# disallows
  reserved-keyword overlaps but none here) — pragmatic but ugly. The
  implementer already accepted `Q` (P3-D1), which sets a precedent.

(A) is cleaner because the wire-name decision lives next to the action that
defines the wire surface; (B) propagates the wire decision into the
dependency-free Contracts project.

### Verify

```
dotnet test --filter "FullyQualifiedName~OpenApiSnapshotTests"
# Snapshot will need a regen after the fix; verify it now contains
# lower-case parameter names matching docs.
STASH_SNAPSHOT_REGEN=1 dotnet test --filter "FullyQualifiedName~OpenApiSnapshotTests"
```

---

## F05 — [MEDIUM] ScopeGrammarAttribute and TokenExpiryAttribute duplicate canonical grammar/parse — no cross-check guards drift (P2-D1)

**Status:** open
**Files:** `Stash.Registry.Contracts/Validation/ScopeGrammarAttribute.cs:27`, `Stash.Registry.Contracts/Validation/TokenExpiryAttribute.cs:83-122`, `Stash.Core/Common/PackageManifest.cs:31`, `Stash.Registry/Endpoints/AuthHelper.cs:39-63`
**Phase:** P2
**Commit:** 4df0db13

### Observation

The brief mandated that `ScopeGrammarAttribute` "wraps
`PackageManifest.IsValidScopeName`" and `TokenExpiryAttribute` "wraps
`AuthHelper.ParseTokenExpiry`". Literal wrapping is impossible because
`Stash.Registry.Contracts` is dependency-free (cannot reference `Stash.Core`
or `Stash.Registry`), so both attributes **inline** the canonical logic:

- `ScopeGrammarAttribute` re-declares `^[a-z][a-z0-9-]{0,38}$` (line 27); the
  source generator on `PackagingRegexes.ScopeSegment()` is identical
  (verified byte-for-byte against `Stash.Core/Common/PackageManifest.cs:31`).
- `TokenExpiryAttribute.TryParseTotalMinutes` duplicates the suffix-parse
  logic of `AuthHelper.ParseTokenExpiry` (`d`/`h`/`m` + plain-int fallback).

Today the two copies agree. They are not guarded against drift: changing
either home does not fail any test.

This violates the CLAUDE.md "single source of truth" rule for bounded domains:
"A closed set duplicated across files (e.g. the same `["a","b"]` array copied
into three places) is the same defect as an inline literal — collapse it." The
scope grammar is the canonical example.

### Why this matters

- **Drift cost is real.** The grammar is referenced from `PackageManifest`
  (validates package names), `Stash.Cli` checks, the playground tokenizer,
  registry controllers, and now `[ScopeGrammar]` in Contracts. If the language
  team decides to allow underscores (or extend max length), four places must
  change in lockstep with no compile-time or test-time enforcement.
- **TokenExpiry is more entangled** than the duplication suggests. The
  attribute intentionally adds a `≥1h` floor that `AuthHelper.ParseTokenExpiry`
  does NOT have. A naive "share the helper by moving it to Contracts" would
  route the existing token-creation call sites through the floored version
  and CHANGE token-creation behaviour (e.g. config-driven access-token
  expiries shorter than 1h would start throwing). Share the parse, keep the
  floor in the attribute.

### Suggested fix

**Minimum acceptable (low cost, high return):** add cross-check tests that
fail CI if the two homes diverge.

```csharp
// Stash.Tests/Registry/Validation/ScopeGrammarAgreementMetaTests.cs
[Theory]
[InlineData("alice")]
[InlineData("MyOrg")]                    // uppercase
[InlineData("a_b")]                      // underscore
[InlineData("")]
[InlineData("a"*40)]                     // too long
public void ScopeGrammarAttribute_AgreesWith_PackageManifestIsValidScopeName(string v)
{
    bool attrSaysValid = new ScopeGrammarAttribute()
        .GetValidationResult(v, new ValidationContext(v ?? new object()))
        == ValidationResult.Success;
    bool coreSaysValid = !string.IsNullOrEmpty(v) && PackageManifest.IsValidScopeName(v);
    // Attribute treats empty as success (combined with [Required]); align for the cross-check.
    if (string.IsNullOrEmpty(v)) coreSaysValid = true;
    Assert.Equal(coreSaysValid, attrSaysValid);
}
```

Same shape for `TokenExpiryAttribute` vs `AuthHelper.ParseTokenExpiry` over a
corpus including boundary cases (`0d`, `59m`, `60m`, `1h`, `1d`, malformed).

**Durable (more work, recommended next milestone):** relocate the canonical
grammar (regex) and the duration parse into `Stash.Registry.Contracts` (or a
new dependency-free `Stash.Common.Grammars`); `PackageManifest` and
`AuthHelper` consume from there. The `≥1h` floor stays attribute-only.

### Verify

```
dotnet test --filter "FullyQualifiedName~ScopeGrammarAgreement|FullyQualifiedName~TokenExpiryAgreement"
dotnet test --filter "FullyQualifiedName~ContractsValidationAttributesTests"
```

---

## F06 — [MEDIUM] Validation tests have a coverage hole: no test sends a normalizable-but-raw-invalid body

**Status:** fixed
**Fixed in:** 8dd34a23
**Files:** `Stash.Tests/Registry/Validation/AuthControllerValidationTests.cs`, `Stash.Tests/Registry/Validation/OrganizationsControllerValidationTests.cs`, `Stash.Tests/Registry/Validation/ScopesControllerValidationTests.cs`, `Stash.Tests/Registry/Validation/ContractsValidationAttributesTests.cs`
**Phase:** P3 / P4 / P5
**Commit:** c9344b47, 8db697ca, d628db5b

### Observation

The new validation tests cover empty / missing fields and out-of-range
pagination, but no test asserts what happens for inputs that differ from a
valid value only by **whitespace** or **case**. This is precisely the input
shape whose behaviour silently changed (F01) — the test suite *cannot* see
that change today.

`ContractsValidationAttributesTests` for `ClaimScopeRequest` covers:
- `Scope = null` → fails Required (line 214)
- `Scope = "Invalid-Scope"` → fails ScopeGrammar (line 218)
- `Owner = null` → fails Required (line 222)
- `Owner = "   "` (whitespace) cross-field rule fires (line 240)

But no test for `Scope = "MyOrg"` (uppercase) or `Scope = " my-scope "`
(surrounding whitespace) asserting the chosen behaviour. Similarly missing
for `RegisterRequest.Username` and `CreateOrgRequest.Name`.

### Why this matters

A coverage hole here is what made F01 invisible. Whichever direction F01 is
resolved (accept-strict or preserve-lenient), the resolution must come with
tests pinning the behaviour so a future change to the validation order or DTO
setter cannot drift again.

### Suggested fix

After F01 is resolved, add one test per affected field × variant (whitespace,
uppercase, mixed) asserting the chosen outcome (400 InvalidRequest under (A)
accept, or 201/200 + canonical lowercase storage under (B) preserve). For
`ContractsValidationAttributesTests`:

```csharp
[Theory]
[InlineData("MyOrg")]
[InlineData(" myorg ")]
[InlineData("My-Org")]
public void ClaimScopeRequest_ScopeWithCaseOrWhitespace_<Accepted|Rejected>(string value) { ... }
```

Wire-level coverage in `ScopesControllerValidationTests` for at least one
case ensures the controller + factory + filter pipeline is exercised, not
just the attribute in isolation.

### Verify

```
dotnet test --filter "FullyQualifiedName~ContractsValidationAttributesTests"
dotnet test --filter "FullyQualifiedName~ScopesControllerValidationTests"
```

---

## F07 — [MEDIUM] ClaimScopeRequest.Validate() rejects OwnerType = System but has no test coverage (P5-D2 gap)

**Status:** fixed
**Fixed in:** 8dd34a23
**Files:** `Stash.Registry.Contracts/ScopeContracts.cs:52-66`, `Stash.Tests/Registry/Validation/ContractsValidationAttributesTests.cs`
**Phase:** P5
**Commit:** d628db5b

### Observation

P5 extended `ClaimScopeRequest.Validate()` to reject `OwnerType == null` and
`OwnerType == ScopeOwnerTypes.System` with `"owner_type must be 'user' or
'org'."` (`ScopeContracts.cs:60-66`). This was a P2 coverage gap surfaced in
P5 — the original P2 implementation only checked `Owner` presence when
`OwnerType` was set, allowing `OwnerType = System` to slip through.

`ContractsValidationAttributesTests` does not test:
- `ClaimScopeRequest { OwnerType = null, ... }` → expected to fail with
  "owner_type field is required..."
- `ClaimScopeRequest { OwnerType = ScopeOwnerTypes.System, ... }` → expected
  to fail with "owner_type must be 'user' or 'org'..."

The pre-feature controller had the equivalent guard:

```csharp
if (ownerTypeRaw == null || (ownerTypeRaw != User && ownerTypeRaw != Org))
    return BadRequest("owner_type must be 'user' or 'org'.");
```

So the behaviour is **wire-equivalent** to pre-feature on these paths (good),
but the coverage hole means a future regression to `Validate()` could remove
the `System` rejection silently. The implementer flagged this as P5-D2 and
asked for reviewer confirmation.

### Why this matters

`ScopeOwnerTypes.System` is the wire-reserved type for the `@stash` /
`@admin` reserved scopes. If a future refactor of `Validate()` removed the
"must be user or org" branch, the request would reach the action body with
`OwnerType = System` and fall into the `else` (Org) branch (line 126 of
`ScopesController.cs`), looking up an org with the literal owner string. The
PDP and the `ReservedScopes.IsReserved` check would still catch most paths
(409), but the wire response shape would diverge.

### Suggested fix

Add explicit tests in `ContractsValidationAttributesTests.cs`:

```csharp
[Fact]
public void ClaimScopeRequest_OwnerTypeNull_FailsCrossFieldRule()
{
    var model = new ClaimScopeRequest { Scope = "alice", Owner = "alice", OwnerType = null };
    var errors = model.Validate(new ValidationContext(model)).ToList();
    Assert.Contains(errors, e => e.ErrorMessage?.Contains("owner_type") == true);
}

[Fact]
public void ClaimScopeRequest_OwnerTypeSystem_FailsCrossFieldRule()
{
    var model = new ClaimScopeRequest { Scope = "alice", Owner = "alice", OwnerType = ScopeOwnerTypes.System };
    var errors = model.Validate(new ValidationContext(model)).ToList();
    Assert.Contains(errors, e => e.ErrorMessage?.Contains("user") == true && e.ErrorMessage?.Contains("org") == true);
}
```

### Verify

```
dotnet test --filter "FullyQualifiedName~ContractsValidationAttributesTests"
```

---

## F08 — [LOW] Dead inline IsValidScopeName guard in ScopesController.ClaimScope (P5-D4)

**Status:** fixed
**Fixed in:** 8dd34a23
**Files:** `Stash.Registry/Controllers/ScopesController.cs:95-102`
**Phase:** P5
**Commit:** d628db5b

### Observation

`ScopeGrammarAttribute` on `ClaimScopeRequest.Scope` runs before the action
body, so any invalid grammar already 400s at the filter. The action then
normalizes (`Trim().ToLowerInvariant()`) and re-checks
`PackageManifest.IsValidScopeName(scopeName)` at line 96-102 — but
`Trim().ToLowerInvariant()` only ever LOOSENS the grammar conformance; if the
raw input passed `[ScopeGrammar]`, the normalised value also passes. The
inline check is dead code.

The implementer kept it (the brief's done_when for P5 did not list it for
deletion) and flagged it in `deviations.md` P5-D4.

### Why this matters

Three reasons it should go:

1. The brief's Guard-deletion policy says delete what validation fully
   covers — this fits.
2. It contradicts the rest of the ClaimScope migration, which deleted other
   structural guards.
3. Most importantly: it READS as "we still validate the grammar here just in
   case", which obscures what the post-migration invariant actually is.

But — the interaction with F01 matters. Under **F01-(B)-preserve** (DTO
setter normalises before validation), this inline check becomes a defensible
safety net (the only place that re-validates the post-normalisation value).
Under **F01-(A)-accept**, it is unambiguously dead.

### Suggested fix

Defer until F01 is resolved. Then:

- F01 → (A) accept-strict: delete `ScopesController.cs:95-102`. Add a comment
  pointing at `[ScopeGrammar]` on `ClaimScopeRequest.Scope`.
- F01 → (B) preserve-normalize-first: keep the inline check, add a comment
  noting it is the post-normalisation safety net.

### Verify

```
dotnet test --filter "FullyQualifiedName~ScopesControllerValidationTests"
dotnet test --filter "FullyQualifiedName~RegistryAuthzMatrixTests"
```

---

## F09 — [LOW] CreateUserRequest.Username regex restoration deserves a behaviour-pinning test (P5-D3)

**Status:** fixed
**Fixed in:** e9f7bdd4
**Files:** `Stash.Registry.Contracts/AdminContracts.cs:23-29`, `Stash.Tests/Registry/Validation/AdminControllerValidationTests.cs`
**Phase:** P5
**Commit:** df69e2cd (fix), d628db5b (feat)

### Observation

The pre-feature `AdminController.CreateUser` validated usernames against
`^[a-zA-Z0-9_-]+$` (a different grammar from the lowercase-only scope grammar
used for self-registration): it allows UPPERCASE and underscores. P2's
initial DataAnnotations only added `[Required] + [StringLength(64)]`, dropping
the char-set check. P5 fix commit `df69e2cd` restored it via
`[RegularExpression(@"^[a-zA-Z0-9_-]+$")]`.

The dual-grammar situation (admin allows uppercase + underscore, self-register
does not) is **intentional** and pre-existing. The fix preserves it. But:

1. The admin grammar is now a bare `[RegularExpression]` literal — not a
   named bounded-domain constant. This is the second copy of an admin-specific
   pattern in the codebase (`_authProvider.CreateUserAsync` does not
   re-validate at the auth layer); a future change to the admin grammar
   has no single home.
2. No P2 test caught the regression; no P5 test pins the restored behaviour.
   `AdminControllerValidationTests` should assert a UPPERCASE admin username
   succeeds (201) and a non-conforming username fails (400) so a future
   refactor can't drop the regex silently.

### Why this matters

The CLAUDE.md no-magic-strings doctrine treats bounded patterns (regex,
grammar) like enum values: they want a named home. A bare regex literal in a
DTO is exactly the shape that drifted between P2 and P5. The pre-existing
admin-vs-register grammar split is fine; the lack of enforcement is what's
fragile.

### Suggested fix

- Add tests pinning the dual grammar:

  ```csharp
  [Fact]
  public Task CreateUser_UpperCaseUsername_Returns201() { ... }
  [Fact]
  public Task CreateUser_UsernameWithUnderscore_Returns201() { ... }
  [Fact]
  public Task CreateUser_UsernameWithSpace_Returns400() { ... }
  ```

- Optionally: extract the admin-username pattern into a named const in
  `AdminContracts.cs` (e.g. `internal const string AdminUsernamePattern =
  "^[a-zA-Z0-9_-]+$";`) and reference it in the attribute. Cheap doctrine
  alignment; one source of truth.

### Verify

```
dotnet test --filter "FullyQualifiedName~AdminControllerValidationTests"
```

---

