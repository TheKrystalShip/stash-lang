# Implementer deviations — for the reviewer to adjudicate

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
