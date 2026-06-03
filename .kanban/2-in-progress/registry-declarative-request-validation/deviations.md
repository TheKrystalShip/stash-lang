# Implementer deviations — for the reviewer to adjudicate

## ✅ DECISION (user, 2026-06-03) — review pass 1 findings

**F01 → (A) ACCEPT the stricter validation.** Request-body grammar fields are validated as-sent
(no Trim/lowercase normalization before validation); mixed-case/whitespace inputs that were
previously normalized-and-accepted now return 400. No code revert — document the behaviour.

**F02 → (A) KEEP `[ScopeGrammar]` on `CreateTeam.Name`.** Team names are now grammar-validated like
org/scope names (a new, intended restriction). Document it.

**Rationale (user-endorsed):** the registry is pre-release — no deployed instance, no existing
clients, the in-repo CLI is the only consumer and already sends canonical values — so the
compatibility cost of the stricter behaviour is ≈ zero, and the cleaner declarative contract wins.

**F03 = clean regression** (not one of the two decisions): restore `AuditLogQuery.PageSize` default
to 50 (preserve), matching the P6 docs. The remaining findings resolve in the (A) direction (e.g.
F06 tests assert 400 for normalizable-but-raw-invalid input; F08 deletes the now-truly-dead guard).

---


This file accumulates **plan deviations and ratification requests** reported by the implementer
agents during the phase loop. The reviewer reads only the diff + `brief.md`; these deviations
otherwise live only in the driver's context. They are surfaced here (and in the `/feature-review`
dispatch) so the review adjudicates them rather than rubber-stamping pre-rationalized code.

**Driver note on a review trap:** some deviations are pre-rationalized *in code comments*
(e.g. `// inlined here to keep Stash.Registry.Contracts dependency-free`). A diff-only reviewer
reads those as already-settled. Treat each item below as an OPEN question to confirm or refute,
not as a justified decision.

---

## P1 — Roslyn meta-test lands RED with pinned exemptions (commit 82627ac3, +d7f22643)

- **P1-D1 — third detection sink added (`Request.Query`).** `done_when` named two sinks (manual
  `DeserializeAsync`, Contracts-typed param missing a binding attr), but the 12-entry pin includes
  2 query-string actions (`Admin.GetAuditLog`, `Search.Search`) that neither named sink catches.
  Implementer added sink (c) for direct `Request.Query` access so the live violation set == 12.
  **Driver verdict: sound** — the `done_when` observable contract (live set == 12-pin) was the real
  spec; the mechanism was under-specified. Confirm the third sink is correctly scoped (doesn't
  over-flag legitimate query reads outside controller actions).
- **P1-D2 — `Mvc.Core` metadata reference added** so the Roslyn semantic model can bind `[FromBody]`
  in sink (b)'s attribute resolution; binding-floor probe now also asserts `FromBodyAttribute`
  resolves. **Driver verdict: necessary and correct.**
- **P1-D3 — sink (b) positive self-test added** (`d7f22643`, after the chore commit) — `done_when`
  only mandated a sink-(a) positive + clean-`[FromBody]` negative; sink (b) had no teeth-proof.
  **Driver verdict: strengthens the test.** Note it landed as a separate commit *after* the P2
  checkpoint chore — harmless (tree clean, P1 recorded commit reachable) but worth noting.

## P2 — Contracts DTOs gain DataAnnotations + factory aggregates (commit 4df0db13)

- **P2-D1 — custom attributes REIMPLEMENT instead of WRAP (MOST SIGNIFICANT).** `done_when` says
  `ScopeGrammarAttribute` "wraps `PackageManifest.IsValidScopeName`" and `TokenExpiryAttribute`
  "wraps `AuthHelper.ParseTokenExpiry`". Literal wrapping is **impossible**: `Stash.Registry.Contracts`
  is deliberately dependency-free, but `IsValidScopeName` lives in `Stash.Core` and `ParseTokenExpiry`
  in `Stash.Registry` — referencing either would create a forbidden/circular dep. So both were
  inlined (duplicated).
  - **Driver discriminating check (done):** the inlined scope regex `^[a-z][a-z0-9-]{0,38}$` is
    **byte-for-byte identical** to the source-generated `[GeneratedRegex]` on
    `PackagingRegexes.ScopeSegment()` (Stash.Core/Common/PackageManifest.cs:32). So it has **not**
    diverged — currently correct. Severity = **single-source-of-truth duplication (medium)**, NOT a
    live bug. The defect is *future drift*: two copies of a bounded grammar (CLAUDE.md doctrine:
    "duplicated closed set == inline literal").
  - **Durable fix to weigh:** relocate the canonical grammar (and the `d/h/m/int` parse) INTO
    `Stash.Registry.Contracts` so the controllers' existing call sites AND the attributes share ONE
    definition. **Caveat for the resolver (advisor-flagged):** this is NOT a pure dedup —
    `TokenExpiryAttribute` intentionally ADDS a `>=1h` floor that `AuthHelper.ParseTokenExpiry` does
    not have. The *parse* can be shared; the floor is validation-only. A naive "move the helper"
    that routes the existing token-creation call sites through the floored version would CHANGE
    token-creation behavior. Share parse, keep floor in the attribute.
  - **Minimum acceptable resolution if relocation is rejected:** a cross-check test asserting
    `ScopeGrammarAttribute` agrees with `IsValidScopeName` over a corpus (incl. boundary/invalid
    cases), so drift fails CI.
- **P2-D2 — OpenAPI snapshot regenerated.** Brief implied "no snapshot bump." Forced because
  `AddOrgMemberRequest` is already `[FromBody]`-bound in `OrganizationsController.AddMember`, so
  `[Required]` on `Username` surfaces as `"required": ["username"]`. Diff = 3 lines, one required
  field on one schema; no 400-shape / `ErrorResponse` $ref change; AuthzMatrix green. **Driver
  verdict: legitimate** — the brief's "no bump" was about the factory/ErrorResponse change and
  didn't account for `[Required]` on an already-bound DTO. Confirm the snapshot diff is exactly that.
- **P2-D3 — `plan.yaml` P2 verify command corrected.** Was `dotnet build A B C` (multi-project,
  which `dotnet build` rejects) → `dotnet build`. Within the "stale verify command" correction
  allowance, but note the implementer edited a checkpoint artifact (`plan.yaml`).
- **P2-D4 — factory adds `.Where(m => !string.IsNullOrEmpty(m))`** before the `"; "` join, beyond
  the literal `done_when` formula, to avoid spurious delimiters. **Driver verdict: defensible.**
- **P2-D5 — AOT/trim cleanliness unverified at phase level.** `StringLength`/`Range` carry
  `[UnconditionalSuppressMessage]` per the `DeprecatePackageRequest.Message` precedent; `[Required]`
  + the two custom attributes have no `[RequiresUnreferencedCode]` path. `final_verify` runs
  `dotnet test`, NOT the AOT publish — so trim cleanliness of the new attributes is NOT gated by
  `/done`. The AOT enum self-test lives in `build.stash` (manual). Reviewer: confirm whether an AOT
  publish smoke is warranted before merge, or accept the precedent-mirroring as sufficient.

## P3 — AuthController + SearchController migrate; exemption -5 (commit 4d812e13, feat c9344b47)

- **P3-D1 — `SearchQuery.Query` renamed to `SearchQuery.Q` (wire-binding deviation).** ASP.NET
  `[FromQuery]` binds by property name (case-insensitive), not `[JsonPropertyName]`; and Contracts is
  dependency-free so `[FromQuery(Name="q")]` cannot be applied to the DTO property. Implementer
  renamed the property `Query` → `Q` so `?q=` binds. **Driver verdict: pragmatic but the ugliest
  surviving artifact** — `Q` is a poor public property name. Reviewer: decide whether `Q` is
  acceptable, or whether a controller-side binding shim (a thin `[FromQuery(Name="q")]` parameter in
  `Stash.Registry`, which CAN reference MVC) is the cleaner home for the wire-name mapping. Confirm
  the wire contract `?q=` is unchanged (the REST table documents `/api/v1/search?q=...`).
- **P3-D2 — recorded behaviour change: out-of-range `pageSize` now 400, was silent clamp.** This is
  the deliberate open-question #4 resolution from brief.md (reject via `[Range]`). Any client today
  sending `pageSize=500` got a clamped 200; now gets 400 InvalidRequest. Reviewer: confirm this is
  the intended, documented break (it is per the brief) and that docs/CLAUDE.md reflect it (P6).
- **P3-D3 — three PRE-EXISTING tests edited to match new behaviour.** `ContractsAssemblyShapeTests`
  (P2 gap: the new `Stash.Registry.Contracts.Validation` sub-namespace wasn't in the allowed shape),
  and `LoginReadDefaultTests.CreateToken_WithScopeFieldOnly_Returns400CeilingRequired` +
  `TokenLifetimeCapTests.CreateToken_ExpiresInAbsent_Returns400` (inline guard removal moved the 400
  from a hand-thrown message to the validation filter, so the expected `ErrorMessage` text changed).
  **Reviewer: scrutinise these edits** — changing an existing test to match new behaviour is exactly
  where a real regression can be masked. Confirm each still asserts the same *semantics* (400 +
  ceiling/expiry-required + body-not-entered), only the message-source changed.
- **P3-D4 — OpenAPI snapshot regenerated** (Auth endpoints gain requestBody schemas; Search gains
  Q/Page/PageSize query params + a 400 response). Expected consequence of the migration. Confirm the
  diff is exactly the migrated surface, no unrelated drift.
- **P3-D5 — `ErrorMessage` strings added to existing P2 `[Required]` attributes** on `LoginRequest`
  / `TokenCreateRequest` to preserve specific assertion text ("Username is required." etc.). Benign
  — these are user-facing prose (exempt from no-magic-strings), but they ARE the strings the
  aggregation test asserts on; confirm message ↔ test stay in sync.

## Driver-applied fixes (not implementer deviations)

- **plan.yaml P4 + P5 verify commands** changed from `dotnet build Stash.Registry Stash.Tests`
  (multi-project, which `dotnet build` rejects) to `dotnet build`, matching the P2/P3 self-heals.
  Mechanical; prevents the next two implementers re-discovering the same stale command. Committed by
  the driver as a chore.

## P4 — OrganizationsController migrates; AddMember non-nullable; exemption -3 (commit 8db697ca)

- **P4-D1 — OpenAPI snapshot regenerated** (recurring): `[FromBody]` on CreateOrg/CreateTeam/
  AddTeamMember adds 3 requestBody blocks + 3 DTO schemas; `AddMember`'s `oneOf:[null,$ref]` became
  `$ref` + `required:true` (the non-nullable flip). Expected migration surface. Confirm no unrelated
  drift.
- **P4-D2 — validation test helper corrected to a publish-ceiling token.** The new
  `OrganizationsControllerValidationTests` helper initially minted a read-ceiling token; CreateOrg/
  AddMember/CreateTeam require `publish`, so it was changed to the `RegistryAuthzTestBase` pattern
  (login → explicit publish-ceiling token). Test-fixture correctness fix, not production behaviour.
- **Guard-deletion executed cleanly:** deleted the JSON try/catch + empty/grammar guards now covered
  by attributes; KEPT normalization (Trim/ToLowerInvariant), `ReservedScopes.IsReserved` → 409, the
  `InvalidOperationException` → 409 conflict path, and DB 404 lookups. Reviewer: spot-check that no
  *validation* guard was kept (dead code) and no *business* guard was deleted (behaviour loss).

## P5 — AdminController + ScopesController migrate; exemption empty (commit df69e2cd, feat d628db5b)

### ⚑ P5-D1 — TOP REVIEW ITEM: validate-then-normalize ordering flipped (systemic, test-invisible behaviour change)

**Confirmed by diff against base 5ff66c16.** The pre-feature controllers normalized THEN validated:
```csharp
string? name = body?.Name?.Trim().ToLowerInvariant();   // normalize first
if (!PackageManifest.IsValidScopeName(name)) return 400; // validate the NORMALIZED value
```
Declarative validation runs on the RAW bound value BEFORE the action body, so the order is now
`validate(raw) → action normalizes`. **Consequence:** inputs that differ from a valid scope name
ONLY by uppercase or surrounding whitespace flip from *silently normalized + accepted* to *400
InvalidRequest*. Example: `CreateOrg` with `name:"MyOrg"` → was accepted (stored as `myorg`), now 400.

**Affected (reviewer: enumerate the full set):** every field carrying `[ScopeGrammar]` that the
action previously `Trim().ToLowerInvariant()`-normalized before validating — at least
`CreateOrgRequest.Name`, `CreateTeamRequest.Name`, `ClaimScopeRequest.Scope`,
`RegisterRequest.Username`.

**Why it's invisible:** `RegistryAuthzMatrixTests` uses well-formed lowercase bodies; the new
validation tests assert empty/missing-field cases. No test sends a normalizable-but-raw-invalid
body, so the suite is green despite the change.

**This was NOT an authorised break.** Only the `pageSize` reject (P3/P5) was an explicit
open-question decision. This normalization-strictness change was never surfaced to the user.

**Decision required (do NOT auto-resolve — escalate to the human merge handoff):**
- **(A) Accept** the stricter behaviour — document it in `docs/Registry` + CLAUDE.md as a second
  intended break, alongside `pageSize`.
- **(B) Preserve** old behaviour — normalize before the grammar check, e.g. normalize in the DTO
  property setter so the bound value is already trimmed/lowercased when DataAnnotations run (keeps
  declarative validation while restoring normalize-then-validate). More work; touches Contracts DTOs.

Per the scope-authorization principle, the safe default is **(B) preserve** unless the user
explicitly opts into the stricter behaviour. Flag in review.md; surface at handoff.

### P5-D2 — `ClaimScopeRequest.Validate()` extended (P2 GAP fix, ScopeContracts.cs)
P2's `IValidatableObject` only validated `Owner` when `OwnerType != null` — it did NOT reject an
absent or `System` owner_type, so the brief's "IValidatableObject covers the owner_type==null guard"
was inaccurate as P2 shipped. P5 extended `Validate()` to reject null / `System` owner_type.
**Reviewer: confirm** the extended rule matches the original controller's owner_type handling, and
note that P2's `ContractsValidationAttributesTests` had a coverage hole (the gap existed but wasn't
caught until P5's done_when exercised it).

### P5-D3 — `[RegularExpression(@"^[a-zA-Z0-9_-]+$")]` added to `CreateUserRequest.Username` (P2 GAP fix, AdminContracts.cs)
The original admin `CreateUser` validated `^[a-zA-Z0-9_-]+$` (allows UPPERCASE + underscore — a
DIFFERENT grammar from the lowercase-only `[ScopeGrammar]` used for self-`Register`). P2 added only
`[Required]`+`[StringLength(64)]`, dropping the char-set check; P5 restored it. **Reviewer: confirm**
the admin-vs-register username dual-grammar is intentional/pre-existing (it is — preserved, not
introduced), and that using a bare `[RegularExpression]` here (not `[ScopeGrammar]`) is correct.
Note: this is NOT subject to P5-D1's ordering issue — admin usernames were never lowercased.

### P5-D4 — `ScopesController` inline `IsValidScopeName()` now DEAD CODE (kept, flagged)
`[ScopeGrammar]` on `ClaimScopeRequest.Scope` covers the grammar; the inline check is unreachable
for failures. Implementer kept it (done_when didn't list it for deletion) and flagged it. **Note the
interaction with P5-D1:** the inline check runs POST-normalization, `[ScopeGrammar]` runs PRE — so
they are NOT equivalent for mixed-case/whitespace input (see P5-D1). If the team chooses (B) preserve,
this inline check may become the normalization safety net; if (A) accept, delete it as dead code.

### P5-D5 — OpenAPI snapshot re-baselined twice (migration + the new `[RegularExpression]`). Routine.
